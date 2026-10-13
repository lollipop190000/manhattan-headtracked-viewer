using NUnit.Framework;
using UnityEngine;

namespace WebShooter.Input.Tests
{
    public sealed class HandAimTests
    {
        [Test]
        public void ParsesFirmwareLine()
        {
            Assert.IsTrue(WristProtocol.TryParse("12.3,-4.5,1\r\n", out float yaw, out float pitch, out int fire));
            Assert.AreEqual(12.3f, yaw, 1e-4f);
            Assert.AreEqual(-4.5f, pitch, 1e-4f);
            Assert.AreEqual(1, fire);
        }

        [TestCase("")]
        [TestCase("1,2")]
        [TestCase("a,b,c")]
        [TestCase("1,2,3")]          // fire 범위 밖
        [TestCase("500,0,0")]        // yaw 범위 밖
        [TestCase("0,200,0")]        // pitch 범위 밖
        [TestCase("# i2c timeout")]  // 펌웨어 디버그 줄
        [TestCase("1,2,3,4")]
        public void RejectsBrokenLines(string line) =>
            Assert.IsFalse(WristProtocol.TryParse(line, out _, out _, out _));

        [Test]
        public void CenterMapsToMiddle()
        {
            Vector2 p = HandAimMath.ToScreen01(10f, 5f, 10f, 5f, 30f, 20f, false, false);
            Assert.AreEqual(0.5f, p.x, 1e-5f);
            Assert.AreEqual(0.5f, p.y, 1e-5f);
        }

        [Test]
        public void RightAndUpMoveTheCursorRightAndUp()
        {
            Vector2 p = HandAimMath.ToScreen01(15f, 10f, 0f, 0f, 30f, 20f, false, false);
            Assert.AreEqual(0.75f, p.x, 1e-5f);   // +yaw = 오른쪽
            Assert.AreEqual(0.25f, p.y, 1e-5f);   // +pitch = 위 (화면 y는 위가 0)
        }

        [Test]
        public void RangeEdgesReachTheScreenEdgesAndClamp()
        {
            Assert.AreEqual(1f, HandAimMath.ToScreen01(30f, 0f, 0f, 0f, 30f, 20f, false, false).x, 1e-5f);
            Assert.AreEqual(0f, HandAimMath.ToScreen01(-90f, 0f, 0f, 0f, 30f, 20f, false, false).x, 1e-5f);
            Assert.AreEqual(1f, HandAimMath.ToScreen01(0f, -20f, 0f, 0f, 30f, 20f, false, false).y, 1e-5f);
        }

        [Test]
        public void YawWrapsAroundPlusMinus180()
        {
            // 중앙이 +170°일 때 -170°는 오른쪽으로 20° 간 것이다.
            Vector2 p = HandAimMath.ToScreen01(-170f, 0f, 170f, 0f, 40f, 20f, false, false);
            Assert.AreEqual(0.5f + 0.25f, p.x, 1e-4f);
        }

        [Test]
        public void InvertFlagsFlipDirections()
        {
            Vector2 p = HandAimMath.ToScreen01(15f, 10f, 0f, 0f, 30f, 20f, true, true);
            Assert.AreEqual(0.25f, p.x, 1e-5f);
            Assert.AreEqual(0.75f, p.y, 1e-5f);
        }

        [Test]
        public void CenterAimFromCenteredEyeLooksStraightIntoTheScene()
        {
            var plane = new GameObject("plane").transform;
            try
            {
                Ray ray = HandAimMath.RayThroughScreen(plane, new Vector3(0f, 0f, -0.6f), new Vector2(0.5f, 0.5f), 0.53f, 0.30f);
                Assert.AreEqual(0f, Vector3.Angle(ray.direction, Vector3.forward), 1e-3f);
                Assert.AreEqual(-0.6f, ray.origin.z, 1e-5f);
            }
            finally { Object.DestroyImmediate(plane.gameObject); }
        }

        [Test]
        public void RightEdgeAimTiltsTowardTheScreenEdgeFromTheEye()
        {
            var plane = new GameObject("plane").transform;
            try
            {
                Ray ray = HandAimMath.RayThroughScreen(plane, new Vector3(0f, 0f, -0.6f), new Vector2(1f, 0.5f), 0.53f, 0.30f);
                // 눈은 화면에서 0.6m, 화면 오른쪽 끝은 중심에서 0.265m 오른쪽 -> atan(0.265/0.6)
                float expected = Mathf.Atan(0.265f / 0.6f) * Mathf.Rad2Deg;
                Assert.AreEqual(expected, Vector3.Angle(ray.direction, Vector3.forward), 1e-3f);
                Assert.Greater(ray.direction.x, 0f);
                Assert.AreEqual(0f, ray.direction.y, 1e-5f);
            }
            finally { Object.DestroyImmediate(plane.gameObject); }
        }

        [Test]
        public void MovingTheEyeChangesTheRayThroughTheSameScreenPoint()
        {
            var plane = new GameObject("plane").transform;
            try
            {
                var aim = new Vector2(0.5f, 0.5f);   // 화면 정중앙
                Ray centered = HandAimMath.RayThroughScreen(plane, new Vector3(0f, 0f, -0.6f), aim, 0.53f, 0.30f);
                Ray headRight = HandAimMath.RayThroughScreen(plane, new Vector3(0.2f, 0f, -0.6f), aim, 0.53f, 0.30f);
                Assert.Less(headRight.direction.x, 0f);   // 머리가 오른쪽이면 같은 화면 점은 왼쪽 앞을 향한다
                Assert.AreNotEqual(centered.direction.x, headRight.direction.x);
            }
            finally { Object.DestroyImmediate(plane.gameObject); }
        }

        [Test]
        public void RotatedScreenPlaneRotatesTheRay()
        {
            var plane = new GameObject("plane").transform;
            try
            {
                plane.rotation = Quaternion.Euler(0f, 90f, 0f);   // 화면의 +Z(장면 쪽)가 월드 +X를 향함
                Ray ray = HandAimMath.RayThroughScreen(plane, new Vector3(0f, 0f, -0.6f), new Vector2(0.5f, 0.5f), 0.53f, 0.30f);
                Assert.AreEqual(0f, Vector3.Angle(ray.direction, Vector3.right), 1e-3f);
            }
            finally { Object.DestroyImmediate(plane.gameObject); }
        }

        [Test]
        public void ScreenLocalPointUsesCenterOriginAndYUp()
        {
            Vector3 topLeft = HandAimMath.ScreenLocalPoint(new Vector2(0f, 0f), 0.53f, 0.30f);
            Assert.AreEqual(-0.265f, topLeft.x, 1e-5f);
            Assert.AreEqual(0.15f, topLeft.y, 1e-5f);
            Assert.AreEqual(0f, topLeft.z, 1e-6f);
        }
    }
}
