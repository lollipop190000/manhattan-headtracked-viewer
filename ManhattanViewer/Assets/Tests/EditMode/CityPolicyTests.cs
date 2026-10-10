using System.Collections.Generic;
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
