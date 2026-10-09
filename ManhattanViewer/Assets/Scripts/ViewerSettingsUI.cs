using System;
using System.Globalization;
using HeadTracked.Display;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Manhattan.Viewer
{
    public sealed class ViewerSettingsUI : MonoBehaviour
    {
        private ManhattanBootstrap app;
        private Font font;
        private GameObject drawer, setup;
        private UnityEngine.UI.Text diagnostics, message;
        private float nextDiagnostics;
        private static readonly Color Ink = new Color(.91f, .95f, .99f);
        private static readonly Color Panel = new Color(.045f, .063f, .085f, .96f);
        private static readonly Color Accent = new Color(.27f, .79f, .67f);

        public void Initialize(ManhattanBootstrap bootstrap)
        {
            app = bootstrap; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Viewer UI Events"); events.AddComponent<EventSystem>(); events.AddComponent<InputSystemUIInputModule>();
            }
            var canvasObject = new GameObject("Manhattan UI", typeof(RectTransform)); canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1200); scaler.matchWidthOrHeight = .5f;
            canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var header = Rect("Header", canvas.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(670, 132));
            header.pivot = new Vector2(0, 1);
            Text(header, "MANHATTAN  /  HEAD TRACKED", 23, 0, 0, 670, 32);
            Text(header, "WASD fly  ·  Q/E descend / rise  ·  Mouse look  ·  Esc settings", 15, 0, -38, 670, 28);
            var comparisons = Rect("Comparisons", header, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -80), new Vector2(670, 34));
            comparisons.pivot = new Vector2(0, 1);
            CompareButton(comparisons, "Tracking", 0, () => app.Preferences.tracking, value => { app.Preferences.tracking = value; app.Display.TrackingEnabled = value; app.SavePreferences(); });
            CompareButton(comparisons, "Window", 174, () => app.Preferences.windowFrame, value => { app.Preferences.windowFrame = value; app.Frame.EffectEnabled = value; app.SavePreferences(); });
            CompareButton(comparisons, "Surface", 348, () => app.Preferences.surfaceDetail, value => { app.Preferences.surfaceDetail = value; app.Surfaces.EnabledDetails = value; app.SavePreferences(); });
            var settings = Rect("Settings Anchor", canvas.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-184, -24), new Vector2(160, 40));
            settings.pivot = new Vector2(0, 1); Button(settings, "Settings  /  Esc", () => ShowDrawer(drawer == null || !drawer.activeSelf));
            var diag = Rect("Diagnostics", canvas.transform, Vector2.zero, Vector2.zero, new Vector2(24, 24), new Vector2(900, 130)); diag.pivot = Vector2.zero;
            diagnostics = Text(diag, "", 15, 0, 0, 900, 130); diagnostics.rectTransform.anchorMin = diagnostics.rectTransform.anchorMax = Vector2.zero; diagnostics.rectTransform.pivot = Vector2.zero;
            BuildDrawer(canvas.transform); BuildSetup(canvas.transform);
            app.KeyboardInput.EscapePressed += OnEscape;
            bool requiresSetup = !ViewerPersistence.HasCalibration && !ManhattanBootstrap.HasArgument("--benchmark") && !ManhattanBootstrap.HasArgument("--screenshot-out") && !ManhattanBootstrap.HasArgument("--comparison-captures-out") && !ManhattanBootstrap.HasArgument("--fade-proof-out");
            setup.SetActive(requiresSetup); drawer.SetActive(false);
            app.KeyboardInput.InputEnabled = !requiresSetup && !ManhattanBootstrap.HasArgument("--benchmark");
        }

        private void CompareButton(Transform parent, string name, float x, Func<bool> read, Action<bool> write)
        {
            var anchor = Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, 0), new Vector2(160, 34)); anchor.pivot = new Vector2(0, 1);
            UnityEngine.UI.Text label = null;
            var button = Button(anchor, name + (read() ? "  ON" : "  OFF"), () => { write(!read()); label.text = name + (read() ? "  ON" : "  OFF"); });
            label = button.GetComponentInChildren<UnityEngine.UI.Text>();
        }

        private void BuildDrawer(Transform parent)
        {
            var panel = Rect("Settings Drawer", parent, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-24, 0), new Vector2(420, -104));
            panel.pivot = new Vector2(1, .5f); panel.offsetMin = new Vector2(-444, 24); panel.offsetMax = new Vector2(-24, -80);
            drawer = panel.gameObject; panel.gameObject.AddComponent<UnityEngine.UI.Image>().color = Panel;
            var view = Rect("ScrollView", panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); view.offsetMin = new Vector2(20, 16); view.offsetMax = new Vector2(-20, -16);
            var scroll = view.gameObject.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 28;
            var viewport = Rect("Viewport", view, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            var content = Rect("Content", viewport, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero); content.pivot = new Vector2(.5f, 1);
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.spacing = 9; layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            Heading(content, "SETTINGS");
            TextRow(content, "Physical values use centimetres. Changes preserve city position.", 54);
            var c = app.Display.Calibration;
            NumericRow(content, "Screen width (cm)", c.screenWidth * 100f, v => { if (v >= 10 && v <= 300) { c.screenWidth = v / 100f; app.UpdateCalibration(); } });
            NumericRow(content, "Screen height (cm)", c.screenHeight * 100f, v => { if (v >= 10 && v <= 200) { c.screenHeight = v / 100f; app.UpdateCalibration(); } });
            NumericRow(content, "Viewing distance (cm)", c.referenceEyeDistanceFromScreen * 100f, v => { if (v >= 25 && v <= 150) { c.referenceEyeDistanceFromScreen = v / 100f; app.UpdateCalibration(); } });
            ActionRow(content, "Save physical calibration", () => { ViewerPersistence.Save(ViewerPersistence.CalibrationFile, c); app.Rendering.ResetHistory(); });
            ActionRow(content, "Capture current face reference", () => { if (app.Display.CaptureReference()) { ViewerPersistence.Save(ViewerPersistence.CalibrationFile, c); app.Rendering.ResetHistory(); } });
            Heading(content, "FLIGHT & IMAGE");
            NumericRow(content, "Movement speed (m/s)", app.Flight.Speed, v => { app.Flight.Speed = v; app.Preferences.movementSpeed = app.Flight.Speed; app.SavePreferences(); });
            NumericRow(content, "Mouse sensitivity", app.KeyboardInput.MouseSensitivity, v => { app.KeyboardInput.MouseSensitivity = Mathf.Clamp(v, .01f, 1f); app.Preferences.mouseSensitivity = app.KeyboardInput.MouseSensitivity; app.SavePreferences(); });
            ToggleRow(content, "Temporal anti-aliasing", app.Preferences.temporalAA, v => { app.Preferences.temporalAA = v; app.Rendering.SetTemporalAA(v); app.SavePreferences(); });
            ToggleRow(content, "Show diagnostics", app.Preferences.diagnostics, v => { app.Preferences.diagnostics = v; app.SavePreferences(); });
            Heading(content, "CAMERA CALIBRATION");
            NumericRow(content, "Eye separation (mm)", c.measuredEyeSeparationMeters * 1000f, v => c.measuredEyeSeparationMeters = Mathf.Clamp(v, 45, 85) / 1000f);
            NumericRow(content, "Webcam horizontal FOV", c.horizontalFovDegrees, v => c.horizontalFovDegrees = Mathf.Clamp(v, 30, 110));
            NumericRow(content, "Webcam X (cm)", c.webcamPosition.x * 100f, v => c.webcamPosition.x = v / 100f);
            NumericRow(content, "Webcam Y (cm)", c.webcamPosition.y * 100f, v => c.webcamPosition.y = v / 100f);
            NumericRow(content, "Webcam Z (cm)", c.webcamPosition.z * 100f, v => c.webcamPosition.z = v / 100f);
            NumericRow(content, "Webcam pitch (degrees)", c.webcamEulerDegrees.x, v => c.webcamEulerDegrees.x = v);
            NumericRow(content, "Webcam yaw (degrees)", c.webcamEulerDegrees.y, v => c.webcamEulerDegrees.y = v);
            ToggleRow(content, "Mirror observation X", c.mirrorImageX, v => c.mirrorImageX = v);
            ToggleRow(content, "Precise camera intrinsics", c.usePreciseIntrinsics, v => c.usePreciseIntrinsics = v);
            NumericRow(content, "Camera fx (pixels)", c.focalXPixels, v => c.focalXPixels = Mathf.Max(1, v));
            NumericRow(content, "Camera fy (pixels)", c.focalYPixels, v => c.focalYPixels = Mathf.Max(1, v));
            NumericRow(content, "Principal cx (pixels)", c.principalXPixels, v => c.principalXPixels = v);
            NumericRow(content, "Principal cy (pixels)", c.principalYPixels, v => c.principalYPixels = v);
            NumericRow(content, "Intrinsic image width", c.intrinsicsImageWidth, v => c.intrinsicsImageWidth = Mathf.Max(1, Mathf.RoundToInt(v)));
            NumericRow(content, "Intrinsic image height", c.intrinsicsImageHeight, v => c.intrinsicsImageHeight = Mathf.Max(1, Mathf.RoundToInt(v)));
            NumericRow(content, "Distortion k1", c.k1, v => c.k1 = v); NumericRow(content, "Distortion k2", c.k2, v => c.k2 = v);
            NumericRow(content, "Distortion k3", c.k3, v => c.k3 = v); NumericRow(content, "Distortion p1", c.p1, v => c.p1 = v); NumericRow(content, "Distortion p2", c.p2, v => c.p2 = v);
            NumericRow(content, "Filter cutoff (Hz)", app.Display.FilterMinimumCutoffHz, v => { app.Display.FilterMinimumCutoffHz = v; app.Preferences.filterCutoff = app.Display.FilterMinimumCutoffHz; app.SavePreferences(); });
            NumericRow(content, "Filter speed coefficient", app.Display.FilterSpeedCoefficient, v => { app.Display.FilterSpeedCoefficient = v; app.Preferences.filterSpeedCoefficient = app.Display.FilterSpeedCoefficient; app.SavePreferences(); });
            Heading(content, "LAYOUTS");
            TextRow(content, "Bookmarks load only when selected. Calibration never changes the next start position.", 52);
            ActionRow(content, "Save position bookmark", app.SaveBookmark); ActionRow(content, "Load position bookmark", app.LoadBookmark);
            ActionRow(content, "Return to Fifth Avenue", () => { if (app.catalog != null) app.Flight.SetPose(app.catalog.initialEyePosition, Quaternion.Euler(app.catalog.initialEulerAngles)); });
            Heading(content, "BENCHMARK");
            ToggleRow(content, "Nearby collision proxy probes", app.Streamer.CollisionProxiesEnabled, v => app.Streamer.CollisionProxiesEnabled = v);
            ActionRow(content, "Run 20 minute route at current speed", () => { app.Benchmark.StartBenchmark(app.Flight.Speed, 1200f, null, app.Streamer.CollisionProxiesEnabled); ShowDrawer(false); });
            ActionRow(content, "Finish and save current benchmark", () => app.Benchmark.Finish());
            ActionRow(content, "Close settings", () => ShowDrawer(false));
        }

        private void BuildSetup(Transform parent)
        {
            var overlay = Rect("First Physical Setup", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            setup = overlay.gameObject; overlay.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, .7f);
            var card = Rect("Calibration Card", overlay, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(550, 420));
            card.gameObject.AddComponent<UnityEngine.UI.Image>().color = Panel;
            var content = Rect("Setup Content", card, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); content.offsetMin = new Vector2(32, 28); content.offsetMax = new Vector2(-32, -28);
            var layout = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.spacing = 12; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
            Heading(content, "YOUR DISPLAY"); TextRow(content, "Measure the visible screen and your eye distance. Values are in centimetres.", 52);
            float width = app.Display.Calibration.screenWidth * 100f, height = app.Display.Calibration.screenHeight * 100f, distance = app.Display.Calibration.referenceEyeDistanceFromScreen * 100f;
            NumericRow(content, "Screen width", width, value => width = value); NumericRow(content, "Screen height", height, value => height = value); NumericRow(content, "Eye distance", distance, value => distance = value);
            var messageRow = Row(content, 36); message = Text(messageRow, "", 14, 0, 0, 480, 36); Stretch(message.rectTransform);
            ActionRow(content, "Enter Manhattan", () =>
            {
                if (!ViewerPersistence.ValidatePhysical(width / 100f, height / 100f, distance / 100f)) { message.text = "Enter a valid screen size and 25–150 cm viewing distance."; return; }
                var c = app.Display.Calibration; c.screenWidth = width / 100f; c.screenHeight = height / 100f; c.referenceEyeDistanceFromScreen = distance / 100f;
                app.UpdateCalibration();
                if (!ViewerPersistence.Save(ViewerPersistence.CalibrationFile, c)) { message.text = "Calibration could not be saved. Check the player log."; return; }
                setup.SetActive(false); app.KeyboardInput.InputEnabled = true;
            });
        }

        private void OnEscape() { if (setup != null && setup.activeSelf) return; ShowDrawer(!drawer.activeSelf); }
        private void ShowDrawer(bool show)
        {
            if (setup != null && setup.activeSelf) return;
            drawer.SetActive(show); app.KeyboardInput.Capture(false);
            app.KeyboardInput.InputEnabled = !show && !app.Benchmark.Recording;
        }
        private void Update()
        {
            if (app == null || diagnostics == null || Time.unscaledTime < nextDiagnostics) return;
            nextDiagnostics = Time.unscaledTime + .25f;
            diagnostics.text = app.Preferences.diagnostics ? string.Format(CultureInfo.InvariantCulture,
                "{0:F1} ms  ·  {1} tiles / {2} pending  ·  stream {3:F2} ms\nCity ({4:F1}, {5:F1}, {6:F1})  ·  CPU {7:F0} MB  ·  Gfx {8:F0} MB\n{9}  ·  tracking {10}  ·  age {11:F0} ms{12}",
                app.Benchmark.CurrentFrameMs, app.Streamer.LoadedTiles, app.Streamer.PendingLoads, app.Streamer.FrameWorkMilliseconds,
                app.Flight.CityPosition.x, app.Flight.CityPosition.y, app.Flight.CityPosition.z,
                app.Benchmark.CpuMemoryBytes / 1048576f, app.Benchmark.GpuMemoryBytes / 1048576f,
                app.Display.SourceStatus, app.Display.IsTracking, app.Display.ResultAgeMilliseconds,
                app.Benchmark.Recording ? "\nRecording benchmark…" : "") : "";
        }

        private RectTransform Row(Transform parent, float height)
        {
            var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>(); row.SetParent(parent, false);
            var element = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); element.minHeight = height; element.preferredHeight = height; return row;
        }
        private void Heading(Transform parent, string title) { var row = Row(parent, 32); var label = Text(row, title, 19, 0, 0, 380, 32); label.color = Accent; Stretch(label.rectTransform); }
        private void TextRow(Transform parent, string text, float height) { var row = Row(parent, height); var label = Text(row, text, 14, 0, 0, 380, height); Stretch(label.rectTransform); }
        private void ActionRow(Transform parent, string title, Action action) => Button(Row(parent, 38), title, action);
        private void NumericRow(Transform parent, string title, float current, Action<float> changed)
        {
            var row = Row(parent, 36); var label = Text(row, title, 15, 0, 0, 252, 36); label.alignment = TextAnchor.MiddleLeft;
            var inputRect = Rect("Value", row, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(0, 0), new Vector2(118, 34)); inputRect.pivot = new Vector2(1, .5f);
            inputRect.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.12f, .16f, .21f);
            var input = inputRect.gameObject.AddComponent<UnityEngine.UI.InputField>(); input.contentType = UnityEngine.UI.InputField.ContentType.DecimalNumber;
            var text = Text(inputRect, "", 16, 6, 0, 106, 34); text.alignment = TextAnchor.MiddleLeft; Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(8, 0); text.rectTransform.offsetMax = new Vector2(-8, 0);
            input.textComponent = text; input.text = current.ToString("0.###", CultureInfo.InvariantCulture);
            input.onEndEdit.AddListener(value => { if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed)) changed(parsed); });
        }
        private void ToggleRow(Transform parent, string title, bool current, Action<bool> changed)
        {
            bool value = current; UnityEngine.UI.Text label = null;
            var button = Button(Row(parent, 36), title + (value ? "  ON" : "  OFF"), () => { value = !value; changed(value); label.text = title + (value ? "  ON" : "  OFF"); });
            label = button.GetComponentInChildren<UnityEngine.UI.Text>();
        }
        private UnityEngine.UI.Button Button(Transform parent, string title, Action clicked)
        {
            var rect = Rect("Button", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.13f, .21f, .27f);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            var label = Text(rect, title, 15, 0, 0, 300, 36); Stretch(label.rectTransform); label.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(() => clicked()); return button;
        }
        private UnityEngine.UI.Text Text(Transform parent, string value, int size, float x, float y, float width, float height)
        {
            var rect = Rect("Text", parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, y), new Vector2(width, height)); rect.pivot = new Vector2(0, 1);
            var label = rect.gameObject.AddComponent<UnityEngine.UI.Text>(); label.text = value; label.font = font; label.fontSize = size; label.color = Ink; label.raycastTarget = false; return label;
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private void OnDestroy() { if (app != null && app.KeyboardInput != null) app.KeyboardInput.EscapePressed -= OnEscape; }
    }
}
