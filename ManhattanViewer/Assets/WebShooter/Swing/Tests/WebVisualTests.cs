using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class WebVisualTests
    {
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
