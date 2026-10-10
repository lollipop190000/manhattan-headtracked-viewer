# Validation and performance record

The acceptance target is a webcam-enabled standalone Windows player on RTX 4070 Laptop 8GB, 32GB RAM, AC power, 1920x1200. Unity Editor frame rate is not an acceptance measurement. Record GPU power mode, driver, resolution, tracker camera/rate, source manifest version, Git commits and runtime settings with every report.

## Automated checks

`tools/test.ps1 -DataOnly` runs Python data tests using only the standard library, plus eight PowerShell assertions for GPU CSV and environment metadata. These cover source integrity/extraction, catalog/fixture/notice invariants, build preflight and measured-report analysis. GitHub Actions also verifies technology/UPM revision agreement and prevents tracked downloads, personal calibration, build output or generated city files.

`tools/test.ps1` adds Unity EditMode and PlayMode suites and writes JUnit reports into ignored `TestResults/`. Preserve the head-tracking package's projection, calibration and lifecycle tests. Validate any package changes in a minimal project without URP, Manhattan or demo dependencies before publishing its pinned commit.

## Fixed route acceptance

Benchmark start, warmup, measured duration and CSV timestamps consistently use `Time.unscaledTimeAsDouble`. This avoids float-clock quantization causing a false speed failure after long application uptime. Regression checks simulate 20s, 20,000s and 100,000s clock baselines at 100m/s and 60Hz; they are arithmetic checks, not an hours-long GPU run.

Use the benchmark source rather than hand-driven paths. Select separate closed circuits for dense Midtown, Downtown, Central Park, Uptown and an elevated view using `--benchmark-region Midtown|Downtown|Park|Uptown|Air`; repeat at 10m/s, 50m/s and 100m/s. The first 15 seconds warm assets and histories after the initial region teleport, before samples are collected. Each of the fifteen region/speed cases needs at least two complete recorded circuits and full route coverage. A separate qualifying trace of at least 1200 measured seconds tests memory over repeated round trips; every regional run does not need to last 20 minutes. A qualifying 20-minute regional run can supply that memory trace. `Full` is an optional connected city route; it cannot reach every district in 20 minutes at 10m/s and does not substitute for regional coverage. The report records the selected region and completed circuits. `requestedSpeed` is the requested value; `actualRouteSpeed` and legacy `speed` describe the applied route setting. The analyzer independently checks city-position differences against recorded wall time (20 or more intervals, at least 90% trace-time coverage, median/P10/P90 within 5% of the request). Missing or sparse speed traces remain pending. A request of 100m/s with a 50m/s route cannot qualify. Benchmark mode temporarily replaces the motion input; it does not change or save the ordinary movement speed. Runtime `performanceTargetPassed` includes streaming P95 <=2ms, but remains provisional; the analyzer still checks driver VRAM, build identity, routes and webcam telemetry separately.

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

At least one matched proxy OFF/ON pair at the same region, speed, source, hardware and settings measures the added collision workload. The analyzer keeps this paired comparison and the long memory trace separate from the fifteen regional cases; it does not require a second fifteen-case proxy matrix.

`tools/benchmark_suite.ps1` runs those cases sequentially with the webcam enabled (camera 0 by default): five regional cases at each speed, Midtown 50m/s proxy ON paired with its regional OFF run, and Full 50m/s proxy ON for the long memory trace. Regional durations are 900s at 10m/s and 180s at 50m/s and 100m/s; the paired run is 180s. Full memory uses **1900s**, allowing at least two recorded full circuits after the player excludes the initial partial circuit. The planned measured total is 2h19m40s, plus seventeen 15s warmups and loading.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark_suite.ps1 -DryRun
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark_suite.ps1 -CalibrationDirectory '<private calibration directory>'
# Select cases by wildcard. This leaves all other acceptance cases pending.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark_suite.ps1 -CaseFilter 'Midtown-*'
# Explicit short exploration/baseline, never a qualification shortcut.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/benchmark_suite.ps1 -CaseFilter 'Midtown-50' -DurationOverride 60 -NoTracker -DryRun
```

Each suite creates a new GUID folder under ignored `BenchmarkResults/`, preserving every case's original frame/JSON/GPU/log files. `suite-progress.json` records pending/running/recorded/failed/interrupted status and actual artifact paths. Collection stops at the first failure unless `-ContinueOnError` is supplied. Existing launcher/benchmark `finally` blocks own player/tracker/GPU cleanup; the suite does not stop unrelated runs. `recorded` means files were collected, not performance passed. `-NoTracker` is labeled a baseline; `-DurationOverride` is explicitly exploratory and never silently shortens the standard durations.

Suite case directories are nested, so pass them explicitly to the analyzer:

```powershell
$suiteDirectory = '<completed suite directory>'
$caseDirectories = @(Get-ChildItem -LiteralPath $suiteDirectory -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'benchmark.json') } | ForEach-Object { $_.FullName })
& python ./tools/analyze_benchmarks.py @caseDirectories --output-json (Join-Path $suiteDirectory 'aggregate.json') --output-markdown (Join-Path $suiteDirectory 'performance-report.md')
```

`tools/benchmark.ps1` creates a unique output directory by default and starts a hidden, owned NVIDIA driver collector when `nvidia-smi` is available. It writes `gpu.csv` beside the frame report and stops the collector in `finally` when the shared launcher returns, including failure paths. `-SkipGpuLog` disables it; `-GpuIntervalMs 500` samples twice per second. Existing benchmark/GPU result files are not overwritten. Collector logs remain beside the report so missing or failed measurements are explicit.

`process.csv` records the exact player executable's Windows working set and private bytes at 1Hz. `tracker-process.csv` records the exact tracker virtual-environment executable and its verified descendants, including Windows' separate base-Python child. Descendant ownership uses PID/parent-PID and creation-time checks; it never reads command lines or includes unrelated Python processes. Current ancestry is verified for every counted sample. Missing counters or incomplete ownership make the corresponding total unavailable. The analyzer aligns samples to the measured interval and reports game and tracker separately. The sum of their individual private-byte peaks is a conservative upper bound, not a measured simultaneous peak. Driver VRAM remains a separate measurement.

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

This explicit mode holds movement and city residency while toggling the same comparison APIs, then restores original states, input and streaming. It never saves preferences and has no effect on normal startup. Run it separately from `--benchmark`, `--screenshot-out` and `--fade-proof-out`; `-NoTracker` is available for a labeled rendering baseline. It uses existing/default calibration for QA without opening the startup form, so first prepare measured calibration for physical head-tracking checks.

The report records state checks, image writes, round-trip restoration and fingerprints of rig/screen transforms, calibration, city/source materials, lighting and pipeline. It records requested Near fade values, which do not prove the rasterized appearance. Head observations continue, so the eye can move while tracking is enabled. Inspect every image for window/surface changes, alignment and TAA artifacts; passing report invariants do not replace image or streamed-gameplay review.

## Standalone opaque distance-fade proof

Use a separate rebuilt standalone run with `--fade-proof-out <directory> --quit-after-fade-proof` to test whether the actual converted Near materials honor the `_CityDetailOpacity` float property block on the GPU. The game uses its `Manhattan/CityDetailLit` shader for Near runtime variants and keeps the source Lit PBR maps, UV transforms and alpha state. Non-Near shaders and imported materials remain unchanged. A strong Resources material reference and source keyword-combination templates retain the shader/PBR variants in the player. Its opacity clip compiles without depending on an LOD keyword. This is separate from the eight comparison captures: only this explicit QA path temporarily selects AA=None and disables SSAO renderer features, fixes the physical eye with Tracking OFF, holds movement/streaming and hides UI. SSAO's per-frame blue-noise variation would otherwise invalidate exact repeated RGB controls. Normal startup and comparison switches do not alter AA or SSAO.

```powershell
$fadeDirectory = [System.IO.Path]::GetFullPath('BenchmarkResults/fade-proof-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$fadeArguments = '-screen-fullscreen 1 -window-mode exclusive -screen-width 1920 -screen-height 1200 --fade-proof-out "' + $fadeDirectory + '" --quit-after-fade-proof'
& ./tools/run.ps1 -NoTracker -PlayerArguments $fadeArguments
# This rendering baseline does not certify webcam tracking; omit -NoTracker for an owned webcam run.
```

After a 30-second warmup and up to 30 additional seconds for pending loads, it captures `near-1.png`, `control-1.png`, `near-0.png` and `restored-1.png`. Each saved PNG is decoded before exact RGB hashes/differences are calculated. Throughout the four images, the only city-rendering change is the requested fade vector on active opaque Near renderer property blocks. Renderer/GameObject visibility, material references, imported assets, lighting, city transforms and physical calibration remain unchanged. Original complete property blocks, AA, each SSAO feature's original active state, tracking, input, streaming and UI states are restored; preferences are never saved.

`fade-proof.json` records the actual build identity, output/render size, renderer/shader/frustum/property-support evidence, original/requested opacity floats, pose/calibration/material/light/pipeline/UI/geometry fingerprints, SSAO disable/restoration checks, RGB sample counts, hashes, changed pixel count, mean/max RGB difference and changed-pixel bounds. A pass requires visible Near evidence, settled city loading, stable repeated/restored opacity-1 images, an opacity-0 image with different RGB pixels, preserved invariants and restored states. No Near evidence, zero RGB difference or an unstable control remains **inconclusive**. Exit codes are 0 for pass, 2 for inconclusive and 1 for failure. Inspect the PNGs alongside the report: a pass proves the tested Near draws affected the standalone output, not all distances or performance targets.

The live-GPU PlayMode test `OpaqueDistanceFadeChangesRenderedCoverage` checks an emissive Lit cube through the custom Near shader; batch/null graphics runs intentionally skip it. `NearFadeVariantsPreservePbrAlphaAndUvAndRestoreImportedSource` separately tests PBR/alpha/UV preservation, reversible surface variants and last-reference release. Neither test replaces standalone evidence for retained build variants and real Near materials.

During implementation, controlled live-GPU tests of the original stock URP Unlit and Lit fade path both passed with no skips. A separate original-path standalone capture at 1920×1200 recorded 52 opaque Near targets, eight intersecting the main view, and identical RGB images for all four opacity states. Its loading, scene invariants and state restoration checks passed, but that zero-difference result was **inconclusive**. The cause could not be attributed to reserved property handling, build variants or occlusion from those measurements. The new custom-property path remains pending until its own rebuilt standalone capture produces qualifying RGB differences; stored MPB values and Editor cube passes cannot certify it.

## Release reproducibility

Clone the published repository into a new folder without the original sibling technology checkout or expanded city. Run setup with the private release, bake, Unity tests, Windows build and webcam launch. Confirm that a second setup/bake is repeatable and that modified local source inputs are preserved and rejected rather than overwritten. Record the clone commit and resulting build hash in the release report.

```powershell
# After the reviewed commit and private source release have been pushed:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify_clone.ps1 -Revision <full Git commit> -Run -Camera 0
# A quick portable clone check without Unity or city downloads:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify_clone.ps1 -Revision <full Git commit> -StaticOnly
```

The command keeps the independent clone in ignored `.verification/` and records its exact commit, source version and Windows player SHA-256. `-SourceArchiveDirectory` permits an explicitly labeled offline-source variant; omit it when verifying authenticated private-release download. `-NoTracker` is a baseline variant and is not webcam acceptance. The isolated build uses the normal clean-source gate. A preflight normalizes only a generated Addressables GUID difference and Unity serializer trailing spaces on `m_LockedProperties` in `Resources/CityDetailMaterial.mat` and `Resources/WindowFrameMaterial.mat`, plus the empty AssetVersion `m_Name` in the latter, after verifying that every other byte matches HEAD. Authored scene/settings/material changes remain subject to the dirty-source gate.

The verification record reads source version/release and technology revision from the **cloned revision**, and fingerprints its source manifest and tracker lock. A static-only run records that no sources were downloaded and no player was launched. Setup requires the committed, tested tracker lock; it cannot silently fall back to a different dependency set.

## Current record

As of 2026-10-10, reusable changes published in [technology commit `960d5de55037a3fe2739ed055c8de091b8c54aed`](https://github.com/lollipop190000/head-tracked-display/commit/960d5de55037a3fe2739ed055c8de091b8c54aed) passed the following compatibility checks. The viewer's UPM, launcher and tracker are pinned to that commit.

| Validation context | Recorded result |
|---|---|
| Head-tracking demo | 39/39 EditMode and 22/22 PlayMode tests passed |
| Minimal consumer without URP or demo | 39/39 EditMode and 11/11 PlayMode tests passed |
| Runtime-only consumer without URP or Unity Test Framework | Windows build succeeded; standalone package smoke exited 0 |
| Parameterized shared launcher | 21 assertions passed |
| Manhattan portable checks | 74/74 Python data tests and 8/8 PowerShell assertions passed |
| Manhattan Unity tests after scene portability and streaming fixes | 42/42 EditMode passed; PlayMode 15 passed, 1 GPU-only fade test skipped; zero failures/errors |
| Custom Near shader live-GPU test | 1/1 passed on the actual GPU, zero skips; standalone city verification remains separate |
| Deferred tile-release lifecycle | All 5 added PlayMode tests passed; the combined PlayMode suite has 15 passed and 1 GPU-only skip |

Original package XML/build/smoke evidence remains in the technical checkout's ignored `Builds/ManhattanPackageValidation/`. The runtime-only smoke covers projection, frame/source ownership, collider removal, JSON calibration and feature round trips. These package checks do not establish the Manhattan player's visual quality or frame rate.

Source transport and extracted-input hashes have been verified against all six ZIPs and their 370 entries after the v2 source replacement. The two revised ZIPs retain byte-identical geodata/provenance/terms and semantically identical materials and embedded texture contents; the four detail/vegetation archives retain their original hashes. Sixteen city/source/notice data tests cover the 243-tile inventory, digest mismatch, idempotent extraction, local-edit preservation, path escape rejection, ZIP symlink/case-alias rejection, manifest totals and required provenance/terms, catalog source/bounds/layer/exact-replacement-count invariants, verified build notice copies and the real small GLB fixture's header, vertex bounds, index ranges and one-time coordinate conversion. Five PowerShell assertions check multi-GPU parsing, timestamps, numeric counters, unsupported values and CSV round trips.

The driver collector also wrote actual RTX 4070 Laptop samples (8188MiB total, driver 591.74). A mock launcher failure retained its CSV samples, terminated the owned collector process and removed its stop file. This checks logging and cleanup only; it does not measure the viewer's VRAM or frame rate.

Hardware metadata capture independently reported a 2880×1800@60Hz prelaunch desktop, AC online and the same GPU/driver. `power.limit` was unavailable on this laptop while the enforced/default/minimum/maximum readings were exposed; the environment record retains that distinction. Three additional pure PowerShell assertions cover unavailable power limits, numeric readings and environment JSON round trips.

An isolated Windows x64 Python 3.13.5 environment passed `pip check`, all ten tracker protocol/pose/lifetime tests, and a CPU Face Landmarker smoke test using the SHA-256-pinned model and a blank synthetic image. `tools/tracker-requirements.lock.txt` records this environment with MediaPipe 1.1.0, NumPy 2.5.3 and a single OpenCV contrib 5.0.0.93 provider. `tools/test_tracker.py` repeats its interpreter, every locked dependency version, provider/model checks and CPU inference without opening a webcam. The existing tracker environment was not changed. Webcam/head-movement acceptance remains separate.

Full-city Windows builds have succeeded with 10,038 independently baked tiles, 68,693,368 triangles across all source/detail layers and 44,567 building records. Every generated tile's native dependencies were checked to exclude the original Core GLB. Standalone rendering and the eight comparison combinations plus their restoration were inspected on the actual RTX 4070 Laptop GPU. Near-detail opacity was also checked in a settled standalone view: opacity 1→0 changed 120,578 RGB pixels, and restoring 1 reproduced the first image exactly. That proof temporarily disabled TAA/SSAO only for deterministic capture and restored them; normal comparison switches retain rendering settings.

The first two 180-second Midtown 50m/s measurements recorded two complete circuits each at an actual 1920×1200 render/output. Their frame P95 was 16.683ms, P99 16.684ms and maximum 33.337ms, with no ≥50ms frame and no tile-load failure. The second run recorded a 2.063GiB player working-set peak. Its streaming P95 was 2.319ms, above the initial 2ms streaming goal. These are limited baseline observations, not acceptance of all city routes. The webcam process was connected but fresh face observations were 0%; regional webcam qualification and physical head movement remain pending.

A webcam startup run exited cleanly, released its owned tracker, and the camera could immediately be reopened to acquire a frame. No captured webcam image was retained. Ten regional measurements, matched proxy comparison, long memory stability and manual head-motion/TAA checks remain separate acceptance work.

Local URP 17.3 code inspection confirms that the render pipeline applies TAA jitter to the camera's off-axis projection and keeps an unjittered matrix for motion vectors. Reset-history calls also reset the persistent motion-vector state. The custom Near shader uses the stock object-motion pass with matching opacity clipping. This source review and projection tests do not replace moving-head image checks for temporal ghosting, facade shimmer or the first frame after a history reset.

## Published source and initial fresh-clone record

The [private viewer repository](https://github.com/lollipop190000/manhattan-headtracked-viewer) now contains initial `main` commit `030c241af832b7f96f6d7dda3e68bce05bb9c424` and its LFS fixture. Its private visibility and remote commit were checked after push. [GitHub Actions run 37970061969](https://github.com/lollipop190000/manhattan-headtracked-viewer/actions/runs/37970061969) completed successfully on that commit.

[Source Release `city-source-v2`](https://github.com/lollipop190000/manhattan-headtracked-viewer/releases/tag/city-source-v2) is published on the same commit. All six original ZIPs plus `source-manifest.json`, the reviewed Release notice, MIT license and third-party credits are uploaded. Their ten server-reported SHA-256 digests and byte counts match the local canonical manifest/notices, and the published body matches the reviewed notice. Source version is `manhattan-2026-10-10-v2`; manifest SHA-256 is `82bc35bea819762e3832210187232cafa8e16bea9fd92ecf3cf8dc2f683f8bc7`. This source publication does not include a validated Windows viewer binary.

An independent GitHub clone in ignored `.verification/clone-aad98656e9e24628803810ccdf2db6da` checked out that exact initial commit. Its LFS fixture was downloaded as the actual 383800-byte GLB, with SHA-256 `88873443df2dc51fa98ca4e8cccb9d7f8e30b4b4bc1b13c6710ba9dde9fa59e8`, and its 27 Python tests plus eight PowerShell assertions passed.

That clone then ran `tools/setup.ps1 -SkipUnity` without an offline-source override: authenticated GitHub Release downloads supplied all six ZIPs, and extraction verified the hashes of all 370 files. The setup fetched technology commit `960d5de55037a3fe2739ed055c8de091b8c54aed` into its own tool checkout and prepared a new Python 3.13.5 tracker environment. Exact dependency-lock verification, `pip check`, ten tracker tests and the pinned model's CPU smoke test passed. No Unity Editor, city bake, player or webcam was launched during this preparation.

The independent clone subsequently resolved Unity packages, imported and converted the complete city using its downloaded sources, and passed the then-current 41 EditMode tests plus 9 PlayMode tests (one GPU-only test skipped in headless mode). Its native tile/catalog counts match the original checkout; the generated catalog JSON is byte-identical with SHA-256 `78a9dbe7b69399e7cf7982ec73076123ddf5b81378f9775d399d91f9aee7f38b`. Neither generated assets nor the original checkout's Unity cache were copied into it.

Token-free local evidence is retained under ignored `.verification/` and the clone's `Builds/verification/`. Final-commit Windows build and execution are recorded separately after the build portability fix. Only those actual results can complete the clone-to-build criterion.
