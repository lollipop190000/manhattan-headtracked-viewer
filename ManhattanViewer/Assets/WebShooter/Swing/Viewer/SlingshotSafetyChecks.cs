using System;
using Manhattan.Viewer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
namespace WebShooter.Swing
{
    public static class SlingshotSafetyChecks
    {
        public static void Run(Action<bool,string> check,Action<string,float> metric)
        {
            var rig=new GameObject("Launch safety rig");var flight=rig.AddComponent<FreeFlightController>();flight.enabled=false;
            var controls=new BufferedTraversalInput();var settings=TraversalTuning.Defaults();
            var motion=new WebSwingMotionInput(controls,flight,null,settings);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Launch frontal blocker";wall.layer=30;
            var start=new Vector3(-10000,3000,-10000);
            void Frame(float dt){var c=motion.Sample(dt);flight.SetPose(c.cityPosition,c.cityRotation);}
            void Reset(Vector3 p){controls.Clear();controls.Actions=new TraversalActions{active=true};flight.SetPose(p,Quaternion.identity);motion.Activate();}
            try
            {
                var keyboard=InputSystem.AddDevice<Keyboard>();var mouse=InputSystem.AddDevice<Mouse>();
                try
                {
                    var human=new HumanTraversalInput(null,()=>true);
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();human.ReadActions();
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));InputSystem.Update();var actions=human.ReadActions();
                    check(actions.launchPressed && actions.launchHeld && actions.reel==0,"shipping human Q holds launch, never payout");
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));InputSystem.Update();actions=human.ReadActions();
                    check(actions.launchReleased && !actions.launchHeld && actions.reel==-1,"shipping Q-up releases and Z pays out");
                }
                finally{InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);}
                wall.transform.position=start+Vector3.forward*6;wall.transform.localScale=new Vector3(200,500,.1f);Physics.SyncTransforms();Reset(start);
                check(motion.Launch.Begin(start,Vector3.forward,Vector3.zero,TraversalCollision.Mask),"front fixture has two real attachment points");
                motion.Launch.Tick(2,start,Vector3.zero,TraversalCollision.Mask);
                check(!motion.Launch.Release(motion.Body,Vector3.forward,Vector3.zero,TraversalCollision.Mask),"blocked maximum launch rejected before entering a wall");
                check(motion.Body.Position==start && motion.Body.Velocity==Vector3.zero,"rejected launch neither teleports nor adds velocity");
                foreach(int fps in new[]{30,60,120})foreach(float speed in new[]{82f,120f,180f})
                {
                    Reset(start);motion.Body.Velocity=Vector3.forward*speed;
                    float maxZ=start.z;for(int i=0;i<fps/2;i++){Frame(1f/fps);maxZ=Mathf.Max(maxZ,motion.Body.Position.z);}
                    check(maxZ<start.z+5.66f,$"{fps}fps {speed}m/s thin wall never crossed");
                    metric($"thinWall_{fps}_{speed}_clearance",start.z+5.95f-maxZ);
                }
                // Recreate the old visible snap with a deliberately shortened
                // attachment. The safety layer must detach, never restore history.
                wall.transform.position=start+Vector3.up*100;wall.transform.localScale=new Vector3(40,1,40);Physics.SyncTransforms();
                foreach(bool guard in new[]{false,true})
                {
                    Reset(start);motion.PreventConstraintSnaps=guard;settings.features.attachmentDrop=true;settings.features.initialLengthRatio=.1f;
                    check(motion.TryFire(new Ray(start,Vector3.up)),"short constraint fixture hit");
                    float maxTravel=0;for(int i=0;i<12;i++){var p=motion.Body.Position;Frame(1f/60);maxTravel=Mathf.Max(maxTravel,Vector3.Distance(p,motion.Body.Position));}
                    metric($"shortConstraint_guard{guard}_maxFrameTravel",maxTravel);
                    if(guard)check(maxTravel<1 && motion.ConstraintSnapPreventions>0,"excessive rope projection detached without a pose jump");
                    else check(maxTravel>50,"unguarded snap reproduced and measured");
                }
                settings.features.attachmentDrop=false;motion.PreventConstraintSnaps=true;
                wall.transform.position=start+Vector3.right*5;wall.transform.localScale=new Vector3(10,200,200);Physics.SyncTransforms();
                foreach(int fps in new[]{30,60,120})
                {
                    Reset(start+Vector3.left*.35f);motion.Body.Velocity=new Vector3(15,0,40);var prior=motion.Body.Position;float maxTravel=0;
                    for(int i=0;i<fps;i++){Frame(1f/fps);maxTravel=Mathf.Max(maxTravel,Vector3.Distance(prior,motion.Body.Position));prior=motion.Body.Position;}
                    check(motion.Body.Position.x<=start.x-.3f && maxTravel<50f/fps+.1f,$"{fps}fps rubbing wall has bounded successive poses");
                    metric($"wallRub_{fps}_maxFrameTravel",maxTravel);
                }
                check(TraversalTemporalHistory.Weight(.95f,.55f,60,false,false)<.56f,"fast motion reduces old-frame history weight");
                check(Mathf.Abs(TraversalTemporalHistory.Weight(.95f,.55f,0,false,false)-.95f)<.001f,"stationary AA preference preserved");
                check(TraversalTemporalHistory.Weight(.95f,.55f,0,false,false,.3f,0)<.56f,"head translation reduces history without steering physics");
                check(TraversalTemporalHistory.Weight(.95f,.55f,0,false,false,0,90)<.56f,"fast view rotation reduces old-frame trails");
                metric("history95_halfLifeFrames",Mathf.Log(.5f)/Mathf.Log(.95f));
                metric("history55_halfLifeFrames",Mathf.Log(.5f)/Mathf.Log(.55f));
                foreach(bool guard in new[]{false,true})
                {
                    var s=TraversalTuning.Defaults();s.chain.closeAnchorReelGuard=guard;
                    var b=new SwingPhysics(s);b.Reset(Vector3.up*100);b.Attach(b.Position+new Vector3(10,10,0));b.Velocity=new Vector3(-20,20,5);
                    b.AssistCruise(.1f,false);metric($"closeCruise_guard{guard}_verticalSpeed",b.Velocity.y);
                    if(guard)check(Mathf.Abs(b.Velocity.y-20)<.001f,"near-corner W cruise adds no powered vertical velocity");
                }
                var launch=new DualWebLaunch();wall.SetActive(false);
                check(!launch.Begin(start,Vector3.forward,Vector3.zero,TraversalCollision.Mask),"empty sky cannot store launch energy");
                using var map=new AirChainReplay.Map(22);
                foreach(int fps in new[]{30,60,120})
                {
                    var p=map.Center+new Vector3(0,45,-300);Reset(p);motion.Body.Velocity=Vector3.forward*40;
                    motion.Collision.CitySolids=new CitySolidRecovery(map.Records,motion.Body);motion.AutomaticCruiseAssist=true;
                    var before=motion.Body.Position;var velocity=motion.Body.Velocity;int sequence=motion.Body.ContactSequence;
                    using(var pilot=new TraversalPreviewPilot())pilot.Select(motion,flight.transform.rotation,Vector3.forward,map.Center,
                        p+new Vector3(22,65,35),0,fps,out _,out _);
                    check(motion.Body.Position==before && motion.Body.Velocity==velocity && motion.Body.ContactSequence==sequence,$"{fps}fps prediction leaves live state untouched");
                    var observerObject=new GameObject("Prediction comparison");var observer=observerObject.AddComponent<FreeFlightController>();observer.enabled=false;
                    var copyInput=new BufferedTraversalInput();observer.SetPose(p,flight.transform.rotation);var copy=motion.CopyForPrediction(copyInput,observer);
                    try
                    {
                        float difference=0;
                        for(int frame=0;frame<fps;frame++)
                        {
                            controls.Movement=copyInput.Movement=Vector2.up;
                            var actions=new TraversalActions{active=true,firePressed=frame==0,fireHeld=true,reel=frame<fps*.2f?1:0,aim=new Ray(p,new Vector3(22,65,35))};
                            controls.Actions=copyInput.Actions=actions;
                            var c=copy.Sample(1f/fps);observer.SetPose(c.cityPosition,c.cityRotation);Frame(1f/fps);
                            difference=Mathf.Max(difference,Vector3.Distance(copy.Body.Position,motion.Body.Position),Vector3.Distance(copy.Body.Velocity,motion.Body.Velocity));
                        }
                        metric($"prediction_{fps}_maxStateDifference",difference);check(difference<.001f,$"{fps}fps copied controller matches actual inputs");
                    }
                    finally{copy.Collision.Dispose();UnityEngine.Object.DestroyImmediate(observerObject);}
                }
            }
            finally{motion.Collision.Dispose();UnityEngine.Object.DestroyImmediate(wall);UnityEngine.Object.DestroyImmediate(rig);}
        }
    }
}
