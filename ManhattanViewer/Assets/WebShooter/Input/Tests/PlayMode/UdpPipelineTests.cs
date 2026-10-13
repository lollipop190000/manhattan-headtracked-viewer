using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using HeadTracked.Display;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WebShooter.Input.Tests
{
    /// <summary>실제 UDP 소켓과 워커 스레드, Update 루프를 거치는 전체 경로: 수신 -> 시작 보정 -> 조준 -> 발사/재보정.</summary>
    public sealed class UdpPipelineTests
    {
        private const int TestPort = 18766;   // 운영 기본 포트(8766)와 분리
        private GameObject root;
        private WristUdpReceiver receiver;
        private HandAimController aim;
        private UdpClient sender;
        private int fired, recentered;
        private Ray lastRay;

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private void Send(string line)
        {
            byte[] data = Encoding.ASCII.GetBytes(line + "\n");
            sender.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, TestPort));
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 3f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        }

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("wrist-test");
            root.SetActive(false);     // 필드를 채운 뒤 활성화해야 OnEnable이 올바른 포트로 시작한다
            receiver = root.AddComponent<WristUdpReceiver>();
            SetField(receiver, "port", TestPort);
            aim = root.AddComponent<HandAimController>();
            SetField(aim, "source", receiver);
            SetField(aim, "startDelaySeconds", 0f);
            SetField(aim, "smoothingSeconds", 0f);
            SetField(aim, "showDebug", false);
            fired = recentered = 0;
            aim.Fired += ray => { fired++; lastRay = ray; };
            aim.Recentered += () => recentered++;
            sender = new UdpClient();
            root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            sender?.Close();
            UnityEngine.Object.DestroyImmediate(root);   // OnDisable이 소켓을 닫고 스레드를 끝낸다
        }

        [UnityTest]
        public IEnumerator ReceivesCalibratesAimsFiresAndRecenters()
        {
            yield return WaitUntil(() => receiver.Status.StartsWith("Listening"));
            Assert.IsTrue(receiver.Status.StartsWith("Listening"), receiver.Status);

            // 첫 샘플(10,0)이 시작 보정 기준: 즉시 보정(startDelay=0)
            Send("10.0,0.0,0");
            yield return WaitUntil(() => aim.Calibrated);
            Assert.IsTrue(aim.Calibrated, "첫 데이터 후 보정되어야 한다");
            Assert.AreEqual(0.5f, aim.Aim01.x, 1e-3f);
            Assert.AreEqual(0.5f, aim.Aim01.y, 1e-3f);

            // 오른쪽 15도, 아래 10도 -> 화면 (0.75, 0.75)
            Send("25.0,-10.0,0");
            yield return WaitUntil(() => Mathf.Abs(aim.Aim01.x - 0.75f) < 1e-3f);
            Assert.AreEqual(0.75f, aim.Aim01.x, 1e-3f);
            Assert.AreEqual(0.75f, aim.Aim01.y, 1e-3f);

            // 깨진 줄(디버그 문구, 잘린 줄)은 무시되고 조준이 튀지 않는다
            Send("# i2c timeout");
            Send("12.3,4");
            yield return null; yield return null;
            Assert.AreEqual(0.75f, aim.Aim01.x, 1e-3f);

            // fire=1 -> Fired 한 번, 레이는 카메라 폴백이 없으면 기본 레이(예외 없이 값이 와야 함)
            Send("25.0,-10.0,1");
            yield return WaitUntil(() => fired > 0);
            Assert.AreEqual(1, fired, "엣지 트리거: 발사 이벤트는 한 번만");
            Assert.IsFalse(float.IsNaN(lastRay.direction.x));
            Send("25.0,-10.0,0");
            yield return null; yield return null;
            Assert.AreEqual(1, fired, "fire=0 줄이 이어져도 다시 발사되면 안 된다");

            // fire=2 -> 재보정: 현재 방향이 새 중앙
            Send("25.0,-10.0,2");
            yield return WaitUntil(() => recentered > 0);
            Assert.AreEqual(1, recentered);
            Assert.AreEqual(0.5f, aim.Aim01.x, 1e-3f);
            Assert.AreEqual(0.5f, aim.Aim01.y, 1e-3f);
            Assert.AreEqual(1, fired, "재보정은 발사로 세지 않는다");
        }

        [UnityTest]
        public IEnumerator FireBeforeCalibrationIsIgnored()
        {
            SetField(aim, "startDelaySeconds", 60f);   // 카운트다운이 끝나지 않게
            yield return WaitUntil(() => receiver.Status.StartsWith("Listening"));
            Send("0.0,0.0,1");
            yield return WaitUntil(() => receiver.TryGetLatest(out _));
            yield return null; yield return null;
            Assert.IsFalse(aim.Calibrated);
            Assert.AreEqual(0, fired);
        }

        [UnityTest]
        public IEnumerator OldQueuedFireIsDiscardedEvenAfterFreshData()
        {
            yield return WaitUntil(() => receiver.Status.StartsWith("Listening"));
            Send("0,0,0"); yield return WaitUntil(() => aim.Calibrated);
            aim.enabled = false;
            Send("0,0,1");
            yield return new WaitForSecondsRealtime(1.2f);
            Send("0,0,0");
            yield return new WaitForSecondsRealtime(.1f);
            aim.enabled = true;
            yield return null; yield return null;
            Assert.That(fired, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HeadTrackedDisplayExposesTheEyeAndScreenUsedForAiming()
        {
            // 패키지 API 계약 확인: HeadTrackedDisplay가 눈 위치/화면 평면/화면 크기를 노출하고, 조준 레이가 이를 쓴다.
            var plane = new GameObject("screen-plane").transform;
            var cameraObject = new GameObject("tracked-camera");
            cameraObject.AddComponent<Camera>();
            var display = cameraObject.AddComponent<HeadTrackedDisplay>();
            display.Configure(plane, null);
            yield return null;   // LateUpdate가 중립 눈 위치를 설정한다

            aim.Configure(receiver, display);
            Ray ray = aim.AimRay;     // 조준점 기본값 (0.5, 0.5)
            Vector3 eyeWorld = plane.TransformPoint(display.EyePositionMeters);
            Assert.AreEqual(eyeWorld.z, ray.origin.z, 1e-4f);
            Assert.Less(display.EyePositionMeters.z, 0f, "눈은 화면 앞(음의 z)에 있어야 한다");
            Assert.AreEqual(0f, Vector3.Angle(ray.direction, plane.forward), 1e-2f, "화면 중앙 조준은 화면 법선 방향");

            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(plane.gameObject);
        }
    }
}
