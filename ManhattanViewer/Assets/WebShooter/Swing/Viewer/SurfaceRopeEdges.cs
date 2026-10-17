using System.Collections.Generic;
using UnityEngine;
namespace WebShooter.Swing
{
    // The graph uses physical sharp edges, not a tile/rotated building's AABB.
    // Mesh topology is welded and cached only while this route exists.
    internal sealed class SurfaceRopeEdges
    {
        private struct Edge { public Vector3 a,b,n,m; }
        private readonly Dictionary<Mesh,List<Edge>> cache=new Dictionary<Mesh,List<Edge>>();
        public int Candidates(Collider collider,Vector3 a,Vector3 b,Vector3 offset,Vector3[] nodes)
        {
            int count=1;var box=collider as BoxCollider;
            if(box!=null)
            {
                for(int axis=0;axis<3;axis++)for(int u=-1;u<=1;u+=2)for(int v=-1;v<=1;v+=2)
                {
                    int j=(axis+1)%3,k=(axis+2)%3;var x=box.center;var y=box.center;var n=Vector3.zero;var m=Vector3.zero;
                    x[axis]-=box.size[axis]*.5f;y[axis]+=box.size[axis]*.5f;
                    x[j]=y[j]=box.center[j]+u*box.size[j]*.5f;x[k]=y[k]=box.center[k]+v*box.size[k]*.5f;n[j]=u;m[k]=v;
                    AddEdge(box.transform.TransformPoint(x)+offset,box.transform.TransformPoint(y)+offset,
                        box.transform.TransformDirection(n),box.transform.TransformDirection(m));
                }
            }
            else if(collider is MeshCollider mesh && mesh.sharedMesh!=null && mesh.sharedMesh.isReadable)
            {
                if(!cache.TryGetValue(mesh.sharedMesh,out var edges))
                {if(cache.Count>=16)cache.Clear();cache[mesh.sharedMesh]=edges=Build(mesh.sharedMesh);}
                foreach(var edge in edges)AddEdge(mesh.transform.TransformPoint(edge.a)+offset,mesh.transform.TransformPoint(edge.b)+offset,
                    mesh.transform.TransformDirection(edge.n),mesh.transform.TransformDirection(edge.m));
            }
            return count;

            void AddEdge(Vector3 x,Vector3 y,Vector3 n,Vector3 m)
            {
                var d=y-x;if(d.sqrMagnitude<.00001f)return;
                // Closest edge point to the original span, plus its endpoints.
                var ab=b-a;float dd=d.sqrMagnitude,aa=ab.sqrMagnitude,ad=Vector3.Dot(ab,d),den=aa*dd-ad*ad;
                float t=den>.000001f?Mathf.Clamp01((aa*Vector3.Dot(a-x,d)-ad*Vector3.Dot(a-x,ab))/den):Mathf.Clamp01(Vector3.Dot((a+b)*.5f-x,d)/dd);
                var closest=x+d*t;
                for(int sign=-1;sign<=1;sign+=2)
                {
                    var shift=(n+m*sign).normalized*.015f;
                    Add(closest+shift);Add(closest-shift);
                    Add(x+shift);Add(x-shift);Add(y+shift);Add(y-shift);
                }
            }
            void Add(Vector3 point)
            {
                float score=Vector3.Distance(a,point)+Vector3.Distance(point,b);
                for(int i=1;i<count;i++)if((nodes[i]-point).sqrMagnitude<.00001f)return;
                if(count<nodes.Length-1){nodes[count++]=point;return;}
                int worst=1;float value=0;
                for(int i=1;i<count;i++){float s=Vector3.Distance(a,nodes[i])+Vector3.Distance(nodes[i],b);if(s>value){worst=i;value=s;}}
                if(score<value)nodes[worst]=point;
            }
        }
        private static List<Edge> Build(Mesh mesh)
        {
            var vertices=mesh.vertices;var triangles=mesh.triangles;var weld=new Dictionary<Vector3,int>();var ids=new int[vertices.Length];
            for(int i=0;i<vertices.Length;i++){if(!weld.TryGetValue(vertices[i],out int id)){id=weld.Count;weld[vertices[i]]=id;}ids[i]=id;}
            var all=new Dictionary<long,Edge>();
            for(int i=0;i+2<triangles.Length;i+=3)
            {
                var a=vertices[triangles[i]];var b=vertices[triangles[i+1]];var c=vertices[triangles[i+2]];
                var n=Vector3.Cross(b-a,c-a).normalized;if(n.sqrMagnitude<.5f)continue;
                for(int e=0;e<3;e++)
                {
                    int p=triangles[i+e],q=triangles[i+(e+1)%3],u=ids[p],v=ids[q];if(u==v)continue;
                    long key=((long)Mathf.Min(u,v)<<32)|(uint)Mathf.Max(u,v);
                    if(!all.TryGetValue(key,out var edge))edge=new Edge{a=vertices[p],b=vertices[q],n=n};
                    else if(Mathf.Abs(Vector3.Dot(edge.n,n))<.85f)edge.m=n;
                    all[key]=edge;
                }
            }
            var result=new List<Edge>();foreach(var edge in all.Values)if(edge.m.sqrMagnitude>.5f)result.Add(edge);return result;
        }
    }
}
