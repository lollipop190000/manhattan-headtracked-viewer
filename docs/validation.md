# Validation and performance record

The acceptance target is a webcam-enabled standalone Windows player on RTX 4070 Laptop 8GB, 32GB RAM, AC power, 1920x1200. Unity Editor frame rate is not an acceptance measurement. Record GPU power mode, driver, resolution, tracker camera/rate, source manifest version, Git commits and runtime settings with every report.

## Automated checks

`tools/test.ps1 -DataOnly` runs 27 Python data tests using only the standard library, plus eight PowerShell assertions for GPU CSV and environment metadata. These cover source integrity/extraction, catalog/fixture/notice invariants and measured-report analysis. GitHub Actions also verifies technology/UPM revision agreement and prevents tracked downloads, personal calibration, build output or generated city files.

`tools/test.ps1` adds Unity EditMode and PlayMode suites and writes JUnit reports into ignored `TestResults/`. Preserve the head-tracking package's projection, calibration and lifecycle tests. Validate any package changes in a minimal project without URP, Manhattan or demo dependencies before publishing its pinned commit.

## Fixed route acceptance

Use the benchmark source rather than hand-driven paths. Select separate closed circuits for dense Midtown, Downtown, Central Park, Uptown and an elevated view using `--benchmark-region Midtown|Downtown|Park|Uptown|Air`; repeat at 10m/s and 50m/s. The first 15 seconds warm assets and histories after the initial region teleport, before samples are collected. Each of the ten region/speed cases needs at least two complete recorded circuits and full route coverage. A separate qualifying trace of at least 1200 measured seconds tests memory over repeated round trips; every regional run does not need to last 20 minutes. A qualifying 20-minute regional run can supply that memory trace. `Full` is an optional connected city route; it cannot reach every district in 20 minutes at 10m/s and does not substitute for regional coverage. The report records the selected region and completed circuits.

Before automated measurements, start a normal webcam run, enter the actual display width/height and viewing distance, and save physical calibration. Automated benchmark/capture modes bypass the startup form. The shared launcher and benchmark accept `-CalibrationDirectory '<private calibration directory>'` to use the same local calibration files; first prepare that directory through a normal run with the same option. Keep its contents outside Git and source Releases. No personal dimensions or calibration files belong in the public validation record.

Runtime arguments:

```text
--benchmark --benchmark-speed 10 --benchmark-region Midtown --benchmark-duration 1200
--benchmark-out <output-directory> --quit-after-benchmark
```

Use the shared launcher to keep the webcam process owned by the benchmark:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark.ps1 -Region Midtown -Speed 10 -Duration 1200
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark.ps1 -Region Downtown -Speed 50 -Duration 1200
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark.ps1 -Region Midtown -Speed 10 -Duration 1200 -CollisionProxies
# Optional: use previously measured calibration kept in a private local directory.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark.ps1 -Region Midtown -Speed 10 -Duration 1200 -CalibrationDirectory '<private calibration directory>'
```

Repeat with `Park`, `Uptown` and `Air`, and pair each measured circuit with the same speed/settings when comparing a change. `maxProcessWorkingSetBytes` samples actual process working set at 1Hz; `maxCpuBytes` records Unity allocation separately. CPU working set and the approximate GPU counter do not establish dedicated GPU memory usage by themselves.

At least one matched proxy OFF/ON pair at the same region, speed, source, hardware and settings measures the added collision workload. The analyzer keeps this paired comparison and the long memory trace separate from the ten regional cases; it does not require a second ten-case proxy matrix.

`tools/benchmark.ps1` creates a unique output directory by default and starts a hidden, owned NVIDIA driver collector when `nvidia-smi` is available. It writes `gpu.csv` beside the frame report and stops the collector in `finally` when the shared launcher returns, including failure paths. `-SkipGpuLog` disables it; `-GpuIntervalMs 500` samples twice per second. Existing benchmark/GPU result files are not overwritten. Collector logs remain beside the report so missing or failed measurements are explicit.

The launcher requests **1920×1200 Exclusive Fullscreen** (`-screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1200`) to use the whole measured physical panel. Check the player's actual recorded render size; a requested size alone is not evidence that the driver accepted the mode. Windowed output on a 2880×1800 panel occupies only part of the measured screen and needs calibration for that smaller physical viewport.

`benchmark-environment.json` records the requested mode, prelaunch desktop pixel size/refresh rate, AC status, GPU name/driver/total memory and driver-exposed power-limit readings. Unsupported values are `null` with explicit availability flags; they are never replaced by a guessed wattage. Desktop size/Hz comes from [Windows EnumDisplaySettingsW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw), which reports physical pixels independent of DPI scaling. These driver limits can vary with laptop power management and do not prove that one fixed performance profile was active throughout the run.

For a separate driver trace:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/measure_gpu.ps1 -DurationSec 1200 -IntervalMs 1000 -OutputPath BenchmarkResults/manual-gpu.csv
```

The driver trace records UTC collection time, driver timestamp, GPU index/name, dedicated memory total/used in MiB, GPU utilization, driver version, power draw and performance state. Unsupported numeric counters are blank. This machine was identified as an RTX 4070 Laptop GPU with 8GB VRAM and driver 591.74 during implementation. A trace records the actual current driver rather than relying on that historical value.

`memoryUsedMiB` is the **whole driver's used dedicated memory for that GPU**, including Unity editors, browsers, desktop composition and other applications. It is a conservative upper bound on this viewer's dedicated VRAM use, not a per-process measurement. The trace includes startup/warmup as well as the sampled benchmark period; match timestamps with the runtime report. Sampling can miss a shorter transient peak. A recorded maximum <=6144MiB demonstrates a sampled whole-GPU bound of 6GiB; a larger value does not by itself identify the viewer as the cause. Use a profiler/driver inspection to attribute or rule out other applications. Unity's `Gfx Used Memory` counter is approximate allocation accounting, not dedicated VRAM usage.

Add `--collision-proxies` for the paired proxy workload; add `--no-tracker` only for an explicitly labeled baseline. Final acceptance uses the tracker enabled. Runtime outputs `benchmark.json`, `frames.csv` and `screenshot.png` in its results directory; `tools/benchmark.ps1` chooses a unique timestamped directory unless one is supplied.

Aggregate the recorded results with `python tools/analyze_benchmarks.py`. It writes ignored `BenchmarkResults/aggregate.json` and `docs/performance-report.md`. Qualification requires actual output **and rendered camera pixels** of 1920×1200 in fullscreen, clean matching embedded build identity, two recorded circuits with full route coverage, fresh webcam tracking for at least 90% of measured samples, raw frame traces and time-aligned driver GPU samples. Requested launch dimensions, CLI revision assertions and a runtime pass flag cannot substitute for those records. Missing measurements remain pending. First/last working-set medians and slope after the first complete circuit aid the 1200-second memory review; the slope alone does not prove a leak or stable residency.

| Check | Acceptance |
|---|---|
| Frame time after initial warmup | P95 <=16.7ms, P99 <=25ms, sustained 60fps target |
| Streaming stall | No streaming-caused frame >=50ms |
| Runtime memory | GPU <=6GB, system <=10GB initial budget, no continuing growth over 20-minute round trip |
| Collision workload | Paired additional CPU P95 <=1ms |
| High-speed traversal | No missing-city holes, double buildings or inconsistent material states |
| Coordinate stability | No street-detail jitter after large travel/origin shifts |
| TAA | Stable frame/foreground edges under head motion; no stale history after teleport/origin/calibration changes |

Frame-time measurements can detect a long frame, but attribution to streaming requires profiler evidence. Unity's reserved memory counters are not a substitute for GPU driver dedicated-memory measurements. Record both the automatic counters and the hardware observations when approving the budget.

Manually test all eight combinations of Head Tracking/window frame/Surface detail and their round trips while stopped and moving. Verify head movement in three axes, face loss/recovery, Settings pointer capture, tracker-off movement and webcam cleanup after quit. Check source imports and generated tile bounds remain aligned.

## Explicit comparison captures

`--comparison-captures-out <directory> --quit-after-comparison-captures` runs a separate standalone visual check. It warms the city for 30 seconds, waits for pending streaming work to settle, and writes baseline, eight T/W/S combinations and round-trip PNGs plus `comparison-report.json`. Use a new output directory for each run:

```powershell
$captureDirectory = [System.IO.Path]::GetFullPath('BenchmarkResults/comparison-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$captureArguments = '-screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1200 --comparison-captures-out "' + $captureDirectory + '" --quit-after-comparison-captures'
& ./tools/run.ps1 -Camera 0 -PlayerArguments $captureArguments
# Add -CalibrationDirectory '<private calibration directory>' when using prepared external calibration.
```

This explicit mode holds movement and city residency while toggling the same comparison APIs, then restores original states, input and streaming. It never saves preferences and has no effect on normal startup. Run it separately from `--benchmark` and `--screenshot-out`; `-NoTracker` is available for a labeled rendering baseline. It uses existing/default calibration for QA without opening the startup form, so first prepare measured calibration for physical head-tracking checks.

The report records state checks, image writes, round-trip restoration and fingerprints of rig/screen transforms, calibration, city/source materials, lighting and pipeline. It records requested Near fade values, which do not prove the rasterized appearance. Head observations continue, so the eye can move while tracking is enabled. Inspect every image for window/surface changes, alignment and TAA artifacts; passing report invariants do not replace image or streamed-gameplay review.

## Release reproducibility

Clone the published repository into a new folder without the original sibling technology checkout or expanded city. Run setup with the private release, bake, Unity tests, Windows build and webcam launch. Confirm that a second setup/bake is repeatable and that modified local source inputs are preserved and rejected rather than overwritten. Record the clone commit and resulting build hash in the release report.

```powershell
# After the reviewed commit and private source release have been pushed:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify_clone.ps1 -Revision <full Git commit> -Run -Camera 0
# A quick portable clone check without Unity or city downloads:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify_clone.ps1 -Revision <full Git commit> -StaticOnly
```

The command keeps the independent clone in ignored `.verification/` and records its exact commit, source version and Windows player SHA-256. `-SourceArchiveDirectory` permits an explicitly labeled offline-source variant; omit it when verifying authenticated private-release download. `-NoTracker` is a baseline variant and is not webcam acceptance. Builds in the isolated clone permit regenerated scene/settings changes; Git source and original city input hashes remain recorded.

The verification record reads source version/release and technology revision from the **cloned revision**, and fingerprints its source manifest and tracker lock. A static-only run records that no sources were downloaded and no player was launched. Setup requires the committed, tested tracker lock; it cannot silently fall back to a different dependency set.

## Current record

As of 2026-10-10, reusable changes published in [technology commit `960d5de55037a3fe2739ed055c8de091b8c54aed`](https://github.com/lollipop190000/head-tracked-display/commit/960d5de55037a3fe2739ed055c8de091b8c54aed) passed the following compatibility checks. The viewer's UPM, launcher and tracker are pinned to that commit.

| Validation context | Recorded result |
|---|---|
| Head-tracking demo | 39/39 EditMode and 22/22 PlayMode tests passed |
| Minimal consumer without URP or demo | 39/39 EditMode and 11/11 PlayMode tests passed |
| Runtime-only consumer without URP or Unity Test Framework | Windows build succeeded; standalone package smoke exited 0 |
| Parameterized shared launcher | 21 assertions passed |
| Manhattan portable checks | 27/27 Python data tests and 8/8 PowerShell assertions passed |

Original package XML/build/smoke evidence remains in the technical checkout's ignored `Builds/ManhattanPackageValidation/`. The runtime-only smoke covers projection, frame/source ownership, collider removal, JSON calibration and feature round trips. These package checks do not establish the Manhattan player's visual quality or frame rate.

Source transport and extracted-input hashes have been verified against all six ZIPs and their 370 entries after the v2 source replacement. The two revised ZIPs retain byte-identical geodata/provenance/terms and semantically identical materials and embedded texture contents; the four detail/vegetation archives retain their original hashes. Sixteen city/source/notice data tests cover the 243-tile inventory, digest mismatch, idempotent extraction, local-edit preservation, path escape rejection, ZIP symlink/case-alias rejection, manifest totals and required provenance/terms, catalog source/bounds/layer/exact-replacement-count invariants, verified build notice copies and the real small GLB fixture's header, vertex bounds, index ranges and one-time coordinate conversion. Five PowerShell assertions check multi-GPU parsing, timestamps, numeric counters, unsupported values and CSV round trips.

The driver collector also wrote actual RTX 4070 Laptop samples (8188MiB total, driver 591.74). A mock launcher failure retained its CSV samples, terminated the owned collector process and removed its stop file. This checks logging and cleanup only; it does not measure the viewer's VRAM or frame rate.

Hardware metadata capture independently reported a 2880×1800@60Hz prelaunch desktop, AC online and the same GPU/driver. `power.limit` was unavailable on this laptop while the enforced/default/minimum/maximum readings were exposed; the environment record retains that distinction. Three additional pure PowerShell assertions cover unavailable power limits, numeric readings and environment JSON round trips.

An isolated Windows x64 Python 3.13.5 environment passed `pip check`, all ten tracker protocol/pose/lifetime tests, and a CPU Face Landmarker smoke test using the SHA-256-pinned model and a blank synthetic image. `tools/tracker-requirements.lock.txt` records this environment with MediaPipe 1.1.0, NumPy 2.5.3 and a single OpenCV contrib 5.0.0.93 provider. `tools/test_tracker.py` repeats its interpreter, every locked dependency version, provider/model checks and CPU inference without opening a webcam. The existing tracker environment was not changed. Webcam/head-movement acceptance remains separate.

The full Manhattan standalone build/render, ten regional measurements, matched proxy comparison, 1200-second memory trace, comparison PNG review, manual webcam/head-motion checks and clean GitHub clone-to-build execution are **pending**. No Manhattan FPS, frame-time percentile, VRAM-budget result or 20-minute memory-stability result has been accepted yet. Record those actual outputs after the final implementation run; targets in this document are not measured performance.
