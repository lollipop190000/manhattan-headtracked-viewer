using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Manhattan.Viewer
{
    /// <summary>
    /// Legacy merged Skyline bundles need primitive ownership while only part of their Core
    /// replacement is ready. Only coincident triangles are masked, on private mesh variants.
    /// Prepare the next cohort incrementally; publish mesh pointers and Core activation together.
    /// Imported geometry/materials, fallback triangles with no demonstrated owner, and city metres
    /// are preserved. Visibility/removal may lag while work is prepared, but coverage never does.
    /// </summary>
    public sealed class OfflineOwnershipGroupCompiler : IDisposable
    {

        // Explicit offline operation. Normal viewer runtime never instantiates this compiler.
        public SkylineOwnershipGroup ExportGroup(string id)
        {
            var sky=entries.Values.Single(e=>e.tile.id==id&&e.tile.layer==CityLayer.Skyline);
            var result=new SkylineOwnershipGroup {skylineId=id};
            result.parts=sky.parts.Select(part=>new SkylineOwnershipPart {
                path=SkylineOwnershipBinding.Path(part.filter.transform,sky.root.transform),
                owners=entries.Values.Where(e=>e.tile.layer==CityLayer.Core).Select(core=>new SkylineOwnershipOwner {
                    coreId=core.tile.id,ordinals=core.matches.Where(slot=>slot.part==part).Select(slot=>slot.ordinal).Distinct().OrderBy(i=>i).ToArray()
                }).Where(owner=>owner.ordinals.Length>0).Concat(jointMatches.Where(j=>j.slot.part==part)
                    .GroupBy(j=>string.Concat(j.providers.Select(p=>p.tile.id.Length+":"+p.tile.id)))
                    .Select(group=>new SkylineOwnershipOwner {coreId=group.First().providers[0].tile.id,
                        requiredCoreIds=group.First().providers.Select(p=>p.tile.id).ToArray(),
                        ordinals=group.Select(j=>j.slot.ordinal).Distinct().OrderBy(i=>i).ToArray()}))
                    .OrderBy(owner=>owner.coreId,StringComparer.Ordinal).ThenBy(owner=>string.Join("/",owner.requiredCoreIds),StringComparer.Ordinal).ToArray()
            }).Where(part=>part.owners.Length>0).ToArray();
            return result;
        }

        private sealed class Entry
        {
            public CityTileRecord tile; public GameObject root;
            public bool desired, committed, indexed, presented, removed;
            public int coreGeneration=-1;
            public CoplanarTriangleCoverage coverage;
            public readonly HashSet<Slot> matches=new HashSet<Slot>();
            public readonly List<Part> parts=new List<Part>();
            public readonly Dictionary<MeshFilter,bool[]> originalHullEligibility=new Dictionary<MeshFilter,bool[]>();
        }
        private sealed class Part
        {
            public Entry entry; public MeshFilter filter; public Mesh original, first, second, stagedMesh;
            public int[][] indices; public bool[] matchable, current, staged;
        }
        private sealed class Slot {public Part part;public int ordinal;public Triangle triangle;public Slot next;}
        private sealed class JointMatch {public Slot slot;public Entry[] providers;}
        private readonly List<JointMatch> jointMatches=new List<JointMatch>();
        private readonly Dictionary<GameObject,Entry> entries=new Dictionary<GameObject,Entry>();
        // Buckets discover candidates only. Raw cyclic vertex distances establish coverage;
        // independent millimetre rounding both misses close faces and accepts cube diagonals.
        private readonly Dictionary<CentroidBucket,Slot> triangles=new Dictionary<CentroidBucket,Slot>();
        private readonly List<Entry> snapshot=new List<Entry>();
        private readonly Stopwatch timer=new Stopwatch();
        private IEnumerator work;
        private bool dirty, disposed;
        private int skylineGeneration;
        public bool Busy => work!=null||dirty;
        public long MaskedTriangles {get;private set;}
        public int RuntimeMeshCount {get;private set;}
        public int UnsupportedMeshes {get;private set;}
        public long Publications {get;private set;}

        public bool Register(CityTileRecord tile,GameObject root)
        {
            bool relevant=tile!=null&&root!=null&&
                ((tile.layer==CityLayer.Core&&tile.replaces!=null&&tile.replaces.Length>0)||
                 (tile.layer==CityLayer.Skyline&&tile.requiredReplacementCount>0));
            if(!relevant||entries.ContainsKey(root)||root.GetComponentInChildren<MeshFilter>(true)==null)return false;
            bool canPresent=tile.layer==CityLayer.Skyline;
            if(canPresent)foreach(var existing in entries.Values)
                if(existing.tile.layer==CityLayer.Core&&
                   (existing.committed||existing.desired||(work!=null&&snapshot.Contains(existing)))&&
                   Array.IndexOf(existing.tile.replaces,tile.id)>=0){canPresent=false;break;}
            var entry=new Entry {tile=tile,root=root,presented=canPresent};
            if(tile.layer==CityLayer.Skyline)
            {
                // Register precedes SurfaceDetailComparison.Register. Capture the imported
                // hull contract now; a later Surface OFF clone cannot erase PBR provenance.
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh=filter.sharedMesh;var renderer=filter.GetComponent<MeshRenderer>();
                    if(mesh==null||renderer==null)continue;
                    var materials=renderer.sharedMaterials;var eligible=new bool[mesh.subMeshCount];
                    for(int sub=0;sub<eligible.Length;sub++)
                        eligible[sub]=materials.Length>0&&LegacyHullMaterial(materials[Mathf.Min(sub,materials.Length-1)])&&Opaque(filter,sub,true);
                    entry.originalHullEligibility.Add(filter,eligible);
                }
            }
            entries.Add(root,entry);
            if(!canPresent)root.SetActive(false);dirty=true;return true;
        }
        public bool Manages(GameObject root)=>root!=null&&entries.ContainsKey(root);
        public bool CanShowSkyline(GameObject root)=>!entries.TryGetValue(root,out var e)||e.presented;
        public void SetDesired(GameObject root,bool value)
        {
            if(!entries.TryGetValue(root,out var e)||e.desired==value)return;
            e.desired=value;dirty=true;
        }
        public bool CanRelease(GameObject root)
        {
            if(!entries.TryGetValue(root,out var e))return true;
            // Keep providers/instances alive until a captured cohort finishes. In particular,
            // an old active Core cannot disappear before its fallback indices are restored.
            return !e.committed && (work==null||!snapshot.Contains(e));
        }
        public void Unregister(GameObject root)
        {
            if(!entries.TryGetValue(root,out var e))return;
            e.removed=true;entries.Remove(root);
            foreach(var p in e.parts)ReleasePart(p);
            // Skyline normally remains permanently resident. If explicitly removed, rebuild
            // its index from surviving entries instead of retaining dangling ownership slots.
            if(e.tile.layer==CityLayer.Skyline){triangles.Clear();skylineGeneration++;foreach(var item in entries.Values)if(item.tile.layer==CityLayer.Skyline)item.indexed=false;}
            dirty=true;
        }
        public void Tick(float budgetMilliseconds)
        {
            if(disposed||budgetMilliseconds<=0)return;
            timer.Restart();
            while(timer.Elapsed.TotalMilliseconds<budgetMilliseconds)
            {
                if(work==null)
                {
                    if(!dirty)return;
                    dirty=false;snapshot.Clear();foreach(var e in entries.Values)snapshot.Add(e);
                    work=Reconcile();
                }
                try
                {
                    if(work.MoveNext())continue;
                    (work as IDisposable)?.Dispose();work=null;snapshot.Clear();
                }
                catch(Exception error)
                {
                    // A bad/unreadable asset must not erase the city's existing representation.
                    // Fall back to the pre-mask full meshes and ordinary visible Core policy.
                    UnityEngine.Debug.LogWarning("Skyline ownership preparation failed; preserving full geometry: "+error.Message);
                    (work as IDisposable)?.Dispose();work=null;
                    foreach(var e in entries.Values)
                    {
                        foreach(var p in e.parts){if(p.filter!=null)p.filter.sharedMesh=p.original;Array.Clear(p.current,0,p.current.Length);}
                        e.presented=true;if(e.tile.layer==CityLayer.Core){e.committed=e.desired;if(e.root!=null)e.root.SetActive(e.committed);}
                    }
                    MaskedTriangles=0;snapshot.Clear();dirty=false;return;
                }
            }
        }
        private IEnumerator Reconcile()
        {
            // Skyline is indexed first, including late/retried fallback loads. Existing Core
            // geometry then rescans against the new index before the fallback becomes visible.
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null||e.tile.layer!=CityLayer.Skyline||e.indexed)continue;
                if(e.parts.Count==0)
                {
                    foreach(var filter in e.root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mesh=filter.sharedMesh;if(mesh==null)continue;
                        if(!Renderable(filter,e.root))continue;
                        if(!ReadableTriangles(mesh)){UnsupportedMeshes++;continue;}
                        var indices=new int[mesh.subMeshCount][];var matchable=new bool[mesh.subMeshCount];int total=0;
                        e.originalHullEligibility.TryGetValue(filter,out var originalEligibility);
                        for(int sub=0;sub<indices.Length;sub++){indices[sub]=mesh.GetTriangles(sub);matchable[sub]=originalEligibility!=null&&sub<originalEligibility.Length&&originalEligibility[sub]&&Opaque(filter,sub,true);total+=indices[sub].Length/3;}
                        e.parts.Add(new Part {entry=e,filter=filter,original=mesh,indices=indices,matchable=matchable,current=new bool[total],staged=new bool[total]});yield return null;
                    }
                }
                foreach(var part in e.parts)
                {
                    var points=new Point[part.original.vertexCount];var reader=ReadPoints(e,part.filter,part.original,points);while(reader.MoveNext())yield return null;
                    int ordinal=0,steps=0;
                    for(int sub=0;sub<part.indices.Length;sub++)
                    {
                        var indices=part.indices[sub];
                        if(!part.matchable[sub]){ordinal+=indices.Length/3;continue;}
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            var triangle=new Triangle(points[indices[i]],points[indices[i+1]],points[indices[i+2]]);
                            var bucket=triangle.Bucket;
                            triangles.TryGetValue(bucket,out var previous);triangles[bucket]=new Slot {part=part,ordinal=ordinal++,triangle=triangle,next=previous};
                            if(++steps%256==0)yield return null;
                        }
                    }
                }
                e.indexed=true;skylineGeneration++;yield return null;
            }
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null||e.tile.layer!=CityLayer.Core||e.coreGeneration==skylineGeneration)continue;
                e.matches.Clear();
                var coverage = e.coverage = new CoplanarTriangleCoverage();
                foreach(var filter in e.root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh=filter.sharedMesh;if(mesh==null)continue;
                    if(!Renderable(filter,e.root))continue;
                    if(!ReadableTriangles(mesh)){UnsupportedMeshes++;continue;}
                    var points=new Point[mesh.vertexCount];var reader=ReadPoints(e,filter,mesh,points);while(reader.MoveNext())yield return null;
                    for(int sub=0;sub<mesh.subMeshCount;sub++)
                    {
                        if(!Opaque(filter,sub))continue;
                        var indices=mesh.GetTriangles(sub);yield return null;
                        for(int i=0;i<indices.Length;i+=3)
                        {
                            MatchTriangle(e,new Triangle(points[indices[i]],points[indices[i+1]],points[indices[i+2]]));
                            coverage.Add(points[indices[i]].position,points[indices[i+1]].position,points[indices[i+2]].position);
                            if(i%768==0)yield return null;
                        }
                    }
                }
                // Partial cohorts need geometric coverage even when the flat hull subdivides
                // an edge differently or uses another diagonal from its detailed provider.
                foreach(var first in triangles.Values)for(var slot=first;slot!=null;slot=slot.next)
                {
                    if(!e.matches.Contains(slot)&&!slot.part.entry.removed&&
                       Array.IndexOf(e.tile.replaces,slot.part.entry.tile.id)>=0&&
                       coverage.Covers(slot.triangle.a,slot.triangle.b,slot.triangle.c))e.matches.Add(slot);
                    yield return null;
                }
                e.coreGeneration=skylineGeneration;e.indexed=true;yield return null;
            }
            // A wall can be split between Core tiles at triangle boundaries. Record
            // conjunctions rather than treating either partial provider as sufficient.
            jointMatches.Clear();
            var individuallyOwned=new HashSet<Slot>();
            foreach(var core in snapshot)if(!core.removed)individuallyOwned.UnionWith(core.matches);
            var providersBySky=new Dictionary<Entry,List<Entry>>();
            foreach(var sky in snapshot)if(!sky.removed&&sky.tile.layer==CityLayer.Skyline)
                providersBySky[sky]=snapshot.Where(e=>!e.removed&&e.root!=null&&e.tile.layer==CityLayer.Core&&e.coverage!=null&&
                    Array.IndexOf(e.tile.replaces,sky.tile.id)>=0).OrderBy(e=>e.tile.id,StringComparer.Ordinal).ToList();
            foreach(var first in triangles.Values)for(var slot=first;slot!=null;slot=slot.next)
            {
                if(slot.part.entry.removed||individuallyOwned.Contains(slot))continue;
                var providers=new List<Entry>(providersBySky[slot.part.entry]);
                if(providers.Count<2)continue;
                bool Covered(List<Entry> entries)=>new CoplanarTriangleCoverage(entries.Select(e=>e.coverage))
                    .Covers(slot.triangle.a,slot.triangle.b,slot.triangle.c);
                if(!Covered(providers))continue;
                for(int i=providers.Count-1;i>=0&&providers.Count>1;i--)
                {
                    var candidate=new List<Entry>(providers);candidate.RemoveAt(i);
                    if(Covered(candidate))providers=candidate;
                }
                jointMatches.Add(new JointMatch {slot=slot,providers=providers.ToArray()});
                yield return null;
            }
            // Capture desired visibility after indexing. Later changes are queued for a fresh
            // cohort, rather than repeatedly cancelling work during continuous flight.
            var active=new HashSet<Entry>();
            foreach(var e in snapshot)if(!e.removed&&e.root!=null&&e.tile.layer==CityLayer.Core&&e.indexed&&e.desired)active.Add(e);
            foreach(var e in snapshot)if(!e.removed)foreach(var p in e.parts)Array.Clear(p.staged,0,p.staged.Length);
            foreach(var e in active)foreach(var slot in e.matches)if(!slot.part.entry.removed)slot.part.staged[slot.ordinal]=true;
            foreach(var joint in jointMatches)if(joint.providers.All(active.Contains))joint.slot.part.staged[joint.slot.ordinal]=true;
            long masked=0;
            foreach(var e in snapshot)
            {
                if(e.removed)continue;
                foreach(var part in e.parts)
                {
                    bool changed=false,any=false;
                    for(int i=0;i<part.staged.Length;i++){any|=part.staged[i];changed|=part.staged[i]!=part.current[i];if(part.staged[i])masked++;if(i%256==0)yield return null;}
                    part.stagedMesh=part.filter.sharedMesh;
                    if(!changed)continue;
                    if(!any){part.stagedMesh=part.original;continue;}
                    Mesh variant;
                    if(part.filter.sharedMesh!=part.first)
                    {
                        if(part.first==null){part.first=Object.Instantiate(part.original);RuntimeMeshCount++;yield return null;}
                        variant=part.first;
                    }
                    else
                    {
                        if(part.second==null){part.second=Object.Instantiate(part.original);RuntimeMeshCount++;yield return null;}
                        variant=part.second;
                    }
                    variant.name=part.original.name+" (owned Skyline primitive mask)";
                    int ordinal=0;
                    for(int sub=0;sub<part.indices.Length;sub++)
                    {
                        var source=part.indices[sub];var kept=new List<int>(source.Length);
                        for(int i=0;i<source.Length;i+=3){if(!part.staged[ordinal++]){kept.Add(source[i]);kept.Add(source[i+1]);kept.Add(source[i+2]);}if(i%768==0)yield return null;}
                        variant.SetTriangles(kept,sub,false);yield return null;
                    }
                    part.stagedMesh=variant;
                }
            }
            // No yield from pointer publication through Core activation. No rendered frame can
            // observe missing fallback before Core appears, or missing Core before restoration.
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null)continue;
                foreach(var p in e.parts){if(p.filter!=null)p.filter.sharedMesh=p.stagedMesh;var old=p.current;p.current=p.staged;p.staged=old;}
                e.presented=true;
            }
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null||e.tile.layer!=CityLayer.Core)continue;
                e.committed=active.Contains(e);e.root.SetActive(e.committed);
                if(e.desired!=e.committed)dirty=true;
            }
            MaskedTriangles=masked;
            Publications++;
        }
        private static bool ReadableTriangles(Mesh mesh)
        {
            if(!mesh.isReadable)return false;
            for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)!=MeshTopology.Triangles)return false;
            return true;
        }
        private static bool Renderable(MeshFilter filter,GameObject root)
        {
            var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null||!renderer.enabled||renderer.forceRenderingOff)return false;
            for(var t=filter.transform;t!=null&&t!=root.transform;t=t.parent)if(!t.gameObject.activeSelf)return false;
            return true;
        }
        private static bool Opaque(MeshFilter filter,int submesh,bool fallback=false)
        {
            var renderer=filter.GetComponent<MeshRenderer>();var mats=renderer.sharedMaterials;
            if(mats.Length==0)return false;var m=mats[Mathf.Min(submesh,mats.Length-1)];
            // Extra renderer materials draw again over the last submesh. Removing its indices
            // would also remove intentional overlays, even when its first material is a hull.
            if(fallback&&submesh==filter.sharedMesh.subMeshCount-1&&mats.Length>filter.sharedMesh.subMeshCount)return false;
            if(fallback&&!LegacyHullMaterial(m,true))return false;
            // An alpha/displaced/custom surface is not proof that it covers the fallback.
            // Preserve its full fallback instead of guessing from a shared vertex signature.
            return m!=null&&m.shader!=null&&m.shader.name=="Universal Render Pipeline/Lit"&&
                m.HasProperty("_Surface")&&m.GetFloat("_Surface")==0&&
                m.HasProperty("_AlphaClip")&&m.GetFloat("_AlphaClip")==0&&
                !m.IsKeywordEnabled("_ALPHATEST_ON")&&!m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")&&
                !m.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")&&!m.IsKeywordEnabled("_ALPHAMODULATE_ON")&&
                (!m.HasProperty("_ZWrite")||m.GetFloat("_ZWrite")!=0)&&
                m.HasProperty("_Cull")&&(m.GetFloat("_Cull")==0||m.GetFloat("_Cull")==2)&&
                (fallback ? renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off||renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On :
                 renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On||renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.TwoSided)&&
                // A default Core need not cover a two-sided fallback's back face/shadow.
                // Preserve those fallback indices instead of inferring pair capabilities.
                (!fallback||m.GetFloat("_Cull")==2);
        }
        private static readonly string[] HullMaps={"_BaseMap","_BumpMap","_MetallicGlossMap","_SpecGlossMap","_OcclusionMap","_ParallaxMap","_DetailAlbedoMap","_DetailNormalMap","_EmissionMap"};
        private static bool LegacyHullMaterial(Material material,bool allowRuntimeVariant=false)
        {
            // Baker's imported flat hull only; runtime clones never establish provenance.
            if(material==null||(material.name!="Skyline"&&(!allowRuntimeVariant||material.name!="Skyline (surface off)")))return false;
            foreach(string map in HullMaps)if(material.HasProperty(map)&&material.GetTexture(map)!=null)return false;
            return true;
        }
        private static IEnumerator ReadPoints(Entry entry,MeshFilter filter,Mesh mesh,Point[] result)
        {
            var vertices=mesh.vertices;
            // Compose local transforms without the floating world origin, so a rebase cannot
            // invalidate a cached metre-space signature through cancellation/float rounding.
            Matrix4x4 local=Matrix4x4.identity;var t=filter.transform;
            while(t!=null&&t!=entry.root.transform){local=Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale)*local;t=t.parent;}
            for(int i=0;i<vertices.Length;i++){result[i]=new Point(local.MultiplyPoint3x4(vertices[i])+entry.tile.cityOrigin);if(i%256==0)yield return null;}
        }
        private void MatchTriangle(Entry core,Triangle triangle)
        {
            // A vertex-wise 1mm displacement moves the centroid at most one cell on
            // each axis. Search 27 fixed neighbours, not combinations of vertex buckets.
            var bucket=triangle.Bucket;
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
            {
                if(!triangles.TryGetValue(bucket.Offset(x,y,z),out var slot))continue;
                for(;slot!=null;slot=slot.next)
                    if(!slot.part.entry.removed&&Array.IndexOf(core.tile.replaces,slot.part.entry.tile.id)>=0&&triangle.Matches(slot.triangle))
                        core.matches.Add(slot);
            }
        }
        private void ReleasePart(Part p)
        {
            if(p.filter!=null)p.filter.sharedMesh=p.original;
            if(p.first!=null){Object.Destroy(p.first);RuntimeMeshCount--;}
            if(p.second!=null){Object.Destroy(p.second);RuntimeMeshCount--;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;(work as IDisposable)?.Dispose();work=null;
            foreach(var e in entries.Values)foreach(var p in e.parts)ReleasePart(p);
            entries.Clear();triangles.Clear();jointMatches.Clear();snapshot.Clear();MaskedTriangles=0;
        }
        private const float PositionTolerance=.001f;
        private const double PositionToleranceSquared=(double)PositionTolerance*PositionTolerance;
        private readonly struct Point
        {
            public readonly Vector3 position;
            public Point(Vector3 p){if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))throw new InvalidOperationException("Non-finite city vertex");position=p;}
        }
        private readonly struct Triangle
        {
            public readonly Vector3 a,b,c;
            public Triangle(Point x,Point y,Point z){a=x.position;b=y.position;c=z.position;}
            public CentroidBucket Bucket=>new CentroidBucket(a,b,c);
            public bool Matches(Triangle other)
            {
                // Cyclic rotations retain winding. Opposite winding and degenerate
                // geometry do not prove pixel coverage, even for a two-sided Core.
                if(Vector3.Dot(Vector3.Cross(b-a,c-a),Vector3.Cross(other.b-other.a,other.c-other.a))<=0)return false;
                return (Near(a,other.a)&&Near(b,other.b)&&Near(c,other.c))||
                       (Near(a,other.b)&&Near(b,other.c)&&Near(c,other.a))||
                       (Near(a,other.c)&&Near(b,other.a)&&Near(c,other.b));
            }
            private static bool Near(Vector3 left,Vector3 right)
            {
                double x=(double)left.x-right.x,y=(double)left.y-right.y,z=(double)left.z-right.z;
                return x*x+y*y+z*z<=PositionToleranceSquared;
            }
        }
        private readonly struct CentroidBucket : IEquatable<CentroidBucket>
        {
            private readonly int x,y,z;
            public CentroidBucket(Vector3 a,Vector3 b,Vector3 c)
            {
                // Compute in double so kilometre-scale float centroid rounding cannot
                // move otherwise valid neighbours two cells apart.
                x=Axis(a.x,b.x,c.x);y=Axis(a.y,b.y,c.y);z=Axis(a.z,b.z,c.z);
            }
            private CentroidBucket(int x,int y,int z){this.x=x;this.y=y;this.z=z;}
            private static int Axis(float a,float b,float c)=>checked((int)Math.Floor(((double)a+b+c)/(3d*PositionTolerance)));
            public CentroidBucket Offset(int dx,int dy,int dz)=>new CentroidBucket(checked(x+dx),checked(y+dy),checked(z+dz));
            public bool Equals(CentroidBucket other)=>x==other.x&&y==other.y&&z==other.z;
            public override bool Equals(object value)=>value is CentroidBucket other&&Equals(other);
            public override int GetHashCode(){unchecked{return (x*397^y)*397^z;}}
        }
    }
}
