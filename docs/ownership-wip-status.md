# Latest scoped streaming work, 2026-10-10

This followup builds on `633c6fc024f34ced9c1c102160bcbc90f111caa9`, which fixed the confirmed same-camera blank facade/roof regression. Its push CI run38065434465 and PR CI run38065438159 both completed successfully. Those jobs are portable source/repository checks: Python81ran with18platform skips, plus5GPU-parser and3environment-parser assertions; they do not run Unity. Main and PR #1 remain unchanged. Draft PR #2 remains WIP.

## Short cost isolation and minimum reduction

A separate optional-instrumentation Player used15s warmup and30s Midtown100m/s measurement, noTracker, no capture,1835measured frames. Primary nested phase totals are diagnostic, not a new acceptance gate. Streaming1343.18ms total includes ownership plus post-publication visibility688.22ms(51.2%), evaluation180.55ms, request169.80ms, completed-load150.00ms, Near feedback127.42ms and release23.97ms. Inside those phases Core coverage preparation106.40ms and atomic Core revalidation57.92ms are distinguished; Instantiate itself is19.12ms. Do not add inner phases again. This supports inspecting repeated CPU validation/mask work before changing assets or GPU effects.

Only redundant work is reduced: reuse shared-material contract reads within a single no-yield Core coverage check, clearing the cache before each Core and on every Tick exit; skip exact-owner ordinal writes when a complete validated cohort will immediately overwrite all eligible hull bits. Partial ownership still uses the original exact correspondence. The fresh atomic publication cache and all root/mesh/material/cohort rechecks remain. No budget/threshold reduction, visibility radius change, material override, geometry regeneration, new model, engine/package change, webcam launch or20-minute requirement is introduced.

## Fixed visual scope and exact source

Current SkylineGeometryOwnership.cs SHA256 `cdadfb8646903798e90cc5d5eef60458383f1464f63bf4fd36cbb3b5b7d7c53c`.
Current normal diagnostic Player runtime assembly SHA256 `71203dd87f96acc75c60eea7589c435b45f9232dfad324d8031626c3a68b5d73`.
Current continuous roundtrip MP4 SHA256 `4ee00e3cb30cbce64c8ce4a7fdf29005d32c789c3c2fe543351c47b74399ddcd`.
Same street and roof poses preserve the restored windowed facade/roof surfaces. Mean absolute RGB difference against public C8 is0.2382/255 street and0.4519/255 roof; against fixed633c6fc it is0.3168 and0.3870. TAA/timing prevents bit-identical images. The16s east400m/back400m roof/LOD route used481actual backbuffer frames,29.9707fps,1920x1200,TAA/SurfaceON,noTracker,normal streaming/origin shifts,pending<=2,failures0,readback/skip0. Variable timestamps are retained with maximum0.669ms encoding deviation; no interpolation, trimming or accelerated playback. The free-flight route passes close to/through existing building geometry with collision proxies disabled. Original facade occlusion restoration is distinguished from global perceptual flicker acceptance, which remains pending. Private media/logs are excluded from Git.

## Current checks and limitations

At the initial optimization source69505f06, full local PlayMode110/110 passed. After adding only cache cleanup on every Tick exit, current sourcecdadfb86 focused Skyline30/30 passed. These are overlapping scopes; do not claim140 unique tests or a full110-case rerun at the final cleanup SHA. The previous fixed633 source's full105 and focused30 union has110unique cases, with25overlap. Local results and source hashes remain explicitly scoped; no new remote CI result is assumed before this followup's checks finish.

Current captureOFF normal Windowed1920x1200 Midtown100m/s noTracker TAA/vSync1,target60,15s warmup90s measurement: all5372measured rows, nearest-rank engine frameP95=16.6832ms,P99=16.6848ms,max=42.9503ms,>=50ms=0; streamingP95=2.3883ms,P99=3.2657ms,max=7.9414ms. Original streaming2ms gate still fails. GPU sampled P955.2685ms; CPU main P954.3363ms. CPU FrameTiming P99=24.9814ms differs from engine delta-time P99 and includes waits/asynchronous sampling; do not interpret the lower engine percentile as a controlled improvement or actual absence of presentation hiccups. No statistical paired improvement claim is made. All-region/fullscreen, real valid webcam observations, long stability and perceptual acceptance remain pending;20minutes is not a required new gate.

## Branch and running

Use branch `goat/ownership-wip-20261010` / draft PR #2. The default main build is not this source. Preserve existing user checkouts; use a separate owned checkout if reproducing the branch. Reuse the current hash-verified city/catalog/sidecar/bundles rather than regenerating assets. The sidecar schema/rule and sourceVersion manhattan-2026-10-10-v2 are unchanged.

For an existing built Player, use the repository's public launcher: `tools/run.ps1 -NoTracker -PlayerPath <this-build-executable> -CalibrationDirectory <separate-disposable-directory> -PlayerArguments "-screen-fullscreen 0 -screen-width 1920 -screen-height 1200"` in PowerShell7. Pass authorized bundles via MANHATTAN_CITY_BUNDLES if using a diagnostic build. NoTracker/synthetic calibration is a diagnostic mode, not webcam validation. Do not overwrite personal calibration. Optional AsyncPlayerCapture.cs attaches only for MANHATTAN_ASYNC_CAPTURE; unset it and MANHATTAN_PLAYER_TELEMETRY/MANHATTAN_STAGE_COSTS/MANHATTAN_BINDING_PROBE for manual interaction. Capture helper is source-only outside Assets in this repository. Media cadence is not product FPS.

Historical fixed633c6fc evidence follows; its old hashes/measurements are not the current optimization hashes.

---

# WIP ownership branch: complete-cohort visual fix

This followup builds on shared WIP `3e8d038be1d21b0251da84704c1299d9bf1ba316`, based on public `c8f3904b17ec1cd4d3471bd59e198ce10d429957`. Main and PR #1 are unchanged. Draft PR #2 remains unfinished; no merge or release is requested.

## Confirmed cause and minimum fix

The previous b99 ownership candidate left the eligible flat Skyline hull in front of textured Core surfaces even when all catalog replacements were ready. Exact oriented triangle matching cannot establish complete coverage when the simplified hull has a different tessellation. At the street diagnostic pixel1670,550, the old hull was1.240mm nearer than the intact brownstone Core; pixel1690,900 showed1.057mm. Core source/final submesh index hashes, material slots, textures, renderer enablement and SurfaceON remained unchanged.

Keep exact primitive ownership for partial replacement. Restore full eligible-hull retirement only when the frozen catalog declares exactly the expected replacement cohort, every distinct Core binding is valid, every Core is ready to cover, and original root/mesh/material contracts still hold. Revalidate the cohort immediately before atomic mesh/activation publication. Restore the fallback before owner departure. Imported textured or intentional/overlay/alpha/shadow-incompatible Skyline material intent remains preserved. No epsilon expansion, model migration, material override, engine/package update, shadow/TAA disablement or whole-root hiding is introduced.

In the fixed street/roof probes all9 expected replacements of Skyline_2_7 are bound/desired/committed/covering. Its eligible submesh falls from20586 source indices to0 final indices, while Core source/final geometry and material slots remain identical. Windows and roof surfaces return in the same camera poses. Mean absolute RGB difference against public C8 changes from3.9466 to0.4116 for street,13.0139 to0.4075 for roof. TAA/timing means these are not bit-identical images and do not prove universal flicker elimination.

## Source and actual Player evidence

`SkylineGeometryOwnership.cs` SHA256: `d5437c06b2060cfc9ee0cc05d98dc5049d273e2556589929c5ba647489e5736e`.
Actual fixed capture/benchmark Player runtime assembly SHA256: `0f65328871c891fa2ae97767879a25dbb663445b40526b0313e027ddbc095aa8`.
Continuous fixed MP4 SHA256: `377573f88626654eab1a0597608c51117736f38a9899e73a890d6d1bd322d28c`.
391 real backbuffer frames over13.0011711s, actual29.9973fps,1920x1200, TAA ON, SurfaceON, noTracker, no face capture, existing manhattan-2026-10-10-v2 data. Roof route moves east50m/s with normal streaming/origin shifts. Variable frame timing is preserved (maximum encoded timestamp deviation0.635ms), with no interpolation, trimming or acceleration; native anchor/raw orientation comparison MAD0. Capture max issue0.1725ms, copy7.4912ms, background write8.7999ms; requested capture cadence is not product FPS. Pending<=2, failure0, readback/skip0. Stills and MP4 are private Library evidence and are not embedded in Git.

Fixed probe v3 retained an old launcher revision label; it is not used as commit identity evidence. Current async launcher source SHA and fixed build hashes establish the source association. BuildIdentity in this diagnostic Player still records C8 plus dirty ownership, so verify the exact file/assembly SHA rather than treating that label as a public revision.

## Tests and current performance

Full local PlayMode105/105 passed; an additional focused Skyline suite30/30 passed after adding opposite-diagonal and pre-publication mesh/material/relation mutation cases. These scopes overlap and must not be added as135 unique tests. An earlier focused run failed2 newly authored cases because their fixture replaced vertices before clearing obsolete indices; the repaired fixture clears the meshes first. Failed XML/logs were preserved. The full105 suite preceded the final additional5 focused cases. The runtime source did not change between these validations and the fixed capture.

Current normal visible1920x1200 Midtown100m/s noTracker TAA/vSync1 target60 run used15s warmup plus90s measurement with captureOFF. All measured rows, nearest-rank: frameP95=16.6833ms, P99=24.0795ms, maximum=44.1166ms, frames>=50ms=0; streamingP95=2.4792ms. Original gates remain frameP95<=16.7ms, P99<=25ms, zero>=50ms frames, streamingP95<=2ms. Gate results: `{"frameP95": true, "frameP99": true, "noFramesGE50": true, "streamingP95": false}`. Do not replace these with an active-work subset. No statistically controlled improvement claim is made. CPU frame includes engine waits; this run does not certify real webcam, fullscreen, all regions,20-minute stability or perceptual flicker acceptance.

The prior frozen b99 portable checks81Python/8PowerShell,178EditMode/100PlayMode remain historical scoped results, not a clean checkout rerun of this followup. No remote CI pass is asserted.

## Reproduction and exclusions

Generate bundles/catalog/sidecar from authorized source assets with the existing tooling and [skyline-ownership.md](skyline-ownership.md). Generated city assets, derived sidecar JSON/hash, bundles, builds, logs, personal calibration and face data are excluded from this public branch. Existing schema2/rule legacy-flat-hull-v4-extra-material-safe and package versions are unchanged.

Optional helpers in tools/ownership_wip are source-only; copy into an owned diagnostic project's Assets before Unity import. AsyncPlayerCapture.cs attaches only when MANHATTAN_ASYNC_CAPTURE is set; pass MANHATTAN_CAPTURE_RUNTIME_SHA from the exact runtime file hash. BindingProbe.cs uses MANHATTAN_BINDING_PROBE. Use --no-tracker, separate HEADTRACKED_CALIBRATION_DIRECTORY, native1920x1200 Windowed Player, and authorized bundles via MANHATTAN_CITY_BUNDLES. Build via Unity Editor APIs. Never overwrite personal calibration or existing evidence. Historical ActualPlayerCapture.cs identifies b99; use the current async helper for this fixed source. encode_async_capture.py accepts the capture directory argument.

Street pose(1190.60266,1.304856,3515.447998), Euler(317.97827,343.10513,0); roof(1225,150,3585), Euler(35,208.5,0). Prepare loaded tiles and pending0 before native stills; compare the identical catalog, settings, poses and source hashes. BindingProbe records original/final submesh counts/hashes, material slots, activation, ray hits and cohort contracts. Run -runTests -testPlatform PlayMode -testFilter SkylineSidecarRegressionTests for focused cases, or omit filter for the full PlayMode suite, preserving separate output paths.
