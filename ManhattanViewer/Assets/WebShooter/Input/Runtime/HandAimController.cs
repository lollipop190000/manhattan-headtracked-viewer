using System;
using HeadTracked.Display;
using UnityEngine;

namespace WebShooter.Input
{
    /// <summary>손 방향(yaw/pitch) -> 화면 위 정규화 좌표. webshooter.py와 같은 규칙을 순수 함수로 둔다.</summary>
    public static class HandAimMath
    {
        /// <summary>
        /// 중앙 기준 각도 차이를 화면 좌표(0..1, 왼쪽 위가 0,0)로 바꾼다.
        /// yaw +는 오른쪽, pitch +는 위. 좌우 ±yawRange도, 상하 ±pitchRange도가 화면 끝.
        /// </summary>
        public static Vector2 ToScreen01(float yaw, float pitch, float yaw0, float pitch0,
            float yawRange, float pitchRange, bool invertYaw, bool invertPitch)
        {
            float dYaw = Mathf.DeltaAngle(yaw0, yaw);              // ±180 랩어라운드 처리
            float dPitch = pitch - pitch0;
            float x = 0.5f + (invertYaw ? -1f : 1f) * dYaw / yawRange * 0.5f;
            float y = 0.5f - (invertPitch ? -1f : 1f) * dPitch / pitchRange * 0.5f;
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        /// <summary>
        /// 머리 추적 투영에서 화면 점이 가리키는 방향: 눈에서 "화면 위의 그 점"을 지나는 직선.
        /// 눈과 화면 점은 HeadTrackedDisplay와 같은 화면 로컬 좌표(중심 원점, +x 오른쪽, +y 위, 단위 m)로 준다.
        /// </summary>
        public static Vector3 ScreenLocalPoint(Vector2 aim01, float screenWidth, float screenHeight) =>
            new Vector3((aim01.x - 0.5f) * screenWidth, (0.5f - aim01.y) * screenHeight, 0f);

        /// <summary>눈(화면 로컬 좌표)에서 화면 위의 조준점을 지나는 월드 레이. plane은 HeadTrackedDisplay.ScreenPlane.</summary>
        public static Ray RayThroughScreen(Transform plane, Vector3 eyeLocalMeters, Vector2 aim01, float screenWidth, float screenHeight)
        {
            Vector3 eye = plane.TransformPoint(eyeLocalMeters);
            Vector3 point = plane.TransformPoint(ScreenLocalPoint(aim01, screenWidth, screenHeight));
            return new Ray(eye, (point - eye).normalized);
        }
    }

    /// <summary>
    /// 손목 장치의 조준을 화면 좌표와 월드 레이로 바꾸고, 발사/재보정 이벤트를 낸다. 이동(IMotionInputSource)과는 별개다.
    /// 머리 추적이 켜져 있으면(display 지정) 눈 위치에서 화면 점을 지나는 레이를, 아니면 카메라 뷰포트 레이를 쓴다.
    /// </summary>
    public sealed class HandAimController : MonoBehaviour
    {
        [SerializeField] private WristSource source;
        [Tooltip("머리 추적 디스플레이. 비워두면 일반 카메라 뷰포트 레이를 쓴다.")]
        [SerializeField] private HeadTrackedDisplay display;
        [SerializeField] private Camera fallbackCamera;
        [SerializeField] private float yawRangeDegrees = 30f;
        [SerializeField] private float pitchRangeDegrees = 20f;
        [SerializeField] private bool invertYaw;
        [SerializeField] private bool invertPitch;
        [Tooltip("조준점 EMA 시간 상수(초). 0이면 끔.")]
        [SerializeField, Min(0f)] private float smoothingSeconds = 0.05f;
        [Tooltip("첫 데이터 수신 후 이 시간(초)이 지난 순간의 손 방향을 화면 중앙으로 삼는다. 0이면 즉시.")]
        [SerializeField, Min(0f)] private float startDelaySeconds = 3f;
        [Tooltip("이 시간(초) 동안 데이터가 없으면 연결이 끊긴 것으로 본다.")]
        [SerializeField, Min(0.1f)] private float staleSeconds = 1f;
        [SerializeField] private bool showDebug = true;

        private float yaw0, pitch0;
        private float firstDataAt = -1f;
        private Vector2 target = new Vector2(0.5f, 0.5f);
        private Vector2 aim = new Vector2(0.5f, 0.5f);
        private WristSample last;

        /// <summary>화면 위 조준점. (0,0)이 왼쪽 위, (1,1)이 오른쪽 아래.</summary>
        public Vector2 Aim01 => aim;
        public bool Calibrated { get; private set; }
        public bool Connected { get; private set; }
        /// <summary>시작 보정까지 남은 초. 보정이 끝났거나 데이터가 없으면 0.</summary>
        public float CountdownRemaining =>
            Calibrated || firstDataAt < 0f ? 0f : Mathf.Max(0f, startDelaySeconds - (Time.unscaledTime - firstDataAt));

        /// <summary>손가락을 접었을 때(fire=1) 발사 방향 레이와 함께 발생.</summary>
        public event Action<Ray> Fired;
        /// <summary>센서를 길게 가려서 재보정(fire=2)했을 때 발생.</summary>
        public event Action Recentered;

        public void Configure(WristSource wristSource, HeadTrackedDisplay headTrackedDisplay)
        {
            source = wristSource;
            display = headTrackedDisplay;
        }

        /// <summary>현재 조준점이 가리키는 월드 레이.</summary>
        public Ray AimRay
        {
            get
            {
                if (display != null && display.ScreenPlane != null && display.Calibration != null)
                {
                    return HandAimMath.RayThroughScreen(display.ScreenPlane, display.EyePositionMeters, aim,
                        display.Calibration.screenWidth, display.Calibration.screenHeight);
                }
                Camera cam = fallbackCamera != null ? fallbackCamera : Camera.main;
                return cam != null ? cam.ViewportPointToRay(new Vector3(aim.x, 1f - aim.y, 0f)) : new Ray(Vector3.zero, Vector3.forward);
            }
        }

        /// <summary>현재 손 방향을 화면 중앙으로 삼는다.</summary>
        public void Recenter(bool announce = true)
        {
            yaw0 = last.yaw; pitch0 = last.pitch;
            target = aim = new Vector2(0.5f, 0.5f);
            Calibrated = true;
            if (announce) Recentered?.Invoke();
        }

        private void Update()
        {
            if (source == null) return;
            bool have = source.TryGetLatest(out WristSample sample);
            Connected = have && WristSource.AgeSeconds(sample) < staleSeconds;
            if (Connected) last = sample;

            if (!Calibrated)
            {
                if (Connected)
                {
                    if (firstDataAt < 0f) firstDataAt = Time.unscaledTime;
                    if (Time.unscaledTime - firstDataAt >= startDelaySeconds) Recenter(false);
                }
            }
            else if (Connected)
            {
                target = HandAimMath.ToScreen01(sample.yaw, sample.pitch, yaw0, pitch0,
                    yawRangeDegrees, pitchRangeDegrees, invertYaw, invertPitch);
            }

            float k = smoothingSeconds <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / smoothingSeconds);
            aim = Vector2.Lerp(aim, target, k);

            while (source.TryDequeueFire(out int fire))
            {
                if (!Calibrated) continue;                  // 보정 전에는 발사/재보정 무시
                if (fire == 1) Fired?.Invoke(AimRay);
                else if (fire == 2) Recenter();
            }
        }

        private void OnGUI()
        {
            if (!showDebug || source == null) return;
            string state = Connected ? "CONNECTED" : "NO DATA";
            string line = $"Wrist {state}  {source.Status}  {source.Hz:0} Hz";
            if (!Calibrated && firstDataAt >= 0f) line += $"   POINT AT SCREEN CENTER {Mathf.CeilToInt(CountdownRemaining)}";
            else if (Calibrated) line += $"   aim {aim.x:0.00},{aim.y:0.00}";
            GUI.Label(new Rect(12, 10, Screen.width - 24, 24), line);
            if (Calibrated)
            {
                float px = aim.x * Screen.width, py = aim.y * Screen.height;
                GUI.Label(new Rect(px - 6, py - 12, 24, 24), "+");
            }
        }
    }
}
