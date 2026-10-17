using System.Collections.Generic;
using Manhattan.Viewer;
using UnityEngine;

namespace WebShooter.Swing
{
    // A rigidly relocated replay of real catalog footprints. This supplements
    // the controlled street matrix; it is not rendered-tile/streaming coverage.
    public static class CityChainGeometry
    {
        public static readonly Vector3 Intersection = new Vector3(1188.7766f,0,3518.1879f);
        public const float RoadYaw = 28.46516f;
        public static BuildingRecord[] Create(CityCatalog catalog,Vector3 relocated,List<GameObject> objects,List<Mesh> meshes,Dictionary<Collider,float> roofs)
        {
            var region=new CityBuildRegion{yaw=RoadYaw};
            var result=new List<BuildingRecord>();var inverse=Quaternion.Euler(0,-RoadYaw,0);
            foreach(var source in catalog.buildings)
            {
                var delta=source.cityBounds.center-Intersection;delta.y=0;
                if(!region.Intersects(source.cityBounds) || delta.sqrMagnitude>750*750 || source.footprint==null || source.footprint.Length<3)continue;
                var ring=new Vector2[source.footprint.Length];var bounds=new Bounds();
                for(int i=0;i<ring.Length;i++)
                {
                    var v=inverse*(new Vector3(source.footprint[i].x,0,source.footprint[i].y)-Intersection)+relocated;
                    ring[i]=new Vector2(v.x,v.z);
                    if(i==0)bounds=new Bounds(new Vector3(v.x,source.groundHeight,v.z),Vector3.zero);
                    bounds.Encapsulate(new Vector3(v.x,source.groundHeight,v.z));bounds.Encapsulate(new Vector3(v.x,source.height,v.z));
                }
                var b=new BuildingRecord{id=source.id,footprint=ring,groundHeight=source.groundHeight,height=source.height,cityBounds=bounds};
                result.Add(b);
                var vertices=new List<Vector3>();var triangles=new List<int>();
                for(int i=0;i<ring.Length;i++)
                {
                    var a=ring[i];var c=ring[(i+1)%ring.Length];int n=vertices.Count;
                    vertices.Add(new Vector3(a.x,source.groundHeight,a.y)-relocated);vertices.Add(new Vector3(c.x,source.groundHeight,c.y)-relocated);
                    vertices.Add(new Vector3(c.x,source.height,c.y)-relocated);vertices.Add(new Vector3(a.x,source.height,a.y)-relocated);
                    triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3,n+2,n+1,n,n+3,n+2,n});
                }
                var mesh=new Mesh{name="Catalog replay "+source.id};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();meshes.Add(mesh);
                var go=new GameObject("Catalog building "+source.id){layer=30};go.transform.position=relocated;var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;roofs.Add(collider,source.height);objects.Add(go);
            }
            return result.ToArray();
        }
        public static Vector3 Aim(BuildingRecord[] buildings,Dictionary<Collider,float> roofs,Vector3 p,Vector3 ideal,Vector3 direction,Vector3 velocity,out bool found)
        {
            float best=float.PositiveInfinity;Vector3 target=ideal;
            Vector3 travel=Vector3.ProjectOnPlane(velocity,Vector3.up).normalized;
            foreach(var b in buildings)
            {
                if(b.height<24)continue;
                Vector3 d=b.cityBounds.center-p;d.y=0;
                float bx=Mathf.Max(Mathf.Max(b.cityBounds.min.x-p.x,0),p.x-b.cityBounds.max.x);
                float bz=Mathf.Max(Mathf.Max(b.cityBounds.min.z-p.z,0),p.z-b.cityBounds.max.z);
                if(bx*bx+bz*bz>180*180)continue;
                Vector2 nearest=default;float distance=float.PositiveInfinity;
                var q=new Vector2(ideal.x,ideal.z);
                for(int i=0;i<b.footprint.Length;i++)
                {
                    var a=b.footprint[i];var e=b.footprint[(i+1)%b.footprint.Length]-a;
                    if(e.magnitude<12)continue;
                    var point=a+e*Mathf.Clamp(Vector2.Dot(q-a,e)/Mathf.Max(e.sqrMagnitude,.0001f),.25f,.75f);
                    float cost=(point-q).sqrMagnitude;if(cost<distance){distance=cost;nearest=point;}
                }
                if(float.IsPositiveInfinity(distance))continue;
                var candidate=new Vector3(nearest.x,Mathf.Min(ideal.y,b.height-12),nearest.y);
                if(!Physics.Raycast(p,(candidate-p).normalized,out var hit,Mathf.Min(250,(candidate-p).magnitude+.2f),TraversalCollision.Mask,QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(hit.normal.y)>.2f)continue;
                Consider(hit);
            }
            // Repeatable visual scan by the test pilot: broad overhead faces,
            // rather than aiming at a tiny roof-edge vertex with perfect input.
            Vector3 side=Vector3.Cross(Vector3.up,direction);
            foreach(float lateral in new[]{-120f,-90f,-60f,-40f,-25f,25f,40f,60f,90f,120f})foreach(float pitch in new[]{-45f,-25f,-10f,15f,35f,55f,75f})
            {
                var flat=Quaternion.AngleAxis(lateral,Vector3.up)*direction;
                var ray=flat*Mathf.Cos(pitch*Mathf.Deg2Rad)+Vector3.up*Mathf.Sin(pitch*Mathf.Deg2Rad);
                if(Physics.Raycast(p,ray,out var hit,250,TraversalCollision.Mask,QueryTriggerInteraction.Ignore))Consider(hit);
            }
            found=!float.IsPositiveInfinity(best);return target;
            void Consider(RaycastHit hit)
            {
                Vector3 delta=hit.point-p;
                float retention=velocity.magnitude>1?TraversalAim.RetainedSpeed(p,hit.point,velocity)/velocity.magnitude:1;
                if(delta.magnitude<velocity.magnitude*.3f || hit.point.y-delta.magnitude-1.85f<8 || (p.y<50 && delta.y<8) ||
                    (Vector3.Dot(Vector3.ProjectOnPlane(delta,Vector3.up).normalized,travel)<-.25f && retention<.8f) ||
                    Mathf.Abs(hit.normal.y)>.2f || !roofs.TryGetValue(hit.collider,out float roof) || hit.point.y>roof-8)return;
                float score=(hit.point-ideal).magnitude+delta.magnitude*.15f-
                    Vector3.Dot(Vector3.ProjectOnPlane(delta,Vector3.up).normalized,travel)*8;
                if(score<best){best=score;target=hit.point;}
            }
        }
        public static float Clearance(BuildingRecord[] buildings,Vector3 p)
        {
            float best=10000;var q=new Vector2(p.x,p.z);
            foreach(var b in buildings)
            {
                if(p.y-1.85f>b.height+.02f)continue;
                float dx=Mathf.Max(Mathf.Max(b.cityBounds.min.x-p.x,0),p.x-b.cityBounds.max.x);
                float dz=Mathf.Max(Mathf.Max(b.cityBounds.min.z-p.z,0),p.z-b.cityBounds.max.z);
                if(dx*dx+dz*dz>best*best)continue;
                bool inside=false;float distance=float.PositiveInfinity;var ring=b.footprint;
                for(int i=0,j=ring.Length-1;i<ring.Length;j=i++)
                {
                    var a=ring[j];var c=ring[i];var e=c-a;
                    if((a.y>q.y)!=(c.y>q.y) && q.x<(c.x-a.x)*(q.y-a.y)/(c.y-a.y)+a.x)inside=!inside;
                    var v=a+e*Mathf.Clamp01(Vector2.Dot(q-a,e)/Mathf.Max(e.sqrMagnitude,.0001f));distance=Mathf.Min(distance,(v-q).magnitude);
                }
                if(inside)return -distance-.3f;best=Mathf.Min(best,distance);
            }
            return best-.3f;
        }
    }
}
