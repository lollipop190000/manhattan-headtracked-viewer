using NUnit.Framework;
using UnityEngine;
using Manhattan.Viewer;

namespace WebShooter.Swing.Tests
{
    public class TraversalTests
    {
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void MouseHoldReleasesDuringFlightAndAfterAttachment(int fps)
        {
            var rig = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(0, 100, 20); wall.transform.localScale = new Vector3(100,100,1);
                var flight = rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 100, Quaternion.identity);
                var input = new WebSwingMotionInput(null, flight, null); Physics.SyncTransforms();
                var ray = new Ray(flight.CityPosition, Vector3.forward);
                Assert.That(input.HandleMouseHold(true, true, true, ray), Is.True);
                input.HandleMouseHold(true, false, false, ray); input.Sample(1f/fps);
                Assert.That(input.Flying || input.Body.Attached, Is.False, "release before arrival cancels shot");
                input.HandleMouseHold(true, true, true, ray);
                for (int i=0;i<fps/2;i++)
                {
                    input.HandleMouseHold(true, false, true, ray);
                    var c=input.Sample(1f/fps); flight.transform.SetPositionAndRotation(c.cityPosition,c.cityRotation);
                }
                Assert.That(input.Body.Attached, Is.True);
                var velocity=input.Body.Velocity;
                input.HandleMouseHold(true, false, false, ray);
                Assert.That(input.Body.Attached || input.Flying, Is.False);
                Assert.That(input.Body.Velocity, Is.EqualTo(velocity));
                input.HandleMouseHold(true, false, true, ray);
                Assert.That(input.Body.Attached || input.Flying, Is.False, "holding without new press cannot refire");
                input.HandleMouseHold(true,true,true,ray); input.HandleMouseHold(false,false,true,ray);
                Assert.That(input.Body.Attached || input.Flying, Is.False, "focus/menu loss cancels shot");
                input.Collision.Dispose();
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(wall); }
        }
        [Test] public void CancelledHighShotNeverLaunchesFromGround()
        {
            var rig=new GameObject(); var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer=30; wall.transform.position=new Vector3(0,20,50); wall.transform.localScale=new Vector3(100,40,1);
                var flight=rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up*1.85f,Quaternion.identity);
                var input=new WebSwingMotionInput(null,flight,null); Physics.SyncTransforms();
                Assert.That(input.HandleMouseHold(true,true,true,new Ray(flight.CityPosition,new Vector3(0,20,50))),Is.True);
                input.HandleMouseHold(true,false,false,default);
                for(int i=0;i<120;i++) { var c=input.Sample(1f/60); flight.transform.position=c.cityPosition; }
                Assert.That(input.Body.Position,Is.EqualTo(Vector3.up*1.85f));
                Assert.That(input.Body.Velocity,Is.EqualTo(Vector3.zero));
                input.Collision.Dispose();
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(wall); }
        }
        [Test] public void DefaultTraversalDoesNotRotateViewOrEnableAutomaticForces()
        {
            var rig = new GameObject();
            try
            {
                var flight=rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up*100,Quaternion.identity);
                var input=new WebSwingMotionInput(null,flight,null); input.Activate();
                Assert.That(input.AutoFollow || input.AutomaticRopeAssist || input.AutomaticCruiseAssist,Is.False);
                input.Body.Velocity=Vector3.right*20;
                var c=input.Sample(.1f); Assert.That(Quaternion.Angle(c.cityRotation,Quaternion.identity),Is.LessThan(.001f));
                input.Collision.Dispose();
            }
            finally { Object.DestroyImmediate(rig); }
        }
        [Test] public void SwingWallDeflectsOnceAndPreservesGlancingMomentum()
        {
            var b = Body(); b.Attach(b.Position + Vector3.up * 30);
            b.Velocity = new Vector3(-12, 0, 24); float speed = b.Velocity.magnitude;
            b.Contact(Vector3.right);
            Assert.That(b.Attached, Is.False);
            Assert.That(b.Velocity.x, Is.GreaterThan(7));
            Assert.That(b.Velocity.z, Is.GreaterThan(23));
            Assert.That(b.Velocity.magnitude, Is.LessThan(speed));
            b.Contact(Vector3.right); Assert.That(b.WallDeflections, Is.EqualTo(1));
            b.Step(.1f, Vector3.left);
            Assert.That(b.Velocity.x, Is.GreaterThan(7));
        }
        [Test] public void LandingDoesNotBounceOrDetachRope()
        {
            var b = Body(); b.Attach(b.Position + Vector3.up * 30); b.Velocity = Vector3.down * 20;
            b.Contact(Vector3.up);
            Assert.That(b.Grounded, Is.True); Assert.That(b.Attached, Is.True);
            Assert.That(b.Velocity, Is.EqualTo(Vector3.zero));
        }
        [Test] public void CruiseAssistanceCanBeBrakedAndDisabled()
        {
            var b = Body(); b.Attach(b.Position + Vector3.up * 30); b.Velocity = Vector3.forward * 20;
            b.AssistCruise(.1f, true); Assert.That(b.Velocity.z, Is.EqualTo(20));
            b.AssistStrength = 0; b.AssistCruise(.1f, false); Assert.That(b.Velocity.z, Is.EqualTo(20));
            b.AssistStrength = 1; b.AssistCruise(.1f, false); Assert.That(b.Velocity.z, Is.EqualTo(21.2f).Within(.001f));
            b.Release(); var v = b.Velocity; b.AssistCruise(.1f, false); Assert.That(b.Velocity, Is.EqualTo(v));
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void TrackedLeanRejectsJitterAndDropsStaleInput(int fps)
        {
            var lean = new TrackedLeanSteering(); float dt = 1f / fps;
            lean.Sample(.03f, true, dt);
            for (int i = 0; i < fps; i++) Assert.That(lean.Sample(.04f, true, dt), Is.Zero);
            float value = 0;
            for (int i = 0; i < fps; i++) value = lean.Sample(.13f, true, dt);
            Assert.That(value, Is.EqualTo(1f).Within(.001f));
            Assert.That(lean.Sample(.13f, false, dt), Is.Zero);
            Assert.That(lean.Sample(.13f, true, dt), Is.Zero);
            Assert.That(lean.Sample(float.NaN, true, dt), Is.Zero);
        }
        [Test] public void HighShotAutomaticallyLaunchesButMissDoesNot()
        {
            var rig = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(0, 10, 20); wall.transform.localScale = new Vector3(20, 20, 1);
                var flight = rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 1.85f, Quaternion.identity);
                var input = new WebSwingMotionInput(null, flight, null); Physics.SyncTransforms();
                Assert.That(input.TryFire(new Ray(flight.CityPosition, new Vector3(0, 10, 20))), Is.True);
                Assert.That(input.Body.Velocity, Is.EqualTo(Vector3.zero), "no launch before arrival");
                Assert.That(input.Body.Attached, Is.False);
                input.Sample(.1f);
                Assert.That(input.Body.Attached, Is.True);
                Assert.That(input.Body.Velocity.y, Is.GreaterThan(12)); Assert.That(input.Body.Velocity.z, Is.GreaterThan(10));
                input.Activate(); input.Body.AssistStrength = 0;
                input.TryFire(new Ray(flight.CityPosition, new Vector3(0, 10, 20)));
                Assert.That(input.Body.Velocity, Is.EqualTo(Vector3.zero));
                input.Activate(); input.Body.AssistStrength = 1;
                Assert.That(input.TryFire(new Ray(flight.CityPosition, Vector3.back)), Is.False);
                Assert.That(input.Body.Velocity, Is.EqualTo(Vector3.zero));
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(wall); }
        }
        [Test] public void FasterReelAndPayoutHaveSeparateRates()
        {
            var b = Body(); b.Attach(new Vector3(0, 200, 0));
            b.Step(.1f, default, 1); Assert.That(b.RopeLength, Is.EqualTo(97.6f).Within(.001f));
            b.Step(.1f, default, -1); Assert.That(b.RopeLength, Is.EqualTo(100.8f).Within(.001f));
        }
        [Test] public void AssistedSlackTakeupIsBoundedAndCanBeDisabled()
        {
            var b = Body(); b.Attach(new Vector3(0, 200, 0)); b.Position += Vector3.up * 10;
            b.AssistRope(.1f); Assert.That(b.RopeLength, Is.EqualTo(99f).Within(.001f));
            b.AssistStrength = 0; b.AssistRope(.1f); Assert.That(b.RopeLength, Is.EqualTo(99f).Within(.001f));
        }
        [Test] public void HighShotWithNoMovementInputFormsAnAirborneArc()
        {
            var rig = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(0, 25, 50); wall.transform.localScale = new Vector3(100, 50, 1);
                var flight = rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 1.85f, Quaternion.identity);
                var input = new WebSwingMotionInput(null, flight, null) { AutomaticRopeAssist = true, AutomaticCruiseAssist = true };
                Physics.SyncTransforms(); Assert.That(input.TryFire(new Ray(flight.CityPosition, new Vector3(0, 30, 50))), Is.True);
                float peak = 0, lateHeight = 0;
                for (int i = 0; i < 360; i++)
                {
                    var c = input.Sample(1f / 120); flight.transform.SetPositionAndRotation(c.cityPosition, c.cityRotation);
                    peak = Mathf.Max(peak, c.cityPosition.y);
                    if (i == 335) lateHeight = c.cityPosition.y;
                    Assert.That(float.IsFinite(c.cityPosition.x + c.cityPosition.y + c.cityPosition.z), Is.True);
                    Assert.That(c.cityPosition.z, Is.LessThan(49.3f));
                }
                Assert.That(peak, Is.GreaterThan(8f)); Assert.That(lateHeight, Is.GreaterThan(3f));
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(wall); }
        }
        [Test] public void AutomaticViewFollowsFlightGraduallyWithoutMouseInput()
        {
            var rig = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(0, 100, 20); wall.transform.localScale = new Vector3(100, 100, 1);
                var flight = rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 100, Quaternion.identity);
                var input = new WebSwingMotionInput(null, flight, null) { AutoFollow = true };
                Physics.SyncTransforms(); input.TryFire(new Ray(flight.CityPosition, Vector3.forward)); input.Body.Velocity = Vector3.right * 15;
                var command = input.Sample(.1f);
                Assert.That(command.cityRotation.eulerAngles.y, Is.InRange(3.9f, 4.1f));
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(0, command.cityRotation.eulerAngles.z)), Is.LessThan(.001f));
                input.AutoFollow = false; flight.transform.SetPositionAndRotation(command.cityPosition, command.cityRotation);
                var next = input.Sample(.1f); Assert.That(next.cityRotation, Is.EqualTo(command.cityRotation));
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(wall); }
        }
        [Test] public void ReelingIntoWallReleasesInsteadOfOscillating()
        {
            var rig = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(0, 100, 20); wall.transform.localScale = new Vector3(100, 100, 1);
                var flight = rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 100, Quaternion.identity);
                var input = new WebSwingMotionInput(null, flight, null, new SwingSettings { minRopeLength = .05f, reelSpeed = 100, gravity = 0 });
                Physics.SyncTransforms(); input.TryFire(new Ray(flight.CityPosition, Vector3.forward)); input.ReelInput = 1;
                for (int i = 0; i < 240; i++)
                {
                    var command = input.Sample(1f / 120); flight.transform.position = command.cityPosition;
                    Assert.That(command.cityPosition.z, Is.LessThan(19.3f));
                    Assert.That(float.IsFinite(command.cityPosition.y), Is.True);
                }
                Assert.That(input.CollisionReleaseCount, Is.GreaterThan(0)); Assert.That(input.Body.Attached, Is.False);
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(wall); }
        }
        [TearDown] public void CleanQueryShapes()
        {
            foreach (var shape in Resources.FindObjectsOfTypeAll<CapsuleCollider>())
                if (shape.gameObject.name == "Traversal overlap query") Object.DestroyImmediate(shape.gameObject);
        }
        [TestCase(4.4f)] [TestCase(5f)]
        public void StationaryEmbeddedBodyIsRecovered(float x)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(5, 100, 0); wall.transform.localScale = new Vector3(1, 20, 100);
                var b = Body(); b.Position = new Vector3(x, 100, 0);
                using var collision = new TraversalCollision(null, b);
                var recovered = collision.Constrain(b.Position, b.Position);
                Assert.That(Mathf.Abs(recovered.x - 5), Is.GreaterThanOrEqualTo(.8f));
                Assert.That(Physics.CheckCapsule(recovered - Vector3.up * 1.55f, recovered, .3f, TraversalCollision.Mask), Is.False);
            }
            finally { Object.DestroyImmediate(wall); }
        }
        [Test] public void ResidentFootprintsBlockUnloadedMeshAndRecoverInterior()
        {
            var b = Body();
            var building = new BuildingRecord { height = 200, groundHeight = 0,
                footprint = new[] { new Vector2(4.5f, -10), new Vector2(5.5f, -10), new Vector2(5.5f, 10), new Vector2(4.5f, 10) },
                cityBounds = new Bounds(new Vector3(5, 100, 0), new Vector3(1, 200, 20)) };
            var solid = new CitySolidRecovery(new[] { building }, b);
            var hit = solid.Constrain(new Vector3(0, 100, 0), new Vector3(100, 100, 3));
            Assert.That(hit.x, Is.LessThan(4.2f)); Assert.That(hit.z, Is.GreaterThan(2));
            var escape = solid.Constrain(new Vector3(5, 100, 0), new Vector3(5, 100, 0));
            Assert.That(Mathf.Abs(escape.x - 5), Is.GreaterThan(.8f));
            var roof = solid.Constrain(new Vector3(5, 210, 0), new Vector3(5, 190, 0));
            Assert.That(roof.y, Is.EqualTo(201.87f).Within(.01f));
            var outside = solid.Constrain(new Vector3(0, 100, 0), new Vector3(0, 100, 8));
            Assert.That(Vector3.Distance(outside, new Vector3(0, 100, 8)), Is.LessThan(.001f));
        }
        [Test] public void GhostFlightSweepsAndCanRetreatFromCorner()
        {
            var rig = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); var side = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = side.layer = 30;
                wall.transform.position = new Vector3(5, 100, 0); wall.transform.localScale = new Vector3(1, 20, 100);
                side.transform.position = new Vector3(0, 100, 5); side.transform.localScale = new Vector3(100, 20, 1);
                var flight = rig.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 100, Quaternion.identity);
                var input = new WebSwingMotionInput(null, flight, null) { Enabled = false };
                var gate = new MouseWebShooter.FlightCollisionGate(input);
                Vector3 p = flight.CityPosition;
                for (int i = 0; i < 120; i++) p = gate.Constrain(p, p + new Vector3(10, 0, 10));
                Assert.That(p.x, Is.InRange(4.1f, 4.3f)); Assert.That(p.z, Is.InRange(4.1f, 4.3f));
                var retreat = gate.Constrain(p, p - new Vector3(2, 0, 2));
                Assert.That(Vector3.Distance(retreat, p - new Vector3(2, 0, 2)), Is.LessThan(.001f));
                input.Collision.Dispose();
            }
            finally { Object.DestroyImmediate(rig); Object.DestroyImmediate(wall); Object.DestroyImmediate(side); }
        }
        [Test] public void WallRunAgainstActualColliderRemainsOutside()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(5, 100, 0); wall.transform.localScale = new Vector3(1, 200, 100);
                var b = Body(); b.Position = new Vector3(4.18f, 100, 0); b.Velocity = Vector3.forward * 12;
                using var collision = new TraversalCollision(null, b);
                collision.CitySolids = new CitySolidRecovery(new[] { new BuildingRecord {
                    height = 200, groundHeight = 0, cityBounds = new Bounds(new Vector3(5, 100, 0), new Vector3(1, 200, 100)),
                    footprint = new[] { new Vector2(4.5f, -50), new Vector2(5.5f, -50), new Vector2(5.5f, 50), new Vector2(4.5f, 50) }
                } }, b);
                for (int i = 0; i < 120; i++)
                {
                    b.UpdateWall(collision.FindWall(b.Position, Vector3.forward), true, 1f / 120);
                    var before = b.Position; b.Step(1f / 120, new Vector3(1, 0, 1));
                    b.Position = collision.Constrain(before, b.Position);
                    Assert.That(b.Position.x, Is.LessThanOrEqualTo(4.201f));
                }
                Assert.That(b.State, Is.EqualTo(TraversalState.WallRunning));
                Assert.That(b.Jump(), Is.True); Assert.That(b.Velocity.x, Is.LessThan(0));
            }
            finally { Object.DestroyImmediate(wall); }
        }
        private SwingPhysics Body() { var b = new SwingPhysics(new SwingSettings { airDrag = 0f }); b.Reset(new Vector3(0, 100, 0)); return b; }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void FreeFallMatchesGravity(int fps)
        {
            var b = Body(); for (int i = 0; i < fps; i++) b.Step(1f / fps);
            Assert.That(b.Velocity.y, Is.EqualTo(-9.81f).Within(.002f));
            Assert.That(b.Position.y, Is.EqualTo(95.095f).Within(.05f));
        }
        [Test] public void ReleasePreservesMomentum()
        {
            var b = Body(); b.Velocity = new Vector3(20, -5, 10); b.Attach(new Vector3(0, 120, 0));
            b.Release(); Assert.That(b.Velocity, Is.EqualTo(new Vector3(20, -5, 10)));
        }
        [Test] public void SlackRopeDoesNotPull()
        {
            var b = Body(); b.Attach(new Vector3(0, 120, 0)); b.Position += Vector3.up * 5; b.Step(.01f);
            Assert.That(b.Velocity.y, Is.EqualTo(-.0981f).Within(.001f));
        }
        [Test] public void RopeNeverExtends()
        {
            var b = Body(); b.Attach(new Vector3(0, 120, 0)); b.Velocity = Vector3.right * 30;
            for (int i = 0; i < 600; i++) { b.Step(1f / 120); Assert.That(Vector3.Distance(b.Position, b.Anchor), Is.LessThanOrEqualTo(b.RopeLength + .001f)); }
        }
        [Test] public void ZeroAssistIgnoresSteering()
        {
            var b = Body(); b.AssistStrength = 0; b.Step(.1f, Vector3.right); Assert.That(b.Velocity.x, Is.Zero);
        }
        [Test] public void AssistedSteeringIsAccelerationNotVelocityReplacement()
        {
            var b = Body(); b.Velocity = Vector3.forward * 20; b.Step(.01f, Vector3.right);
            Assert.That(b.Velocity.x, Is.InRange(.11f, .13f)); Assert.That(b.Velocity.z, Is.EqualTo(20));
        }
        [Test] public void GroundAndWallJump()
        {
            var b = Body(); b.Reset(Vector3.up * b.FloorY); Assert.That(b.Jump(), Is.True); Assert.That(b.Velocity.y, Is.GreaterThan(0));
            b.Position = Vector3.up * 10; b.Velocity = Vector3.forward * 10;
            b.UpdateWall(Vector3.right, true, .01f); Assert.That(b.State, Is.EqualTo(TraversalState.WallRunning));
            Assert.That(b.Jump(), Is.True); Assert.That(b.Velocity.x, Is.GreaterThan(0));
            Assert.That(b.State, Is.EqualTo(TraversalState.Airborne));
        }
        [Test] public void WallRunTimesOutAndCannotRestartUntilExit()
        {
            var b = Body(); b.Velocity = Vector3.forward * 10;
            for (int i = 0; i < 400; i++) b.UpdateWall(Vector3.right, true, .01f);
            Assert.That(b.State, Is.EqualTo(TraversalState.Airborne));
        }
        [Test] public void WallEdgeReleasesRunner()
        {
            var b = Body(); b.Velocity = Vector3.forward * 10; b.UpdateWall(Vector3.right, true, .01f);
            b.UpdateWall(Vector3.zero, true, .01f); Assert.That(b.State, Is.EqualTo(TraversalState.Airborne));
        }
        [Test] public void SweptCollisionStopsFastBodyAndSlides()
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(5, 100, 0); wall.transform.localScale = new Vector3(1, 20, 100);
                var b = Body(); b.Velocity = new Vector3(70, 0, 10);
                var collision = new TraversalCollision(null, b);
                var result = collision.Constrain(b.Position, b.Position + new Vector3(20, 0, 10));
                Assert.That(result.x, Is.LessThan(4.3f)); Assert.That(result.z, Is.GreaterThan(8)); Assert.That(b.Velocity.x, Is.EqualTo(0).Within(.001f));
            }
            finally { Object.DestroyImmediate(wall); }
        }
        private sealed class Look : IMotionInputSource { public MotionCommand Sample(float dt) => new MotionCommand { lookDegrees = new Vector2(30, 0) }; }
        [Test] public void RooftopLandingSupportsJump()
        {
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                roof.layer = 30; roof.transform.position = new Vector3(0, 50, 0); roof.transform.localScale = new Vector3(50, 2, 50);
                var b = Body(); b.Velocity = Vector3.down * 70;
                b.Position = new TraversalCollision(null, b).Constrain(b.Position, new Vector3(0, 40, 0));
                Assert.That(b.Grounded, Is.True); Assert.That(b.Position.y, Is.EqualTo(51 + b.Settings.eyeHeight + .02f).Within(.03f));
                Assert.That(b.Jump(), Is.True); Assert.That(b.Velocity.y, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(roof); }
        }
        [Test] public void RebaseKeepsCityAnchorAndCollisionCoordinates()
        {
            var go = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var origin = go.AddComponent<FloatingOrigin>();
                typeof(FloatingOrigin).GetField("east", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(origin, 1000d);
                wall.layer = 30; wall.transform.position = new Vector3(5, 100, 0); wall.transform.localScale = new Vector3(1, 30, 30);
                var b = Body(); b.Position += Vector3.right * 1000; b.Attach(new Vector3(1000, 120, 0));
                var result = new TraversalCollision(origin, b).Constrain(b.Position, b.Position + Vector3.right * 30);
                Assert.That(result.x, Is.InRange(1004f, 1004.3f)); Assert.That(b.Anchor.x, Is.EqualTo(1000));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(wall); }
        }
        [Test] public void LookingDoesNotSteerAndPauseFreezesPhysics()
        {
            var go = new GameObject();
            try
            {
                var flight = go.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 100, Quaternion.identity);
                var input = new WebSwingMotionInput(new Look(), flight, null); input.Activate(); input.Body.Velocity = Vector3.forward * 10;
                input.Sample(.01f); Assert.That(input.Body.Velocity.x, Is.Zero);
                var p = input.Body.Position; var v = input.Body.Velocity; input.Paused = true; input.Sample(.1f);
                Assert.That(input.Body.Position, Is.EqualTo(p)); Assert.That(input.Body.Velocity, Is.EqualTo(v));
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test] public void FireWaitsForArrivalAndRefireMissReleases()
        {
            var go = new GameObject(); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer = 30; wall.transform.position = new Vector3(0, 100, 20); wall.transform.localScale = new Vector3(20, 20, 1);
                Physics.SyncTransforms(); var flight = go.AddComponent<FreeFlightController>(); flight.SetPose(Vector3.up * 100, Quaternion.identity);
                var input = new WebSwingMotionInput(null, flight, null);
                Assert.That(input.TryFire(new Ray(Vector3.up * 100, Vector3.forward)), Is.True);
                Assert.That(input.Body.Attached, Is.False); input.Sample(.07f); Assert.That(input.Body.Attached, Is.True);
                Assert.That(input.TryFire(new Ray(Vector3.up * 100, Vector3.back)), Is.False); Assert.That(input.Body.Attached, Is.False);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(wall); }
        }
    }
}
