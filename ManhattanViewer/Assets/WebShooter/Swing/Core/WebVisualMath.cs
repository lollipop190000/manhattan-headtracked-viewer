using System.Collections.Generic;
using UnityEngine;
namespace WebShooter.Swing
{
    public static class WebVisualMath
    {
        public static float FiringAmplitude(float amplitude,float progress)=>amplitude*Mathf.Pow(1f-Mathf.Clamp01(progress),.8f);
        public static float AttachmentAmplitude(float amplitude,float age,float duration)
        { float t=Mathf.Clamp01(age/Mathf.Max(.001f,duration));return amplitude*.3f*(1f-t)*Mathf.Sin(t*Mathf.PI*2f); }
        public static float TremorAmplitude(float speed,float tension,float strength,float maximum)=>
            Mathf.Min(maximum,Mathf.Max(0,strength)*(Mathf.Max(0,speed)*.00004f+Mathf.Sqrt(Mathf.Max(0,tension))*.0002f));
        public static int Resample(IReadOnlyList<Vector3> knots,Vector3[] points,int samplesPerSegment,float amplitude,float phase,float turns)
        {
            int count=0;samplesPerSegment=Mathf.Clamp(samplesPerSegment,2,30);
            for(int k=1;k<knots.Count;k++)
            {
                Vector3 a=knots[k-1],b=knots[k],axis=(b-a).normalized;
                Vector3 side=Vector3.Cross(axis,Mathf.Abs(axis.y)>.95f?Vector3.right:Vector3.up).normalized;
                Vector3 up=Vector3.Cross(side,axis);
                for(int i=k==1?0:1;i<=samplesPerSegment && count<points.Length;i++)
                {
                    float t=i/(float)samplesPerSegment,angle=t*Mathf.PI*2*turns-phase;
                    float envelope=Mathf.Sin(t*Mathf.PI)*amplitude;
                    points[count++]=Vector3.Lerp(a,b,t)+(side*Mathf.Sin(angle)+up*Mathf.Cos(angle)*.35f)*envelope;
                }
            }
            return count;
        }
    }
}
