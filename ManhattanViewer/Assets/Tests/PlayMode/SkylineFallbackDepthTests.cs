using System.Collections;
using System.Collections.Generic;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class SkylineFallbackDepthTests
{
    private readonly List<Object> objects = new List<Object>();
    private readonly SkylineFallbackDepth depth = new SkylineFallbackDepth();
    private readonly CityTileRecord skyline = new CityTileRecord { layer = CityLayer.Skyline };
    private Material Material(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Skyline" };
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0);
        objects.Add(material); return material;
    }
    private GameObject Quad(Material material)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Quad); objects.Add(root);
        root.GetComponent<Renderer>().sharedMaterial = material; root.layer = 29; return root;
    }
    [TearDown] public void Cleanup()
    {
        depth.Dispose();
        foreach (var item in objects) if (item != null) Object.DestroyImmediate(item);
        objects.Clear();
    }

    [Test]
    public void SurfaceRoundTripsAndSharedResidencyRestoreImportedMaterial()
    {
        var source = Material(Color.red); var first = Quad(source); var second = Quad(source);
        var surfaces = first.AddComponent<SurfaceDetailComparison>();
        for (int cycle = 0; cycle < 2; cycle++)
        {
            depth.Register(skyline, first); depth.Register(skyline, second);
            var variant = first.GetComponent<Renderer>().sharedMaterial;
            Assert.That(variant, Is.Not.SameAs(source));
            Assert.That(second.GetComponent<Renderer>().sharedMaterial, Is.SameAs(variant));
            Assert.That(depth.RuntimeVariantCount, Is.EqualTo(1));
            surfaces.Register(first);
            surfaces.EnabledDetails = false;
            Assert.That(first.GetComponent<Renderer>().sharedMaterial.shader.name, Is.EqualTo(SkylineFallbackDepth.ShaderName));
            surfaces.EnabledDetails = true;
            Assert.That(first.GetComponent<Renderer>().sharedMaterial, Is.SameAs(variant));
            surfaces.Unregister(first); depth.Unregister(first);
            Assert.That(first.GetComponent<Renderer>().sharedMaterial, Is.SameAs(source));
            Assert.That(depth.RuntimeVariantCount, Is.EqualTo(1));
            depth.Unregister(second); Assert.That(depth.RuntimeVariantCount, Is.Zero);
        }
        Assert.That(source.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
        Assert.That(source.GetColor("_BaseColor"), Is.EqualTo(Color.red));
    }

    [TestCase("core")]
    [TestCase("textured")]
    [TestCase("transparent")]
    [TestCase("cutout")]
    [TestCase("overlay")]
    [TestCase("other-name")]
    public void UnsupportedMaterialOrLayerRemainsUntouched(string kind)
    {
        var source = Material(Color.red); var root = Quad(source);
        var tile = new CityTileRecord { layer = kind == "core" ? CityLayer.Core : CityLayer.Skyline };
        if (kind == "textured") source.SetTexture("_BaseMap", Texture2D.whiteTexture);
        if (kind == "transparent") source.SetFloat("_Surface", 1);
        if (kind == "cutout") source.SetFloat("_AlphaClip", 1);
        if (kind == "other-name") source.name = "intentional facade";
        if (kind == "overlay") root.GetComponent<Renderer>().sharedMaterials = new[] { source, source };
        depth.Register(tile, root);
        Assert.That(root.GetComponent<Renderer>().sharedMaterial, Is.SameAs(source));
        Assert.That(depth.RuntimeVariantCount, Is.Zero);
    }

    [UnityTest]
    public IEnumerator RoundedRoofHeightRetainsDetailAtGrazingAnglesAndRealOcclusion()
    {
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires the live GPU PlayMode runner");
        var fallback = Quad(Material(Color.red)); var detail = Quad(Material(Color.green));
        fallback.transform.rotation = detail.transform.rotation = Quaternion.Euler(90, 0, 0);
        var cameraObject = new GameObject("grazing roof camera"); objects.Add(cameraObject);
        var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = 1 << 29;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.nearClipPlane = .3f; camera.farClipPlane = 5000; camera.fieldOfView = 35;
        var data = camera.GetUniversalAdditionalCameraData();
        data.antialiasing = AntialiasingMode.None; data.renderPostProcessing = false;
        var lightObject = new GameObject("roof light"); objects.Add(lightObject);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(60, 0, 0);
        light.color = Color.white; light.intensity = 2; light.shadows = LightShadows.None; light.cullingMask = 1 << 29;
        var target = new RenderTexture(192, 192, 32); target.Create(); objects.Add(target); camera.targetTexture = target;
        var pixels = new Texture2D(192, 192, TextureFormat.RGB24, false); objects.Add(pixels);
        var surfaces = fallback.AddComponent<SurfaceDetailComparison>();
        depth.Register(skyline, fallback); surfaces.Register(fallback); surfaces.Register(detail);
        var previous = RenderTexture.active;
        try
        {
            foreach (float distance in new[] { 50f, 1250f, 4000f })
            foreach (float angle in new[] { 5f, 10f, 30f })
            {
                float size = distance * .2f;
                // Measured bundled roof: 188.519m hull vs 188.5188m Core.
                fallback.transform.position = new Vector3(0, 188.519f, 0);
                detail.transform.position = new Vector3(-size * .25f, 188.5188f, 0);
                fallback.transform.localScale = new Vector3(size, size, 1);
                detail.transform.localScale = new Vector3(size * .5f, size, 1);
                var center = new Vector3(0, 188.5188f, 0);
                var offset = Quaternion.Euler(angle, 0, 0) * Vector3.back * distance;
                camera.transform.SetPositionAndRotation(center + offset, Quaternion.LookRotation(-offset));
                foreach (bool enabled in new[] { true, false, true })
                {
                    surfaces.EnabledDetails = enabled;
                    for (int frame = 0; frame < 4; frame++)
                    {
                        camera.transform.position = center + offset + Vector3.right * (frame % 2 == 0 ? -.12f : .12f);
                        camera.ResetProjectionMatrix(); var projection = camera.projectionMatrix;
                        projection.m02 += (frame % 2 == 0 ? -.75f : .75f) / 192f;
                        projection.m12 += (frame < 2 ? -.75f : .75f) / 192f;
                        camera.projectionMatrix = projection;
                        yield return null; // Render a new frame after transform/projection changes.
                        yield return new WaitForEndOfFrame();
                        RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 192, 192), 0, 0); pixels.Apply();
                        var sample = camera.WorldToViewportPoint(center + Vector3.left * size * .25f);
                        var covered = pixels.GetPixel((int)(sample.x * 192), (int)(sample.y * 192));
                        Assert.That(covered.g, Is.GreaterThan(covered.r + .2f), $"Rounded roof at {distance}m / {angle} degrees / Surface {enabled} / jitter {frame}");
                        sample = camera.WorldToViewportPoint(center + Vector3.right * size * .25f);
                        var uncovered = pixels.GetPixel((int)(sample.x * 192), (int)(sample.y * 192));
                        Assert.That(uncovered.r, Is.GreaterThan(uncovered.g + .2f), "Partial fallback must remain visible");
                    }
                }
                fallback.transform.position += Vector3.up * .02f;
                yield return null;
                yield return new WaitForEndOfFrame();
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 192, 192), 0, 0); pixels.Apply();
                var foregroundSample = camera.WorldToViewportPoint(center + Vector3.left * size * .25f);
                var foreground = pixels.GetPixel((int)(foregroundSample.x * 192), (int)(foregroundSample.y * 192));
                Assert.That(foreground.r, Is.GreaterThan(foreground.g + .2f), $"A real 2cm foreground roof must still occlude detail at {distance}m / {angle} degrees");
            }
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            surfaces.Unregister(detail); surfaces.Unregister(fallback); depth.Unregister(fallback);
        }
    }

    [UnityTest]
    public IEnumerator PartialCoplanarWallKeepsDetailAndUncoveredFallbackAcrossSurfaceRoundTrips()
    {
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("Requires the live GPU PlayMode runner");
        var red = Material(Color.red); var green = Material(Color.green);
        var fallback = Quad(red); var detail = Quad(green);
        fallback.transform.localScale = new Vector3(4, 4, 1);
        detail.transform.localScale = new Vector3(2, 4, 1);
        var cameraObject = new GameObject("depth priority camera"); objects.Add(cameraObject);
        var camera = cameraObject.AddComponent<Camera>(); camera.cullingMask = 1 << 29;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.nearClipPlane = .3f; camera.farClipPlane = 5000; camera.fieldOfView = 45;
        camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var lightObject = new GameObject("depth priority light"); objects.Add(lightObject);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
        light.color = Color.white; light.intensity = 2; light.shadows = LightShadows.None; light.cullingMask = 1 << 29;
        var target = new RenderTexture(96, 96, 32); target.Create(); objects.Add(target); camera.targetTexture = target;
        var pixels = new Texture2D(96, 96, TextureFormat.RGB24, false); objects.Add(pixels);
        var surfaces = fallback.AddComponent<SurfaceDetailComparison>();
        depth.Register(skyline, fallback); surfaces.Register(fallback); surfaces.Register(detail);
        var previous = RenderTexture.active;
        try
        {
            foreach (float distance in new[] { 10f, 1000f, 4000f })
            {
                // Keep the same image footprint at realistic aerial depths.
                float scale = distance / 10f;
                fallback.transform.position = new Vector3(0, 0, distance);
                fallback.transform.localScale = new Vector3(4, 4, 1) * scale;
                detail.transform.position = new Vector3(-scale, 0, distance);
                detail.transform.localScale = new Vector3(2, 4, 1) * scale;
                foreach (bool enabled in new[] { true, false, true })
                {
                    surfaces.EnabledDetails = enabled;
                    yield return new WaitForEndOfFrame();
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 96, 96), 0, 0); pixels.Apply();
                    var covered = pixels.GetPixel(36, 48); var uncovered = pixels.GetPixel(60, 48);
                    Assert.That(covered.g, Is.GreaterThan(covered.r + .2f), "Detailed half must win the depth tie at " + distance);
                    Assert.That(uncovered.r, Is.GreaterThan(uncovered.g + .2f), "Uncovered hull must survive at " + distance);
                }
                // A genuinely separated surface must retain ordinary occlusion.
                detail.transform.position += Vector3.forward * .02f;
                yield return new WaitForEndOfFrame();
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 96, 96), 0, 0); pixels.Apply();
                var front = pixels.GetPixel(36, 48);
                Assert.That(front.r, Is.GreaterThan(front.g + .2f), "A 2cm foreground hull must not be erased at " + distance);
            }
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            surfaces.Unregister(detail); surfaces.Unregister(fallback); depth.Unregister(fallback);
        }
    }
}
