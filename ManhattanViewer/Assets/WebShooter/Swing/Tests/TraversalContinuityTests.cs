using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
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
