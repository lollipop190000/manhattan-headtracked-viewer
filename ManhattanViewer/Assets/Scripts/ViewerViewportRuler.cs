using System.Globalization;
using HeadTracked.Display;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Manhattan.Viewer
{
    /// <summary>Manual measurement aid only. Never applies or persists calibration.</summary>
    public sealed class ViewerViewportRuler : MonoBehaviour
    {
        public struct Layout
        {
            public Vector2 HorizontalStart, HorizontalEnd, VerticalStart, VerticalEnd;
            public float HorizontalPixels => HorizontalEnd.x - HorizontalStart.x;
            public float VerticalPixels => VerticalEnd.y - VerticalStart.y;
        }

        private Camera camera;
        private Canvas canvas;
        private DisplayCalibration calibration;
        private RectTransform host, overlay, horizontal, vertical;
        private readonly RectTransform[] horizontalTicks = new RectTransform[11], verticalTicks = new RectTransform[11];
        private UnityEngine.UI.Text horizontalLabel, verticalLabel;
        private Vector2Int renderedPixels;
        private Rect renderedViewport;
        private int renderedFrame = -10;
        private static readonly int ScaledScreenParams = Shader.PropertyToID("_ScaledScreenParams");
        public bool Visible { get; private set; }
        private Vector2 PhysicalSize
        {
            get
            {
                var display = camera != null ? camera.GetComponent<HeadTrackedDisplay>() : null;
                if (display == null || !float.IsFinite(calibration.screenWidth) || !float.IsFinite(calibration.screenHeight) ||
                    calibration.screenWidth <= 0 || calibration.screenHeight <= 0)
                    return new Vector2(calibration.screenWidth, calibration.screenHeight);
                Rect aperture = display.ProjectionScreenRectMeters;
                return aperture.size;
            }
        }
        public RectTransform HorizontalBar => horizontal;
        public RectTransform VerticalBar => vertical;

        public void Initialize(Canvas owner, Camera contentCamera, DisplayCalibration appliedCalibration, Font font)
        {
            canvas = owner; camera = contentCamera; calibration = appliedCalibration;
            host = GetComponent<RectTransform>(); host.SetParent(owner.transform, false);
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = host.offsetMax = Vector2.zero;
            overlay = Rect("Manual measurement rulers");
            overlay.anchorMin = Vector2.zero; overlay.anchorMax = Vector2.one; overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            horizontal = Line("Horizontal 10 cm", overlay); vertical = Line("Vertical 10 cm", overlay);
            for (int i = 0; i <= 10; i++)
            {
                horizontalTicks[i] = Line("Horizontal tick " + i, overlay);
                verticalTicks[i] = Line("Vertical tick " + i, overlay);
            }
            horizontalLabel = Label("Horizontal label", overlay, font);
            verticalLabel = Label("Vertical label", overlay, font);
            overlay.gameObject.SetActive(false);
        }

        public void SetVisible(bool value) { Visible = value; RefreshLayout(); }

        public static bool TryCalculate(Rect viewport, Vector2 physicalMeters, out Layout result)
        {
            result = default;
            if (!Finite(viewport.x) || !Finite(viewport.y) || !Positive(viewport.width) || !Positive(viewport.height) ||
                !Positive(physicalMeters.x) || !Positive(physicalMeters.y) || physicalMeters.x < .10f || physicalMeters.y < .10f) return false;
            float width = viewport.width * (.10f / physicalMeters.x), height = viewport.height * (.10f / physicalMeters.y);
            var h = new Vector2(viewport.center.x, viewport.yMin + viewport.height * .35f);
            var v = new Vector2(viewport.xMin + viewport.width * .30f, viewport.center.y);
            result.HorizontalStart = h - Vector2.right * width * .5f;
            result.HorizontalEnd = h + Vector2.right * width * .5f;
            result.VerticalStart = v - Vector2.up * height * .5f;
            result.VerticalEnd = v + Vector2.up * height * .5f;
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Positive(float value) => Finite(value) && value > 0f;
        private bool Onscreen => camera != null && canvas != null && camera.targetTexture == null &&
            canvas.renderMode == RenderMode.ScreenSpaceOverlay && camera.targetDisplay == canvas.targetDisplay;

        public void RefreshLayout()
        {
            if (overlay == null) return;
            Layout layout = default;
            bool valid = Onscreen && calibration != null && TryCalculate(camera.pixelRect,
                PhysicalSize, out layout);
            overlay.gameObject.SetActive(Visible && valid);
            if (!Visible || !valid) return;
            PixelRect(horizontal, new Rect(layout.HorizontalStart.x, layout.HorizontalStart.y - 1, layout.HorizontalPixels, 2));
            PixelRect(vertical, new Rect(layout.VerticalStart.x - 1, layout.VerticalStart.y, 2, layout.VerticalPixels));
            for (int i = 0; i <= 10; i++)
            {
                float tick = i % 5 == 0 ? 14 : 8;
                var h = Vector2.Lerp(layout.HorizontalStart, layout.HorizontalEnd, i / 10f);
                var v = Vector2.Lerp(layout.VerticalStart, layout.VerticalEnd, i / 10f);
                PixelRect(horizontalTicks[i], new Rect(h.x - 1, h.y - tick * .5f, 2, tick));
                PixelRect(verticalTicks[i], new Rect(v.x - tick * .5f, v.y - 1, tick, 2));
            }
            float scale = canvas.scaleFactor;
            PixelRect(horizontalLabel.rectTransform, new Rect((layout.HorizontalStart.x + layout.HorizontalEnd.x) * .5f - 60 * scale,
                layout.HorizontalStart.y - 30 * scale, 120 * scale, 24 * scale));
            PixelRect(verticalLabel.rectTransform, new Rect(layout.VerticalStart.x + 14 * scale,
                (layout.VerticalStart.y + layout.VerticalEnd.y) * .5f - 12 * scale, 120 * scale, 24 * scale));
        }

        // Convert both endpoints rather than assuming CanvasScaler settings or a full-screen camera.
        private void PixelRect(RectTransform rect, Rect pixels)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(host, pixels.min, null, out Vector2 min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(host, pixels.max, null, out Vector2 max);
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = Vector2.zero;
            rect.anchoredPosition = min - host.rect.min; rect.sizeDelta = max - min;
        }

        public string Describe()
        {
            if (camera == null || calibration == null) return "Viewport check unavailable: camera or calibration missing.";
            Rect viewport = camera.pixelRect;
            bool fresh = renderedFrame >= Time.frameCount - 2 && renderedViewport == viewport;
            string render = fresh ? string.Format(CultureInfo.InvariantCulture, "{0} x {1} px (URP frame)", renderedPixels.x, renderedPixels.y) : "awaiting URP camera frame";
            string note = !Onscreen ? "Rulers unavailable: offscreen target or different display." :
                !TryCalculate(viewport, PhysicalSize, out _) ? "Rulers unavailable: invalid size or size below 10 cm." : "Measure both 10 cm rulers with a real ruler.";
            return string.Format(CultureInfo.InvariantCulture,
                "Camera viewport: ({0:0}, {1:0}) {2:0} x {3:0} px\nRender: {4}\nAspect: viewport {5:0.0000} / applied {6:0.0000}\nApplied size: {7:0.##} x {8:0.##} cm\n{9}\nMeasurement aid only; no values changed or saved.",
                viewport.x, viewport.y, viewport.width, viewport.height, render,
                viewport.height > 0 ? viewport.width / viewport.height : 0,
                PhysicalSize.y > 0 ? PhysicalSize.x / PhysicalSize.y : 0,
                PhysicalSize.x * 100, PhysicalSize.y * 100, note);
        }

        private void OnCameraRendered(ScriptableRenderContext context, Camera renderedCamera)
        {
            if (renderedCamera != camera || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            // URP ScriptableRenderer sets this from cameraTargetDescriptor, including dynamic scaling.
            Vector4 size = Shader.GetGlobalVector(ScaledScreenParams);
            if (!Positive(size.x) || !Positive(size.y)) return;
            renderedPixels = new Vector2Int(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
            renderedViewport = camera.pixelRect; renderedFrame = Time.frameCount;
        }
        private void LateUpdate() => RefreshLayout();
        private void OnEnable() => RenderPipelineManager.endCameraRendering += OnCameraRendered;
        private void OnDisable()
        {
            RenderPipelineManager.endCameraRendering -= OnCameraRendered;
            if (overlay != null) overlay.gameObject.SetActive(false);
        }
        private RectTransform Rect(string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(host, false); return rect;
        }
        private static RectTransform Line(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.27f, .79f, .67f); image.raycastTarget = false; return rect;
        }
        private static UnityEngine.UI.Text Label(string name, Transform parent, Font font)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            var label = rect.gameObject.AddComponent<UnityEngine.UI.Text>(); label.font = font; label.fontSize = 14;
            label.text = "10 cm (applied)"; label.color = new Color(.91f, .95f, .99f); label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false; return label;
        }
    }
}
