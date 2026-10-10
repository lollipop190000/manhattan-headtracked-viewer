using System;
using System.Collections.Generic;
using System.IO;
using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class TrackingMeasurementTests
{
    private static MeasurementSample Sample(int sequence, double seconds, Vector3? eye = null, bool angles = true) => new MeasurementSample {
        sequence = sequence, receivedSeconds = seconds, reportedProcessingAgeMs = 25, fresh = true, valid = true,
        raw = eye ?? new Vector3(0, 0, -.65f), input = eye ?? new Vector3(0, 0, -.65f), rendered = eye ?? new Vector3(0, 0, -.65f), anglesKnown = angles };
    private static MeasurementReadiness Ready()
    { var gate = new MeasurementReadiness(); for (int i = 1; i <= 42; i++) gate.Push(Sample(i, i * .05)); Assert.That(gate.Ready, Is.True); return gate; }

    [Test] public void UniqueFramesRequireTwoSecondsEvenAtHighRenderRate()
    {
        var gate = new MeasurementReadiness();
        for (int i = 0; i < 200; i++) gate.Push(Sample(1, 1));
        Assert.That(gate.Count, Is.EqualTo(1)); Assert.That(gate.Ready, Is.False);
        for (int i = 2; i <= 30; i++) gate.Push(Sample(i, 1 + i * .01));
        Assert.That(gate.Ready, Is.False);
    }
    [TestCase("stale")] [TestCase("invalid")] [TestCase("nanRaw")] [TestCase("nanRendered")]
    [TestCase("boundary")] [TestCase("missingSequence")] [TestCase("infiniteAngle")]
    public void UnsafeInputClearsReadiness(string problem)
    {
        var gate = Ready(); var s = Sample(43, 2.15);
        switch (problem) { case "stale": s.reportedProcessingAgeMs = 101; break; case "invalid": s.valid = false; break;
            case "nanRaw": s.raw.x = float.NaN; break; case "nanRendered": s.rendered.z = float.NaN; break;
            case "boundary": s.atViewingRangeBoundary = true; break; case "missingSequence": s.sequence = 0; break;
            case "infiniteAngle": s.reportedEulerDegrees.y = float.PositiveInfinity; break; }
        Assert.That(gate.Push(s), Is.False); Assert.That(gate.Count, Is.Zero); Assert.That(gate.Ready, Is.False);
    }
    [TestCase("sequence")] [TestCase("clock")] [TestCase("gap")]
    public void RestartOrTimingDiscontinuityClearsReadiness(string problem)
    {
        var gate = Ready(); var s = Sample(43, 2.15);
        if (problem == "sequence") s.sequence = 1;
        if (problem == "clock") s.receivedSeconds = 1;
        if (problem == "gap") s.receivedSeconds = 2.3;
        Assert.That(gate.Push(s), Is.False); Assert.That(gate.Count, Is.Zero);
    }
    [TestCase("raw")] [TestCase("rendered")] [TestCase("rotation")]
    public void SteadyRenderedEyeCannotHideRawOrRotationJitter(string channel)
    {
        var gate = new MeasurementReadiness();
        for (int i = 1; i <= 42; i++) { var s = Sample(i, i * .05); float sign = i % 2 == 0 ? 1 : -1;
            if (channel == "raw") s.raw.x = sign * .02f;
            if (channel == "rendered") s.rendered.x = sign * .02f;
            if (channel == "rotation") s.reportedEulerDegrees.y = sign * 10;
            gate.Push(s); }
        Assert.That(gate.Ready, Is.False);
    }
    [Test] public void WrappedAnglesRemainSteady()
    {
        var list = new List<MeasurementSample>();
        for (int i = 1; i <= 42; i++) { var s = Sample(i, i * .05); s.reportedEulerDegrees.z = i % 2 == 0 ? 179.5f : -179.5f; list.Add(s); }
        Assert.That(MeasurementHold.Calculate(list).Stable, Is.True);
    }
    private static void Hold(TrackingMeasurementSession session, ref int sequence, double now, Vector3 eye, bool angles = true, Vector3? rotation = null)
    {
        Assert.That(session.Mark(now, "synthetic-user-marker", Sample(++sequence, now, eye, angles), true), Is.True);
        for (int i = 0; i <= 101; i++) { var s = Sample(++sequence, now + i * .05, eye, angles); s.reportedEulerDegrees = rotation ?? Vector3.zero;
            session.Observe(s, true, now + i * .05, "synthetic-clock", "context"); }
    }
    private static TrackingMeasurementSession Session()
    { var session = new TrackingMeasurementSession(); Assert.That(session.Begin(.65f, .1f, true, "context", true), Is.True); return session; }
    private static TrackingMeasurementReport Report(Vector3 b, Vector3 returned, bool known = true, Vector3? angle = null)
    { var session = Session(); int seq = 0; Hold(session, ref seq, 0, new Vector3(0, 0, -.65f), known);
        Hold(session, ref seq, 10, b, known, angle); Hold(session, ref seq, 20, returned, known); Assert.That(session.Phase, Is.EqualTo(MeasurementPhase.Complete)); return session.Report; }
    [Test] public void AThenBRequiresIndependentReturnBeforeCompleting()
    {
        var session = Session(); int seq = 0; Hold(session, ref seq, 0, new Vector3(0, 0, -.65f));
        Assert.That(session.Phase, Is.EqualTo(MeasurementPhase.ReadyB));
        Hold(session, ref seq, 10, new Vector3(.1f, 0, -.65f));
        Assert.That(session.Phase, Is.EqualTo(MeasurementPhase.ReadyReturn)); Assert.That(session.Report, Is.Null);
        Hold(session, ref seq, 20, new Vector3(0, 0, -.65f));
        Assert.That(session.Report.LateralChecksConsistent, Is.True); Assert.That(session.Report.captureTimestampAvailable, Is.False);
        Assert.That(session.Report.rawDelta.x, Is.EqualTo(.1f).Within(.00001f));
        Assert.That(session.Report.rawReturnDelta.x, Is.EqualTo(-.1f).Within(.00001f)); Assert.That(session.Report.markers.Length, Is.EqualTo(6));
    }
    [TestCase(-1, -1)] [TestCase(.005f, .002f)]
    public void RulerUncertaintyRemainsAUserDeclaration(float distance, float motion)
    {
        var session = new TrackingMeasurementSession(); session.Begin(.65f, .1f, true, "context", true, distance, motion);
        int seq = 0; Hold(session, ref seq, 0, new Vector3(0, 0, -.65f)); Hold(session, ref seq, 10, new Vector3(.1f, 0, -.65f)); Hold(session, ref seq, 20, new Vector3(0, 0, -.65f));
        Assert.That(session.Report.userDeclaredDistanceUncertaintyMeters, Is.EqualTo(distance));
        Assert.That(session.Report.userDeclaredMotionUncertaintyMeters, Is.EqualTo(motion));
    }
    [Test] public void DuplicateRenderFramesDoNotIncreaseHoldCount()
    {
        var session = Session(); var s = Sample(1, 0); session.Mark(0, "marker", s, true);
        session.Observe(s, true, 0, "clock", "context");
        for (int i = 0; i <= 500; i++) session.Observe(s, false, i * .01, "clock", "context");
        Assert.That(session.Phase, Is.EqualTo(MeasurementPhase.ReadyA)); Assert.That(session.Report, Is.Null);
    }
    [Test] public void LossAfterAErasesPreviousAAndB()
    {
        var session = Session(); int seq = 0; Hold(session, ref seq, 0, new Vector3(0, 0, -.65f));
        var bad = Sample(++seq, 6); bad.valid = false; session.Observe(bad, true, 6, "clock", "context");
        Assert.That(session.Phase, Is.EqualTo(MeasurementPhase.ReadyA)); Assert.That(session.Report, Is.Null);
        Hold(session, ref seq, 10, new Vector3(.3f, 0, -.65f)); Hold(session, ref seq, 20, new Vector3(.4f, 0, -.65f)); Hold(session, ref seq, 30, new Vector3(.3f, 0, -.65f));
        Assert.That(session.Report.a.raw.mean.x, Is.EqualTo(.3f).Within(.00001f)); Assert.That(session.Report.markers.Length, Is.EqualTo(6));
    }
    [Test] public void CalibrationChangeRequiresNewConfirmedSession()
    { var session = Session(); session.Observe(Sample(1, 1), true, 1, "clock", "other"); Assert.That(session.Phase, Is.EqualTo(MeasurementPhase.Idle)); }
    [TestCase(false, true)] [TestCase(true, false)]
    public void StartRequiresConfirmedValuesAndReadyInput(bool confirmed, bool ready)
    { Assert.That(new TrackingMeasurementSession().Begin(.65f, .1f, confirmed, "context", ready), Is.False); }
    [Test] public void UnknownRotationIsUnknownAndBlocksInterpretation()
    {
        var r = Report(new Vector3(.1f, 0, -.65f), new Vector3(0, 0, -.65f), false);
        Assert.That(r.anglesKnown, Is.False); Assert.That(r.reportedAngleDelta, Is.Null); Assert.That(r.LateralChecksConsistent, Is.False);
        Assert.That(ViewerMeasurementWorkflow.DescribeResult(r), Does.Contain("UNKNOWN"));
    }
    [TestCase("yz")] [TestCase("rotation")] [TestCase("mirror")] [TestCase("return")]
    public void ContaminationDirectionAndReturnCannotBeHidden(string problem)
    {
        var b = new Vector3(.1f, 0, -.65f); var returned = new Vector3(0, 0, -.65f); Vector3 rotation = Vector3.zero;
        if (problem == "yz") b.z -= .03f; if (problem == "rotation") rotation.y = 8;
        if (problem == "mirror") b.x = -.1f; if (problem == "return") returned.x = .03f;
        var r = Report(b, returned, true, rotation); Assert.That(r.LateralChecksConsistent, Is.False);
        if (problem == "mirror") { Assert.That(r.directionsMatch, Is.False); Assert.That(r.rawDelta.x, Is.LessThan(0)); }
        if (problem == "return") Assert.That(r.returnMatches, Is.False);
    }
    [TestCase(-.25f, true)] [TestCase(-1.5f, true)] [TestCase(-.65f, false)]
    public void ViewingRangeBoundaryIsExcluded(float z, bool expected)
    { Assert.That(ViewerMeasurementWorkflow.AtBoundary(new Vector3(0, 0, z), new DisplayCalibration()), Is.EqualTo(expected)); }

    private static HeadObservation Face(float fx = 500) => new HeadObservation {
        found = true, confidence = 1, frameWidth = 640, frameHeight = 480, sequence = 1,
        leftEye = new Vector2(.5f - fx * .063f / .625f / 640 / 2, .5f),
        rightEye = new Vector2(.5f + fx * .063f / .625f / 640 / 2, .5f),
        poseSupported = true, poseValid = true, poseEyeCamera = new Vector3(.08f, .02f, .625f)
    };
    [Test] public void ReferencePreviewChangesSharedFocalOnlyAfterApplyAndNeverRigidPacketGain()
    {
        var c = new DisplayCalibration { referenceEyeDistanceFromScreen = .6f, mirrorImageX = false };
        var original = JsonUtility.ToJson(c); var obs = Face(); EyePoseEstimator.TryEstimate(obs, c, out var before);
        Assert.That(FaceReferencePreview.TryCreate(c, obs, .65f, true, true, out var preview, out var error), Is.True, error);
        Assert.That(JsonUtility.ToJson(c), Is.EqualTo(original)); Assert.That(preview.CandidateIntrinsics.x, Is.EqualTo(500).Within(.001f));
        Assert.That(preview.BeforeIntrinsics.x, Is.EqualTo(554.2563f).Within(.001f));
        Assert.That(preview.TryApply(c, 640, 480, .65f, true, out error), Is.True, error);
        EyePoseEstimator.TryEstimate(obs, c, out var after); Assert.That(after, Is.EqualTo(before)); Assert.That(preview.NeedsExplicitSave, Is.True);
        Assert.That(preview.TryRevert(c, out error), Is.True, error); Assert.That(JsonUtility.ToJson(c), Is.EqualTo(original));
    }
    [TestCase("context")] [TestCase("resolution")]
    public void OutdatedPreviewCannotApply(string problem)
    {
        var c = new DisplayCalibration(); FaceReferencePreview.TryCreate(c, Face(), .65f, true, true, out var preview, out _);
        if (problem == "context") c.horizontalFovDegrees = 75;
        Assert.That(preview.TryApply(c, problem == "resolution" ? 1280 : 640, 480, .65f, true, out _), Is.False);
    }
    [Test] public void RevertCannotOverwriteLaterEdits()
    {
        var c = new DisplayCalibration(); FaceReferencePreview.TryCreate(c, Face(), .65f, true, true, out var preview, out _);
        preview.TryApply(c, 640, 480, .65f, true, out _); c.screenWidth = .7f;
        Assert.That(preview.TryRevert(c, out _), Is.False); Assert.That(c.screenWidth, Is.EqualTo(.7f));
    }
    [TestCase(false, .65f)] [TestCase(true, .75f)]
    public void ReferencePreviewRequiresItsOriginalConfirmedDeclaration(bool confirmed, float currentDistance)
    {
        var c = new DisplayCalibration(); string before = JsonUtility.ToJson(c);
        FaceReferencePreview.TryCreate(c, Face(), .65f, true, true, out var preview, out _);
        Assert.That(preview.TryApply(c, 640, 480, currentDistance, confirmed, out _), Is.False);
        Assert.That(JsonUtility.ToJson(c), Is.EqualTo(before));
    }
    [Test] public void UnsavedPreviewDoesNotAffectRestartAndOnlyExplicitSavePersists()
    {
        string prior = Environment.GetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY");
        string temp = Path.Combine(Path.GetTempPath(), "measurement-preview-" + Guid.NewGuid().ToString("N"));
        try {
            Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY", temp);
            var c = new DisplayCalibration(); Assert.That(ViewerPersistence.Save(ViewerPersistence.CalibrationFile, c), Is.True);
            string saved = File.ReadAllText(Path.Combine(temp, ViewerPersistence.CalibrationFile));
            FaceReferencePreview.TryCreate(c, Face(), .65f, true, true, out var preview, out _); preview.TryApply(c, 640, 480, .65f, true, out _);
            Assert.That(File.ReadAllText(Path.Combine(temp, ViewerPersistence.CalibrationFile)), Is.EqualTo(saved));
            var restarted = new DisplayCalibration(); ViewerPersistence.RestoreCalibration(restarted); Assert.That(restarted.referenceEyeSpanPixels, Is.Zero);
            Assert.That(ViewerPersistence.Save(ViewerPersistence.CalibrationFile, c), Is.True); preview.MarkSaved();
            ViewerPersistence.RestoreCalibration(restarted); Assert.That(restarted.referenceEyeSpanPixels, Is.GreaterThan(1)); Assert.That(preview.NeedsExplicitSave, Is.False);
        } finally { Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY", prior); if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    [TestCase(false, true)] [TestCase(true, false)]
    public void PreviewRequiresConfirmedDistanceAndSteadyInput(bool confirmed, bool ready)
    { Assert.That(FaceReferencePreview.TryCreate(new DisplayCalibration(), Face(), .65f, confirmed, ready, out _, out _), Is.False); }
    [Test] public void PreciseIntrinsicsAreNotReplacedByFaceReference()
    {
        var c = new DisplayCalibration { usePreciseIntrinsics = true, focalXPixels = 610, focalYPixels = 590, principalXPixels = 320, principalYPixels = 240, intrinsicsImageWidth = 640, intrinsicsImageHeight = 480 };
        FaceReferencePreview.TryCreate(c, Face(), .65f, true, true, out var preview, out _);
        Assert.That(preview.CandidateIntrinsics.x, Is.EqualTo(610)); Assert.That(preview.CandidateIntrinsics.y, Is.EqualTo(590));
    }
}
