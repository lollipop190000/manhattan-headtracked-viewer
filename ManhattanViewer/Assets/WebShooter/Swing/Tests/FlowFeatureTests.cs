using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public class FlowFeatureTests
    {
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
            Assert.That(b.Velocity.x,Is.GreaterThan(30));
        }
    }
}
