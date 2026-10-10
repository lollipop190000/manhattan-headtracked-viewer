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
                }).Where(owner=>owner.ordinals.Length>0).OrderBy(owner=>owner.coreId,StringComparer.Ordinal).ToArray()
            }).Where(part=>part.owners.Length>0).ToArray();
            return result;
        }

        private sealed class Entry
        {
            public CityTileRecord tile; public GameObject root;
            public bool desired, committed, indexed, presented, removed;
            public int coreGeneration=-1;
            public readonly HashSet<Slot> matches=new HashSet<Slot>();
            public readonly List<Part> parts=new List<Part>();
            public readonly Dictionary<MeshFilter,bool[]> originalHullEligibility=new Dictionary<MeshFilter,bool[]>();
        }
        private sealed class Part
        {
            public Entry entry; public MeshFilter filter; public Mesh original, first, second, stagedMesh;
            public int[][] indices; public bool[] matchable, current, staged;
        }
        private sealed class Slot {public Part part;public int ordinal;public Slot next;}
        private readonly Dictionary<GameObject,Entry> entries=new Dictionary<GameObject,Entry>();
        private readonly Dictionary<Triangle,Slot> triangles=new Dictionary<Triangle,Slot>();
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
                            var key=new Triangle(points[indices[i]],points[indices[i+1]],points[indices[i+2]]);
                            triangles.TryGetValue(key,out var previous);triangles[key]=new Slot {part=part,ordinal=ordinal++,next=previous};
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
                            var key=new Triangle(points[indices[i]],points[indices[i+1]],points[indices[i+2]]);
                            if(triangles.TryGetValue(key,out var slot))for(;slot!=null;slot=slot.next)
                                if(!slot.part.entry.removed&&Array.IndexOf(e.tile.replaces,slot.part.entry.tile.id)>=0)e.matches.Add(slot);
                            if(i%768==0)yield return null;
                        }
                    }
                }
                e.coreGeneration=skylineGeneration;e.indexed=true;yield return null;
            }
            // Capture desired visibility after indexing. Later changes are queued for a fresh
            // cohort, rather than repeatedly cancelling work during continuous flight.
            var active=new HashSet<Entry>();
            foreach(var e in snapshot)if(!e.removed&&e.root!=null&&e.tile.layer==CityLayer.Core&&e.indexed&&e.desired)active.Add(e);
            foreach(var e in snapshot)if(!e.removed)foreach(var p in e.parts)Array.Clear(p.staged,0,p.staged.Length);
            foreach(var e in active)foreach(var slot in e.matches)if(!slot.part.entry.removed)slot.part.staged[slot.ordinal]=true;
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
            entries.Clear();triangles.Clear();snapshot.Clear();MaskedTriangles=0;
        }
        private readonly struct Point : IEquatable<Point>,IComparable<Point>
        {
            private readonly int x,y,z;
            public Point(Vector3 p){if(!float.IsFinite(p.x)||!float.IsFinite(p.y)||!float.IsFinite(p.z))throw new InvalidOperationException("Non-finite city vertex");x=(int)Math.Round((double)p.x*1000);y=(int)Math.Round((double)p.y*1000);z=(int)Math.Round((double)p.z*1000);}
            public bool Equals(Point p)=>x==p.x&&y==p.y&&z==p.z;
            public int CompareTo(Point p){int c=x.CompareTo(p.x);if(c==0)c=y.CompareTo(p.y);return c!=0?c:z.CompareTo(p.z);}
            public override bool Equals(object o)=>o is Point p&&Equals(p);
            public override int GetHashCode(){unchecked{return (x*397^y)*397^z;}}
        }
        private readonly struct Triangle : IEquatable<Triangle>
        {
            private readonly Point a,b,c;
            public Triangle(Point x,Point y,Point z){if(y.CompareTo(x)<0&&y.CompareTo(z)<=0){a=y;b=z;c=x;}else if(z.CompareTo(x)<0&&z.CompareTo(y)<0){a=z;b=x;c=y;}else{a=x;b=y;c=z;}}
            public bool Equals(Triangle t)=>a.Equals(t.a)&&b.Equals(t.b)&&c.Equals(t.c);
            public override bool Equals(object o)=>o is Triangle t&&Equals(t);
            public override int GetHashCode(){unchecked{return (a.GetHashCode()*397^b.GetHashCode())*397^c.GetHashCode();}}
        }
    }
}
