using System;
using UnityEngine;

namespace WebShooter.Swing
{
    public enum TraversalState { Airborne, Grounded, Swinging, WallRunning, Yanking }
    /// <summary>튜닝 가능한 물리 값. 모든 길이는 미터, 시간은 초. 도시 좌표(y가 위)에서 쓴다.</summary>
    [Serializable]
    public sealed class SwingSettings
    {
        public FlowFeatures features = new FlowFeatures();
        [Tooltip("중력 가속도 (m/s^2, 아래 방향)")] public float gravity = 9.81f;
        [Tooltip("선형 공기 저항 계수 (1/s). 클수록 속도가 빨리 줄어든다")] public float airDrag = 0.05f;
        [Tooltip("최대 속도 (m/s)")] public float maxSpeed = 70f;
        [Tooltip("지면의 도시 좌표 y")] public float groundHeight = 0f;
        [Tooltip("지면 위 눈높이 (m). 이동 기준점은 눈 위치다")] public float eyeHeight = 1.85f;
        [Tooltip("지면에 닿아 있을 때 수평 속도가 줄어드는 정도 (1/s)")] public float groundFriction = 5f;
        [Tooltip("공중 조향 가속도 (m/s^2)")] public float airControl = 12f;
        [Tooltip("줄을 감는 속도 (m/s)")] public float reelSpeed = 24f;
        public float payoutSpeed = 32f;
        public float jumpSpeed = 14f, launchForwardSpeed = 12f;
        public float cruiseSpeed = 34f, cruiseAcceleration = 12f;
        [Tooltip("Initial slack-only takeup window; zero keeps unassisted rope physics.")]
        public float attachmentTakeupSeconds = 0f;
        [Tooltip("이보다 가까운 곳에는 거미줄을 붙일 수 없다 (m)")] public float minRopeLength = 2f;
        [Tooltip("물리 하위 스텝의 최대 시간 (s). 프레임이 길어도 이 값 이하로 나눠 적분한다")] public float maxStep = 1f / 120f;
        [Tooltip("거미줄이 붙는 순간 앵커 쪽으로 더해 주는 속도 (m/s). 0이면 끔. 줄은 안쪽으로 당기지 않으므로 지상에서 시작하려면 필요하다")] public float attachPull = 0f;
        [Tooltip("true면 앵커에 가까워진 만큼 줄 길이도 줄인다(래칫). 끌려 올라간 뒤 줄이 다시 팽팽해지며 진자로 이어진다")] public bool ratchetRope = false;
    }

    /// <summary>
    /// 거미줄 스윙 물리: 점 질량 + 늘어나지 않는 줄(진자). 중력, 공기 저항, 지면, 공중 조향, 줄 감기.
    /// Unity 물리 엔진을 쓰지 않는 순수 코드라서 결정적이고 단위 테스트가 가능하다. 건물 충돌은 바깥(이동 제약)이 처리하고
    /// 막혔을 때 <see cref="OnBlocked"/>로 알려 주면 된다.
    /// </summary>
    public sealed class SwingPhysics
    {
        public SwingSettings Settings { get; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public bool Attached { get; private set; }
        public Vector3 Anchor { get; private set; }
        public float RopeLength { get; private set; }
        public bool Grounded { get; private set; }

        public SwingPhysics(SwingSettings settings = null) => Settings = settings ?? new SwingSettings();

        public float FloorY => Settings.groundHeight + Settings.eyeHeight;
        public TraversalState State => wallRunning ? TraversalState.WallRunning : yanking ? TraversalState.Yanking : Attached ? TraversalState.Swinging : Grounded ? TraversalState.Grounded : TraversalState.Airborne;
        public Vector3 ContactNormal { get; private set; }
        public float AssistStrength { get; set; } = 1f;
        public bool Braking { get; set; }
        public bool WallRunRequested { get; set; }
        public string LastCollisionName { get; private set; } = "";
        public int ContactSequence { get; private set; }
        public float TensionAcceleration { get; private set; }
        public float RopeCorrectionMetres { get; private set; }
        private bool wallRunning;
        private bool yanking, yankContact, yankExhausted;
        private Vector3 yankDirection;
        private float wallTime, wallCooldown;
        private bool wallExhausted;
        private float wallEscapeTime, attachmentTakeupRemaining;
        private Vector3 escapeNormal;
        public int WallDeflections { get; private set; }
        public void AssistCruise(float dt, bool braking)
        {
            if (!Attached || AssistStrength <= 0f || braking || Velocity.magnitude >= Settings.cruiseSpeed) return;
            Vector3 tangent = Vector3.ProjectOnPlane(Velocity, (Position - Anchor).normalized);
            if (tangent.sqrMagnitude > 1f)
                Velocity += tangent.normalized * (Settings.cruiseAcceleration * AssistStrength * Mathf.Clamp(dt, 0f, .1f));
        }

        public bool FrontalApproach(Vector3 normal) => -Vector3.Dot(Velocity, normal) >= Settings.features.wallApproachSpeed &&
            -Vector3.Dot(Velocity.normalized, normal) >= Settings.features.wallApproachRatio;

        public void Contact(Vector3 normal, string colliderName = null)
        {
            ContactSequence++;
            if(yanking) yankContact = true;
            if (colliderName != null) LastCollisionName = colliderName;
            ContactNormal = normal;
            float into = Vector3.Dot(Velocity, normal);
            if (Settings.features.preserveGlancingContact && !Grounded && Mathf.Abs(normal.y) < .2f && into < 0f)
            {
                float incoming = Velocity.magnitude;
                bool started = TryBeginWallRun(normal, WallRunRequested, true);
                Velocity = Vector3.ProjectOnPlane(Velocity, normal);
                if (started && Velocity.magnitude < 3f) Velocity += Vector3.up * Mathf.Min(12f, incoming * .5f);
                return;
            }
            if (into < 0f)
            {
                // Preserve tangential momentum and detach so tension cannot pull back into the wall.
                if (Attached && !wallRunning && Mathf.Abs(normal.y) < .2f && -into > 2f && Velocity.magnitude > 5f)
                {
                    Velocity = Vector3.ProjectOnPlane(Velocity, normal) * .98f + normal * Mathf.Min(18f, -into * .65f);
                    Release(); wallEscapeTime = .4f; escapeNormal = normal;
                    wallCooldown = .45f; WallDeflections++;
                }
                else Velocity -= normal * into;
            }
            if (normal.y > .65f) { Grounded = true; if(wallRunning) EndWallRun(.3f); }
        }

        private bool TryBeginWallRun(Vector3 normal, bool requested, bool frontalOnly)
        {
            if (wallRunning || !requested || Grounded || wallExhausted || wallCooldown > 0f ||
                Mathf.Abs(normal.y) >= .2f || normal.sqrMagnitude < .5f || Velocity.magnitude < 3f ||
                (frontalOnly && !FrontalApproach(normal))) return false;
            wallRunning = true; wallTime = 0f; ContactNormal = normal; Release();
            return true;
        }
        private void EndWallRun(float cooldown, bool exhausted=false)
        {
            wallRunning = false; wallTime = 0f;
            wallCooldown = Mathf.Max(wallCooldown, cooldown); wallExhausted |= exhausted;
        }

        public void UpdateWall(Vector3 normal, bool forward, float dt)
        {
            wallCooldown = Mathf.Max(0f, wallCooldown - dt);
            bool valid = forward && !Grounded && Mathf.Abs(normal.y) < .2f && normal.sqrMagnitude > .5f && Velocity.magnitude >= 3f;
            if (normal.sqrMagnitude < .5f || Grounded || !forward) wallExhausted = false;
            if (wallRunning && (!valid || wallTime >= 2f)) EndWallRun(.3f, wallTime >= 2f);
            TryBeginWallRun(normal, forward, Settings.features.preserveGlancingContact);
            if (wallRunning)
            {
                ContactNormal = normal; wallTime += dt;
                float approachSpeed = Velocity.magnitude;
                Velocity = Vector3.ProjectOnPlane(Velocity, normal);
                if (Velocity.magnitude < 3f) Velocity = Vector3.up * Mathf.Clamp(approachSpeed * .5f, 6f, 12f);
            }
        }

        public bool Jump()
        {
            if (wallRunning) { Velocity += ContactNormal * 9f + Vector3.up * 7f; EndWallRun(.3f); }
            else if (Grounded || Position.y <= FloorY + .03f) { Velocity = new Vector3(Velocity.x, Settings.jumpSpeed, Velocity.z); }
            else if (Attached) { Release(); Velocity += Vector3.up * (4f * AssistStrength); }
            else return false;
            Grounded = false;
            return true;
        }

        public void ApplySurfaceFriction(float dt)
        {
            if (!Grounded || dt <= 0f) return;
            Vector3 normal = ContactNormal.y > .65f ? ContactNormal.normalized : Vector3.up;
            Vector3 tangent = Vector3.ProjectOnPlane(Velocity, normal);
            Vector3 damped = tangent * Mathf.Exp(-Settings.groundFriction * dt);
            if (damped.sqrMagnitude < .0025f) damped = Vector3.zero;
            Velocity += damped - tangent;
        }

        public void Reset(Vector3 position)
        {
            Position = position;
            Velocity = Vector3.zero;
            Grounded = position.y <= FloorY + .03f;
            wallRunning = false;
            yanking = yankContact = yankExhausted = false; LastCollisionName = "";
            wallExhausted = false;
            wallTime = wallCooldown = wallEscapeTime = 0f; WallDeflections = 0;
            Release();
        }

        /// <summary>거미줄을 붙인다. 줄 길이는 지금 거리이므로 붙이는 순간에는 팽팽하다. 너무 가까우면 거부한다.</summary>
        public bool Attach(Vector3 anchor)
        {
            float distance = Vector3.Distance(Position, anchor);
            if (distance < Settings.minRopeLength) return false;
            // A new web owns traversal immediately. Briefly inhibit wall reentry while still near the surface.
            if (wallRunning) EndWallRun(.3f);
            yanking = yankContact = yankExhausted = false;
            Anchor = anchor;
            RopeLength = distance;
            if(Settings.features.attachmentDrop)RopeLength=Mathf.Clamp(distance*Settings.features.initialLengthRatio,Settings.minRopeLength,250f);
            Attached = true;
            attachmentTakeupRemaining = Mathf.Max(0f, Settings.attachmentTakeupSeconds);
            if (Settings.attachPull > 0f) Velocity += (anchor - Position) / distance * Settings.attachPull;
            return true;
        }

        public void Release(bool allowBoost=false)
        {
            var f=Settings.features;
            if(allowBoost && Attached && f.releaseBoost && !Grounded && Velocity.y>f.releaseMinimumUpSpeed &&
                Vector3.Dot((Position-Anchor).normalized,Vector3.down)<f.releasePhaseCosine)
            {
                Vector3 forward=Vector3.ProjectOnPlane(Velocity,Vector3.up).normalized;
                Velocity+=(forward*.8f+Vector3.up*.6f).normalized*(f.releaseBoostSpeed*AssistStrength);
            }
            Attached = false; yanking = false; attachmentTakeupRemaining = 0f;
        }

        public bool BeginYank()
        {
            if(!Attached || yankExhausted || Vector3.Distance(Position,Anchor) <= Settings.minRopeLength + Settings.features.yankWallClearance) return false;
            if(yanking) return true;
            yanking = true; yankContact = false; yankDirection = (Anchor-Position).normalized;
            if(Settings.features.preserveTransitionMomentum)
                Velocity += yankDirection * Mathf.Max(0f,Settings.reelSpeed-Vector3.Dot(Velocity,yankDirection)) * Settings.features.yankStartBlend;
            return true;
        }
        public void FinishYank()
        {
            if(!yanking) return;
            if(Settings.features.preserveTransitionMomentum && !yankContact && !Grounded && Attached)
                Velocity += yankDirection * Mathf.Max(0f,Settings.reelSpeed * Settings.features.yankExitSpeedRatio - Vector3.Dot(Velocity,yankDirection));
            yanking = false;
        }
        private void AdvanceYank(float h)
        {
            if(!yanking) return;
            if(Vector3.Distance(Position,Anchor) <= Settings.minRopeLength + Settings.features.yankWallClearance)
            { FinishYank(); yankExhausted = true; return; }
            yankDirection = (Anchor-Position).normalized;
            if(Settings.features.preserveTransitionMomentum)
                Velocity += yankDirection * Mathf.Max(0f,Settings.reelSpeed-Vector3.Dot(Velocity,yankDirection)) * (1f-Mathf.Exp(-15f*h));
        }

        // Change the effective anchor at a wrap corner without moving the body or adding energy.
        public void RouteThrough(Vector3 anchor, float fixedLengthChange)
        {
            // Do not repeatedly expand an unchanged rope to absorb projection rounding in city coordinates.
            // Only a real corner insertion/removal needs a new effective length.
            if (anchor == Anchor && Mathf.Abs(fixedLengthChange) < .00001f) return;
            Anchor = anchor;
            if(Settings.features.resetLengthAtWrap)
            { RopeLength = Mathf.Max(Settings.minRopeLength,Vector3.Distance(Position,anchor)); return; }
            RopeLength = Mathf.Max(Mathf.Max(Settings.minRopeLength, Vector3.Distance(Position, anchor)), RopeLength - fixedLengthChange);
        }

        public void AssistRope(float dt)
        {
            if (!Attached || AssistStrength <= 0f) return;
            float distance = Vector3.Distance(Position, Anchor);
            // Remove slack without snapping position. Near the ground a modest
            // powered winch establishes a usable arc; manual reel input overrides it.
            float target = Mathf.Max(Settings.minRopeLength, distance + .2f);
            if (Position.y < FloorY + 7f && Anchor.y > Position.y + 8f)
                target = Mathf.Max(Settings.minRopeLength, distance - 1f);
            RopeLength = Mathf.MoveTowards(RopeLength, Mathf.Min(RopeLength, target), 10f * AssistStrength * Mathf.Clamp(dt, 0f, .1f));
        }

        /// <summary>이동이 장애물에 막혔을 때 호출한다. 장애물 쪽으로 향하는 속도 성분만 지운다.</summary>
        public void OnBlocked(Vector3 towardsObstacle)
        {
            if (towardsObstacle.sqrMagnitude < 1e-12f) return;
            Vector3 n = towardsObstacle.normalized;
            float into = Vector3.Dot(Velocity, n);
            if (into > 0f) Velocity -= n * into;
        }

        /// <summary>
        /// 시간을 dt만큼 진행한다. steer는 공중 조향 입력(월드 방향, 크기 1 이하로 잘림), reel은 +면 줄 감기, -면 풀기.
        /// </summary>
        public void Step(float dt, Vector3 steer = default, float reel = 0f)
        {
            if (dt <= 0f) return;
            if(reel > 0f) BeginYank();
            else { FinishYank(); yankExhausted = false; }
            dt = Mathf.Min(dt, 0.1f);   // 멈췄다 재개할 때 폭주하지 않도록
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / Settings.maxStep));
            float h = dt / steps;
            Vector3 steering = Vector3.ClampMagnitude(steer, 1f) * Settings.airControl * AssistStrength * (Attached || Grounded || wallRunning ? 3f : 1f);
            if (Attached) steering = Vector3.ProjectOnPlane(steering, (Position - Anchor).normalized);
            if (wallRunning)
            {
                float intoWall = Mathf.Max(0f, -Vector3.Dot(steering, ContactNormal));
                steering = Vector3.ProjectOnPlane(steering, ContactNormal) + Vector3.up * intoWall;
            }
            if (wallEscapeTime > 0f)
            {
                float inward = Vector3.Dot(steering, escapeNormal);
                if (inward < 0f) steering -= escapeNormal * inward;
                wallEscapeTime = Mathf.Max(0f, wallEscapeTime - dt);
            }
            Vector3 gravity = Vector3.down * Settings.gravity * (wallRunning ? .2f : 1f);
            for (int i = 0; i < steps; i++) Substep(h, steering, gravity, reel);
        }

        private void Substep(float h, Vector3 steering, Vector3 gravity, float reel)
        {
            TensionAcceleration=0;
            RopeCorrectionMetres=0;
            AdvanceYank(h);
            if (Settings.features.preserveSteeringMomentum && !Braking && !wallRunning && !Grounded && steering.sqrMagnitude > .0001f)
            {
                Vector3 normal = Attached ? (Position - Anchor).normalized : Vector3.up;
                Vector3 tangent = Vector3.ProjectOnPlane(Velocity, normal);
                Vector3 direction = Vector3.ProjectOnPlane(steering, normal).normalized;
                float target = Attached ? Settings.cruiseSpeed : Settings.features.airTargetSpeed;
                if (tangent.magnitude >= target)
                {
                    // Preserve the effective input force: normalization must not turn a tiny axis or assist into full steering.
                    float fullAcceleration = Settings.airControl * (Attached ? 3f : 1f);
                    float inputStrength = Mathf.Clamp01(steering.magnitude / Mathf.Max(.0001f, fullAcceleration));
                    float turnScale = inputStrength * (Attached ? 1f : Settings.features.airSteeringRatio);
                    Vector3 turned = Vector3.RotateTowards(tangent, direction * tangent.magnitude,
                        Settings.features.steeringTurnDegrees * turnScale * Mathf.Deg2Rad * h, 0f);
                    Velocity += turned - tangent;
                }
                else
                {
                    float deficit = Mathf.Max(0f, target - Vector3.Dot(tangent, direction));
                    Velocity += direction * Mathf.Min(deficit, steering.magnitude * h);
                }
                steering = Vector3.zero;
            }
            Vector3 v = Velocity + (gravity + steering) * h;
            v *= Mathf.Exp(-Settings.airDrag * h);
            Vector3 p = Position + v * h;
            // Resolve the supporting plane before rope tension so gravity cannot pull a resting
            // body below the floor and turn that penetration into horizontal web propulsion.
            if (p.y <= FloorY) { p.y = FloorY; if (v.y < 0f) v.y = 0f; }

            if (Attached)
            {
                if(Settings.features.gentleReel && reel==0f && Velocity.y<0 && Position.y>FloorY+3 &&
                    Vector3.Distance(Position,Anchor)>=RopeLength-.05f)
                    RopeLength=Mathf.Max(Settings.minRopeLength,RopeLength-Settings.features.gentleReelSpeed*h);
                if (reel != 0f) attachmentTakeupRemaining = 0f;
                if (reel != 0f) RopeLength = Mathf.Clamp(RopeLength - reel * (reel > 0f ? Settings.reelSpeed : Settings.payoutSpeed) * h, Settings.minRopeLength, 250f);
                Vector3 d = p - Anchor;
                float dist = d.magnitude;
                // Reel in only length already made slack by inward movement. No teleport or impulse.
                if (attachmentTakeupRemaining > 0f)
                {
                    RopeLength = Mathf.Min(RopeLength, Mathf.Max(Settings.minRopeLength, dist));
                    attachmentTakeupRemaining = Mathf.Max(0f, attachmentTakeupRemaining - h);
                }
                if (Settings.ratchetRope && reel == 0f && dist < RopeLength) RopeLength = Mathf.Max(Settings.minRopeLength, dist);
                bool taut=Settings.features.tensionOnlyRope?dist>=RopeLength:dist>RopeLength;
                if (taut && dist > 1e-6f)
                {
                    Vector3 n = d / dist;
                    RopeCorrectionMetres=Mathf.Max(0,dist-RopeLength);
                    p = Anchor + n * RopeLength;          // 줄이 늘어나지 않게 위치를 구 위로 되돌린다
                    float outward = Vector3.Dot(v, n);
                    TensionAcceleration=Mathf.Max(0,outward)/h;
                    if (outward > 0f) v -= n * outward;   // 줄 방향 바깥쪽 속도를 지운다 (장력)
                }
            }

            Grounded = false;
            if (p.y <= FloorY)
            {
                p.y = FloorY;
                if (v.y < 0f) v.y = 0f;
                float friction = Mathf.Exp(-Settings.groundFriction * h);
                v.x *= friction; v.z *= friction;
                Grounded = true;
                ContactNormal = Vector3.up; LastCollisionName = "ground plane"; ContactSequence++;
            }

            float speed = v.magnitude;
            if (speed > Settings.maxSpeed)
            {
                float limited=Settings.features.softSpeedLimit?
                    Settings.maxSpeed+(speed-Settings.maxSpeed)*Mathf.Exp(-Settings.features.excessSpeedDamping*h):Settings.maxSpeed;
                v*=limited/speed;
            }
            Position = p;
            Velocity = v;
        }
    }
}
