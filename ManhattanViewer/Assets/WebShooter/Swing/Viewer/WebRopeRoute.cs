using System.Collections.Generic;
using UnityEngine;
namespace WebShooter.Swing
{
    // City-space route. Queries are bounded, allocation-free after construction, and run once per frame.
    public sealed class WebRopeRoute
    {
        public readonly List<Vector3> Points = new List<Vector3>(16);
        private readonly Vector3[] nodes = new Vector3[10];
        private readonly float[] costs = new float[10];
        private readonly int[] previous = new int[10], reverse = new int[10];
        private readonly bool[] visited = new bool[10];
        public float FixedLength { get; private set; }
        public void Clear() { Points.Clear(); FixedLength = 0; }
        public void Begin(Vector3 anchor) { Clear(); Points.Add(anchor); }
        public static bool Blocked(Vector3 a, Vector3 b, Vector3 offset, int mask, out RaycastHit hit)
        {
            var d=b-a; float length=d.magnitude;
            hit=default;
            return length>.08f && Physics.Raycast(a-offset, d/length, out hit, length-.06f, mask, QueryTriggerInteraction.Ignore);
        }
        public bool Update(Vector3 body, Vector3 offset, int mask)
        {
            if(Points.Count==0) return false;
            while(Points.Count>1 && !Blocked(body,Points[1],offset,mask,out _)) Points.RemoveAt(0);
            var start=body;
            for(int segment=0;segment<Points.Count;segment++)
            {
                var end=Points[segment];
                if(Blocked(start,end,offset,mask,out var hit))
                {
                    if(Points.Count>=12 || !Detour(start,end,hit.collider.bounds,offset,mask,out int count)) return false;
                    if(Points.Count+count>12) return false;
                    for(int i=count-1;i>=0;i--) Points.Insert(segment++,nodes[reverse[i]]);
                }
                start=end;
            }
            FixedLength=0;
            for(int i=1;i<Points.Count;i++) FixedLength+=Vector3.Distance(Points[i-1],Points[i]);
            return true;
        }
        private bool Detour(Vector3 a,Vector3 b,Bounds bounds,Vector3 offset,int mask,out int count)
        {
            // Conservative bounds support rotated building proxies too. Never route through a solid.
            bounds.Expand(.24f); nodes[0]=a; nodes[9]=b;
            for(int i=0;i<8;i++) nodes[i+1]=bounds.center+offset+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            for(int i=0;i<10;i++){costs[i]=float.PositiveInfinity;previous[i]=-1;visited[i]=false;}
            costs[0]=0; count=0;
            for(int step=0;step<10;step++)
            {
                int current=-1;
                for(int i=0;i<10;i++) if(!visited[i] && (current<0 || costs[i]<costs[current])) current=i;
                if(current<0 || float.IsPositiveInfinity(costs[current])) break;
                if(current==9) break;
                visited[current]=true;
                for(int next=1;next<10;next++)
                {
                    if(visited[next] || Blocked(nodes[current],nodes[next],offset,mask,out _)) continue;
                    float cost=costs[current]+Vector3.Distance(nodes[current],nodes[next]);
                    if(cost<costs[next]){costs[next]=cost;previous[next]=current;}
                }
            }
            if(previous[9]<0) return false;
            for(int i=previous[9];i>0;i=previous[i]) reverse[count++]=i;
            return count>0;
        }
    }
}
