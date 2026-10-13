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
    private sealed class RelativeTestInput : IMotionInputSource
    {
        public Vector3 movement;
        public MotionCommand Sample(float deltaTime) => new MotionCommand { localMovement = movement };
    }

    [UnityTest]
    public IEnumerator RelativeWasdKeepsGroundHeightThroughPitchAndOriginRebase()
    {
        var root = new GameObject("planar observer regression");
        var rig = new GameObject("observer"); rig.transform.SetParent(root.transform);
        var origin = root.AddComponent<FloatingOrigin>(); origin.observer = rig.transform;
        var flight = rig.AddComponent<FreeFlightController>(); flight.Origin = origin; flight.Speed = 100;
        var camera = new GameObject("head camera child"); camera.transform.SetParent(rig.transform);
        camera.transform.localPosition = new Vector3(.1f, .2f, .3f);
        Vector3 localCameraPosition = camera.transform.localPosition;
        flight.SetPose(new Vector3(600, 27, 20), Quaternion.Euler(85, 37, 0));
        flight.InputSource = new RelativeTestInput { movement = Vector3.ClampMagnitude(new Vector3(1, 0, 1), 1) };
        yield return null;
        Assert.That(flight.CityPosition.y, Is.EqualTo(27f).Within(.0001f));
        Assert.That(flight.Velocity.y, Is.EqualTo(0f).Within(.0001f));
        Assert.That(flight.Velocity.magnitude, Is.EqualTo(100f).Within(.15f));
        Assert.That(origin.Offset.sqrMagnitude, Is.GreaterThan(0));
        Assert.That(camera.transform.localPosition, Is.EqualTo(localCameraPosition));
        flight.InputSource = new RelativeTestInput { movement = Vector3.up };
        yield return null;
        Assert.That(flight.CityPosition.y, Is.GreaterThan(27f));
        Assert.That(flight.Velocity.x, Is.EqualTo(0f).Within(.0001f));
        Assert.That(flight.Velocity.z, Is.EqualTo(0f).Within(.0001f));
        Assert.That(flight.Velocity.y, Is.EqualTo(100f).Within(.1f));
        Object.Destroy(root); yield return null;
    }

    [UnityTest]
    public IEnumerator AbsoluteBenchmarkRouteStillMovesVerticallyAtRequestedSpeed()
    {
        var rig = new GameObject("absolute airborne route regression");
        var flight = rig.AddComponent<FreeFlightController>();
        Vector3 initial = new Vector3(100, 400, 100);
        flight.SetPose(initial, Quaternion.identity);
        flight.InputSource = new BenchmarkRouteInput(new[] { initial, initial + new Vector3(300, 200, 400) }, 100);
        yield return null;
        Assert.That(flight.CityPosition.y, Is.GreaterThan(initial.y));
        Assert.That(flight.Velocity.y, Is.GreaterThan(0));
        Assert.That(flight.Velocity.magnitude, Is.EqualTo(100f).Within(.2f));
        Object.Destroy(rig); yield return null;
    }

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
    public IEnumerator CachedOpacityRetainsEndpointsAcrossSurfaceSwitchesAndFreshBindings()
    {
        var host = new GameObject("cached opacity lifecycle"); var surfaces = host.AddComponent<SurfaceDetailComparison>();
        var first = GameObject.CreatePrimitive(PrimitiveType.Cube); first.transform.SetParent(host.transform);
        var second = GameObject.CreatePrimitive(PrimitiveType.Cube); second.transform.SetParent(host.transform);
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var firstRenderer = first.GetComponent<Renderer>(); var secondRenderer = second.GetComponent<Renderer>();
        firstRenderer.sharedMaterial = secondRenderer.sharedMaterial = source;
        var block = new MaterialPropertyBlock(); int unrelated = Shader.PropertyToID("_UnrelatedOpacityTest");
        block.SetFloat(unrelated, 7); firstRenderer.SetPropertyBlock(block);
        surfaces.Register(first, true); surfaces.SetOpacity(first, 0); surfaces.SetOpacity(first, -1);
        surfaces.EnabledDetails = false; surfaces.SetOpacity(first, 0);
        firstRenderer.GetPropertyBlock(block);
        Assert.That(block.GetFloat(SurfaceDetailComparison.DistanceFadeProperty), Is.Zero);
        Assert.That(block.GetFloat(unrelated), Is.EqualTo(7), "Updating opacity must preserve other per-renderer data.");
        block.Clear(); block.SetFloat(SurfaceDetailComparison.DistanceFadeProperty, .25f); secondRenderer.SetPropertyBlock(block);
        surfaces.Register(second, true); surfaces.SetOpacity(second, 2); secondRenderer.GetPropertyBlock(block);
        Assert.That(block.GetFloat(SurfaceDetailComparison.DistanceFadeProperty), Is.EqualTo(1), "A new binding must initialize full coverage even while the shared surface variant is off.");
        Assert.That(secondRenderer.sharedMaterial.shader.name, Is.EqualTo("Manhattan/CityDetailLit"));
        surfaces.EnabledDetails = true; surfaces.SetOpacity(first, 0); firstRenderer.GetPropertyBlock(block);
        Assert.That(block.GetFloat(SurfaceDetailComparison.DistanceFadeProperty), Is.Zero, "Changing the material variant must retain the cached zero-coverage property block.");
        surfaces.SetOpacity(first, 1); surfaces.SetOpacity(first, 1); firstRenderer.GetPropertyBlock(block);
        Assert.That(block.GetFloat(SurfaceDetailComparison.DistanceFadeProperty), Is.EqualTo(1));
        Assert.That(block.GetFloat(unrelated), Is.EqualTo(7));
        surfaces.SetOpacity(first, 0); surfaces.Unregister(first); surfaces.Register(first, true); firstRenderer.GetPropertyBlock(block);
        Assert.That(block.GetFloat(SurfaceDetailComparison.DistanceFadeProperty), Is.EqualTo(1), "A fresh binding must not inherit the old binding's cached zero.");
        surfaces.Unregister(first); surfaces.Unregister(second);
        Assert.That(surfaces.RuntimeVariantCount, Is.Zero);
        Assert.That(firstRenderer.sharedMaterial, Is.SameAs(source)); Assert.That(secondRenderer.sharedMaterial, Is.SameAs(source));
        Object.Destroy(host); Object.Destroy(source); yield return null;
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

public sealed class NearRuntimeDiagnosticsTests
{
    [Test]
    public void SnapshotReadsInstantiatedNearActivityBoundsAndRealPropertyBlocksWithoutMutation()
    {
        var host = new GameObject("near diagnostic fixture");
        var streamer = host.AddComponent<CityStreamer>(); streamer.enabled = false;
        var surfaces = host.AddComponent<SurfaceDetailComparison>(); streamer.surfaces = surfaces;
        var observer = new GameObject("observer"); observer.transform.SetParent(host.transform);
        var flight = observer.AddComponent<FreeFlightController>(); flight.enabled = false; streamer.flight = flight;
        var catalog = ScriptableObject.CreateInstance<CityCatalog>(); catalog.sourceVersion = "test-v2"; streamer.catalog = catalog;
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var fixtures = new System.Collections.Generic.List<GameObject>();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var entries = (IList)typeof(CityStreamer).GetField("residents", flags).GetValue(streamer);
        var type = typeof(CityStreamer).GetNestedType("Residency", System.Reflection.BindingFlags.NonPublic);
        try
        {
            Add("active-partial", CityLayer.Near, 100, true, .25f);
            Add("inactive-nearest", CityLayer.Near, 50, false, 1f);
            Add("active-zero", CityLayer.Near, 180, true, 0f);
            Add("core-not-near", CityLayer.Core, 0, true, 1f);
            long writes = surfaces.OpacityPropertyWrites, visits = surfaces.OpacityTileVisits;
            var state = streamer.GetNearDiagnostics();
            Assert.That(state.sourceVersion, Is.EqualTo("test-v2"));
            Assert.That(state.loadedTiles, Is.EqualTo(3)); Assert.That(state.activeTiles, Is.EqualTo(2));
            Assert.That(state.nearestLoadedTile, Is.EqualTo("inactive-nearest"));
            Assert.That(state.nearestLoadedBoundsDistance, Is.EqualTo(50));
            Assert.That(state.activeRenderers, Is.EqualTo(2)); Assert.That(state.opacityAvailable, Is.True);
            Assert.That(state.minimumOpacity, Is.EqualTo(0)); Assert.That(state.maximumOpacity, Is.EqualTo(.25f));
            Assert.That(state.visibleRendererFlags, Is.InRange(0, state.activeRenderers));
            Assert.That(surfaces.OpacityPropertyWrites, Is.EqualTo(writes));
            Assert.That(surfaces.OpacityTileVisits, Is.EqualTo(visits));
            for (int i = 0; i < 100; i++) streamer.GetNearDiagnostics();
            long allocatedBefore = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) streamer.GetNearDiagnostics();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Assert.That(allocated, Is.EqualTo(0L), "Repeated snapshot queries must not allocate or create material variants.");
            flight.SetPose(Vector3.right * 40, Quaternion.identity);
            Assert.That(streamer.GetNearDiagnostics().nearestLoadedBoundsDistance, Is.EqualTo(10));
            surfaces.EnabledDetails = false;
            state = streamer.GetNearDiagnostics(); Assert.That(state.surfaceDetailsEnabled, Is.False);
            Assert.That(state.loadedTiles, Is.EqualTo(3)); Assert.That(state.activeTiles, Is.EqualTo(2));
            Assert.That(state.minimumOpacity, Is.EqualTo(0)); Assert.That(state.maximumOpacity, Is.EqualTo(.25f));
            streamer.flight = null;
            Assert.That(streamer.GetNearDiagnostics().nearestLoadedTile, Is.Null);
        }
        finally
        {
            // Fixture residents have no Addressables handles; clear them before component teardown.
            entries.Clear();
            foreach (var fixture in fixtures) surfaces.Unregister(fixture);
            UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(source); UnityEngine.Object.DestroyImmediate(catalog);
        }
        void Add(string id, CityLayer layer, float x, bool active, float opacity)
        {
            var fixture = GameObject.CreatePrimitive(PrimitiveType.Cube); fixture.transform.SetParent(host.transform);
            fixture.GetComponent<Renderer>().sharedMaterial = source; fixtures.Add(fixture);
            surfaces.Register(fixture, layer == CityLayer.Near);
            if (layer == CityLayer.Near) surfaces.SetOpacity(fixture, opacity);
            fixture.SetActive(active);
            var entry = System.Activator.CreateInstance(type, true);
            type.GetField("tile").SetValue(entry, new CityTileRecord { id = id, layer = layer, cityBounds = new Bounds(Vector3.right * x, Vector3.zero) });
            type.GetField("instance").SetValue(entry, fixture);
            entries.Add(entry);
        }
    }
}

// Oracle is the evaluation policy at 78bd313. Compare production decisions and exact
// requested priorities, including the original List.Sort behavior for equal priorities.
