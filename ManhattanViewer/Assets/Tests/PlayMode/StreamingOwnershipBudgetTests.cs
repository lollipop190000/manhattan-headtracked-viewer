using System;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class StreamingOwnershipBudgetTests
{
    [TestCase(.00001f)]
    [TestCase(1.4f)]
    public void DeferredPreparationPreservesBothFacesAndRestoresFallbackBeforeRelease(float budget)
    {
        var positions = new[] { Vector3.zero, Vector3.right, Vector3.up,
            Vector3.right * 10, Vector3.right * 11, Vector3.right * 10 + Vector3.up };
        var original = new Mesh { vertices = positions, triangles = new[] { 0, 1, 2, 3, 4, 5 } };
        var replacement = new Mesh { vertices = positions, triangles = new[] { 0, 1, 2 } };
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Skyline" };
        material.SetFloat("_Surface", 0); material.SetFloat("_AlphaClip", 0);
        material.SetFloat("_ZWrite", 1); material.SetFloat("_Cull", 2);
        var sky = new GameObject("budget fallback");
        var core = new GameObject("budget Core");
        sky.AddComponent<MeshFilter>().sharedMesh = original;
        sky.AddComponent<MeshRenderer>().sharedMaterial = material;
        core.AddComponent<MeshFilter>().sharedMesh = replacement;
        core.AddComponent<MeshRenderer>().sharedMaterial = material;
        var tiles = new[] {
            new CityTileRecord { id = "sky", layer = CityLayer.Skyline, requiredReplacementCount = 2 },
            new CityTileRecord { id = "core", layer = CityLayer.Core, replaces = new[] { "sky" } }
        };
        using (var ownership = new SkylineGeometryOwnership())
        {
            try
            {
                OwnershipTestData.Configure(ownership, tiles, new[] { sky, core });
                Assert.That(ownership.Register(tiles[0], sky), Is.True);
                Assert.That(ownership.Register(tiles[1], core), Is.True);
                ownership.SetDesired(core, true);
                DrainWithCoverageChecks();
                Assert.That(ownership.MaskedTriangles, Is.EqualTo(1));
                Assert.That(core.activeSelf, Is.True);
                Assert.That(sky.GetComponent<MeshFilter>().sharedMesh.triangles, Is.EqualTo(new[] { 3, 4, 5 }));

                ownership.SetDesired(core, false);
                ownership.Tick(0);
                Assert.That(core.activeSelf, Is.True);
                Assert.That(ownership.CanRelease(core), Is.False, "Exhausted work cannot release the only published owner.");
                DrainWithCoverageChecks();
                Assert.That(core.activeSelf, Is.False);
                Assert.That(ownership.CanRelease(core), Is.True);
                Assert.That(sky.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(original));
                ownership.Unregister(core);
                DrainWithCoverageChecks();
                Assert.That(original.triangles, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
                Assert.That(original.vertices, Is.EqualTo(positions));
                Assert.That(sky.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(material));

                void DrainWithCoverageChecks()
                {
                    int ticks = 0;
                    do
                    {
                        ownership.Tick(budget);
                        Assert.That(sky.activeSelf, Is.True);
                        int[] indices = sky.GetComponent<MeshFilter>().sharedMesh.triangles;
                        bool hasFirst = Array.IndexOf(indices, 0) >= 0;
                        bool hasOther = Array.IndexOf(indices, 3) >= 0;
                        Assert.That(hasOther, Is.True, "The unrelated fallback face must survive every preparation tick.");
                        Assert.That(hasFirst || core.activeSelf, Is.True, "The owned face needs fallback or active Core on every tick.");
                    } while (ownership.Busy && ++ticks < 2000);
                    Assert.That(ownership.Busy, Is.False, "Finite work must converge even when split across tiny ticks.");
                }
            }
            finally
            {
                ownership.Dispose();
                Object.DestroyImmediate(sky); Object.DestroyImmediate(core);
                Object.DestroyImmediate(original); Object.DestroyImmediate(replacement); Object.DestroyImmediate(material);
            }
        }
    }
}
