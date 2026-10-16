using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class WebVisualTests
    {
        [Test] public void WrappedLineIncludesEveryCornerWithoutSmoothingThroughASolid()
        {
            var knots=new[]{Vector3.zero,Vector3.right*5,new Vector3(5,0,10),new Vector3(9,0,10)};var points=new Vector3[512];
            int count=WebVisualMath.Resample(knots,points,26,.1f,.7f,3);
            Assert.That(count,Is.EqualTo(79));
            for(int i=0;i<knots.Length;i++)Assert.That(Vector3.Distance(points[i*26],knots[i]),Is.LessThan(.0001f));
            Assert.That(WebVisualMath.RequiredPoints(13,30),Is.EqualTo(361));
        }
        [Test] public void WrappedSamplingIsInvariantUnderFloatingOriginTranslation()
        {
            var a=new Vector3[512];var b=new Vector3[512];var delta=new Vector3(1200,100,3500);
            var knots=new[]{Vector3.zero,Vector3.right*5,Vector3.forward*10};
            var shifted=new[]{knots[0]+delta,knots[1]+delta,knots[2]+delta};
            int n=WebVisualMath.Resample(knots,a,26,0,0,3);WebVisualMath.Resample(shifted,b,26,0,0,3);
            for(int i=0;i<n;i++)Assert.That(Vector3.Distance(a[i],b[i]-delta),Is.LessThan(.001f));
        }
        [Test] public void AttachmentQuadMatchesWallAndRoofNormals()
        {
            foreach(var n in new[]{Vector3.up,Vector3.right,Vector3.back})
                Assert.That(Vector3.Distance(WebAnchorMark.SurfaceRotation(n)*Vector3.forward,n),Is.LessThan(.001f));
        }
        [Test] public void AttachmentMarkAllocatesNativePropertiesDuringConfiguration()
        {
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);var material=new Material(Resources.Load<Shader>("TraversalWebStrand"));
            try
            {
                var mark=quad.AddComponent<WebAnchorMark>();mark.Configure(Vector3.up*10,Vector3.up,null,material,()=>false,.5f);
                Assert.That(quad.transform.position.y,Is.EqualTo(10.015f).Within(.0001f));mark.Release(.1f);
            }
            finally{Object.DestroyImmediate(quad);Object.DestroyImmediate(material);}
        }
        [Test] public void BraidedTextureHasTransparentEdgesAndInternalFilaments()
        {
            var t=WebTextureFactory.Strand(5);
            try
            {
                Assert.That(t.width,Is.EqualTo(256));Assert.That(t.height,Is.EqualTo(64));
                Assert.That(t.GetPixel(0,0).a,Is.Zero);Assert.That(t.GetPixel(0,63).a,Is.Zero);
                Assert.That(t.GetPixel(0,32).a,Is.GreaterThan(.5f));
                float darkest=1,lightest=0;
                foreach(var pixel in t.GetPixels(0,32,256,1)){darkest=Mathf.Min(darkest,pixel.r);lightest=Mathf.Max(lightest,pixel.r);}
                Assert.That(lightest-darkest,Is.GreaterThan(.2f));
                TestContext.WriteLine($"WEB_TEXTURE centreAlpha={t.GetPixel(0,32).a:F6} filamentContrast={lightest-darkest:F6}");
            }
            finally{Object.DestroyImmediate(t);}
        }
        [Test] public void RadialMarkHasVisibleStrandsAndTransparentBoundary()
        {
            var t=WebTextureFactory.RadialMark();
            try
            {
                float maximum=0;foreach(var pixel in t.GetPixels())maximum=Mathf.Max(maximum,pixel.a);
                Assert.That(maximum,Is.GreaterThan(.8f));Assert.That(t.GetPixel(0,64).a,Is.Zero);
                TestContext.WriteLine($"WEB_MARK maximumAlpha={maximum:F6}");
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
