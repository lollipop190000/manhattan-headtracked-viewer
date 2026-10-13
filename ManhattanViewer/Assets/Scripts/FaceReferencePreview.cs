using System;
using System.Security.Cryptography;
using System.Text;
using HeadTracked.Display;
using UnityEngine;

namespace Manhattan.Viewer
{
    public sealed class FaceReferencePreview
    {
        private readonly DisplayCalibration before, candidate;
        private readonly string originalContext;
        private string appliedContext;
        public Vector4 BeforeIntrinsics { get; }
        public Vector4 CandidateIntrinsics { get; }
        public bool Applied => appliedContext != null;
        public bool NeedsExplicitSave { get; private set; }
        public int FrameWidth { get; }
        public int FrameHeight { get; }
        public float DeclaredEyeDistanceMeters { get; }
        private FaceReferencePreview(DisplayCalibration original, DisplayCalibration proposed, HeadObservation observation, float declaredDistance)
        {
            before = Clone(original); candidate = Clone(proposed); originalContext = Context(original);
            FrameWidth = observation.frameWidth; FrameHeight = observation.frameHeight;
            DeclaredEyeDistanceMeters = declaredDistance;
            BeforeIntrinsics = Intrinsics(before, FrameWidth, FrameHeight); CandidateIntrinsics = Intrinsics(candidate, FrameWidth, FrameHeight);
        }
        public static bool TryCreate(DisplayCalibration original, HeadObservation observation, float userMeasuredDistance, bool measurementsConfirmed, bool inputReady, out FaceReferencePreview preview, out string error)
        {
            preview = null; error = "";
            if (!inputReady || !observation.IsUsable || !measurementsConfirmed || original == null ||
                !MeasurementSample.Finite(userMeasuredDistance) || userMeasuredDistance < .25f || userMeasuredDistance > 1.5f)
            { error = "Need fresh steady input and a confirmed measured eye-to-screen distance."; return false; }
            var clone = Clone(original); clone.referenceEyeDistanceFromScreen = userMeasuredDistance;
            if (!clone.CaptureReference(observation)) { error = "Reference capture failed; nothing changed or saved."; return false; }
            preview = new FaceReferencePreview(original, clone, observation, userMeasuredDistance); return true;
        }
        public bool TryApply(DisplayCalibration current, int width, int height, float currentDeclaredDistance, bool measurementsConfirmed, out string error)
        {
            error = "";
            if (!measurementsConfirmed || !MeasurementSample.Finite(currentDeclaredDistance) || currentDeclaredDistance != DeclaredEyeDistanceMeters || Applied || Context(current) != originalContext || width != FrameWidth || height != FrameHeight)
            { error = "Declaration, confirmation, calibration or input dimensions changed. Confirm measurements and create a new preview."; return false; }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(candidate), current); appliedContext = Context(current); NeedsExplicitSave = true; return true;
        }
        public bool TryRevert(DisplayCalibration current, out string error)
        {
            error = "";
            if (!Applied || Context(current) != appliedContext) { error = "Applied settings changed; revert would overwrite later edits."; return false; }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(before), current); appliedContext = null; NeedsExplicitSave = false; return true;
        }
        public void MarkSaved() { NeedsExplicitSave = false; }
        public static string Context(DisplayCalibration c)
        {
            if (c == null) return "";
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(c)))).Replace("-", "");
        }
        private static DisplayCalibration Clone(DisplayCalibration c) => JsonUtility.FromJson<DisplayCalibration>(JsonUtility.ToJson(c));
        private static Vector4 Intrinsics(DisplayCalibration c, int width, int height)
        { EyePoseEstimator.CameraIntrinsics(c, width, height, out float fx, out float fy, out float cx, out float cy); return new Vector4(fx, fy, cx, cy); }
    }
}
