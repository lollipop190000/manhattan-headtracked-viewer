using System;
using System.IO;
using System.Linq;
using Manhattan.Editor;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class CityBakeTests
{
    [Test]
    public void LandmarkFallbacksKeepOrdinarySkylineBuildingMetadataAndCollisionProxies()
    {
        CityBake.ValidateLandmarkFallbacks();
    }

    [TestCase("Landmark Empire State / Facade / limestone", "Empire State")]
    [TestCase("Landmark Chrysler / Roof / weathered charcoal", "Chrysler")]
    [TestCase("Landmark One WTC / Rooftop / machinery", "One WTC")]
    [TestCase("Landmark One Vanderbilt / Facade / glass", "One Vanderbilt")]
    [TestCase("Landmark 432 Park / Facade / pale_stone", "432 Park")]
    [TestCase("Landmark 111 West 57th / Facade / pale_stone", "111 West 57th")]
    [TestCase("Tile -03_-08 / Facade / limestone", null)]
    [TestCase("Landmark Empire State impostor / Facade", null)]
    public void LandmarkSourceNamesAreExact(string sourceName,string expected)
    {
        Assert.That(CityBake.LandmarkNameForSourceObject(sourceName),Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NonReadableTextureCloneKeepsExactPixelsMipsAndSrgbFormatAfterReload(bool linear)
    {
        if(!SystemInfo.supportsAsyncGPUReadback)Assert.Ignore("Requires a graphics device for the GPU-only texture path.");
        string path="Assets/CitySource/TextureReadbackValidation_"+Guid.NewGuid().ToString("N")+".asset";
        var source=new Texture2D(2,2,TextureFormat.RGBA32,true,linear);
        Texture2D clone=null;
        try
        {
            source.SetPixels32(new[]{new Color32(19,41,63,127),new Color32(233,177,111,255),new Color32(64,128,192,201),new Color32(9,7,5,3)});
            source.Apply(true,false);
            byte[] pixels=source.GetRawTextureData<byte>().ToArray();
            var format=source.graphicsFormat;int mips=source.mipmapCount;
            source.Apply(false,true);Assert.That(source.isReadable,Is.False);
            clone=CityBake.CloneNativeTexture(source);
            Assert.That(clone.graphicsFormat,Is.EqualTo(format));Assert.That(clone.mipmapCount,Is.EqualTo(mips));
            Assert.That(clone.GetRawTextureData<byte>().ToArray(),Is.EqualTo(pixels));
            Directory.CreateDirectory("Assets/CitySource");AssetDatabase.CreateAsset(clone,path);AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var reloaded=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(reloaded.graphicsFormat,Is.EqualTo(format));Assert.That(reloaded.GetRawTextureData<byte>().ToArray(),Is.EqualTo(pixels));
        }
        finally
        {
            Object.DestroyImmediate(source);
            if(clone!=null&&!EditorUtility.IsPersistent(clone))Object.DestroyImmediate(clone);
            AssetDatabase.DeleteAsset(path);
        }
    }

    [Test]
    public void BatchedFixtureKeepsNativeMeshMaterialTextureAndColliderReferences()
    {
        CityBake.ValidateFixtureBake();
    }

    [Test]
    public void WholeBuildingFallbackRequiresBothCoreCellsAndMappingIsRepeatable()
    {
        var crossing = Tile("Skyline_0_0", CityLayer.Skyline, new Vector3(500, 5, 250), new Vector3(100, 10, 100), Vector3.zero);
        var far = Tile("Skyline_8_0", CityLayer.Skyline, new Vector3(4250, 5, 250), new Vector3(100, 10, 100), new Vector3(4000, 0, 0));
        var left = Tile("Core_left", CityLayer.Core, new Vector3(250, 5, 250), new Vector3(500, 10, 500), Vector3.zero);
        var right = Tile("Core_right", CityLayer.Core, new Vector3(750, 5, 250), new Vector3(500, 10, 500), new Vector3(500, 0, 0));
        var emptyCell = Tile("Core_no_fallback", CityLayer.Core, new Vector3(2250, 5, 250), new Vector3(500, 10, 500), new Vector3(2000, 0, 0));
        var ground = Tile("Ground_0_0", CityLayer.Ground, new Vector3(250, 0, 250), new Vector3(500, 1, 500), Vector3.zero);
        var farGround = Tile("Skyline_Ground", CityLayer.Skyline, Vector3.zero, Vector3.one * 10000, Vector3.zero);
        var water = Tile("Skyline_Water", CityLayer.Skyline, Vector3.zero, Vector3.one * 60000, Vector3.zero);
        var records = new[] { crossing, far, left, right, emptyCell, ground, farGround, water };
        ground.replaces = new[] { "invalid_old_reference" };
        crossing.requiredReplacementCount = 123;

        for (int roundTrip = 0; roundTrip < 2; roundTrip++)
        {
            CityBake.AssignReplacementCoverage(records);
            Assert.That(crossing.requiredReplacementCount, Is.EqualTo(2));
            Assert.That(left.replaces, Is.EqualTo(new[] { crossing.id }));
            Assert.That(right.replaces, Is.EqualTo(new[] { crossing.id }));
            Assert.That(emptyCell.replaces, Is.Empty);
            Assert.That(ground.replaces, Is.Empty);
            Assert.That(far.requiredReplacementCount, Is.Zero);
            Assert.That(farGround.requiredReplacementCount, Is.Zero);
            Assert.That(water.requiredReplacementCount, Is.Zero);
            Assert.That(records.Where(r => r.layer != CityLayer.Core).SelectMany(r => r.replaces), Is.Empty);
            Assert.That(records.SelectMany(r => r.replaces).All(id => records.Any(r => r.id == id && r.layer == CityLayer.Skyline)), Is.True);
        }
    }

    [Test]
    public void LongTriangleBoundsRequireTheirCoveringCoreCell()
    {
        var fallback = Tile("Skyline_1_0", CityLayer.Skyline, new Vector3(800, 5, 250), new Vector3(50, 10, 50), new Vector3(500, 0, 0));
        var longTriangle = Tile("Core_crossing", CityLayer.Core, new Vector3(450, 5, 250), new Vector3(800, 10, 200), Vector3.zero);
        CityBake.AssignReplacementCoverage(new[] { fallback, longTriangle });
        Assert.That(longTriangle.replaces, Is.EqualTo(new[] { fallback.id }));
        Assert.That(fallback.requiredReplacementCount, Is.EqualTo(1));
    }

    [Test]
    public void ActualGltfImportRetainsDocumentedAxisConversionAndBounds()
    {
        string fixtureRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../assets/fixtures"));
        var manifest = JsonUtility.FromJson<FixtureManifest>(File.ReadAllText(Path.Combine(fixtureRoot, "fixture-manifest.json")));
        string assetPath = "Assets/CitySource/AxisValidationFixture_" + Guid.NewGuid().ToString("N") + ".glb";
        GameObject instance = null;
        try
        {
            Directory.CreateDirectory("Assets/CitySource");
            File.Copy(Path.Combine(fixtureRoot, "NearestNearDetail.glb"), assetPath);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Assert.That(imported, Is.Not.Null, "The installed glTFast importer must import the real validation GLB.");
            instance = (GameObject)PrefabUtility.InstantiatePrefab(imported);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Assert.That(Vector3.Distance(bounds.min, Vector(manifest.unityBounds.min)), Is.LessThan(.005f));
            Assert.That(Vector3.Distance(bounds.max, Vector(manifest.unityBounds.max)), Is.LessThan(.005f));
            long triangles = 0;
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
                for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) triangles += filter.sharedMesh.GetIndexCount(sub) / 3;
            Assert.That(triangles, Is.EqualTo(manifest.triangles));
            Vector3 source = Vector(manifest.initialBlender);
            Assert.That(Vector3.Distance(CityBake.SourceToCity(source.x, source.y, source.z), Vector(manifest.initialUnity)), Is.LessThan(.001f));
        }
        finally
        {
            if (instance != null) Object.DestroyImmediate(instance);
            AssetDatabase.DeleteAsset(assetPath);
        }
    }

    private static CityTileRecord Tile(string id, CityLayer layer, Vector3 center, Vector3 size, Vector3 origin) =>
        new CityTileRecord { id = id, layer = layer, cityBounds = new Bounds(center, size), cityOrigin = origin };
    private static Vector3 Vector(float[] value) => new Vector3(value[0], value[1], value[2]);
    [Serializable] private sealed class FixtureManifest
    {
        public FixtureBounds unityBounds;
        public float[] initialBlender, initialUnity;
        public int triangles;
    }
    [Serializable] private sealed class FixtureBounds { public float[] min, max; }
}
