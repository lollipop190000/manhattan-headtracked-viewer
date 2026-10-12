using System;
using System.Collections.Generic;
using System.Linq;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class OfflineOwnershipToleranceTests
{
    private readonly List<Object> objects = new List<Object>();
    private Material material;

    [SetUp]
    public void SetUp()
    {
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Skyline" };
        material.SetFloat("_Surface", 0); material.SetFloat("_AlphaClip", 0);
        material.SetFloat("_Cull", 2); material.SetFloat("_ZWrite", 1);
        objects.Add(material);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    private GameObject Root(string name, Vector3[] cityVertices, int[] indices, Vector3 cityOrigin = default)
    {
        var root = new GameObject(name); objects.Add(root);
        var mesh = new Mesh { name = name + " imported mesh", vertices = cityVertices.Select(v => v - cityOrigin).ToArray(), triangles = indices };
        objects.Add(mesh); root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        return root;
    }

    private static CityTileRecord Sky(string id = "sky", Vector3 origin = default) =>
        new CityTileRecord { id = id, address = id, layer = CityLayer.Skyline, cityOrigin = origin, requiredReplacementCount = 9 };

    private static CityTileRecord Core(string id = "core", string replaces = "sky", Vector3 origin = default) =>
        new CityTileRecord { id = id, address = id, layer = CityLayer.Core, cityOrigin = origin, replaces = new[] { replaces } };

    private static SkylineOwnershipGroup Compile(CityTileRecord skyTile, GameObject sky, CityTileRecord coreTile, GameObject core)
    {
        var originalSky = sky.GetComponent<MeshFilter>().sharedMesh;
        var originalCore = core.GetComponent<MeshFilter>().sharedMesh;
        var skyVertices = originalSky.vertices; var coreVertices = originalCore.vertices;
        var skyIndices = originalSky.triangles; var coreIndices = originalCore.triangles;
        var skyMaterials = sky.GetComponent<MeshRenderer>().sharedMaterials;
        var coreMaterials = core.GetComponent<MeshRenderer>().sharedMaterials;
        SkylineOwnershipGroup group;
        using (var compiler = new OfflineOwnershipGroupCompiler())
        {
            Assert.That(compiler.Register(skyTile, sky), Is.True);
            Assert.That(compiler.Register(coreTile, core), Is.True);
            // The offline correspondence includes eligible providers independently of
            // desired runtime visibility. Keep this EditMode fixture free of native variants.
            compiler.SetDesired(core, false);
            compiler.Tick(.00001f);
            for (int i = 0; i < 10000 && compiler.Busy; i++) compiler.Tick(.01f);
            Assert.That(compiler.Busy, Is.False, "Resumable correspondence must finish.");
            Assert.That(compiler.UnsupportedMeshes, Is.Zero);
            group = compiler.ExportGroup(skyTile.id);
        }
        Assert.That(sky.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(originalSky));
        Assert.That(core.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(originalCore));
        Assert.That(originalSky.vertices, Is.EqualTo(skyVertices)); Assert.That(originalCore.vertices, Is.EqualTo(coreVertices));
        Assert.That(originalSky.triangles, Is.EqualTo(skyIndices)); Assert.That(originalCore.triangles, Is.EqualTo(coreIndices));
        Assert.That(sky.GetComponent<MeshRenderer>().sharedMaterials, Is.EqualTo(skyMaterials));
        Assert.That(core.GetComponent<MeshRenderer>().sharedMaterials, Is.EqualTo(coreMaterials));
        return group;
    }

    private static int[] Ordinals(SkylineOwnershipGroup group, string coreId) =>
        group.parts.SelectMany(p => p.owners).Where(o => o.coreId == coreId).SelectMany(o => o.ordinals).OrderBy(i => i).ToArray();

    [Test]
    public void MeasuredWallMatchesAcrossMillimetreRoundingBoundaryWithoutClaimingOtherFallback()
    {
        // Exact native source vertices: Skyline_1_6_0 ordinal 7081 and
        // Core_Manhattan_Core_2_6_17 ordinal 100. A/B differ by 0.576 mm.
        var skyVertices = new[] {
            new Vector3(1018.458984375f, .22f, 3234.323974609375f),
            new Vector3(1018.458984375f, 182.8800048828125f, 3234.323974609375f),
            new Vector3(989.177978515625f, 182.8800048828125f, 3250.080078125f),
            new Vector3(950, .22f, 3200), new Vector3(950, 50, 3200), new Vector3(930, 50, 3210)
        };
        var coreVertices = new[] {
            new Vector3(1018.4586791992188f, .22f, 3234.323486328125f),
            new Vector3(1018.4586791992188f, 182.8800048828125f, 3234.323486328125f),
            new Vector3(989.17822265625f, 182.8800048828125f, 3250.080078125f)
        };
        var skyOrigin = new Vector3(500, 0, 3000); var coreOrigin = new Vector3(1000, 0, 3000);
        var group = Compile(Sky(origin: skyOrigin), Root("measured fallback", skyVertices, new[] { 0, 1, 2, 3, 4, 5 }, skyOrigin),
            Core(origin: coreOrigin), Root("measured textured provider", coreVertices, new[] { 0, 1, 2 }, coreOrigin));
        Assert.That(Ordinals(group, "core"), Is.EqualTo(new[] { 0 }), "Only the demonstrated wall can be claimed; another fallback remains unowned.");
    }

    [TestCase(.00049f, false)]
    [TestCase(.00049f, true)]
    [TestCase(.00099f, false)]
    public void QuantizationAndCentroidBoundaryCrossingsPreserveCyclicWinding(float offset, bool cyclic)
    {
        var skyVertices = new[] { new Vector3(offset, 0, 0), new Vector3(offset, 1, 0), new Vector3(offset, 0, 1) };
        var coreVertices = skyVertices.Select(v => v + Vector3.right * .00002f).ToArray();
        var group = Compile(Sky(), Root("fallback", skyVertices, new[] { 0, 1, 2 }), Core(),
            Root("close provider", coreVertices, cyclic ? new[] { 1, 2, 0 } : new[] { 0, 1, 2 }));
        Assert.That(Ordinals(group, "core"), Is.EqualTo(new[] { 0 }));
    }

    [TestCase("reversed")]
    [TestCase("beyondNeighbourBucket")]
    [TestCase("beyondDistanceInsideSameQuantizedBucket")]
    public void ReverseWindingOrVertexDistanceOverOneMillimetreCannotClaimFallback(string kind)
    {
        var triangle = new[] { Vector3.zero, Vector3.right, Vector3.up };
        var skyVertices = triangle; var coreVertices = triangle; var indices = new[] { 0, 1, 2 };
        if (kind == "reversed") indices = new[] { 0, 2, 1 };
        else if (kind == "beyondNeighbourBucket") coreVertices = triangle.Select(v => v + Vector3.forward * .0012f).ToArray();
        else
        {
            // Both triangles round into identical millimetre keys; their true
            // diagonal vertex separation is 1.697 mm and must still be rejected.
            skyVertices = triangle.Select(v => v + Vector3.one * .00049f).ToArray();
            coreVertices = triangle.Select(v => v - Vector3.one * .00049f).ToArray();
        }
        var group = Compile(Sky(), Root("fallback", skyVertices, new[] { 0, 1, 2 }), Core(), Root("noncovering provider", coreVertices, indices));
        Assert.That(group.parts, Is.Empty);
    }

    [Test]
    public void PartialCohortMapsCoveredRetriangulationWithoutClaimingAdjacentFallback()
    {
        var skyVertices = new[] { Vector3.zero, Vector3.right, Vector3.one-Vector3.forward,
            new Vector3(2,0,0),new Vector3(3,0,0),new Vector3(3,1,0) };
        var coreVertices = new[] { Vector3.zero,Vector3.right,Vector3.up,new Vector3(1,1,0) };
        var group = Compile(Sky(),Root("retessellated fallback",skyVertices,new[]{0,1,2,3,4,5}),
            Core(),Root("covering quad",coreVertices,new[]{0,1,2,1,3,2}));
        Assert.That(Ordinals(group,"core"),Is.EqualTo(new[]{0}));
    }

    [Test]
    public void GeometricCoincidenceCannotInventAnUnrelatedCatalogReplacement()
    {
        var triangle = new[] { Vector3.zero, Vector3.right, Vector3.up };
        var group = Compile(Sky(), Root("unrelated fallback", triangle, new[] { 0, 1, 2 }),
            Core(replaces: "different skyline"), Root("unrelated provider", triangle, new[] { 0, 1, 2 }));
        Assert.That(group.parts, Is.Empty);
    }
}
