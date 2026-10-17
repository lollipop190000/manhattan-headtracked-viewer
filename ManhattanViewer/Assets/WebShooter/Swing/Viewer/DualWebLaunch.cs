using System;
using UnityEngine;

namespace WebShooter.Swing
{
    [Serializable]
    public sealed class DualWebLaunchSettings
    {
        public bool enabled=true, chargeFeedback=true;
        [Min(.1f)] public float fullChargeSeconds=1.35f;
        [Min(0)] public float minimumHoldSeconds=.12f;
        [Min(0)] public float minimumSpeed=24f;
        [Range(24,90)] public float maximumSpeed=82f;
        [Range(10,75)] public float elevationDegrees=40f;
        [Range(0,1)] public float viewWeight=.25f;
        [Range(8,250)] public float range=180f;
        [Range(.1f,1)] public float safetyPreviewSeconds=.65f;
        [Range(0,.004f)] public float shakeMetres=.0012f;
    }

    // Stored elastic energy, never a position constraint. Both real surface hits
    // are required; charging neither reels the body nor suspends gravity.
    public sealed class DualWebLaunch
    {
        public DualWebLaunchSettings Settings { get; set; } = new DualWebLaunchSettings();
        public bool Charging { get; private set; }
        public float Age { get; private set; }
        public float Charge => Mathf.Clamp01(Age/Mathf.Max(.1f,Settings.fullChargeSeconds));
        public bool Settled => Charging && Charge>=.9f;
        public string Status { get; private set; }="";
        public Vector3 Left { get; private set; }
        public Vector3 Right { get; private set; }
        public Vector3 LeftNormal { get; private set; }
        public Vector3 RightNormal { get; private set; }
        public Vector3 LastLaunchVelocity { get; private set; }
        public int Launches { get; private set; }
        private Collider leftCollider,rightCollider;
        private Vector3 initialForward;
        public float FeedbackAmplitude => !Charging || !Settings.chargeFeedback?0:
            Settings.shakeMetres*Mathf.SmoothStep(0,1,Charge)*Mathf.Lerp(1,.12f,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.78f,.96f,Charge)));

        public bool Begin(Vector3 city,Vector3 forward,Vector3 offset,int mask)
        {
            Cancel();
            if(!Settings.enabled)return false;
            initialForward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized;
            if(initialForward.sqrMagnitude<.5f)initialForward=Vector3.forward;
            if(!Find(city-offset,initialForward,-1,mask,out var left) || !Find(city-offset,initialForward,1,mask,out var right))
            {Status="Need visible buildings on BOTH sides";return false;}
            Left=left.point+offset;Right=right.point+offset;LeftNormal=left.normal;RightNormal=right.normal;
            leftCollider=left.collider;rightCollider=right.collider;Charging=true;Status="Charging";return true;
        }
        private bool Find(Vector3 p,Vector3 forward,float side,int mask,out RaycastHit best)
        {
            best=default;float score=float.NegativeInfinity;
            foreach(float azimuth in new[]{25f,40f,55f,70f,85f})foreach(float elevation in new[]{20f,35f,50f,65f})
            {
                var horizontal=Quaternion.AngleAxis(side*azimuth,Vector3.up)*forward;
                var d=horizontal*Mathf.Cos(elevation*Mathf.Deg2Rad)+Vector3.up*Mathf.Sin(elevation*Mathf.Deg2Rad);
                if(!Physics.Raycast(p,d,out var hit,Settings.range,mask,QueryTriggerInteraction.Ignore) || hit.distance<8 || hit.point.y<p.y+3)continue;
                float value=Vector3.Dot(d,forward)*2-Mathf.Abs(elevation-Settings.elevationDegrees)*.015f-hit.distance*.001f;
                if(value>score){score=value;best=hit;}
            }
            return best.collider!=null;
        }
        public bool Tick(float dt,Vector3 city,Vector3 offset,int mask)
        {
            if(!Charging)return false;
            if(!Valid(city,offset,mask)){Cancel();Status="Web path obstructed";return false;}
            Age+=dt;Status=Settled?"Ready - release Q":"Charging";return true;
        }
        private bool Valid(Vector3 city,Vector3 offset,int mask) => leftCollider!=null && rightCollider!=null &&
            leftCollider.enabled && rightCollider.enabled && leftCollider.gameObject.activeInHierarchy && rightCollider.gameObject.activeInHierarchy &&
            !WebRopeRoute.Blocked(city,Left,offset,mask,out _) && !WebRopeRoute.Blocked(city,Right,offset,mask,out _);
        public bool Release(SwingPhysics body,Vector3 view,Vector3 offset,int mask)
        {
            if(!Charging)return false;
            bool valid=Age>=Settings.minimumHoldSeconds && Valid(body.Position,offset,mask);
            float amount=Charge;Charging=false;
            if(!valid){Status="Charge cancelled";return false;}
            var physical=(Left-body.Position).normalized+(Right-body.Position).normalized;
            var forward=Vector3.ProjectOnPlane(physical,Vector3.up).normalized;
            if(forward.sqrMagnitude<.1f)forward=initialForward;
            var sight=Vector3.ProjectOnPlane(view,Vector3.up).normalized;
            // Sight can refine a bilateral launch; it cannot reverse its physical direction.
            if(sight.sqrMagnitude>.5f && Vector3.Angle(forward,sight)<50)forward=Vector3.Slerp(forward,sight,Settings.viewWeight).normalized;
            float maximum=Mathf.Clamp(Settings.maximumSpeed,0,Mathf.Min(90,body.Settings.maxSpeed));
            float target=Mathf.Lerp(Mathf.Min(Settings.minimumSpeed,maximum),maximum,amount);
            Vector3 launch=Vector3.zero;float best=float.NegativeInfinity;
            foreach(float turn in new[]{0f,-20f,20f,-35f,35f})foreach(float extra in new[]{0f,15f,30f})
            {
                var horizontal=Quaternion.AngleAxis(turn,Vector3.up)*forward;
                float angle=Mathf.Clamp(Settings.elevationDegrees+extra,15,75)*Mathf.Deg2Rad;
                var direction=horizontal*Mathf.Cos(angle)+Vector3.up*Mathf.Sin(angle);
                var velocity=body.Velocity+direction*Mathf.Max(0,target-Vector3.Dot(body.Velocity,direction));
                // Only newly added energy is limited; existing faster inertia is preserved.
                velocity=Vector3.ClampMagnitude(velocity,Mathf.Max(body.Velocity.magnitude,maximum));
                if(!Clear(body.Position-offset,velocity,body.Settings,mask))continue;
                float score=-Mathf.Abs(turn)-extra*.5f;
                if(score>best){best=score;launch=velocity;}
            }
            if(float.IsNegativeInfinity(best)){Status="Launch blocked - choose an open direction";return false;}
            body.Launch(launch);LastLaunchVelocity=launch;Launches++;Status="Launched";return true;
        }
        private bool Clear(Vector3 p,Vector3 velocity,SwingSettings physics,int mask)
        {
            float h=Settings.safetyPreviewSeconds/12;
            for(int i=0;i<12;i++)
            {
                var next=p+velocity*h+Vector3.down*(.5f*physics.gravity*h*h);velocity+=Vector3.down*physics.gravity*h;
                var delta=next-p;
                if(Physics.CapsuleCast(p-Vector3.up*(physics.eyeHeight-TraversalCollision.Radius),p,TraversalCollision.Radius+.12f,
                    delta.normalized,out _,delta.magnitude+TraversalCollision.Skin,mask,QueryTriggerInteraction.Ignore))return false;
                p=next;
            }
            return true;
        }
        public void Cancel(){Charging=false;Age=0;Status="";leftCollider=rightCollider=null;}
    }
}
