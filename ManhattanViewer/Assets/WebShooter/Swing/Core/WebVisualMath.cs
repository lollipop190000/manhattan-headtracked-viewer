using System.Collections.Generic;
using UnityEngine;
namespace WebShooter.Swing
{
    public static class WebVisualMath
    {
        public static float FiringAmplitude(float amplitude,float progress)=>amplitude*Mathf.Pow(1f-Mathf.Clamp01(progress),.8f);
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
