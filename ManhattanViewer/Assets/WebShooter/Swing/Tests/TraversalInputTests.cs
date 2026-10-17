using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class ReconnectInputTests
    {
        private GameObject rig,wall;
        private Manhattan.Viewer.FreeFlightController flight;
        private WebSwingMotionInput motion;
        private Ray Target=>new Ray(motion.Body.Position,wall.transform.position-motion.Body.Position);
        private Ray Miss=>new Ray(motion.Body.Position,Vector3.right);
        [SetUp] public void SetUp()
        {
            rig=new GameObject();flight=rig.AddComponent<Manhattan.Viewer.FreeFlightController>();flight.enabled=false;
            flight.SetPose(Vector3.up*100,Quaternion.identity);
            var settings=WebSwingMotionInput.TraversalSettings();settings.gravity=0;settings.airDrag=0;
            motion=new WebSwingMotionInput(null,flight,null,settings);motion.Activate();
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.layer=30;
            wall.transform.position=new Vector3(0,125,100);wall.transform.localScale=new Vector3(10,20,1);Physics.SyncTransforms();
        }
        [TearDown] public void TearDown(){motion.Collision.Dispose();Object.DestroyImmediate(wall);Object.DestroyImmediate(rig);}
        private void Prime()
        {motion.ReconnectAssist.enabled=true;motion.Body.Attach(motion.Body.Position+Vector3.up*50);motion.Release();}
        private void Step(float seconds)
        {
            for(float remaining=seconds;remaining>.000001f;)
            {float h=Mathf.Min(1f/120,remaining);var c=motion.Sample(h);flight.SetPose(c.cityPosition,c.cityRotation);remaining-=h;}
        }
        [Test] public void FirstShotAndAgentDefaultKeepTheirOriginalAttachmentTiming()
        {
            Assert.That(motion.ReconnectAssist.enabled,Is.False);
            motion.ReconnectAssist.enabled=true;
            Assert.That(motion.TryFire(Target),Is.True);Step(5f/120);
            Assert.That(motion.Body.Attached,Is.False);Assert.That(motion.LastShotWasReconnect,Is.False);
        }
        [Test] public void NearMissReconnectUsesBoundedConeButPreservesAnExactDirectHit()
        {
            Prime();var ray=new Ray(motion.Body.Position,new Vector3(12,25,100));
            Assert.That(Physics.Raycast(ray,250,1<<30),Is.False);
            Assert.That(motion.TryFire(ray),Is.True);
            TestContext.WriteLine($"RECONNECT coneAngle={Vector3.Angle(ray.direction,motion.ShotTarget-ray.origin):F4} target={motion.ShotTarget}");
            Assert.That(Vector3.Angle(ray.direction,motion.ShotTarget-ray.origin),Is.LessThanOrEqualTo(12.01f));
            Physics.Raycast(Target,out var direct,250,1<<30);
            Assert.That(motion.TryFire(Target),Is.True);Assert.That(Vector3.Distance(motion.ShotTarget,direct.point),Is.LessThan(.001f));
        }
        [Test] public void AssistNeverFindsAnAnchorOutsideItsConeOrRange()
        {
            Prime();Assert.That(motion.TryFire(Miss),Is.False);
            wall.transform.position=new Vector3(0,150,300);Physics.SyncTransforms();
            Assert.That(motion.TryFire(Target),Is.False);Assert.That(motion.Body.Attached||motion.Flying,Is.False);
        }
        [Test] public void BufferedMissUsesTheFreshAimWithoutAnotherPress()
        {
            Prime();Assert.That(motion.HandleMouseHold(true,true,true,Miss),Is.False);Assert.That(motion.ReconnectPending,Is.True);
            Step(.05f);Assert.That(motion.HandleMouseHold(true,false,true,Target),Is.True);
            Assert.That(motion.ReconnectPending,Is.False);Step(5f/120);
            TestContext.WriteLine($"RECONNECT bufferedAttachSeconds={motion.ShotAge:F6}");Assert.That(motion.Body.Attached,Is.True);
            Assert.That(motion.ShotAge,Is.InRange(.04f,.05f));
        }
        [TestCase(false)][TestCase(true)] public void ReleaseOrFocusLossCancelsTheBufferedShot(bool focusLost)
        {
            Prime();motion.HandleMouseHold(true,true,true,Miss);
            motion.HandleMouseHold(!focusLost,false,focusLost,Miss);Step(.05f);
            Assert.That(motion.HandleMouseHold(true,false,true,Target),Is.False);Assert.That(motion.ReconnectPending,Is.False);
            Assert.That(motion.Flying||motion.Body.Attached,Is.False);
        }
        [Test] public void ExpiredMissOrDisabledAssistCannotFireLater()
        {
            Prime();motion.HandleMouseHold(true,true,true,Miss);Step(.4f);
            Assert.That(motion.HandleMouseHold(true,false,true,Target),Is.False);
            motion.HandleMouseHold(true,true,true,Miss);motion.ReconnectAssist.enabled=false;Step(.05f);
            Assert.That(motion.HandleMouseHold(true,false,true,Target),Is.False);
        }
        [Test] public void ReleaseWindowExpiresAndNoOldSensorRayIsReplayed()
        {
            Prime();Assert.That(motion.TryFire(Miss),Is.False);Assert.That(motion.ReconnectPending,Is.False);
            Step(1.3f);Assert.That(motion.TryFire(Target),Is.True);Step(5f/120);
            Assert.That(motion.LastShotWasReconnect,Is.False);Assert.That(motion.Body.Attached,Is.False);
        }
        [Test] public void ReconnectionAndReleaseDoNotAddOrDiscardVelocity()
        {
            Prime();motion.Body.Velocity=Vector3.right*40;var velocity=motion.Body.Velocity;
            Assert.That(motion.HandleMouseHold(true,true,true,Target),Is.True);Assert.That(motion.Body.Velocity,Is.EqualTo(velocity));
            motion.HandleMouseHold(true,false,false,default);Assert.That(motion.Body.Velocity,Is.EqualTo(velocity));
            Assert.That(motion.Body.Attached||motion.Flying,Is.False);
        }
        [Test] public void ZeroGlobalAssistDisablesConeBufferAndQuickAttachment()
        {
            Prime();motion.HandleMouseHold(true,true,true,Miss);motion.Body.AssistStrength=0;Step(.05f);
            Assert.That(motion.HandleMouseHold(true,false,true,Target),Is.False);Assert.That(motion.ReconnectPending,Is.False);
            Assert.That(motion.TryFire(Target),Is.True);Step(5f/120);
            Assert.That(motion.LastShotWasReconnect,Is.False);Assert.That(motion.Body.Attached,Is.False);
        }
        [Test] public void SharedActionAdapterBuffersFreshAimAndReleasesWithoutAnotherPress()
        {
            Prime();var controls=new BufferedTraversalInput{Actions=new TraversalActions{active=true,firePressed=true,fireHeld=true,aim=Miss}};
            motion.InputOverride=controls;Step(1f/120);Assert.That(motion.ReconnectPending,Is.True);
            controls.Actions.aim=Target;Step(.1f);Assert.That(motion.Body.Attached,Is.True);
            controls.Actions.fireHeld=false;motion.Body.Velocity=Vector3.right*40;Step(1f/120);
            Assert.That(motion.Body.Attached||motion.Flying,Is.False);Assert.That(motion.Body.Velocity,Is.EqualTo(Vector3.right*40));
        }
    }
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
