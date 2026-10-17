using System.Collections.Generic;
using UnityEngine;
namespace WebShooter.Swing
{
    // City-space route. Queries are bounded, allocation-free after construction, and run once per frame.
    public sealed class WebRopeRoute
    {
        public readonly List<Vector3> Points = new List<Vector3>(16);
        private const int NodeLimit=26;
        private readonly SurfaceRopeEdges surfaces=new SurfaceRopeEdges();
        private readonly Vector3[] nodes = new Vector3[NodeLimit];
        private readonly float[] costs = new float[NodeLimit];
        private readonly int[] previous = new int[NodeLimit], reverse = new int[NodeLimit];
        private readonly bool[] visited = new bool[NodeLimit];
        public float FixedLength { get; private set; }
        public void Clear() { Points.Clear(); FixedLength = 0; }
        public void Begin(Vector3 anchor) { Clear(); Points.Add(anchor); }
        public void CopyFrom(WebRopeRoute source){Clear();Points.AddRange(source.Points);FixedLength=source.FixedLength;}
        public static bool Blocked(Vector3 a, Vector3 b, Vector3 offset, int mask, out RaycastHit hit, float endpointMargin=.06f)
        {
            var d=b-a; float length=d.magnitude;
            hit=default;
            if(length<=.002f)return false;
            // A graph node can sit just inside a single-sided facade. Forward-only
            // queries then miss the exit face and accept a route through the solid.
            // Scope this synchronous main-thread query and always restore the flag.
            bool previous=Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces=true;
                return Physics.Raycast(a-offset,d/length,out hit,Mathf.Max(0,length-endpointMargin),mask,QueryTriggerInteraction.Ignore);
            }
            finally{Physics.queriesHitBackfaces=previous;}
        }
        public bool Update(Vector3 body, Vector3 offset, int mask)
        {
            if(Points.Count==0) return false;
            while(Points.Count>1 && !Blocked(body,Points[1],offset,mask,out _))Points.RemoveAt(0);
            var start=body;
            for(int segment=0;segment<Points.Count;segment++)
            {
                var end=Points[segment];
                if(Blocked(start,end,offset,mask,out var hit))
                {
                    if(Points.Count>=12 || !Detour(start,end,hit.collider,offset,mask,out int count)) return false;
                    if(Points.Count+count>12) return false;
                    for(int i=count-1;i>=0;i--) Points.Insert(segment++,nodes[reverse[i]]);
                }
                start=end;
            }
            FixedLength=0;
            for(int i=1;i<Points.Count;i++) FixedLength+=Vector3.Distance(Points[i-1],Points[i]);
            return true;
        }
        private bool Detour(Vector3 a,Vector3 b,Collider collider,Vector3 offset,int mask,out int count)
        {
            nodes[0]=a;int end=surfaces.Candidates(collider,a,b,offset,nodes);nodes[end]=b;
            for(int i=0;i<=end;i++){costs[i]=float.PositiveInfinity;previous[i]=-1;visited[i]=false;}
            for(int i=1;i<end;i++) visited[i]=Physics.CheckSphere(nodes[i]-offset,.004f,mask,QueryTriggerInteraction.Ignore);
            costs[0]=0; count=0;
            for(int step=0;step<=end;step++)
            {
                int current=-1;
                for(int i=0;i<=end;i++) if(!visited[i] && (current<0 || costs[i]<costs[current])) current=i;
                if(current<0 || float.IsPositiveInfinity(costs[current])) break;
                if(current==end) break;
                visited[current]=true;
                for(int next=1;next<=end;next++)
                {
                    if(visited[next] || Blocked(nodes[current],nodes[next],offset,mask,out _,next==end?.06f:.001f)) continue;
                    float cost=costs[current]+Vector3.Distance(nodes[current],nodes[next]);
                    if(cost<costs[next]){costs[next]=cost;previous[next]=current;}
                }
            }
            if(previous[end]<0) return false;
            for(int i=previous[end];i>0;i=previous[i]) reverse[count++]=i;
            return count>0;
        }
    }
}
