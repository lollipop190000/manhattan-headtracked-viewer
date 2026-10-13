using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Manhattan.Viewer
{
    public static class FlightSpeedLimits
    {
        public const float Minimum = 1f, Maximum = 100f;
        public static float Clamp(float value) => float.IsFinite(value) ? Mathf.Clamp(value, Minimum, Maximum) : Minimum;
    }

    public struct MotionCommand
    {
        public Vector3 localMovement;
        public Vector2 lookDegrees;
        public bool hasAbsolutePose;
        public Vector3 cityPosition;
        public Quaternion cityRotation;
    }

    /// <summary>Hardware adapters supply metres-per-second direction and angular deltas through this interface.</summary>
    public interface IMotionInputSource { MotionCommand Sample(float deltaTime); }
    public interface ITranslationConstraint { Vector3 Constrain(Vector3 currentCityPosition, Vector3 requestedCityPosition); }

    public sealed class KeyboardMotionInput : MonoBehaviour, IMotionInputSource
    {
        public bool InputEnabled { get; set; }
        public bool Captured { get; private set; }
        public float MouseSensitivity { get; set; } = .09f;
        public event Action EscapePressed;

        public void Capture(bool capture)
        {
            Captured = capture && InputEnabled;
            Cursor.lockState = Captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !Captured;
        }

        public MotionCommand Sample(float deltaTime)
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Capture(false);
                EscapePressed?.Invoke();
            }
            if (InputEnabled && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())) Capture(true);
            if (!InputEnabled || !Captured || keyboard == null) return default;
            float x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            float z = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            float y = (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0);
            Vector2 delta = mouse != null ? mouse.delta.ReadValue() * MouseSensitivity : Vector2.zero;
            return new MotionCommand { localMovement = Vector3.ClampMagnitude(new Vector3(x, y, z), 1), lookDegrees = delta };
        }

        private void OnDisable() => Capture(false);
    }

    [DefaultExecutionOrder(-300)]
    public sealed class FreeFlightController : MonoBehaviour
    {
        private float speed = 10f;
        private float yaw, pitch;
        public float Speed { get => speed; set => speed = FlightSpeedLimits.Clamp(value); }
        public IMotionInputSource InputSource { get; set; }
        public ITranslationConstraint TranslationConstraint { get; set; }
        public FloatingOrigin Origin { get; set; }
        public Vector3 Velocity { get; private set; }
        public Vector3 CityPosition => Origin != null ? Origin.CityPosition(transform) : transform.position;
        public event Action Teleported;

        public void SetPose(Vector3 cityPosition, Quaternion rotation)
        {
            transform.SetPositionAndRotation(Origin != null ? cityPosition - Origin.Offset : cityPosition, rotation);
            yaw = rotation.eulerAngles.y;
            pitch = Mathf.DeltaAngle(0, rotation.eulerAngles.x);
            Velocity = Vector3.zero;
            Teleported?.Invoke();
        }

        private void Update()
        {
            if (InputSource == null) { Velocity = Vector3.zero; return; }
            var command = InputSource.Sample(Time.unscaledDeltaTime);
            if (command.hasAbsolutePose)
            {
                Vector3 old = CityPosition;
                Vector3 absoluteRequested = TranslationConstraint != null ? TranslationConstraint.Constrain(old, command.cityPosition) : command.cityPosition;
                if (Quaternion.Angle(transform.rotation, command.cityRotation) > 45f ||
                    Vector3.Distance(old, absoluteRequested) > 100f) Teleported?.Invoke();
                transform.SetPositionAndRotation(absoluteRequested - (Origin != null ? Origin.Offset : Vector3.zero), command.cityRotation);
                yaw = command.cityRotation.eulerAngles.y;
                pitch = Mathf.DeltaAngle(0, command.cityRotation.eulerAngles.x);
                Velocity = (CityPosition - old) / Mathf.Max(Time.unscaledDeltaTime, .0001f);
                return;
            }
            yaw += command.lookDegrees.x;
            pitch = Mathf.Clamp(pitch - command.lookDegrees.y, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 requestedVelocity = GroundPlaneMovement(command.localMovement, transform.rotation, yaw) * speed;
            Vector3 current = CityPosition;
            Vector3 requested = current + requestedVelocity * Time.unscaledDeltaTime;
            if (TranslationConstraint != null) requested = TranslationConstraint.Constrain(current, requested);
            transform.position = requested - (Origin != null ? Origin.Offset : Vector3.zero);
            Velocity = (requested - current) / Mathf.Max(Time.unscaledDeltaTime, .0001f);
        }

        /// <summary>WASD follows the view azimuth on world XZ; Q/E follows fixed world up.</summary>
        public static Vector3 GroundPlaneMovement(Vector3 localMovement, Quaternion viewRotation, float yawDegrees)
        {
            Vector3 forward = Vector3.ProjectOnPlane(viewRotation * Vector3.forward, Vector3.up);
            // Projected forward loses its azimuth at a vertical view. The controller retains
            // yaw separately, so this fallback neither divides by zero nor invents a new heading.
            if (forward.sqrMagnitude <= .000001f)
                forward = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            // Orthonormal horizontal axes preserve the sampled analog/diagonal magnitude.
            return right * localMovement.x + Vector3.up * localMovement.y + forward * localMovement.z;
        }
    }

    [DefaultExecutionOrder(-200)]
    public sealed class FloatingOrigin : MonoBehaviour
    {
        public Transform observer;
        public float rebaseDistance = 500f;
        private double east, north;
        public Vector3 Offset => new Vector3((float)east, 0f, (float)north);
        public event Action<Vector3> Rebased;
        public Vector3 CityPosition(Transform value) => value.position + Offset;

        private void Update()
        {
            if (observer == null) return;
            Vector3 p = observer.position;
            if (new Vector2(p.x, p.z).sqrMagnitude < rebaseDistance * rebaseDistance) return;
            Vector3 shift = new Vector3(Mathf.Round(p.x / rebaseDistance) * rebaseDistance, 0f,
                Mathf.Round(p.z / rebaseDistance) * rebaseDistance);
            east += shift.x; north += shift.z;
            observer.position -= shift;
            Rebased?.Invoke(shift);
        }
    }

    /// <summary>Distance-parametrized repeatable routes use the same motion input interface as hardware.</summary>
    public sealed class BenchmarkRouteInput : IMotionInputSource
    {
        private readonly Vector3[] waypoints;
        private readonly float speed;
        private readonly float[] segmentEnds;
        private float elapsed;
        public bool Complete { get; private set; }
        public float Duration { get; }
        public float Elapsed => elapsed;
        public float Speed => speed;

        public BenchmarkRouteInput(Vector3[] points, float metresPerSecond)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("A route needs at least two waypoints.", nameof(points));
            waypoints = (Vector3[])points.Clone(); speed = FlightSpeedLimits.Clamp(metresPerSecond);
            segmentEnds = new float[points.Length - 1];
            for (int i = 0; i < segmentEnds.Length; i++)
                segmentEnds[i] = (i == 0 ? 0 : segmentEnds[i - 1]) + Vector3.Distance(points[i], points[i + 1]) / speed;
            Duration = segmentEnds[segmentEnds.Length - 1];
        }

        public MotionCommand Sample(float deltaTime)
        {
            elapsed = Mathf.Min(Duration, elapsed + Mathf.Max(0, deltaTime));
            Complete = elapsed >= Duration;
            int segment = 0;
            while (segment < segmentEnds.Length - 1 && elapsed > segmentEnds[segment]) segment++;
            float startTime = segment == 0 ? 0 : segmentEnds[segment - 1];
            float t = Mathf.InverseLerp(startTime, segmentEnds[segment], elapsed);
            Vector3 direction = waypoints[segment + 1] - waypoints[segment];
            return new MotionCommand { hasAbsolutePose = true, cityPosition = Vector3.Lerp(waypoints[segment], waypoints[segment + 1], t),
                cityRotation = direction.sqrMagnitude > .001f ? Quaternion.LookRotation(direction) : Quaternion.identity };
        }
    }
}
