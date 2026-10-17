using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public class FlowFeatureTests
    {
        [TestCase(false)][TestCase(true)] public void ExistingWrapStaysAttachedAndOptionalResetIsTaut(bool enabled)
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                box.layer=30;box.transform.position=Vector3.up*100;box.transform.localScale=Vector3.one*4;Physics.SyncTransforms();
                var s=WebSwingMotionInput.TraversalSettings();s.features.resetLengthAtWrap=enabled;
                var b=new SwingPhysics(s);b.Reset(new Vector3(-5,100,0));b.Velocity=Vector3.forward*30;b.Attach(new Vector3(5,100,0));
                var route=new WebRopeRoute();route.Begin(b.Anchor);Assert.That(route.Update(b.Position,Vector3.zero,1<<30),Is.True);
                var velocity=b.Velocity;var position=b.Position;b.RouteThrough(route.Points[0],route.FixedLength);
                Assert.That(b.Attached,Is.True);Assert.That(b.Velocity,Is.EqualTo(velocity));Assert.That(b.Position,Is.EqualTo(position));
                if(enabled)Assert.That(b.RopeLength,Is.EqualTo(Vector3.Distance(position,b.Anchor)).Within(.001f));
                TestContext.WriteLine($"FLOW wrap={enabled} points={route.Points.Count} speed={b.Velocity.magnitude:F4} slack={b.RopeLength-Vector3.Distance(b.Position,b.Anchor):F4}");
            }
            finally{Object.DestroyImmediate(box);}
        }
        [TestCase(false)][TestCase(true)] public void GroundJumpAlreadyPreservesHorizontalMomentum(bool enabled)
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.preserveTransitionMomentum=enabled;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*b.FloorY);b.Velocity=Vector3.forward*40;b.Jump();
            Assert.That(b.Velocity.z,Is.EqualTo(40));Assert.That(b.Velocity.y,Is.EqualTo(s.jumpSpeed));
        }
        [TestCase(false)][TestCase(true)] public void ReelExitInheritsPoweredMotionWhenEnabled(bool enabled)
        {
            var s=WebSwingMotionInput.TraversalSettings();s.gravity=0;s.airDrag=0;s.features.preserveTransitionMomentum=enabled;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(Vector3.up*150);b.Velocity=Vector3.right*10;
            b.BeginYank();Assert.That(b.Velocity.x,Is.EqualTo(10));
            for(int i=0;i<120;i++)b.Step(1f/120,default,1);
            b.FinishYank();float toward=Vector3.Dot(b.Velocity,(b.Anchor-b.Position).normalized);
            TestContext.WriteLine($"FLOW yank={enabled} exitToward={toward:F4} speed={b.Velocity.magnitude:F4}");
            if(enabled)Assert.That(toward,Is.GreaterThanOrEqualTo(12));
            else Assert.That(Mathf.Abs(toward),Is.LessThan(.01f));
        }
        [Test] public void YankExitCannotReinjectVelocityIntoAContact()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.preserveTransitionMomentum=true;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(b.Position+Vector3.forward*20);b.BeginYank();
            b.Contact(Vector3.back,"wall");b.FinishYank();Assert.That(b.Velocity.z,Is.LessThanOrEqualTo(0));
        }
        [TestCase(false)][TestCase(true)] public void GlancingWallKeepsRopeOnlyWithFeature(bool enabled)
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.preserveGlancingContact=enabled;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(Vector3.up*150);b.Velocity=new Vector3(-3,0,40);
            b.Contact(Vector3.right,"glancing wall");
            TestContext.WriteLine($"FLOW wall={enabled} attached={b.Attached} speed={b.Velocity.magnitude:F4}");
            Assert.That(b.Attached,Is.EqualTo(enabled));Assert.That(b.Velocity.z,Is.GreaterThan(39));
            if(enabled)Assert.That(b.State,Is.EqualTo(TraversalState.Swinging));
        }
        [Test] public void OnlyFrontalRequestedContactStartsWallRun()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.preserveGlancingContact=true;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(Vector3.up*150);b.Velocity=Vector3.left*20;
            b.WallRunRequested=true;b.Contact(Vector3.right,"frontal wall");
            Assert.That(b.State,Is.EqualTo(TraversalState.WallRunning));Assert.That(b.Attached,Is.False);
            Assert.That(b.LastCollisionName,Is.EqualTo("frontal wall"));
        }
        [TestCase(false)][TestCase(true)]
        public void ReleasedForwardInputKeepsAtLeast35MetresPerSecond(bool enabled)
        {
            var b=new SwingPhysics(WebSwingMotionInput.TraversalSettings());
            b.Settings.features.preserveSteeringMomentum=enabled;
            b.Reset(Vector3.up*1000);b.Velocity=Vector3.forward*40;
            b.Attach(b.Position+Vector3.up*50);b.Release();
            for(int i=0;i<120;i++)b.Step(1f/120,Vector3.forward);
            float speed=Vector3.ProjectOnPlane(b.Velocity,Vector3.up).magnitude;
            TestContext.WriteLine($"FLOW steering={enabled} horizontalAfter1s={speed:F4}");
            Assert.That(speed,Is.GreaterThanOrEqualTo(35));
            if(enabled)Assert.That(speed,Is.InRange(39.5f,40.1f));
        }
        [Test] public void HighSpeedLateralInputTurnsWithoutLosingMagnitude()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.gravity=0;s.airDrag=0;s.features.preserveSteeringMomentum=true;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Velocity=Vector3.forward*40;
            for(int i=0;i<120;i++)b.Step(1f/120,Vector3.right);
            Assert.That(b.Velocity.magnitude,Is.EqualTo(40).Within(.002f));
            float expected=s.features.steeringTurnDegrees*s.features.airSteeringRatio;
            Assert.That(Mathf.Atan2(b.Velocity.x,b.Velocity.z)*Mathf.Rad2Deg,Is.EqualTo(expected).Within(.02f));
        }
    }
}
