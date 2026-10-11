using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HeadTracked.Display;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Manhattan.Viewer
{
    [Serializable]
    public sealed class ViewerBuildIdentity
    {
        public string viewerRevision, technologyRevision, sourceManifestSha256, buildUtc;
        public bool dirtyWorkingTree;
    }

    [Serializable]
    public sealed class BenchmarkReport
    {
        public string utcTime, unityVersion, gpu, cpu, sourceVersion, routeRegion, reportKind = "measured-runtime";
        public int width, height, frameCount, streamingFailures;
        public int renderWidth, renderHeight, displayModeWidth, displayModeHeight, completedMeasuredCircuits;
        public string fullscreenMode;
        public float routeDistanceMeters, routeDurationSeconds, measuredRouteCoverageFraction;
        public bool translationConstrained;
        public string viewerRevision, technologyRevision, sourceManifestSha256, buildUtc;
        public bool dirtyWorkingTree, buildIdentityAvailable;
        public float speed, durationSeconds, p95FrameMs, p99FrameMs, maximumFrameMs, p95StreamingMs, p95CollisionMs, trackingFraction;
        // speed remains the legacy alias for the applied route speed, never the requested speed.
        public float requestedSpeed, actualRouteSpeed;
        public long maximumCpuBytes, maximumGpuBytes, maximumEstimatedTileBytes, endingCpuBytes, maximumProcessWorkingSetBytes;
        public int framesOver50Ms;
        public bool graphicsMemoryCounterAvailable, collisionProxies, performanceTargetPassed;
        public string limitation;
    }

    public static class BenchmarkStatistics
    {
        public static double MeasuredSeconds(double clockSeconds, double warmupUntil) => Math.Max(0d, clockSeconds - warmupUntil);
        public static bool RuntimeTargetsPassed(BenchmarkReport report) => report.frameCount > 0 &&
            report.requestedSpeed >= FlightSpeedLimits.Minimum && report.requestedSpeed <= FlightSpeedLimits.Maximum &&
            report.requestedSpeed == report.actualRouteSpeed && report.p95FrameMs <= 16.7f && report.p99FrameMs <= 25f &&
            float.IsFinite(report.p95StreamingMs) && report.p95StreamingMs >= 0f && report.p95StreamingMs <= 2f &&
            report.framesOver50Ms == 0 && report.streamingFailures == 0 && report.trackingFraction >= .9f &&
            report.maximumProcessWorkingSetBytes <= 10L * 1024 * 1024 * 1024 && report.graphicsMemoryCounterAvailable &&
            report.maximumGpuBytes <= 6L * 1024 * 1024 * 1024;

        public static float Percentile(List<float> values, float percentile)
        {
            if (values.Count == 0) return 0;
            var sorted = values.ToArray(); Array.Sort(sorted);
            return sorted[Mathf.Clamp(Mathf.CeilToInt(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
        }
    }

    [DefaultExecutionOrder(100)]
    public sealed class ViewerBenchmark : MonoBehaviour
    {
        public FreeFlightController flight;
        public CityStreamer streamer;
        public HeadTrackedDisplay display;
        private BenchmarkRouteInput route;
        private IMotionInputSource previousInput;
        private readonly List<float> frames = new List<float>(), streaming = new List<float>(), collision = new List<float>();
        private StreamWriter csvWriter;
        private ProfilerRecorder graphicsMemory;
        private double startedAt, warmupUntil;
        private float duration, benchmarkSpeed, requestedSpeed;
        private string outputDirectory;
        private string benchmarkRegion;
        private bool quitWhenDone, recording;
        private int trackedFrames;
        private long maxCpu, maxGpu, maxTiles, maxProcess;
        private long processWorkingSet;
        private double nextMemorySample;
        private Vector3[] points;
        private int routeCycles, measuredCycleEvents;
        private float routeDuration, measuredStartRouteTime = -1f;
        public bool Recording => recording;
        public string LastReportPath { get; private set; }
        public float CurrentFrameMs { get; private set; }
        public long CpuMemoryBytes => Profiler.GetTotalAllocatedMemoryLong();
        public long GpuMemoryBytes => graphicsMemory.Valid ? graphicsMemory.LastValue : 0;

        private void OnEnable() => graphicsMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory", 1);
        private void OnDisable() { CloseCsv(); if (graphicsMemory.Valid) graphicsMemory.Dispose(); }

        private void CloseCsv()
        {
            var writer = csvWriter; csvWriter = null;
            if (writer != null) writer.Dispose();
        }

        public void StartBenchmark(float speed, float seconds, string directory, bool collisionProxies, bool quit = false, string region = "Midtown")
        {
            if (recording) return;
            previousInput = flight.InputSource;
            if (!float.IsFinite(speed)) throw new ArgumentOutOfRangeException(nameof(speed), "Benchmark speed must be finite.");
            requestedSpeed = speed; benchmarkSpeed = FlightSpeedLimits.Clamp(speed); duration = Mathf.Clamp(seconds, 1f, 7200f);
            outputDirectory = string.IsNullOrEmpty(directory) ? Path.Combine(Application.persistentDataPath, "Benchmarks", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")) : Path.GetFullPath(directory);
            // Retained CSV strings otherwise create an artificial rising memory
            // curve during the very test intended to detect residency leaks.
            Directory.CreateDirectory(outputDirectory); CloseCsv();
            csvWriter = new StreamWriter(new FileStream(Path.Combine(outputDirectory, "frames.csv"), FileMode.CreateNew,
                FileAccess.Write, FileShare.Read, 65536), new UTF8Encoding(false), 65536);
            csvWriter.WriteLine("seconds,frame_ms,streaming_ms,collision_ms,unity_cpu_bytes,gpu_bytes,tile_bytes,loaded_tiles,pending,tracking,process_working_set_bytes,city_x,city_y,city_z,route_cycles,route_elapsed_seconds");
            // Reserve numeric samples before warmup, retaining their full timing
            // precision without growing list backing arrays during measurements.
            int capacity = Mathf.CeilToInt(duration * Mathf.Max(120f, (float)Screen.currentResolution.refreshRateRatio.value * 2f)) + 4;
            frames.Clear(); streaming.Clear(); collision.Clear();
            if (frames.Capacity < capacity) frames.Capacity = capacity;
            if (streaming.Capacity < capacity) streaming.Capacity = capacity;
            if (collision.Capacity < capacity) collision.Capacity = capacity;
            benchmarkRegion = NormalizeRegion(region);
            points = RegionRoute(benchmarkRegion, flight.CityPosition);
            flight.SetPose(points[0], Quaternion.LookRotation(points[1] - points[0]));
            route = new BenchmarkRouteInput(points, benchmarkSpeed); flight.InputSource = route;
            routeDuration = route.Duration; routeCycles = measuredCycleEvents = 0; measuredStartRouteTime = -1f;
            streamer.CollisionProxiesEnabled = collisionProxies;
            trackedFrames = 0; maxCpu = maxGpu = maxTiles = maxProcess = processWorkingSet = 0; nextMemorySample = 0f;
            startedAt = Time.unscaledTimeAsDouble; warmupUntil = startedAt + 15d; quitWhenDone = quit; recording = true;
        }

        public static Vector3[] DefaultRoute(Vector3 initial)
        {
            // glTFast imports source east/up/north into Unity -east/up/-north.
            return new[] { initial, new Vector3(1800, 2, 6200), new Vector3(3400, 25, 8900),
                new Vector3(1200, 2, 3500), new Vector3(-900, 20, -1000), new Vector3(-2600, 25, -5000),
                new Vector3(1200, 400, 3500), initial };
        }

        private static string NormalizeRegion(string region)
        {
            foreach (string allowed in new[] { "Midtown", "Downtown", "Park", "Uptown", "Air", "Full" })
                if (string.Equals(region, allowed, StringComparison.OrdinalIgnoreCase)) return allowed;
            return "Midtown";
        }
        public static Vector3[] RegionRoute(string region, Vector3 initial)
        {
            if (string.Equals(region, "Full", StringComparison.OrdinalIgnoreCase)) return DefaultRoute(initial);
            Vector3 center;
            switch (NormalizeRegion(region))
            {
                case "Downtown": center = new Vector3(3400, 2, 8900); break;
                case "Park": center = new Vector3(-400, 20, -500); break;
                case "Uptown": center = new Vector3(-2400, 2, -4400); break;
                case "Air": center = new Vector3(1225, 400, 3585); break;
                default: center = new Vector3(1225, 1.85f, 3585); break;
            }
            return new[] { center, center + new Vector3(400, 0, -400), center + new Vector3(-200, 0, -800),
                center + new Vector3(-600, 0, -200), center };
        }

        private void Update()
        {
            CurrentFrameMs = Time.unscaledDeltaTime * 1000f;
            if (!recording) return;
            if (route.Complete)
            {
                routeCycles++;
                if (measuredStartRouteTime >= 0f) measuredCycleEvents++;
                route = new BenchmarkRouteInput(points, benchmarkSpeed); flight.InputSource = route;
            }
            if (Time.unscaledTimeAsDouble < warmupUntil) return;
            if (measuredStartRouteTime < 0f) measuredStartRouteTime = routeCycles * routeDuration + route.Elapsed;
            float collisionMs = 0f;
            if (streamer.CollisionProxiesEnabled)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                Physics.SyncTransforms();
                Vector3 p = flight.transform.position;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 direction = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                    Physics.Raycast(p, direction, out _, 100f, 1 << 30, QueryTriggerInteraction.Ignore);
                    Physics.SphereCast(p, .3f, direction, out _, 4f, 1 << 30, QueryTriggerInteraction.Ignore);
                }
                collisionMs = (float)timer.Elapsed.TotalMilliseconds;
            }
            frames.Add(CurrentFrameMs); streaming.Add(streamer.FrameWorkMilliseconds); collision.Add(collisionMs);
            long cpu = CpuMemoryBytes, gpu = GpuMemoryBytes;
            if (Time.unscaledTimeAsDouble >= nextMemorySample)
            {
                nextMemorySample = Time.unscaledTimeAsDouble + 1d;
                ProcessMemoryTelemetry.TryWorkingSetBytes(out processWorkingSet);
            }
            maxCpu = Math.Max(maxCpu, cpu); maxGpu = Math.Max(maxGpu, gpu); maxTiles = Math.Max(maxTiles, streamer.EstimatedResidentBytes);
            maxProcess = Math.Max(maxProcess, processWorkingSet);
            if (display.IsTracking) trackedFrames++;
            Vector3 cityPosition = flight.CityPosition;
            try { csvWriter.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F4},{2:F4},{3:F4},{4},{5},{6},{7},{8},{9},{10},{11:F6},{12:F6},{13:F6},{14},{15:F6}",
                BenchmarkStatistics.MeasuredSeconds(Time.unscaledTimeAsDouble, warmupUntil), CurrentFrameMs, streamer.FrameWorkMilliseconds, collisionMs,
                cpu, gpu, streamer.EstimatedResidentBytes, streamer.LoadedTiles, streamer.PendingLoads, display.IsTracking ? 1 : 0, processWorkingSet,
                cityPosition.x, cityPosition.y, cityPosition.z, routeCycles, route.Elapsed)); }
            catch (IOException error)
            {
                recording = false; flight.InputSource = previousInput; CloseCsv();
                Debug.LogError("Benchmark frame recording failed: " + error.Message);
                if (quitWhenDone) Application.Quit(1); return;
            }
            if (BenchmarkStatistics.MeasuredSeconds(Time.unscaledTimeAsDouble, warmupUntil) >= duration) Finish();
        }

        public BenchmarkReport Finish(bool captureScreenshot = true)
        {
            if (!recording) return null;
            recording = false; flight.InputSource = previousInput;
            CloseCsv();
            var camera = display.GetComponent<Camera>();
            float routeDistance = 0f;
            for (int i = 1; i < points.Length; i++) routeDistance += Vector3.Distance(points[i - 1], points[i]);
            var identityText = Resources.Load<TextAsset>("ViewerBuildIdentity");
            ViewerBuildIdentity identity = null;
            if (identityText != null)
            {
                try { identity = JsonUtility.FromJson<ViewerBuildIdentity>(identityText.text); }
                catch (ArgumentException error) { Debug.LogWarning("Invalid embedded build identity; report will mark it unavailable: " + error.Message); }
            }
            var report = new BenchmarkReport
            {
                utcTime = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName, cpu = SystemInfo.processorType,
                sourceVersion = streamer.catalog != null ? streamer.catalog.sourceVersion : "missing",
                routeRegion = benchmarkRegion,
                width = Screen.width, height = Screen.height, frameCount = frames.Count,
                renderWidth = camera.scaledPixelWidth, renderHeight = camera.scaledPixelHeight,
                displayModeWidth = Screen.currentResolution.width, displayModeHeight = Screen.currentResolution.height,
                fullscreenMode = Screen.fullScreenMode.ToString(),
                routeDistanceMeters = routeDistance, routeDurationSeconds = routeDuration,
                completedMeasuredCircuits = Mathf.Max(0, measuredCycleEvents - 1),
                measuredRouteCoverageFraction = flight.TranslationConstraint != null || measuredStartRouteTime < 0f ? -1f :
                    Mathf.Clamp01((routeCycles * routeDuration + route.Elapsed - measuredStartRouteTime) / Mathf.Max(.001f, routeDuration)),
                translationConstrained = flight.TranslationConstraint != null,
                viewerRevision = identity?.viewerRevision, technologyRevision = identity?.technologyRevision,
                sourceManifestSha256 = identity?.sourceManifestSha256, buildUtc = identity?.buildUtc,
                dirtyWorkingTree = identity != null && identity.dirtyWorkingTree, buildIdentityAvailable = identity != null,
                speed = route.Speed, requestedSpeed = requestedSpeed, actualRouteSpeed = route.Speed,
                durationSeconds = (float)BenchmarkStatistics.MeasuredSeconds(Time.unscaledTimeAsDouble, warmupUntil),
                p95FrameMs = BenchmarkStatistics.Percentile(frames, .95f), p99FrameMs = BenchmarkStatistics.Percentile(frames, .99f),
                maximumFrameMs = BenchmarkStatistics.Percentile(frames, 1f), p95StreamingMs = BenchmarkStatistics.Percentile(streaming, .95f),
                p95CollisionMs = BenchmarkStatistics.Percentile(collision, .95f),
                trackingFraction = frames.Count > 0 ? (float)trackedFrames / frames.Count : 0f,
                maximumCpuBytes = maxCpu, maximumGpuBytes = maxGpu, maximumEstimatedTileBytes = maxTiles, endingCpuBytes = CpuMemoryBytes,
                maximumProcessWorkingSetBytes = maxProcess,
                graphicsMemoryCounterAvailable = graphicsMemory.Valid && maxGpu > 0, collisionProxies = streamer.CollisionProxiesEnabled,
                streamingFailures = streamer.Failures,
                limitation = "GPU counter is Unity graphics allocations, not total dedicated VRAM. CPU and process working-set counters are reported separately. Frame timings include vsync. Human head movements and visual artifacts require visual review."
            };
            foreach (float frame in frames) if (frame >= 50) report.framesOver50Ms++;
            report.performanceTargetPassed = BenchmarkStatistics.RuntimeTargetsPassed(report);
            Directory.CreateDirectory(outputDirectory);
            LastReportPath = Path.Combine(outputDirectory, "benchmark.json");
            File.WriteAllText(LastReportPath, JsonUtility.ToJson(report, true));
            if (captureScreenshot) ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, "screenshot.png"));
            Debug.Log("Benchmark saved: " + LastReportPath);
            if (quitWhenDone) Invoke(nameof(QuitAfterCapture), 1f);
            return report;
        }
        private void QuitAfterCapture() => Application.Quit();
    }
}
