using System;
using System.Collections;
using HeadTracked.Display;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class HeadTrackingPipelineTests
{
    private GameObject root, rig, cube;
    private Transform plane;
    private Camera camera;
    private HeadTrackedDisplay display;
    private SyntheticHeadObservationSource source;
    private FreeFlightController flight;
    private FloatingOrigin origin;
    private Matrix4x4 unjitteredProjection, view;
    private int poses;
    private string savedCalibration;
    private static readonly Vector3 Neutral = new Vector3(0, 0, -.6f);

    private sealed class AbsoluteInput : IMotionInputSource
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public MotionCommand Sample(float deltaTime) => new MotionCommand
        {
            hasAbsolutePose = true, cityPosition = Position, cityRotation = Rotation
        };
    }

    [SetUp]
    public void CreatePipeline()
    {
        // The viewer's RuntimeInitializeOnLoadMethod only suppresses its normal
        // bootstrap when the editor process has -runTests. An interactive Test
        // Runner session can otherwise start the viewer before this setup runs.
        Assert.That(ManhattanBootstrap.HasArgument("-runTests"), Is.True,
            "Start a fresh Unity test process with -runTests; do not use an ordinary interactive editor session.");
        Assert.That(Object.FindFirstObjectByType<ManhattanBootstrap>(), Is.Null,
            "The normal viewer is already running. Use a clean test scene; this guard cannot undo its startup.");
        root = new GameObject("Head observation pipeline regression");
        rig = new GameObject("Observer rig"); rig.transform.SetParent(root.transform, false);
        flight = rig.AddComponent<FreeFlightController>();
        origin = root.AddComponent<FloatingOrigin>(); origin.observer = rig.transform;
        origin.rebaseDistance = 10000f; flight.Origin = origin;
        plane = new GameObject("Physical screen").transform; plane.SetParent(rig.transform, false);
        plane.localPosition = Vector3.forward * .6f;
        source = root.AddComponent<SyntheticHeadObservationSource>();
        var cameraObject = new GameObject("Observation-driven camera");
        cameraObject.SetActive(false); cameraObject.transform.SetParent(rig.transform, false);
        camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.nearClipPlane = .03f; camera.farClipPlane = 25000f;
        display = cameraObject.AddComponent<HeadTrackedDisplay>();
        var c = display.Calibration;
        c.screenWidth = .53f; c.screenHeight = .30f;
        c.deriveScreenHeightFromResolution = false;
        c.webcamPosition = Vector3.zero; c.webcamEulerDegrees = Vector3.zero;
        c.mirrorImageX = false; c.useRigidFacePose = true;
        c.referenceEyeDistanceFromScreen = .6f; c.measuredEyeSeparationMeters = .063f;
        source.Calibration = c;
        display.Configure(plane, source);
        display.PoseUpdated += RecordProjection;
        cameraObject.SetActive(true);
        cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Physical 8 cm reference cube"; cube.transform.SetParent(root.transform, false);
        cube.transform.localScale = Vector3.one * .08f;
        // Rendering stays disabled: no GPU, TAA, URP, or end-of-frame dependency.
        cube.GetComponent<Renderer>().enabled = false;
        origin.Rebased += ShiftWorldContent;
        poses = 0;
    }

    [UnityTearDown]
    public IEnumerator DestroyPipeline()
    {
        if (display != null) display.PoseUpdated -= RecordProjection;
        if (origin != null) origin.Rebased -= ShiftWorldContent;
        Object.Destroy(root);
        yield return null;
    }

    private void RecordProjection(Vector3 eye, bool tracking, float confidence)
    {
        // PoseUpdated is raised after the package writes its CPU projection.
        // This disabled camera never renders, so the snapshot is unjittered.
        // Do not recalculate with OffAxisProjection: test the matrix actually applied.
        unjitteredProjection = camera.projectionMatrix;
        view = camera.worldToCameraMatrix;
        poses++;
    }

    private void ShiftWorldContent(Vector3 shift) => cube.transform.position -= shift;

    private IEnumerator NextPose(int updates = 1)
    {
        // Coroutines resume after Update, before LateUpdate. Navigation/rebase
        // callers request two poses to include the next complete Update/LateUpdate.
        int target = poses + updates;
        double deadline = Time.realtimeSinceStartupAsDouble + 3;
        while (poses < target && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        Assert.That(poses, Is.GreaterThanOrEqualTo(target), "HeadTrackedDisplay.LateUpdate did not run.");
    }

    private IEnumerator Settle(Vector3 eye)
    {
        source.Eye = eye;
        double deadline = Time.realtimeSinceStartupAsDouble + 4;
        do
        {
            yield return NextPose();
            if (display.IsTracking && Vector3.Distance(display.EyePositionMeters, eye) < .0002f) break;
        } while (Time.realtimeSinceStartupAsDouble < deadline);
        Assert.That(display.HasFreshEyeEstimate, Is.True);
        Assert.That(display.IsTracking, Is.True);
        AssertVector(display.EstimatedEyePositionMeters, eye, .00002f, "Estimated metric eye");
        AssertVector(display.EyePositionMeters, eye, .0002f, "Filtered/rendered eye");
    }

    private IEnumerator CaptureBasicReference()
    {
        source.Eye = Neutral;
        yield return NextPose();
        Assert.That(display.HasFreshEyeEstimate, Is.True);
        Assert.That(display.CaptureReference(), Is.True);
        Assert.That(display.Calibration.referenceEyeSpanPixels, Is.EqualTo(63f).Within(.0001f));
        savedCalibration = JsonUtility.ToJson(display.Calibration);
        yield return Settle(Neutral);
    }

    [UnityTest]
    public IEnumerator BasicImageObservationsDriveMetricXYZAndPhysicalCubeProjection()
    {
        yield return CaptureBasicReference();
        yield return ExercisePhysicalMovement();
    }

    [UnityTest]
    public IEnumerator RigidPoseObservationsDriveMetricXYZWithWebcamExtrinsics()
    {
        source.RigidPose = true;
        display.Calibration.webcamPosition = new Vector3(.02f, .17f, -.025f);
        display.Calibration.webcamEulerDegrees = new Vector3(-12, 4, 0);
        display.Calibration.mirrorImageX = true;
        savedCalibration = JsonUtility.ToJson(display.Calibration);
        yield return ExercisePhysicalMovement();
    }

    private IEnumerator ExercisePhysicalMovement()
    {
        var eyes = new[] { Neutral, new Vector3(.1f, 0, -.6f), new Vector3(-.1f, 0, -.6f),
            new Vector3(0, .06f, -.6f), new Vector3(0, -.06f, -.6f),
            new Vector3(0, 0, -.4f), new Vector3(0, 0, -.9f), new Vector3(.08f, .04f, -.45f) };
        foreach (Vector3 eye in eyes)
        {
            yield return Settle(eye);
            AssertScreenCorners();
            // Near metric depths on both sides of the screen, not a distant skyline.
            foreach (float depth in new[] { -.10f, 0f, .15f, .40f })
            {
                cube.transform.SetPositionAndRotation(plane.TransformPoint(new Vector3(0, 0, depth)), plane.rotation);
                AssertCubeSightlines();
            }
            Assert.That(JsonUtility.ToJson(display.Calibration), Is.EqualTo(savedCalibration));
        }
        yield return Settle(Neutral);
    }

    [UnityTest]
    public IEnumerator BasicDepthNeedsExplicitReferenceAndCaptureEnablesDepthMotion()
    {
        // Records the package contract underlying the viewer's first-setup risk.
        source.Eye = new Vector3(0, 0, -.4f);
        yield return NextPose();
        Assert.That(display.HasFreshEyeEstimate, Is.True);
        Assert.That(display.Calibration.referenceEyeSpanPixels, Is.Zero);
        Assert.That(display.EstimatedEyePositionMeters.z, Is.EqualTo(-.6f).Within(.00002f));
        yield return CaptureBasicReference();
        yield return Settle(new Vector3(0, 0, -.4f));
        AssertScreenCorners();
    }

    [UnityTest]
    public IEnumerator TrackingOffFreezesRenderButMeasurementsFilterAndFlightContinue()
    {
        yield return CaptureBasicReference();
        yield return Settle(new Vector3(.08f, .03f, -.5f));
        yield return new WaitForSecondsRealtime(.2f);
        yield return NextPose();
        display.FilterMinimumCutoffHz = .5f; display.FilterSpeedCoefficient = 0;
        Vector3 frozen = display.EyePositionMeters;
        Vector3 expectedFiltered = frozen;
        double previousTimestamp = display.LatestObservation.receivedAtSeconds;
        Action<Vector3, bool, float> accumulateExpected = (_, tracking, confidence) =>
        {
            if (!tracking) return;
            double timestamp = display.LatestObservation.receivedAtSeconds;
            float dt = (float)(timestamp - previousTimestamp);
            if (dt > .3f) expectedFiltered = display.EstimatedEyePositionMeters;
            else if (dt > 0)
            {
                // Independent constant-cutoff low-pass recurrence, not AdaptiveEyeFilter.
                float alpha = (2f * Mathf.PI * .5f * dt) / (1f + 2f * Mathf.PI * .5f * dt);
                expectedFiltered += alpha * (display.EstimatedEyePositionMeters - expectedFiltered);
            }
            previousTimestamp = timestamp;
        };
        display.PoseUpdated += accumulateExpected;
        Matrix4x4 frozenProjection = unjitteredProjection;
        display.TrackingEnabled = false;
        int beforeSequence = display.LatestObservation.sequence;
        source.Eye = new Vector3(-.07f, -.04f, -.8f);
        var input = new AbsoluteInput { Position = new Vector3(17, 5, 30), Rotation = Quaternion.Euler(8, 45, 0) };
        flight.InputSource = input;
        double until = Time.realtimeSinceStartupAsDouble + .8;
        while (Time.realtimeSinceStartupAsDouble < until)
        {
            yield return NextPose(2);
            AssertVector(display.EyePositionMeters, frozen, .000001f, "Frozen render eye");
            AssertMatrix(unjitteredProjection, frozenProjection);
        }
        Assert.That(display.LatestObservation.sequence, Is.GreaterThan(beforeSequence));
        Assert.That(display.HasFreshEyeEstimate && display.IsTracking, Is.True);
        AssertVector(display.EstimatedEyePositionMeters, source.Eye, .00002f, "Live estimate while frozen");
        AssertVector(flight.CityPosition, input.Position, .0001f, "Flight continues while frozen");
        AssertVector(camera.transform.position, plane.TransformPoint(frozen), .0001f, "Rig-local frozen offset");
        AssertScreenCorners();
        display.TrackingEnabled = true;
        // The slow filter deliberately has not settled. A broken implementation
        // that pauses filtering then resets to the latest raw sample cannot pass.
        if (Vector3.Distance(expectedFiltered, source.Eye) < .002f)
            Assert.Inconclusive("Runner stalls reset/settled the filter; repeat without long frames.");
        yield return NextPose();
        if (Vector3.Distance(expectedFiltered, source.Eye) < .002f)
            Assert.Inconclusive("A resume-time stall reset the filter; repeat without long frames.");
        AssertVector(display.EyePositionMeters, expectedFiltered, .00005f, "Resume background-filtered trajectory");
        display.PoseUpdated -= accumulateExpected;
        AssertScreenCorners();
        Assert.That(JsonUtility.ToJson(display.Calibration), Is.EqualTo(savedCalibration));
    }

    [UnityTest]
    public IEnumerator BriefObservationLossHoldsTheLastFilteredEye()
    {
        yield return CaptureBasicReference();
        yield return Settle(new Vector3(.1f, .04f, -.45f));
        Vector3 held = display.EyePositionMeters;
        double lastGood = display.LatestObservation.receivedAtSeconds;
        source.Found = false;
        yield return NextPose();
        Assert.That(display.HasFreshEyeEstimate || display.IsTracking, Is.False);
        // Separate timing-sensitive coverage so an overloaded runner cannot
        // suppress the independent stale/recovery regression below.
        if (Time.realtimeSinceStartupAsDouble - lastGood >= .10)
            Assert.Inconclusive("Runner skipped the short hold interval; rerun at a higher frame rate.");
        AssertVector(display.EyePositionMeters, held, .00002f, "Brief loss holds the filtered eye");
    }

    [UnityTest]
    public IEnumerator LostOrStaleObservationReturnsNeutralAndRecovers()
    {
        yield return CaptureBasicReference();
        Vector3 tracked = new Vector3(.1f, .04f, -.45f);
        yield return Settle(tracked);
        Vector3 held = display.EyePositionMeters;
        source.Found = false;
        yield return NextPose();
        Assert.That(display.IsTracking || display.HasFreshEyeEstimate, Is.False);
        yield return new WaitForSecondsRealtime(.7f);
        yield return NextPose();
        Assert.That(Vector3.Distance(display.EyePositionMeters, Neutral), Is.LessThan(Vector3.Distance(held, Neutral) * .5f));
        AssertScreenCorners();
        source.Found = true;
        yield return Settle(new Vector3(-.06f, .02f, -.8f));
        AssertScreenCorners();
        // A found=true packet can still be stale because processing age counts.
        source.FrameAgeMilliseconds = 400;
        yield return NextPose();
        Assert.That(display.LatestObservation.IsUsable, Is.True);
        Assert.That(display.IsTracking || display.HasFreshEyeEstimate, Is.False);
        Assert.That(display.CaptureReference(), Is.False, "Stale packets cannot replace the reference.");
        Assert.That(JsonUtility.ToJson(display.Calibration), Is.EqualTo(savedCalibration));
        source.FrameAgeMilliseconds = 0;
        yield return Settle(tracked);
        // A disconnected source that keeps its last packet also times out.
        source.Publish = false;
        yield return new WaitForSecondsRealtime(.35f);
        yield return NextPose();
        Assert.That(display.HasFreshEyeEstimate, Is.False);
        source.Publish = true;
        yield return Settle(Neutral);
        Assert.That(JsonUtility.ToJson(display.Calibration), Is.EqualTo(savedCalibration));
    }

    [UnityTest]
    public IEnumerator FlightAndFloatingOriginKeepProjectionAndCityGeometryConsistent()
    {
        yield return CaptureBasicReference();
        yield return Settle(new Vector3(.08f, .025f, -.5f));
        var input = new AbsoluteInput { Position = new Vector3(1225, 2, 3585), Rotation = Quaternion.Euler(9, -151.5f, 0) };
        flight.InputSource = input;
        yield return NextPose(2);
        cube.transform.SetPositionAndRotation(plane.TransformPoint(new Vector3(.02f, -.02f, .2f)), plane.rotation);
        AssertScreenCorners(.002f);
        AssertCubeSightlines(.002f);
        Vector3 cityCube = origin.CityPosition(cube.transform);
        Vector2 projectedBefore = Project(cube.transform.position);
        Vector3 eyeBefore = display.EyePositionMeters;
        int rebases = 0;
        origin.Rebased += _ => rebases++;
        origin.rebaseDistance = 500;
        yield return NextPose(2);
        Assert.That(rebases, Is.EqualTo(1));
        Assert.That(origin.Offset.sqrMagnitude, Is.GreaterThan(0));
        AssertVector(flight.CityPosition, input.Position, .001f, "Rebase preserves city position");
        AssertVector(origin.CityPosition(cube.transform), cityCube, .001f, "Content rebased with world");
        AssertVector(display.EyePositionMeters, eyeBefore, .0002f, "Rebase preserves metric eye");
        Assert.That(Vector2.Distance(Project(cube.transform.position), projectedBefore), Is.LessThan(.002f));
        AssertScreenCorners(.002f);
        AssertCubeSightlines(.002f);
        Assert.That(JsonUtility.ToJson(display.Calibration), Is.EqualTo(savedCalibration));
    }

    [UnityTest]
    public IEnumerator AdaptiveFilterConsumesEachObservationOnceAndSmoothsMetricSteps()
    {
        yield return CaptureBasicReference();
        // Exit the initial 120ms reacquisition blend before isolating the filter.
        yield return new WaitForSecondsRealtime(.2f);
        yield return NextPose();
        display.FilterMinimumCutoffHz = .5f;
        display.FilterSpeedCoefficient = 0;
        double previousSample = display.LatestObservation.receivedAtSeconds;
        source.Eye = new Vector3(.1f, 0, -.6f);
        yield return NextPose();
        if (display.LatestObservation.receivedAtSeconds - previousSample >= .3)
            Assert.Inconclusive("Runner skipped the filter's 300ms reset interval; rerun with shorter frame times.");
        AssertVector(display.EstimatedEyePositionMeters, source.Eye, .00002f, "Raw step reached estimator");
        float filteredX = display.EyePositionMeters.x;
        Assert.That(filteredX, Is.GreaterThan(0).And.LessThan(.099f), "Adaptive filtering must smooth the step.");
        source.Publish = false;
        int reads = source.ReadCount;
        double timestamp = display.LatestObservation.receivedAtSeconds;
        for (int i = 0; i < 3; i++)
        {
            yield return NextPose();
            if (Time.realtimeSinceStartupAsDouble - timestamp >= .25)
                Assert.Inconclusive("Runner consumed the observation freshness window; rerun with shorter frame times.");
            Assert.That(display.HasFreshEyeEstimate, Is.True);
            Assert.That(display.LatestObservation.receivedAtSeconds, Is.EqualTo(timestamp));
            Assert.That(display.EyePositionMeters.x, Is.EqualTo(filteredX).Within(.000001f),
                "Rendering the same sample again must not advance the adaptive filter.");
        }
        Assert.That(source.ReadCount, Is.GreaterThan(reads), "Source is polled even when its sample is unchanged.");
        source.Publish = true;
        yield return Settle(source.Eye);
        AssertScreenCorners();
    }

    [UnityTest]
    public IEnumerator ViewingDistanceCorrectionFlowsThroughFilterToProjectionWithoutChangingXY()
    {
        source.RigidPose = true;
        source.Eye = new Vector3(.06f, -.03f, -.7f);
        yield return NextPose();
        var c = display.Calibration;
        Assert.That(c.viewingDistance.Fit(.45f, .4f, .95f, .8f,
            ViewingDistanceCalibration.Setup(c, display.LatestObservation)), Is.True);
        var expected = new Vector3(.06f, -.03f, -.6f);
        double deadline = Time.realtimeSinceStartupAsDouble + 4;
        do { yield return NextPose(); }
        while (Vector3.Distance(display.EyePositionMeters, expected) > .0002f && Time.realtimeSinceStartupAsDouble < deadline);
        AssertVector(display.UncalibratedEyePositionMeters, source.Eye, .00002f, "Raw eye");
        AssertVector(display.EstimatedEyePositionMeters, expected, .00002f, "Corrected eye");
        AssertVector(display.EyePositionMeters, expected, .0002f, "Filtered corrected eye");
        AssertScreenCorners();
    }

    private Vector2 Project(Vector3 world)
    {
        Vector4 clip = unjitteredProjection * view * new Vector4(world.x, world.y, world.z, 1);
        Assert.That(clip.w, Is.GreaterThan(camera.nearClipPlane));
        return new Vector2(.5f + clip.x / clip.w * .5f, .5f + clip.y / clip.w * .5f);
    }

    private void AssertScreenCorners(float tolerance = .0001f)
    {
        var c = display.Calibration;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
        {
            Vector3 world = plane.TransformPoint(new Vector3(x * c.screenWidth * .5f, y * c.screenHeight * .5f, 0));
            Vector2 actual = Project(world);
            Assert.That(actual.x, Is.EqualTo((x + 1) * .5f).Within(tolerance), "Screen corner X");
            Assert.That(actual.y, Is.EqualTo((y + 1) * .5f).Within(tolerance), "Screen corner Y");
        }
    }

    private void AssertCubeSightlines(float tolerance = .0001f)
    {
        Vector3 eye = display.EyePositionMeters;
        var c = display.Calibration;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            Vector3 world = cube.transform.TransformPoint(new Vector3(x, y, z) * .5f);
            Vector3 point = plane.InverseTransformPoint(world);
            // Ray/physical-screen intersection is an independent projection oracle.
            float t = -eye.z / (point.z - eye.z);
            Vector3 screenHit = eye + t * (point - eye);
            Vector2 expected = new Vector2(.5f + screenHit.x / c.screenWidth, .5f + screenHit.y / c.screenHeight);
            Assert.That(Vector2.Distance(Project(world), expected), Is.LessThan(tolerance), "8cm cube vertex sightline");
        }
    }

    private static void AssertVector(Vector3 actual, Vector3 expected, float tolerance, string context) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(tolerance), context);

    private static void AssertMatrix(Matrix4x4 actual, Matrix4x4 expected)
    {
        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++)
            Assert.That(actual[row, column], Is.EqualTo(expected[row, column]).Within(.000001f));
    }
}
