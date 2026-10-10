using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class BenchmarkSpeedLifecycleTests
{
    [UnityTest]
    public IEnumerator Benchmark100ExecutesWithoutChangingSavedOrdinarySpeed()
    {
        Assert.That(ManhattanBootstrap.HasArgument("--no-tracker"), Is.True);
        string prior = Environment.GetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY");
        string directory = Path.Combine(Path.GetTempPath(), "benchmark-speed-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY", directory);
        GameObject root = null;
        try
        {
            Assert.That(ViewerPersistence.Save(ViewerPersistence.PreferencesFile, new ViewerPreferences { movementSpeed = 17 }), Is.True);
            string path = Path.Combine(directory, ViewerPersistence.PreferencesFile);
            byte[] saved = File.ReadAllBytes(path);
            root = new GameObject("Synthetic benchmark speed"); var app = root.AddComponent<ManhattanBootstrap>();
            var priorInput = app.Flight.InputSource;
            app.Benchmark.StartBenchmark(100, 1200, Path.Combine(directory, "run"), false);
            Assert.That(app.Flight.Speed, Is.EqualTo(17));
            Assert.That(app.Preferences.movementSpeed, Is.EqualTo(17));
            Assert.That(((BenchmarkRouteInput)app.Flight.InputSource).Speed, Is.EqualTo(100));
            typeof(ViewerBenchmark).GetField("warmupUntil", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app.Benchmark, Time.unscaledTimeAsDouble);
            yield return null;
            Vector3 start = app.Flight.CityPosition; float started = Time.unscaledTime;
            for (int i = 0; i < 5; i++) yield return null;
            float elapsed = Time.unscaledTime - started;
            Assert.That(Vector3.Distance(start, app.Flight.CityPosition) / elapsed, Is.EqualTo(100).Within(1));
            BenchmarkReport report = app.Benchmark.Finish();
            Assert.That(report.requestedSpeed, Is.EqualTo(100));
            Assert.That(report.actualRouteSpeed, Is.EqualTo(100));
            Assert.That(report.speed, Is.EqualTo(100));
            Assert.That(app.Flight.InputSource, Is.SameAs(priorInput));
            Assert.That(app.Flight.Speed, Is.EqualTo(17));
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(path));
            Assert.That(File.ReadAllText(Path.Combine(directory, "run", "frames.csv")), Does.Contain("city_x,city_y,city_z,route_cycles,route_elapsed_seconds"));
            // Finish requests a generic scene screenshot; allow its write to complete before fixture cleanup.
            yield return new WaitForEndOfFrame(); yield return null;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            Environment.SetEnvironmentVariable("HEADTRACKED_CALIBRATION_DIRECTORY", prior);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
