using UnityEngine;
namespace WebShooter.Swing
{
    public static class TraversalAim
    {
        public static bool Select(Ray ray,Vector3 player,Vector3 velocity,float range,int mask,FlowFeatures f,out RaycastHit selected)
        {
            bool direct=Physics.Raycast(ray,out selected,range,mask,QueryTriggerInteraction.Ignore);
            float strength=f.assistAnchor?Mathf.Clamp01(f.anchorAssistStrength):0;
            if(strength==0)return direct;
            Vector3 ahead=Vector3.ProjectOnPlane(velocity.sqrMagnitude>9?velocity:ray.direction,Vector3.up).normalized;
            float Score(RaycastHit hit)
            {
                Vector3 delta=hit.point-player;
                float forward=Vector3.Dot(Vector3.ProjectOnPlane(delta,Vector3.up).normalized,ahead);
                if(delta.y<3 || forward<.25f)return float.NegativeInfinity;
                return Mathf.Clamp01(delta.y/40f)*.4f+forward*.4f-Vector3.Angle(ray.direction,delta.normalized)/90f-hit.distance*.0002f;
            }
            // A satisfactory direct aim is never replaced, even if a sampled alternative scores higher.
            if(direct && !float.IsNegativeInfinity(Score(selected)))return true;
            float best=float.NegativeInfinity;bool found=false;
            Vector3 axis=ray.direction.normalized;
            Vector3 side=Vector3.Cross(axis,Mathf.Abs(axis.y)>.95f?Vector3.right:Vector3.up).normalized;
            Vector3 up=Vector3.Cross(side,axis);float cone=Mathf.Tan(f.anchorConeDegrees*strength*Mathf.Deg2Rad);
            int samples=Mathf.Clamp(f.anchorSamples,4,24);
            for(int i=0;i<samples;i++)
            {
                float a=i*2.399963f,r=Mathf.Sqrt((i+.5f)/samples)*cone;
                Vector3 direction=(axis+(side*Mathf.Cos(a)+up*Mathf.Sin(a))*r).normalized;
                if(!Physics.Raycast(ray.origin,direction,out var hit,range,mask,QueryTriggerInteraction.Ignore))continue;
                float score=Score(hit);if(score>best){best=score;selected=hit;found=true;}
            }
            return direct||found;
        }
    }
}
