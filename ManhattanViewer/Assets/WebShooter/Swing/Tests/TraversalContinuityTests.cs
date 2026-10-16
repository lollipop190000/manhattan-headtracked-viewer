using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalAnchorMomentumTests
    {
        [TestCase(-80,6.945927f)][TestCase(-45,28.28427f)][TestCase(0,40)][TestCase(45,40)]
        public void RetainedSpeedPredictsOnlyOutwardLoss(float angle,float expected)
        {
            var radial=Quaternion.AngleAxis(angle,Vector3.right)*Vector3.down;
            Assert.That(TraversalAim.RetainedSpeed(Vector3.zero,-radial*50,Vector3.forward*40),Is.EqualTo(expected).Within(.001f));
        }
        [Test] public void FallingReconnectSelectsMoreRetainedMomentumWithinTheSameCone()
        {
            var near=GameObject.CreatePrimitive(PrimitiveType.Cube);var wide=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var rig=new GameObject();WebSwingMotionInput motion=null;
            try
            {
                Vector3 start=Vector3.up*100;var aim=new Ray(start,new Vector3(0,1,.25f));
                near.layer=wide.layer=30;near.transform.position=start+new Vector3(0,60,17);near.transform.localScale=new Vector3(8,1,2);
                wide.transform.position=start+new Vector3(0,60,27);wide.transform.localScale=new Vector3(12,1,6);Physics.SyncTransforms();
                Assert.That(Physics.Raycast(aim,250,1<<30),Is.False);
                var flight=rig.AddComponent<Manhattan.Viewer.FreeFlightController>();flight.enabled=false;
                var s=WebSwingMotionInput.TraversalSettings();s.gravity=0;s.airDrag=0;s.features.SetFlow(true);
                motion=new WebSwingMotionInput(null,flight,null,s);motion.ReconnectAssist.enabled=true;
                float Run(float weight)
                {
                    flight.SetPose(start,Quaternion.identity);motion.Activate();motion.Body.Attach(start+Vector3.up*50);motion.Release();
                    motion.Body.Velocity=Vector3.down*40;motion.ReconnectAssist.momentumWeight=weight;
                    Assert.That(motion.TryFire(aim),Is.True);Vector3 target=motion.ShotTarget;
                    Assert.That(Vector3.Angle(aim.direction,target-start),Is.LessThanOrEqualTo(12.01f));
                    for(int i=0;i<5;i++){var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                    Assert.That(motion.Body.Attached,Is.True);Assert.That(motion.ShotAge,Is.InRange(.04f,.05f));
                    float result=motion.Body.Velocity.magnitude;
                    Assert.That(result,Is.LessThanOrEqualTo(40.001f));
                    TestContext.WriteLine($"ANCHOR weight={weight:F1} target={target:F4} angle={Vector3.Angle(aim.direction,target-start):F6} afterAttachSpeed={result:F6}");
                    return result;
                }
                float geometric=Run(0),momentum=Run(1.5f);
                Assert.That(momentum,Is.GreaterThan(geometric*1.2f));
            }
            finally{motion?.Collision.Dispose();Object.DestroyImmediate(near);Object.DestroyImmediate(wide);Object.DestroyImmediate(rig);}
        }
        [Test] public void DirectHitRemainsExactWithMomentumScoring()
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer=30;wall.transform.position=new Vector3(0,160,15);wall.transform.localScale=Vector3.one*5;Physics.SyncTransforms();
                var ray=new Ray(Vector3.up*100,wall.transform.position-Vector3.up*100);Physics.Raycast(ray,out var direct,250,1<<30);
                var f=new FlowFeatures{assistAnchor=true,anchorAssistStrength=1,anchorMomentumWeight=2};
                Assert.That(TraversalAim.Select(ray,ray.origin,Vector3.down*40,250,1<<30,f,out var selected),Is.True);
                Assert.That(selected.point,Is.EqualTo(direct.point));
            }
            finally{Object.DestroyImmediate(wall);}
        }
    }
    public sealed class TraversalSteeringStrengthTests
    {
        [TestCase(1,1)][TestCase(1,.1f)][TestCase(.25f,1)][TestCase(.25f,.1f)]
        [TestCase(.01f,1)][TestCase(.01f,.1f)][TestCase(0,1)][TestCase(1,0)]
        public void HighSpeedAirTurnScalesWithAssistAndAxis(float assist,float axis)
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(true);s.gravity=0;s.airDrag=0;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.AssistStrength=assist;b.Velocity=Vector3.forward*40;
            for(int i=0;i<120;i++)b.Step(1f/120,Vector3.right*axis);
            float angle=Mathf.Atan2(b.Velocity.x,b.Velocity.z)*Mathf.Rad2Deg;
            Assert.That(angle,Is.EqualTo(65*.35f*assist*axis).Within(.005f));
            Assert.That(b.Velocity.magnitude,Is.EqualTo(40).Within(.002f));
            TestContext.WriteLine($"AIR assist={assist:F2} axis={axis:F2} turnDegrees={angle:F6} speed={b.Velocity.magnitude:F6}");
        }
        [Test] public void AirTurnRatioCanDisableRotationWithoutDiscardingMomentum()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(true);s.features.airSteeringRatio=0;s.gravity=0;s.airDrag=0;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Velocity=Vector3.forward*40;b.Step(.1f,Vector3.right);
            Assert.That(b.Velocity,Is.EqualTo(Vector3.forward*40));
        }
    }
    public sealed class TraversalWallTransitionTests
    {
        private static SwingPhysics Runner()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(true);s.airDrag=0;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.WallRunRequested=true;b.Velocity=Vector3.left*20;
            b.Contact(Vector3.right,"wall");Assert.That(b.State,Is.EqualTo(TraversalState.WallRunning));return b;
        }
        [Test] public void ContactRespectsJumpCooldownThenAllowsFreshEntry()
        {
            var b=Runner();b.Jump();b.Velocity=Vector3.left*20;b.Contact(Vector3.right);
            Assert.That(b.State,Is.EqualTo(TraversalState.Airborne));
            for(int i=0;i<37;i++){b.Velocity=Vector3.left*20;b.UpdateWall(Vector3.right,true,1f/120);}
            Assert.That(b.State,Is.EqualTo(TraversalState.WallRunning));
            TestContext.WriteLine("WALL cooldown blocked at 0s; fresh entry allowed after 0.308333s");
        }
        [Test] public void ContactCannotBypassExhaustionUntilWallExit()
        {
            var b=Runner();b.Velocity=Vector3.up*10;
            for(int i=0;i<250;i++)b.UpdateWall(Vector3.right,true,1f/120);
            b.Velocity=Vector3.left*20;b.Contact(Vector3.right);
            Assert.That(b.State,Is.EqualTo(TraversalState.Airborne));
            b.UpdateWall(Vector3.zero,true,.1f);
            for(int i=0;i<40;i++){b.Velocity=Vector3.left*20;b.UpdateWall(Vector3.right,true,1f/120);}
            Assert.That(b.State,Is.EqualTo(TraversalState.WallRunning));
            TestContext.WriteLine("WALL contact cannot restart exhausted run at 2.083333s; leaving wall permits later entry");
        }
        [Test] public void RepeatedContactDoesNotResetTheRunTimer()
        {
            var b=Runner();
            for(int i=0;i<250;i++)
            {b.Velocity=Vector3.left*20;b.UpdateWall(Vector3.right,true,1f/120);b.Velocity=Vector3.left*20;b.Contact(Vector3.right);}
            Assert.That(b.State,Is.EqualTo(TraversalState.Airborne));
        }
        [Test] public void WebAttachmentExitsWallRunAndPreservesVelocityWithFullGravity()
        {
            var b=Runner();b.Velocity=Vector3.forward*20;var velocity=b.Velocity;
            Assert.That(b.Attach(b.Position+new Vector3(0,50,50)),Is.True);
            Assert.That(b.Velocity,Is.EqualTo(velocity));b.UpdateWall(Vector3.right,true,1f/120);b.Step(1f/120);
            Assert.That(b.State,Is.EqualTo(TraversalState.Swinging));Assert.That(b.Attached,Is.True);
            Assert.That(b.Velocity.y,Is.EqualTo(-.2f).Within(.00001f));
            TestContext.WriteLine($"WALL newWeb state={b.State} velocityY={b.Velocity.y:F6} expectedFullGravity=-0.200000");
        }
        [Test] public void InvalidAttachmentDoesNotCancelAWallRun()
        {var b=Runner();Assert.That(b.Attach(b.Position),Is.False);Assert.That(b.State,Is.EqualTo(TraversalState.WallRunning));}
        [Test] public void RoofContactEndsWallRunImmediately()
        {var b=Runner();b.Contact(Vector3.up);Assert.That(b.Grounded,Is.True);Assert.That(b.State,Is.EqualTo(TraversalState.Grounded));}
    }
}
