using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Manhattan.Viewer
{
    /// <summary>
    /// Legacy merged Skyline bundles need primitive ownership while only part of their Core
    /// replacement is ready. Exact primitive masks handle partial replacement; a complete
    /// validated catalog cohort retires the eligible legacy hull on private mesh variants.
    /// Prepare the next cohort incrementally; publish mesh pointers and Core activation together.
    /// Imported geometry/materials, fallback triangles with no demonstrated owner, and city metres
    /// are preserved. Visibility/removal may lag while work is prepared, but coverage never does.
    /// </summary>
    public sealed class SkylineGeometryOwnership : IDisposable
    {
        private sealed class Entry
        {
            public CityTileRecord tile; public GameObject root, sourcePrefab;
            public Quaternion rootRotation;public Vector3 rootScale;
            public bool desired, committed, indexed, presented, removed;
            public bool bound; public SkylineOwnershipGroup group;
            public readonly List<CoverageSource> coverage=new List<CoverageSource>();
            public readonly List<Part> parts=new List<Part>();
            public readonly Dictionary<MeshFilter,bool[]> originalHullEligibility=new Dictionary<MeshFilter,bool[]>();
        }
        private sealed class CoverageSource
        {
            public MeshFilter filter;public Mesh mesh;public Matrix4x4 local;public MeshRenderer renderer;
            public readonly List<Material> materials=new List<Material>();
        }
        private sealed class Part
        {
            public Entry entry; public MeshFilter filter; public Mesh original, first, second, stagedMesh;
            public int[][] indices; public bool[] matchable, current, staged, stagedSubmeshes; public SkylineOwnershipPart ownership;
            public Matrix4x4 local; public bool stagedAny;
        }
        private readonly Dictionary<GameObject,Entry> entries=new Dictionary<GameObject,Entry>();
        private readonly Dictionary<string,SkylineOwnershipTile> bindings=new Dictionary<string,SkylineOwnershipTile>();
        private readonly Dictionary<string,SkylineOwnershipGroup> groups=new Dictionary<string,SkylineOwnershipGroup>();
        // The catalog's complete replacement contract is separate from primitive coincidence.
        // Legacy hull and detailed Core can use different triangulation, so a complete, bound
        // replacement cohort must retire the eligible hull even when exact ordinals miss it.
        private readonly Dictionary<string,string[]> replacementCohorts=new Dictionary<string,string[]>();
        private Func<string,string> resolveBinding=SkylineOwnershipBinding.BundleSignature;
        public int SidecarBindings => bindings.Count;
        public long DecodedMappingBytes {get;private set;}
        public double SourceValidationMilliseconds {get;private set;}
        public double MeshPreparationMilliseconds {get;private set;}
        public double MaximumTickMilliseconds {get;private set;}
        public double LastTickMilliseconds {get;private set;}
        public double MaximumSourceValidationMilliseconds {get;private set;}
        public double MaximumMeshReadMilliseconds {get;private set;}
        public double MaximumMeshCloneMilliseconds {get;private set;}
        public double MaximumIndexUploadMilliseconds {get;private set;}
        public double MaximumPublicationMilliseconds {get;private set;}
        public double MaximumRevalidationMilliseconds {get;private set;}
        public double MaximumCleanupMilliseconds {get;private set;}
        public long StagingArrayBytes {get;private set;}
        public void Configure(SkylineOwnershipData data,string sourceVersion,Func<string,string> resolver=null,CityCatalog catalog=null)
        {
            if(entries.Count!=0)throw new InvalidOperationException("Configure ownership before residency loads");
            bindings.Clear();groups.Clear();replacementCohorts.Clear();DecodedMappingBytes=0;
            resolveBinding=resolver??SkylineOwnershipBinding.BundleSignature;
            if(data==null||data.schema!=2||data.ruleVersion!="legacy-flat-hull-v4-extra-material-safe"||data.sourceVersion!=sourceVersion)return;
            try
            {
                foreach(var tile in data.tiles)bindings.Add(tile.id,tile);
                foreach(var group in data.groups){groups.Add(group.skylineId,group);foreach(var part in group.parts)foreach(var owner in part.owners)DecodedMappingBytes+=owner.ordinals.LongLength*4;}
                if(catalog!=null&&catalog.sourceVersion==sourceVersion)
                    foreach(var sky in catalog.tiles)
                    {
                        if(sky==null||sky.layer!=CityLayer.Skyline||sky.requiredReplacementCount<=0||!bindings.ContainsKey(sky.id))continue;
                        var ids=new HashSet<string>();bool valid=true;
                        foreach(var core in catalog.tiles)
                            if(core!=null&&core.layer==CityLayer.Core&&core.replaces!=null&&Array.IndexOf(core.replaces,sky.id)>=0)
                                if(!bindings.ContainsKey(core.id)||!ids.Add(core.id))valid=false;
                        if(valid&&ids.Count==sky.requiredReplacementCount){var expected=new string[ids.Count];ids.CopyTo(expected);replacementCohorts.Add(sky.id,expected);}
                    }
            }
            catch(Exception){bindings.Clear();groups.Clear();replacementCohorts.Clear();DecodedMappingBytes=0;}
        }
        private readonly List<Entry> snapshot=new List<Entry>();
        private struct MaterialContract {public bool opaque, legacyFallback;}
        private readonly Dictionary<Material,MaterialContract> publicationMaterials=new Dictionary<Material,MaterialContract>();
        private readonly Dictionary<Material,MaterialContract> preparationMaterials=new Dictionary<Material,MaterialContract>();
        private readonly Stopwatch timer=new Stopwatch();
        private IEnumerator work;
        private bool dirty, disposed;
        public bool Busy => work!=null||dirty;
        public long MaskedTriangles {get;private set;}
        public int RuntimeMeshCount {get;private set;}
        public int UnsupportedMeshes {get;private set;}
        public long Publications {get;private set;}

        public bool Register(CityTileRecord tile,GameObject root,GameObject sourcePrefab=null)
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
            var entry=new Entry {tile=tile,root=root,sourcePrefab=sourcePrefab??root,rootRotation=root.transform.rotation,rootScale=root.transform.lossyScale,presented=canPresent};
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
            // No geometry index or departing group cache survives removal.
            dirty=true;
        }
        public void Tick(float budgetMilliseconds)
        {
            LastTickMilliseconds=0;
            if(disposed||budgetMilliseconds<=0)return;
            timer.Restart();
            try
            {
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
                        foreach(var p in e.parts)ReleasePart(p);e.parts.Clear();e.bound=false;
                        e.presented=true;if(e.tile.layer==CityLayer.Core){e.committed=e.desired;if(e.root!=null)e.root.SetActive(e.committed);}
                    }
                    // Restoration is a visibility publication too: wake the streamer immediately,
                    // including late Skyline roots gated inactive during preparation.
                    MaskedTriangles=0;Publications++;bindings.Clear();groups.Clear();replacementCohorts.Clear();DecodedMappingBytes=0;snapshot.Clear();dirty=false;return;
                }
            }
            }
            finally {publicationMaterials.Clear();preparationMaterials.Clear();LastTickMilliseconds=timer.Elapsed.TotalMilliseconds;MaximumTickMilliseconds=Math.Max(MaximumTickMilliseconds,LastTickMilliseconds);}
        }
        private IEnumerator Reconcile()
        {
            // Source identity is resolved once per resident instance from built bundle hashes.
            // No Core/Sky vertex arrays or geometry signatures are read or compared here.
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null||e.indexed)continue;
                var watch=Stopwatch.StartNew();
                e.bound=ValidateSource(e);SourceValidationMilliseconds+=watch.Elapsed.TotalMilliseconds;MaximumSourceValidationMilliseconds=Math.Max(MaximumSourceValidationMilliseconds,watch.Elapsed.TotalMilliseconds);
                if(e.tile.layer==CityLayer.Skyline)groups.TryGetValue(e.tile.id,out e.group);
                e.indexed=true;yield return null;
            }
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null||e.tile.layer!=CityLayer.Skyline||!e.bound||(e.group==null&&!replacementCohorts.ContainsKey(e.tile.id))||e.parts.Count!=0)continue;
                bool needed=false;
                foreach(var core in snapshot)if(!core.removed&&core.bound&&core.tile.layer==CityLayer.Core&&(core.desired||core.committed)&&Array.IndexOf(core.tile.replaces,e.tile.id)>=0){needed=true;break;}
                if(!needed)continue;
                foreach(var filter in e.root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh=filter.sharedMesh;if(mesh==null||!Renderable(filter,e.root))continue;
                    var path=SkylineOwnershipBinding.Path(filter.transform,e.root.transform);
                    SkylineOwnershipPart map=null;if(e.group!=null)foreach(var candidate in e.group.parts)if(candidate.path==path){map=candidate;break;}
                    if(map==null){if(!replacementCohorts.ContainsKey(e.tile.id))continue;map=new SkylineOwnershipPart {path=path,owners=Array.Empty<SkylineOwnershipOwner>()};}
                    if(!ReadableTriangles(mesh)){UnsupportedMeshes++;continue;}
                    var watch=Stopwatch.StartNew();
                    var indices=new int[mesh.subMeshCount][];var matchable=new bool[mesh.subMeshCount];int total=0;
                    e.originalHullEligibility.TryGetValue(filter,out var originalEligibility);
                    for(int sub=0;sub<indices.Length;sub++){indices[sub]=mesh.GetTriangles(sub);matchable[sub]=originalEligibility!=null&&sub<originalEligibility.Length&&originalEligibility[sub]&&Opaque(filter,sub,true);total+=indices[sub].Length/3;}
                    foreach(var owner in map.owners)foreach(int ordinal in owner.ordinals)if(ordinal<0||ordinal>=total)throw new InvalidOperationException("Ownership ordinal outside original mesh");
                    e.parts.Add(new Part {entry=e,filter=filter,original=mesh,indices=indices,matchable=matchable,current=new bool[total],staged=new bool[total],stagedSubmeshes=new bool[mesh.subMeshCount],local=SkylineOwnershipBinding.LocalTransform(filter.transform,e.root.transform),ownership=map});
                    MeshPreparationMilliseconds+=watch.Elapsed.TotalMilliseconds;MaximumMeshReadMilliseconds=Math.Max(MaximumMeshReadMilliseconds,watch.Elapsed.TotalMilliseconds);
                    StagingArrayBytes+=total*2L+mesh.subMeshCount;foreach(var array in indices)StagingArrayBytes+=array.LongLength*4;yield return null;
                }
            }
            // Capture desired visibility after indexing. Later changes are queued for a fresh
            // cohort, rather than repeatedly cancelling work during continuous flight.
            var active=new HashSet<Entry>();
            foreach(var e in snapshot)if(!e.removed&&e.root!=null&&e.tile.layer==CityLayer.Core&&e.indexed&&e.desired)active.Add(e);
            var covers=new HashSet<Entry>();
            // Cache shared material reads only within one no-yield Core check. Clear before
            // every Core and retain the separate fresh atomic-publication cache.
            foreach(var core in active){preparationMaterials.Clear();if(core.bound&&CoreCovers(core,preparationMaterials))covers.Add(core);yield return null;}
            var complete=new HashSet<Entry>();
            foreach(var e in snapshot)if(!e.removed&&e.tile.layer==CityLayer.Skyline&&CompleteCohort(e,active,covers))complete.Add(e);
            foreach(var e in snapshot)if(!e.removed)foreach(var p in e.parts)Array.Clear(p.staged,0,p.staged.Length);
            foreach(var e in snapshot)
            {
                if(e.removed)continue;
                foreach(var part in e.parts)
                {
                    // A complete catalog cohort overwrites all eligible hull bits below.
                    // Exact owner ordinals add no coverage in that case; partial cohorts
                    // still use every original primitive correspondence.
                    if(!complete.Contains(e))foreach(var owner in part.ownership.owners)
                    {
                        Entry core=null;foreach(var candidate in active)if(candidate.tile.id==owner.coreId&&candidate.bound&&Array.IndexOf(candidate.tile.replaces,e.tile.id)>=0){core=candidate;break;}
                        if(core==null||!covers.Contains(core)||!SameOriginSpace(e,core))continue;
                        foreach(int ordinal in owner.ordinals)part.staged[ordinal]=true;
                        yield return null;
                    }
                    int first=0;
                    for(int sub=0;sub<part.indices.Length;sub++)
                    {
                        int count=part.indices[sub].Length/3;
                        if(!part.matchable[sub]||!Opaque(part.filter,sub,true))Array.Clear(part.staged,first,count);
                        else if(complete.Contains(e))for(int i=first;i<first+count;i++)part.staged[i]=true;
                        first+=count;
                    }
                }
            }
            long masked=0;
            foreach(var e in snapshot)
            {
                if(e.removed)continue;
                foreach(var part in e.parts)
                {
                    bool changed=false,any=false;int submesh=0,end=part.indices[0].Length/3;
                    Array.Clear(part.stagedSubmeshes,0,part.stagedSubmeshes.Length);
                    for(int i=0;i<part.staged.Length;i++)
                    {
                        while(i>=end&&submesh+1<part.indices.Length)end+=part.indices[++submesh].Length/3;
                        any|=part.staged[i];changed|=part.staged[i]!=part.current[i];
                        if(part.staged[i]){masked++;part.stagedSubmeshes[submesh]=true;}
                        if(i%256==0)yield return null;
                    }
                    part.stagedAny=any;
                    part.stagedMesh=part.filter.sharedMesh;
                    if(!changed)continue;
                    if(!any){part.stagedMesh=part.original;continue;}
                    Mesh variant;
                    if(part.filter.sharedMesh!=part.first)
                    {
                        if(part.first==null){var copyWatch=Stopwatch.StartNew();part.first=Object.Instantiate(part.original);MaximumMeshCloneMilliseconds=Math.Max(MaximumMeshCloneMilliseconds,copyWatch.Elapsed.TotalMilliseconds);RuntimeMeshCount++;yield return null;}
                        variant=part.first;
                    }
                    else
                    {
                        if(part.second==null){var copyWatch=Stopwatch.StartNew();part.second=Object.Instantiate(part.original);MaximumMeshCloneMilliseconds=Math.Max(MaximumMeshCloneMilliseconds,copyWatch.Elapsed.TotalMilliseconds);RuntimeMeshCount++;yield return null;}
                        variant=part.second;
                    }
                    variant.name=part.original.name+" (owned Skyline primitive mask)";
                    int ordinal=0;
                    for(int sub=0;sub<part.indices.Length;sub++)
                    {
                        var source=part.indices[sub];var kept=new List<int>(source.Length);
                        for(int i=0;i<source.Length;i+=3){if(!part.staged[ordinal++]){kept.Add(source[i]);kept.Add(source[i+1]);kept.Add(source[i+2]);}if(i%768==0)yield return null;}
                        var uploadWatch=Stopwatch.StartNew();variant.SetTriangles(kept,sub,false);MaximumIndexUploadMilliseconds=Math.Max(MaximumIndexUploadMilliseconds,uploadWatch.Elapsed.TotalMilliseconds);yield return null;
                    }
                    part.stagedMesh=variant;
                }
            }
            // No yield from pointer publication through Core activation. No rendered frame can
            // observe missing fallback before Core appears, or missing Core before restoration.
            // Recheck source pointers/transforms and coverage after resumable preparation.
            // Loaded shared meshes are immutable; replacing one revokes this cohort's mask.
            var publicationWatch=Stopwatch.StartNew();
            // This cache exists only within the no-yield atomic publication. Shared material
            // contracts are read once, while renderer/pointer/transform state is rechecked.
            publicationMaterials.Clear();
            foreach(var core in covers)if(!CoreCovers(core,publicationMaterials))throw new InvalidOperationException("Core coverage changed during preparation");
            foreach(var sky in complete)if(!CompleteCohort(sky,active,covers))throw new InvalidOperationException("Complete replacement cohort changed during preparation");
            foreach(var e in snapshot)if(!e.removed&&e.tile.layer==CityLayer.Skyline)foreach(var p in e.parts)
            {
                if(p.filter==null||!OriginalRootFrame(e)||!p.local.Equals(SkylineOwnershipBinding.LocalTransform(p.filter.transform,e.root.transform))||
                   (p.filter.sharedMesh!=p.original&&p.filter.sharedMesh!=p.first&&p.filter.sharedMesh!=p.second))throw new InvalidOperationException("Fallback source changed during preparation");
                // Per-submesh summaries were built with the resumable mask pass. Publication
                // checks surface coverage without rescanning every triangle or resolving paths.
                for(int sub=0;sub<p.indices.Length;sub++)
                    if(p.stagedSubmeshes[sub]&&(!p.matchable[sub]||!Opaque(p.filter,sub,true,publicationMaterials)))throw new InvalidOperationException("Fallback material changed during preparation");
                foreach(var core in covers)if(Array.IndexOf(core.tile.replaces,e.tile.id)>=0&&!SameOriginSpace(e,core))throw new InvalidOperationException("Core origin frame changed during preparation");
            }
            MaximumRevalidationMilliseconds=Math.Max(MaximumRevalidationMilliseconds,publicationWatch.Elapsed.TotalMilliseconds);
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null)continue;
                foreach(var p in e.parts){if(p.filter!=null&&p.filter.sharedMesh!=p.stagedMesh)p.filter.sharedMesh=p.stagedMesh;var old=p.current;p.current=p.staged;p.staged=old;}
                e.presented=true;
            }
            foreach(var e in snapshot)
            {
                if(e.removed||e.root==null||e.tile.layer!=CityLayer.Core)continue;
                e.committed=active.Contains(e);if(e.root.activeSelf!=e.committed)e.root.SetActive(e.committed);
                if(e.desired!=e.committed)dirty=true;
            }
            MaskedTriangles=masked;
            Publications++;
            var cleanupWatch=Stopwatch.StartNew();
            // Release decoded index buffers and native variants once a group's final owner
            // departs. Travelling to new regions cannot accumulate every visited mesh clone.
            foreach(var e in snapshot)
            {
                if(e.removed||e.tile.layer!=CityLayer.Skyline||e.parts.Count==0)continue;
                bool any=false;foreach(var part in e.parts)any|=part.stagedAny;
                if(!any){foreach(var part in e.parts)ReleasePart(part);e.parts.Clear();}
            }
            MaximumCleanupMilliseconds=Math.Max(MaximumCleanupMilliseconds,cleanupWatch.Elapsed.TotalMilliseconds);
            MaximumPublicationMilliseconds=Math.Max(MaximumPublicationMilliseconds,publicationWatch.Elapsed.TotalMilliseconds);
        }
        private static bool ReadableTriangles(Mesh mesh)
        {
            if(!mesh.isReadable)return false;
            for(int i=0;i<mesh.subMeshCount;i++)if(mesh.GetTopology(i)!=MeshTopology.Triangles)return false;
            return true;
        }
        private bool CompleteCohort(Entry sky,HashSet<Entry> active,HashSet<Entry> covers)
        {
            if(!sky.bound||!replacementCohorts.TryGetValue(sky.tile.id,out var expected)||sky.tile.requiredReplacementCount!=expected.Length)return false;
            foreach(string id in expected)
            {
                Entry found=null;
                foreach(var core in active)if(!core.removed&&core.root!=null&&core.tile.id==id){if(found!=null)return false;found=core;}
                if(found==null||!found.bound||!covers.Contains(found)||found.tile.replaces==null||Array.IndexOf(found.tile.replaces,sky.tile.id)<0||!SameOriginSpace(sky,found))return false;
            }
            return true;
        }
        private static bool Renderable(MeshFilter filter,GameObject root,MeshRenderer cachedRenderer=null)
        {
            var renderer=cachedRenderer!=null?cachedRenderer:filter.GetComponent<MeshRenderer>();if(renderer==null||!renderer.enabled||renderer.forceRenderingOff)return false;
            for(var t=filter.transform;t!=null&&t!=root.transform;t=t.parent)if(!t.gameObject.activeSelf)return false;
            return true;
        }
        private static bool Opaque(MeshFilter filter,int submesh,bool fallback=false,Dictionary<Material,MaterialContract> cache=null)
        {
            var renderer=filter.GetComponent<MeshRenderer>();
            if(renderer==null)return false;
            return Opaque(renderer,filter.sharedMesh,renderer.sharedMaterials,submesh,fallback,cache);
        }
        private static bool Opaque(MeshRenderer renderer,Mesh mesh,IList<Material> mats,int submesh,bool fallback=false,Dictionary<Material,MaterialContract> cache=null)
        {
            if(mats.Count==0)return false;var material=mats[Mathf.Min(submesh,mats.Count-1)];
            // Extra materials redraw the last submesh; their intentional overlay must survive.
            if(fallback&&submesh==mesh.subMeshCount-1&&mats.Count>mesh.subMeshCount)return false;
            if(material==null)return false;
            MaterialContract contract;
            if(cache==null||!cache.TryGetValue(material,out contract))
            {
                contract=new MaterialContract();
                contract.opaque=material.shader!=null&&material.shader.name=="Universal Render Pipeline/Lit"&&
                    material.HasProperty("_Surface")&&material.GetFloat("_Surface")==0&&
                    material.HasProperty("_AlphaClip")&&material.GetFloat("_AlphaClip")==0&&
                    !material.IsKeywordEnabled("_ALPHATEST_ON")&&!material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")&&
                    !material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")&&!material.IsKeywordEnabled("_ALPHAMODULATE_ON")&&
                    (!material.HasProperty("_ZWrite")||material.GetFloat("_ZWrite")!=0)&&
                    material.HasProperty("_Cull")&&(material.GetFloat("_Cull")==0||material.GetFloat("_Cull")==2);
                contract.legacyFallback=contract.opaque&&material.GetFloat("_Cull")==2&&LegacyHullMaterial(material,true);
                if(cache!=null)cache.Add(material,contract);
            }
            return contract.opaque&&(!fallback||contract.legacyFallback)&&
                (fallback ? renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off||renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On :
                 renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.On||renderer.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.TwoSided);
        }
        private static readonly string[] HullMaps={"_BaseMap","_BumpMap","_MetallicGlossMap","_SpecGlossMap","_OcclusionMap","_ParallaxMap","_DetailAlbedoMap","_DetailNormalMap","_EmissionMap"};
        private static bool LegacyHullMaterial(Material material,bool allowRuntimeVariant=false)
        {
            // Baker's imported flat hull only; runtime clones never establish provenance.
            if(material==null||(material.name!="Skyline"&&(!allowRuntimeVariant||material.name!="Skyline (surface off)")))return false;
            foreach(string map in HullMaps)if(material.HasProperty(map)&&material.GetTexture(map)!=null)return false;
            return true;
        }
        private bool ValidateSource(Entry entry,bool verifyContentIdentity=true)
        {
            // The legacy bake uses axis-aligned metre roots; root TRS is excluded from its
            // primitive comparison. Non-identity roots cannot safely use that correspondence.
            if(!entry.root.transform.rotation.Equals(Quaternion.identity)||!entry.root.transform.lossyScale.Equals(Vector3.one)||
               entry.sourcePrefab==null||!entry.sourcePrefab.transform.rotation.Equals(Quaternion.identity)||!entry.sourcePrefab.transform.lossyScale.Equals(Vector3.one))return false;
            if(!bindings.TryGetValue(entry.tile.id,out var expected)||expected.address!=entry.tile.address||expected.cityOrigin!=entry.tile.cityOrigin||string.IsNullOrEmpty(expected.bundleSignature)||(verifyContentIdentity&&expected.bundleSignature!=resolveBinding(entry.tile.address)))return false;
            var filters=entry.root.GetComponentsInChildren<MeshFilter>(true);int count=0;
            entry.coverage.Clear();
            foreach(var filter in filters)
            {
                var mesh=filter.sharedMesh;if(mesh==null)continue;count++;
                string path=SkylineOwnershipBinding.Path(filter.transform,entry.root.transform);SkylineOwnershipMesh expectedMesh=null;
                if(!SourceMeshMatches(entry,filter,path))return false;
                foreach(var candidate in expected.meshes)if(candidate.path==path){expectedMesh=candidate;break;}
                if(expectedMesh==null||expectedMesh.name!=mesh.name||expectedMesh.vertices!=mesh.vertexCount||expectedMesh.indexCounts.Length!=mesh.subMeshCount||!expectedMesh.localTransform.Equals(SkylineOwnershipBinding.LocalTransform(filter.transform,entry.root.transform)))return false;
                for(int sub=0;sub<mesh.subMeshCount;sub++)if(mesh.GetTopology(sub)!=UnityEngine.MeshTopology.Triangles||mesh.GetIndexCount(sub)!=(uint)expectedMesh.indexCounts[sub])return false;
                var source=new CoverageSource {filter=filter,mesh=mesh,local=expectedMesh.localTransform,renderer=filter.GetComponent<MeshRenderer>()};
                if(source.renderer!=null)source.renderer.GetSharedMaterials(source.materials);
                entry.coverage.Add(source);
            }
            return count==expected.meshes.Length;
        }
        private bool CoreCovers(Entry core,Dictionary<Material,MaterialContract> cache=null)
        {
            // Require the complete bound mesh set: a removed/null filter is loss of coverage,
            // never vacuous proof from an empty enumeration. Bundle identity is cached per
            // loaded prefab; pointer/count/path/transform checks remain cheap and repeatable.
            if(!core.bound||!OriginalRootFrame(core)||core.coverage.Count==0)return false;
            // Conservative: any changed/unsupported covering surface retains the fallback.
            foreach(var source in core.coverage)
            {
                var filter=source.filter;if(filter==null)return false;
                var mesh=filter.sharedMesh;if(mesh==null||mesh!=source.mesh)return false;
                if(!source.local.Equals(SkylineOwnershipBinding.LocalTransform(filter.transform,core.root.transform)))return false;
                // A destroyed original renderer revokes coverage, including replacement by a
                // new component. Refresh references in the retained list for every validation;
                // Surface switches/material replacement must never use a stale material array.
                if(source.renderer==null||!Renderable(filter,core.root,source.renderer))return false;
                source.renderer.GetSharedMaterials(source.materials);
                for(int sub=0;sub<mesh.subMeshCount;sub++)if(!Opaque(source.renderer,mesh,source.materials,sub,false,cache))return false;
            }
            return true;
        }
        private static bool OriginalRootFrame(Entry entry)=>entry.root.transform.rotation.Equals(entry.rootRotation)&&entry.root.transform.lossyScale.Equals(entry.rootScale);
        private static bool SameOriginSpace(Entry sky,Entry core)=>(sky.root.transform.position-sky.tile.cityOrigin-core.root.transform.position+core.tile.cityOrigin).sqrMagnitude<=0.000001f;
        private static bool SourceMeshMatches(Entry entry,MeshFilter filter,string path)
        {
            if(entry.sourcePrefab==null)return false;
            Transform source=entry.sourcePrefab.transform;
            if(path.Length>0)foreach(var piece in path.Split('/')){if(!int.TryParse(piece,out int index)||index<0||index>=source.childCount)return false;source=source.GetChild(index);}
            var sourceFilter=source.GetComponent<MeshFilter>();
            return sourceFilter!=null&&sourceFilter.sharedMesh==filter.sharedMesh&&SkylineOwnershipBinding.LocalTransform(source,entry.sourcePrefab.transform).Equals(SkylineOwnershipBinding.LocalTransform(filter.transform,entry.root.transform));
        }
        private void ReleasePart(Part p)
        {
            StagingArrayBytes-=p.current.LongLength*2+p.stagedSubmeshes.LongLength;foreach(var array in p.indices)StagingArrayBytes-=array.LongLength*4;
            if(p.filter!=null&&(p.filter.sharedMesh==p.first||p.filter.sharedMesh==p.second))p.filter.sharedMesh=p.original;
            if(p.first!=null){Object.Destroy(p.first);RuntimeMeshCount--;}
            if(p.second!=null){Object.Destroy(p.second);RuntimeMeshCount--;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;(work as IDisposable)?.Dispose();work=null;
            foreach(var e in entries.Values)foreach(var p in e.parts)ReleasePart(p);
            entries.Clear();bindings.Clear();groups.Clear();replacementCohorts.Clear();snapshot.Clear();publicationMaterials.Clear();preparationMaterials.Clear();MaskedTriangles=0;DecodedMappingBytes=0;
        }
    }
}
