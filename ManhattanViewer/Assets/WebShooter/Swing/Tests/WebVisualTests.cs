using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class WebVisualTests
    {
        [Test] public void BraidedTextureHasTransparentEdgesAndInternalFilaments()
        {
            var t=WebTextureFactory.Strand(5);
            try
            {
                Assert.That(t.width,Is.EqualTo(256));Assert.That(t.height,Is.EqualTo(64));
                Assert.That(t.GetPixel(0,0).a,Is.Zero);Assert.That(t.GetPixel(0,63).a,Is.Zero);
                Assert.That(t.GetPixel(0,32).a,Is.GreaterThan(.5f));
                Assert.That(t.GetPixel(0,32),Is.Not.EqualTo(t.GetPixel(8,32)));
            }
            finally{Object.DestroyImmediate(t);}
        }
        [Test] public void TensionTremorIsZeroAtRestAndBoundedAtSpeed()
        {
            Assert.That(WebVisualMath.TremorAmplitude(0,0,1,.004f),Is.Zero);
            Assert.That(WebVisualMath.TremorAmplitude(95,1000,1,.004f),Is.EqualTo(.004f));
            Assert.That(WebVisualMath.TremorAmplitude(95,1000,0,.004f),Is.Zero);
        }
        [Test] public void AttachmentHasSmallOvershootAndSettlesBy120Milliseconds()
        {
            Assert.That(WebVisualMath.AttachmentAmplitude(.22f,.03f,.12f),Is.GreaterThan(0));
            Assert.That(WebVisualMath.AttachmentAmplitude(.22f,.09f,.12f),Is.LessThan(0));
            Assert.That(WebVisualMath.AttachmentAmplitude(.22f,.12f,.12f),Is.Zero);
        }
        [Test] public void FlightWaveHas27PointsFixedEndpointsAndDecayingAmplitude()
        {
            var p=new Vector3[512];var knots=new[]{Vector3.zero,Vector3.forward*20};
            int count=WebVisualMath.Resample(knots,p,26,.22f,1,3);
            Assert.That(count,Is.EqualTo(27));Assert.That(p[0].magnitude,Is.LessThan(.0001f));Assert.That(Vector3.Distance(p[count-1],knots[1]),Is.LessThan(.0001f));
            float radius=0;foreach(var v in p)radius=Mathf.Max(radius,new Vector2(v.x,v.y).magnitude);
            Assert.That(radius,Is.InRange(.1f,.24f));
            Assert.That(WebVisualMath.FiringAmplitude(.22f,1),Is.Zero);
            Assert.That(WebVisualMath.FiringAmplitude(.22f,.5f),Is.LessThan(.22f));
        }
    }
}
