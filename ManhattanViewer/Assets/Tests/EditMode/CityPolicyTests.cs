using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System;
using System.IO;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class CityPolicyTests
{
    [TestCase(CityLayer.Core)] [TestCase(CityLayer.Ground)] [TestCase(CityLayer.Near)]
    [TestCase(CityLayer.Vegetation)] [TestCase(CityLayer.Collision)]
    public void VisibilityBoundaryRoundTripsRequireLeavingTheExitBand(CityLayer layer)
    {
        float enter = CityStreamer.VisibleRadius(layer), exit = CityStreamer.VisibleExitRadius(layer);
        Assert.That(exit, Is.GreaterThan(enter));
        Assert.That(exit, Is.LessThan(CityStreamer.LoadRadius(layer)));
        Assert.That(exit, Is.LessThan(CityStreamer.ReleaseRadius(layer)));
        bool visible = false;
        foreach (float distance in new[] { enter + .1f, enter + .01f })
            Assert.That(visible = CityStreamer.IsVisible(layer, distance, visible), Is.False);
        Assert.That(visible = CityStreamer.IsVisible(layer, enter, visible), Is.True);
        foreach (float distance in new[] { enter + .1f, enter - .1f, exit })
            Assert.That(visible = CityStreamer.IsVisible(layer, distance, visible), Is.True);
        Assert.That(visible = CityStreamer.IsVisible(layer, exit + .01f, visible), Is.False);
        Assert.That(CityStreamer.IsVisible(layer, exit - .01f, visible), Is.False);
        Assert.That(CityStreamer.IsVisible(layer, enter, visible), Is.True);
    }

    [TestCase(0f, 1f)] [TestCase(120f, 1f)] [TestCase(150f, .5f)]
    [TestCase(180f, 0f)] [TestCase(190f, 0f)] [TestCase(320f, 0f)]
    public void NearFadeRetainsPhysicalEndpointsAndZeroOpacityExitBand(float distance, float expected) =>
        Assert.That(CityStreamer.NearOpacity(distance), Is.EqualTo(expected).Within(.00001f));

    [Test]
    public void CurrentWindowsProcessHasRealWorkingSetTelemetry()
    {
        if (Application.platform != RuntimePlatform.WindowsEditor && Application.platform != RuntimePlatform.WindowsPlayer)
            Assert.Ignore("Windows OS telemetry integration check.");
        Assert.That(ProcessMemoryTelemetry.TryWorkingSetBytes(out long bytes), Is.True);
        Assert.That(bytes, Is.GreaterThan(1024L * 1024), "An active Unity process must report actual resident memory instead of an unavailable zero counter.");
    }

    [Test]
    public void StreamingBoundsDistanceUsesNearestSurfaceRatherThanTileCenter()
    {
        var bounds = new Bounds(new Vector3(250, 50, 250), new Vector3(500, 100, 500));
        Assert.That(CityStreamer.DistanceToTile(bounds, new Vector3(501, 1, 250)), Is.EqualTo(1).Within(.0001));
        Assert.That(CityStreamer.DistanceToTile(bounds, bounds.center), Is.Zero);
    }

    [Test]
    public void ManagedBoundsDistanceMatchesUnityInsideFacesEdgesCornersAndDegenerateBounds()
    {
        var boundsCases = new[]
        {
            new Bounds(new Vector3(250, 50, 250), new Vector3(500, 100, 500)),
            new Bounds(new Vector3(-1225.25f, 1.75f, 3585.5f), new Vector3(125.5f, 63.25f, 250.75f)),
            new Bounds(new Vector3(9000, 0, -8000), Vector3.zero),
            new Bounds(new Vector3(-9000, 0, 8000), new Vector3(500, 0, 500))
        };
        foreach (Bounds bounds in boundsCases)
        {
            Vector3 minimum = bounds.min, maximum = bounds.max;
            var positions = new[]
            {
                bounds.center, minimum, maximum,
                new Vector3(minimum.x, bounds.center.y, bounds.center.z),
                new Vector3(maximum.x + 1, bounds.center.y, bounds.center.z),
                new Vector3(bounds.center.x, minimum.y - 2, maximum.z + 3),
                minimum - new Vector3(3, 4, 12), maximum + new Vector3(3, 4, 12)
            };
            foreach (Vector3 position in positions)
            {
                float expected = Vector3.Distance(bounds.ClosestPoint(position), position);
                Assert.That(CityStreamer.DistanceToTile(bounds, position), Is.EqualTo(expected).Within(.002f),
                    "Bounds " + bounds + ", position " + position);
            }
        }
    }

    [Test]
    public void ManagedBoundsDistanceMatchesUnityForDeterministicCityScaleCases()
    {
        var random = new System.Random(170322);
        float Sample(float minimum, float maximum) => minimum + (float)random.NextDouble() * (maximum - minimum);
        for (int i = 0; i < 2048; i++)
        {
            var bounds = new Bounds(new Vector3(Sample(-10000, 10000), Sample(-100, 1000), Sample(-10000, 10000)),
                new Vector3(Sample(0, 1000), Sample(0, 600), Sample(0, 1000)));
            Vector3 position = bounds.center + new Vector3(Sample(-1500, 1500), Sample(-800, 800), Sample(-1500, 1500));
            float expected = Vector3.Distance(bounds.ClosestPoint(position), position);
            Assert.That(CityStreamer.DistanceToTile(bounds, position), Is.EqualTo(expected).Within(.002f),
                "Deterministic case " + i + ": " + bounds + ", position " + position);
        }
    }

    [TestCase(CityLayer.Core)] [TestCase(CityLayer.Near)] [TestCase(CityLayer.Vegetation)] [TestCase(CityLayer.Collision)]
    public void ManagedDistancePreservesVisibilityLoadAndReleaseBoundaries(CityLayer layer)
    {
        var bounds = new Bounds(new Vector3(256, 64, -512), new Vector3(128, 128, 256));
        foreach (float radius in new[] { CityStreamer.VisibleRadius(layer), CityStreamer.LoadRadius(layer), CityStreamer.ReleaseRadius(layer) })
        {
            foreach (float offset in new[] { -.125f, 0f, .125f })
            {
                Vector3 position = new Vector3(bounds.max.x + radius + offset, bounds.center.y, bounds.center.z);
                float expected = Vector3.Distance(bounds.ClosestPoint(position), position);
                float actual = CityStreamer.DistanceToTile(bounds, position);
                Assert.That(actual, Is.EqualTo(expected), "Axis-aligned boundary distances must remain exact.");
                Assert.That(actual <= radius, Is.EqualTo(expected <= radius));
                Assert.That(1f - Mathf.InverseLerp(120f, 180f, actual), Is.EqualTo(1f - Mathf.InverseLerp(120f, 180f, expected)));
            }
        }
    }

    [TestCase(CityLayer.Core)] [TestCase(CityLayer.Near)] [TestCase(CityLayer.Vegetation)] [TestCase(CityLayer.Collision)]
    public void LoadAndReleaseHysteresisPreventsBoundaryReloading(CityLayer layer)
    {
        Assert.That(CityStreamer.LoadRadius(layer), Is.GreaterThan(CityStreamer.VisibleRadius(layer)));
        Assert.That(CityStreamer.ReleaseRadius(layer), Is.GreaterThan(CityStreamer.LoadRadius(layer)));
    }

    [Test]
    public void PartialReplacementFailureAndUnloadKeepSkylineCoverage()
    {
        var fallback = new CityTileRecord { id = "Skyline", layer = CityLayer.Skyline, requiredReplacementCount = 2 };
        Assert.That(CityStreamer.CanReplace(fallback, 0), Is.False);
        Assert.That(CityStreamer.CanReplace(fallback, 1), Is.False, "One pending or failed Core tile must retain the whole-building fallback.");
        Assert.That(CityStreamer.CanReplace(fallback, 2), Is.True);
        Assert.That(CityStreamer.CanReplace(fallback, 1), Is.False, "Unloading a covering Core tile restores the fallback.");
        Assert.That(CityStreamer.CanReplace(fallback, 2), Is.True, "Reloading complete coverage hides the fallback again.");
    }

    [Test]
    public void UnclassifiedFallbackAndGroundAreNeverHiddenByReplacementCounts()
    {
        var unclassified = new CityTileRecord { layer = CityLayer.Skyline, requiredReplacementCount = 0 };
        Assert.That(CityStreamer.CanReplace(unclassified, 10000), Is.False);
        var ground = new CityTileRecord { layer = CityLayer.Ground, requiredReplacementCount = 1 };
        Assert.That(CityStreamer.CanReplace(ground, 10000), Is.False);
    }

    [Test]
    public void RouteTravelsAtSelectedSpeedAndCompletesExactly()
    {
        var route = new BenchmarkRouteInput(new[] { Vector3.zero, new Vector3(100, 0, 0), new Vector3(100, 0, 100) }, 10);
        Assert.That(route.Duration, Is.EqualTo(20f));
        Assert.That(route.Sample(5).cityPosition, Is.EqualTo(new Vector3(50, 0, 0)));
        Assert.That(route.Sample(10).cityPosition, Is.EqualTo(new Vector3(100, 0, 50)));
        Assert.That(route.Sample(500).cityPosition, Is.EqualTo(new Vector3(100, 0, 100)));
        Assert.That(route.Complete, Is.True);
    }

    [Test]
    public void RouteResultIsIndependentOfFramePartition()
    {
        var points = new[] { Vector3.zero, Vector3.forward * 1000 };
        var fast = new BenchmarkRouteInput(points, 50); var slow = new BenchmarkRouteInput(points, 50);
        MotionCommand result = default;
        for (int i = 0; i < 120; i++) result = slow.Sample(1f / 60f);
        Assert.That(Vector3.Distance(result.cityPosition, fast.Sample(2).cityPosition), Is.LessThan(.001f));
    }

    [Test]
    public void CalibrationRejectsNonFiniteAndOutOfRangeInput()
    {
        Assert.That(ViewerPersistence.ValidatePhysical(.53f, .3f, .6f), Is.True);
        Assert.That(ViewerPersistence.ValidatePhysical(float.NaN, .3f, .6f), Is.False);
        Assert.That(ViewerPersistence.ValidatePhysical(.53f, float.PositiveInfinity, .6f), Is.False);
        Assert.That(ViewerPersistence.ValidatePhysical(.53f, .3f, 10f), Is.False);
        Assert.That(ViewerPersistence.CalibrationFile, Is.Not.EqualTo(ViewerPersistence.PreferencesFile));
        Assert.That(ViewerPersistence.CalibrationFile, Is.Not.EqualTo(ViewerPersistence.BookmarkFile));
    }

    [Test]
    public void PortableCalibrationDirectoryUsesLauncherOverrideAndDefault()
    {
        const string key = "HEADTRACKED_CALIBRATION_DIRECTORY";
        string previous = Environment.GetEnvironmentVariable(key);
        try
        {
            string configured = Path.Combine(Path.GetTempPath(), "ManhattanCalibrationPathTest");
            Environment.SetEnvironmentVariable(key, configured);
            Assert.That(ViewerPersistence.DirectoryPath, Is.EqualTo(Path.GetFullPath(configured)));
            Environment.SetEnvironmentVariable(key, null);
            Assert.That(ViewerPersistence.DirectoryPath, Is.EqualTo(Application.persistentDataPath));
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    [Test]
    public void TailStatisticsUseObservedSamples()
    {
        var values = new List<float>(); for (int i = 1; i <= 100; i++) values.Add(i);
        Assert.That(BenchmarkStatistics.Percentile(values, .95f), Is.EqualTo(95));
        Assert.That(BenchmarkStatistics.Percentile(values, .99f), Is.EqualTo(99));
        Assert.That(BenchmarkStatistics.Percentile(values, 1), Is.EqualTo(100));
    }

    [TestCase("Midtown")] [TestCase("Downtown")] [TestCase("Park")] [TestCase("Uptown")] [TestCase("Air")]
    public void RegionCircuitsFinishWithinTwentyMinutesAtTenMetresPerSecond(string region)
    {
        Vector3[] points = ViewerBenchmark.RegionRoute(region, Vector3.zero);
        var route = new BenchmarkRouteInput(points, 10);
        Assert.That(points[0], Is.EqualTo(points[points.Length - 1]));
        Assert.That(route.Duration, Is.LessThan(1200));
    }
}

public sealed class NearDiagnosticEmptyStateTests
{
    [Test]
    public void UnstartedStreamerDoesNotClaimLoadedActiveOpacityOrDistanceSamples()
    {
        var host = new GameObject("empty near diagnostic fixture");
        try
        {
            var streamer = host.AddComponent<CityStreamer>(); streamer.enabled = false;
            var state = streamer.GetNearDiagnostics();
            Assert.That(state.loadedTiles, Is.Zero); Assert.That(state.activeTiles, Is.Zero);
            Assert.That(state.activeRenderers, Is.Zero); Assert.That(state.visibleRendererFlags, Is.Zero);
            Assert.That(state.opacityAvailable, Is.False); Assert.That(state.nearestLoadedTile, Is.Null);
            Assert.That(state.sourceVersion, Is.EqualTo("unavailable"));
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
}

public sealed class CityEvaluationEquivalenceTests
{
    private static readonly Type Residency = typeof(CityStreamer).GetNestedType("Residency", BindingFlags.NonPublic);
    private static readonly MethodInfo EvaluateTile = typeof(CityStreamer).GetMethod("EvaluateTile", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly MethodInfo Evaluate = typeof(CityStreamer).GetMethod("Evaluate", BindingFlags.NonPublic | BindingFlags.Instance);
    private static FieldInfo Field(string name) => Residency.GetField(name, BindingFlags.Public | BindingFlags.Instance);
    private static readonly FieldInfo Tile = Field("tile"), Wanted = Field("wanted"), Visible = Field("visible"),
        Priority = Field("priority"), Distance = Field("distance"), HasHandle = Field("hasHandle"), Loading = Field("loading");
    private struct Decision { public bool wanted, visible; public float distance, priority; }
    private static Decision Legacy(CityTileRecord tile, Vector3 position, Vector3 predicted, bool hasHandle, bool wasVisible, bool collision)
    {
        float distance = CityStreamer.DistanceToTile(tile.cityBounds, position);
        float predictedDistance = CityStreamer.DistanceToTile(tile.cityBounds, predicted);
        bool allowed = tile.layer != CityLayer.Collision || collision;
        float nearest = Mathf.Min(distance, predictedDistance);
        float priority = tile.layer == CityLayer.Skyline ? -100000f : nearest;
        if (tile.layer == CityLayer.Core || tile.layer == CityLayer.Ground) priority -= 2000;
        return new Decision { wanted = allowed && nearest <= (hasHandle ? CityStreamer.ReleaseRadius(tile.layer) : CityStreamer.LoadRadius(tile.layer)),
            visible = allowed && CityStreamer.IsVisible(tile.layer, distance, wasVisible), distance = distance, priority = priority };
    }
    private static object State(CityTileRecord tile)
    {
        object state = Activator.CreateInstance(Residency, true); Tile.SetValue(state, tile); return state;
    }
    private static void Exact(float actual, float expected, string context)
    {
        if (float.IsNaN(expected)) Assert.That(float.IsNaN(actual), Is.True, context);
        else Assert.That(BitConverter.SingleToInt32Bits(actual), Is.EqualTo(BitConverter.SingleToInt32Bits(expected)), context);
    }
    private static void Compare(object state, Vector3 position, Vector3 predicted, bool hasHandle, bool wasVisible, bool collision)
    {
        var tile = (CityTileRecord)Tile.GetValue(state);
        Decision expected = Legacy(tile, position, predicted, hasHandle, wasVisible, collision);
        HasHandle.SetValue(state, hasHandle); Visible.SetValue(state, wasVisible);
        EvaluateTile.Invoke(null, new object[] { state, position, predicted, collision });
        string context = tile.layer + ", " + position + ", predicted " + predicted;
        Assert.That((bool)Wanted.GetValue(state), Is.EqualTo(expected.wanted), context);
        Assert.That((bool)Visible.GetValue(state), Is.EqualTo(expected.visible), context);
        if (expected.wanted)
        {
            Exact((float)Distance.GetValue(state), expected.distance, context + " requested distance");
            Exact((float)Priority.GetValue(state), expected.priority, context + " request priority");
        }
    }
    private static float Adjacent(float value, int direction) => BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(value) + direction);

    [TestCase(170322)] [TestCase(781313)] [TestCase(100100)]
    public void RandomizedCurrentAndPredictedDecisionsMatchLegacy(int seed)
    {
        var random = new System.Random(seed);
        float Sample(float extent) => ((float)random.NextDouble() * 2 - 1) * extent;
        var tile = new CityTileRecord(); object state = State(tile);
        for (int i = 0; i < 6000; i++)
        {
            tile.layer = (CityLayer)random.Next(0, 7); // Includes the legacy default-radius branch.
            tile.cityBounds = new Bounds(new Vector3(Sample(1000000), Sample(1000), Sample(1000000)),
                new Vector3(Mathf.Abs(Sample(1500)), Mathf.Abs(Sample(300)), Mathf.Abs(Sample(1500))));
            Vector3 position = tile.cityBounds.center + new Vector3(Sample(2500), Sample(600), Sample(2500));
            Vector3 predicted = position + new Vector3(Sample(100), Sample(100), Sample(100)) * 2;
            Compare(state, position, predicted, random.Next(2) == 0, random.Next(2) == 0, random.Next(2) == 0);
        }
    }

    [TestCase(CityLayer.Skyline)] [TestCase(CityLayer.Core)] [TestCase(CityLayer.Ground)]
    [TestCase(CityLayer.Near)] [TestCase(CityLayer.Vegetation)] [TestCase(CityLayer.Collision)]
    public void AllRadiusFacesEdgesCornersAndFloatNeighborsMatchLegacy(CityLayer layer)
    {
        var tile = new CityTileRecord { layer = layer, cityBounds = new Bounds(Vector3.zero, Vector3.zero) };
        object state = State(tile);
        foreach (float radius in new[] { CityStreamer.VisibleRadius(layer), CityStreamer.VisibleExitRadius(layer), CityStreamer.LoadRadius(layer), CityStreamer.ReleaseRadius(layer) })
        {
            if (float.IsPositiveInfinity(radius)) { Compare(state, Vector3.one * 1e30f, Vector3.one * 1e30f, false, true, true); continue; }
            foreach (float distance in new[] { Adjacent(radius, -1), radius, Adjacent(radius, 1), radius - .009f, radius + .009f, radius + .011f })
            foreach (Vector3 direction in new[] { Vector3.right, new Vector3(3, 4, 0).normalized, new Vector3(3, 4, 12).normalized })
            foreach (bool hasHandle in new[] { false, true })
            foreach (bool visible in new[] { false, true })
            foreach (bool collision in new[] { false, true })
            {
                Compare(state, direction * distance, direction * (distance + 200), hasHandle, visible, collision);
                Compare(state, direction * (distance + 200), direction * distance, hasHandle, visible, collision);
            }
        }
    }

    [Test]
    public void NonFiniteLargeDegenerateAndNegativeBoundsRetainLegacyDecisions()
    {
        var tile = new CityTileRecord(); object state = State(tile);
        var bounds = new[] { new Bounds(Vector3.zero, Vector3.zero), new Bounds(Vector3.one * 1e30f, Vector3.one * 1e25f),
            new Bounds(new Vector3(float.NaN, 0, 0), Vector3.one), new Bounds(new Vector3(float.PositiveInfinity, 0, 0), Vector3.one),
            new Bounds(Vector3.zero, new Vector3(float.PositiveInfinity, 0, 0)), new Bounds(Vector3.one, -Vector3.one) };
        var positions = new[] { Vector3.zero, Vector3.one * 1e30f, -Vector3.one * 1e30f, Vector3.one * float.MaxValue,
            new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0), new Vector3(float.NegativeInfinity, 0, 0) };
        foreach (Bounds b in bounds)
        foreach (Vector3 position in positions)
        foreach (Vector3 predicted in positions)
        foreach (CityLayer layer in new[] { CityLayer.Skyline, CityLayer.Core, CityLayer.Near, CityLayer.Collision, (CityLayer)(-1), (CityLayer)99 })
        foreach (bool hasHandle in new[] { false, true })
        foreach (bool visible in new[] { false, true })
        {
            tile.layer = layer; tile.cityBounds = b;
            Compare(state, position, predicted, hasHandle, visible, true);
            if (layer == CityLayer.Collision) Compare(state, position, predicted, hasHandle, visible, false);
        }
    }

    private static IList List(CityStreamer streamer, string name) => (IList)typeof(CityStreamer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streamer);
    private static string[] RequestIds(CityStreamer streamer)
    {
        IList requests = List(streamer, "requests"); var result = new string[requests.Count];
        for (int i = 0; i < requests.Count; i++) result[i] = ((CityTileRecord)Tile.GetValue(requests[i])).id;
        return result;
    }
    private struct Request { public string id; public float priority; }

    [Test]
    public void ActualEvaluationPreservesRequestOrderTiesPredictionAndLiveCatalogEdits()
    {
        var host = new GameObject("evaluation equivalence"); var observer = new GameObject("evaluation observer");
        var catalog = ScriptableObject.CreateInstance<CityCatalog>();
        try
        {
            var flight = observer.AddComponent<FreeFlightController>(); flight.enabled = false;
            var streamer = host.AddComponent<CityStreamer>(); streamer.enabled = false; streamer.flight = flight; streamer.catalog = catalog;
            catalog.tiles = new CityTileRecord[96];
            for (int i = 0; i < catalog.tiles.Length; i++) catalog.tiles[i] = new CityTileRecord { id = i.ToString(), address = i.ToString(),
                layer = (CityLayer)(i % 6), cityBounds = new Bounds(new Vector3((i % 4 - 2) * 200, 0, (i % 8 - 4) * 100), Vector3.one * 20) };
            streamer.Initialize(); IList all = List(streamer, "all");
            for (int i = 0; i < all.Count; i++) { HasHandle.SetValue(all[i], i % 5 == 0); Loading.SetValue(all[i], true); }
            for (int step = 0; step < 8; step++)
            {
                streamer.CollisionProxiesEnabled = step % 2 == 0;
                Vector3 position = new Vector3(step * 120 - 420, step * 3, step * 80 - 350), velocity = new Vector3(100, -10, -30);
                flight.SetPose(position, Quaternion.identity);
                typeof(FreeFlightController).GetField("<Velocity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(flight, velocity);
                if (step == 2) catalog.tiles[1].cityBounds = new Bounds(Vector3.one * 10000, Vector3.zero);
                if (step == 3) catalog.tiles[1].layer = CityLayer.Skyline;
                var expected = new Decision[all.Count]; var requests = new List<Request>();
                for (int i = 0; i < all.Count; i++)
                {
                    expected[i] = Legacy(catalog.tiles[i], position, position + velocity * 2, (bool)HasHandle.GetValue(all[i]), (bool)Visible.GetValue(all[i]), streamer.CollisionProxiesEnabled);
                    if (expected[i].wanted && !(bool)HasHandle.GetValue(all[i])) requests.Add(new Request { id = catalog.tiles[i].id, priority = expected[i].priority });
                }
                requests.Sort((a, b) => a.priority.CompareTo(b.priority));
                Evaluate.Invoke(streamer, null);
                for (int i = 0; i < all.Count; i++)
                {
                    Assert.That((bool)Wanted.GetValue(all[i]), Is.EqualTo(expected[i].wanted));
                    Assert.That((bool)Visible.GetValue(all[i]), Is.EqualTo(expected[i].visible));
                    if (expected[i].wanted) Exact((float)Priority.GetValue(all[i]), expected[i].priority, "priority at step " + step);
                }
                Assert.That(RequestIds(streamer), Is.EqualTo(requests.ConvertAll(a => a.id).ToArray()), "Including ties at step " + step);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(observer); UnityEngine.Object.DestroyImmediate(catalog); }
    }

    [Test]
    public void ActualOriginRebasePreservesCityCoordinatesAndEvaluationDecisions()
    {
        var host = new GameObject("rebase evaluation"); var observer = new GameObject("rebase observer");
        var catalog = ScriptableObject.CreateInstance<CityCatalog>();
        try
        {
            var origin = host.AddComponent<FloatingOrigin>(); origin.enabled = false; origin.observer = observer.transform;
            var flight = observer.AddComponent<FreeFlightController>(); flight.enabled = false; flight.Origin = origin;
            var streamer = host.AddComponent<CityStreamer>(); streamer.enabled = false; streamer.catalog = catalog; streamer.flight = flight; streamer.origin = origin;
            catalog.tiles = new[] { new CityTileRecord { id = "near", address = "near", layer = CityLayer.Near, cityBounds = new Bounds(new Vector3(1500, 10, 3500), Vector3.one * 20) },
                new CityTileRecord { id = "core", address = "core", layer = CityLayer.Core, cityBounds = new Bounds(new Vector3(2000, 10, 3500), Vector3.one * 20) },
                new CityTileRecord { id = "skyline", address = "skyline", layer = CityLayer.Skyline, cityBounds = new Bounds(Vector3.one * 10000, Vector3.one) } };
            streamer.Initialize(); flight.SetPose(new Vector3(1510, 10, 3500), Quaternion.identity); Evaluate.Invoke(streamer, null);
            Vector3 before = flight.CityPosition; string[] requests = RequestIds(streamer); IList all = List(streamer, "all");
            var decisions = new Decision[all.Count];
            for (int i = 0; i < all.Count; i++) decisions[i] = new Decision { wanted = (bool)Wanted.GetValue(all[i]), visible = (bool)Visible.GetValue(all[i]), priority = (float)Priority.GetValue(all[i]) };
            typeof(FloatingOrigin).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(origin, null);
            Assert.That(origin.Offset, Is.Not.EqualTo(Vector3.zero)); Assert.That(flight.CityPosition, Is.EqualTo(before));
            Evaluate.Invoke(streamer, null); Assert.That(RequestIds(streamer), Is.EqualTo(requests));
            for (int i = 0; i < all.Count; i++)
            {
                Assert.That((bool)Wanted.GetValue(all[i]), Is.EqualTo(decisions[i].wanted)); Assert.That((bool)Visible.GetValue(all[i]), Is.EqualTo(decisions[i].visible));
                Exact((float)Priority.GetValue(all[i]), decisions[i].priority, "Rebase priority");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(observer); UnityEngine.Object.DestroyImmediate(catalog); }
    }
}
