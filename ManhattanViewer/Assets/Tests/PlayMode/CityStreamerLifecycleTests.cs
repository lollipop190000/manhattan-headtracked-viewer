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
        public int Pending => pending.Count;
        public readonly List<int> ReleaseFrames = new List<int>();
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
            var asset = new GameObject("provided " + handle.Location.PrimaryKey);
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

    private void CreateStreamer(int count)
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
                id = address, address = address, layer = CityLayer.Core, residentBytes = 1024,
                cityBounds = new Bounds(new Vector3(i * 20, 0, 0), new Vector3(10, 20, 10)),
                cityOrigin = new Vector3(i * 20, 0, 0)
            };
        }
        host = new GameObject("streaming lifecycle host");
        streamer = host.AddComponent<CityStreamer>(); streamer.enabled = false;
        streamer.catalog = catalog; streamer.flight = flight; streamer.workBudgetMilliseconds = 1000;
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
        host = observer = null; streamer = null; flight = null; catalog = null; locator = null; provider = null;
    }
}
