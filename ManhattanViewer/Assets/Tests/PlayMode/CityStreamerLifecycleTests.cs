using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class CityStreamerLifecycleTests
{
    // A real Addressables provider/locator keeps the production reference-counted handle path
    // under test while avoiding disk, bundles and large city assets.
    private sealed class TileProvider : ResourceProviderBase
    {
        private readonly string id = "CityStreamerLifecycle-" + Guid.NewGuid().ToString("N");
        private readonly List<ProvideHandle> pending = new List<ProvideHandle>();
        private readonly List<GameObject> assets = new List<GameObject>();
        public int Provides, Releases;
        public bool DeferCompletions;
        public bool RenderAssets, FailLoads;
        public Func<string,GameObject> AssetFactory;
        public readonly HashSet<string> FailAddresses = new HashSet<string>();
        public int Pending => pending.Count;
        public readonly List<int> ReleaseFrames = new List<int>();
        public readonly Dictionary<string, int> ReleasesPerAddress = new Dictionary<string, int>();
        public override string ProviderId => id;
        public override Type GetDefaultType(IResourceLocation location) => typeof(GameObject);
        public override void Provide(ProvideHandle handle)
        {
            Provides++;
            if (DeferCompletions) pending.Add(handle);
            else Complete(handle);
        }
        private void Complete(ProvideHandle handle)
        {
            if (FailLoads || FailAddresses.Contains(handle.Location.PrimaryKey)) { handle.Complete<GameObject>(null, false, new Exception("Synthetic tile failure")); return; }
            var asset = AssetFactory != null ? AssetFactory(handle.Location.PrimaryKey) : RenderAssets ? GameObject.CreatePrimitive(PrimitiveType.Cube) : new GameObject();
            asset.name = "provided " + handle.Location.PrimaryKey;
            asset.SetActive(false); assets.Add(asset);
            handle.Complete(asset, true, null);
        }
        public void CompletePending()
        {
            foreach (var handle in pending) Complete(handle);
            pending.Clear();
        }
        public override void Release(IResourceLocation location, object asset)
        {
            Releases++; ReleaseFrames.Add(Time.frameCount);
            ReleasesPerAddress.TryGetValue(location.PrimaryKey, out int released);
            ReleasesPerAddress[location.PrimaryKey] = released + 1;
            Object.Destroy(asset as GameObject);
        }
        public void Cleanup()
        {
            foreach (var asset in assets) if (asset != null) Object.Destroy(asset);
            assets.Clear();
        }
    }

    private TileProvider provider;
    private ResourceLocationMap locator;
    private GameObject host, observer;
    private CityCatalog catalog;
    private CityStreamer streamer;
    private FreeFlightController flight;
    private readonly List<Object> ownershipAssets = new List<Object>();
    private static readonly MethodInfo UpdateMethod = typeof(CityStreamer).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo EvaluateMethod = typeof(CityStreamer).GetMethod("Evaluate", BindingFlags.Instance | BindingFlags.NonPublic);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        var initialization = Addressables.InitializeAsync(false);
        yield return initialization;
        try { Assert.That(initialization.Status, Is.EqualTo(AsyncOperationStatus.Succeeded)); }
        finally { Addressables.Release(initialization); }
        provider = new TileProvider();
        locator = new ResourceLocationMap(provider.ProviderId);
        Addressables.ResourceManager.ResourceProviders.Add(provider);
        Addressables.AddResourceLocator(locator);
    }

    private void CreateStreamer(int count, CityLayer layer = CityLayer.Core, bool withOrigin = false)
    {
        observer = new GameObject("streaming lifecycle observer");
        flight = observer.AddComponent<FreeFlightController>(); flight.enabled = false;
        catalog = ScriptableObject.CreateInstance<CityCatalog>();
        catalog.tiles = new CityTileRecord[count];
        for (int i = 0; i < count; i++)
        {
            string address = provider.ProviderId + "-tile-" + i;
            locator.Add(address, new ResourceLocationBase(address, address, provider.ProviderId, typeof(GameObject)));
            catalog.tiles[i] = new CityTileRecord
            {
                id = address, address = address, layer = layer, residentBytes = 1024,
                cityBounds = new Bounds(new Vector3(i * 20, 0, 0), new Vector3(10, 20, 10)),
                cityOrigin = new Vector3(i * 20, 0, 0)
            };
        }
        host = new GameObject("streaming lifecycle host");
        streamer = host.AddComponent<CityStreamer>(); streamer.enabled = false;
        streamer.catalog = catalog; streamer.flight = flight; streamer.workBudgetMilliseconds = 1000;
        if (withOrigin)
        {
            var floating = host.AddComponent<FloatingOrigin>(); floating.observer = observer.transform;
            streamer.origin = floating; flight.Origin = floating;
        }
        streamer.Initialize();
    }

    // Manual ticks isolate the streamer's frame work from editor frame-rate variation.
    // Each tested tick is followed by a real Unity frame, allowing Addressables callbacks/destruction.
    private void Tick() => UpdateMethod.Invoke(streamer, null);
    private void MoveAndEvaluate(Vector3 cityPosition)
    {
        flight.SetPose(cityPosition, Quaternion.identity);
        EvaluateMethod.Invoke(streamer, null);
    }
    private IEnumerator WaitLoaded(int count)
    {
        for (int i = 0; i < 120 && streamer.LoadedTiles != count; i++) { Tick(); yield return null; }
        Assert.That(streamer.LoadedTiles, Is.EqualTo(count));
        Assert.That(streamer.PendingLoads, Is.Zero);
        Assert.That(streamer.EstimatedResidentBytes, Is.EqualTo(count * 1024));
    }

    private SurfaceDetailComparison EnableSurfaceFeedback()
    {
        var surfaces = host.AddComponent<SurfaceDetailComparison>();
        streamer.surfaces = surfaces;
        return surfaces;
    }
    private float TileOpacity(Transform tile)
    {
        var block = new MaterialPropertyBlock();
        tile.GetComponent<Renderer>().GetPropertyBlock(block);
        return block.GetFloat(SurfaceDetailComparison.DistanceFadeProperty);
    }
    private void PreventCatalogEvaluation() => typeof(CityStreamer).GetField("nextEvaluation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(streamer, float.PositiveInfinity);

    private void CreateGeometryStreamer(bool withOrigin = false)
    {
        CreateStreamer(3, CityLayer.Core, withOrigin);
        catalog.tiles[0].layer=CityLayer.Skyline; catalog.tiles[0].requiredReplacementCount=2;
        for(int i=0;i<3;i++)
        {
            catalog.tiles[i].cityOrigin=Vector3.zero;
            catalog.tiles[i].cityBounds=new Bounds(i==0?Vector3.zero:Vector3.right*10000, Vector3.one);
            if(i>0)catalog.tiles[i].replaces=new[]{catalog.tiles[0].id};
        }
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));ownershipAssets.Add(material);
        material.name="Skyline";
        material.SetFloat("_Surface",0);material.SetFloat("_AlphaClip",0);material.SetFloat("_ZWrite",1);material.SetFloat("_Cull",2);
        provider.AssetFactory=address=>
        {
            int index=Array.FindIndex(catalog.tiles,t=>t.address==address);
            var mesh=new Mesh {vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.right*10,Vector3.right*11,Vector3.right*10+Vector3.up},
                triangles=index==0?new[]{0,1,2,3,4,5}:index==1?new[]{0,1,2}:new[]{3,4,5}};
            ownershipAssets.Add(mesh);
            var root=new GameObject("ownership fixture");root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;
            return root;
        };
        var fixtureRoots=Array.ConvertAll(catalog.tiles,t=>provider.AssetFactory(t.address));
        var manager=(SkylineGeometryOwnership)typeof(CityStreamer).GetField("ownership",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streamer);
        OwnershipTestData.Configure(manager,catalog.tiles,fixtureRoots,catalog.sourceVersion);
        foreach(var fixture in fixtureRoots)Object.Destroy(fixture);
    }
    private Transform OwnershipTile(int index)=>host.transform.Find(catalog.tiles[index].id);
    private void CreateSplitWallStreamer()
    {
        CreateGeometryStreamer();catalog.tiles[0].requiredReplacementCount=3;
        catalog.tiles[1].cityBounds=new Bounds(Vector3.zero,Vector3.one);
        catalog.tiles[2].cityBounds=new Bounds(Vector3.right*1500,Vector3.one);
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Skyline"};ownershipAssets.Add(material);
        material.SetFloat("_Surface",0);material.SetFloat("_AlphaClip",0);material.SetFloat("_ZWrite",1);material.SetFloat("_Cull",2);
        provider.AssetFactory=address=>
        {
            int index=Array.FindIndex(catalog.tiles,t=>t.address==address);
            var vertices=new[]{Vector3.zero,Vector3.right,Vector3.right+Vector3.up,Vector3.up};
            var mesh=new Mesh {vertices=vertices,triangles=index==0?new[]{0,1,2,0,2,3}:index==1?new[]{0,1,3}:new[]{1,2,3}};ownershipAssets.Add(mesh);
            var root=new GameObject("split wall fixture");root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=material;return root;
        };
        var roots=Array.ConvertAll(catalog.tiles,t=>provider.AssetFactory(t.address));
        var map=OwnershipTestData.Compile(catalog.tiles,roots,catalog.sourceVersion);
        Assert.That(map.groups[0].parts[0].owners[0].requiredCoreIds.Length,Is.EqualTo(2));
        var manager=(SkylineGeometryOwnership)typeof(CityStreamer).GetField("ownership",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streamer);
        manager.Configure(map,catalog.sourceVersion,_=>"fixture-content",catalog);
        foreach(var root in roots)Object.Destroy(root);
    }
    [UnityTest]
    public IEnumerator JointWallLoadsItsOutOfRadiusPartnerAndReleasesItAfterTheSeedLeaves()
    {
        CreateSplitWallStreamer();MoveAndEvaluate(Vector3.zero);
        yield return WaitLoaded(3);yield return WaitOwnershipIdle();
        Assert.That(SkylineIndexCount(),Is.Zero);Assert.That(OwnershipTile(2).gameObject.activeSelf,Is.True);
        for(int cycle=0;cycle<2;cycle++)
        {
            MoveAndEvaluate(Vector3.right*10000);yield return WaitLoaded(1);yield return WaitOwnershipIdle();
            Assert.That(SkylineIndexCount(),Is.EqualTo(6),"Fallback returns before either provider releases.");
            MoveAndEvaluate(Vector3.zero);yield return WaitLoaded(3);yield return WaitOwnershipIdle();
            Assert.That(SkylineIndexCount(),Is.Zero);
        }
        Assert.That(provider.ReleasesPerAddress[catalog.tiles[2].address],Is.EqualTo(2));
    }
    [UnityTest]
    public IEnumerator JointPartnerRequestsRespectTheResidencyBudgetAndKeepFallback()
    {
        CreateSplitWallStreamer();streamer.residencyBudgetBytes=2048;MoveAndEvaluate(Vector3.zero);
        yield return WaitLoaded(2);yield return WaitOwnershipIdle();
        for(int i=0;i<5;i++){Tick();yield return null;}
        Assert.That(streamer.LoadedTiles,Is.EqualTo(2));Assert.That(SkylineIndexCount(),Is.EqualTo(6));
        streamer.residencyBudgetBytes=3072;yield return WaitLoaded(3);yield return WaitOwnershipIdle();
        Assert.That(SkylineIndexCount(),Is.Zero);
    }
    private int SkylineIndexCount()=>OwnershipTile(0).GetComponent<MeshFilter>().sharedMesh.triangles.Length;
    private IEnumerator WaitOwnershipIdle()
    {
        for(int i=0;i<120&&streamer.OwnershipWorkPending;i++){Tick();yield return null;}
        Assert.That(streamer.OwnershipWorkPending,Is.False);
    }

    [UnityTest]
    public IEnumerator DelayedCoreMasksOnlyItsOwnedGeometryAndFailedSecondCoreRetainsRemainingFallback()
    {
        CreateGeometryStreamer();yield return WaitLoaded(1);yield return WaitOwnershipIdle();
        provider.DeferCompletions=true;catalog.tiles[1].cityBounds=new Bounds(Vector3.zero,Vector3.one);
        MoveAndEvaluate(Vector3.zero);Tick();yield return null;
        Assert.That(streamer.PendingLoads,Is.EqualTo(1));Assert.That(SkylineIndexCount(),Is.EqualTo(6));
        provider.CompletePending();yield return WaitLoaded(2);yield return WaitOwnershipIdle();
        Assert.That(OwnershipTile(1).gameObject.activeSelf,Is.True);Assert.That(OwnershipTile(0).gameObject.activeSelf,Is.True);
        Assert.That(SkylineIndexCount(),Is.EqualTo(3));Assert.That(streamer.MaskedSkylineTriangles,Is.EqualTo(1));
        provider.DeferCompletions=false;provider.FailAddresses.Add(catalog.tiles[2].address);catalog.tiles[2].cityBounds=new Bounds(Vector3.zero,Vector3.one);
        LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex("Synthetic tile failure"));
        LogAssert.Expect(LogType.Warning,"City tile failed: "+catalog.tiles[2].id+". Keeping skyline fallback.");
        MoveAndEvaluate(Vector3.zero);
        for(int i=0;i<10&&streamer.Failures==0;i++){Tick();yield return null;}
        Assert.That(streamer.Failures,Is.EqualTo(1));Assert.That(SkylineIndexCount(),Is.EqualTo(3));Assert.That(OwnershipTile(0).gameObject.activeSelf,Is.True);
    }

    [UnityTest]
    public IEnumerator CanceledCoreCompletionRetainsFullFallbackAndReloadedCoreOwnsOnlyItsTriangle()
    {
        CreateGeometryStreamer();yield return WaitLoaded(1);yield return WaitOwnershipIdle();
        provider.DeferCompletions=true;catalog.tiles[1].cityBounds=new Bounds(Vector3.zero,Vector3.one);
        MoveAndEvaluate(Vector3.zero);Tick();yield return null;Assert.That(provider.Pending,Is.EqualTo(1));
        catalog.tiles[1].cityBounds=new Bounds(Vector3.right*10000,Vector3.one);MoveAndEvaluate(Vector3.zero);provider.CompletePending();Tick();yield return null;
        Assert.That(streamer.LoadedTiles,Is.EqualTo(1));Assert.That(SkylineIndexCount(),Is.EqualTo(6));Tick();yield return null;Assert.That(provider.Releases,Is.EqualTo(1));
        provider.DeferCompletions=false;catalog.tiles[1].cityBounds=new Bounds(Vector3.zero,Vector3.one);MoveAndEvaluate(Vector3.zero);
        yield return WaitLoaded(2);yield return WaitOwnershipIdle();Assert.That(SkylineIndexCount(),Is.EqualTo(3));Assert.That(streamer.MaskedSkylineTriangles,Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator GeometryOwnershipSurvivesOriginRebaseAndRestoresFallbackBeforeProviderRelease()
    {
        CreateGeometryStreamer(true);catalog.tiles[1].cityBounds=new Bounds(Vector3.zero,Vector3.one);
        yield return WaitLoaded(2);yield return WaitOwnershipIdle();
        flight.SetPose(new Vector3(600,0,20),Quaternion.identity);yield return null;
        Assert.That(streamer.origin.Offset.sqrMagnitude,Is.GreaterThan(0));
        Assert.That(OwnershipTile(0).position,Is.EqualTo(-streamer.origin.Offset));Assert.That(OwnershipTile(1).position,Is.EqualTo(-streamer.origin.Offset));
        Assert.That(SkylineIndexCount(),Is.EqualTo(3));
        catalog.tiles[1].cityBounds=new Bounds(Vector3.right*10000,Vector3.one);MoveAndEvaluate(Vector3.zero);streamer.workBudgetMilliseconds=0;Tick();yield return null;
        Assert.That(OwnershipTile(1).gameObject.activeSelf,Is.True);Assert.That(provider.Releases,Is.Zero);Assert.That(SkylineIndexCount(),Is.EqualTo(3));
        streamer.workBudgetMilliseconds=1000;Tick();yield return null;
        Assert.That(OwnershipTile(1).gameObject.activeSelf,Is.False);Assert.That(SkylineIndexCount(),Is.EqualTo(6));Assert.That(provider.Releases,Is.Zero,"The fallback cohort must publish before the Core handle is released.");
        Tick();yield return null;Assert.That(provider.Releases,Is.EqualTo(1));Assert.That(streamer.LoadedTiles,Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator ReplacementCountDoesNotHideUnownedSkylineFaces()
    {
        CreateGeometryStreamer();catalog.tiles[0].requiredReplacementCount=1;catalog.tiles[1].cityBounds=new Bounds(Vector3.zero,Vector3.one);
        yield return WaitLoaded(2);yield return WaitOwnershipIdle();
        Assert.That(OwnershipTile(1).gameObject.activeSelf,Is.True);Assert.That(OwnershipTile(0).gameObject.activeSelf,Is.True);
        Assert.That(SkylineIndexCount(),Is.EqualTo(3),"One Core count cannot demonstrate ownership of the second fallback triangle.");
        catalog.tiles[1].cityBounds=new Bounds(Vector3.right*10000,Vector3.one);MoveAndEvaluate(Vector3.zero);
        for(int i=0;i<10&&streamer.LoadedTiles!=1;i++){Tick();yield return null;}
        provider.AssetFactory=address=>
        {
            var root=new GameObject("nonmatching core");var mesh=new Mesh {vertices=new[]{Vector3.forward*5,Vector3.forward*5+Vector3.right,Vector3.forward*5+Vector3.up},triangles=new[]{0,1,2}};ownershipAssets.Add(mesh);
            root.AddComponent<MeshFilter>().sharedMesh=mesh;root.AddComponent<MeshRenderer>().sharedMaterial=OwnershipTile(0).GetComponent<MeshRenderer>().sharedMaterial;return root;
        };
        catalog.tiles[1].cityBounds=new Bounds(Vector3.zero,Vector3.one);MoveAndEvaluate(Vector3.zero);yield return WaitLoaded(2);yield return WaitOwnershipIdle();
        Assert.That(streamer.MaskedSkylineTriangles,Is.Zero);Assert.That(OwnershipTile(0).gameObject.activeSelf,Is.True);Assert.That(SkylineIndexCount(),Is.EqualTo(6));
    }

    [UnityTest]
    public IEnumerator NearFadeFollowsFastMovementBetweenCatalogEvaluationsWithoutChangingBodyVisibility()
    {
        provider.RenderAssets = true; CreateStreamer(2, CityLayer.Near);
        catalog.tiles[1].layer = CityLayer.Core;
        catalog.tiles[0].cityBounds = new Bounds(Vector3.zero, Vector3.zero);
        var surfaces = EnableSurfaceFeedback();
        flight.SetPose(Vector3.right * 150, Quaternion.identity);
        yield return WaitLoaded(2);
        Transform near = host.transform.Find(catalog.tiles[0].id), body = host.transform.Find(catalog.tiles[1].id);
        Assert.That(TileOpacity(near), Is.EqualTo(.5f).Within(.00001f), "A new tile must receive current distance fade on its first frame.");
        Assert.That(body.gameObject.activeSelf, Is.True, "Near is additive; its fade must not replace the body.");
        long scans = streamer.CatalogEvaluations;
        int requests = provider.Provides;
        PreventCatalogEvaluation();
        for (int frame = 1; frame <= 24; frame++)
        {
            float distance = 150 + frame * (100f / 60f);
            flight.SetPose(Vector3.right * distance, Quaternion.identity); Tick();
            Assert.That(TileOpacity(near), Is.EqualTo(Mathf.Clamp01((180 - distance) / 60)).Within(.00001f));
            Assert.That(body.gameObject.activeSelf, Is.True);
            yield return null;
        }
        Assert.That(near.gameObject.activeSelf, Is.True, "The exit band retains zero-opacity detail without toggling.");
        Assert.That(TileOpacity(near), Is.Zero);
        flight.SetPose(Vector3.right * 201, Quaternion.identity); Tick();
        Assert.That(near.gameObject.activeSelf, Is.False);
        flight.SetPose(Vector3.right * 190, Quaternion.identity); Tick();
        Assert.That(near.gameObject.activeSelf, Is.False, "Re-entry must reach the 180 metre enter boundary.");
        flight.SetPose(Vector3.right * 170, Quaternion.identity); Tick();
        Assert.That(near.gameObject.activeSelf, Is.True);
        Assert.That(TileOpacity(near), Is.EqualTo(1f / 6).Within(.00001f));
        Assert.That(streamer.CatalogEvaluations, Is.EqualTo(scans));
        Assert.That(provider.Provides, Is.EqualTo(requests));
        Assert.That(provider.Releases, Is.Zero);
        Assert.That(surfaces.OpacityPropertyWrites, Is.GreaterThan(1));
    }

    [UnityTest]
    public IEnumerator StationaryNearFeedbackAvoidsPropertyWritesAndManagedAllocations()
    {
        provider.RenderAssets = true; CreateStreamer(1, CityLayer.Near);
        catalog.tiles[0].cityBounds = new Bounds(Vector3.zero, Vector3.zero);
        var surfaces = EnableSurfaceFeedback();
        flight.SetPose(Vector3.right * 150, Quaternion.identity); yield return WaitLoaded(1);
        PreventCatalogEvaluation();
        var tick = (Action)UpdateMethod.CreateDelegate(typeof(Action), streamer);
        for (int i = 0; i < 32; i++) tick();
        long writes = surfaces.OpacityPropertyWrites, visits = surfaces.OpacityTileVisits, scans = streamer.CatalogEvaluations;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++) tick();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.That(surfaces.OpacityPropertyWrites, Is.EqualTo(writes));
        Assert.That(surfaces.OpacityTileVisits, Is.EqualTo(visits), "An unchanged tile must not traverse its renderer bindings.");
        Assert.That(streamer.CatalogEvaluations, Is.EqualTo(scans));
        Assert.That(allocated, Is.Zero, "Warmed stationary resident feedback must not allocate managed memory.");
        long movingBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++)
        {
            flight.SetPose(Vector3.right * (125 + (i % 31) * 1.6f), Quaternion.identity);
            tick();
        }
        long movingAllocated = GC.GetAllocatedBytesForCurrentThread() - movingBefore;
        Assert.That(movingAllocated, Is.Zero, "Changing a warmed resident's fade must not allocate managed memory.");
        Assert.That(surfaces.OpacityPropertyWrites, Is.EqualTo(writes + 512), "One changed tile renderer needs one property write per tick.");
        Assert.That(surfaces.OpacityTileVisits, Is.EqualTo(visits + 512));
        Assert.That(streamer.CatalogEvaluations, Is.EqualTo(scans));
        Assert.That(provider.Provides, Is.EqualTo(1)); Assert.That(provider.Releases, Is.Zero);
    }

    [UnityTest]
    public IEnumerator DelayedNearLoadUsesLatestPoseAndRestoresMaterialsOnRelease()
    {
        provider.RenderAssets = true; provider.DeferCompletions = true;
        CreateStreamer(1, CityLayer.Near); catalog.tiles[0].cityBounds = new Bounds(Vector3.zero, Vector3.zero);
        var surfaces = EnableSurfaceFeedback();
        flight.SetPose(Vector3.right * 120, Quaternion.identity); Tick(); yield return null;
        Assert.That(streamer.PendingLoads, Is.EqualTo(1));
        PreventCatalogEvaluation();
        flight.SetPose(Vector3.right * 165, Quaternion.identity); provider.CompletePending(); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.EqualTo(1));
        var tile = host.transform.Find(catalog.tiles[0].id);
        Assert.That(TileOpacity(tile), Is.EqualTo(.25f).Within(.00001f));
        var renderer = tile.GetComponent<Renderer>();
        var original = surfaces.OriginalMaterialsSnapshot()[0];
        Assert.That(renderer.sharedMaterial, Is.Not.SameAs(original));
        foreach (string pass in new[] { "DepthOnly", "DepthNormals", "MotionVectors" })
            Assert.That(renderer.sharedMaterial.FindPass(pass), Is.GreaterThanOrEqualTo(0), "A freshly registered Near variant must retain its existing " + pass + " pass.");
        var block = new MaterialPropertyBlock(); block.SetFloat("_UnrelatedTestValue", 42); renderer.SetPropertyBlock(block);
        flight.SetPose(Vector3.right * 170, Quaternion.identity); Tick();
        renderer.GetPropertyBlock(block); Assert.That(block.GetFloat("_UnrelatedTestValue"), Is.EqualTo(42));
        Assert.That(TileOpacity(tile), Is.EqualTo(1f / 6).Within(.00001f));
        surfaces.EnabledDetails = false; Tick(); Assert.That(TileOpacity(tile), Is.EqualTo(1f / 6).Within(.00001f));
        surfaces.Unregister(tile.gameObject); Assert.That(renderer.sharedMaterial, Is.SameAs(original));
        Assert.That(surfaces.RuntimeVariantCount, Is.Zero);
        MoveAndEvaluate(Vector3.right * 10000); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.Zero); Assert.That(provider.Releases, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator FailedNearLoadRetainsSkylineAndDefersHandleRelease()
    {
        CreateStreamer(2, CityLayer.Near); catalog.tiles[1].layer = CityLayer.Skyline;
        // Load the fallback first, then fail just the Near request.
        catalog.tiles[0].cityBounds = new Bounds(Vector3.right * 10000, Vector3.one);
        yield return WaitLoaded(1);
        provider.FailLoads = true; catalog.tiles[0].cityBounds = new Bounds(Vector3.zero, Vector3.one);
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Synthetic tile failure"));
        LogAssert.Expect(LogType.Warning, "City tile failed: " + catalog.tiles[0].id + ". Keeping skyline fallback.");
        MoveAndEvaluate(Vector3.zero); Tick(); yield return null; Tick(); yield return null;
        Assert.That(streamer.Failures, Is.EqualTo(1)); Assert.That(streamer.LoadedTiles, Is.EqualTo(1));
        Assert.That(host.transform.Find(catalog.tiles[1].id).gameObject.activeSelf, Is.True);
        Tick(); yield return null;
        Assert.That(streamer.PendingLoads, Is.Zero);
        Assert.That(streamer.EstimatedResidentBytes, Is.EqualTo(1024));
    }

    [UnityTest]
    public IEnumerator NearCanceledCompletionCanReturnWithCurrentFadeBeforeItsQueuedRelease()
    {
        provider.RenderAssets = true; provider.DeferCompletions = true;
        CreateStreamer(1, CityLayer.Near); catalog.tiles[0].cityBounds = new Bounds(Vector3.zero, Vector3.zero);
        EnableSurfaceFeedback();
        flight.SetPose(Vector3.right * 120, Quaternion.identity); Tick(); yield return null;
        MoveAndEvaluate(Vector3.right * 10000); provider.CompletePending(); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.Zero); Assert.That(streamer.PendingLoads, Is.Zero);
        Assert.That(provider.Releases, Is.Zero);
        MoveAndEvaluate(Vector3.right * 165); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.EqualTo(1));
        Assert.That(TileOpacity(host.transform.Find(catalog.tiles[0].id)), Is.EqualTo(.25f).Within(.00001f));
        Assert.That(provider.Provides, Is.EqualTo(1)); Assert.That(provider.Releases, Is.Zero);
        Object.Destroy(host); yield return null; host = null;
        Assert.That(provider.Releases, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator ReloadingNearAtTheSameDistanceInitializesTheNewRendererFade()
    {
        provider.RenderAssets = true; CreateStreamer(1, CityLayer.Near);
        catalog.tiles[0].cityBounds = new Bounds(Vector3.zero, Vector3.zero); EnableSurfaceFeedback();
        flight.SetPose(Vector3.right * 150, Quaternion.identity); yield return WaitLoaded(1);
        Assert.That(TileOpacity(host.transform.Find(catalog.tiles[0].id)), Is.EqualTo(.5f).Within(.00001f));
        MoveAndEvaluate(Vector3.right * 10000); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.Zero); Assert.That(provider.Releases, Is.EqualTo(1));
        MoveAndEvaluate(Vector3.right * 150); yield return WaitLoaded(1);
        Assert.That(TileOpacity(host.transform.Find(catalog.tiles[0].id)), Is.EqualTo(.5f).Within(.00001f), "The prior instance's tile cache must not skip the replacement renderer.");
        Assert.That(provider.Provides, Is.EqualTo(2)); Assert.That(provider.Releases, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator CoreBoundaryJitterKeepsCoverageAndPartialSkylineFallback()
    {
        CreateStreamer(3);
        catalog.tiles[0].cityBounds = catalog.tiles[1].cityBounds = new Bounds(Vector3.zero, Vector3.zero);
        catalog.tiles[1].layer = catalog.tiles[2].layer = CityLayer.Skyline;
        catalog.tiles[1].requiredReplacementCount = 1;
        catalog.tiles[2].requiredReplacementCount = 2;
        catalog.tiles[0].replaces = new[] { catalog.tiles[1].id, catalog.tiles[2].id };
        flight.SetPose(Vector3.right * 800, Quaternion.identity); yield return WaitLoaded(3);
        Transform core = host.transform.Find(catalog.tiles[0].id), covered = host.transform.Find(catalog.tiles[1].id), partial = host.transform.Find(catalog.tiles[2].id);
        foreach (float distance in new[] { 800.1f, 799.9f, 810f, 820f })
        {
            MoveAndEvaluate(Vector3.right * distance);
            Assert.That(core.gameObject.activeSelf, Is.True);
            Assert.That(covered.gameObject.activeSelf, Is.False);
            Assert.That(partial.gameObject.activeSelf, Is.True, "Partial coverage must retain the complete fallback.");
        }
        MoveAndEvaluate(Vector3.right * 821);
        Assert.That(core.gameObject.activeSelf, Is.False); Assert.That(covered.gameObject.activeSelf, Is.True);
        MoveAndEvaluate(Vector3.right * 810);
        Assert.That(core.gameObject.activeSelf, Is.False); Assert.That(covered.gameObject.activeSelf, Is.True);
        MoveAndEvaluate(Vector3.right * 800);
        Assert.That(core.gameObject.activeSelf, Is.True); Assert.That(covered.gameObject.activeSelf, Is.False);
        Assert.That(provider.Provides, Is.EqualTo(3)); Assert.That(provider.Releases, Is.Zero);
    }

    [UnityTest]
    public IEnumerator VegetationPreloadsBeyondStreetscapeAndSurvivesBoundaryRoundTrip()
    {
        CreateStreamer(1, CityLayer.Vegetation);
        catalog.tiles[0].cityBounds = new Bounds(Vector3.zero, Vector3.zero);
        MoveAndEvaluate(Vector3.right * 950); yield return WaitLoaded(1);
        Transform tree = host.transform.Find(catalog.tiles[0].id);
        Assert.That(tree.gameObject.activeSelf, Is.False, "Preloading must not expose the tile early.");
        MoveAndEvaluate(Vector3.right * 750);
        Assert.That(tree.gameObject.activeSelf, Is.True, "Trees should already be visible well beyond the old 250m cutoff.");
        foreach (float distance in new[] { 801f, 799f, 810f, 820f })
        {
            MoveAndEvaluate(Vector3.right * distance);
            Assert.That(tree.gameObject.activeSelf, Is.True, "Boundary jitter must not toggle the trees.");
        }
        MoveAndEvaluate(Vector3.right * 821);
        Assert.That(tree.gameObject.activeSelf, Is.False);
        MoveAndEvaluate(Vector3.right * 810);
        Assert.That(tree.gameObject.activeSelf, Is.False);
        MoveAndEvaluate(Vector3.right * 800);
        Assert.That(tree.gameObject.activeSelf, Is.True);
        MoveAndEvaluate(Vector3.up * 400);
        Assert.That(tree.gameObject.activeSelf, Is.True, "Aerial observers must retain the nearby canopy.");
        MoveAndEvaluate(Vector3.right * 1100); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.EqualTo(1));
        Assert.That(provider.Provides, Is.EqualTo(1)); Assert.That(provider.Releases, Is.Zero);
        MoveAndEvaluate(Vector3.right * 1201); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.Zero); Assert.That(provider.Releases, Is.EqualTo(1));
        MoveAndEvaluate(Vector3.right * 750); yield return WaitLoaded(1);
        Assert.That(host.transform.Find(catalog.tiles[0].id).gameObject.activeSelf, Is.True);
        Assert.That(provider.Provides, Is.EqualTo(2));
    }

    [Test]
    public void FeedbackExecutionOrderFollowsMotionAndFloatingOrigin()
    {
        int Order(Type type) => type.GetCustomAttribute<DefaultExecutionOrder>().order;
        Assert.That(Order(typeof(FreeFlightController)), Is.LessThan(Order(typeof(FloatingOrigin))));
        Assert.That(Order(typeof(FloatingOrigin)), Is.LessThan(Order(typeof(CityStreamer))));
    }

    [UnityTest]
    public IEnumerator MovingAwayHidesImmediatelyAndReleasesAtMostOneAssetPerBudgetedFrame()
    {
        CreateStreamer(4); yield return WaitLoaded(4);
        MoveAndEvaluate(Vector3.right * 10000);
        Assert.That(provider.Releases, Is.Zero, "Classification must not batch-release handles.");
        foreach (Transform tile in host.transform) Assert.That(tile.gameObject.activeSelf, Is.False);
        streamer.workBudgetMilliseconds = 0; Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.EqualTo(4), "An exhausted work budget must defer release.");
        streamer.workBudgetMilliseconds = 1000;
        for (int remaining = 3; remaining >= 0; remaining--)
        {
            Tick(); yield return null;
            Assert.That(streamer.LoadedTiles, Is.EqualTo(remaining));
            Assert.That(streamer.EstimatedResidentBytes, Is.EqualTo(remaining * 1024));
            Assert.That(provider.Releases, Is.EqualTo(4 - remaining));
        }
        Assert.That(new HashSet<int>(provider.ReleaseFrames).Count, Is.EqualTo(4), "Asset provider releases must occur on different Unity frames.");
        Assert.That(streamer.Failures, Is.Zero);
    }

    [UnityTest]
    public IEnumerator ReturningBeforeQueuedReleaseRetainsExistingAssetsWithoutDuplicateLoads()
    {
        CreateStreamer(3); yield return WaitLoaded(3);
        MoveAndEvaluate(Vector3.right * 10000); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.EqualTo(2)); Assert.That(provider.Releases, Is.EqualTo(1));
        MoveAndEvaluate(Vector3.zero);
        yield return WaitLoaded(3);
        Assert.That(provider.Provides, Is.EqualTo(4), "Only the one already released tile should be reloaded.");
        Assert.That(provider.Releases, Is.EqualTo(1), "Queued tiles that became useful again must retain their handles.");
        foreach (Transform tile in host.transform) Assert.That(tile.gameObject.activeSelf, Is.True);
    }

    [UnityTest]
    public IEnumerator CompletedUnwantedLoadCanBecomeUsefulBeforeReleaseWithoutDownloadingAgain()
    {
        provider.DeferCompletions = true; CreateStreamer(1); Tick(); yield return null;
        Assert.That(provider.Pending, Is.EqualTo(1)); Assert.That(streamer.PendingLoads, Is.EqualTo(1));
        MoveAndEvaluate(Vector3.right * 10000); provider.CompletePending(); Tick(); yield return null;
        Assert.That(streamer.PendingLoads, Is.Zero); Assert.That(streamer.LoadedTiles, Is.Zero);
        Assert.That(provider.Releases, Is.Zero, "A just-completed unwanted load is queued for the next frame.");
        MoveAndEvaluate(Vector3.zero); Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.EqualTo(1)); Assert.That(streamer.PendingLoads, Is.Zero);
        Assert.That(provider.Provides, Is.EqualTo(1)); Assert.That(provider.Releases, Is.Zero);
        Assert.That(streamer.EstimatedResidentBytes, Is.EqualTo(1024));
    }

    [UnityTest]
    public IEnumerator DestroyingStreamerReleasesAllQueuedAssetsEvenWithNoFrameBudget()
    {
        CreateStreamer(3); yield return WaitLoaded(3);
        MoveAndEvaluate(Vector3.right * 10000); streamer.workBudgetMilliseconds = 0;
        Object.Destroy(host); yield return null; host = null;
        Assert.That(provider.Releases, Is.EqualTo(3), "Shutdown must release every retained handle without waiting for the deferred queue.");
    }

    [UnityTest]
    public IEnumerator SwappedResidentCanBeReleasedReloadedAndCleanedUpWithoutLosingOtherHandles()
    {
        CreateStreamer(5); yield return WaitLoaded(5);
        Bounds first = catalog.tiles[1].cityBounds, last = catalog.tiles[4].cityBounds;
        catalog.tiles[1].cityBounds = catalog.tiles[4].cityBounds = new Bounds(Vector3.right * 10000, Vector3.one);
        MoveAndEvaluate(Vector3.zero);
        Tick(); yield return null;
        Tick(); yield return null;
        Assert.That(streamer.LoadedTiles, Is.EqualTo(3)); Assert.That(streamer.EstimatedResidentBytes, Is.EqualTo(3072));
        Assert.That(provider.ReleasesPerAddress[catalog.tiles[1].address], Is.EqualTo(1));
        Assert.That(provider.ReleasesPerAddress[catalog.tiles[4].address], Is.EqualTo(1), "A queued resident moved into the freed slot must still release its own handle.");
        catalog.tiles[1].cityBounds = first; catalog.tiles[4].cityBounds = last;
        MoveAndEvaluate(Vector3.zero); yield return WaitLoaded(5);
        Assert.That(provider.Provides, Is.EqualTo(7), "Only the two departed residents may be downloaded again.");
        foreach (Transform tile in host.transform) Assert.That(tile.gameObject.activeSelf, Is.True);
        streamer.workBudgetMilliseconds = 0; Object.Destroy(host); yield return null; host = null;
        Assert.That(provider.Releases, Is.EqualTo(7));
        for (int i = 0; i < catalog.tiles.Length; i++)
            Assert.That(provider.ReleasesPerAddress[catalog.tiles[i].address], Is.EqualTo(i == 1 || i == 4 ? 2 : 1),
                "Each original/reloaded Addressables handle must be released exactly once.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        provider?.CompletePending();
        if (host != null) Object.Destroy(host);
        if (observer != null) Object.Destroy(observer);
        if (catalog != null) Object.Destroy(catalog);
        yield return null;
        if (locator != null) Addressables.RemoveResourceLocator(locator);
        if (provider != null)
        {
            Addressables.ResourceManager.ResourceProviders.Remove(provider); provider.Cleanup();
        }
        yield return null;
        foreach(var asset in ownershipAssets)if(asset!=null)Object.Destroy(asset);ownershipAssets.Clear();
        host = observer = null; streamer = null; flight = null; catalog = null; locator = null; provider = null;
    }
}
