using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public class RopeRouteTests
    {
        [Test] public void WrapsSolidAndUnwrapsWithClearSight()
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                box.layer=30;box.transform.position=new Vector3(0,100,0);box.transform.localScale=new Vector3(4,4,4);Physics.SyncTransforms();
                var route=new WebRopeRoute();var body=new Vector3(-5,100,0);var end=new Vector3(5,100,0);route.Begin(end);
                Assert.That(route.Update(body,Vector3.zero,1<<30),Is.True);
                Assert.That(route.Points.Count,Is.GreaterThan(1));
                var previous=body;
                foreach(var point in route.Points){Assert.That(WebRopeRoute.Blocked(previous,point,Vector3.zero,1<<30,out _),Is.False);previous=point;}
                Assert.That(route.FixedLength,Is.GreaterThan(0));
                Assert.That(route.Update(new Vector3(5,100,-8),Vector3.zero,1<<30),Is.True);
                Assert.That(route.Points.Count,Is.EqualTo(1));
            }
            finally{Object.DestroyImmediate(box);}
        }
        [Test] public void WrapUsesCityCoordinatesAfterOriginShift()
        {
            var box=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                box.layer=30;box.transform.localScale=Vector3.one*4;Physics.SyncTransforms();
                Vector3 offset=new Vector3(1200,100,3500);var route=new WebRopeRoute();route.Begin(offset+Vector3.right*5);
                Assert.That(route.Update(offset-Vector3.right*5,offset,1<<30),Is.True);
                foreach(var p in route.Points)Assert.That(Vector3.Distance(p,offset),Is.LessThan(10));
            }
            finally{Object.DestroyImmediate(box);}
        }
        [TestCase(30)][TestCase(60)][TestCase(120)]
        public void ContinuousTakeupHasNoSlackOrAddedImpulse(int fps)
        {
            var b=new SwingPhysics(new SwingSettings{gravity=0,airDrag=0,ratchetRope=true});b.Reset(Vector3.up*100);b.Velocity=Vector3.up*10;b.Attach(Vector3.up*150);
            for(int i=0;i<fps*2;i++){b.Step(1f/fps);Assert.That(b.RopeLength,Is.EqualTo(Vector3.Distance(b.Position,b.Anchor)).Within(.002f));}
            Assert.That(b.Velocity,Is.EqualTo(Vector3.up*10));
            b.Step(.1f,default,-1);Assert.That(b.RopeLength-Vector3.Distance(b.Position,b.Anchor),Is.GreaterThan(3));
            var position=b.Position;var velocity=b.Velocity;b.RouteThrough(b.Anchor+Vector3.right*3,4);
            Assert.That(b.Position,Is.EqualTo(position));Assert.That(b.Velocity,Is.EqualTo(velocity));
        }
    }
}
