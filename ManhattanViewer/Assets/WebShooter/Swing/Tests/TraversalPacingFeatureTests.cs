using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalPacingFeatureTests
    {
        [TestCase(false)][TestCase(true)] public void SlackRopeAllowsFreeFallInTheActualBaselineAndExperiment(bool enabled)
        {
            var s=WebSwingMotionInput.TraversalSettings();s.airDrag=0;s.features.tensionOnlyRope=enabled;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(Vector3.up*200);b.Position+=Vector3.up*10;
            for(int i=0;i<60;i++)b.Step(1f/120);
            Assert.That(b.Velocity.y,Is.EqualTo(-12).Within(.01f));
            Assert.That(b.Position.y,Is.InRange(106.9f,107.1f));
        }
    }
}
