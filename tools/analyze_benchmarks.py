"""Aggregate measured Unity/driver benchmark files; never substitute missing measurements.

python tools/analyze_benchmarks.py [BenchmarkResults/run ...]
Default outputs: BenchmarkResults/aggregate.json and docs/performance-report.md.
"""
from __future__ import annotations

import argparse
import csv
from datetime import datetime, timedelta, timezone
import hashlib
import json
import math
from pathlib import Path
import re
import statistics

ROOT = Path(__file__).resolve().parents[1]
GIB = 1024 ** 3
REGIONS = ("Midtown", "Downtown", "Park", "Uptown", "Air")
REGION_CIRCUIT_METRES = math.hypot(400, 400) + 2 * math.hypot(600, 400) + math.hypot(600, 200)


def number(value):
    try:
        value = float(value)
        return value if math.isfinite(value) else None
    except (TypeError, ValueError):
        return None


def utc(value):
    try:
        parsed = datetime.fromisoformat(str(value).replace("Z", "+00:00"))
        return parsed.replace(tzinfo=timezone.utc) if parsed.tzinfo is None else parsed.astimezone(timezone.utc)
    except (TypeError, ValueError):
        return None


def percentile(values, fraction):
    """Same nearest-rank definition as BenchmarkStatistics in the player."""
    if not values:
        return None
    ordered = sorted(values)
    return ordered[max(0, min(len(ordered) - 1, math.ceil(fraction * len(ordered)) - 1))]


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def read_frames(path):
    rows, invalid = [], 0
    if not path.exists():
        return rows, ["frames.csv missing; raw timing and memory trends unavailable"]
    with path.open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream):
            seconds, frame_ms = number(row.get("seconds")), number(row.get("frame_ms"))
            if seconds is None or frame_ms is None or seconds < 0 or frame_ms < 0:
                invalid += 1
                continue
            parsed = {key: number(value) for key, value in row.items()}
            parsed.update(seconds=seconds, frame_ms=frame_ms)
            rows.append(parsed)
    rows.sort(key=lambda row: row["seconds"])
    return rows, ([f"Ignored {invalid} malformed/non-finite frame rows"] if invalid else [])


def memory_trend(rows, region, speed, measured_duration, circuit_duration=None):
    circuit = circuit_duration if circuit_duration and circuit_duration > 0 else REGION_CIRCUIT_METRES / speed if region in REGIONS and speed and speed > 0 else None
    start = max(0.0, circuit - 15.0) if circuit is not None else None
    # Working set is sampled once/second by the player. Remove repeated frame-level values.
    samples = {}
    for row in rows:
        value = row.get("process_working_set_bytes")
        if start is not None and row["seconds"] >= start and value is not None and value > 0:
            samples[int(row["seconds"])] = (row["seconds"], value)
    ordered = sorted(samples.values())
    result = dict(firstCircuitSeconds=circuit, analysisStartSeconds=start, samples=len(ordered),
                  firstWindowMedianBytes=None, lastWindowMedianBytes=None, medianDeltaBytes=None,
                  slopeBytesPerSecond=None, assessment="pending: insufficient samples after first complete circuit")
    if start is None:
        result["assessment"] = "pending: Full/custom route has no recorded first-circuit duration"
        return result
    if len(ordered) < 3 or ordered[-1][0] <= ordered[0][0]:
        return result
    span = ordered[-1][0] - ordered[0][0]
    window = min(60.0, span / 4)
    first = [v for t, v in ordered if t <= ordered[0][0] + window]
    last = [v for t, v in ordered if t >= ordered[-1][0] - window]
    mean_t = statistics.fmean(t for t, _ in ordered)
    mean_v = statistics.fmean(v for _, v in ordered)
    denominator = sum((t - mean_t) ** 2 for t, _ in ordered)
    slope = sum((t - mean_t) * (v - mean_v) for t, v in ordered) / denominator
    result.update(firstWindowMedianBytes=statistics.median(first), lastWindowMedianBytes=statistics.median(last),
                  medianDeltaBytes=statistics.median(last) - statistics.median(first),
                  slopeBytesPerSecond=slope, analyzedDurationSeconds=span, medianWindowSeconds=window)
    if measured_duration is None or measured_duration < 1200:
        result["assessment"] = "short-run trend only; 20-minute memory acceptance pending"
    else:
        result["assessment"] = "20-minute trend measured; review repeat-circuit residency and slope before accepting memory stability"
    return result


def driver_gpu(path, report):
    result = dict(peakBytes=None, sampleCount=0, timeAligned=False, gpuName=None,
                  scope="whole selected GPU, including other applications", warnings=[])
    if not path.exists():
        result["warnings"].append("gpu.csv missing; dedicated driver memory acceptance pending")
        return result
    candidates = []
    with path.open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream):
            used = number(row.get("memoryUsedMiB"))
            if used is None or used < 0:
                continue
            candidates.append((utc(row.get("sampleUtc")), row.get("gpuName", "").strip(), used * 1024 ** 2))
    expected = str(report.get("gpu", "")).strip().casefold()
    matched = [row for row in candidates if row[1].casefold() == expected] if expected else []
    if not matched:
        names = {name for _, name, _ in candidates}
        if len(names) == 1:
            matched = candidates
            result["warnings"].append("Driver GPU matched by unique device; runtime name did not match exactly")
        elif names:
            result["warnings"].append("Multiple driver GPUs and no matching runtime GPU; memory remains unavailable")
            return result
    finish = utc(report.get("utcTime"))
    duration = number(report.get("durationSeconds"))
    if finish is not None and duration is not None and duration >= 0:
        begin = finish - timedelta(seconds=duration)
        aligned = [row for row in matched if row[0] is not None and begin <= row[0] <= finish]
        if aligned:
            matched = aligned
            result["timeAligned"] = True
        else:
            result["warnings"].append("No driver sample aligned with measured runtime interval; raw peak includes startup/warmup")
    else:
        result["warnings"].append("Runtime UTC/duration missing; driver peak cannot exclude startup/warmup")
    if matched:
        result.update(peakBytes=max(v for _, _, v in matched), sampleCount=len(matched), gpuName=matched[0][1])
    else:
        result["warnings"].append("No valid driver memory samples")
    return result


def metric(report, rows, json_key, csv_key, fraction=.95):
    values = [r[csv_key] for r in rows if r.get(csv_key) is not None and r[csv_key] >= 0]
    return percentile(values, fraction) if values else number(report.get(json_key))


def analyze_run(directory):
    report = read_json(directory / "benchmark.json")
    rows, warnings = read_frames(directory / "frames.csv")
    driver = driver_gpu(directory / "gpu.csv", report)
    speed, duration = number(report.get("speed")), number(report.get("durationSeconds"))
    region = report.get("routeRegion") or "unspecified"
    environment_path = directory / "benchmark-environment.json"
    environment = read_json(environment_path) if environment_path.exists() else None
    route_duration = number(report.get("routeDurationSeconds"))
    coverage = number(report.get("measuredRouteCoverageFraction"))
    if coverage is not None and coverage < 0:
        coverage = None
    completed = number(report.get("completedMeasuredCircuits"))
    if route_duration and rows and all(row.get("route_cycles") is not None and row.get("route_elapsed_seconds") is not None for row in (rows[0], rows[-1])):
        start = rows[0]["route_cycles"] * route_duration + rows[0]["route_elapsed_seconds"]
        end = rows[-1]["route_cycles"] * route_duration + rows[-1]["route_elapsed_seconds"]
        csv_coverage = max(0, min(1, (end - start) / route_duration))
        if not report.get("translationConstrained"):
            coverage = csv_coverage
    trace_duration = rows[-1]["seconds"] - rows[0]["seconds"] if len(rows) > 1 else None
    frames50 = sum(row["frame_ms"] >= 50 for row in rows) if rows else number(report.get("framesOver50Ms"))
    frame_count = len(rows) if rows else number(report.get("frameCount"))
    tracking = [row["tracking"] for row in rows if row.get("tracking") is not None]
    tracking_fraction = statistics.fmean(tracking) if tracking else number(report.get("trackingFraction"))
    working_sets = [row["process_working_set_bytes"] for row in rows if row.get("process_working_set_bytes") is not None and row["process_working_set_bytes"] > 0]
    working_set_peak = max(working_sets) if working_sets else number(report.get("maximumProcessWorkingSetBytes"))
    gfx = [row["gpu_bytes"] for row in rows if row.get("gpu_bytes") is not None and row["gpu_bytes"] > 0]
    gfx_peak = max(gfx) if gfx else number(report.get("maximumGpuBytes"))
    if not report.get("graphicsMemoryCounterAvailable") or not gfx_peak:
        gfx_peak = None
    run = dict(directory=str(directory), measured=bool(frame_count and frame_count > 0), utcTime=report.get("utcTime"),
               unityVersion=report.get("unityVersion"), sourceVersion=report.get("sourceVersion"), gpu=report.get("gpu"), cpu=report.get("cpu"),
               region=region, speed=speed, durationSeconds=duration, width=report.get("width"), height=report.get("height"),
               renderWidth=report.get("renderWidth"), renderHeight=report.get("renderHeight"), fullscreenMode=report.get("fullscreenMode"),
               displayModeWidth=report.get("displayModeWidth"), displayModeHeight=report.get("displayModeHeight"), environment=environment,
               routeDurationSeconds=route_duration, routeDistanceMeters=number(report.get("routeDistanceMeters")),
               routeCoverageFraction=coverage, completedMeasuredCircuits=completed, traceDurationSeconds=trace_duration,
               viewerRevision=report.get("viewerRevision"), technologyRevision=report.get("technologyRevision"),
               sourceManifestSha256=report.get("sourceManifestSha256"), buildUtc=report.get("buildUtc"), dirtyWorkingTree=report.get("dirtyWorkingTree"),
               buildIdentityAvailable=bool(report.get("buildIdentityAvailable")),
               frameCount=frame_count, trackingFraction=tracking_fraction, collisionProxies=bool(report.get("collisionProxies")),
               p95FrameMs=metric(report, rows, "p95FrameMs", "frame_ms"),
               p99FrameMs=metric(report, rows, "p99FrameMs", "frame_ms", .99),
               maximumFrameMs=metric(report, rows, "maximumFrameMs", "frame_ms", 1), framesOver50Ms=frames50,
               p95StreamingMs=metric(report, rows, "p95StreamingMs", "streaming_ms"),
               p95CollisionMs=metric(report, rows, "p95CollisionMs", "collision_ms"),
               workingSetPeakBytes=working_set_peak if working_set_peak else None, gfxApproxPeakBytes=gfx_peak,
               driverGpu=driver, memoryTrend=memory_trend(rows, region, speed, duration, route_duration),
               streamingFailures=number(report.get("streamingFailures")), runtimeReportedPassed=report.get("performanceTargetPassed"),
               warnings=warnings + driver["warnings"], runtimeLimitation=report.get("limitation"))
    if rows and number(report.get("frameCount")) != len(rows):
        run["warnings"].append("JSON frameCount differs from valid CSV rows; CSV statistics used")
    # Missing telemetry stays pending instead of becoming a false zero/pass.
    checks = {
        "measuredSamples": run["measured"],
        "frameTrace": True if rows else None,
        "buildIdentity": None if not run["buildIdentityAvailable"] else
            run["dirtyWorkingTree"] is False and bool(re.fullmatch(r"[a-fA-F0-9]{40}", str(run["viewerRevision"] or ""))) and
            bool(re.fullmatch(r"[a-fA-F0-9]{40}", str(run["technologyRevision"] or ""))) and
            bool(re.fullmatch(r"[a-fA-F0-9]{64}", str(run["sourceManifestSha256"] or ""))),
        "frameP95": None if run["p95FrameMs"] is None else run["p95FrameMs"] <= 16.7,
        "frameP99": None if run["p99FrameMs"] is None else run["p99FrameMs"] <= 25,
        "noFramesOver50Ms": None if frames50 is None else frames50 == 0,
        "streamingP95": None if run["p95StreamingMs"] is None else run["p95StreamingMs"] <= 2,
        "collisionP95": (None if run["p95CollisionMs"] is None else run["p95CollisionMs"] <= 1) if run["collisionProxies"] else True,
        "processMemory": None if not working_set_peak else working_set_peak <= 10 * GIB,
        "driverGpuMemory": None if driver["peakBytes"] is None or not driver["timeAligned"] else driver["peakBytes"] <= 6 * GIB,
        "webcamTracking": None if tracking_fraction is None else tracking_fraction >= .9,
        "resolution": None if any(run[key] is None for key in ("width", "height", "renderWidth", "renderHeight")) else
            (run["width"], run["height"], run["renderWidth"], run["renderHeight"]) == (1920, 1200, 1920, 1200),
        "fullscreen": None if run["fullscreenMode"] is None else run["fullscreenMode"] in ("ExclusiveFullScreen", "FullScreenWindow"),
        "routeCoverage": None if coverage is None else coverage >= .999,
        "repeatedCircuits": None if completed is None else completed >= 2,
        "streamingFailures": None if run["streamingFailures"] is None else run["streamingFailures"] == 0,
    }
    run["checks"] = checks
    run["acceptance"] = "passed measured regional numeric criteria; separate memory/visual review pending" if all(v is True for v in checks.values()) else "pending" if any(v is None for v in checks.values()) or checks["routeCoverage"] is False or checks["repeatedCircuits"] is False or checks["buildIdentity"] is False else "criteria not met"
    return run


def geometry_report(path):
    if not path.exists():
        return dict(status="pending: city-bake.json not present", layers=[])
    report = read_json(path)
    layers = {}
    for tile in report.get("tiles", []):
        layer = str(tile.get("layer", "unspecified"))
        item = layers.setdefault(layer, dict(layer=layer, tiles=0, triangles=0, estimatedResidentBytes=0))
        item["tiles"] += 1
        item["triangles"] += int(tile.get("triangles", 0))
        item["estimatedResidentBytes"] += int(tile.get("residentBytes", 0))
    return dict(status="baked geometry metadata; resident-byte values are estimates, not runtime allocations",
                sourceVersion=report.get("sourceVersion"), tileCount=report.get("tileCount"), triangles=report.get("triangles"),
                buildingCount=report.get("buildingCount"), layers=list(layers.values()),
                reportSha256=hashlib.sha256(path.read_bytes()).hexdigest())


def aggregate(directories, repository=ROOT, technology_revision=None, viewer_revision=None):
    runs, errors = [], []
    for directory in directories:
        try:
            runs.append(analyze_run(directory))
        except (OSError, ValueError, TypeError) as error:
            errors.append(dict(directory=str(directory), error=str(error)))
    manifest_path = repository / "assets/source-manifest.json"
    tech_path = repository / "tools/technology-version.json"
    manifest = read_json(manifest_path) if manifest_path.exists() else {}
    tech = read_json(tech_path) if tech_path.exists() else {}
    provenance = dict(sourceVersion=manifest.get("sourceVersion"),
                      sourceManifestSha256=hashlib.sha256(manifest_path.read_bytes()).hexdigest() if manifest_path.exists() else None,
                      sourceArchives=[dict(asset=p.get("asset"), sha256=p.get("sha256"), bytes=p.get("bytes")) for p in manifest.get("packs", [])],
                      technologyRevision=technology_revision or tech.get("revision"), viewerRevision=viewer_revision,
                      identityBasis="runtime embedded identity is authoritative; expected repository inputs and optional caller assertions are checked below")
    for run in runs:
        mismatches = []
        if technology_revision and run["technologyRevision"] != technology_revision:
            mismatches.append("CLI technology revision does not match embedded build identity")
        if viewer_revision and run["viewerRevision"] != viewer_revision:
            mismatches.append("CLI viewer revision does not match embedded build identity")
        if run["buildIdentityAvailable"]:
            if provenance["sourceManifestSha256"] and run["sourceManifestSha256"] != provenance["sourceManifestSha256"]:
                mismatches.append("Embedded source manifest SHA does not match selected repository source")
            if tech.get("revision") and run["technologyRevision"] != tech["revision"]:
                mismatches.append("Embedded technology revision does not match current pinned technology")
        if mismatches:
            run["checks"]["buildIdentity"] = False
            run["warnings"].extend(mismatches)
            run["acceptance"] = "pending: build identity mismatch"
    pending = []
    proxy_pairs = []
    def qualifies(run):
        return run["measured"] and all(run["checks"][key] is True for key in ("frameTrace", "buildIdentity", "webcamTracking", "resolution", "fullscreen", "routeCoverage", "repeatedCircuits")) and run["driverGpu"]["timeAligned"]
    for region in REGIONS:
        for speed in (10, 50):
            measured = [r for r in runs if r["measured"] and r["region"] == region and r["speed"] == speed]
            complete = [r for r in measured if qualifies(r)]
            if not complete:
                pending.append(dict(region=region, speed=speed, reason="missing measured run" if not measured else
                                    "requires clean matching embedded build identity, >=2 complete recorded circuits, full route coverage, actual 1920x1200 render/output fullscreen, >=90% fresh webcam tracking and frame/driver GPU traces"))
            off = [r for r in complete if not r["collisionProxies"]]
            on = [r for r in complete if r["collisionProxies"]]
            for baseline in off:
                candidates = [r for r in on if r["gpu"] == baseline["gpu"] and r["cpu"] == baseline["cpu"] and r["sourceVersion"] == baseline["sourceVersion"]]
                if candidates:
                    proxy = candidates[-1]
                    proxy_pairs.append(dict(region=region, speed=speed, baselineDirectory=baseline["directory"], proxyDirectory=proxy["directory"],
                                            p95CollisionMs=proxy["p95CollisionMs"],
                                            p95FrameDeltaMs=proxy["p95FrameMs"] - baseline["p95FrameMs"] if proxy["p95FrameMs"] is not None and baseline["p95FrameMs"] is not None else None,
                                            collisionTargetPassed=proxy["checks"]["collisionP95"]))
                    break
    memory_runs = [r for r in runs if qualifies(r) and r["durationSeconds"] is not None and r["durationSeconds"] >= 1200 and
                   r["traceDurationSeconds"] is not None and r["traceDurationSeconds"] >= 1199 and r["memoryTrend"]["slopeBytesPerSecond"] is not None]
    for run in runs:
        if provenance["sourceVersion"] and run["sourceVersion"] != provenance["sourceVersion"]:
            run["warnings"].append("Run source version differs from current source manifest; provenance does not identify this run")
    return dict(schemaVersion=1, generatedUtc=datetime.now(timezone.utc).isoformat(), provenance=provenance,
                targets=dict(resolution="1920x1200", frameP95Ms=16.7, frameP99Ms=25, streamingP95Ms=2, collisionP95Ms=1,
                             workingSetGiB=10, driverGpuGiB=6, regionalRecordedCircuits=2, memoryRoundtripSeconds=1200, webcamTrackingFraction=.9),
                runs=runs, errors=errors, geometry=geometry_report(repository / "Builds/city-bake.json"), pendingRuns=pending,
                proxyComparisons=proxy_pairs, proxyComparisonPending=not bool(proxy_pairs), memoryRoundtripRuns=[r["directory"] for r in memory_runs], memoryRoundtripPending=not bool(memory_runs),
                pendingManualChecks=["Head movement and face loss/reacquisition; visual TAA ghosting and window/texture stability",
                                     "Eight comparison combinations and round trips during streamed gameplay",
                                     "Memory stability review across repeated complete circuits; slope alone does not establish a leak",
                                     "Clean GitHub clone source preparation, Unity tests, Windows build and webcam execution"])


def format_number(value, decimals=2, divisor=1):
    return "pending" if value is None else f"{value / divisor:.{decimals}f}"


def markdown(result):
    provenance = result["provenance"]
    lines = ["# Performance measurements", "", "This report separates measured samples from pending acceptance work. Missing values remain pending; no frame rate or memory result is inferred from code or hardware capacity.", "",
             f"Source version: `{provenance['sourceVersion'] or 'pending'}`. Source manifest SHA-256: `{provenance['sourceManifestSha256'] or 'pending'}`.", "",
             f"Expected technology commit: `{provenance['technologyRevision'] or 'pending'}`. Caller asserted viewer commit: `{provenance['viewerRevision'] or 'not asserted'}`. Identity basis: {provenance['identityBasis']}.", "",
             "## Measured runs", "",
             "Timing includes vsync. Driver memory is the entire selected GPU, including other applications; GPU samples are restricted to the measured UTC interval where timestamps permit. Gfx is Unity allocation telemetry and remains an approximation. Process WS is the actual player working set.", "",
             "| Region | m/s | Output / render | Mode | Seconds | Tracking | Proxies | P95 / P99 / max ms | ≥50ms frames | Stream P95 ms | Collision P95 ms | WS GiB | Driver GPU GiB | Gfx approx GiB |", "|---|---:|---|---|---:|---:|---|---|---:|---:|---:|---:|---:|---:|"]
    for run in result["runs"]:
        lines.append("| " + " | ".join([str(run["region"]), format_number(run["speed"], 0), f"{run['width']}×{run['height']} / {run['renderWidth']}×{run['renderHeight']}", str(run['fullscreenMode'] or 'pending'), format_number(run["durationSeconds"], 1),
            format_number(run["trackingFraction"] * 100 if run["trackingFraction"] is not None else None, 1) + "%", "ON" if run["collisionProxies"] else "OFF",
            " / ".join(format_number(run[k]) for k in ("p95FrameMs", "p99FrameMs", "maximumFrameMs")), format_number(run["framesOver50Ms"], 0),
            format_number(run["p95StreamingMs"]), format_number(run["p95CollisionMs"]) if run["collisionProxies"] else "—",
            format_number(run["workingSetPeakBytes"], 3, GIB), format_number(run["driverGpu"]["peakBytes"], 3, GIB), format_number(run["gfxApproxPeakBytes"], 3, GIB)]) + " |")
    if not result["runs"]:
        lines += ["", "No measured benchmark files are available."]
    lines += ["", "Regional acceptance needs two complete recorded circuits at each of the five regions and both speeds. Each region does not need a 20-minute run. The separate memory round trip needs at least one measured 1200-second trace. Proxy cost needs a matched OFF/ON pair at the same region/speed, hardware and source.", "",
              "| Region / m/s / proxies | Recorded complete circuits | Route coverage | Display mode pixels | Launch environment |", "|---|---:|---:|---|---|"]
    for run in result["runs"]:
        coverage = format_number(run["routeCoverageFraction"] * 100 if run["routeCoverageFraction"] is not None else None, 1)
        environment = "recorded (requested values are separate from runtime values)" if run["environment"] else "pending"
        lines.append(f"| {run['region']} / {format_number(run['speed'], 0)} / {'ON' if run['collisionProxies'] else 'OFF'} | {format_number(run['completedMeasuredCircuits'], 0)} | {coverage}% | {run['displayModeWidth']}×{run['displayModeHeight']} | {environment} |")
    lines += ["", "Embedded binary identities are authoritative. Dirty development builds and caller revision assertions that disagree with the embedded identity remain pending.", "",
              "| Region / m/s | Viewer commit | Technology commit | Source manifest SHA-256 | Dirty |", "|---|---|---|---|---|"]
    for run in result["runs"]:
        lines.append(f"| {run['region']} / {format_number(run['speed'], 0)} | `{run['viewerRevision'] or 'pending'}` | `{run['technologyRevision'] or 'pending'}` | `{run['sourceManifestSha256'] or 'pending'}` | {run['dirtyWorkingTree']} |")
    lines += ["", "Requested launch size and prelaunch desktop size do not substitute for the runtime render/output measurements above.", "",
              "| Region / m/s | Requested size / mode | Prelaunch desktop / Hz | AC online | Driver | GPU total GiB | Enforced power limit W |", "|---|---|---|---|---|---:|---:|"]
    for run in result["runs"]:
        environment = run["environment"] or {}
        requested = environment.get("requestedRenderResolution") or {}
        desktop = environment.get("prelaunchDesktop") or {}
        devices = environment.get("gpus") or []
        gpu = next((device for device in devices if str(device.get("gpuName", "")).casefold() == str(run["gpu"] or "").casefold()), devices[0] if len(devices) == 1 else {})
        total_mib = number(gpu.get("memoryTotalMiB"))
        power = number(gpu.get("enforcedPowerLimitWatts")) if gpu.get("enforcedPowerLimitAvailable") else None
        lines.append(f"| {run['region']} / {format_number(run['speed'], 0)} | {requested.get('width', 'pending')}×{requested.get('height', 'pending')} / {requested.get('windowMode', 'pending')} | "
                     f"{desktop.get('width', 'pending')}×{desktop.get('height', 'pending')} / {format_number(number(desktop.get('refreshHz')), 1)} | "
                     f"{environment.get('acOnline', 'pending')} | {gpu.get('driverVersion', 'pending')} | {format_number(total_mib, 3, 1024)} | {format_number(power, 1)} |")
    lines += ["", "## Memory after the first complete circuit", "", "The first/last medians use at most 60-second windows after the first completed regional circuit. Slopes are least-squares process working-set trends from one sample per second. Positive growth can include caches; it is not automatically called a leak. Short runs do not satisfy the 20-minute stability check.", "",
              "| Region / m/s / proxies | First median MiB | Last median MiB | Δ MiB | Slope MiB/min | Assessment |", "|---|---:|---:|---:|---:|---|"]
    for run in result["runs"]:
        trend = run["memoryTrend"]
        lines.append(f"| {run['region']} / {format_number(run['speed'], 0)} / {'ON' if run['collisionProxies'] else 'OFF'} | " + " | ".join([
            format_number(trend["firstWindowMedianBytes"], 2, 1024 ** 2), format_number(trend["lastWindowMedianBytes"], 2, 1024 ** 2), format_number(trend["medianDeltaBytes"], 2, 1024 ** 2),
            format_number(trend["slopeBytesPerSecond"] * 60 if trend["slopeBytesPerSecond"] is not None else None, 3, 1024 ** 2), trend["assessment"]]) + " |")
    geometry = result["geometry"]
    lines += ["", "## Geometry and residency budget", "", geometry["status"] + ".", "", "| Layer | Baked tiles | Triangles | Sum of tile byte estimates (GiB) |", "|---|---:|---:|---:|"]
    for layer in geometry["layers"]:
        lines.append(f"| {layer['layer']} | {layer['tiles']:,} | {layer['triangles']:,} | {layer['estimatedResidentBytes'] / GIB:.3f} |")
    lines += ["", "Tile sums describe all baked geometry, including mutually exclusive detail levels. They are not the simultaneously visible triangle count, texture/render-target residency, or a VRAM measurement. Runtime tile budget is 1.5 GiB of estimates; actual app targets are 10 GiB working set and 6 GiB GPU memory.", "",
              "## Pending acceptance", ""]
    for pending in result["pendingRuns"]:
        lines.append(f"- {pending['region']} at {pending['speed']}m/s: {pending['reason']}.")
    if not result["pendingRuns"]:
        lines.append("All ten regional speed cases have qualifying repeated route samples; numeric and manual review still determine acceptance.")
    lines.append("- Separate 20-minute memory round trip: " + ("pending (requires raw memory trace, repeated circuit, correct fullscreen render and fresh webcam tracking)." if result["memoryRoundtripPending"] else "measured; inspect medians/slope and repeated residency before accepting stability."))
    lines.append("- Matched proxy OFF/ON comparison: " + ("pending." if result["proxyComparisonPending"] else "measured; paired probe costs below."))
    if result["proxyComparisons"]:
        lines += ["", "| Region | m/s | Proxy probe P95 ms | ON minus OFF frame P95 ms | Probe ≤1ms |", "|---|---:|---:|---:|---|"]
        for pair in result["proxyComparisons"]:
            lines.append(f"| {pair['region']} | {format_number(pair['speed'], 0)} | {format_number(pair['p95CollisionMs'])} | {format_number(pair['p95FrameDeltaMs'])} | {pair['collisionTargetPassed']} |")
        lines.append("Frame delta includes GPU/vsync scheduling and is not an isolated CPU cost; the timed collision probes provide the CPU cost.")
    lines += ["", "Numeric thresholds: P95 ≤16.7ms, P99 ≤25ms, no ≥50ms frame, streaming P95 ≤2ms, collision probe P95 ≤1ms, no streaming failure. A runtime pass flag is not substituted for independent checks or dedicated memory telemetry.", ""]
    for run in result["runs"]:
        failed = [key for key, value in run["checks"].items() if value is False]
        unavailable = [key for key, value in run["checks"].items() if value is None]
        lines.append(f"- {run['region']} / {format_number(run['speed'], 0)}m/s / proxies {'ON' if run['collisionProxies'] else 'OFF'}: {run['acceptance']}. Failed: {', '.join(failed) or 'none'}. Unavailable: {', '.join(unavailable) or 'none'}.")
        for warning in run["warnings"]:
            lines.append(f"  - {warning}.")
    lines += ["", *[f"- {item}." for item in result["pendingManualChecks"]], ""]
    for error in result["errors"]:
        lines.append(f"- Unreadable run `{error['directory']}`: {error['error']}.")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("runs", nargs="*", type=Path, help="Selected run directories or benchmark.json paths")
    parser.add_argument("--repository", type=Path, default=ROOT)
    parser.add_argument("--output-json", type=Path)
    parser.add_argument("--output-markdown", type=Path)
    parser.add_argument("--technology-revision", help="Actual build's tested technology SHA, if known")
    parser.add_argument("--viewer-revision", help="Actual build's viewer Git SHA, if known")
    args = parser.parse_args()
    repository = args.repository.resolve()
    selected = args.runs or sorted((repository / "BenchmarkResults").glob("*/benchmark.json"))
    directories = [path.resolve().parent if path.name == "benchmark.json" else path.resolve() for path in selected]
    result = aggregate(directories, repository, args.technology_revision, args.viewer_revision)
    out_json = args.output_json or repository / "BenchmarkResults/aggregate.json"
    out_md = args.output_markdown or repository / "docs/performance-report.md"
    for path, contents in ((out_json, json.dumps(result, indent=2, allow_nan=False) + "\n"), (out_md, markdown(result))):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(contents, encoding="utf-8")
    print(f"Measured runs: {len(result['runs'])}; remaining required run combinations: {len(result['pendingRuns'])}")
    print(f"JSON: {out_json}\nReport: {out_md}")
    return 1 if result["errors"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
