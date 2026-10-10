using System.Globalization;
using HeadTracked.Display;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Read-only labels for the package's public state; never captures or saves calibration.</summary>
    public static class ViewerTrackingDiagnostics
    {
        public static string Format(HeadTrackedDisplay display)
        {
            var c = display.Calibration;
            return SingleLine(display.SourceStatus) + "\n" +
                FormatTrackingState(display.TrackingEnabled, display.HasFreshEyeEstimate, display.IsTracking) +
                "  ·  age " + FormatAge(display.HasObservation, display.ResultAgeMilliseconds) + "\n" +
                "Depth lock " + (display.FreezeViewingDistance ? "ON" : "OFF") +
                "  ·  filter " + (display.UseAdaptiveFilter ? "adaptive" : "exponential") + "\n" +
                "Estimator: " + FormatEstimator(c, display.LatestObservation, display.HasFreshEyeEstimate) + "\n" +
                "Eye raw (m): " + FormatEye(display.UncalibratedEyePositionMeters, display.HasFreshEyeEstimate) +
                "  ·  input (m): " + FormatEye(display.EstimatedEyePositionMeters, display.IsTracking) + "\n" +
                "Eye rendered (m): " + FormatEye(display.EyePositionMeters, true) + "  ·  screen-local XYZ, viewer Z < 0\n" +
                FormatPhysicalReference(c) + "\n" + FormatEyeSpanReference(c) +
                (UsesBasicFixedDepth(c, display.LatestObservation, display.HasFreshEyeEstimate)
                    ? "\nBasic fallback uses fixed camera depth. Capture a face reference for distance response." : "");
        }

        public static string FormatTrackingState(bool enabled, bool freshEstimate, bool validInput) =>
            "Tracking " + (enabled ? "ON" : "OFF (render frozen)") +
            "  ·  eye " + (freshEstimate ? "fresh" : "unavailable") +
            "  ·  input " + (validInput ? "valid" : "invalid");

        public static string FormatAge(bool hasObservation, float milliseconds) =>
            hasObservation && float.IsFinite(milliseconds) && milliseconds >= 0f
                ? milliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms" : "unavailable";

        public static string FormatEye(Vector3 eye, bool available) =>
            available && float.IsFinite(eye.x) && float.IsFinite(eye.y) && float.IsFinite(eye.z)
                ? string.Format(CultureInfo.InvariantCulture, "({0:F3}, {1:F3}, {2:F3})", eye.x, eye.y, eye.z)
                : "unavailable";

        public static string FormatEstimator(DisplayCalibration c, HeadObservation observation, bool freshEstimate)
        {
            // An old/default observation cannot establish the current source's mode.
            if (c == null || !freshEstimate) return "unknown (no fresh eye estimate)";
            if (c.useRigidFacePose && observation.poseSupported) return "rigid face pose";
            if (c.usePreciseIntrinsics && c.focalXPixels > 1f) return "eye landmarks / precise intrinsics";
            return HasEyeSpanReference(c) ? "eye landmarks / basic reference" : "eye landmarks / basic fixed-depth fallback";
        }

        public static bool UsesBasicFixedDepth(DisplayCalibration c, HeadObservation observation, bool freshEstimate) =>
            c != null && freshEstimate && !(c.useRigidFacePose && observation.poseSupported) &&
            !(c.usePreciseIntrinsics && c.focalXPixels > 1f) && !HasEyeSpanReference(c);

        public static string FormatPhysicalReference(DisplayCalibration c) => c == null ? "Physical calibration unavailable" :
            string.Format(CultureInfo.InvariantCulture, "Screen {0:F1} x {1:F1} cm  ·  reference eye distance {2:F1} cm",
                c.screenWidth * 100f, c.screenHeight * 100f, c.referenceEyeDistanceFromScreen * 100f);

        public static string FormatEyeSpanReference(DisplayCalibration c) => HasEyeSpanReference(c)
            ? string.Format(CultureInfo.InvariantCulture, "Basic eye-span reference: {0:F1} px at {1} px frame width",
                c.referenceEyeSpanPixels, c.referenceFrameWidth)
            : "Basic eye-span reference: absent (not required for rigid pose / precise intrinsics)";

        public static string FormatCaptureResult(bool captured, bool saved)
        {
            if (!captured) return "Reference capture failed. Check tracking and face landmarks, then retry. No file was saved.";
            return saved ? "Reference captured and saved." :
                "Reference captured for this session, but could not be saved. Check the player log.";
        }

        private static bool HasEyeSpanReference(DisplayCalibration c) =>
            c != null && c.referenceEyeSpanPixels > 1f && c.referenceFrameWidth > 0;

        private static string SingleLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return "Tracking source status unavailable";
            string line = value.Replace('\r', ' ').Replace('\n', ' ');
            return line.Length > 100 ? line.Substring(0, 97) + "..." : line;
        }
    }
}
