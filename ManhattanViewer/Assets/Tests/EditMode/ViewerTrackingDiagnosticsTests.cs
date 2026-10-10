using System.Globalization;
using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class ViewerTrackingDiagnosticsTests
{
    [Test]
    public void NearDiagnosticsDistinguishLoadedActiveFlagsAndUnavailableOpacity()
    {
        string empty = ViewerTrackingDiagnostics.FormatNear(default);
        StringAssert.Contains("Near 0 loaded / 0 active", empty);
        StringAssert.Contains("Nearest loaded Near bounds: unavailable", empty);
        StringAssert.Contains("opacity: unavailable", empty);
        StringAssert.Contains("Catalog: unavailable", empty);
        var state = new CityStreamer.NearDiagnosticState { sourceVersion = "city-v2", loadedTiles = 7,
            activeTiles = 4, activeRenderers = 9, visibleRendererFlags = 5, nearestLoadedTile = "Near_test",
            nearestLoadedBoundsDistance = 25.5f, minimumOpacity = 0, maximumOpacity = .25f,
            opacityAvailable = true, surfaceDetailsEnabled = true };
        string text = ViewerTrackingDiagnostics.FormatNear(state);
        StringAssert.Contains("7 loaded / 4 active", text);
        StringAssert.Contains("25.5 m (Near_test)", text);
        StringAssert.Contains("0.000..0.250", text);
        StringAssert.Contains("visibility flags (pixels unverified)", text);
        StringAssert.Contains("additive window trim", text);
        StringAssert.Contains("Surface ON", text);
        state.surfaceDetailsEnabled = false;
        StringAssert.Contains("Surface OFF", ViewerTrackingDiagnostics.FormatNear(state));
    }

    [TestCase(float.NaN, 1f)] [TestCase(0f, float.PositiveInfinity)]
    [TestCase(-1f, 1f)] [TestCase(1f, 0f)] [TestCase(0f, 1.1f)]
    public void InvalidNearOpacityRangeIsUnavailable(float minimum, float maximum)
    {
        var state = new CityStreamer.NearDiagnosticState { opacityAvailable = true, minimumOpacity = minimum, maximumOpacity = maximum };
        StringAssert.Contains("opacity: unavailable", ViewerTrackingDiagnostics.FormatNear(state));
    }

    [Test]
    public void EnabledDoesNotClaimFreshOrValidObservations()
    {
        string status = ViewerTrackingDiagnostics.FormatTrackingState(true, false, false);
        StringAssert.Contains("Tracking ON", status);
        StringAssert.Contains("eye unavailable", status);
        StringAssert.Contains("input invalid", status);
    }

    [Test]
    public void FrozenRenderingCanStillHaveFreshValidObservations()
    {
        string status = ViewerTrackingDiagnostics.FormatTrackingState(false, true, true);
        StringAssert.Contains("OFF (render frozen)", status);
        StringAssert.Contains("eye fresh", status);
        StringAssert.Contains("input valid", status);
    }

    [Test]
    public void FreshRawEstimateDoesNotImplyAcceptedProjectionInput()
    {
        string status = ViewerTrackingDiagnostics.FormatTrackingState(true, true, false);
        StringAssert.Contains("eye fresh", status);
        StringAssert.Contains("input invalid", status);
    }

    [Test]
    public void NoObservationDoesNotShowZeroMilliseconds()
    {
        Assert.That(ViewerTrackingDiagnostics.FormatAge(false, 0f), Is.EqualTo("unavailable"));
        Assert.That(ViewerTrackingDiagnostics.FormatAge(true, 0f), Is.EqualTo("0 ms"));
        Assert.That(ViewerTrackingDiagnostics.FormatAge(true, 315f), Is.EqualTo("315 ms"));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-1f)]
    public void InvalidAgeIsUnavailable(float age)
    {
        Assert.That(ViewerTrackingDiagnostics.FormatAge(true, age), Is.EqualTo("unavailable"));
    }

    [Test]
    public void EyeAndPhysicalValuesUseInvariantUnitsAndCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.That(ViewerTrackingDiagnostics.FormatEye(new Vector3(.125f, -.25f, -.6f), true),
                Is.EqualTo("(0.125, -0.250, -0.600)"));
            Assert.That(ViewerTrackingDiagnostics.FormatPhysicalReference(new DisplayCalibration()),
                Is.EqualTo("Screen 53.0 x 30.0 cm  ·  reference eye distance 60.0 cm"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Test]
    public void MissingOrNonfiniteEyeIsNotPresentedAsAMeasurement()
    {
        Assert.That(ViewerTrackingDiagnostics.FormatEye(new Vector3(0, 0, -.6f), false), Is.EqualTo("unavailable"));
        foreach (Vector3 eye in new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0),
            new Vector3(0, 0, float.NegativeInfinity) })
            Assert.That(ViewerTrackingDiagnostics.FormatEye(eye, true), Is.EqualTo("unavailable"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OldObservationCannotEstablishCurrentModeOrShowFallbackWarning(bool poseSupported)
    {
        var c = new DisplayCalibration();
        var o = Observation(63f); o.poseSupported = poseSupported;
        Assert.That(ViewerTrackingDiagnostics.FormatEstimator(c, o, false), Is.EqualTo("unknown (no fresh eye estimate)"));
        Assert.That(ViewerTrackingDiagnostics.UsesBasicFixedDepth(c, o, false), Is.False);
    }

    [TestCase(true, true, false, 0f, 0, "rigid face pose", false)]
    [TestCase(false, true, false, 0f, 0, "eye landmarks / basic fixed-depth fallback", true)]
    [TestCase(true, false, false, 0f, 0, "eye landmarks / basic fixed-depth fallback", true)]
    [TestCase(true, false, true, 0f, 0, "eye landmarks / precise intrinsics", false)]
    [TestCase(true, false, false, 63f, 640, "eye landmarks / basic reference", false)]
    [TestCase(true, false, false, 63f, 0, "eye landmarks / basic fixed-depth fallback", true)]
    public void ModeMatchesPackageBranchSelection(bool useRigid, bool poseSupported, bool precise,
        float referenceSpan, int referenceWidth, string expected, bool warning)
    {
        var c = new DisplayCalibration { useRigidFacePose = useRigid, usePreciseIntrinsics = precise,
            focalXPixels = 600f, referenceEyeSpanPixels = referenceSpan, referenceFrameWidth = referenceWidth };
        var o = Observation(63f); o.poseSupported = poseSupported;
        Assert.That(ViewerTrackingDiagnostics.FormatEstimator(c, o, true), Is.EqualTo(expected));
        Assert.That(ViewerTrackingDiagnostics.UsesBasicFixedDepth(c, o, true), Is.EqualTo(warning));
    }

    [Test]
    public void PreciseToggleWithoutUsableFxStillUsesBasicFallback()
    {
        var c = new DisplayCalibration { usePreciseIntrinsics = true, focalXPixels = 1f };
        var o = Observation(63f);
        Assert.That(ViewerTrackingDiagnostics.UsesBasicFixedDepth(c, o, true), Is.True);
    }

    [Test]
    public void FixedDepthWarningAgreesWithRealBasicEstimatorBeforeAndAfterReferenceCapture()
    {
        var c = new DisplayCalibration { webcamPosition = Vector3.zero, mirrorImageX = false };
        var near = Observation(63f);
        var far = Observation(42f);
        Assert.That(EyePoseEstimator.TryEstimateUncalibrated(near, c, out Vector3 beforeNear), Is.True);
        Assert.That(EyePoseEstimator.TryEstimateUncalibrated(far, c, out Vector3 beforeFar), Is.True);
        Assert.That(beforeFar.z, Is.EqualTo(beforeNear.z).Within(.0001f));
        Assert.That(ViewerTrackingDiagnostics.UsesBasicFixedDepth(c, near, true), Is.True);
        // This test owns the calibration object; runtime diagnostics never perform this action.
        Assert.That(c.CaptureReference(near), Is.True);
        Assert.That(EyePoseEstimator.TryEstimateUncalibrated(near, c, out Vector3 afterNear), Is.True);
        Assert.That(EyePoseEstimator.TryEstimateUncalibrated(far, c, out Vector3 afterFar), Is.True);
        Assert.That(afterFar.z, Is.LessThan(afterNear.z - .1f));
        Assert.That(ViewerTrackingDiagnostics.UsesBasicFixedDepth(c, far, true), Is.False);
    }

    [Test]
    public void EyeSpanReferenceDescribesMemoryStateWithoutClaimingItWasSaved()
    {
        var c = new DisplayCalibration();
        StringAssert.Contains("absent", ViewerTrackingDiagnostics.FormatEyeSpanReference(c));
        c.referenceEyeSpanPixels = 63f; c.referenceFrameWidth = 640;
        Assert.That(ViewerTrackingDiagnostics.FormatEyeSpanReference(c),
            Is.EqualTo("Basic eye-span reference: 63.0 px at 640 px frame width"));
    }

    [Test]
    public void CaptureFeedbackDistinguishesFailedCaptureFailedSaveAndPersistedSuccess()
    {
        string failed = ViewerTrackingDiagnostics.FormatCaptureResult(false, false);
        StringAssert.Contains("Reference capture failed.", failed);
        StringAssert.Contains("No file was saved.", failed);
        Assert.That(ViewerTrackingDiagnostics.FormatCaptureResult(false, true), Is.EqualTo(failed));
        string unsaved = ViewerTrackingDiagnostics.FormatCaptureResult(true, false);
        StringAssert.Contains("captured for this session", unsaved);
        StringAssert.Contains("could not be saved", unsaved);
        Assert.That(ViewerTrackingDiagnostics.FormatCaptureResult(true, true), Is.EqualTo("Reference captured and saved."));
    }

    [Test]
    public void FormattingAnUnstartedDisplayIsReadOnlyAndShowsUnavailableMeasurements()
    {
        var go = new GameObject("Diagnostics formatting test");
        try
        {
            var display = go.AddComponent<HeadTrackedDisplay>();
            string before = JsonUtility.ToJson(display.Calibration);
            string text = ViewerTrackingDiagnostics.Format(display);
            Assert.That(JsonUtility.ToJson(display.Calibration), Is.EqualTo(before));
            StringAssert.Contains("eye unavailable", text);
            StringAssert.Contains("age unavailable", text);
            StringAssert.Contains("unknown (no fresh eye estimate)", text);
            StringAssert.Contains("Eye raw (m): unavailable", text);
            StringAssert.DoesNotContain("Basic fallback uses fixed camera depth.", text);
        }
        finally { Object.DestroyImmediate(go); }
    }

    private static HeadObservation Observation(float spanPixels) => new HeadObservation
    {
        found = true, frameWidth = 640, frameHeight = 480, confidence = 1f,
        leftEye = new Vector2(.5f - spanPixels / 1280f, .5f),
        rightEye = new Vector2(.5f + spanPixels / 1280f, .5f)
    };
}
