using System;
using HeadTracked.Display;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Composition only. City generation, input, persistence and UI live in separate components.</summary>
    public sealed class ManhattanBootstrap : MonoBehaviour
    {
        public CityCatalog catalog;
        public Material windowFrameMaterial;
        public HeadTrackedDisplay Display { get; private set; }
        public ScreenWindowFrame Frame { get; private set; }
        public FreeFlightController Flight { get; private set; }
        public FloatingOrigin Origin { get; private set; }
        public KeyboardMotionInput KeyboardInput { get; private set; }
        public ControllerMotionInput ControllerInput { get; private set; }
        public SurfaceDetailComparison Surfaces { get; private set; }
        public CityStreamer Streamer { get; private set; }
        public AfternoonRenderProfile Rendering { get; private set; }
        public ViewerBenchmark Benchmark { get; private set; }
        public ViewerPreferences Preferences { get; private set; }
        private Camera renderCamera;
        private Material frameMaterial;
        private Transform plane;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartEmptyScene()
        {
            if (HasArgument("-runTests")) return;
            if (FindFirstObjectByType<ManhattanBootstrap>() != null) return;
            var catalog = Resources.Load<CityCatalog>("CityCatalog");
            if (catalog == null) return;
            var go = new GameObject("Manhattan Viewer"); go.SetActive(false);
            go.AddComponent<ManhattanBootstrap>().catalog = catalog; go.SetActive(true);
        }

        private void Awake()
        {
            if (catalog == null) catalog = Resources.Load<CityCatalog>("CityCatalog");
            Preferences = ViewerPersistence.Load(ViewerPersistence.PreferencesFile, new ViewerPreferences());
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None)) camera.gameObject.SetActive(false);
            var rig = new GameObject("Observer Rig"); rig.transform.SetParent(transform, false);
            Origin = gameObject.AddComponent<FloatingOrigin>(); Origin.observer = rig.transform;
            KeyboardInput = gameObject.AddComponent<KeyboardMotionInput>(); KeyboardInput.MouseSensitivity = Preferences.mouseSensitivity;
            Flight = rig.AddComponent<FreeFlightController>(); Flight.Origin = Origin; Flight.InputSource = KeyboardInput; Flight.Speed = Preferences.movementSpeed;
            if (!HasArgument("--no-controller"))
            {
                ControllerInput = gameObject.AddComponent<ControllerMotionInput>(); ControllerInput.Keyboard = KeyboardInput;
                Flight.InputSource = ControllerInput;
            }
            Flight.SetPose(catalog != null ? catalog.initialEyePosition : new Vector3(1225, 1.85f, 3585),
                Quaternion.Euler(catalog != null ? catalog.initialEulerAngles : new Vector3(0, -151.5f, 0)));
            plane = new GameObject("Physical Screen Plane").transform; plane.SetParent(rig.transform, false);
            var cameraObject = new GameObject("Head Tracked Camera"); cameraObject.transform.SetParent(rig.transform, false);
            // Apply saved physical calibration before HeadTrackedDisplay.Awake chooses its neutral eye.
            cameraObject.SetActive(false);
            cameraObject.tag = "MainCamera";
            renderCamera = cameraObject.AddComponent<Camera>(); renderCamera.nearClipPlane = .03f; renderCamera.farClipPlane = 25000f;
            renderCamera.clearFlags = CameraClearFlags.Skybox; renderCamera.backgroundColor = new Color(.43f, .57f, .72f);
            cameraObject.AddComponent<AudioListener>();
            HeadObservationSource source = null;
            if (!HasArgument("--no-tracker")) source = gameObject.AddComponent<PythonBridgeSource>();
            Display = cameraObject.AddComponent<HeadTrackedDisplay>();
            ViewerPersistence.RestoreCalibration(Display.Calibration); Display.Configure(plane, source);
            Frame = gameObject.AddComponent<ScreenWindowFrame>();
            var frameTemplate = windowFrameMaterial != null ? windowFrameMaterial : Resources.Load<Material>("WindowFrameMaterial");
            if (frameTemplate == null) throw new InvalidOperationException("Window frame material is missing. Run Manhattan/Create viewer scene and render settings before building.");
            frameMaterial = new Material(frameTemplate) { name = "Physical Window Frame", hideFlags = HideFlags.DontSave };
            frameMaterial.SetColor("_BaseColor", new Color(.13f, .16f, .19f)); frameMaterial.SetFloat("_Metallic", .4f); frameMaterial.SetFloat("_Smoothness", .65f);
            Frame.FrameMaterial = frameMaterial; Frame.Configure(Display);
            Surfaces = gameObject.AddComponent<SurfaceDetailComparison>();
            Rendering = gameObject.AddComponent<AfternoonRenderProfile>(); Rendering.Configure(renderCamera);
            Streamer = new GameObject("City Residency").AddComponent<CityStreamer>(); Streamer.transform.SetParent(transform, false);
            Streamer.catalog = catalog; Streamer.flight = Flight; Streamer.origin = Origin; Streamer.surfaces = Surfaces; Streamer.Initialize();
            Benchmark = gameObject.AddComponent<ViewerBenchmark>(); Benchmark.flight = Flight; Benchmark.streamer = Streamer; Benchmark.display = Display;
            Origin.Rebased += OnRebased; Flight.Teleported += Rendering.ResetHistory;
            UpdateCalibration(); ApplyPreferences();
            cameraObject.SetActive(true);
            gameObject.AddComponent<ViewerSettingsUI>().Initialize(this);
            QualitySettings.vSyncCount = 1; Application.targetFrameRate = 60;
            string screenshotPath = StringArgument("--screenshot-out");
            if (!string.IsNullOrEmpty(screenshotPath))
                gameObject.AddComponent<ViewerScreenshotCapture>().Begin(screenshotPath, FloatArgument("--screenshot-delay", 30f), HasArgument("--quit-after-screenshot"));
            string comparisonPath = StringArgument("--comparison-captures-out");
            if (!string.IsNullOrEmpty(comparisonPath))
                gameObject.AddComponent<ViewerComparisonCapture>().Begin(this, comparisonPath, HasArgument("--quit-after-comparison-captures"));
            string fadeProofPath = StringArgument("--fade-proof-out");
            if (!string.IsNullOrEmpty(fadeProofPath))
                gameObject.AddComponent<RuntimeFadeProofCapture>().Begin(this, fadeProofPath, HasArgument("--quit-after-fade-proof"));
            if (HasArgument("--benchmark"))
            {
                KeyboardInput.InputEnabled = false;
                Benchmark.StartBenchmark(FloatArgument("--benchmark-speed", 10f), FloatArgument("--benchmark-duration", 1200f),
                    StringArgument("--benchmark-out"), HasArgument("--collision-proxies"), HasArgument("--quit-after-benchmark"), StringArgument("--benchmark-region") ?? "Midtown");
            }
        }

        public void UpdateCalibration()
        {
            var c = Display.Calibration;
            c.deriveScreenHeightFromResolution = false;
            plane.localPosition = new Vector3(0, 0, c.referenceEyeDistanceFromScreen);
            renderCamera.fieldOfView = Mathf.Rad2Deg * 2f * Mathf.Atan(c.screenHeight / (2f * c.referenceEyeDistanceFromScreen));
            Frame.Refresh(); Rendering.ResetHistory();
        }
        public void ApplyPreferences()
        {
            Display.TrackingEnabled = Preferences.tracking; Frame.EffectEnabled = Preferences.windowFrame;
            Surfaces.EnabledDetails = Preferences.surfaceDetail; Flight.Speed = Preferences.movementSpeed;
            KeyboardInput.MouseSensitivity = Preferences.mouseSensitivity;
            Display.FilterMinimumCutoffHz = Preferences.filterCutoff;
            Display.FilterSpeedCoefficient = Preferences.filterSpeedCoefficient;
            Rendering.SetTemporalAA(Preferences.temporalAA);
        }
        public void SavePreferences() => ViewerPersistence.Save(ViewerPersistence.PreferencesFile, Preferences);
        public void SaveBookmark() => ViewerPersistence.Save(ViewerPersistence.BookmarkFile, new ViewerBookmark { cityPosition = Flight.CityPosition, eulerAngles = Flight.transform.eulerAngles });
        public void LoadBookmark()
        {
            var bookmark = ViewerPersistence.Load<ViewerBookmark>(ViewerPersistence.BookmarkFile, null);
            if (bookmark != null) Flight.SetPose(bookmark.cityPosition, Quaternion.Euler(bookmark.eulerAngles));
        }
        private void OnRebased(Vector3 shift) => Rendering.ResetHistory();
        private static string[] Arguments => Environment.GetCommandLineArgs();
        public static bool HasArgument(string name) => Array.IndexOf(Arguments, name) >= 0;
        private static string StringArgument(string name)
        {
            var args = Arguments; int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        private static float FloatArgument(string name, float fallback) =>
            float.TryParse(StringArgument(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) ? value : fallback;
        private void OnDestroy()
        {
            if (Origin != null) Origin.Rebased -= OnRebased;
            if (Flight != null && Rendering != null) Flight.Teleported -= Rendering.ResetHistory;
            if (frameMaterial != null) Destroy(frameMaterial);
        }
    }
}
