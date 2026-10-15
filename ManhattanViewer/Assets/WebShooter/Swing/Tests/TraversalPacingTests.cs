using NUnit.Framework;
using UnityEngine;
using Manhattan.Viewer;
namespace WebShooter.Swing.Tests
{
    public class TraversalPacingTests
    {
        [Test] public void ShippingAdapterDoesNotPumpAnIdlePendulum()
        {
            var rig=new GameObject();var anchor=GameObject.CreatePrimitive(PrimitiveType.Cube);WebSwingMotionInput motion=null;
            try
            {
                anchor.layer=30;anchor.transform.position=new Vector3(30,2040,0);
                var flight=rig.AddComponent<FreeFlightController>();flight.SetPose(new Vector3(0,2000,0),Quaternion.identity);
                motion=new WebSwingMotionInput(null,flight,null);motion.Activate();Physics.SyncTransforms();
                Assert.That(motion.TryFire(new Ray(flight.CityPosition,anchor.transform.position-flight.CityPosition)),Is.True);
                for(int i=0;i<60;i++){var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                float length=motion.Body.RopeLength,energy=.5f*motion.Body.Velocity.sqrMagnitude+24f*motion.Body.Position.y;
                for(int i=0;i<1200;i++)
                {
                    var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);
                    Assert.That(motion.Body.Attached,Is.True,$"frame {i}, position {motion.Body.Position}");
                    Assert.That(motion.Body.RopeLength,Is.EqualTo(length).Within(.01f),$"frame {i}, corners {motion.Route.Points.Count}");
                    Assert.That(.5f*motion.Body.Velocity.sqrMagnitude+24f*motion.Body.Position.y,Is.LessThanOrEqualTo(energy+3f),$"frame {i}");
                }
            }
            finally {motion?.Collision.Dispose();Object.DestroyImmediate(rig);Object.DestroyImmediate(anchor);}
        }
        [TestCase(30)][TestCase(60)][TestCase(120)]
        public void PassivePendulumDoesNotGainEnergyOrShortenItsRope(int fps)
        {
            var b=new SwingPhysics(WebSwingMotionInput.TraversalSettings());
            b.Reset(new Vector3(0,200,0));b.Attach(new Vector3(0,240,30));
            float length=b.RopeLength, energy=b.Settings.gravity*b.Position.y;
            for(int i=0;i<fps*20;i++)
            {
                b.Step(1f/fps);
                Assert.That(b.Position.y,Is.LessThan(201),"no self-powered climb");
                Assert.That(b.RopeLength,Is.EqualTo(length).Within(.001f));
                Assert.That(.5f*b.Velocity.sqrMagnitude+b.Settings.gravity*b.Position.y,Is.LessThanOrEqualTo(energy+2));
            }
        }
        [TestCase(30)][TestCase(60)][TestCase(120)]
        public void RooftopLandingStopsQuicklyAndCanJumpAgain(int fps)
        {
            var rig=new GameObject();var roof=GameObject.CreatePrimitive(PrimitiveType.Cube);
            WebSwingMotionInput motion=null;
            try
            {
                roof.layer=30;roof.transform.position=new Vector3(0,100,0);roof.transform.localScale=new Vector3(100,2,100);
                var flight=rig.AddComponent<FreeFlightController>();flight.SetPose(new Vector3(0,103,0),Quaternion.identity);
                motion=new WebSwingMotionInput(null,flight,null);motion.Activate();motion.Body.Velocity=new Vector3(40,-10,0);
                for(int i=0;i<fps;i++){var c=motion.Sample(1f/fps);flight.SetPose(c.cityPosition,c.cityRotation);}
                Assert.That(motion.Body.Grounded,Is.True);
                Assert.That(motion.Body.Position.y,Is.EqualTo(102.87f).Within(.04f));
                Assert.That(motion.Body.Position.x,Is.LessThan(4));
                Assert.That(motion.Body.Velocity.magnitude,Is.LessThan(.1f));
                motion.JumpRequested=true;motion.Sample(1f/fps);
                Assert.That(motion.Body.Velocity.y,Is.GreaterThan(18));
            }
            finally{motion?.Collision.Dispose();Object.DestroyImmediate(rig);Object.DestroyImmediate(roof);}
        }
        [TestCase(30)][TestCase(60)][TestCase(120)]
        public void ReleaseRetainsSpeedAndFallIsFaster(int fps)
        {
            var body=new SwingPhysics(WebSwingMotionInput.TraversalSettings());
            body.Reset(Vector3.up*1000);body.Velocity=Vector3.forward*45;body.Attach(body.Position+Vector3.up*100);
            var before=body.Velocity;body.Release();Assert.That(body.Velocity,Is.EqualTo(before));
            for(int i=0;i<fps*2;i++) body.Step(1f/fps);
            Assert.That(body.Velocity.z,Is.GreaterThan(44));
            Assert.That(1000-body.Position.y,Is.InRange(47f,49f));
            Assert.That(body.Position.z,Is.InRange(88f,90f));
        }
        [Test] public void CruiseAcceleratesAlongRopeTangentAndHonorsBraking()
        {
            var body=new SwingPhysics(WebSwingMotionInput.TraversalSettings());
            body.Reset(Vector3.up*100);body.Attach(Vector3.up*150);body.Velocity=Vector3.forward*30;
            body.AssistCruise(.1f,false);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));Assert.That(body.Velocity.y,Is.Zero);
            body.AssistCruise(.1f,true);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));
            body.AssistStrength=0;body.AssistCruise(.1f,false);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));
            body.Release();body.AssistStrength=1;body.AssistCruise(.1f,false);Assert.That(body.Velocity.z,Is.EqualTo(32).Within(.001));
        }
        [Test] public void ActiveHandIsInFrontOfScreenButClearOfEye()
        {
            Assert.That(TraversalHands.WristDepth(-.6f,.78f,1),Is.LessThan(-.15f));
            Assert.That(TraversalHands.WristDepth(-.2f,1,1),Is.GreaterThan(-.1f));
            Assert.That(TraversalHands.WristDepth(-.6f,.78f,0),Is.GreaterThan(0));
            Assert.That(TraversalHands.ShouldShow(true,false,true,1),Is.True);
            Assert.That(TraversalHands.ShouldShow(true,false,false,1),Is.False);
            Assert.That(TraversalHands.ShouldShow(true,false,true,0),Is.False);
            Assert.That(TraversalHands.ShouldShow(true,true,true,1),Is.False);
        }
    }
}
