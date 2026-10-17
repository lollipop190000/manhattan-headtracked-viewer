using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalMouseReelTests
    {
        private Mouse mouse;private Keyboard keyboard;private bool active;private HumanTraversalInput human;
        [SetUp] public void SetUp(){mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();active=true;human=new HumanTraversalInput(null,()=>active);}
        [TearDown] public void TearDown(){InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);}
        private void Buttons(bool right=false,bool left=false,bool e=false,bool q=false,bool z=false)
        {
            var state=new MouseState();if(right)state=state.WithButton(MouseButton.Right);if(left)state=state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse,state);
            var keys=new System.Collections.Generic.List<Key>();if(e)keys.Add(Key.E);if(q)keys.Add(Key.Q);if(z)keys.Add(Key.Z);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));
            InputSystem.Update();
        }
        [Test] public void RightButtonIsContinuousReelAndNeverRelease()
        {
            human.ReadActions();Buttons(right:true);
            for(int i=0;i<3;i++){var a=human.ReadActions();Assert.That(a.reel,Is.EqualTo(1));Assert.That(a.release,Is.False);InputSystem.Update();}
            Buttons();Assert.That(human.ReadActions().reel,Is.Zero);
        }
        [Test] public void KeyboardAliasDoesNotDoubleReelAndOpposingPayoutCancels()
        {
            Buttons(right:true,e:true);
            Assert.That(human.ReadActions().reel,Is.EqualTo(1));
            Buttons(right:true,e:true,z:true);Assert.That(human.ReadActions().reel,Is.Zero);
            Buttons(z:true);
            Assert.That(human.ReadActions().reel,Is.EqualTo(-1));
        }
        [Test] public void QChargesLaunchAndNeverPaysOut()
        {
            Buttons();human.ReadActions();Buttons(q:true);var a=human.ReadActions();
            Assert.That(a.launchHeld,Is.True,"held Q");Assert.That(a.launchPressed,Is.True,"Q down edge");Assert.That(a.reel,Is.Zero);
            Buttons();a=human.ReadActions();Assert.That(a.launchReleased,Is.True,"Q up edge");Assert.That(a.launchHeld,Is.False);
        }
        [Test] public void InactiveInputCannotReel()
        {
            Buttons(right:true);active=false;
            var a=human.ReadActions();Assert.That(a.active,Is.False);Assert.That(a.reel,Is.Zero);Assert.That(a.release,Is.False);
        }
        [Test] public void RealMouseHoldReelsSixMetresWithoutDetaching()
        {
            var rig=new GameObject();var anchor=GameObject.CreatePrimitive(PrimitiveType.Cube);WebSwingMotionInput motion=null;
            try
            {
                var flight=rig.AddComponent<Manhattan.Viewer.FreeFlightController>();flight.enabled=false;flight.SetPose(Vector3.up*100,Quaternion.identity);
                var s=WebSwingMotionInput.TraversalSettings();s.features.SetFlow(true);s.gravity=0;s.airDrag=0;
                anchor.layer=30;anchor.transform.position=Vector3.up*150;Physics.SyncTransforms();
                motion=new WebSwingMotionInput(human,flight,null,s);motion.Activate();
                Buttons(left:true);human.ReadActions();InputSystem.Update();
                Assert.That(motion.HandleMouseHold(true,true,true,new Ray(Vector3.up*100,Vector3.up)),Is.True);
                for(int i=0;i<60;i++){var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                Assert.That(motion.Body.Attached,Is.True);
                float initial=motion.Body.RopeLength;Buttons(right:true,left:true);
                for(int i=0;i<30;i++){var c=motion.Sample(1f/120);flight.SetPose(c.cityPosition,c.cityRotation);}
                Assert.That(motion.Body.Attached,Is.True);Assert.That(initial-motion.Body.RopeLength,Is.EqualTo(6).Within(.001f));
                Buttons(left:true);var command=motion.Sample(1f/120);flight.SetPose(command.cityPosition,command.cityRotation);
                Assert.That(motion.Body.Attached,Is.True);Assert.That(motion.ReelInput,Is.Zero);
                TestContext.WriteLine($"MOUSE heldRightSeconds=0.25 shortenedMetres={initial-motion.Body.RopeLength:F6} stillAttached={motion.Body.Attached}");
            }
            finally{motion?.Collision.Dispose();Object.DestroyImmediate(anchor);Object.DestroyImmediate(rig);}
        }
    }
}
