using System;
using System.Globalization;
using System.IO;
using HeadTracked.Display;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Manhattan.Viewer
{
    /// <summary>Opt-in numeric diagnostics. Never starts a tracker, moves the rig, fits gain or saves calibration automatically.</summary>
    [DefaultExecutionOrder(11000)]
    public sealed class ViewerMeasurementWorkflow : MonoBehaviour
    {
        private ManhattanBootstrap app;
        private readonly MeasurementReadiness readiness = new MeasurementReadiness();
        private readonly TrackingMeasurementSession session = new TrackingMeasurementSession();
        private FaceReferencePreview preview;
        private MeasurementSample latest;
        private string context, lastReportId, actionMessage = "Confirm ruler measurements before starting.";
        private bool settingsVisible;
        private UnityEngine.UI.Text hud;
        private double nextLabel;
        private float declaredDistance, declaredMotion = .1f, distanceUncertainty = -1, motionUncertainty = -1;
        private bool confirmed;
        private float confirmedScreenWidth, confirmedScreenHeight;
        private Rect confirmedViewport, confirmedAperture;
        public float DeclaredDistanceMeters { get => declaredDistance; set => SetDeclaration(ref declaredDistance, value); }
        public float DeclaredMotionMeters { get => declaredMotion; set => SetDeclaration(ref declaredMotion, value); }
        public float DeclaredDistanceUncertaintyMeters { get => distanceUncertainty; set => SetDeclaration(ref distanceUncertainty, value); }
        public float DeclaredMotionUncertaintyMeters { get => motionUncertainty; set => SetDeclaration(ref motionUncertainty, value); }
        public bool MeasurementsConfirmed
        {
            get => confirmed && ConfirmationMatchesViewport();
            set { if (value) ConfirmMeasurements(); else InvalidateDeclarations("Measurement confirmation cleared; confirm the current values again."); }
        }
        public bool Active => session.Active;
        private string MeasurementContext() => FaceReferencePreview.Context(app.Display.Calibration) + ":" +
            app.Display.FilterMinimumCutoffHz.ToString("R", CultureInfo.InvariantCulture) + ":" + app.Display.FilterSpeedCoefficient.ToString("R", CultureInfo.InvariantCulture) + ":" + app.Display.UseAdaptiveFilter;
        private void SetDeclaration(ref float field, float value)
        {
            if (field.Equals(value)) return;
            field = value; InvalidateDeclarations("Ruler declaration changed. Measurement cancelled; confirm current values and create a new preview.");
        }
        private void InvalidateDeclarations(string reason)
        {
            confirmed = false; session.Cancel(); readiness.Clear();
            if (preview != null && !preview.Applied) preview = null;
            actionMessage = reason; if (app != null) RefreshInput();
        }
        private bool ConfirmationMatchesViewport() => app != null &&
            app.Display.Calibration.screenWidth.Equals(confirmedScreenWidth) && app.Display.Calibration.screenHeight.Equals(confirmedScreenHeight) &&
            app.Display.GetComponent<Camera>().pixelRect.Equals(confirmedViewport) &&
            app.Display.ProjectionScreenRectMeters.Equals(confirmedAperture) &&
            (app.WindowViewport == null || app.WindowViewport.MappingValid);
        private void ReconcileConfirmation()
        {
            if (confirmed && !ConfirmationMatchesViewport())
                InvalidateDeclarations("Physical screen size or camera viewport changed. Confirm both ruler spans and current declarations again.");
        }
        public bool ConfirmMeasurements()
        {
            if (app == null) return false;
            ReconcileConfirmation();
            var c = app.Display.Calibration; var viewport = app.Display.GetComponent<Camera>().pixelRect;
            if (app.WindowViewport != null && !app.WindowViewport.MappingValid)
            { InvalidateDeclarations("Physical viewport mapping unavailable. Resolve the viewport status before confirming."); return false; }
            if (!ViewerPersistence.ValidatePhysical(c.screenWidth, c.screenHeight, DeclaredDistanceMeters) ||
                !MeasurementSample.Finite(DeclaredMotionMeters) || DeclaredMotionMeters < .02f || DeclaredMotionMeters > .5f ||
                !MeasurementSample.Finite(DeclaredDistanceUncertaintyMeters) || !MeasurementSample.Finite(DeclaredMotionUncertaintyMeters) ||
                !MeasurementSample.Finite(viewport.x) || !MeasurementSample.Finite(viewport.y) || !MeasurementSample.Finite(viewport.width) || !MeasurementSample.Finite(viewport.height) || viewport.width <= 0 || viewport.height <= 0)
            { InvalidateDeclarations("Enter valid ruler declarations and use a visible camera viewport before confirming."); return false; }
            confirmedScreenWidth = c.screenWidth; confirmedScreenHeight = c.screenHeight; confirmedViewport = viewport;
            confirmedAperture = app.Display.ProjectionScreenRectMeters; confirmed = true;
            actionMessage = "User confirmed current declarations and both ruler spans for this physical screen size and camera viewport."; return true;
        }
        private bool CurrentInputReady() => readiness.Ready && latest.Usable && app.Display.ResultAgeMilliseconds >= 0 && app.Display.ResultAgeMilliseconds <= 100;

        public void Initialize(ManhattanBootstrap bootstrap, Canvas canvas, Font font)
        {
            app = bootstrap; DeclaredDistanceMeters = app.Display.Calibration.referenceEyeDistanceFromScreen;
            context = MeasurementContext();
            var go = new GameObject("Measurement instructions", typeof(RectTransform)); go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -170); rect.sizeDelta = new Vector2(940, 310);
            go.AddComponent<UnityEngine.UI.Image>().color = new Color(.03f, .05f, .07f, .95f);
            var label = new GameObject("Instructions", typeof(RectTransform)); label.transform.SetParent(go.transform, false);
            var lr = (RectTransform)label.transform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(18, 12); lr.offsetMax = new Vector2(-18, -12);
            hud = label.AddComponent<UnityEngine.UI.Text>(); hud.font = font; hud.fontSize = 19; hud.color = Color.white; hud.supportRichText = false; hud.raycastTarget = false;
            go.SetActive(false);
        }
        public void SetSettingsVisible(bool visible)
        {
            if (settingsVisible != visible && !session.Active) readiness.Clear();
            settingsVisible = visible;
        }
        public bool Begin()
        {
            ReconcileConfirmation();
            if (app.Benchmark.Recording || !app.Display.TrackingEnabled || app.Display.FreezeViewingDistance)
            { actionMessage = "Stop the benchmark; enable tracking and disable depth lock first."; return false; }
            bool started = session.Begin(DeclaredDistanceMeters, DeclaredMotionMeters, MeasurementsConfirmed, context, CurrentInputReady(), DeclaredDistanceUncertaintyMeters, DeclaredMotionUncertaintyMeters);
            actionMessage = session.Message;
            if (started) { app.KeyboardInput.Capture(false); app.KeyboardInput.InputEnabled = false; }
            return started;
        }
        public void Cancel()
        { session.Cancel(); readiness.Clear(); actionMessage = "Measurement cancelled; no calibration changed."; RefreshInput(); }
        public bool MarkCurrentHold()
        { ReconcileConfirmation(); return MeasurementsConfirmed && session.Mark(Time.realtimeSinceStartupAsDouble, DateTime.UtcNow.ToString("o"), latest, CurrentInputReady()); }
        public void PreviewReference()
        {
            ReconcileConfirmation();
            if (session.Active) { actionMessage = "Finish or cancel measurement before previewing reference."; return; }
            if (preview != null && preview.Applied && preview.NeedsExplicitSave)
            { actionMessage = "Save or revert the applied preview before replacing it."; return; }
            FaceReferencePreview.TryCreate(app.Display.Calibration, app.Display.LatestObservation, DeclaredDistanceMeters,
                MeasurementsConfirmed, CurrentInputReady(), out preview, out actionMessage);
            if (preview != null) actionMessage = string.Format(CultureInfo.InvariantCulture,
                "Preview only: fx/fy {0:F1}/{1:F1} -> {2:F1}/{3:F1} px. IPD and webcam pose remain assumptions. No file saved.",
                preview.BeforeIntrinsics.x, preview.BeforeIntrinsics.y, preview.CandidateIntrinsics.x, preview.CandidateIntrinsics.y);
        }
        public void ApplyReference()
        {
            ReconcileConfirmation();
            if (session.Active || preview == null || !MeasurementsConfirmed || !CurrentInputReady())
            { actionMessage = "Need an existing preview and fresh steady input; finish measurement first."; return; }
            var obs = app.Display.LatestObservation;
            if (!preview.TryApply(app.Display.Calibration, obs.frameWidth, obs.frameHeight, DeclaredDistanceMeters, MeasurementsConfirmed, out actionMessage)) return;
            app.UpdateCalibration(); ResetForCalibrationChange();
            actionMessage = "Reference applied for this session. Save physical calibration explicitly to keep it; otherwise restart restores the previous file.";
        }
        public void RevertReference()
        {
            if (preview == null || !preview.TryRevert(app.Display.Calibration, out actionMessage)) return;
            app.UpdateCalibration(); ResetForCalibrationChange();
            actionMessage = "Previous session settings restored. No file saved; explicit Save writes the restored values if needed.";
        }
        public void CalibrationSaved(bool success)
        {
            if (success && preview != null) preview.MarkSaved();
            actionMessage = success ? "Physical calibration explicitly saved." : "Save failed. Session settings remain unsaved.";
        }
        private void ResetForCalibrationChange()
        { readiness.Clear(); session.Cancel(); context = MeasurementContext(); RefreshInput(); }
        private void RefreshInput() { if (app != null) app.KeyboardInput.InputEnabled = !settingsVisible && !session.Active && !app.Benchmark.Recording; }
        private void RefreshHudVisibility()
        { if (hud != null) hud.transform.parent.gameObject.SetActive(session.Active || session.Phase == MeasurementPhase.Complete); }
        private void LateUpdate()
        {
            if (app == null) return;
            ReconcileConfirmation();
            RefreshHudVisibility();
            if (!settingsVisible && !session.Active && session.Phase != MeasurementPhase.Complete) return;
            string currentContext = MeasurementContext();
            if (context != currentContext) { ResetForCalibrationChange(); actionMessage = "Calibration changed; previous measurement samples cleared."; }
            var display = app.Display; var obs = display.LatestObservation; var c = display.Calibration;
            latest = new MeasurementSample {
                sequence = obs.sequence, receivedSeconds = obs.receivedAtSeconds, reportedProcessingAgeMs = display.ResultAgeMilliseconds,
                fresh = display.HasFreshEyeEstimate, valid = display.IsTracking && display.TrackingEnabled && !display.FreezeViewingDistance,
                raw = display.UncalibratedEyePositionMeters, input = display.EstimatedEyePositionMeters, rendered = display.EyePositionMeters,
                anglesKnown = obs.poseSupported && obs.poseValid, reportedEulerDegrees = obs.headEulerDegrees,
                atViewingRangeBoundary = AtBoundary(display.UncalibratedEyePositionMeters, c) || AtBoundary(display.EstimatedEyePositionMeters, c) || AtBoundary(display.EyePositionMeters, c)
            };
            bool unique = readiness.Push(latest);
            if (session.Active && latest.Usable && !unique && readiness.Count == 0)
                session.Restart("Observation stream restarted or paused. Previous holds cleared; retry A.");
            double now = Time.realtimeSinceStartupAsDouble;
            if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame) Cancel();
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
                MarkCurrentHold();
            session.Observe(latest, unique, now, DateTime.UtcNow.ToString("o"), context);
            if (session.Report != null && session.Report.sessionId != lastReportId)
            {
                lastReportId = session.Report.sessionId;
                SaveNumericReport(session.Report); RefreshInput();
            }
            RefreshHudVisibility();
            if (hud != null)
            {
                if (now >= nextLabel) { nextLabel = now + .2; hud.text = session.Message + "\n" + DescribeResult(session.Report) + "\n" + readiness.Reason; }
            }
        }
        public static bool AtBoundary(Vector3 eye, DisplayCalibration c) => -eye.z <= c.minimumEyeDistanceFromScreen + .001f || -eye.z >= c.maximumEyeDistanceFromScreen - .001f;
        private void SaveNumericReport(TrackingMeasurementReport report)
        {
            try {
                string directory = Path.Combine(ViewerPersistence.DirectoryPath, "measurement-sessions"); Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + report.sessionId + ".json");
                // Unique name and CreateNew preserve existing output. Numeric fields only; no camera image or face landmarks.
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(report, true));
                actionMessage = "Numeric report saved locally in measurement-sessions. No calibration file changed.";
            } catch (Exception e) { actionMessage = "Numeric report could not be saved: " + e.GetType().Name; }
        }
        public string Describe() => actionMessage + "\n" + readiness.Reason + "\n" +
            (MeasurementsConfirmed ? "Ruler declarations confirmed by user. " : "Ruler declarations UNCONFIRMED. ") +
            "Distance/motion uncertainty: " + Uncertainty(DeclaredDistanceUncertaintyMeters) + " / " + Uncertainty(DeclaredMotionUncertaintyMeters) + ". " +
            string.Format(CultureInfo.InvariantCulture, "Distance {0:F1} cm; RIGHT motion {1:F1} cm. IPD {2:F1} mm / FOV {3:F1} deg are separate configured assumptions. ", DeclaredDistanceMeters * 100, DeclaredMotionMeters * 100, app.Display.Calibration.measuredEyeSeparationMeters * 1000, app.Display.Calibration.horizontalFovDegrees) +
            "No automatic gain fit. Reported processing + receive age is not exposure/MTP.\n" + DescribeResult(session.Report);
        private static string Uncertainty(float meters) => meters < 0 ? "UNKNOWN" : (meters * 100).ToString("F1", CultureInfo.InvariantCulture) + " cm";
        public static string DescribeResult(TrackingMeasurementReport report)
        {
            if (report == null) return "F8 marks each steady A, B and RETURN hold; F9 cancels.";
            string angles = report.anglesKnown ? string.Format(CultureInfo.InvariantCulture,
                "Pitch/yaw/roll A->B {0}; return {1} deg", report.reportedAngleDelta.pitchYawRollDegrees, report.reportedReturnAngleDelta.pitchYawRollDegrees) : "Orientation: UNKNOWN; metric interpretation blocked.";
            return string.Format(CultureInfo.InvariantCulture,
                "A->B raw/input/render XYZ (m): {0} / {1} / {2}\nReturn raw/render (m): {3} / {4}\n{5}\nDirection {6}; Y/Z + rotation screen {7}; return match {8}. Engineering checks only; no perceptual pass or correction.",
                report.rawDelta.ToString("F3"), report.inputDelta.ToString("F3"), report.renderedDelta.ToString("F3"), report.rawReturnDelta.ToString("F3"), report.renderedReturnDelta.ToString("F3"), angles,
                report.directionsMatch, report.lateralUncontaminated, report.returnMatches);
        }
    }
}
