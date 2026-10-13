using System;
using System.Collections.Generic;
using UnityEngine;

namespace Manhattan.Viewer
{
    [Serializable]
    public struct MeasurementSample
    {
        public int sequence;
        public double receivedSeconds;
        public float reportedProcessingAgeMs;
        public bool fresh, valid, atViewingRangeBoundary, anglesKnown;
        public Vector3 raw, input, rendered, reportedEulerDegrees;
        public bool Usable => fresh && valid && sequence > 0 && !atViewingRangeBoundary &&
            Finite(receivedSeconds) && receivedSeconds >= 0 &&
            Finite(reportedProcessingAgeMs) && reportedProcessingAgeMs >= 0 && reportedProcessingAgeMs <= 100 &&
            Finite(raw) && Finite(input) && Finite(rendered) && (!anglesKnown || Finite(reportedEulerDegrees));
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }

    [Serializable]
    public sealed class MeasurementVectorStats
    {
        public Vector3 mean, standardDeviation;
        public static MeasurementVectorStats Calculate(IReadOnlyList<MeasurementSample> samples, Func<MeasurementSample, Vector3> value, bool angles = false)
        {
            var result = new MeasurementVectorStats();
            if (samples.Count == 0) return result;
            Vector3 origin = value(samples[0]);
            for (int i = 0; i < samples.Count; i++) result.mean += Unwrap(value(samples[i]), origin, angles);
            result.mean /= samples.Count;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 d = Unwrap(value(samples[i]), origin, angles) - result.mean;
                sum += new Vector3(d.x * d.x, d.y * d.y, d.z * d.z);
            }
            result.standardDeviation = new Vector3(Mathf.Sqrt(sum.x / samples.Count), Mathf.Sqrt(sum.y / samples.Count), Mathf.Sqrt(sum.z / samples.Count));
            return result;
        }
        private static Vector3 Unwrap(Vector3 v, Vector3 origin, bool angles) => !angles ? v : origin + new Vector3(
            Mathf.DeltaAngle(origin.x, v.x), Mathf.DeltaAngle(origin.y, v.y), Mathf.DeltaAngle(origin.z, v.z));
    }

    [Serializable]
    public sealed class MeasurementHold
    {
        public int uniqueObservations;
        public bool anglesKnown;
        public MeasurementVectorStats raw, input, rendered, reportedAngles;
        public float reportedAgeMedianMs, reportedAgeP95Ms;
        public static MeasurementHold Calculate(IReadOnlyList<MeasurementSample> samples)
        {
            var hold = new MeasurementHold { uniqueObservations = samples.Count, anglesKnown = samples.Count > 0,
                raw = MeasurementVectorStats.Calculate(samples, s => s.raw), input = MeasurementVectorStats.Calculate(samples, s => s.input),
                rendered = MeasurementVectorStats.Calculate(samples, s => s.rendered) };
            var ages = new List<float>(samples.Count);
            for (int i = 0; i < samples.Count; i++) { hold.anglesKnown &= samples[i].anglesKnown; ages.Add(samples[i].reportedProcessingAgeMs); }
            if (hold.anglesKnown) hold.reportedAngles = MeasurementVectorStats.Calculate(samples, s => s.reportedEulerDegrees, true);
            ages.Sort();
            if (ages.Count > 0) { hold.reportedAgeMedianMs = ages[(ages.Count - 1) / 2]; hold.reportedAgeP95Ms = ages[Mathf.RoundToInt((ages.Count - 1) * .95f)]; }
            return hold;
        }
        // Engineering hold-quality screen. This is not a perceptual acceptance threshold.
        public bool Stable => uniqueObservations >= 20 && Within(raw.standardDeviation, new Vector3(.005f, .005f, .015f)) &&
            Within(input.standardDeviation, new Vector3(.005f, .005f, .015f)) && Within(rendered.standardDeviation, new Vector3(.005f, .005f, .015f)) &&
            (!anglesKnown || Within(reportedAngles.standardDeviation, Vector3.one * 2f));
        public static bool Within(Vector3 v, Vector3 limit) => Mathf.Abs(v.x) <= limit.x && Mathf.Abs(v.y) <= limit.y && Mathf.Abs(v.z) <= limit.z;
    }

    public sealed class MeasurementReadiness
    {
        private readonly List<MeasurementSample> samples = new List<MeasurementSample>(128);
        private int lastSequence = -1;
        private double lastReceived = -1;
        public int Count => samples.Count;
        public MeasurementHold Statistics => MeasurementHold.Calculate(samples);
        public string Reason { get; private set; } = "Waiting for fresh, valid unique observations.";
        public bool Ready => samples.Count >= 20 && samples[samples.Count - 1].receivedSeconds - samples[0].receivedSeconds >= 1.95 && Statistics.Stable;
        public bool Push(MeasurementSample sample)
        {
            if (!sample.Usable) { Clear("Input unavailable, stale, non-finite, at range boundary, or missing frame identity."); return false; }
            if (sample.sequence == lastSequence) return false;
            if (sample.sequence < lastSequence || sample.receivedSeconds <= lastReceived || (lastReceived >= 0 && sample.receivedSeconds - lastReceived > .1))
            { Clear("Source restarted or observation clock moved backwards."); return false; }
            lastSequence = sample.sequence; lastReceived = sample.receivedSeconds; samples.Add(sample);
            while (samples.Count > 1 && sample.receivedSeconds - samples[0].receivedSeconds > 2.1) samples.RemoveAt(0);
            Reason = Ready ? "Fresh, valid and steady. Engineering checks only." : "Hold still for 2 seconds; raw and rendered eyes must be steady.";
            return true;
        }
        public void Clear(string reason = "Waiting for fresh, valid unique observations.")
        { samples.Clear(); lastSequence = -1; lastReceived = -1; Reason = reason; }
    }

    public enum MeasurementPhase { Idle, ReadyA, CollectA, ReadyB, CollectB, ReadyReturn, CollectReturn, Complete }
    [Serializable]
    public sealed class MeasurementMarker
    {
        public string stage, utc;
        public double applicationSeconds;
        public int sequence;
    }
    [Serializable]
    public sealed class TrackingMeasurementReport
    {
        public int schema = 1;
        public string sessionId, calibrationContext, note;
        public float userDeclaredEyeDistanceMeters, userDeclaredMotionMeters;
        // Negative means UNKNOWN, not a claim of exact ruler measurements.
        public float userDeclaredDistanceUncertaintyMeters, userDeclaredMotionUncertaintyMeters;
        public bool userConfirmedMeasurements, captureTimestampAvailable, anglesKnown, lateralUncontaminated, directionsMatch, returnMatches;
        public MeasurementHold a, b, returned;
        public Vector3 rawDelta, inputDelta, renderedDelta, rawReturnDelta, renderedReturnDelta;
        public MeasurementAngleChange reportedAngleDelta, reportedReturnAngleDelta;
        public MeasurementMarker[] markers;
        public bool LateralChecksConsistent => userConfirmedMeasurements && anglesKnown && lateralUncontaminated && directionsMatch && returnMatches;
    }
    [Serializable]
    public sealed class MeasurementAngleChange { public Vector3 pitchYawRollDegrees; }

    public sealed class TrackingMeasurementSession
    {
        private readonly List<MeasurementSample> collecting = new List<MeasurementSample>(256);
        private readonly List<MeasurementMarker> markers = new List<MeasurementMarker>();
        private double started;
        private string context;
        private float declaredDistance, declaredMotion, distanceUncertainty, motionUncertainty;
        public MeasurementPhase Phase { get; private set; }
        public TrackingMeasurementReport Report { get; private set; }
        public string Message { get; private set; } = "Not recording.";
        public bool Active => Phase != MeasurementPhase.Idle && Phase != MeasurementPhase.Complete;
        private MeasurementHold a, b;
        public bool Begin(float eyeDistance, float motion, bool userConfirmed, string calibrationContext, bool ready, float eyeDistanceUncertainty = -1, float measuredMotionUncertainty = -1)
        {
            Cancel();
            if (!ready) { Message = "Cannot start: wait for fresh, valid and steady input."; return false; }
            if (!userConfirmed || !MeasurementSample.Finite(eyeDistance) || !MeasurementSample.Finite(motion) || !MeasurementSample.Finite(eyeDistanceUncertainty) || !MeasurementSample.Finite(measuredMotionUncertainty) || eyeDistance < .25f || eyeDistance > 1.5f || motion < .02f || motion > .5f || string.IsNullOrEmpty(calibrationContext))
            { Message = "Enter and confirm ruler-measured distance and motion first."; return false; }
            declaredDistance = eyeDistance; declaredMotion = motion; context = calibrationContext;
            distanceUncertainty = eyeDistanceUncertainty < 0 ? -1 : eyeDistanceUncertainty;
            motionUncertainty = measuredMotionUncertainty < 0 ? -1 : measuredMotionUncertainty;
            Phase = MeasurementPhase.ReadyA; Message = "A: stay at the measured start position. When steady, press F8; hold for 5 seconds."; return true;
        }
        public bool Mark(double now, string utc, MeasurementSample sample, bool ready)
        {
            if (!ready || !sample.Usable) { Message = "Mark rejected: fresh, valid and steady input is required."; return false; }
            MeasurementPhase next;
            if (Phase == MeasurementPhase.ReadyA) next = MeasurementPhase.CollectA;
            else if (Phase == MeasurementPhase.ReadyB) next = MeasurementPhase.CollectB;
            else if (Phase == MeasurementPhase.ReadyReturn) next = MeasurementPhase.CollectReturn;
            else return false;
            collecting.Clear(); started = now; Phase = next;
            markers.Add(new MeasurementMarker { stage = Phase + " start (user F8)", utc = utc, applicationSeconds = now, sequence = sample.sequence });
            Message = "Recording " + Phase + ": hold still for 5 seconds. F9 cancels."; return true;
        }
        public void Observe(MeasurementSample sample, bool unique, double now, string utc, string calibrationContext)
        {
            if (!Active) return;
            if (calibrationContext != context)
            { Cancel(); Message = "Calibration changed. Previous holds cleared; confirm measurements and begin again."; return; }
            if (!sample.Usable)
            { Restart("Input lost or calibration changed. Previous holds cleared; start again at A."); return; }
            if (Phase != MeasurementPhase.CollectA && Phase != MeasurementPhase.CollectB && Phase != MeasurementPhase.CollectReturn) return;
            if (unique) collecting.Add(sample);
            if (now - started < 5) return;
            var hold = MeasurementHold.Calculate(collecting);
            if (!hold.Stable) { Restart("Hold was not steady in raw/rendered eyes or orientation. Previous holds cleared; retry A."); return; }
            markers.Add(new MeasurementMarker { stage = Phase + " end", utc = utc, applicationSeconds = now, sequence = sample.sequence });
            if (Phase == MeasurementPhase.CollectA)
            { a = hold; Phase = MeasurementPhase.ReadyB; Message = "A recorded. Move RIGHT by the ruler-measured motion; keep distance and face direction. When steady, F8; hold 5 seconds."; }
            else if (Phase == MeasurementPhase.CollectB)
            { b = hold; Phase = MeasurementPhase.ReadyReturn; Message = "B recorded. Return LEFT to A by the same measured motion. When steady, F8; hold 5 seconds. Return is a separate check."; }
            else
            {
                Report = BuildReport(hold); Phase = MeasurementPhase.Complete;
                Message = "Complete. Numeric results are not a perceptual pass or an automatic calibration. F9 closes.";
            }
            collecting.Clear();
        }
        public void Restart(string reason)
        { collecting.Clear(); markers.Clear(); a = b = null; Report = null; Phase = MeasurementPhase.ReadyA; Message = reason; }
        public void Cancel()
        { collecting.Clear(); markers.Clear(); a = b = null; Report = null; Phase = MeasurementPhase.Idle; Message = "Not recording."; }
        private TrackingMeasurementReport BuildReport(MeasurementHold returned)
        {
            var r = new TrackingMeasurementReport { sessionId = Guid.NewGuid().ToString("N"), calibrationContext = context,
                userDeclaredEyeDistanceMeters = declaredDistance, userDeclaredMotionMeters = declaredMotion, userConfirmedMeasurements = true,
                userDeclaredDistanceUncertaintyMeters = distanceUncertainty, userDeclaredMotionUncertaintyMeters = motionUncertainty,
                captureTimestampAvailable = false, a = a, b = b, returned = returned, markers = markers.ToArray(),
                rawDelta = b.raw.mean - a.raw.mean, inputDelta = b.input.mean - a.input.mean, renderedDelta = b.rendered.mean - a.rendered.mean,
                rawReturnDelta = returned.raw.mean - b.raw.mean, renderedReturnDelta = returned.rendered.mean - b.rendered.mean };
            r.anglesKnown = a.anglesKnown && b.anglesKnown && returned.anglesKnown;
            if (r.anglesKnown)
            {
                r.reportedAngleDelta = new MeasurementAngleChange { pitchYawRollDegrees = AngleDifference(a.reportedAngles.mean, b.reportedAngles.mean) };
                r.reportedReturnAngleDelta = new MeasurementAngleChange { pitchYawRollDegrees = AngleDifference(b.reportedAngles.mean, returned.reportedAngles.mean) };
            }
            r.lateralUncontaminated = r.anglesKnown && Mathf.Abs(r.rawDelta.y) <= .01f && Mathf.Abs(r.rawDelta.z) <= .01f &&
                Mathf.Abs(r.rawReturnDelta.y) <= .01f && Mathf.Abs(r.rawReturnDelta.z) <= .01f &&
                MeasurementHold.Within(r.reportedAngleDelta.pitchYawRollDegrees, Vector3.one * 3) && MeasurementHold.Within(r.reportedReturnAngleDelta.pitchYawRollDegrees, Vector3.one * 3);
            r.directionsMatch = r.rawDelta.x > 0 && r.renderedDelta.x > 0 && r.rawReturnDelta.x < 0 && r.renderedReturnDelta.x < 0;
            r.returnMatches = MeasurementHold.Within(returned.raw.mean - a.raw.mean, Vector3.one * .01f) &&
                MeasurementHold.Within(returned.rendered.mean - a.rendered.mean, Vector3.one * .01f);
            r.note = "User-declared ruler measurements; negative uncertainty means UNKNOWN. IPD/FOV are separate assumptions. Processing age is not exposure age or motion-to-photon. One lateral pair cannot identify FOV, IPD and XYZ scale. No gain/correction is calculated. Unknown orientation blocks metric interpretation.";
            return r;
        }
        private static Vector3 AngleDifference(Vector3 from, Vector3 to) => new Vector3(Mathf.DeltaAngle(from.x, to.x), Mathf.DeltaAngle(from.y, to.y), Mathf.DeltaAngle(from.z, to.z));
    }
}
