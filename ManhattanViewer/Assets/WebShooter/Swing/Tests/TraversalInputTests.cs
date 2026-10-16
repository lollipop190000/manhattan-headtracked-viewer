using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class TraversalInputTests
    {
        private sealed class ForbiddenLiveInput : Manhattan.Viewer.IMotionInputSource
        {public Manhattan.Viewer.MotionCommand Sample(float dt)=>throw new System.InvalidOperationException("Replay polled live input");}
        [Test] public void BufferedInputConsumesEdgesButKeepsHolds()
        {
            var input=new BufferedTraversalInput{Movement=Vector2.up,LookRate=new Vector2(90,0),Actions=new TraversalActions{active=true,fireHeld=true,firePressed=true,jump=true,reel=1}};
            var first=input.ReadActions();var second=input.ReadActions();
            Assert.That(first.firePressed && first.jump,Is.True);Assert.That(second.firePressed || second.jump,Is.False);
            Assert.That(second.fireHeld,Is.True);Assert.That(second.reel,Is.EqualTo(1));
            Assert.That(input.Sample(1f/60).lookDegrees.x,Is.EqualTo(1.5f).Within(.00001f));
        }
        [Test] public void ExplicitReplayOverrideDoesNotPollTheLiveInputAdapter()
        {
            var rig=new GameObject();WebSwingMotionInput motion=null;
            try
            {
                var flight=rig.AddComponent<Manhattan.Viewer.FreeFlightController>();flight.SetPose(Vector3.up*100,Quaternion.identity);
                var settings=WebSwingMotionInput.TraversalSettings();settings.gravity=0;
                var replay=new BufferedTraversalInput{Movement=Vector2.up,LookRate=new Vector2(90,0),Actions=new TraversalActions{active=true}};
                motion=new WebSwingMotionInput(new ForbiddenLiveInput(),flight,null,settings){InputOverride=replay};
                var result=motion.Sample(1f/60);Assert.That(Mathf.DeltaAngle(0,result.cityRotation.eulerAngles.y),Is.EqualTo(1.5f).Within(.0001f));
                Assert.That(motion.Body.Velocity.magnitude,Is.GreaterThan(0));
            }
            finally{motion?.Collision.Dispose();Object.DestroyImmediate(rig);}
        }
        [Test] public void SerializedInspectorFlowFlagsReachTheExistingPhysicsSettings()
        {
            var rig=new GameObject();
            try
            {
                var tuning=rig.AddComponent<TraversalTuning>();var body=new SwingPhysics(tuning.Settings);
                var serialized=new UnityEditor.SerializedObject(tuning);
                foreach(bool enabled in new[]{false,true})
                {
                    foreach(var field in new[]{"preserveSteeringMomentum","preserveGlancingContact","preserveTransitionMomentum","resetLengthAtWrap"})
                        serialized.FindProperty("settings.features."+field).boolValue=enabled;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Assert.That(body.Settings.features.preserveSteeringMomentum,Is.EqualTo(enabled));
                    Assert.That(body.Settings.features.preserveGlancingContact,Is.EqualTo(enabled));
                    Assert.That(body.Settings.features.preserveTransitionMomentum,Is.EqualTo(enabled));
                    Assert.That(body.Settings.features.resetLengthAtWrap,Is.EqualTo(enabled));
                }
            }
            finally{Object.DestroyImmediate(rig);}
        }
    }
}
