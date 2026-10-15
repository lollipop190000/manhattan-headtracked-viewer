using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public class TraversalPacingTests
    {
        [TestCase(30)][TestCase(60)][TestCase(120)]
        public void ReleaseRetainsSpeedAndFallIsFaster(int fps)
        {
            var body=new SwingPhysics(WebSwingMotionInput.TraversalSettings());
            body.Reset(Vector3.up*1000);body.Velocity=Vector3.forward*45;body.Attach(body.Position+Vector3.up*100);
            var before=body.Velocity;body.Release();Assert.That(body.Velocity,Is.EqualTo(before));
            for(int i=0;i<fps*2;i++) body.Step(1f/fps);
            Assert.That(body.Velocity.z,Is.GreaterThan(44));
            Assert.That(1000-body.Position.y,Is.InRange(47f,49f));
            Assert.That(body.Position.z,Is.InRange(88f,90f));
        }
        [Test] public void CruiseAcceleratesAlongRopeTangentAndHonorsBraking()
        {
            var body=new SwingPhysics(WebSwingMotionInput.TraversalSettings());
            body.Reset(Vector3.up*100);body.Attach(Vector3.up*150);body.Velocity=Vector3.forward*30;
            body.AssistCruise(.1f,false);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));Assert.That(body.Velocity.y,Is.Zero);
            body.AssistCruise(.1f,true);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));
            body.AssistStrength=0;body.AssistCruise(.1f,false);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));
            body.Release();body.AssistStrength=1;body.AssistCruise(.1f,false);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));
        }
        [Test] public void ActiveHandIsInFrontOfScreenButClearOfEye()
        {
            Assert.That(TraversalHands.WristDepth(-.6f,.78f,1),Is.LessThan(-.15f));
            Assert.That(TraversalHands.WristDepth(-.2f,1,1),Is.GreaterThan(-.1f));
            Assert.That(TraversalHands.WristDepth(-.6f,.78f,0),Is.GreaterThan(0));
            Assert.That(TraversalHands.ShouldShow(true,false,true,1),Is.True);
            Assert.That(TraversalHands.ShouldShow(true,false,false,1),Is.False);
            Assert.That(TraversalHands.ShouldShow(true,false,true,0),Is.False);
            Assert.That(TraversalHands.ShouldShow(true,true,true,1),Is.False);
        }
    }
}
