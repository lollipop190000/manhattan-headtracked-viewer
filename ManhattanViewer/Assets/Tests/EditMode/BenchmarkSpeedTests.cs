using System.Collections.Generic;
using System.Globalization;
using Manhattan.Viewer;
using NUnit.Framework;
using UnityEngine;

public sealed class BenchmarkSpeedTests
{
    [TestCase(-90.00001f)] [TestCase(-90f)] [TestCase(-89.99999f)]
    [TestCase(-85f)] [TestCase(-40f)] [TestCase(0f)] [TestCase(40f)] [TestCase(85f)]
    [TestCase(89.99999f)] [TestCase(90f)] [TestCase(90.00001f)]
    public void GroundPlaneMovementRetainsYawSpeedAndVerticalSeparation(float pitch)
    {
        foreach (float yaw in new[] { -720f, -181f, -90f, 0f, 37f, 90f, 179f, 540f })
        {
            Quaternion view = Quaternion.Euler(pitch, yaw, 0f);
            Quaternion azimuth = Quaternion.Euler(0f, yaw, 0f);
            foreach (Vector3 input in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
                Vector3.up, Vector3.down, Vector3.ClampMagnitude(new Vector3(1, 0, 1), 1),
                Vector3.ClampMagnitude(new Vector3(1, 1, 1), 1), new Vector3(.2f, -.3f, .4f) })
            {
                Vector3 actual = FreeFlightController.GroundPlaneMovement(input, view, yaw);
                Vector3 expected = azimuth * new Vector3(input.x, 0, input.z) + Vector3.up * input.y;
                Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0002f), $"pitch={pitch}, yaw={yaw}, input={input}");
                Assert.That(actual.y, Is.EqualTo(input.y).Within(.000001f));
                Assert.That(actual.magnitude, Is.EqualTo(input.magnitude).Within(.00001f));
            }
        }
    }

    [Test]
    public void GroundPlaneMovementIgnoresCameraRollWithoutChangingAnalogMagnitude()
    {
        Quaternion yaw = Quaternion.Euler(0, 123, 0);
        Quaternion view = yaw * Quaternion.Euler(80, 0, 0) * Quaternion.Euler(0, 0, 65);
        var input = new Vector3(.2f, .35f, -.5f);
        Vector3 expected = yaw * new Vector3(input.x, 0, input.z) + Vector3.up * input.y;
        Assert.That(Vector3.Distance(FreeFlightController.GroundPlaneMovement(input, view, 123), expected), Is.LessThan(.0001f));
    }

    [TestCase(20d)] [TestCase(20000d)] [TestCase(100000d)]
    public void CsvClockPreserves100MetresPerSecondAfterLongUptime(double warmupUntil)
    {
        var measured = new List<float>(); double previousSeconds = 0;
        for (int i = 0; i <= 120; i++)
        {
            double elapsed = BenchmarkStatistics.MeasuredSeconds(warmupUntil + i / 60d, warmupUntil);
            // Exercise the production F6 CSV representation, not just unformatted doubles.
            double seconds = double.Parse(elapsed.ToString("F6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (i > 0) measured.Add((float)((100d / 60d) / (seconds - previousSeconds)));
            previousSeconds = seconds;
        }
        Assert.That(BenchmarkStatistics.Percentile(measured, .1f), Is.InRange(95f, 105f));
        Assert.That(BenchmarkStatistics.Percentile(measured, .9f), Is.InRange(95f, 105f));
    }

    [TestCase(10f)] [TestCase(50f)] [TestCase(100f)]
    public void SelectedRouteSpeedIsExecutedAndDurationUsesThatSpeed(float speed)
    {
        var route = new BenchmarkRouteInput(new[] { Vector3.zero, Vector3.right * 2000 }, speed);
        Assert.That(route.Speed, Is.EqualTo(speed));
        Assert.That(route.Duration, Is.EqualTo(2000 / speed).Within(.0001f));
        Assert.That(route.Sample(1).cityPosition.x, Is.EqualTo(speed).Within(.0001f));
        var partitioned = new BenchmarkRouteInput(new[] { Vector3.zero, Vector3.right * 2000 }, speed);
        MotionCommand command = default;
        for (int i = 0; i < 100; i++) command = partitioned.Sample(.01f);
        Assert.That(command.cityPosition.x, Is.EqualTo(speed).Within(.001f));
    }

    [TestCase(1f, 1f)] [TestCase(10f, 10f)] [TestCase(50f, 50f)] [TestCase(100f, 100f)]
    [TestCase(150f, 100f)] [TestCase(-1f, 1f)]
    public void FlightAndRouteShareLimitsWithoutChangingDefaults(float request, float expected)
    {
        var host = new GameObject("speed limits");
        try
        {
            var flight = host.AddComponent<FreeFlightController>();
            Assert.That(flight.Speed, Is.EqualTo(10));
            Assert.That(new ViewerPreferences().movementSpeed, Is.EqualTo(10));
            flight.Speed = request;
            Assert.That(flight.Speed, Is.EqualTo(expected));
            Assert.That(new BenchmarkRouteInput(new[] { Vector3.zero, Vector3.right }, request).Speed, Is.EqualTo(expected));
        }
        finally { Object.DestroyImmediate(host); }
    }

    [TestCase(2f, true)] [TestCase(2.001f, false)] [TestCase(2.319f, false)]
    public void RuntimeStreamingTargetMatchesAnalyzerBoundary(float streaming, bool expected)
    {
        var report = PassingReport(); report.p95StreamingMs = streaming;
        Assert.That(BenchmarkStatistics.RuntimeTargetsPassed(report), Is.EqualTo(expected));
    }

    [Test]
    public void RuntimeDoesNotPassClampedRequestOrNonFiniteStreaming()
    {
        var report = PassingReport(); report.actualRouteSpeed = 50;
        Assert.That(BenchmarkStatistics.RuntimeTargetsPassed(report), Is.False);
        report = PassingReport(); report.p95StreamingMs = float.NaN;
        Assert.That(BenchmarkStatistics.RuntimeTargetsPassed(report), Is.False);
    }

    private static BenchmarkReport PassingReport() => new BenchmarkReport
    {
        frameCount = 100, requestedSpeed = 100, actualRouteSpeed = 100, speed = 100,
        p95FrameMs = 16, p99FrameMs = 20, p95StreamingMs = 1, trackingFraction = 1,
        maximumProcessWorkingSetBytes = 1024 * 1024, graphicsMemoryCounterAvailable = true,
        maximumGpuBytes = 1024 * 1024
    };
}
