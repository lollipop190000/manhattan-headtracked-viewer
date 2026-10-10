using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class ViewerViewportRulerTests
{
    [TestCase(1060, 600, .53f, .30f, 200f, 200f)]
    [TestCase(800, 450, .40f, .225f, 200f, 200f)]
    [TestCase(2880, 1800, .53f, .30f, 543.3962f, 600f)]
    public void TenCentimetersUsesEachViewportAxisIndependently(int width, int height, float metersX, float metersY, float expectedX, float expectedY)
    {
        Assert.That(ViewerViewportRuler.TryCalculate(new Rect(0, 0, width, height), new Vector2(metersX, metersY), out var layout), Is.True);
        Assert.That(layout.HorizontalPixels, Is.EqualTo(expectedX).Within(.001f));
        Assert.That(layout.VerticalPixels, Is.EqualTo(expectedY).Within(.001f));
        // A mismatched pixel/physical aspect must remain measurable, never silently corrected.
    }

    [Test]
    public void OffsetViewportKeepsBothRulersInsideItsContentArea()
    {
        var viewport = new Rect(80, 40, 800, 450);
        Assert.That(ViewerViewportRuler.TryCalculate(viewport, new Vector2(.40f, .225f), out var layout), Is.True);
        Assert.That(layout.HorizontalStart.x, Is.EqualTo(380).Within(.001f));
        Assert.That(layout.HorizontalEnd.x, Is.EqualTo(580).Within(.001f));
        Assert.That(layout.HorizontalStart.y, Is.EqualTo(197.5f).Within(.001f));
        Assert.That(layout.HorizontalEnd.y, Is.EqualTo(197.5f).Within(.001f));
        Assert.That(layout.VerticalStart, Is.EqualTo(new Vector2(320, 165)));
        Assert.That(layout.VerticalEnd, Is.EqualTo(new Vector2(320, 365)));
        foreach (var point in new[] { layout.HorizontalStart, layout.HorizontalEnd, layout.VerticalStart, layout.VerticalEnd })
            Assert.That(viewport.Contains(point), Is.True, "Ruler endpoint must be within camera content, not full display coordinates.");
    }

    [TestCase(0, .30f)]
    [TestCase(.53f, 0)]
    [TestCase(-.53f, .30f)]
    [TestCase(.09f, .30f)]
    [TestCase(float.NaN, .30f)]
    [TestCase(.53f, float.PositiveInfinity)]
    public void InvalidOrTooSmallPhysicalSizeDoesNotCreateAMislabelledRuler(float width, float height)
    {
        Assert.That(ViewerViewportRuler.TryCalculate(new Rect(0, 0, 1060, 600), new Vector2(width, height), out _), Is.False);
    }

    [Test]
    public void EmptyOrInvalidViewportDoesNotProduceRulers()
    {
        foreach (var viewport in new[] { new Rect(0, 0, 0, 600), new Rect(0, 0, 1060, -1), new Rect(float.NaN, 0, 1060, 600) })
            Assert.That(ViewerViewportRuler.TryCalculate(viewport, new Vector2(.53f, .30f), out _), Is.False);
    }

    [TestCase(.55f)]
    [TestCase(2f)]
    public void ActualCanvasConversionRetainsPixelLengthAcrossScalingAndViewportResize(float scale)
    {
        var owner = new GameObject("Owned ruler test canvas", typeof(RectTransform), typeof(Canvas));
        var cameraObject = new GameObject("Owned ruler test camera", typeof(Camera));
        try
        {
            var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.scaleFactor = scale;
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.pixelRect = new Rect(80, 40, 800, 450);
            var calibration = new DisplayCalibration { screenWidth = .40f, screenHeight = .225f, deriveScreenHeightFromResolution = false };
            string unchanged = JsonUtility.ToJson(calibration);
            var ruler = new GameObject("Owned ruler test", typeof(RectTransform)).AddComponent<ViewerViewportRuler>();
            ruler.Initialize(canvas, camera, calibration, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            Assert.That(ruler.Visible, Is.False, "Explicit opt-in only.");
            AssertActivation(ruler, false);
            Canvas.ForceUpdateCanvases(); ruler.SetVisible(true); Canvas.ForceUpdateCanvases(); ruler.RefreshLayout();
            AssertActivation(ruler, true);
            // Camera stores a normalized rect; Editor can resize the Game View during this test.
            // Compare actual screen endpoints to this frame's actual content viewport, not the requested rect.
            AssertLength(ruler.HorizontalBar, true, camera.pixelRect.width / 4f);
            AssertLength(ruler.VerticalBar, false, camera.pixelRect.height * (4f / 9f));
            AssertScreenEndpoints(ruler, camera.pixelRect);
            canvas.scaleFactor = scale * 1.7f; camera.pixelRect = new Rect(24, 10, 400, 225);
            Canvas.ForceUpdateCanvases(); ruler.RefreshLayout(); Canvas.ForceUpdateCanvases();
            AssertLength(ruler.HorizontalBar, true, camera.pixelRect.width / 4f);
            AssertLength(ruler.VerticalBar, false, camera.pixelRect.height * (4f / 9f));
            AssertScreenEndpoints(ruler, camera.pixelRect);
            ruler.SetVisible(false);
            AssertActivation(ruler, false);
            ruler.SetVisible(true); Canvas.ForceUpdateCanvases(); ruler.RefreshLayout();
            AssertActivation(ruler, true); AssertScreenEndpoints(ruler, camera.pixelRect);
            ruler.SetVisible(false); AssertActivation(ruler, false);
            Assert.That(JsonUtility.ToJson(calibration), Is.EqualTo(unchanged), "Toggling and resizing must not mutate calibration.");
            StringAssert.Contains("Measurement aid only", ruler.Describe());
            StringAssert.Contains("no values changed or saved", ruler.Describe());
            StringAssert.Contains("awaiting URP camera frame", ruler.Describe());
        }
        finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(cameraObject); }
    }

    [Test]
    public void ActualScaleWithScreenSizeComponentPreservesOffsetEndpointsAcrossAspectChanges()
    {
        var owner = new GameObject("Owned CanvasScaler test", typeof(RectTransform), typeof(Canvas));
        var cameraObject = new GameObject("Owned CanvasScaler camera", typeof(Camera));
        try
        {
            var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Use the installed real component without changing the existing test assembly references.
            var scalerType = System.Reflection.Assembly.Load("UnityEngine.UI").GetType("UnityEngine.UI.CanvasScaler");
            Assert.That(scalerType, Is.Not.Null);
            var scaler = owner.AddComponent(scalerType);
            var mode = scalerType.GetProperty("uiScaleMode");
            mode.SetValue(scaler, System.Enum.Parse(mode.PropertyType, "ScaleWithScreenSize"));
            scalerType.GetProperty("matchWidthOrHeight").SetValue(scaler, .5f);
            scalerType.GetProperty("referenceResolution").SetValue(scaler, new Vector2(1920, 1200));
            // This uGUI version updates via Canvas.preWillRenderCanvases, not MonoBehaviour.Update.
            Canvas.ForceUpdateCanvases();
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.rect = new Rect(.1f, .2f, .8f, .7f);
            var calibration = new DisplayCalibration { screenWidth = .40f, screenHeight = .225f, deriveScreenHeightFromResolution = false };
            string unchanged = JsonUtility.ToJson(calibration);
            var ruler = new GameObject("Owned CanvasScaler ruler", typeof(RectTransform)).AddComponent<ViewerViewportRuler>();
            ruler.Initialize(canvas, camera, calibration, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            ruler.SetVisible(true); Canvas.ForceUpdateCanvases(); ruler.RefreshLayout();
            AssertActivation(ruler, true); AssertScreenEndpoints(ruler, camera.pixelRect);
            float firstScale = canvas.scaleFactor;
            // Change both content viewport dimensions/aspect and the real scaler's reference aspect.
            camera.rect = new Rect(.2f, .1f, .5f, .8f);
            scalerType.GetProperty("referenceResolution").SetValue(scaler, new Vector2(600, 900));
            Canvas.ForceUpdateCanvases();
            Canvas.ForceUpdateCanvases(); ruler.RefreshLayout();
            Assert.That(canvas.scaleFactor, Is.Not.EqualTo(firstScale).Within(.001f));
            AssertActivation(ruler, true); AssertScreenEndpoints(ruler, camera.pixelRect);
            AssertLength(ruler.HorizontalBar, true, camera.pixelRect.width / 4f);
            AssertLength(ruler.VerticalBar, false, camera.pixelRect.height * (4f / 9f));
            ruler.SetVisible(false); AssertActivation(ruler, false);
            Assert.That(JsonUtility.ToJson(calibration), Is.EqualTo(unchanged));
        }
        finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(cameraObject); }
    }

    [Test]
    public void OffscreenTargetCannotBeClaimedAsAMeasurableDisplayRuler()
    {
        var owner = new GameObject("Owned offscreen ruler canvas", typeof(RectTransform), typeof(Canvas));
        var cameraObject = new GameObject("Owned offscreen camera", typeof(Camera));
        var target = new RenderTexture(1060, 600, 24);
        try
        {
            var canvas = owner.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.targetTexture = target;
            var ruler = new GameObject("Owned offscreen ruler", typeof(RectTransform)).AddComponent<ViewerViewportRuler>();
            ruler.Initialize(canvas, camera, new DisplayCalibration { screenWidth = .53f, screenHeight = .30f }, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            ruler.SetVisible(true);
            Assert.That(ruler.HorizontalBar.gameObject.activeInHierarchy, Is.False);
            StringAssert.Contains("offscreen target", ruler.Describe());
        }
        finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(target); }
    }

    private static void AssertLength(RectTransform rect, bool horizontal, float expectedPixels)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        var first = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        var last = RectTransformUtility.WorldToScreenPoint(null, horizontal ? corners[3] : corners[1]);
        Assert.That(Vector2.Distance(first, last), Is.EqualTo(expectedPixels).Within(.1f));
    }

    private static void AssertActivation(ViewerViewportRuler ruler, bool expected)
    {
        Assert.That(ruler.Visible, Is.EqualTo(expected));
        Assert.That(ruler.HorizontalBar.gameObject.activeInHierarchy, Is.EqualTo(expected));
        Assert.That(ruler.VerticalBar.gameObject.activeInHierarchy, Is.EqualTo(expected));
    }

    private static void AssertScreenEndpoints(ViewerViewportRuler ruler, Rect viewport)
    {
        var h = new Vector3[4]; var v = new Vector3[4];
        ruler.HorizontalBar.GetWorldCorners(h); ruler.VerticalBar.GetWorldCorners(v);
        var actual = new[] { (h[0] + h[1]) * .5f, (h[2] + h[3]) * .5f, (v[0] + v[3]) * .5f, (v[1] + v[2]) * .5f };
        // For 40 x 22.5 cm: a 10 cm span is 1/4 width and 4/9 height.
        var expected = new[] {
            new Vector2(viewport.xMin + viewport.width * .375f, viewport.yMin + viewport.height * .35f),
            new Vector2(viewport.xMin + viewport.width * .625f, viewport.yMin + viewport.height * .35f),
            new Vector2(viewport.xMin + viewport.width * .30f, viewport.yMin + viewport.height * (5f / 18f)),
            new Vector2(viewport.xMin + viewport.width * .30f, viewport.yMin + viewport.height * (13f / 18f)) };
        for (int i = 0; i < actual.Length; i++)
            Assert.That(Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, actual[i]), expected[i]), Is.LessThan(.1f), "Actual screen endpoint " + i);
    }
}
