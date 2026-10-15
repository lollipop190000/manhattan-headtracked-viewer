using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class SnagDetectorTests
    {
        [Test] public void SuddenCollisionStopIsRecordedAndFreeApexIsExcluded()
        {
            var d=new SnagDetector();
            d.Observe(new TraversalSample{movementInput=true,previousVelocity=Vector3.forward*10,velocity=Vector3.zero,collided=true,state=TraversalState.Airborne});
            Assert.That(d.Snags,Is.EqualTo(1));
            d.Observe(new TraversalSample{movementInput=true,previousVelocity=Vector3.up*10,velocity=Vector3.zero,state=TraversalState.Swinging});
            Assert.That(d.Snags,Is.EqualTo(1));
        }
        [Test] public void TwoSecondStationaryInputLatchesOneStuck()
        {
            var d=new SnagDetector();
            for(int i=0;i<360;i++)d.Observe(new TraversalSample{time=i/120f,dt=1f/120,movementInput=true,state=TraversalState.Grounded});
            Assert.That(d.Stucks,Is.EqualTo(1));
        }
        [Test] public void CircularTravelIsNotMistakenForZeroDistance()
        {
            var d=new SnagDetector();
            for(int i=0;i<360;i++)d.Observe(new TraversalSample{time=i/120f,movementInput=true,state=TraversalState.Airborne,
                position=new Vector3(Mathf.Cos(i*.04f),100,Mathf.Sin(i*.04f))});
            Assert.That(d.Stucks,Is.Zero);
        }
        [Test] public void NoInputAndApexDoNotBecomeStuck()
        {
            var d=new SnagDetector();for(int i=0;i<360;i++)d.Observe(new TraversalSample{time=i/120f,state=TraversalState.Swinging});
            Assert.That(d.Stucks,Is.Zero);
            for(int i=0;i<360;i++)
            {
                float t=i/120f;
                d.Observe(new TraversalSample{time=3+t,movementInput=true,state=TraversalState.Swinging,
                    position=new Vector3(0,100-5*(t-1.5f)*(t-1.5f),0)});
            }
            Assert.That(d.Stucks,Is.Zero);
        }
        [Test] public void AStationaryAttachedWebDoesNotHideTwoSecondsOfBlockedInput()
        {
            var d=new SnagDetector();for(int i=0;i<360;i++)d.Observe(new TraversalSample{time=i/120f,movementInput=true,state=TraversalState.Swinging});
            Assert.That(d.Stucks,Is.EqualTo(1));
        }
        [Test] public void CsvSupportsQuotedCommaAndEmbeddedNewline()
        {
            string name="wall, \"north\"\ncorner";
            var rows=TraversalCsv.Read(new StringReader("kind,name\nsnag,"+TraversalCsv.Quote(name)+"\n")).ToArray();
            Assert.That(rows.Length,Is.EqualTo(2));Assert.That(rows[1][1],Is.EqualTo(name));
        }
    }
}
