using System;
using System.Collections.Generic;
using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    // Deterministic input replay, not a trained policy or a manual feel test.
    // Every shot uses the public action path and a real collider hit. A case has
    // exactly one initial airborne pose; no pose/velocity rescue between webs.
    public static class AirChainReplay
    {
        public static IEnumerable<Vector2Int> Stages()
        {
            for(int n=2;n<=10;n++)yield return new Vector2Int(n,0);
            yield return new Vector2Int(0,1);
            yield return new Vector2Int(0,2);
            for(int n=2;n<=10;n++)yield return new Vector2Int(n,n);
        }
        [Serializable] public sealed class Result
        {
            public string route, geometry, failure="",lastCollider="";
            public int fixtureBuildings,beforeTurn,afterTurn,seed,fps,shots,attachments,releases,contacts,groundSteps,wallSteps,floorTouches,resets,preventiveReleases,preventiveRefires,guardedReelSteps,wallBrakeSteps;
            public bool passed;
            public bool assisted;
            public float roadWidth,seconds,minHeight=10000,maxHeight,finalHeight,minWallClearance=10000,meanSpeed,finalSpeed,headingDegrees,forwardMetres,rightMetres,maxAttachSeconds,tautSeconds,reelSeconds,minAttachedSeconds=10000,minTautLinkSeconds=10000,meanSampleMilliseconds,maxSampleMilliseconds,endX,endY,endZ,turnStartX,turnStartZ,turnApproachClearance;
        }
        public struct Frame
        {
            public Result run; public float time; public Vector3 position,velocity;
            public Vector3 target,anchor,normal;
            public bool attached; public int cycle; public float reel; public int contacts;
            public TraversalState state;public float ropeLength;public string collider;
            public int floorTouches,resets;
        }
        public sealed class Map : IDisposable
        {
            public readonly float HalfRoad;
            public readonly Vector3 Center = new Vector3(-20000,0,-20000);
            private readonly List<GameObject> objects=new List<GameObject>();
            private readonly List<Mesh> meshes=new List<Mesh>();
            public readonly Dictionary<Collider,float> Roofs=new Dictionary<Collider,float>();
            public readonly bool City;
            public readonly float LegDistance,ReelCeiling;
            public readonly BuildingRecord[] Records;
            public Map(float halfRoad=30f,CityCatalog city=null)
            {
                HalfRoad=halfRoad;
                City=city!=null;LegDistance=City?50:60;ReelCeiling=City?55:85;
                if(City){Records=CityChainGeometry.Create(city,Center,objects,meshes,Roofs);Physics.SyncTransforms();return;}
                var records=new List<BuildingRecord>();
                // A finite-height, street-level cross with four solid blocks.
                // Long roads are needed for twenty separate airborne attachments.
                foreach(int x in new[]{-1,1}) foreach(int z in new[]{-1,1})
                {
                    var bounds=new Bounds(Center+new Vector3(x*(1000+HalfRoad),100,z*(1000+HalfRoad)),new Vector3(2000,200,2000));
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=$"Chain block {x}/{z}";go.layer=30;
                    go.transform.position=bounds.center;go.transform.localScale=bounds.size;objects.Add(go);
                    records.Add(new BuildingRecord { id=go.name,height=200,groundHeight=0,cityBounds=bounds,
                        footprint=new[]{new Vector2(bounds.min.x,bounds.min.z),new Vector2(bounds.max.x,bounds.min.z),
                            new Vector2(bounds.max.x,bounds.max.z),new Vector2(bounds.min.x,bounds.max.z)} });
                }
                Records=records.ToArray();Physics.SyncTransforms();
            }
            public void Dispose(){foreach(var go in objects)UnityEngine.Object.DestroyImmediate(go);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);}
        }
        public static Result Run(Map map,int before,int after,int seed,int fps,Action<Frame> observe=null,bool assisted=true)
        {
            bool turn=after>0;
            var result=new Result{route=turn?"right":"straight",beforeTurn=before,afterTurn=after,seed=seed,fps=fps,assisted=assisted,roadWidth=map.HalfRoad*2};
            result.fixtureBuildings=map.Records.Length;
            result.geometry=map.City?"Manhattan development footprints":"controlled cross";
            var random=new System.Random(seed);
            var rig=new GameObject("Air chain replay input");
            var flight=rig.AddComponent<FreeFlightController>();flight.enabled=false;
            var controls=new BufferedTraversalInput();controls.Actions=new TraversalActions{active=true};
            var settings=TraversalTuning.Defaults();
            settings.chain.enabled=assisted;
            var motion=new WebSwingMotionInput(controls,flight,null,settings){AutomaticCruiseAssist=true};
            motion.ReconnectAssist.enabled=true;
            motion.Collision.CitySolids=new CitySolidRecovery(map.Records,motion.Body);
            float startZ=turn?-before*map.LegDistance-45f:-(10*map.LegDistance+45);
            Vector3 start=map.Center+new Vector3(0,45,startZ);
            flight.SetPose(start,Quaternion.identity);motion.Activate();motion.Body.Velocity=Vector3.forward*(36f+seed%5);
            float attachedAt=-1,linkTaut=0;
            motion.WebAttached+=_=>{attachedAt=motion.SimulatedTime;linkTaut=0;result.attachments++;result.maxAttachSeconds=Mathf.Max(result.maxAttachSeconds,motion.ShotAge);};
            motion.WebReleased+=()=>{result.releases++;if(attachedAt>=0){result.minAttachedSeconds=Mathf.Min(result.minAttachedSeconds,motion.SimulatedTime-attachedAt);result.minTautLinkSeconds=Mathf.Min(result.minTautLinkSeconds,linkTaut);attachedAt=-1;}};
            float speedSum=0;int steps=0;double sampleTotal=0;int samples=0;
            motion.PhysicsStepped+=sample=>
            {
                steps++;speedSum+=sample.velocity.magnitude;
                result.minHeight=Mathf.Min(result.minHeight,sample.position.y-settings.eyeHeight);
                result.maxHeight=Mathf.Max(result.maxHeight,sample.position.y-settings.eyeHeight);
                if(sample.collided)result.contacts++;
                if(sample.state==TraversalState.Grounded)result.groundSteps++;
                if(sample.state==TraversalState.WallRunning)result.wallSteps++;
                if(motion.Body.Attached && Vector3.Distance(sample.position,motion.Body.Anchor)>=motion.Body.RopeLength-.05f){result.tautSeconds+=sample.dt;linkTaut+=sample.dt;}
                if(motion.Body.Attached && motion.ReelInput>0)result.reelSeconds+=sample.dt;
            };
            float dt=1f/fps;
            try
            {
                int total=turn?before+after:before;
                for(int cycle=0;cycle<total;cycle++)
                {
                    bool outgoing=turn && cycle>=before;
                    Vector3 direction=outgoing?Vector3.right:Vector3.forward;
                    Vector3 right=Vector3.Cross(Vector3.up,direction);
                    float side=outgoing?((cycle-before)%2==0?-1:1):(cycle%2==0?1:-1);
                    Vector3 p=motion.Body.Position;
                    if(outgoing && cycle==before)
                    {
                        result.turnStartX=p.x-map.Center.x;result.turnStartZ=p.z-map.Center.z;
                        if(map.City)result.turnApproachClearance=CityChainGeometry.Clearance(map.Records,new Vector3(p.x,settings.eyeHeight+.02f,p.z));
                    }
                    Vector3 target=p+direction*35+right*(map.HalfRoad*side-Vector3.Dot(p-map.Center,right));
                    target.y=Mathf.Min(190,p.y+65);
                    if(outgoing)target.z=map.Center.z+map.HalfRoad*side;
                    else target.x=map.Center.x+map.HalfRoad*side;
                    if(map.City)
                    {
                        bool ready=false;
                        for(int wait=0;wait<=fps;wait++)
                        {
                            target=CityChainGeometry.Aim(map.Records,map.Roofs,motion.Body.Position,target,direction,motion.Body.Velocity,out ready);
                            if(ready)break;
                            SetPilot(direction,right);controls.Actions=new TraversalActions{active=true};Advance(cycle);if(Failed())return Finish();
                        }
                        if(!ready){result.failure="no safe visible anchor";return Finish();}
                    }
                    float error=((float)random.NextDouble()*2-1)*3f;
                    float timingError=((float)random.NextDouble()*2-1)*.08f;
                    float coastSeconds=.16f+((float)random.NextDouble()*2-1)*.04f;
                    int frames=Mathf.CeilToInt(2.5f*fps);
                    float attachmentTime=-1;
                    for(int frame=0;frame<frames;frame++)
                    {
                        p=motion.Body.Position;
                        // Normal movement feedback keeps the lane centre; it is
                        // fed through WASD, never by directly changing velocity.
                        SetPilot(direction,right);
                        if(attachmentTime<0 && motion.Body.Attached)attachmentTime=frame/(float)fps;
                        controls.Actions=new TraversalActions{active=true,firePressed=frame==0,fireHeld=true,
                            reel=attachmentTime<0 || frame/(float)fps-attachmentTime<.12f ||
                                (frame/(float)fps-attachmentTime<.45f && p.y<map.ReelCeiling)?1:0,
                            aim=new Ray(p,Quaternion.AngleAxis(error,Vector3.up)*(target-p)),viewportX=side>0?.75f:.25f};
                        if(frame==0)result.shots++;
                        Advance(cycle);
                        if(Failed())return Finish();
                        float goal=outgoing?(cycle-before+1)*map.LegDistance:(cycle+1)*map.LegDistance;
                        float progress=outgoing?motion.Body.Position.x-map.Center.x:motion.Body.Position.z-start.z;
                        if(map.City && attachmentTime>=0 && frame/(float)fps-attachmentTime>=.32f && Risky(.45f))
                        {result.preventiveReleases++;break;}
                        if(map.City && attachmentTime>=0 && frame/(float)fps-attachmentTime>=.35f &&
                            (!outgoing || Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(motion.Body.Velocity.x,motion.Body.Velocity.z)*Mathf.Rad2Deg,90))<25) &&
                            (motion.Body.Velocity.y>3 || frame/(float)fps-attachmentTime>.95f))break;
                        if(attachmentTime>=0 && frame/(float)fps-attachmentTime>(map.City ? .32f : .4f) &&
                            progress>=goal-Vector3.Dot(motion.Body.Velocity,direction)*(coastSeconds-timingError))break;
                    }
                    // Distinct mouse-up and airborne coast between every shot.
                    int coast=Mathf.CeilToInt((map.City?2.5f:coastSeconds)*fps);
                    for(int frame=0;frame<coast;frame++)
                    {
                        SetPilot(direction,right);
                        controls.Actions=new TraversalActions{active=true};Advance(cycle);
                        if(Failed())return Finish();
                        float goal=outgoing?(cycle-before+1)*map.LegDistance:(cycle+1)*map.LegDistance;
                        float progress=outgoing?motion.Body.Position.x-map.Center.x:motion.Body.Position.z-start.z;
                        if(map.City && frame/(float)fps>=coastSeconds && progress>=goal)break;
                        if(map.City && frame/(float)fps>=coastSeconds && Risky(.65f)){result.preventiveRefires++;break;}
                    }
                    if(result.attachments<cycle+1){result.failure="shot did not attach";return Finish();}
                }
                // Do not count merely firing the last web: verify its release
                // and sustained collision-free airborne travel afterward too.
                for(int frame=0;frame<Mathf.CeilToInt((map.City?1.2f:.35f)*fps);frame++)
                {
                    var direction=turn?Vector3.right:Vector3.forward;
                    SetPilot(direction,Vector3.Cross(Vector3.up,direction));controls.Actions=new TraversalActions{active=true};Advance(total);if(Failed())return Finish();
                    if(frame/(float)fps>=.35f && (!turn || Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(motion.Body.Velocity.x,motion.Body.Velocity.z)*Mathf.Rad2Deg,90))<=15))break;
                }
                if(turn && Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(motion.Body.Velocity.x,motion.Body.Velocity.z)*Mathf.Rad2Deg,90))>15)result.failure="right turn incomplete";
                if(result.attachments!=total)result.failure="attachment count mismatch";
                if(result.minAttachedSeconds<.3f)result.failure="insufficient attached swing duration";
                if(result.minTautLinkSeconds<.05f)result.failure="link never became taut long enough";
                result.passed=result.failure.Length==0;return Finish();
            }
            finally{motion.Collision.Dispose();UnityEngine.Object.DestroyImmediate(rig);}

            Vector3 Desired(Vector3 direction,Vector3 right,Vector3 position)
            {
                float lateral=Vector3.Dot(position-map.Center,right);
                float correction=lateral*.035f;
                if(map.City)correction=Mathf.Clamp(correction,-.5f,.5f);
                return (direction-right*correction).normalized;
            }
            void SetPilot(Vector3 direction,Vector3 right)
            {
                float yaw=flight.transform.eulerAngles.y;
                float targetYaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
                float rate=Mathf.Clamp(Mathf.DeltaAngle(yaw,targetYaw)*5,-120,120);
                controls.LookRate=new Vector2(rate,0);
                // Sample applies this frame's look before translating local axes.
                // Stop stale look rates persisting through the released coast.
                var heading=Quaternion.Euler(0,yaw+rate*dt,0);
                var local=Quaternion.Inverse(heading)*Desired(direction,right,motion.Body.Position);
                controls.Movement=new Vector2(local.x,Mathf.Max(0,local.z));
            }
            bool Risky(float seconds)
            {
                var p=motion.Body.Position;var v=motion.Body.Velocity;
                if(Physics.CheckCapsule(p-Vector3.up*(settings.eyeHeight-.3f),p,1f,TraversalCollision.Mask,QueryTriggerInteraction.Ignore))return true;
                float h=seconds/8;
                for(int i=0;i<8;i++)
                {
                    v+=Vector3.down*settings.gravity*h;var next=p+v*h;
                    if(next.y<settings.eyeHeight+5)return true;
                    if(motion.Body.Attached)
                    {
                        var delta=next-motion.Body.Anchor;
                        if(delta.magnitude>motion.Body.RopeLength)
                        {
                            var n=delta.normalized;next=motion.Body.Anchor+n*motion.Body.RopeLength;
                            v-=n*Mathf.Max(0,Vector3.Dot(v,n));
                        }
                    }
                    var travel=next-p;
                    if(travel.sqrMagnitude>.00001f && Physics.CapsuleCast(p-Vector3.up*(settings.eyeHeight-.3f),p,.6f,
                        travel.normalized,out _,travel.magnitude,TraversalCollision.Mask,QueryTriggerInteraction.Ignore))return true;
                    if(CityChainGeometry.Clearance(map.Records,next)<.5f)return true;
                    p=next;
                }
                return false;
            }
            void Advance(int cycle)
            {
                var c=motion.Sample(dt);samples++;sampleTotal+=motion.LastSampleMilliseconds;result.maxSampleMilliseconds=Mathf.Max(result.maxSampleMilliseconds,(float)motion.LastSampleMilliseconds);flight.SetPose(c.cityPosition,c.cityRotation);result.seconds+=dt;
                Vector3 p=motion.Body.Position;
                float clearance=Mathf.Max(map.HalfRoad-Mathf.Abs(p.x-map.Center.x),map.HalfRoad-Mathf.Abs(p.z-map.Center.z))-.3f;
                if(map.City)clearance=CityChainGeometry.Clearance(map.Records,p);
                result.minWallClearance=Mathf.Min(result.minWallClearance,clearance);
                observe?.Invoke(new Frame{run=result,time=result.seconds,position=p,velocity=motion.Body.Velocity,
                    attached=motion.Body.Attached,cycle=cycle,reel=motion.ReelInput,contacts=result.contacts,target=motion.ShotTarget,anchor=motion.Body.Anchor,
                    state=motion.Body.State,ropeLength=motion.Body.RopeLength,collider=motion.Body.LastCollisionName,normal=motion.Body.ContactNormal,
                    floorTouches=motion.Body.FloorContactSequence,resets=motion.Body.ResetSequence});
            }
            bool Failed()
            {
                if(motion.Body.ResetSequence!=1){result.failure="unexpected physics reset";return true;}
                if(result.contacts>0 || motion.Body.FloorContactSequence>0 || result.groundSteps>0 || result.wallSteps>0 || result.minHeight<=.05f)
                {result.failure="ground or wall contact";return true;}
                if(!map.City && motion.Body.Position.y>195){result.failure="over roof, street test invalid";return true;}
                if(map.City && result.minWallClearance<0){result.failure="left street over footprint";return true;}
                return false;
            }
            Result Finish()
            {
                result.floorTouches=motion.Body.FloorContactSequence;result.resets=motion.Body.ResetSequence;
                result.guardedReelSteps=motion.Body.ReelGuardSequence;
                result.wallBrakeSteps=motion.WallApproachBrakeSteps;
                var end=motion.Body.Position-map.Center;result.endX=end.x;result.endY=end.y;result.endZ=end.z;
                result.lastCollider=motion.Body.LastCollisionName;
                result.meanSampleMilliseconds=samples>0?(float)(sampleTotal/samples):0;
                result.meanSpeed=steps>0?speedSum/steps:0;result.finalSpeed=motion.Body.Velocity.magnitude;
                result.headingDegrees=Mathf.Atan2(motion.Body.Velocity.x,motion.Body.Velocity.z)*Mathf.Rad2Deg;
                result.forwardMetres=motion.Body.Position.z-start.z;result.rightMetres=motion.Body.Position.x-map.Center.x;
                result.finalHeight=motion.Body.Position.y-settings.eyeHeight;
                return result;
            }
        }
    }
}
