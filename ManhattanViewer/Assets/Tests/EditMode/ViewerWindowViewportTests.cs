using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class ViewerWindowViewportTests
{
    [TestCase(0, 0, 1920, 1200, 0, 0, 1920, 1200)]
    [TestCase(0, 0, 1920, 1200, 240, 150, 960, 600)]
    [TestCase(-2560, 0, 2560, 1600, -2240, 200, 1280, 800)]
    [TestCase(0, -1200, 1920, 1200, 240, -1050, 960, 600)]
    public void DevicePixelMappingHandlesOffsetMonitorsAndIndependentRenderResolution(
        float mx, float my, float mw, float mh, float cx, float cy, float cw, float ch)
    {
        Assert.That(ViewerWindowViewport.TryNormalize(new Rect(mx, my, mw, mh), new Rect(cx, cy, cw, ch), out Rect surface), Is.True);
        Assert.That(surface.x, Is.EqualTo((cx - mx) / mw).Within(1e-6));
        Assert.That(surface.y, Is.EqualTo((my + mh - cy - ch) / mh).Within(1e-6));
        Rect aperture = PhysicalScreenViewport.Calculate(.4f, .25f, surface, new Rect(.1f, .2f, .7f, .6f));
        Assert.That(aperture.width, Is.EqualTo(.4f * cw / mw * .7f).Within(1e-6));
        Assert.That(aperture.height, Is.EqualTo(.25f * ch / mh * .6f).Within(1e-6));
    }

    [TestCase(-1, 0, 100, 100)]
    [TestCase(1850, 0, 100, 100)]
    [TestCase(0, 1150, 100, 100)]
    [TestCase(0, 0, 0, 100)]
    [TestCase(float.NaN, 0, 100, 100)]
    public void UnknownOrPartlyOffPanelClientIsRejected(float x, float y, float w, float h)
    {
        Assert.That(ViewerWindowViewport.TryNormalize(new Rect(0, 0, 1920, 1200), new Rect(x, y, w, h), out _), Is.False);
    }

    [Test]
    public void MappingModeRoundTripPreservesPhysicalCalibrationRigAndComparison()
    {
        var owner = new GameObject("Viewport mode test");
        try
        {
            var camera = owner.AddComponent<Camera>(); camera.enabled = false;
            var display = owner.AddComponent<HeadTrackedDisplay>(); display.enabled = false;
            display.TrackingEnabled = false;
            var position = owner.transform.position; var calibration = JsonUtility.ToJson(display.Calibration);
            var mapping = owner.AddComponent<ViewerWindowViewport>(); mapping.Configure(display);
            mapping.SetPanelMapping(true);
            Assert.That(mapping.MappingValid, Is.False, "Editor has no physical Player client mapping");
            mapping.SetPanelMapping(false);
            Assert.That(mapping.MappingValid, Is.True);
            Assert.That(display.DisplayViewport, Is.EqualTo(PhysicalScreenViewport.Full));
            Assert.That(display.TrackingEnabled, Is.False);
            Assert.That(owner.transform.position, Is.EqualTo(position));
            Assert.That(JsonUtility.ToJson(display.Calibration), Is.EqualTo(calibration));
        }
        finally { Object.DestroyImmediate(owner); }
    }
}
