using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalPacingFeatureTests
    {
        [Test] public void ReleaseBoostRequiresAscendingUpperArcAndExplicitRelease()
        {
            var s=WebSwingMotionInput.TraversalSettings();s.features.releaseBoost=true;
            var b=new SwingPhysics(s);b.Reset(new Vector3(30,80,0));b.Attach(Vector3.up*100);b.Velocity=new Vector3(20,30,0);
            var v=b.Velocity;b.Release(true);Assert.That((b.Velocity-v).magnitude,Is.EqualTo(6).Within(.001f));
            b.Reset(Vector3.up*50);b.Attach(Vector3.up*100);b.Velocity=Vector3.right*40;v=b.Velocity;b.Release(true);Assert.That(b.Velocity,Is.EqualTo(v));
            b.Reset(new Vector3(30,80,0));b.Attach(Vector3.up*100);b.Velocity=new Vector3(20,30,0);v=b.Velocity;b.Release();Assert.That(b.Velocity,Is.EqualTo(v));
        }
        [Test] public void ZeroAimStrengthKeepsTheExactDirectHit()
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.layer=30;wall.transform.position=new Vector3(0,100,20);wall.transform.localScale=new Vector3(20,40,1);Physics.SyncTransforms();
                var ray=new Ray(Vector3.up*100,Vector3.forward);Physics.Raycast(ray,out var direct,250,1<<30);
                var f=new FlowFeatures{assistAnchor=true,anchorAssistStrength=0};
                Assert.That(TraversalAim.Select(ray,ray.origin,Vector3.forward*40,250,1<<30,f,out var hit),Is.True);
                Assert.That(hit.point,Is.EqualTo(direct.point));
            }
            finally{Object.DestroyImmediate(wall);}
        }
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
