using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class MeasurementWorkflowLifecycleTests
{
    private GameObject root;
    private ManhattanBootstrap app;
    private ViewerSettingsUI ui;
    private ViewerMeasurementWorkflow workflow;
    private SyntheticHeadObservationSource source;
    private string priorDirectory, directory;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [SetUp] public void Create()
    {
        Assert.That(ManhattanBootstrap.HasArgument("-runTests") && ManhattanBootstrap.HasArgument("--no-tracker"), Is.True);
        Assert.That(Object.FindFirstObjectByType<ManhattanBootstrap>(), Is.Null);
        priorDirectory = Environment.GetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY");
        directory = Path.Combine(Path.GetTempPath(), "measurement-workflow-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY", directory);
        root = new GameObject("Synthetic measurement workflow"); root.SetActive(false);
        app = root.AddComponent<ManhattanBootstrap>(); app.discoverCatalog = false; root.SetActive(true);
        // This fixture explicitly opts out of city discovery even when generated assets exist.
        // Production display composition is used, with an opt-in synthetic source.
        Assert.That(app.catalog, Is.Null);
        var c = app.Display.Calibration; c.webcamPosition = Vector3.zero; c.webcamEulerDegrees = Vector3.zero; c.mirrorImageX = false;
        source = root.AddComponent<SyntheticHeadObservationSource>(); source.Calibration = c; source.RigidPose = true;
        app.Display.Configure(app.Display.ScreenPlane, source);
        ui = root.GetComponent<ViewerSettingsUI>(); workflow = root.GetComponent<ViewerMeasurementWorkflow>();
        ((GameObject)typeof(ViewerSettingsUI).GetField("setup", Private).GetValue(ui)).SetActive(false);
        ShowSettings(true);
    }
    private void ShowSettings(bool value) => typeof(ViewerSettingsUI).GetMethod("ShowDrawer", Private).Invoke(ui, new object[] { value });
    private MeasurementReadiness Gate => (MeasurementReadiness)typeof(ViewerMeasurementWorkflow).GetField("readiness", Private).GetValue(workflow);
    private TrackingMeasurementSession Session => (TrackingMeasurementSession)typeof(ViewerMeasurementWorkflow).GetField("session", Private).GetValue(workflow);
    private IEnumerator WaitReady()
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 8;
        while (!Gate.Ready && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        Assert.That(Gate.Ready, Is.True, Gate.Reason);
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        if (workflow != null) workflow.Cancel(); Object.Destroy(root); yield return null;
        Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY", priorDirectory);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
    [UnityTest] public IEnumerator FullSettingsUIIsOptInAndShowsUnknownWithoutWritingCalibration()
    {
        Assert.That(Object.FindFirstObjectByType<PythonBridgeSource>(), Is.Null);
        Assert.That(workflow.Active, Is.False); Assert.That(workflow.MeasurementsConfirmed, Is.False);
        Assert.That(workflow.Begin(), Is.False); Assert.That(ViewerPersistence.HasCalibration, Is.False);
        yield return WaitReady();
        Canvas.ForceUpdateCanvases();
        string output = Environment.GetEnvironmentVariable("MANHATTAN_MEASUREMENT_OUTPUT");
        if (!string.IsNullOrEmpty(output) && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "synthetic-settings.png")); yield return new WaitForEndOfFrame(); yield return null;
        }
        ShowSettings(false); yield return null;
        Assert.That(Gate.Count, Is.Zero); Assert.That(app.KeyboardInput.InputEnabled, Is.True); Assert.That(ViewerPersistence.HasCalibration, Is.False);
    }
    [UnityTest] public IEnumerator MarkedAUsesActualObservationsThenTrackingLossClearsItAndCancelRestoresFlight()
    {
        yield return WaitReady(); workflow.DeclaredDistanceMeters = .6f; workflow.MeasurementsConfirmed = true;
        Assert.That(workflow.Begin(), Is.True); ShowSettings(false); Assert.That(app.KeyboardInput.InputEnabled, Is.False);
        Assert.That(workflow.MarkCurrentHold(), Is.True);
        double deadline = Time.realtimeSinceStartupAsDouble + 7;
        while (Session.Phase == MeasurementPhase.CollectA && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.ReadyB));
        source.Publish = false;
        yield return new WaitForSecondsRealtime(.2f); yield return null;
        Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.ReadyA)); Assert.That(Session.Report, Is.Null); Assert.That(Gate.Ready, Is.False);
        workflow.Cancel(); Assert.That(app.KeyboardInput.InputEnabled, Is.True); Assert.That(ViewerPersistence.HasCalibration, Is.False);
    }
    [UnityTest] public IEnumerator UIReferencePreviewApplyRevertAndExplicitSaveUseSeparateActions()
    {
        workflow.DeclaredDistanceMeters = .65f; yield return WaitReady(); workflow.MeasurementsConfirmed = true;
        string before = JsonUtility.ToJson(app.Display.Calibration);
        workflow.PreviewReference(); Assert.That(JsonUtility.ToJson(app.Display.Calibration), Is.EqualTo(before)); Assert.That(ViewerPersistence.HasCalibration, Is.False);
        workflow.ApplyReference(); Assert.That(app.Display.Calibration.referenceEyeSpanPixels, Is.GreaterThan(1)); Assert.That(ViewerPersistence.HasCalibration, Is.False);
        workflow.RevertReference(); Assert.That(JsonUtility.ToJson(app.Display.Calibration), Is.EqualTo(before)); Assert.That(ViewerPersistence.HasCalibration, Is.False);
        yield return WaitReady(); workflow.PreviewReference(); workflow.ApplyReference();
        Assert.That(ViewerPersistence.Save(ViewerPersistence.CalibrationFile, app.Display.Calibration), Is.True); workflow.CalibrationSaved(true);
        var restarted = new DisplayCalibration(); ViewerPersistence.RestoreCalibration(restarted);
        Assert.That(restarted.referenceEyeSpanPixels, Is.GreaterThan(1)); Assert.That(restarted.referenceEyeDistanceFromScreen, Is.EqualTo(.65f));
    }
    private IEnumerator MarkAndFinish(MeasurementPhase next)
    {
        Assert.That(workflow.MarkCurrentHold(), Is.True);
        double deadline = Time.realtimeSinceStartupAsDouble + 7;
        while (Session.Phase != next && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        Assert.That(Session.Phase, Is.EqualTo(next), Session.Message);
    }
    [UnityTest] public IEnumerator DeclarationAndUncertaintyEditsCancelPriorHoldsAndRequireReconfirmation()
    {
        yield return WaitReady(); workflow.MeasurementsConfirmed = true; Assert.That(workflow.Begin(), Is.True); ShowSettings(false);
        yield return MarkAndFinish(MeasurementPhase.ReadyB); ShowSettings(true);
        workflow.DeclaredMotionMeters = .2f;
        Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.Idle)); Assert.That(workflow.MeasurementsConfirmed, Is.False);
        Assert.That(workflow.MarkCurrentHold(), Is.False); Assert.That(Session.Report, Is.Null); Assert.That(workflow.Begin(), Is.False);
        yield return WaitReady(); workflow.MeasurementsConfirmed = true; Assert.That(workflow.Begin(), Is.True);
        workflow.DeclaredMotionUncertaintyMeters = .005f;
        Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.Idle)); Assert.That(workflow.MeasurementsConfirmed, Is.False);
        yield return WaitReady(); workflow.MeasurementsConfirmed = true; Assert.That(workflow.Begin(), Is.True);
        workflow.DeclaredDistanceUncertaintyMeters = .005f;
        Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.Idle)); Assert.That(workflow.MeasurementsConfirmed, Is.False);
    }
    [UnityTest] public IEnumerator DeclarationEditsDiscardUnappliedReferenceEvenAfterReconfirmation()
    {
        workflow.DeclaredDistanceMeters = .65f; yield return WaitReady(); workflow.MeasurementsConfirmed = true;
        string before = JsonUtility.ToJson(app.Display.Calibration); workflow.PreviewReference();
        workflow.DeclaredDistanceMeters = .75f;
        Assert.That(workflow.MeasurementsConfirmed, Is.False); workflow.ApplyReference(); Assert.That(JsonUtility.ToJson(app.Display.Calibration), Is.EqualTo(before));
        yield return WaitReady(); workflow.MeasurementsConfirmed = true; workflow.ApplyReference();
        Assert.That(JsonUtility.ToJson(app.Display.Calibration), Is.EqualTo(before), "Reconfirmation must not resurrect the old 65 cm preview.");
    }
    [UnityTest] public IEnumerator PhysicalSizeAndActualPixelRectChangesInvalidateRulerConfirmation()
    {
        yield return WaitReady(); workflow.MeasurementsConfirmed = true;
        app.Display.Calibration.screenWidth = .60f;
        Assert.That(workflow.MeasurementsConfirmed, Is.False); Assert.That(workflow.Begin(), Is.False);
        yield return WaitReady(); workflow.MeasurementsConfirmed = true; Assert.That(workflow.Begin(), Is.True);
        var camera = app.Display.GetComponent<Camera>(); camera.rect = new Rect(.1f, .1f, .8f, .8f);
        Assert.That(workflow.MeasurementsConfirmed, Is.False); Assert.That(workflow.Begin(), Is.False); Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.Idle));
        // Even immediate explicit reconfirmation after another resize clears the prior session first.
        yield return WaitReady(); workflow.MeasurementsConfirmed = true; Assert.That(workflow.Begin(), Is.True);
        camera.rect = new Rect(0, 0, 1, 1); workflow.MeasurementsConfirmed = true;
        Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.Idle));
    }
    [UnityTest] public IEnumerator PhysicalWindowMoveInvalidatesConfirmationWithoutChangingCalibration()
    {
        yield return WaitReady(); workflow.MeasurementsConfirmed = true;
        Assert.That(workflow.MeasurementsConfirmed, Is.True);
        string calibration = JsonUtility.ToJson(app.Display.Calibration);
        Rect pixels = app.Display.GetComponent<Camera>().pixelRect;
        app.Display.DisplayViewport = new Rect(.1f, .2f, .7f, .6f);
        Assert.That(app.Display.GetComponent<Camera>().pixelRect, Is.EqualTo(pixels));
        Assert.That(workflow.MeasurementsConfirmed, Is.False, "A window move changes physical aperture even at the same render resolution");
        Assert.That(JsonUtility.ToJson(app.Display.Calibration), Is.EqualTo(calibration));
        app.WindowViewport.SetPanelMapping(true); // Editor cannot claim a valid native Player mapping.
        Assert.That(workflow.ConfirmMeasurements(), Is.False);
    }

    [UnityTest] public IEnumerator ViewportChangeWithSettingsClosedCancelsAndHidesMeasurementHUD()
    {
        yield return WaitReady(); workflow.MeasurementsConfirmed = true; Assert.That(workflow.Begin(), Is.True); ShowSettings(false);
        yield return null; yield return null;
        var hud = (Component)typeof(ViewerMeasurementWorkflow).GetField("hud", Private).GetValue(workflow);
        Assert.That(hud.transform.parent.gameObject.activeSelf, Is.True);
        app.Display.GetComponent<Camera>().rect = new Rect(.1f, .1f, .8f, .8f);
        yield return null; yield return null;
        Assert.That(Session.Phase, Is.EqualTo(MeasurementPhase.Idle)); Assert.That(workflow.Active, Is.False);
        Assert.That(hud.transform.parent.gameObject.activeSelf, Is.False, "A cancelled session must not leave stale recording/F9 instructions visible.");
        Assert.That(app.KeyboardInput.InputEnabled, Is.True); Assert.That(ViewerPersistence.HasCalibration, Is.False);
    }
    [UnityTest] public IEnumerator ActualSyntheticABReturnWritesOneNumericReportWithoutSavingCalibration()
    {
        yield return WaitReady(); workflow.DeclaredDistanceMeters = .6f; workflow.MeasurementsConfirmed = true;
        Assert.That(workflow.Begin(), Is.True); ShowSettings(false);
        yield return MarkAndFinish(MeasurementPhase.ReadyB);
        source.Eye = new Vector3(.1f, 0, -.6f); yield return null; yield return null; yield return WaitReady();
        yield return MarkAndFinish(MeasurementPhase.ReadyReturn);
        source.Eye = new Vector3(0, 0, -.6f); yield return null; yield return null; yield return WaitReady();
        yield return MarkAndFinish(MeasurementPhase.Complete);
        Assert.That(Session.Report.LateralChecksConsistent, Is.True); Assert.That(Session.Report.rawDelta.x, Is.EqualTo(.1f).Within(.001f));
        Assert.That(Session.Report.rawReturnDelta.x, Is.EqualTo(-.1f).Within(.001f));
        Assert.That(Session.Report.a.uniqueObservations, Is.InRange(20, source.ReadCount));
        Assert.That(Directory.GetFiles(Path.Combine(directory, "measurement-sessions"), "*.json").Length, Is.EqualTo(1));
        Assert.That(ViewerPersistence.HasCalibration, Is.False); Assert.That(app.KeyboardInput.InputEnabled, Is.True);
        string output = Environment.GetEnvironmentVariable("MANHATTAN_MEASUREMENT_OUTPUT");
        if (!string.IsNullOrEmpty(output) && !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        { yield return new WaitForSecondsRealtime(.25f); ScreenCapture.CaptureScreenshot(Path.Combine(output, "synthetic-measurement-complete.png")); yield return new WaitForEndOfFrame(); yield return null; }
        yield return null;
        Assert.That(Directory.GetFiles(Path.Combine(directory, "measurement-sessions"), "*.json").Length, Is.EqualTo(1), "The same completed report must not be saved every frame.");
    }
}
