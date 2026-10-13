using HeadTracked.Display;
using UnityEngine;

// Test assembly only: never starts a tracker or changes the player's calibration.
public sealed class SyntheticHeadObservationSource : HeadObservationSource
{
    public Vector3 Eye = new Vector3(0, 0, -.6f);
    public DisplayCalibration Calibration;
    public bool Found = true;
    public bool Available = true;
    public bool Publish = true;
    public bool RigidPose;
    public float FrameAgeMilliseconds;
    public int ReadCount { get; private set; }
    private HeadObservation latest;
    private int sequence;
    public override string Status => "Synthetic test observations";

    public override bool TryGetLatest(out HeadObservation observation)
    {
        ReadCount++;
        if (Publish)
        {
            latest = ImageOf(Eye, Calibration);
            latest.found = Found;
            latest.poseSupported = RigidPose;
            latest.poseValid = Found;
            latest.poseConfidence = .9f;
            latest.receivedAtSeconds = Time.realtimeSinceStartupAsDouble;
            latest.frameAgeMs = FrameAgeMilliseconds;
            latest.sequence = ++sequence;
        }
        observation = latest;
        return Available;
    }

    // Independent pinhole forward model: 640x480, 600px focal length, 63mm IPD.
    // It produces observations, never camera transforms or projection matrices.
    public static HeadObservation ImageOf(Vector3 screenEye, DisplayCalibration calibration)
    {
        Vector3 opticalEye = Quaternion.Inverse(Quaternion.Euler(calibration.webcamEulerDegrees)) *
            (screenEye - calibration.webcamPosition);
        if (calibration.mirrorImageX) opticalEye.x = -opticalEye.x;
        float distance = -opticalEye.z;
        float x = .5f + 600f * opticalEye.x / (distance * 640f);
        float y = .5f - 600f * opticalEye.y / (distance * 480f);
        float halfSpan = 600f * .063f / (2f * distance * 640f);
        return new HeadObservation
        {
            found = true, confidence = 1f, frameWidth = 640, frameHeight = 480,
            leftEye = new Vector2(x - halfSpan, y), rightEye = new Vector2(x + halfSpan, y),
            eyeSpanForeshortening = 1f,
            poseEyeCamera = new Vector3(opticalEye.x, -opticalEye.y, -opticalEye.z)
        };
    }
}
