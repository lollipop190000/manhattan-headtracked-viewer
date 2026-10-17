using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
namespace WebShooter.Swing.Tests
{
    public sealed class AirChainAssistanceTests
    {
        [TestCase(true,5,false)][TestCase(false,5,true)][TestCase(true,25,true)]
        public void FastShotRejectsUnsafeCloseAnchorWithoutChangingVelocity(bool assisted,float range,bool expected)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);var rig=new GameObject();WebSwingMotionInput m=null;
            try
            {
                var start=Vector3.up*3000;wall.layer=30;wall.transform.position=start+new Vector3(0,0,range+.5f);
                wall.transform.localScale=new Vector3(20,40,1);Physics.SyncTransforms();
                var f=rig.AddComponent<FreeFlightController>();f.enabled=false;f.SetPose(start,Quaternion.identity);
                var s=TraversalTuning.Defaults();s.chain.enabled=assisted;
                m=new WebSwingMotionInput(null,f,null,s);m.ReconnectAssist.enabled=true;m.Activate();m.Body.Velocity=Vector3.forward*40;
                Assert.That(m.TryFire(new Ray(start,Vector3.forward)),Is.EqualTo(expected));
                Assert.That(m.Body.Velocity,Is.EqualTo(Vector3.forward*40));
            }
            finally{m?.Collision.Dispose();Object.DestroyImmediate(wall);Object.DestroyImmediate(rig);}
        }
        [TestCase(true)][TestCase(false)]
        public void ForwardInputOnSlackRopeDoesNotInventVerticalOrSideMotion(bool assisted)
        {
            var s=TraversalTuning.Defaults();s.gravity=s.airDrag=0;s.chain.enabled=assisted;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(b.Position+new Vector3(5,60,120));b.Velocity=Vector3.forward*40;
            for(int i=0;i<15;i++)b.Step(.1f,Vector3.forward);
            TestContext.WriteLine($"assisted={assisted} p={b.Position:F5} v={b.Velocity:F5} loaded={b.RopeLoaded}");
            if(assisted){Assert.That(b.Velocity.x,Is.Zero.Within(.001f));Assert.That(b.Velocity.y,Is.Zero.Within(.001f));Assert.That(b.Velocity.z,Is.GreaterThanOrEqualTo(40));}
            else Assert.That(b.Velocity.y,Is.LessThan(-.1f));
        }
        [TestCase(1,1,7.2f)][TestCase(.25f,1,1.8f)][TestCase(1,.25f,1.8f)][TestCase(0,1,0)][TestCase(1,0,0)]
        public void ReleaseBankPreservesSpeedWithoutInputOrAssistAutoturn(float assist,float axis,float degrees)
        {
            var s=TraversalTuning.Defaults();s.gravity=s.airDrag=0;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.AssistStrength=assist;b.Velocity=new Vector3(0,7,40);
            b.Attach(b.Position+Vector3.up*2000);b.Release();b.Step(.1f,Vector3.right*axis);
            Assert.That(new Vector2(b.Velocity.x,b.Velocity.z).magnitude,Is.EqualTo(40).Within(.002f));
            Assert.That(b.Velocity.y,Is.EqualTo(7).Within(.001f));
            Assert.That(Mathf.Atan2(b.Velocity.x,b.Velocity.z)*Mathf.Rad2Deg,Is.EqualTo(degrees).Within(.01f));
        }
        [TestCase(30,true)][TestCase(60,true)][TestCase(120,true)]
        [TestCase(30,false)][TestCase(60,false)][TestCase(120,false)]
        public void CornerBrakeRemovesWallApproachWhileKeepingForwardTravel(int fps,bool enabled)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);var rig=new GameObject();WebSwingMotionInput m=null;
            try
            {
                var start=new Vector3(-5,10000,0);wall.layer=30;wall.transform.position=new Vector3(.5f,10050,0);
                wall.transform.localScale=new Vector3(1,300,400);Physics.SyncTransforms();
                var f=rig.AddComponent<FreeFlightController>();f.enabled=false;f.SetPose(start,Quaternion.identity);
                var controls=new BufferedTraversalInput{Movement=Vector2.up};
                var s=TraversalTuning.Defaults();s.gravity=s.airDrag=0;s.chain.wallApproachBrake=enabled;s.chain.cornerSpeed=1000;
                m=new WebSwingMotionInput(controls,f,null,s);m.ReconnectAssist.enabled=true;m.Activate();m.Body.Velocity=new Vector3(30,0,40);
                controls.Actions=new TraversalActions{active=true,firePressed=true,fireHeld=true,aim=new Ray(start,new Vector3(5,60,120))};
                int contacts=0;float clearance=100;
                m.PhysicsStepped+=frame=>
                {
                    if(frame.collided)contacts++;clearance=Mathf.Min(clearance,-frame.position.x);
                    if(enabled && fps==120)TestContext.WriteLine($"BRAKE_TRACE t{frame.time:F4} x{frame.position.x:F5} vx{frame.velocity.x:F5} vz{frame.velocity.z:F5} threat{m.Collision.CornerThreat} normal{m.Collision.CornerNormal} rate{m.Body.AssistedRedirectRate} loaded{m.Body.RopeLoaded} count{m.WallApproachBrakeSteps} hit{frame.collided}");
                };
                for(int i=0;i<Mathf.CeilToInt(.6f*fps);i++){var c=m.Sample(1f/fps);f.SetPose(c.cityPosition,c.cityRotation);}
                TestContext.WriteLine($"fps={fps} enabled={enabled} contacts={contacts} clearance={clearance:F5} v={m.Body.Velocity:F5} brakingSteps={m.WallApproachBrakeSteps}");
                if(enabled)
                {
                    Assert.That(contacts,Is.Zero);Assert.That(clearance,Is.GreaterThanOrEqualTo(.319f));
                    Assert.That(m.Body.Velocity.z,Is.GreaterThan(35));Assert.That(m.WallApproachBrakeSteps,Is.GreaterThan(0));
                }
                else Assert.That(contacts,Is.GreaterThan(0));
            }
            finally{m?.Collision.Dispose();Object.DestroyImmediate(wall);Object.DestroyImmediate(rig);}
        }
        [TestCase(true)][TestCase(false)]
        public void CloseAnchorReelGuardDoesNotAddLiftOrRedirectMomentum(bool guarded)
        {
            var s=TraversalTuning.Defaults();s.gravity=s.airDrag=0;s.chain.closeAnchorReelGuard=guarded;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(b.Position+new Vector3(8,4,8));
            b.Velocity=Vector3.forward*40;b.Step(.1f,Vector3.zero,1);
            TestContext.WriteLine($"guard={guarded} p={b.Position:F5} v={b.Velocity:F5} length={b.RopeLength:F5}");
            if(guarded)
            {
                Assert.That(b.ReelGuardSequence,Is.GreaterThan(0));
                Assert.That(b.Velocity,Is.EqualTo(Vector3.forward*40));
                Assert.That(b.Position.y,Is.EqualTo(100).Within(.001f));
                Assert.That(b.State,Is.EqualTo(TraversalState.Swinging));
            }
            else Assert.That(b.Velocity.y,Is.GreaterThan(.1f));
        }
        [Test] public void RopeCorrectionCannotHideAnEarlierFloorTouchFromDiagnostics()
        {
            var s=new SwingSettings{gravity=0,airDrag=0,attachmentTakeupSeconds=0};
            s.features.attachmentDrop=true;s.features.initialLengthRatio=.75f;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*(b.FloorY+.05f));b.Attach(b.Position+Vector3.up*40);
            b.Velocity=Vector3.down*40;b.Step(1f/120);
            Assert.That(b.Position.y,Is.GreaterThan(b.FloorY+5));
            Assert.That(b.FloorContactSequence,Is.EqualTo(1));
            Assert.That(b.ResetSequence,Is.EqualTo(1));
            b.Reset(Vector3.up*40);Assert.That(b.ResetSequence,Is.EqualTo(2));Assert.That(b.FloorContactSequence,Is.Zero);
        }
        [TestCase(1.2f,TraversalState.Airborne)][TestCase(0,TraversalState.WallRunning)]
        public void ReleaseGraceDefersProximityEntryWithoutDisablingCollision(float grace,TraversalState expected)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);var rig=new GameObject();WebSwingMotionInput m=null;
            try
            {
                var start=new Vector3(-.9f,3000,0);wall.layer=30;wall.transform.position=new Vector3(.5f,3000,0);
                wall.transform.localScale=new Vector3(1,100,200);Physics.SyncTransforms();
                var f=rig.AddComponent<FreeFlightController>();f.enabled=false;f.SetPose(start,Quaternion.identity);
                var input=new BufferedTraversalInput{Movement=Vector2.up};
                var s=TraversalTuning.Defaults();s.chain.wallProximityGraceSeconds=grace;
                m=new WebSwingMotionInput(input,f,null,s);m.Activate();m.Body.Velocity=Vector3.forward*20;
                input.Actions=new TraversalActions{active=true,firePressed=true,fireHeld=true,aim=new Ray(start,new Vector3(.9f,20,20))};
                for(int i=0;i<6;i++){var c=m.Sample(1f/120);f.SetPose(c.cityPosition,c.cityRotation);}
                Assert.That(m.Body.Attached,Is.True);
                input.Actions=new TraversalActions{active=true};
                for(int i=0;i<24;i++){var c=m.Sample(1f/120);f.SetPose(c.cityPosition,c.cityRotation);}
                Assert.That(m.Body.State,Is.EqualTo(expected));
                Assert.That(m.Body.ContactSequence,Is.Zero);
                var hit=m.Collision.Constrain(m.Body.Position,m.Body.Position+Vector3.right*10);
                Assert.That(hit.x,Is.LessThanOrEqualTo(-.319f),"real wall sweep remains active during grace");
            }
            finally{m?.Collision.Dispose();Object.DestroyImmediate(wall);Object.DestroyImmediate(rig);}
        }
        [TestCase(10,12,true)][TestCase(10,0,false)][TestCase(30,12,false)]
        public void AirborneMissFindsSmallVisibleFacadeOnlyInsideBoundedCone(float offset,float radius,bool expected)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);var rig=new GameObject();WebSwingMotionInput m=null;
            try
            {
                var start=Vector3.up*3000;wall.layer=30;wall.transform.position=start+new Vector3(offset,40,100);
                wall.transform.localScale=new Vector3(.2f,4,1);Physics.SyncTransforms();
                var f=rig.AddComponent<FreeFlightController>();f.enabled=false;f.SetPose(start,Quaternion.identity);
                var s=TraversalTuning.Defaults();s.chain.aimAcquisitionRadius=radius;
                m=new WebSwingMotionInput(null,f,null,s);m.ReconnectAssist.enabled=true;m.Activate();
                var ray=new Ray(start,new Vector3(0,40,100));
                Assert.That(m.TryFire(ray),Is.EqualTo(expected));
                if(expected)
                {
                    Assert.That(Vector3.Angle(ray.direction,m.ShotTarget-start),Is.LessThanOrEqualTo(12));
                    Assert.That(Physics.Raycast(start,(m.ShotTarget-start).normalized,out var hit,250,TraversalCollision.Mask),Is.True);
                    Assert.That(hit.collider,Is.EqualTo(wall.GetComponent<Collider>()));
                }
            }
            finally{m?.Collision.Dispose();Object.DestroyImmediate(wall);Object.DestroyImmediate(rig);}
        }
        [TestCase(1,0,40)][TestCase(0,1,40)][TestCase(1,1,39)]
        public void EarlySlackTakeupRequiresReelAndAssistAndDoesNotAddVelocity(float assist,float reel,float expectedLength)
        {
            var s=TraversalTuning.Defaults();s.gravity=s.airDrag=s.reelSpeed=0;s.attachmentTakeupSeconds=0;
            var body=new SwingPhysics(s);body.Reset(Vector3.up*40);body.AssistStrength=assist;
            body.Attach(body.Position+Vector3.up*40);body.Velocity=Vector3.up*10;
            body.Step(.1f,Vector3.zero,reel);
            Assert.That(body.RopeLength,Is.EqualTo(expectedLength).Within(.002f));
            Assert.That(body.Velocity,Is.EqualTo(Vector3.up*10));
            Assert.That(body.Position.y,Is.EqualTo(41).Within(.002f));
        }
        [TestCase(1,1,12)][TestCase(.25f,1,3)][TestCase(1,.25f,3)][TestCase(0,1,0)][TestCase(1,0,0)]
        public void InputBankKeepsHorizontalAndVerticalSpeed(float assist,float axis,float expected)
        {
            var s=TraversalTuning.Defaults();s.gravity=s.airDrag=0;
            var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.AssistStrength=assist;b.Velocity=new Vector3(0,7,40);
            b.Attach(b.Position+Vector3.up*2000);b.Step(.1f,Vector3.right*axis);
            Assert.That(new Vector2(b.Velocity.x,b.Velocity.z).magnitude,Is.EqualTo(40).Within(.002f));
            Assert.That(b.Velocity.y,Is.EqualTo(7).Within(.001f));
            Assert.That(Mathf.Atan2(b.Velocity.x,b.Velocity.z)*Mathf.Rad2Deg,Is.EqualTo(expected).Within(.01f));
        }
        [TestCase(25)][TestCase(100)][TestCase(200)]
        public void FirstAirborneShotAttachesInFiveSubstepsWithoutAddedSpeed(float range)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);var rig=new GameObject();WebSwingMotionInput m=null;
            try
            {
                var start=Vector3.up*3000;wall.layer=30;wall.transform.position=start+new Vector3(0,20,range);wall.transform.localScale=new Vector3(20,40,1);Physics.SyncTransforms();
                var f=rig.AddComponent<FreeFlightController>();f.enabled=false;f.SetPose(start,Quaternion.identity);
                var s=TraversalTuning.Defaults();s.gravity=s.airDrag=0;m=new WebSwingMotionInput(null,f,null,s);m.ReconnectAssist.enabled=true;m.Activate();m.Body.Velocity=Vector3.forward*40;
                Assert.That(m.TryFire(new Ray(start,wall.transform.position-start)),Is.True);
                for(int i=0;i<5;i++){var c=m.Sample(1f/120);f.SetPose(c.cityPosition,c.cityRotation);}
                Assert.That(m.Body.Attached,Is.True);Assert.That(m.ShotAge,Is.LessThanOrEqualTo(.05f));
                Assert.That(m.Body.Velocity.magnitude,Is.EqualTo(40).Within(.001f));
                var v=m.Body.Velocity;m.Release();Assert.That(m.Body.Velocity,Is.EqualTo(v));
            }
            finally{m?.Collision.Dispose();Object.DestroyImmediate(wall);Object.DestroyImmediate(rig);}
        }
        [Test] public void FirstGroundMissDoesNotBufferDespiteUninitializedBody()
        {
            var rig=new GameObject();WebSwingMotionInput m=null;
            try
            {
                var f=rig.AddComponent<FreeFlightController>();f.enabled=false;f.SetPose(Vector3.up*1.85f,Quaternion.identity);
                m=new WebSwingMotionInput(null,f,null,TraversalTuning.Defaults());m.ReconnectAssist.enabled=true;
                Assert.That(m.HandleMouseHold(true,true,true,new Ray(f.CityPosition,Vector3.up)),Is.False);
                Assert.That(m.ReconnectPending,Is.False);
            }
            finally{m?.Collision.Dispose();Object.DestroyImmediate(rig);}
        }
    }
}
