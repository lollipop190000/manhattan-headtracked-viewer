using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Manhattan.Viewer
{
    [Serializable]
    public sealed class FadeProofSnapshot
    {
        public string rigAndCamera, screen, calibration, city, lighting, pipeline, importedMaterials, ui;
        public bool tracking, window, surface, trackingObservationFresh;
        public int loadedTiles, pendingLoads, streamingFailures, visibleNearRenderers;
        public Vector3 cityPosition, eyeMeters;
        public float requestedMinimum, requestedMaximum;
    }
    [Serializable]
    public sealed class FadeProofImage
    {
        public string name, pngSha256, rgbSha256;
        public int width, height, rgbPixelSamples;
        public float requestedOpacity;
        public bool imageWritten, invariantsMatch;
        public FadeProofSnapshot snapshot;
    }
    [Serializable]
    public sealed class FadeProofDifference
    {
        public string from, to;
        public bool comparable;
        public int pixelSamples, changedPixels, maximumChannelDifference;
        public double meanAbsoluteRgbDifference;
        public RectInt changedPixelBounds;
    }
    [Serializable]
    public sealed class FadeProofRenderer
    {
        public string tile, renderer, shader;
        public int materialSlots;
        public Bounds bounds;
        public bool visible, intersectsMainCameraFrustum, allMaterialsOpaque, allMaterialsHaveCrossFade;
        public Vector4 originalFade;
    }
    [Serializable]
    public sealed class FadeProofReport
    {
        public string reportKind = "measured-standalone-opaque-fade-proof", utcTime, unityVersion, sourceVersion, gpu, error, status, antialiasingBefore;
        public int outputWidth, outputHeight, renderWidth, renderHeight;
        public string fullscreenMode;
        public bool streamingSettled, allInvariantsMatch, preferencesUnchanged, originalStatesRestored, ssaoDisabledForProof, ssaoStatesRestored;
        public int ssaoFeatureCount, ssaoPreviouslyActiveCount;
        public ViewerBuildIdentity buildIdentity;
        public FadeProofRenderer[] renderers;
        public FadeProofImage[] images;
        public FadeProofDifference controlRepeat, fadeZero, restoreRepeat;
        public string limitation = "Only current active opaque Near renderer property blocks change between the four images. No city renderer, GameObject or material is disabled or replaced, and no source asset is saved. UI is hidden, AA is temporarily None, and SSAO renderer features are temporarily inactive for this explicit QA path; their original active states are restored. isVisible/frustum are culling evidence, not proof of unoccluded pixels; unchanged 1/0 pixels are inconclusive. A pass proves these Near draws affected this standalone image, not the complete 120–180m transition or its performance.";
    }

    /// <summary>Explicit standalone GPU proof, separate from comparison-invariant captures and benchmarks.</summary>
    public sealed class RuntimeFadeProofCapture : MonoBehaviour
    {
        private sealed class Target
        {
            public Renderer renderer;
            public MaterialPropertyBlock originalBlock;
            public FadeProofRenderer evidence;
        }
        private sealed class Pixels { public FadeProofImage image; public Color32[] colors; }
        private sealed class FeatureState { public ScriptableRendererFeature feature; public bool active; }
        private ManhattanBootstrap app;
        private bool holding, savedInputEnabled, savedCaptured, savedStreaming, savedTracking;
        private IMotionInputSource savedInput;
        private AntialiasingMode savedAA;
        private Canvas[] canvases;
        private GraphicRaycaster[] raycasters;
        private bool[] canvasStates, raycasterStates;
        private Material[] importedMaterials;
        private readonly List<Target> targets = new List<Target>();
        private readonly List<Pixels> pixels = new List<Pixels>();
        private readonly List<FeatureState> ssaoFeatures = new List<FeatureState>();
        private MaterialPropertyBlock scratch;
        private string originalPreferences, originalPipeline, originalUi;
        private FadeProofReport report;
        private string directory;
        private bool quitWhenDone;

        public void Begin(ManhattanBootstrap viewer, string outputDirectory, bool quit)
        {
            if (holding) return;
            app = viewer; quitWhenDone = quit;
            report = new FadeProofReport { unityVersion = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, sourceVersion = app.catalog != null ? app.catalog.sourceVersion : null };
            if (ManhattanBootstrap.HasArgument("--benchmark") || ManhattanBootstrap.HasArgument("--screenshot-out") || ManhattanBootstrap.HasArgument("--comparison-captures-out"))
            {
                Debug.LogError("--fade-proof-out requires a separate run from other automatic QA/benchmark modes."); if (quit) Application.Quit(1); return;
            }
            try { directory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(directory); }
            catch (Exception error) { Debug.LogError("Cannot prepare fade proof directory: " + error.Message); if (quit) Application.Quit(1); return; }
            savedInput = app.Flight.InputSource; savedInputEnabled = app.KeyboardInput.InputEnabled; savedCaptured = app.KeyboardInput.Captured;
            savedStreaming = app.Streamer.enabled; savedTracking = app.Display.TrackingEnabled;
            var camera = app.Rendering.targetCamera; savedAA = camera.GetUniversalAdditionalCameraData().antialiasing; report.antialiasingBefore = savedAA.ToString();
            originalPreferences = Hash(JsonUtility.ToJson(app.Preferences)); originalPipeline = Pipeline(camera);
            canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.InstanceID); raycasters = FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.InstanceID);
            originalUi = Ui(false); canvasStates = new bool[canvases.Length]; raycasterStates = new bool[raycasters.Length];
            for (int i = 0; i < canvases.Length; i++) { canvasStates[i] = canvases[i].enabled; canvases[i].enabled = false; }
            for (int i = 0; i < raycasters.Length; i++) { raycasterStates[i] = raycasters[i].enabled; raycasters[i].enabled = false; }
            holding = true;
            app.Flight.InputSource = null; app.KeyboardInput.InputEnabled = false; app.KeyboardInput.Capture(false); app.Display.TrackingEnabled = false;
            StartCoroutine(GuardedSeries());
        }

        // Drive nested iterators explicitly so IO/capture failures still restore state and produce a report.
        private IEnumerator GuardedSeries()
        {
            var stack = new Stack<IEnumerator>(); stack.Push(Series());
            try
            {
                while (stack.Count > 0)
                {
                    bool moved = false; object yielded = null;
                    try { moved = stack.Peek().MoveNext(); if (moved) yielded = stack.Peek().Current; }
                    catch (Exception error) { report.error = error.ToString(); report.status = "failed"; break; }
                    if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (yielded is IEnumerator nested) { stack.Push(nested); continue; }
                    yield return yielded;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); Restore(); }
            report.preferencesUnchanged = originalPreferences == Hash(JsonUtility.ToJson(app.Preferences));
            report.originalStatesRestored = app.Display.TrackingEnabled == savedTracking && app.Streamer.enabled == savedStreaming &&
                app.Flight.InputSource == savedInput && app.KeyboardInput.InputEnabled == savedInputEnabled && app.KeyboardInput.Captured == savedCaptured &&
                Pipeline(app.Rendering.targetCamera) == originalPipeline && Ui(false) == originalUi;
            report.ssaoStatesRestored = true;
            foreach (var state in ssaoFeatures) report.ssaoStatesRestored &= state.feature != null && state.feature.isActive == state.active;
            report.originalStatesRestored &= report.ssaoStatesRestored;
            foreach (var target in targets) if (target.renderer != null)
            {
                target.renderer.GetPropertyBlock(scratch);
                report.originalStatesRestored &= scratch.GetVector("unity_LODFade") == target.evidence.originalFade;
            }
            report.utcTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            report.images = pixels.ConvertAll(p => p.image).ToArray(); report.renderers = targets.ConvertAll(t => t.evidence).ToArray();
            report.outputWidth = Screen.width; report.outputHeight = Screen.height;
            report.renderWidth = app.Rendering.targetCamera.scaledPixelWidth; report.renderHeight = app.Rendering.targetCamera.scaledPixelHeight; report.fullscreenMode = Screen.fullScreenMode.ToString();
            try
            {
                var identity = Resources.Load<TextAsset>("ViewerBuildIdentity"); if (identity != null) report.buildIdentity = JsonUtility.FromJson<ViewerBuildIdentity>(identity.text);
                if (!report.preferencesUnchanged || !report.originalStatesRestored) report.status = "failed";
                File.WriteAllText(Path.Combine(directory, "fade-proof.json"), JsonUtility.ToJson(report, true));
            }
            catch (Exception error) { Debug.LogError("Cannot save fade proof report: " + error.Message); report.status = "failed"; }
            Debug.Log("Opaque Near fade proof: " + report.status + "; " + directory);
            ReleaseReferences();
            if (quitWhenDone) Application.Quit(report.status == "passed" ? 0 : report.status == "inconclusive" ? 2 : 1);
        }

        private IEnumerator Series()
        {
            yield return new WaitForSecondsRealtime(30f);
            float timeout = Time.realtimeSinceStartup + 30f;
            while (app.Streamer.PendingLoads > 0 && Time.realtimeSinceStartup < timeout) yield return null;
            app.Streamer.enabled = false;
            report.streamingSettled = app.Streamer.PendingLoads == 0 && app.Streamer.LoadedTiles > 0 && app.Streamer.Failures == 0;
            app.Rendering.targetCamera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None; app.Rendering.ResetHistory();
            DisableSsaoForProof();
            yield return new WaitForSecondsRealtime(2f);
            importedMaterials = app.Surfaces.OriginalMaterialsSnapshot(); scratch = new MaterialPropertyBlock(); CollectTargets();
            if (targets.Count == 0) { report.status = "inconclusive"; yield break; }
            float[] values = { 1f, 1f, 0f, 1f };
            string[] names = { "near-1", "control-1", "near-0", "restored-1" };
            FadeProofSnapshot baseline = null; report.allInvariantsMatch = true;
            for (int i = 0; i < values.Length; i++)
            {
                foreach (var target in targets)
                {
                    target.renderer.GetPropertyBlock(scratch); scratch.SetVector("unity_LODFade", new Vector4(values[i], values[i], 0f, 0f)); target.renderer.SetPropertyBlock(scratch);
                }
                yield return new WaitForSecondsRealtime(2f);
                yield return new WaitForEndOfFrame();
                var snapshot = Snapshot(); if (baseline == null) baseline = snapshot;
                var image = new FadeProofImage { name = names[i] + ".png", requestedOpacity = values[i], snapshot = snapshot,
                    invariantsMatch = Same(baseline, snapshot) && snapshot.requestedMinimum == values[i] && snapshot.requestedMaximum == values[i] };
                report.allInvariantsMatch &= image.invariantsMatch;
                pixels.Add(CapturePng(image));
            }
            report.controlRepeat = Difference(pixels[0], pixels[1]); report.fadeZero = Difference(pixels[0], pixels[2]); report.restoreRepeat = Difference(pixels[0], pixels[3]);
            bool evidence = pixels[0].image.snapshot.visibleNearRenderers > 0 && pixels[0].image.rgbPixelSamples > 0;
            bool comparable = report.controlRepeat.comparable && report.fadeZero.comparable && report.restoreRepeat.comparable;
            bool stable = comparable && report.controlRepeat.changedPixels == 0 && report.restoreRepeat.changedPixels == 0;
            bool imagesWritten = true; foreach (var sample in pixels) imagesWritten &= sample.image.imageWritten;
            report.status = !report.allInvariantsMatch || !imagesWritten ? "failed" : !evidence || !stable || !report.streamingSettled || report.fadeZero.changedPixels == 0 ? "inconclusive" : "passed";
        }

        private void CollectTargets()
        {
            if (app.catalog == null) return;
            var nearIds = new HashSet<string>(); foreach (var tile in app.catalog.tiles) if (tile.layer == CityLayer.Near) nearIds.Add(tile.id);
            var planes = GeometryUtility.CalculateFrustumPlanes(app.Rendering.targetCamera);
            foreach (Transform tile in app.Streamer.transform)
            {
                if (!tile.gameObject.activeInHierarchy || !nearIds.Contains(tile.name)) continue;
                foreach (var renderer in tile.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    var evidence = new FadeProofRenderer { tile = tile.name, renderer = renderer.name, bounds = renderer.bounds, visible = renderer.isVisible,
                        intersectsMainCameraFrustum = GeometryUtility.TestPlanesAABB(planes, renderer.bounds), allMaterialsOpaque = true, allMaterialsHaveCrossFade = true };
                    var shaders = new StringBuilder(); var materials = renderer.sharedMaterials; evidence.materialSlots = materials.Length;
                    foreach (var material in materials)
                    {
                        evidence.allMaterialsOpaque &= material != null && material.renderQueue < 3000 && (!material.HasProperty("_Surface") || material.GetFloat("_Surface") < .5f);
                        evidence.allMaterialsHaveCrossFade &= material != null && material.IsKeywordEnabled("LOD_FADE_CROSSFADE");
                        shaders.Append(material != null ? material.shader.name : "null").Append('|');
                    }
                    if (materials.Length == 0 || !evidence.allMaterialsOpaque || !evidence.allMaterialsHaveCrossFade) continue;
                    evidence.shader = shaders.ToString();
                    var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original); evidence.originalFade = original.GetVector("unity_LODFade");
                    targets.Add(new Target { renderer = renderer, originalBlock = original, evidence = evidence });
                }
            }
        }
        // Unity 6.3/URP17.3 exposes the renderer data list publicly. Keep feature toggling private to this explicit QA component.
        private void DisableSsaoForProof()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            var seen = new HashSet<ScriptableRendererFeature>();
            foreach (var data in pipeline.rendererDataList)
            {
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion" || !seen.Add(feature)) continue;
                    ssaoFeatures.Add(new FeatureState { feature = feature, active = feature.isActive });
                    if (feature.isActive) report.ssaoPreviouslyActiveCount++;
                    feature.SetActive(false);
                }
            }
            report.ssaoFeatureCount = ssaoFeatures.Count; report.ssaoDisabledForProof = report.ssaoPreviouslyActiveCount > 0;
        }
        private Pixels CapturePng(FadeProofImage image)
        {
            Texture2D captured = null, decoded = null;
            try
            {
                captured = ScreenCapture.CaptureScreenshotAsTexture();
                if (captured == null) throw new InvalidOperationException("Screen capture returned no GPU image.");
                byte[] png = captured.EncodeToPNG(); File.WriteAllBytes(Path.Combine(directory, image.name), png);
                decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(decoded, png, false)) throw new InvalidOperationException("Saved PNG could not be decoded for RGB verification.");
                var colors = decoded.GetPixels32(); var rgb = new byte[colors.Length * 3];
                for (int i = 0; i < colors.Length; i++) { rgb[i * 3] = colors[i].r; rgb[i * 3 + 1] = colors[i].g; rgb[i * 3 + 2] = colors[i].b; }
                image.width = decoded.width; image.height = decoded.height; image.rgbPixelSamples = colors.Length;
                image.pngSha256 = Hash(png); image.rgbSha256 = Hash(rgb); image.imageWritten = File.Exists(Path.Combine(directory, image.name)) && new FileInfo(Path.Combine(directory, image.name)).Length == png.Length;
                return new Pixels { image = image, colors = colors };
            }
            finally { if (captured != null) Destroy(captured); if (decoded != null) Destroy(decoded); }
        }
        private static FadeProofDifference Difference(Pixels a, Pixels b)
        {
            var difference = new FadeProofDifference { from = a.image.name, to = b.image.name, comparable = a.image.width == b.image.width && a.image.height == b.image.height && a.colors.Length == b.colors.Length && a.colors.Length > 0 };
            if (!difference.comparable) return difference;
            long sum = 0; int minX = a.image.width, minY = a.image.height, maxX = -1, maxY = -1;
            difference.pixelSamples = a.colors.Length;
            for (int i = 0; i < a.colors.Length; i++)
            {
                int r = Math.Abs(a.colors[i].r - b.colors[i].r), g = Math.Abs(a.colors[i].g - b.colors[i].g), blue = Math.Abs(a.colors[i].b - b.colors[i].b);
                sum += r + g + blue; difference.maximumChannelDifference = Math.Max(difference.maximumChannelDifference, Math.Max(r, Math.Max(g, blue)));
                if (r + g + blue == 0) continue;
                difference.changedPixels++; int x = i % a.image.width, y = i / a.image.width;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
            difference.meanAbsoluteRgbDifference = (double)sum / (a.colors.Length * 3L);
            if (difference.changedPixels > 0) difference.changedPixelBounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return difference;
        }

        private FadeProofSnapshot Snapshot()
        {
            var text = new StringBuilder(); Transform rig = app.Flight.transform, camera = app.Rendering.targetCamera.transform;
            Pose(text, rig); Pose(text, camera); Vector(text, app.Origin.Offset); Vector(text, app.Flight.CityPosition); Vector(text, app.Display.EyePositionMeters);
            for (int i = 0; i < 16; i++) Number(text, app.Rendering.targetCamera.projectionMatrix[i]);
            var snapshot = new FadeProofSnapshot { rigAndCamera = Hash(text.ToString()), calibration = Hash(JsonUtility.ToJson(app.Display.Calibration)),
                city = City(), lighting = Lighting(), pipeline = Pipeline(app.Rendering.targetCamera), importedMaterials = Materials(), ui = Ui(),
                tracking = app.Display.TrackingEnabled, window = app.Frame.EffectEnabled, surface = app.Surfaces.EnabledDetails, trackingObservationFresh = app.Display.IsTracking,
                cityPosition = app.Flight.CityPosition, eyeMeters = app.Display.EyePositionMeters, loadedTiles = app.Streamer.LoadedTiles, pendingLoads = app.Streamer.PendingLoads, streamingFailures = app.Streamer.Failures,
                requestedMinimum = 1f, requestedMaximum = 0f };
            text.Clear(); Pose(text, app.Display.ScreenPlane); snapshot.screen = Hash(text.ToString());
            var planes = GeometryUtility.CalculateFrustumPlanes(app.Rendering.targetCamera);
            foreach (var target in targets)
            {
                target.renderer.GetPropertyBlock(scratch); float fade = scratch.GetVector("unity_LODFade").x;
                snapshot.requestedMinimum = Mathf.Min(snapshot.requestedMinimum, fade); snapshot.requestedMaximum = Mathf.Max(snapshot.requestedMaximum, fade);
                if (target.renderer.isVisible && GeometryUtility.TestPlanesAABB(planes, target.renderer.bounds)) snapshot.visibleNearRenderers++;
            }
            return snapshot;
        }
        private static bool Same(FadeProofSnapshot a, FadeProofSnapshot b) => a.rigAndCamera == b.rigAndCamera && a.screen == b.screen && a.calibration == b.calibration &&
            a.city == b.city && a.lighting == b.lighting && a.pipeline == b.pipeline && a.importedMaterials == b.importedMaterials && a.ui == b.ui &&
            a.tracking == b.tracking && a.window == b.window && a.surface == b.surface && a.loadedTiles == b.loadedTiles && a.pendingLoads == b.pendingLoads && a.streamingFailures == b.streamingFailures;
        private void Restore()
        {
            if (!holding) return; holding = false;
            foreach (var target in targets) if (target.renderer != null) target.renderer.SetPropertyBlock(target.originalBlock);
            foreach (var state in ssaoFeatures) if (state.feature != null) state.feature.SetActive(state.active);
            if (app != null)
            {
                if (app.Rendering != null && app.Rendering.targetCamera != null) { app.Rendering.targetCamera.GetUniversalAdditionalCameraData().antialiasing = savedAA; app.Rendering.ResetHistory(); }
                if (app.Display != null) app.Display.TrackingEnabled = savedTracking;
                if (app.Streamer != null) app.Streamer.enabled = savedStreaming;
                if (app.Flight != null) app.Flight.InputSource = savedInput;
                if (app.KeyboardInput != null) { app.KeyboardInput.InputEnabled = savedInputEnabled; app.KeyboardInput.Capture(savedCaptured); }
            }
            if (canvases != null) for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = canvasStates[i];
            if (raycasters != null) for (int i = 0; i < raycasters.Length; i++) if (raycasters[i] != null) raycasters[i].enabled = raycasterStates[i];
        }
        private void ReleaseReferences()
        {
            pixels.Clear(); targets.Clear(); ssaoFeatures.Clear(); importedMaterials = null; scratch = null; savedInput = null;
            canvases = null; raycasters = null; canvasStates = null; raycasterStates = null;
        }
        private void OnDisable() { StopAllCoroutines(); Restore(); ReleaseReferences(); }

        private string City()
        {
            var text = new StringBuilder();
            foreach (var transform in app.Streamer.GetComponentsInChildren<Transform>(true))
            {
                text.Append(transform.GetInstanceID()).Append('|').Append(transform.gameObject.activeSelf).Append('|'); Pose(text, transform);
                var renderer = transform.GetComponent<Renderer>();
                if (renderer != null) { text.Append(renderer.enabled).Append('|'); foreach (var material in renderer.sharedMaterials) text.Append(material != null ? material.GetInstanceID() : 0).Append('|'); }
                var filter = transform.GetComponent<MeshFilter>(); if (filter != null) text.Append(filter.sharedMesh != null ? filter.sharedMesh.GetInstanceID() : 0).Append('|');
            }
            return Hash(text.ToString());
        }
        private string Ui(bool includePose = true)
        {
            var text = new StringBuilder();
            foreach (var canvas in canvases) { text.Append(canvas.GetInstanceID()).Append('|').Append(canvas.enabled).Append('|').Append(canvas.renderMode).Append('|').Append(canvas.sortingOrder).Append('|'); if (includePose) Pose(text, canvas.transform); }
            foreach (var raycaster in raycasters) text.Append(raycaster.GetInstanceID()).Append('|').Append(raycaster.enabled).Append('|');
            return Hash(text.ToString());
        }
        private string Materials()
        {
            Array.Sort(importedMaterials, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID())); var text = new StringBuilder();
            foreach (var material in importedMaterials)
            {
                text.Append(material.GetInstanceID()).Append('|').Append(material.shader.name).Append('|').Append(material.renderQueue).Append('|');
                var keywords = material.shaderKeywords; Array.Sort(keywords, StringComparer.Ordinal); text.AppendJoin(",", keywords).Append('|');
                for (int i = 0; i < material.shader.GetPropertyCount(); i++)
                {
                    string property = material.shader.GetPropertyName(i); text.Append(property).Append('=');
                    switch (material.shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color: Color(text, material.GetColor(property)); break;
                        case ShaderPropertyType.Vector: var v = material.GetVector(property); Vector(text, v); Number(text, v.w); break;
                        case ShaderPropertyType.Texture: var texture = material.GetTexture(property); text.Append(texture != null ? texture.GetInstanceID() : 0).Append('|'); var scale = material.GetTextureScale(property); var offset = material.GetTextureOffset(property); Number(text, scale.x); Number(text, scale.y); Number(text, offset.x); Number(text, offset.y); break;
                        case ShaderPropertyType.Int: text.Append(material.GetInteger(property)).Append('|'); break;
                        default: Number(text, material.GetFloat(property)); break;
                    }
                }
            }
            return Hash(text.ToString());
        }
        private static string Pipeline(Camera camera)
        {
            var text = new StringBuilder(); var asset = GraphicsSettings.currentRenderPipeline; var data = camera.GetUniversalAdditionalCameraData();
            text.Append(asset != null ? asset.GetInstanceID() : 0).Append('|').Append(data.antialiasing).Append('|').Append(data.renderPostProcessing).Append('|');
            text.Append(camera.allowHDR).Append('|').Append(camera.allowMSAA).Append('|').Append(camera.allowDynamicResolution).Append('|'); Number(text, camera.nearClipPlane); Number(text, camera.farClipPlane);
            text.Append(data.taaSettings.quality).Append('|'); Number(text, data.taaSettings.baseBlendFactor); Number(text, data.taaSettings.jitterScale); Number(text, data.taaSettings.mipBias); Number(text, data.taaSettings.varianceClampScale); Number(text, data.taaSettings.contrastAdaptiveSharpening);
            if (asset is UniversalRenderPipelineAsset urp)
            {
                text.Append(urp.msaaSampleCount).Append('|').Append(urp.shadowCascadeCount).Append('|').Append(urp.enableLODCrossFade).Append('|'); Number(text, urp.renderScale); Number(text, urp.shadowDistance);
                foreach (var rendererData in urp.rendererDataList) if (rendererData != null) foreach (var feature in rendererData.rendererFeatures)
                    if (feature != null) text.Append(feature.GetInstanceID()).Append('|').Append(feature.GetType().Name).Append('|').Append(feature.isActive).Append('|');
            }
            return Hash(text.ToString());
        }
        private static string Lighting()
        {
            var text = new StringBuilder(); text.Append(RenderSettings.ambientMode).Append('|').Append(RenderSettings.fog).Append('|').Append(RenderSettings.fogMode).Append('|');
            Color(text, RenderSettings.ambientSkyColor); Color(text, RenderSettings.ambientEquatorColor); Color(text, RenderSettings.ambientGroundColor); Color(text, RenderSettings.fogColor); Number(text, RenderSettings.fogDensity);
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.InstanceID)) { text.Append(light.GetInstanceID()).Append('|').Append(light.type).Append('|').Append(light.shadows).Append('|').Append(light.enabled).Append('|'); Pose(text, light.transform); Color(text, light.color); Number(text, light.intensity); }
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.InstanceID)) { text.Append(volume.GetInstanceID()).Append('|').Append(volume.enabled).Append('|').Append(volume.isGlobal).Append('|'); Number(text, volume.weight); if (volume.sharedProfile != null) foreach (var component in volume.sharedProfile.components) text.Append(JsonUtility.ToJson(component)).Append('|'); }
            return Hash(text.ToString());
        }
        private static void Pose(StringBuilder text, Transform transform) { Vector(text, transform.position); Vector(text, transform.localPosition); var q = transform.rotation; Number(text, q.x); Number(text, q.y); Number(text, q.z); Number(text, q.w); Vector(text, transform.localScale); }
        private static void Color(StringBuilder text, Color color) { Number(text, color.r); Number(text, color.g); Number(text, color.b); Number(text, color.a); }
        private static void Vector(StringBuilder text, Vector3 vector) { Number(text, vector.x); Number(text, vector.y); Number(text, vector.z); }
        private static void Number(StringBuilder text, float value) => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        private static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
