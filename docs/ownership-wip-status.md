# WIP ownership snapshot — unfinished

This branch shares today's local ownership work for collaboration. It is based on `c8f3904b17ec1cd4d3471bd59e198ce10d429957` from `goat/headtracking-diagnostics-20261010`; main and PR #1 are unchanged. No merge/release is requested.

## Blocking evidence

Actual Unity 6000.3.22f1 / URP 17.3.0 normal Windows Player captures used the existing `manhattan-2026-10-10-v2` catalog, 1920x1200, TAA ON, webcam OFF, synthetic screen/eye calibration and normal streaming. UI canvases were hidden only for capture.

The candidate shows broad blank facade regions where the exact public C8 source shows windowed facades, including the right tower in the street pose. Roof surfaces also differ. Both poses match camera city position/rotation/origin, loaded tile counts (street391 / roof389), pending0 and failures0. Public runtime40files were verified against Git blob IDs; the shared existing runtime difference is CityStreamer integration plus the new ownership implementation. Do not attribute this solely to model quality. Submesh indices/material slots/renderer state are being inspected; no root cause or fix is claimed in this snapshot.

Latest candidate `SkylineGeometryOwnership.cs` SHA256: `b99f29aea6e80386bb2d3baf09ca22a043c6f16a491eddd59362f40b9ca27710`.
Frozen23-file input manifest SHA256: `58083ba937f310d0ef6eb85d4dc836e30ed93a372e374262d836b690741f1ad7`.
Actual capture Player runtime assembly SHA256: `f26dc6b5a97c358d12f923a39a893bb166c416578453defd3bc89d109327018d`.
Latest continuous capture MP4 SHA256: `6e50e3bb982a98e6b4f754d1e8a6d6ce3e59b109c0cc5b0bc90459de0cdda77a`.
Capture evidence ZIP SHA256: `4a9c6f46867502fa035d6256b4d3527349ea781066947a60bff4bfdc457d8496`.

The clip preserves171frames over12.9411953s (actual13.1363445fps, requested15). No frame interpolation, trimming or accelerated playback. GPU readback/raw disk writes cost up to26.113ms per frame; still PNG writes79–115ms. This capture rate is not product FPS or performance acceptance. Media remains in the user's private Library; it is not embedded in this public snapshot.

## Visible performance — failed

Original gates use all measured rows and nearest-rank percentiles: frameP95<=16.7ms, P99<=25ms, zero frames>=50ms, streamingP95<=2ms. The candidate visible repeated Midtown100m/s test (15s warmup /90s measure, noTracker, TAA, vSync1) recorded P9516.9655ms, P9933.0742ms, two>=50ms frames, streamingP952.2892ms. All four gates fail. Active-work subsets are diagnostics, not replacement gates.

The old global-dictionary control also failed original gates. The GPU power limits differed between runs, and FrameTiming samples are asynchronous, so this pair does not prove a statistical regression/improvement or exact hitch causality. CPU frame time includes waits. Real webcam, fullscreen, all-region, long memory and perceptual acceptance remain pending.

## Passing scope — not acceptance

For the frozen b99 source, Windows portable checks passed81Python tests and8PowerShell assertions. Full local Unity suites passed178EditMode and100PlayMode cases. The100PlayMode total includes private diagnostic tests not committed here. Technical-package ProjectionTests were not exposed by the viewer's manifest testables. Earlier smaller suites/harness results remain separately scoped. No clean checkout rerun or remote CI pass is asserted for this new WIP commit.

## Reproduction

Generate bundles/catalog from authorized source assets using existing project tooling. Generated city assets, sidecarJSON/hash, bundles, builds, logs, personal calibration and face data are excluded from Git. Sidecar generation and one-group validation are documented in [skyline-ownership.md](skyline-ownership.md). Derived full sidecar SHA256 was `9cb87dd09206c6530d920408b78883375ab5e7a449857cc867be9075bfb1c844`; regenerate when content identity changes.

The optional source-only diagnostic helpers in `tools/ownership_wip` must be copied into an owned diagnostic project's Assets before Unity import; they are not wired into normal builds. `ActualPlayerCapture.cs` attaches only when `MANHATTAN_CURRENT_CAPTURE` names a separate output directory. `BindingProbe.cs` attaches only for `MANHATTAN_BINDING_PROBE`. Use an owned Player, `--no-tracker`, separate `HEADTRACKED_CALIBRATION_DIRECTORY`, native1920x1200 window and existing bundles via `MANHATTAN_CITY_BUNDLES`. Never overwrite the user's saved calibration or existing output. Build helper uses Unity Editor APIs, not YAML edits. Source labels in the capture helper identify the frozen b99 candidate; update metadata if source changes.

Street city pose `(1190.60266,1.304856,3515.447998)`, Euler `(317.97827,343.10513,0)`; roof `(1225,150,3585)`, Euler `(35,208.5,0)`. Roof clip moves east at50m/s for13s with normal origin shifts/streaming. Compare exact public C8 and candidate with identical catalog/settings/poses. BindingProbe records source/final submesh index hashes/counts, original/final material slots, renderer activation and triangle ray hits at fixed image pixels.

Next: resolve the visible binding/coverage difference, retain imported material intent and SurfaceON/OFF reversibility, repeat same-camera native screenshots, then repeat meaningful timing checks separately from media capture. No new model/PBR/engine change is underway.
