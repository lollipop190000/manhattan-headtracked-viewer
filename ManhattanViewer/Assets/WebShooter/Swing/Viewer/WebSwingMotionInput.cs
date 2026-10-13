using System;
using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    /// <summary>
    /// 뷰어의 이동 입력(IMotionInputSource)을 거미줄 스윙 물리로 바꿔 끼운다. 팀 코드는 수정하지 않는다.
    /// 마우스 시선과 WASD/QE는 기존 입력(look)에서 받고, 위치는 SwingPhysics가 계산해서 절대 위치(hasAbsolutePose)로 돌려준다.
    /// 건물 충돌은 FreeFlightController.TranslationConstraint(CityCollisionConstraint)가 처리하고,
    /// 막히면 다음 프레임에 이 클래스가 실제 위치와 비교해서 알아채고 물리에 알려 준다.
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

        public event Action<Vector3> WebAttached;
        public event Action WebReleased;

        public WebSwingMotionInput(IMotionInputSource lookSource, FreeFlightController flightController, FloatingOrigin floatingOrigin,
            SwingSettings settings = null)
        {
            look = lookSource;
            flight = flightController ?? throw new ArgumentNullException(nameof(flightController));
            origin = floatingOrigin;
            Body = new SwingPhysics(settings);
        }

        /// <summary>
        /// 발사: 이전 거미줄을 먼저 끊고, 레이가 건물(충돌 프록시)에 맞으면 새로 붙인다. 허공을 쏘면 끊기만 한다.
        /// 맞았으면 true. 충돌 프록시가 꺼져 있으면(CityStreamer.CollisionProxiesEnabled) 맞는 것이 없다.
        /// </summary>
        public bool TryFire(Ray ray)
        {
            EnsureInitialized();
            Release();
            if (!Physics.Raycast(ray, out RaycastHit hit, MaxRange, CollisionMask, QueryTriggerInteraction.Ignore)) return false;
            Vector3 anchorCity = hit.point + (origin != null ? origin.Offset : Vector3.zero);
            if (!Body.Attach(anchorCity)) return false;
            WebAttached?.Invoke(anchorCity);
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
            MotionCommand input = look != null ? look.Sample(deltaTime) : default;
            if (!Enabled) return input;
            EnsureInitialized();

            // 지난 프레임에 요청한 위치와 실제 위치를 비교: 크게 다르면 순간이동, 조금 다르면 충돌로 막힌 것
            Vector3 actual = flight.CityPosition;
            if (hasRequested)
            {
                Vector3 shortfall = lastRequested - actual;
                if (shortfall.magnitude > TeleportDistance)
                {
                    Body.Reset(actual);          // SetPose 등으로 옮겨졌다: 속도와 거미줄을 버린다
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
            Vector3 steer = heading * new Vector3(input.localMovement.x, 0f, input.localMovement.z);   // 수평 조향
            Body.Step(deltaTime, steer, Body.Attached ? input.localMovement.y : 0f);                    // E: 감기, Q: 풀기

            lastRequested = Body.Position;
            hasRequested = true;
            return new MotionCommand
            {
                hasAbsolutePose = true,
                cityPosition = Body.Position,
                cityRotation = Quaternion.Euler(pitch, yaw, 0f)
            };
        }
    }
}
