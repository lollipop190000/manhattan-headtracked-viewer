import json
import hashlib
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from analyze_benchmarks import aggregate, analyze_run, combined_private_memory, driver_gpu, external_process_memory, memory_trend, percentile, markdown


def measured_fixture(repository, name, duration=160, proxies=False, tracking=1, completed=2, render_width=1920, dirty=False):
    (repository / "assets").mkdir(exist_ok=True)
    manifest = repository / "assets/source-manifest.json"
    manifest.write_text(json.dumps(dict(sourceVersion="fixture", packs=[])), encoding="utf-8")
    (repository / "tools").mkdir(exist_ok=True)
    (repository / "tools/technology-version.json").write_text(json.dumps(dict(revision="1" * 40)), encoding="utf-8")
    directory = repository / name
    directory.mkdir()
    circuit = 52.807313
    report = dict(routeRegion="Midtown", speed=50, durationSeconds=duration, utcTime="2026-10-10T00:30:00Z",
                  width=1920, height=1200, renderWidth=render_width, renderHeight=1200, fullscreenMode="ExclusiveFullScreen",
                  frameCount=6, collisionProxies=proxies, trackingFraction=tracking, streamingFailures=0,
                  completedMeasuredCircuits=completed, measuredRouteCoverageFraction=1, routeDurationSeconds=circuit,
                  gpu="RTX4070", cpu="CPU", sourceVersion="fixture", buildIdentityAvailable=True, dirtyWorkingTree=dirty,
                  viewerRevision="2" * 40, technologyRevision="1" * 40, sourceManifestSha256=hashlib.sha256(manifest.read_bytes()).hexdigest())
    (directory / "benchmark.json").write_text(json.dumps(report), encoding="utf-8")
    lines = ["seconds,frame_ms,streaming_ms,collision_ms,tracking,process_working_set_bytes,route_cycles,route_elapsed_seconds"]
    for i in range(6):
        seconds = duration * i / 5
        route_time = seconds + 15
        lines.append(f"{seconds},16,1,{.5 if proxies else 0},{tracking},1073741824,{int(route_time // circuit)},{route_time % circuit}")
    (directory / "frames.csv").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (directory / "gpu.csv").write_text("sampleUtc,gpuName,memoryUsedMiB\n2026-10-10T00:29:30Z,RTX4070,3000\n", encoding="utf-8")
    return directory


class BenchmarkAnalysisTests(unittest.TestCase):
    def test_nearest_rank_matches_runtime_tail_definition(self):
        self.assertEqual(95, percentile(list(range(1, 101)), .95))
        self.assertEqual(99, percentile(list(range(1, 101)), .99))
        self.assertIsNone(percentile([], .95))

    def test_missing_values_are_pending_instead_of_false_zero_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / "benchmark.json").write_text(json.dumps(dict(routeRegion="Midtown", speed=10, frameCount=10)), encoding="utf-8")
            run = analyze_run(path)
            self.assertIsNone(run["checks"]["driverGpuMemory"])
            self.assertIsNone(run["checks"]["processMemory"])
            self.assertIsNone(run["p95FrameMs"])
            self.assertEqual("pending", run["acceptance"])
            self.assertIsNone(run["memoryTrend"]["slopeBytesPerSecond"])

    def test_csv_has_priority_and_ignores_nonfinite_rows(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)
            (path / "benchmark.json").write_text(json.dumps(dict(frameCount=100, p95FrameMs=1, routeRegion="Midtown", speed=50)), encoding="utf-8")
            (path / "frames.csv").write_text("seconds,frame_ms,tracking\n0,16,1\n1,52,1\n2,nan,1\n", encoding="utf-8")
            run = analyze_run(path)
            self.assertEqual(52, run["p95FrameMs"])
            self.assertEqual(1, run["framesOver50Ms"])
            self.assertEqual(2, run["frameCount"])
            self.assertTrue(any("malformed" in warning for warning in run["warnings"]))

    def test_driver_filters_selected_gpu_and_excludes_warmup(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "gpu.csv"
            path.write_text("sampleUtc,gpuName,memoryUsedMiB\n"
                            "2026-10-10T00:00:05Z,RTX4070,7000\n"
                            "2026-10-10T00:00:15Z,RTX4070,3000\n"
                            "2026-10-10T00:00:15Z,Other GPU,9000\n"
                            "2026-10-10T00:00:16Z,RTX4070,N/A\n", encoding="utf-8")
            result = driver_gpu(path, dict(gpu="RTX4070", utcTime="2026-10-10T00:00:20Z", durationSeconds=10))
            self.assertTrue(result["timeAligned"])
            self.assertEqual(1, result["sampleCount"])
            self.assertEqual(3000 * 1024 ** 2, result["peakBytes"])

    def test_external_process_counters_exclude_startup_and_analyze_one_pid(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "process.csv"
            path.write_text("sampleUtc,pid,workingSetBytes,privateMemoryBytes\n"
                            "2026-10-10T00:00:05Z,99,9000,9000\n"
                            "2026-10-10T00:00:10Z,42,1000,2000\n"
                            "2026-10-10T00:00:15Z,42,1100,2500\n"
                            "2026-10-10T00:00:20Z,42,1200,3000\n"
                            "2026-10-10T00:00:21Z,99,9000,9000\n", encoding="utf-8")
            result = external_process_memory(path, dict(routeRegion="Midtown", speed=50,
                                                       utcTime="2026-10-10T00:00:20Z", durationSeconds=10, routeDurationSeconds=1))
            self.assertTrue(result["timeAligned"])
            self.assertEqual(42, result["pid"])
            self.assertEqual(3, result["sampleCount"])
            self.assertEqual(1200, result["workingSetPeakBytes"])
            self.assertEqual(3000, result["privateCommitPeakBytes"])
            self.assertAlmostEqual(100, result["privateCommitTrend"]["slopeBytesPerSecond"])
            self.assertIn("20-minute", result["privateCommitTrend"]["assessment"])

    def test_external_unknown_counters_and_unaligned_samples_remain_unavailable(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "process.csv"
            report = dict(utcTime="2026-10-10T00:00:20Z", durationSeconds=10)
            path.write_text("sampleUtc,pid,workingSetBytes,privateMemoryBytes\n"
                            "2026-10-10T00:00:15Z,42,N/A,NaN\n"
                            "2026-10-10T00:00:16Z,42,0,-1\n", encoding="utf-8")
            result = external_process_memory(path, report)
            self.assertIsNone(result["workingSetPeakBytes"])
            self.assertIsNone(result["privateCommitPeakBytes"])
            self.assertEqual(0, result["privateCommitSampleCount"])
            path.write_text("sampleUtc,pid,workingSetBytes,privateMemoryBytes\n2026-10-10T00:00:05Z,42,9000,9000\n", encoding="utf-8")
            for incomplete in (report, dict(durationSeconds=10), dict(utcTime=report["utcTime"])):
                with self.subTest(report=incomplete):
                    result = external_process_memory(path, incomplete)
                    self.assertFalse(result["timeAligned"])
                    self.assertIsNone(result["privateCommitPeakBytes"])

    def test_external_multiple_pids_cannot_form_one_memory_trend(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "process.csv"
            path.write_text("sampleUtc,pid,workingSetBytes,privateMemoryBytes\n"
                            "2026-10-10T00:00:15Z,42,1000,2000\n"
                            "2026-10-10T00:00:16Z,43,1100,2500\n", encoding="utf-8")
            result = external_process_memory(path, dict(utcTime="2026-10-10T00:00:20Z", durationSeconds=10))
            self.assertFalse(result["timeAligned"])
            self.assertIsNone(result["privateCommitPeakBytes"])
            self.assertTrue(any("Multiple player PIDs" in warning for warning in result["warnings"]))

    def test_external_private_commit_does_not_replace_native_working_set(self):
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            path = measured_fixture(repository, "with-process")
            (path / "process.csv").write_text("sampleUtc,pid,workingSetBytes,privateMemoryBytes\n"
                                               "2026-10-10T00:29:30Z,42,4294967296,8589934592\n", encoding="utf-8")
            result = aggregate([path], repository)
            run = result["runs"][0]
            self.assertEqual(1073741824, run["workingSetPeakBytes"])
            self.assertEqual(4294967296, run["externalProcess"]["workingSetPeakBytes"])
            self.assertEqual(8589934592, run["externalProcess"]["privateCommitPeakBytes"])
            self.assertTrue(run["checks"]["processMemory"])
            self.assertIn("Secondary external process memory", markdown(result))

    def test_verified_tracker_tree_and_separate_peaks_form_conservative_bound(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            report = dict(utcTime="2026-10-10T00:00:20Z", durationSeconds=20, routeRegion="Midtown", speed=50, routeDurationSeconds=1)
            player_path, tracker_path = folder / "process.csv", folder / "tracker-process.csv"
            player_path.write_text("sampleUtc,pid,workingSetBytes,privateMemoryBytes\n2026-10-10T00:00:04Z,7,1000,8000\n", encoding="utf-8")
            tracker_path.write_text("sampleUtc,pid,workingSetBytes,privateMemoryBytes,scopePids,treeCoverageVerified,treeDiscoveryUtc,treeMembershipVerifiedPerSample\n"
                                    "2026-10-10T00:00:06Z,42,2000,3000,42;43,True,2026-10-10T00:00:05Z,True\n"
                                    "2026-10-10T00:00:11Z,42,2500,3500,42;43,True,2026-10-10T00:00:10Z,True\n"
                                    "2026-10-10T00:00:16Z,42,3000,4000,42;43,True,2026-10-10T00:00:15Z,True\n", encoding="utf-8")
            player = external_process_memory(player_path, report)
            tracker = external_process_memory(tracker_path, report, "tracker", require_tree=True)
            self.assertTrue(tracker["treeCoverageVerified"])
            self.assertEqual([42, 43], tracker["scopePids"])
            self.assertEqual(4000, tracker["privateCommitPeakBytes"])
            bound = combined_private_memory(player, tracker, True)
            self.assertEqual(12000, bound["peakUpperBoundBytes"], "Different sample timestamps are valid for a separate-peak upper bound.")
            self.assertIn("not an actual concurrent peak", bound["scope"])
            self.assertAlmostEqual(100, tracker["privateCommitTrend"]["slopeBytesPerSecond"])

    def test_tracker_tree_unknown_identity_or_counter_keeps_combined_bound_unavailable(self):
        player = dict(timeAligned=True, sampleCount=2, privateCommitSampleCount=2, privateCommitPeakBytes=8000)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "tracker-process.csv"
            report = dict(utcTime="2026-10-10T00:00:20Z", durationSeconds=20)
            header = "sampleUtc,pid,workingSetBytes,privateMemoryBytes,scopePids,treeCoverageVerified,treeDiscoveryUtc,treeMembershipVerifiedPerSample\n"
            for counter, members, verified, discovery in (("4000", "42;43", "False", "00:00:05"),
                                                          ("N/A", "42;43", "True", "00:00:05"),
                                                          ("4000", "43", "True", "00:00:05"),
                                                          ("4000", "42;43", "True", "00:00:30")):
                with self.subTest(counter=counter, members=members, verified=verified, discovery=discovery):
                    path.write_text(header + f"2026-10-10T00:00:06Z,42,3000,{counter},{members},{verified},2026-10-10T{discovery}Z,True\n", encoding="utf-8")
                    tracker = external_process_memory(path, report, "tracker", require_tree=True)
                    self.assertIsNone(combined_private_memory(player, tracker, True)["peakUpperBoundBytes"])

    def test_cached_tree_traces_without_current_membership_proof_are_unavailable(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "tracker-process.csv"
            report = dict(utcTime="2026-10-10T00:00:20Z", durationSeconds=20)
            header = "sampleUtc,pid,workingSetBytes,privateMemoryBytes,scopePids,treeCoverageVerified,treeDiscoveryUtc,treeMembershipVerifiedPerSample\n"
            for marker, discovery in (("", "00:00:05"), ("False", "00:00:05"), ("True", "00:00:01")):
                with self.subTest(marker=marker, discovery=discovery):
                    path.write_text(header + f"2026-10-10T00:00:06Z,42,3000,4000,42;43,True,2026-10-10T{discovery}Z,{marker}\n", encoding="utf-8")
                    tracker = external_process_memory(path, report, "tracker", require_tree=True)
                    self.assertFalse(tracker["treeCoverageVerified"])
                    self.assertIsNone(tracker["privateCommitPeakBytes"])

    def test_no_tracker_is_explicitly_disabled_and_unknown_launch_scope_is_pending(self):
        player = dict(timeAligned=True, sampleCount=1, privateCommitSampleCount=1, privateCommitPeakBytes=8000)
        tracker = external_process_memory(Path("missing-tracker.csv"), {}, "tracker", require_tree=True, enabled=False)
        self.assertFalse(tracker["enabled"])
        self.assertEqual([], tracker["warnings"])
        self.assertEqual(8000, combined_private_memory(player, tracker, False)["peakUpperBoundBytes"])
        self.assertIsNone(combined_private_memory(player, tracker, None)["peakUpperBoundBytes"])

    def test_short_memory_trend_reports_growth_without_claiming_a_leak(self):
        rows = [dict(seconds=float(i), process_working_set_bytes=1000000 + 1000 * i) for i in range(60)]
        trend = memory_trend(rows, "Midtown", 50, 60)
        self.assertGreater(trend["analysisStartSeconds"], 0)
        self.assertAlmostEqual(1000, trend["slopeBytesPerSecond"])
        self.assertIn("20-minute", trend["assessment"])
        self.assertNotIn("leak", trend["assessment"])

    def test_full_route_does_not_invent_first_complete_circuit(self):
        rows = [dict(seconds=float(i), process_working_set_bytes=1000000) for i in range(1201)]
        trend = memory_trend(rows, "Full", 10, 1200)
        self.assertIsNone(trend["analysisStartSeconds"])
        self.assertIsNone(trend["slopeBytesPerSecond"])

    def test_no_data_report_preserves_all_pending_combinations(self):
        with tempfile.TemporaryDirectory() as directory:
            result = aggregate([], Path(directory))
            self.assertEqual(10, len(result["pendingRuns"]))
            self.assertTrue(result["memoryRoundtripPending"])
            self.assertTrue(result["proxyComparisonPending"])
            rendered = markdown(result)
            self.assertIn("No measured benchmark files", rendered)
            self.assertIn("pending", rendered)
            self.assertIsNone(result["provenance"]["sourceManifestSha256"])

    def test_short_repeated_region_qualifies_without_twenty_minute_requirement(self):
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            run = measured_fixture(repository, "short")
            result = aggregate([run], repository)
            self.assertEqual(9, len(result["pendingRuns"]))
            self.assertTrue(result["memoryRoundtripPending"])
            self.assertEqual(2, result["runs"][0]["completedMeasuredCircuits"])

    def test_proxy_pair_and_separate_long_memory_roundtrip(self):
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            baseline = measured_fixture(repository, "baseline", duration=1200)
            proxy = measured_fixture(repository, "proxies", proxies=True)
            result = aggregate([baseline, proxy], repository)
            self.assertFalse(result["memoryRoundtripPending"])
            self.assertFalse(result["proxyComparisonPending"])
            self.assertEqual(.5, result["proxyComparisons"][0]["p95CollisionMs"])

    def test_tracking_zero_wrong_render_size_and_partial_circuits_stay_pending(self):
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            no_webcam = measured_fixture(repository, "no-webcam", tracking=0)
            wrong_render = measured_fixture(repository, "wrong-render", render_width=1536)
            (wrong_render / "benchmark-environment.json").write_text(json.dumps(dict(requestedRenderResolution=dict(width=1920, height=1200, windowMode="exclusive"))), encoding="utf-8")
            partial = measured_fixture(repository, "partial", completed=0)
            result = aggregate([no_webcam, wrong_render, partial], repository)
            self.assertEqual(10, len(result["pendingRuns"]))
            self.assertFalse(result["runs"][0]["checks"]["webcamTracking"])
            self.assertFalse(result["runs"][1]["checks"]["resolution"])
            self.assertEqual(1920, result["runs"][1]["environment"]["requestedRenderResolution"]["width"])
            self.assertEqual(1536, result["runs"][1]["renderWidth"])
            self.assertIn("pending", result["runs"][2]["acceptance"])

    def test_embedded_identity_rejects_cli_mismatch_and_dirty_build(self):
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)
            clean = measured_fixture(repository, "clean")
            mismatch = aggregate([clean], repository, technology_revision="3" * 40)
            self.assertEqual(10, len(mismatch["pendingRuns"]))
            self.assertIn("identity mismatch", mismatch["runs"][0]["acceptance"])
            dirty = measured_fixture(repository, "dirty", dirty=True)
            result = aggregate([dirty], repository)
            self.assertFalse(result["runs"][0]["checks"]["buildIdentity"])
            self.assertEqual(10, len(result["pendingRuns"]))


if __name__ == "__main__":
    unittest.main()
