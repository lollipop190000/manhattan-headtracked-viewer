using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalPacingFeatureTests
    {
        [Test] public void LongerAttachmentAllowsDropAndPointNineCreatesInitialTension()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.attachmentDrop=true;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(Vector3.up*150);
            Assert.That(b.RopeLength,Is.EqualTo(54).Within(.001f));Assert.That(b.Position.y,Is.EqualTo(100));
            s.features.initialLengthRatio=.9f;b.Attach(Vector3.up*150);Assert.That(b.RopeLength,Is.EqualTo(45).Within(.001f));
        }
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
