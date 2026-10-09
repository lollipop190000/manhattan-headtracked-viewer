using System.Collections.Generic;
using System;
using System.IO;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class CityPolicyTests
{
    [Test]
    public void StreamingBoundsDistanceUsesNearestSurfaceRatherThanTileCenter()
    {
        var bounds = new Bounds(new Vector3(250, 50, 250), new Vector3(500, 100, 500));
        Assert.That(CityStreamer.DistanceToTile(bounds, new Vector3(501, 1, 250)), Is.EqualTo(1).Within(.0001));
        Assert.That(CityStreamer.DistanceToTile(bounds, bounds.center), Is.Zero);
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
