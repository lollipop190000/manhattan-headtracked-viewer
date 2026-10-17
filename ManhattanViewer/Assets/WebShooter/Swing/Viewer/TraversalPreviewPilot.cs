using System;
using System.Collections.Generic;
using Manhattan.Viewer;
using UnityEngine;
namespace WebShooter.Swing
{
    // Opt-in fixture pilot only. It rolls out copies of the shipping controller
    // against the same static colliders; it never assigns the live pose/velocity.
    public sealed class TraversalPreviewPilot : IDisposable
    {
        private readonly GameObject rig=new GameObject("Traversal prediction observer");
        private readonly FreeFlightController observer;
        private readonly List<Vector3> candidates=new List<Vector3>(64);
        public TraversalPreviewPilot(){observer=rig.AddComponent<FreeFlightController>();observer.enabled=false;}
        public bool Select(WebSwingMotionInput live,Quaternion rotation,Vector3 direction,Vector3 lane,Vector3 ideal,float error,int fps,
            out Vector3 target,out float reelSeconds)
        {
            target=ideal;reelSeconds=.12f;candidates.Clear();candidates.Add(ideal);
            var p=live.Body.Position;
            foreach(float yaw in new[]{-100f,-65f,-40f,-20f,20f,40f,65f,100f})foreach(float pitch in new[]{-45f,-20f,10f,30f,50f,70f})
            {
                var flat=Quaternion.AngleAxis(yaw,Vector3.up)*direction;
                var ray=flat*Mathf.Cos(pitch*Mathf.Deg2Rad)+Vector3.up*Mathf.Sin(pitch*Mathf.Deg2Rad);
                if(Physics.Raycast(p,ray,out var hit,250,TraversalCollision.Mask,QueryTriggerInteraction.Ignore) &&
                    hit.distance>Mathf.Max(8,live.Body.Velocity.magnitude*.3f) && hit.point.y-hit.distance>8)candidates.Add(hit.point);
            }
            candidates.Sort((a,b)=>Vector3.Distance(a,ideal).CompareTo(Vector3.Distance(b,ideal)));
            float best=float.NegativeInfinity;
            foreach(var candidate in candidates)foreach(float reel in new[]{.12f,.45f})
            {
                if(!Safe(live,rotation,direction,lane,candidate,error,fps,reel,out float score) || score<=best)continue;
                target=candidate;reelSeconds=reel;best=score;
            }
            return !float.IsNegativeInfinity(best);
        }
        public bool CanHold(WebSwingMotionInput live,Quaternion rotation,Vector3 direction,Vector3 lane,Vector3 target,float error,int fps)
            => Safe(live,rotation,direction,lane,target,error,fps,.12f,out _);
        private bool Safe(WebSwingMotionInput live,Quaternion rotation,Vector3 direction,Vector3 lane,Vector3 target,float error,int fps,float reelSeconds,out float score)
        {
            score=float.NegativeInfinity;
            observer.SetPose(live.Body.Position,rotation);var controls=new BufferedTraversalInput();
            var copy=live.CopyForPrediction(controls,observer);int contacts=copy.Body.ContactSequence,floors=copy.Body.FloorContactSequence,resets=copy.Body.ResetSequence;
            float attachedAt=-1,duration=0,taut=0;int attachments=0;
            copy.WebAttached+=_=>{attachments++;attachedAt=copy.SimulatedTime;};
            copy.WebReleased+=()=>{if(attachedAt>=0){duration=copy.SimulatedTime-attachedAt;attachedAt=-1;}};
            float dt=1f/fps;
            try
            {
                int hold=Mathf.CeilToInt(.5f*fps),coast=Mathf.CeilToInt(.2f*fps);
                for(int frame=0;frame<hold+coast;frame++)
                {
                    SetPilot(copy,controls,observer,direction,lane,dt);
                    bool held=frame<hold;
                    controls.Actions=new TraversalActions{active=true,firePressed=frame==0,fireHeld=held,
                        reel=held && frame*dt<reelSeconds+.05f?1:0,
                        aim=new Ray(copy.Body.Position,Quaternion.AngleAxis(error,Vector3.up)*(target-copy.Body.Position))};
                    var c=copy.Sample(dt);observer.SetPose(c.cityPosition,c.cityRotation);
                    if(copy.Body.ContactSequence!=contacts || copy.Body.FloorContactSequence!=floors || copy.Body.ResetSequence!=resets ||
                        copy.Body.Grounded || copy.Body.State==TraversalState.WallRunning)return false;
                    if(copy.Body.Attached && Vector3.Distance(copy.Body.Position,copy.Body.Anchor)>=copy.Body.RopeLength-.05f)taut+=dt;
                }
                var right=Vector3.Cross(Vector3.up,direction);
                score=Vector3.Dot(copy.Body.Position-live.Body.Position,direction)+Vector3.Dot(copy.Body.Velocity,direction)*.25f-
                    Mathf.Abs(Vector3.Dot(copy.Body.Position-lane,right))*.2f-Mathf.Max(0,copy.Body.Position.y-150)*.05f;
                return attachments==1 && duration>=.3f && taut>=.05f;
            }
            finally{copy.Collision.Dispose();}
        }
        public static void SetPilot(WebSwingMotionInput motion,BufferedTraversalInput controls,FreeFlightController flight,Vector3 direction,Vector3 lane,float dt)
        {
            var right=Vector3.Cross(Vector3.up,direction);float lateral=Vector3.Dot(motion.Body.Position-lane,right);
            var desired=(direction-right*Mathf.Clamp(lateral*.035f,-.5f,.5f)).normalized;
            float yaw=flight.transform.eulerAngles.y,targetYaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            float rate=Mathf.Clamp(Mathf.DeltaAngle(yaw,targetYaw)*5,-120,120);controls.LookRate=new Vector2(rate,0);
            var local=Quaternion.Inverse(Quaternion.Euler(0,yaw+rate*dt,0))*desired;controls.Movement=new Vector2(local.x,Mathf.Max(0,local.z));
        }
        public void Dispose(){UnityEngine.Object.DestroyImmediate(rig);}
    }
}
