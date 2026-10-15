using System;
using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    /// <summary>
    /// 뷰어의 이동 입력(IMotionInputSource)을 거미줄 스윙 물리로 바꿔 끼운다. 팀 코드는 수정하지 않는다.
    /// 마우스 시선과 WASD/QE는 기존 입력(look)에서 받고, 위치는 SwingPhysics가 계산해서 절대 위치(hasAbsolutePose)로 돌려준다.
    /// 각 물리 하위 스텝에서 TraversalCollision이 몸의 이동 경로를 검사하고 접촉을 물리에 전달한다.
    /// 모든 위치는 도시 좌표(떠 있는 원점 보정 전)다.
    /// </summary>
    public sealed class WebSwingMotionInput : IMotionInputSource
    {
        private const float TeleportDistance = 20f;    // 이만큼 어긋나면 다른 곳(SetPose 등)이 옮긴 것으로 본다
        private const float BlockedEpsilon = 0.01f;    // 이 이상 못 갔으면 충돌로 막힌 것

        private readonly IMotionInputSource look;
        private readonly FreeFlightController flight;
        private readonly FloatingOrigin origin;
        private float yaw, pitch;
        private bool initialized, hasRequested;
        private Vector3 lastRequested;

        public SwingPhysics Body { get; }
        /// <summary>거미줄이 붙을 수 있는 충돌 레이어. 뷰어의 충돌 프록시는 레이어 30이다.</summary>
        public int CollisionMask { get; set; } = 1 << 30;
        public float MaxRange { get; set; } = 250f;
        public bool Enabled { get; set; } = true;
        public bool Paused { get; set; }
        public Func<bool> CanSimulate { get; set; }
        public bool JumpRequested { get; set; }
        public float ReelInput { get; set; }
        public bool AutomaticRopeAssist { get; set; }
        public bool AutomaticCruiseAssist { get; set; }
        public bool MouseOwnsRope { get; private set; }
        public double LastSampleMilliseconds { get; private set; }
        public bool HandleMouseHold(bool active, bool pressed, bool held, Ray ray)
        {
            if (!active) { Release(); return false; }
            if (MouseOwnsRope && !held) Release();
            if (!pressed || !held) return false;
            bool hit = TryFire(ray);
            MouseOwnsRope = hit;
            return hit;
        }
        public int CollisionReleaseCount { get; private set; }
        public TraversalCollision Collision { get; }
        public WebRopeRoute Route { get; } = new WebRopeRoute();
        public bool Flying { get; private set; }
        public float ShotAge { get; private set; }
        public const float GestureSeconds = .12f;
        public Vector3 ShotTarget { get; private set; }
        public float ShotProgress { get; private set; }
        private float shotDuration;
        private Collider shotCollider;

        public event Action<Vector3> WebAttached;
        public event Action WebReleased;

        public static SwingSettings TraversalSettings() => new SwingSettings
        {
            ratchetRope = false, gravity = 24f, airDrag = .01f, maxSpeed = 95f, groundFriction = 14f,
            airControl = 18f, jumpSpeed = 20f, launchForwardSpeed = 24f,
            cruiseSpeed = 52f, cruiseAcceleration = 20f
        };

        public WebSwingMotionInput(IMotionInputSource lookSource, FreeFlightController flightController, FloatingOrigin floatingOrigin,
            SwingSettings settings = null)
        {
            look = lookSource;
            flight = flightController ?? throw new ArgumentNullException(nameof(flightController));
            origin = floatingOrigin;
            Body = new SwingPhysics(settings ?? TraversalSettings());
            AutomaticCruiseAssist = settings == null;
            Collision = new TraversalCollision(origin, Body);
        }

        /// <summary>
        /// 발사: 이전 거미줄을 먼저 끊고, 레이가 건물(충돌 프록시)에 맞으면 새로 붙인다. 허공을 쏘면 끊기만 한다.
        /// 맞았으면 true. 충돌 프록시가 꺼져 있으면(CityStreamer.CollisionProxiesEnabled) 맞는 것이 없다.
        /// </summary>
        public bool TryFire(Ray ray)
        {
            EnsureInitialized();
            Release();
            if (!Finite(ray.origin) || !Finite(ray.direction) || ray.direction.sqrMagnitude < .0001f) return false;
            if (!Physics.Raycast(ray, out RaycastHit hit, MaxRange, CollisionMask, QueryTriggerInteraction.Ignore)) return false;
            Vector3 anchorCity = hit.point + (origin != null ? origin.Offset : Vector3.zero);
            if (Vector3.Distance(Body.Position, anchorCity) < Body.Settings.minRopeLength) return false;
            ShotTarget = anchorCity;
            shotCollider = hit.collider;
            ShotProgress = 0f; ShotAge = 0f;
            shotDuration = Mathf.Clamp(hit.distance / 500f, .06f, .35f);
            Flying = true;
            return true;
        }

        /// <summary>
        /// 자유 비행에서 스윙 물리로 넘어간다. 지금 위치에서 속도 0으로 시작한다.
        /// Enabled가 false인 동안은 기존 입력이 그대로 통과하므로, 다시 켤 때마다 현재 비행 상태와 맞춰야 한다.
        /// </summary>
        public void Activate()
        {
            initialized = false;
            hasRequested = false;
            Enabled = true;
            EnsureInitialized();
        }

        public void Release()
        {
            MouseOwnsRope = false;
            Flying = false;
            Route.Clear();
            if (!Body.Attached) return;
            Body.Release();
            WebReleased?.Invoke();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            Body.Reset(flight.CityPosition);
            Vector3 euler = flight.transform.rotation.eulerAngles;
            yaw = euler.y;
            pitch = Mathf.DeltaAngle(0f, euler.x);
            initialized = true;
        }

        public MotionCommand Sample(float deltaTime)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return SampleCore(deltaTime); }
            finally { LastSampleMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
        }
        private MotionCommand SampleCore(float deltaTime)
        {
            MotionCommand input = look != null ? look.Sample(deltaTime) : default;
            if (!Enabled) return input;
            EnsureInitialized();
            if (CanSimulate != null) Paused = !CanSimulate();
            if (Paused) { JumpRequested = false; yaw = flight.transform.eulerAngles.y; pitch = Mathf.DeltaAngle(0f, flight.transform.eulerAngles.x); return new MotionCommand { hasAbsolutePose = true, cityPosition = flight.CityPosition, cityRotation = flight.transform.rotation }; }

            Physics.SyncTransforms();
            // 지난 프레임에 요청한 위치와 실제 위치를 비교: 크게 다르면 순간이동, 조금 다르면 충돌로 막힌 것
            Vector3 actual = flight.CityPosition;
            if (!Finite(Body.Position) || !Finite(Body.Velocity)) { Release(); Body.Reset(actual); }
            if (hasRequested)
            {
                Vector3 shortfall = lastRequested - actual;
                if (shortfall.magnitude > TeleportDistance)
                {
                    Body.Reset(actual);          // SetPose 등으로 옮겨졌다: 속도와 거미줄을 버린다
                    Flying = false;
                    WebReleased?.Invoke();
                }
                else if (shortfall.magnitude > BlockedEpsilon)
                {
                    Body.Position = actual;
                    Body.OnBlocked(shortfall);   // 막힌 방향(장애물 쪽)의 속도 성분을 지운다
                }
            }

            yaw += input.lookDegrees.x;
            pitch = Mathf.Clamp(pitch - input.lookDegrees.y, -85f, 85f);
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Body.Braking = input.localMovement.z < 0f;
            Vector3 steer = heading * new Vector3(input.localMovement.x, 0f, input.localMovement.z);   // 수평 조향
            if (input.localMovement.z < 0f)
                steer = heading * new Vector3(input.localMovement.x, 0f, 0f) - Vector3.ProjectOnPlane(Body.Velocity, Vector3.up).normalized * -input.localMovement.z;
            if (Flying)
            {
                ShotAge += deltaTime;
                ShotProgress = Mathf.Clamp01((ShotAge - GestureSeconds) / shotDuration);
                if (shotCollider == null || !shotCollider.enabled || !shotCollider.gameObject.activeInHierarchy) Release();
                else if (ShotProgress >= 1f)
                {
                    Flying = false;
                    // Attachment is passive. Space, W and manual reeling are explicit movement inputs.
                    if (Body.Attach(ShotTarget)) { Route.Begin(ShotTarget); WebAttached?.Invoke(ShotTarget); }
                }
            }
            if (Body.Attached)
            {
                float oldFixed = Route.FixedLength;
                if (shotCollider == null || !shotCollider.enabled || !shotCollider.gameObject.activeInHierarchy ||
                    !Route.Update(Body.Position, origin != null ? origin.Offset : Vector3.zero, CollisionMask)) Release();
                else Body.RouteThrough(Route.Points[0], Route.FixedLength - oldFixed);
            }
            if (JumpRequested)
            {
                bool attached = Body.Attached;
                bool jumped = Body.Jump(); JumpRequested = false;
                Debug.Log($"TRAVERSAL_JUMP accepted={jumped} position={Body.Position} velocity={Body.Velocity}");
                if (attached && !Body.Attached) WebReleased?.Invoke();
            }
            float remaining = Mathf.Min(deltaTime, .1f);
            while (remaining > .000001f)
            {
                float h = Mathf.Min(remaining, Body.Settings.maxStep);
                bool attached = Body.Attached;
                Body.WallRunRequested = input.localMovement.z > .1f;
                bool wantsWall = Body.WallRunRequested && (!Body.Attached || Body.Settings.features.preserveGlancingContact) && !Body.Grounded;
                Body.UpdateWall(wantsWall ? Collision.FindWall(Body.Position, heading * Vector3.forward, false) : Vector3.zero, wantsWall, h);
                if (attached && !Body.Attached) WebReleased?.Invoke();
                Vector3 before = Body.Position;
                if (AutomaticRopeAssist && Mathf.Abs(ReelInput) < .01f) Body.AssistRope(h);
                if (AutomaticCruiseAssist && input.localMovement.z > .1f && !Body.Grounded)
                    Body.AssistCruise(h, false);
                Body.Step(h, steer, Body.Attached ? Mathf.Clamp(ReelInput, -1f, 1f) : 0f);
                Vector3 requested = Body.Position;
                bool ropeBeforeContact = Body.Attached;
                Body.Position = Collision.Constrain(before, requested, false);
                // The analytic ground plane applies its friction inside Step; roofs need the same treatment.
                if (Body.Position.y > Body.FloorY + .01f) Body.ApplySurfaceFriction(h);
                if (ropeBeforeContact && !Body.Attached) { WebReleased?.Invoke(); CollisionReleaseCount++; }
                // A solid wall wins over rope length. Repeatedly projecting into
                // the wall and back out produced violent oscillation at short ropes.
                if (Body.Attached && Vector3.Distance(Body.Position, Body.Anchor) > Body.RopeLength + .12f)
                {
                    Release(); CollisionReleaseCount++;
                }
                if (!Finite(Body.Position) || !Finite(Body.Velocity))
                {
                    Release(); Body.Reset(Finite(before) ? before : actual);
                }
                remaining -= h;
            }

            lastRequested = Body.Position;
            hasRequested = true;
            return new MotionCommand
            {
                hasAbsolutePose = true,
                cityPosition = Body.Position,
                cityRotation = Quaternion.Euler(pitch, yaw, 0f)
            };
        }
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
