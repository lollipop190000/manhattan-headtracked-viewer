using System.Collections;
using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class ViewerCompositionTests
{
    private sealed class AbsoluteTestInput : IMotionInputSource
    {
        public MotionCommand Sample(float deltaTime) => new MotionCommand { hasAbsolutePose = true, cityPosition = new Vector3(10, 0, 0), cityRotation = Quaternion.identity };
    }
    private sealed class LimitTranslation : ITranslationConstraint
    {
        public Vector3 Constrain(Vector3 currentCityPosition, Vector3 requestedCityPosition) => new Vector3(Mathf.Min(2, requestedCityPosition.x), requestedCityPosition.y, requestedCityPosition.z);
    }

    [UnityTest]
    public IEnumerator CollisionBeyondRequestedEndpointNeverExtendsMotion()
    {
        var host = new GameObject("collision endpoint constraint"); var origin = host.AddComponent<FloatingOrigin>();
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube); obstacle.layer = 30;
        obstacle.transform.position = new Vector3(0, 0, 1.6f); obstacle.transform.localScale = new Vector3(2, 2, .2f);
        yield return null;
        var constraint = new CityCollisionConstraint(origin) { Radius = .3f };
        Vector3 requested = Vector3.forward;
        Vector3 actual = constraint.Constrain(Vector3.zero, requested);
        Assert.That(actual.z, Is.EqualTo(requested.z).Within(.0001), "The extra probe radius may detect a wall beyond the endpoint but may never extend the requested movement.");
        Object.Destroy(obstacle); Object.Destroy(host); yield return null;
    }

    [UnityTest]
    public IEnumerator PhysicalDistanceIsAppliedBeforeFirstFrozenView()
    {
        var rig = new GameObject("calibrated frozen startup"); rig.transform.position = new Vector3(10, 2, 20);
        var plane = new GameObject("screen").transform; plane.SetParent(rig.transform, false); plane.localPosition = Vector3.forward * .8f;
        var cameraObject = new GameObject("camera"); cameraObject.SetActive(false); cameraObject.transform.SetParent(rig.transform, false);
        cameraObject.AddComponent<Camera>().enabled = false;
        var display = cameraObject.AddComponent<HeadTrackedDisplay>(); display.Calibration.referenceEyeDistanceFromScreen = .8f;
        display.Configure(plane, null); display.TrackingEnabled = false; cameraObject.SetActive(true);
        yield return null;
        Assert.That(display.EyePositionMeters.z, Is.EqualTo(-.8f).Within(.0001));
        Assert.That(Vector3.Distance(cameraObject.transform.position, rig.transform.position), Is.LessThan(.0001));
        Object.Destroy(rig); yield return null;
    }

    [UnityTest]
    public IEnumerator AbsoluteMotionInputHonorsGameTranslationConstraint()
    {
        var rig = new GameObject("absolute motion constraint");
        var flight = rig.AddComponent<FreeFlightController>(); flight.InputSource = new AbsoluteTestInput(); flight.TranslationConstraint = new LimitTranslation();
        yield return null;
        Assert.That(flight.CityPosition.x, Is.EqualTo(2).Within(.0001));
        Object.Destroy(rig); yield return null;
    }

    [UnityTest]
    public IEnumerator ReapplyingSelectedAAKeepsTemporalHistoryAndPipelineAsset()
    {
        var host = new GameObject("AA lifecycle"); var camera = host.AddComponent<Camera>(); camera.enabled = false;
        var profile = host.AddComponent<AfternoonRenderProfile>();
        RenderPipelineAsset original = QualitySettings.renderPipeline;
        profile.Configure(camera);
        var data = camera.GetUniversalAdditionalCameraData(); data.taaSettings = default;
        Assert.That(data.resetHistory, Is.False);
        profile.SetTemporalAA(true); Assert.That(data.resetHistory, Is.False);
        profile.SetTemporalAA(false); Assert.That(data.resetHistory, Is.True);
        Object.Destroy(host); yield return null;
        Assert.That(QualitySettings.renderPipeline, Is.SameAs(original));
    }

    [UnityTest]
    public IEnumerator OpaqueDistanceFadeChangesRenderedCoverage()
    {
        if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Rendered dither verification requires a live GPU Game View; pure lifecycle tests still run headlessly.");
        var host = new GameObject("rendered fade"); var surfaces = host.AddComponent<SurfaceDetailComparison>();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(host.transform); cube.layer = 29;
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetColor("_BaseColor", Color.white);
        source.SetColor("_EmissionColor", Color.white); source.EnableKeyword("_EMISSION");
        cube.GetComponent<Renderer>().sharedMaterial = source;
        var camera = new GameObject("fade camera").AddComponent<Camera>(); camera.transform.SetParent(host.transform); camera.transform.position = new Vector3(0, 0, -2);
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.cullingMask = 1 << 29;
        camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
        var target = new RenderTexture(64, 64, 24); target.Create(); camera.targetTexture = target;
        var pixels = new Texture2D(64, 64, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            surfaces.Register(cube, true);
            Assert.That(cube.GetComponent<Renderer>().sharedMaterial.shader.name, Is.EqualTo("Manhattan/CityDetailLit"));
            Assert.That(cube.GetComponent<Renderer>().sharedMaterial.HasProperty(SurfaceDetailComparison.DistanceFadeProperty), Is.True);
            surfaces.SetOpacity(cube, 1); yield return new WaitForEndOfFrame();
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
            float visible = pixels.GetPixel(32, 32).grayscale;
            surfaces.SetOpacity(cube, 0); yield return new WaitForEndOfFrame();
            RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
            float hidden = pixels.GetPixel(32, 32).grayscale;
            Assert.That(visible, Is.GreaterThan(.5f)); Assert.That(hidden, Is.LessThan(.05f), "The custom material opacity must affect opaque shader coverage, not only the stored property block.");
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; target.Release();
            Object.Destroy(target); Object.Destroy(pixels); Object.Destroy(source); Object.Destroy(host);
        }
        yield return null;
    }
    [UnityTest]
    public IEnumerator TrackingOffRetainsEyeOffsetWhileRigTranslationContinues()
    {
        var rig = new GameObject("test observer");
        var plane = new GameObject("screen").transform; plane.SetParent(rig.transform); plane.localPosition = Vector3.forward * .6f;
        var cameraObject = new GameObject("test camera"); cameraObject.transform.SetParent(rig.transform);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        var display = cameraObject.AddComponent<HeadTrackedDisplay>(); display.Configure(plane, null);
        yield return null;
        display.TrackingEnabled = false; Vector3 frozen = display.EyePositionMeters;
        rig.transform.SetPositionAndRotation(new Vector3(17, 5, 30), Quaternion.Euler(0, 45, 0));
        yield return null;
        Assert.That(display.EyePositionMeters, Is.EqualTo(frozen));
        Assert.That(Vector3.Distance(camera.transform.position, plane.TransformPoint(frozen)), Is.LessThan(.0001));
        Object.Destroy(rig); yield return null;
    }

    [UnityTest]
    public IEnumerator EightComparisonCombinationsAndRoundTripRetainCityAndImportedMaterial()
    {
        var root = new GameObject("test comparisons");
        var plane = new GameObject("screen").transform; plane.SetParent(root.transform);
        var camera = new GameObject("camera"); camera.transform.SetParent(root.transform); camera.AddComponent<Camera>().enabled = false;
        var display = camera.AddComponent<HeadTrackedDisplay>(); display.Configure(plane, null);
        var frame = root.AddComponent<ScreenWindowFrame>(); frame.Configure(display);
        var surfaces = root.AddComponent<SurfaceDetailComparison>();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform); cube.transform.localPosition = new Vector3(7, 8, 9);
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.SetFloat("_BumpScale", .75f); source.EnableKeyword("_NORMALMAP");
        cube.GetComponent<Renderer>().sharedMaterial = source; surfaces.Register(cube);
        Vector3 position = cube.transform.position; Quaternion rotation = cube.transform.rotation; Vector3 scale = cube.transform.localScale;
        for (int state = 0; state < 8; state++)
        {
            display.TrackingEnabled = (state & 1) != 0; frame.EffectEnabled = (state & 2) != 0; surfaces.EnabledDetails = (state & 4) != 0;
            yield return null;
            Assert.That(display.TrackingEnabled, Is.EqualTo((state & 1) != 0));
            Assert.That(frame.EffectEnabled, Is.EqualTo((state & 2) != 0));
            Assert.That(surfaces.EnabledDetails, Is.EqualTo((state & 4) != 0));
            Assert.That(cube.activeSelf, Is.True); Assert.That(cube.transform.position, Is.EqualTo(position));
            Assert.That(cube.transform.rotation, Is.EqualTo(rotation)); Assert.That(cube.transform.localScale, Is.EqualTo(scale));
            Assert.That(source.GetFloat("_BumpScale"), Is.EqualTo(.75f)); Assert.That(source.IsKeywordEnabled("_NORMALMAP"), Is.True);
        }
        surfaces.EnabledDetails = true;
        Assert.That(cube.GetComponent<Renderer>().sharedMaterial, Is.SameAs(source));
        Object.Destroy(root); Object.Destroy(source); yield return null;
    }

    [UnityTest]
    public IEnumerator NewTileInheritsSurfaceOffAndRestoresOriginal()
    {
        var host = new GameObject("test surfaces"); var surfaces = host.AddComponent<SurfaceDetailComparison>(); surfaces.EnabledDetails = false;
        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit")); source.EnableKeyword("_NORMALMAP");
        var texture = new Texture2D(2, 2); source.SetTexture("_BaseMap", texture); source.SetFloat("_Metallic", .7f);
        var renderer = tile.GetComponent<Renderer>(); renderer.sharedMaterial = source; surfaces.Register(tile);
        Assert.That(renderer.sharedMaterial, Is.Not.SameAs(source)); Assert.That(renderer.sharedMaterial.IsKeywordEnabled("_NORMALMAP"), Is.False);
        Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.Null);
        Assert.That(renderer.sharedMaterial.GetFloat("_Metallic"), Is.Zero);
        Assert.That(source.GetTexture("_BaseMap"), Is.SameAs(texture)); Assert.That(source.GetFloat("_Metallic"), Is.EqualTo(.7f));
        surfaces.EnabledDetails = true; Assert.That(renderer.sharedMaterial, Is.SameAs(source));
        surfaces.Unregister(tile); Assert.That(renderer.sharedMaterial, Is.SameAs(source));
        Assert.That(surfaces.RuntimeVariantCount, Is.Zero);
        Object.Destroy(host); Object.Destroy(tile); Object.Destroy(source); Object.Destroy(texture); yield return null;
    }

    [UnityTest]
    public IEnumerator NearFadeVariantsPreservePbrAlphaAndUvAndRestoreImportedSource()
    {
        var host = new GameObject("near PBR contract"); var surfaces = host.AddComponent<SurfaceDetailComparison>();
        var tile = GameObject.CreatePrimitive(PrimitiveType.Cube); var renderer = tile.GetComponent<Renderer>();
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit")); var texture = new Texture2D(2, 2);
        source.SetTexture("_BaseMap", texture); source.SetTexture("_BumpMap", texture); source.SetTexture("_MetallicGlossMap", texture);
        source.SetTextureScale("_BaseMap", new Vector2(2.5f, 3)); source.SetTextureOffset("_BaseMap", new Vector2(.2f, .3f));
        source.SetFloat("_Metallic", .7f); source.SetFloat("_Smoothness", .8f); source.SetFloat("_AlphaClip", 1); source.SetFloat("_Cutoff", .37f);
        source.EnableKeyword("_ALPHATEST_ON"); source.EnableKeyword("_NORMALMAP"); source.renderQueue = 2450;
        renderer.sharedMaterial = source; surfaces.Register(tile, true); Material detail = renderer.sharedMaterial;
        Assert.That(detail.shader.name, Is.EqualTo("Manhattan/CityDetailLit")); Assert.That(detail.HasProperty(SurfaceDetailComparison.DistanceFadeProperty), Is.True);
        Assert.That(detail.GetTexture("_BaseMap"), Is.SameAs(texture)); Assert.That(detail.GetTexture("_BumpMap"), Is.SameAs(texture));
        Assert.That(detail.GetTexture("_MetallicGlossMap"), Is.SameAs(texture)); Assert.That(detail.GetFloat("_Metallic"), Is.EqualTo(.7f));
        Assert.That(detail.GetFloat("_Smoothness"), Is.EqualTo(.8f)); Assert.That(detail.GetFloat("_Cutoff"), Is.EqualTo(.37f));
        Assert.That(detail.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(2.5f, 3))); Assert.That(detail.GetTextureOffset("_BaseMap"), Is.EqualTo(new Vector2(.2f, .3f)));
        Assert.That(detail.renderQueue, Is.EqualTo(2450)); Assert.That(detail.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
        surfaces.SetOpacity(tile, .42f); var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
        Assert.That(block.GetFloat(SurfaceDetailComparison.DistanceFadeProperty), Is.EqualTo(.42f));
        surfaces.EnabledDetails = false;
        Assert.That(renderer.sharedMaterial.shader, Is.SameAs(detail.shader)); Assert.That(renderer.sharedMaterial.GetTexture("_BaseMap"), Is.SameAs(texture), "Alpha-masked silhouette must remain during surface comparison.");
        Assert.That(renderer.sharedMaterial.GetTexture("_BumpMap"), Is.Null); Assert.That(renderer.sharedMaterial.GetFloat("_Smoothness"), Is.EqualTo(.15f));
        surfaces.EnabledDetails = true; Assert.That(renderer.sharedMaterial, Is.SameAs(detail));
        surfaces.Unregister(tile); Assert.That(renderer.sharedMaterial, Is.SameAs(source)); Assert.That(surfaces.RuntimeVariantCount, Is.Zero);
        Assert.That(source.shader.name, Is.EqualTo("Universal Render Pipeline/Lit")); Assert.That(source.GetTexture("_BumpMap"), Is.SameAs(texture));
        Assert.That(source.GetFloat("_Smoothness"), Is.EqualTo(.8f)); Assert.That(source.renderQueue, Is.EqualTo(2450));
        Object.Destroy(host); Object.Destroy(tile); Object.Destroy(source); Object.Destroy(texture); yield return null;
        Assert.That(detail == null, Is.True, "Owned Near material must be released after its last tile leaves.");
    }

    [UnityTest]
    public IEnumerator SharedRuntimeMaterialsReleaseOnlyWhenLastTileLeaves()
    {
        var host = new GameObject("shared material lifetime"); var surfaces = host.AddComponent<SurfaceDetailComparison>();
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var first = GameObject.CreatePrimitive(PrimitiveType.Cube); var second = GameObject.CreatePrimitive(PrimitiveType.Cube);
        first.GetComponent<Renderer>().sharedMaterial = source; second.GetComponent<Renderer>().sharedMaterial = source;
        surfaces.Register(first, true); surfaces.Register(second, true);
        Assert.That(surfaces.RuntimeVariantCount, Is.EqualTo(1));
        var sharedVariant = second.GetComponent<Renderer>().sharedMaterial;
        surfaces.Unregister(first); yield return null;
        Assert.That(surfaces.RuntimeVariantCount, Is.EqualTo(1)); Assert.That(sharedVariant, Is.Not.Null);
        surfaces.Unregister(second); yield return null;
        Assert.That(surfaces.RuntimeVariantCount, Is.Zero); Assert.That(sharedVariant == null, Is.True);
        Object.Destroy(host); Object.Destroy(first); Object.Destroy(second); Object.Destroy(source); yield return null;
    }
}
