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
