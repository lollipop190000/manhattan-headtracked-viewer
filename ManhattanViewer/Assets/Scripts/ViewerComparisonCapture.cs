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
    public sealed class ComparisonCaptureSample
    {
        public string name, image, calibrationFingerprint, sourceMaterialFingerprint, cityFingerprint, lightingFingerprint, pipelineFingerprint;
        public bool tracking, windowFrame, surfaceDetail, trackingFresh, imageWritten, statesMatch, invariantsMatch;
        public Vector3 cityPosition, rigPosition, rigScale, screenLocalPosition, screenLocalScale, cameraPosition, physicalEyeMeters;
        public Quaternion rigRotation, screenLocalRotation, cameraRotation;
        public float nearFadeMinimum, nearFadeMaximum;
        public int nearRendererCount, nearVisibleRendererCount, loadedTiles, pendingLoads, streamingFailures;
    }

    [Serializable]
    public sealed class ComparisonCaptureReport
    {
        public string utcTime, reportKind = "measured-comparison-captures", unityVersion, sourceVersion, error;
        public int width, height, renderWidth, renderHeight;
        public string fullscreenMode;
        public bool streamingSettled, allImagesWritten, allInvariantsMatch, preferencesUnchanged, originalStatesRestored;
        public ViewerBuildIdentity buildIdentity;
        public ComparisonCaptureSample[] samples;
        public string limitation = "Near fade values are requested MaterialPropertyBlock values, not proof of rasterized opaque coverage. Source-material fingerprints compare this process only. Head observations continue and the camera eye may move while tracking is on; only the movement rig and physical screen remain fixed. Streaming residency is held during the capture series.";
    }

    /// <summary>Explicit standalone QA mode. Comparisons use their APIs without changing or saving preferences.</summary>
    public sealed class ViewerComparisonCapture : MonoBehaviour
    {
        private ManhattanBootstrap app;
        private IMotionInputSource savedInput;
        private bool savedInputEnabled, savedCaptured, savedStreaming, savedTracking, savedWindow, savedSurface, holding;
        private GraphicRaycaster[] raycasters;
        private bool[] raycasterStates;
        private Canvas[] canvases;
        private bool[] canvasStates;
        private Material[] originalMaterials;
        private string preferencesFingerprint;

        public void Begin(ManhattanBootstrap viewer, string directory, bool quit)
        {
            if (holding) return;
            app = viewer;
            if (ManhattanBootstrap.HasArgument("--benchmark") || ManhattanBootstrap.HasArgument("--screenshot-out") || ManhattanBootstrap.HasArgument("--fade-proof-out"))
            {
                Debug.LogError("Comparison captures require a separate run from benchmarks and single screenshots.");
                if (quit) Application.Quit(1);
                return;
            }
            string path;
            try { path = Path.GetFullPath(directory); Directory.CreateDirectory(path); }
            catch (Exception error) { Debug.LogError("Cannot prepare comparison capture directory: " + error.Message); if (quit) Application.Quit(1); return; }
            savedInput = app.Flight.InputSource; savedInputEnabled = app.KeyboardInput.InputEnabled; savedCaptured = app.KeyboardInput.Captured;
            savedStreaming = app.Streamer.enabled;
            savedTracking = app.Display.TrackingEnabled; savedWindow = app.Frame.EffectEnabled; savedSurface = app.Surfaces.EnabledDetails;
            preferencesFingerprint = Hash(JsonUtility.ToJson(app.Preferences));
            canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None); canvasStates = new bool[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) canvasStates[i] = canvases[i].enabled;
            raycasters = FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None); raycasterStates = new bool[raycasters.Length];
            for (int i = 0; i < raycasters.Length; i++) { raycasterStates[i] = raycasters[i].enabled; raycasters[i].enabled = false; }
            holding = true;
            app.KeyboardInput.InputEnabled = false; app.KeyboardInput.Capture(false); app.Flight.InputSource = null;
            StartCoroutine(CaptureSeries(path, quit));
        }

        private IEnumerator CaptureSeries(string directory, bool quit)
        {
            var report = new ComparisonCaptureReport { unityVersion = Application.unityVersion, sourceVersion = app.catalog != null ? app.catalog.sourceVersion : null };
            var samples = new List<ComparisonCaptureSample>();
            try
            {
                yield return new WaitForSecondsRealtime(30f);
                float settleDeadline = Time.realtimeSinceStartup + 30f;
                while (app.Streamer.PendingLoads != 0 && Time.realtimeSinceStartup < settleDeadline) yield return null;
                // Keep precisely the same resident city geometry for every image.
                app.Streamer.enabled = false;
                yield return new WaitForSecondsRealtime(2f);
                report.streamingSettled = app.Streamer.PendingLoads == 0 && app.Streamer.LoadedTiles > 0 && app.Streamer.Failures == 0;
                originalMaterials = app.Surfaces.OriginalMaterialsSnapshot();
                var baseline = Snapshot("baseline", savedTracking, savedWindow, savedSurface);
                yield return SaveSample(directory, baseline); samples.Add(baseline);
                for (int state = 0; state < 8; state++)
                {
                    bool tracking = (state & 1) != 0, window = (state & 2) != 0, surface = (state & 4) != 0;
                    ApplyComparisons(tracking, window, surface);
                    yield return new WaitForSecondsRealtime(2f);
                    var sample = Snapshot("combination-" + state + "-T" + (tracking ? 1 : 0) + "W" + (window ? 1 : 0) + "S" + (surface ? 1 : 0), tracking, window, surface);
                    sample.invariantsMatch = Matches(baseline, sample);
                    yield return SaveSample(directory, sample); samples.Add(sample);
                }
                ApplyComparisons(savedTracking, savedWindow, savedSurface);
                yield return new WaitForSecondsRealtime(2f);
                var roundTrip = Snapshot("roundtrip", savedTracking, savedWindow, savedSurface);
                roundTrip.invariantsMatch = Matches(baseline, roundTrip);
                yield return SaveSample(directory, roundTrip); samples.Add(roundTrip);
                report.preferencesUnchanged = preferencesFingerprint == Hash(JsonUtility.ToJson(app.Preferences));
                report.originalStatesRestored = roundTrip.statesMatch;
                report.allImagesWritten = true; report.allInvariantsMatch = true;
                foreach (var sample in samples) { report.allImagesWritten &= sample.imageWritten; report.allInvariantsMatch &= sample.statesMatch && sample.invariantsMatch; }
                report.width = Screen.width; report.height = Screen.height;
                report.renderWidth = app.Rendering.targetCamera.scaledPixelWidth; report.renderHeight = app.Rendering.targetCamera.scaledPixelHeight;
                report.fullscreenMode = Screen.fullScreenMode.ToString();
                var identity = Resources.Load<TextAsset>("ViewerBuildIdentity");
                if (identity != null) report.buildIdentity = JsonUtility.FromJson<ViewerBuildIdentity>(identity.text);
            }
            finally { Restore(); }
            report.utcTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); report.samples = samples.ToArray();
            bool written = false;
            try { File.WriteAllText(Path.Combine(directory, "comparison-report.json"), JsonUtility.ToJson(report, true)); written = true; }
            catch (Exception error) { Debug.LogError("Cannot write comparison report: " + error.Message); }
            bool passed = written && report.streamingSettled && report.allImagesWritten && report.allInvariantsMatch && report.preferencesUnchanged && report.originalStatesRestored;
            Debug.Log("Comparison captures finished: " + directory + "; recorded checks " + (passed ? "passed" : "pending or failed") + ". Visual image review is still required.");
            if (quit) Application.Quit(passed ? 0 : 1);
        }

        private static IEnumerator SaveSample(string directory, ComparisonCaptureSample sample)
        {
            sample.image = sample.name + ".png";
            yield return ViewerScreenshotCapture.CaptureImage(Path.Combine(directory, sample.image), success => sample.imageWritten = success);
        }
        private void ApplyComparisons(bool tracking, bool window, bool surface)
        {
            app.Display.TrackingEnabled = tracking; app.Frame.EffectEnabled = window; app.Surfaces.EnabledDetails = surface;
        }
        private ComparisonCaptureSample Snapshot(string name, bool tracking, bool window, bool surface)
        {
            Transform rig = app.Flight.transform, screen = app.Display.ScreenPlane, camera = app.Rendering.targetCamera.transform;
            var sample = new ComparisonCaptureSample
            {
                name = name, tracking = app.Display.TrackingEnabled, windowFrame = app.Frame.EffectEnabled, surfaceDetail = app.Surfaces.EnabledDetails,
                statesMatch = tracking == app.Display.TrackingEnabled && window == app.Frame.EffectEnabled && surface == app.Surfaces.EnabledDetails,
                invariantsMatch = true, trackingFresh = app.Display.IsTracking, cityPosition = app.Flight.CityPosition, rigPosition = rig.position,
                rigRotation = rig.rotation, rigScale = rig.localScale, screenLocalPosition = screen.localPosition, screenLocalRotation = screen.localRotation,
                screenLocalScale = screen.localScale, cameraPosition = camera.position, cameraRotation = camera.rotation, physicalEyeMeters = app.Display.EyePositionMeters,
                calibrationFingerprint = Hash(JsonUtility.ToJson(app.Display.Calibration)), sourceMaterialFingerprint = MaterialFingerprint(originalMaterials),
                cityFingerprint = CityFingerprint(app.Streamer.transform), lightingFingerprint = LightingFingerprint(), pipelineFingerprint = PipelineFingerprint(app.Rendering.targetCamera),
                loadedTiles = app.Streamer.LoadedTiles, pendingLoads = app.Streamer.PendingLoads, streamingFailures = app.Streamer.Failures
            };
            app.Surfaces.GetNearFadeRange(out sample.nearFadeMinimum, out sample.nearFadeMaximum, out sample.nearRendererCount, out sample.nearVisibleRendererCount);
            return sample;
        }
        private static bool Matches(ComparisonCaptureSample baseline, ComparisonCaptureSample sample) =>
            baseline.cityPosition == sample.cityPosition && baseline.rigPosition == sample.rigPosition && baseline.rigRotation == sample.rigRotation && baseline.rigScale == sample.rigScale &&
            baseline.screenLocalPosition == sample.screenLocalPosition && baseline.screenLocalRotation == sample.screenLocalRotation && baseline.screenLocalScale == sample.screenLocalScale &&
            baseline.calibrationFingerprint == sample.calibrationFingerprint && baseline.sourceMaterialFingerprint == sample.sourceMaterialFingerprint &&
            baseline.cityFingerprint == sample.cityFingerprint && baseline.lightingFingerprint == sample.lightingFingerprint && baseline.pipelineFingerprint == sample.pipelineFingerprint &&
            baseline.loadedTiles == sample.loadedTiles && baseline.pendingLoads == sample.pendingLoads && baseline.streamingFailures == sample.streamingFailures;

        private void Restore()
        {
            if (!holding) return;
            holding = false;
            if (app != null)
            {
                ApplyComparisons(savedTracking, savedWindow, savedSurface);
                if (app.Streamer != null) app.Streamer.enabled = savedStreaming;
                if (app.Flight != null) app.Flight.InputSource = savedInput;
                if (app.KeyboardInput != null) { app.KeyboardInput.InputEnabled = savedInputEnabled; app.KeyboardInput.Capture(savedCaptured); }
            }
            if (canvases != null) for (int i = 0; i < canvases.Length; i++) if (canvases[i] != null) canvases[i].enabled = canvasStates[i];
            if (raycasters != null) for (int i = 0; i < raycasters.Length; i++) if (raycasters[i] != null) raycasters[i].enabled = raycasterStates[i];
            originalMaterials = null; savedInput = null; raycasters = null; raycasterStates = null; canvases = null; canvasStates = null;
        }
        private void OnDisable() { StopAllCoroutines(); Restore(); }

        private static string CityFingerprint(Transform root)
        {
            var text = new StringBuilder();
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            {
                text.Append(item.GetInstanceID()).Append('|').Append(item.gameObject.activeSelf).Append('|');
                Append(text, item.localPosition); Append(text, item.localRotation); Append(text, item.localScale);
                var mesh = item.GetComponent<MeshFilter>(); if (mesh != null) text.Append(mesh.sharedMesh != null ? mesh.sharedMesh.GetInstanceID() : 0).Append('|');
            }
            return Hash(text.ToString());
        }
        private static string MaterialFingerprint(Material[] materials)
        {
            Array.Sort(materials, (a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
            var text = new StringBuilder();
            foreach (var material in materials)
            {
                text.Append(material.GetInstanceID()).Append('|').Append(material.shader.name).Append('|').Append(material.renderQueue).Append('|');
                string[] keywords = material.shaderKeywords; Array.Sort(keywords, StringComparer.Ordinal); text.AppendJoin(",", keywords).Append('|');
                for (int i = 0; i < material.shader.GetPropertyCount(); i++)
                {
                    string property = material.shader.GetPropertyName(i); text.Append(property).Append('=');
                    switch (material.shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Color: Append(text, material.GetColor(property)); break;
                        case ShaderPropertyType.Vector: Append(text, material.GetVector(property)); break;
                        case ShaderPropertyType.Texture:
                            var texture = material.GetTexture(property); text.Append(texture != null ? texture.GetInstanceID() : 0).Append('|');
                            Append(text, material.GetTextureScale(property)); Append(text, material.GetTextureOffset(property)); break;
                        case ShaderPropertyType.Int: text.Append(material.GetInteger(property)).Append('|'); break;
                        default: Append(text, material.GetFloat(property)); break;
                    }
                }
            }
            return Hash(text.ToString());
        }
        private static string LightingFingerprint()
        {
            var text = new StringBuilder(); text.Append(RenderSettings.ambientMode).Append('|').Append(RenderSettings.fog).Append('|').Append(RenderSettings.fogMode).Append('|');
            Append(text, RenderSettings.ambientSkyColor); Append(text, RenderSettings.ambientEquatorColor); Append(text, RenderSettings.ambientGroundColor);
            Append(text, RenderSettings.fogColor); Append(text, RenderSettings.fogDensity);
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.InstanceID))
            {
                text.Append(light.GetInstanceID()).Append('|').Append(light.type).Append('|').Append(light.shadows).Append('|').Append(light.enabled).Append('|');
                Append(text, light.transform.position); Append(text, light.transform.rotation); Append(text, light.color); Append(text, light.intensity);
            }
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.InstanceID))
            {
                Append(text, volume.weight); text.Append(volume.isGlobal).Append('|').Append(volume.enabled).Append('|');
                if (volume.sharedProfile != null) foreach (var component in volume.sharedProfile.components) text.Append(JsonUtility.ToJson(component)).Append('|');
            }
            return Hash(text.ToString());
        }
        private static string PipelineFingerprint(Camera camera)
        {
            var text = new StringBuilder(); var asset = GraphicsSettings.currentRenderPipeline;
            text.Append(asset != null ? asset.GetInstanceID() : 0).Append('|').Append(camera.allowHDR).Append('|').Append(camera.allowMSAA).Append('|').Append(camera.allowDynamicResolution).Append('|');
            var data = camera.GetUniversalAdditionalCameraData(); text.Append(data.antialiasing).Append('|').Append(data.renderPostProcessing).Append('|');
            Append(text, camera.nearClipPlane); Append(text, camera.farClipPlane); Append(text, camera.fieldOfView);
            text.Append(data.taaSettings.quality).Append('|'); Append(text, data.taaSettings.baseBlendFactor); Append(text, data.taaSettings.jitterScale);
            Append(text, data.taaSettings.mipBias); Append(text, data.taaSettings.varianceClampScale); Append(text, data.taaSettings.contrastAdaptiveSharpening);
            if (asset is UniversalRenderPipelineAsset urp) { text.Append(urp.msaaSampleCount).Append('|').Append(urp.shadowCascadeCount).Append('|'); Append(text, urp.renderScale); Append(text, urp.shadowDistance); }
            return Hash(text.ToString());
        }
        private static void Append(StringBuilder text, Vector2 value) { Append(text, value.x); Append(text, value.y); }
        private static void Append(StringBuilder text, Vector3 value) { Append(text, value.x); Append(text, value.y); Append(text, value.z); }
        private static void Append(StringBuilder text, Color value) { Append(text, value.r); Append(text, value.g); Append(text, value.b); Append(text, value.a); }
        private static void Append(StringBuilder text, Vector4 value) { Append(text, value.x); Append(text, value.y); Append(text, value.z); Append(text, value.w); }
        private static void Append(StringBuilder text, Quaternion value) { Append(text, value.x); Append(text, value.y); Append(text, value.z); Append(text, value.w); }
        private static void Append(StringBuilder text, float value) => text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        private static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
