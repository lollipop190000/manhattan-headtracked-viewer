using UnityEngine;

namespace WebShooter.Swing
{
    // Gameplay adapter only: tracking, calibration and screen projection remain in the technology package.
    public sealed class TrackedLeanSteering
    {
        private bool centered;
        private float center, dwell, filtered, previousSign;
        public float Sample(float eyeX, bool fresh, float dt)
        {
            if (!fresh || !float.IsFinite(eyeX))
            { centered = false; dwell = filtered = previousSign = 0f; return 0f; }
            if (!centered) { center = eyeX; centered = true; return 0f; }
            float offset = eyeX - center;
            float target = Mathf.Sign(offset) * Mathf.Clamp01((Mathf.Abs(offset) - .025f) / .075f);
            float sign = target == 0f ? 0f : Mathf.Sign(target);
            if (sign != previousSign) dwell = 0f;
            previousSign = sign;
            dwell += Mathf.Clamp(dt, 0f, .1f);
            if (dwell < .18f) target = 0f;
            filtered = Mathf.MoveTowards(filtered, target, 3f * Mathf.Clamp(dt, 0f, .1f));
            return filtered;
        }
    }
}
