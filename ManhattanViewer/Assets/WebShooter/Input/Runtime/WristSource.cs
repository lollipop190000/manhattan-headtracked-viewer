using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace WebShooter.Input
{
    /// <summary>손목 장치가 보낸 한 줄("yaw,pitch,fire")을 해석한 결과. yaw는 오른쪽이 +, pitch는 위가 +(도).</summary>
    public struct WristSample
    {
        public float yaw, pitch;
        public long receivedTicks;   // Stopwatch 틱. 워커 스레드에서 Unity API 없이 기록한다.
        public long sequence;
    }

    /// <summary>손목 장치 한 줄 형식의 해석. 장치 펌웨어와 같은 규칙: fire 0=없음, 1=발사, 2=재보정.</summary>
    public static class WristProtocol
    {
        public static bool TryParse(string line, out float yaw, out float pitch, out int fire)
        {
            yaw = pitch = 0f; fire = 0;
            if (string.IsNullOrEmpty(line)) return false;
            string[] parts = line.Trim().Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out yaw) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out pitch) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out fire)) return false;
            // 시리얼 잡음으로 깨진 줄은 버린다 (Python 프로그램과 같은 범위 검사).
            return !float.IsNaN(yaw) && !float.IsNaN(pitch) &&
                   yaw >= -360f && yaw <= 360f && pitch >= -180f && pitch <= 180f && fire >= 0 && fire <= 2;
        }
    }

    /// <summary>수신 경로(시리얼, UDP 등)와 무관한 공통 기반. 워커 스레드에서 Publish, 메인 스레드에서 읽는다.</summary>
    public abstract class WristSource : MonoBehaviour
    {
        private readonly object gate = new object();
        private readonly ConcurrentQueue<(int fire, long ticks)> fires = new ConcurrentQueue<(int, long)>();
        private WristSample latest;
        private bool hasSample;
        private long sequence;
        private long hzLastSequence;
        private float hzTimer;

        public abstract string Status { get; }
        /// <summary>최근 1초 동안 초당 수신한 줄 수 (진단용).</summary>
        public float Hz { get; private set; }

        public bool TryGetLatest(out WristSample sample)
        {
            lock (gate) { sample = latest; return hasSample; }
        }

        public bool TryDequeueFire(out int fire)
        {
            while (fires.TryDequeue(out var queued))
                if ((Stopwatch.GetTimestamp() - queued.ticks) / (double)Stopwatch.Frequency < 1.0)
                { fire = queued.fire; return true; }
            fire = 0; return false;
        }

        public static float AgeSeconds(WristSample sample) =>
            (float)((Stopwatch.GetTimestamp() - sample.receivedTicks) / (double)Stopwatch.Frequency);

        /// <summary>워커 스레드에서 한 줄을 넘긴다. 해석에 실패한 줄은 조용히 버린다.</summary>
        protected void Publish(string line)
        {
            if (!WristProtocol.TryParse(line, out float yaw, out float pitch, out int fire)) return;
            lock (gate)
            {
                sequence++;
                latest = new WristSample { yaw = yaw, pitch = pitch, receivedTicks = Stopwatch.GetTimestamp(), sequence = sequence };
                hasSample = true;
            }
            if (fire != 0) fires.Enqueue((fire, Stopwatch.GetTimestamp()));
        }

        protected void ResetSource()
        {
            lock (gate) { hasSample = false; }
            while (fires.TryDequeue(out _)) { }
        }

        private void Update()
        {
            long seq;
            lock (gate) seq = sequence;
            hzTimer += Time.unscaledDeltaTime;
            if (hzTimer >= 1f)
            {
                Hz = (seq - hzLastSequence) / hzTimer;
                hzLastSequence = seq;
                hzTimer = 0f;
            }
        }
    }
}
