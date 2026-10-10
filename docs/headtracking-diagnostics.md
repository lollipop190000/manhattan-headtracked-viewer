# Head-tracking diagnostics and regression validation

This change adds observability and synthetic regression coverage. It does not establish that a particular webcam or saved calibration is working correctly.

## Scope

- `ViewerSettingsUI` retains the existing diagnostics toggle and 250 ms refresh cadence, shows tracking state, freshness, estimator mode, screen geometry, and raw/input/rendered eye values, and reports the result of the explicit reference-capture action.
- `ViewerTrackingDiagnostics` formats existing public package state without changing calibration, tracking, filtering, or persistence. Projection input is labeled separately from rendered eye position: the package has no independent public filtered-eye getter.
- Reference feedback distinguishes failed capture, captured-and-saved, and session-only capture when saving fails. First setup and capture/save ordering remain unchanged.
- `ViewerTrackingDiagnosticsTests` adds 14 EditMode test methods / 22 NUnit cases.
- `HeadTrackingPipelineTests` adds 9 PlayMode tests with a synthetic observation source. Coverage includes calibrated basic and rigid XYZ motion, screen corners and cube vertices, freeze/resume with continued filtering and flight, hold/loss/staleness, reference capture, floating-origin rebasing, adaptive filtering, and distance correction.
- The committed pipeline-test camera does not render. Projection assertions exercise the actual CPU camera matrices against an independent ray/screen oracle; they do not validate GPU rendering or TAA.

The technology package remains pinned to `960d5de55037a3fe2739ed055c8de091b8c54aed`. There are no package, launcher, workflow, generated-asset, automatic-capture, or first-setup changes.

## Safe Unity test execution

Use Unity **6000.3.22f1**, a fresh test process launched with **`-runTests`**, and a clean test scene. Do not run this fixture in an ordinary interactive Test Runner session.

The existing `ManhattanBootstrap.StartEmptyScene` runtime initializer can start the normal viewer and Python bridge when a catalog is available unless the process has `-runTests`. Fixture setup explicitly fails if this argument is absent or an active bootstrap already exists. This guard cannot undo startup that occurred before setup. The argument also does not suppress an authored bootstrap component already present in a scene.

The repository-prescribed commands are:

```powershell
unity test ./ManhattanViewer --editor-version 6000.3.22f1 --mode EditMode --report-format junit --output ./TestResults/editmode.xml --timeout 1800
unity test ./ManhattanViewer --editor-version 6000.3.22f1 --mode PlayMode --report-format junit --output ./TestResults/playmode.xml --timeout 1800
```

Confirm the installed `unity` wrapper launches the editor with `-runTests`. The verified PlayMode run documented below invoked the Editor executable directly rather than this wrapper. Do not remove the guard to obtain a pass. Treat timing-related **Inconclusive** results as unverified coverage requiring a rerun.

Also run the normal repository checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -DataOnly
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
```

## Verified validation, 2026-10-10

The authored source tested on Windows matched exact PR source commit `df12acb183746aab50af6c48c728923edfce828b`, with no code changes. The isolated copy had one generated Addressables settings difference, described below. These completed checks supersede the initial preparation environment's Unity, PowerShell, and Git LFS verification gaps.

- `tools/test.ps1 -DataOnly`: **74/74 Python tests and 8/8 PowerShell assertions passed**.
- Unity **6000.3.22f1** compilation: **0 errors**. The pre-existing `CityBake.cs:648` CS0618 warning remains: `Physics.BakeMesh(int, bool)` is obsolete in favor of `BakeMesh(EntityId, bool)`.
- Unity EditMode: **63/63 passed**, including all **22 new diagnostic cases**.
- Unity PlayMode: **25/25 passed**, including **16 existing tests and all 9 new pipeline tests**.
- The completed Unity runs had **0 failures, 0 skips, and 0 inconclusive results**. Result XML and screenshots were preserved with the local validation evidence.
- The successful PlayMode process used the existing Personal license and `-runTests`, without `-batchmode`, `-nographics`, or `-quit`, and exited with code 0. The initial headless attempt failed on license entitlement. EditMode ran through the normal Editor Test Runner and its XML was exported. The PlayMode fixture isolation guards were retained.
- Source preservation: hashes of the original viewer's 177 tracked files were unchanged, and both original repository worktrees remained clean. In the isolated validation copy only, Unity changed `ManhattanViewer/ProjectSettings/EditorBuildSettings.asset` at `m_configObjects.com.unity.addressableassets` (a generated GUID). Scenes and code were unchanged; the tests ran with that generated setting. This generated delta is not included in the PR.

### Passing Unity execution

EditMode ran in the normal Unity Editor Test Runner, and the results were exported as `editmode-ui.xml`. The recorded run was **2026-10-10 01:49:08–01:49:18 UTC**.

PlayMode ran in a fresh normal-mode Editor process using the following sanitized PowerShell invocation. Replace the placeholders with the editor executable, isolated Unity project, and results directory. The recorded run was **2026-10-10 01:58:43–01:58:52 UTC**.

```powershell
$env:HEADTRACKED_CALIBRATION_DIRECTORY = "<RESULTS>/disposable-calibration"
& "<UNITY_EDITOR>" -projectPath "<PROJECT>" -runTests -testPlatform PlayMode -assemblyNames Manhattan.Viewer.PlayMode -testResults "<RESULTS>/playmode-nonheadless.xml" --no-tracker -logFile "<RESULTS>/playmode-nonheadless-editor.log"
```

The command intentionally omits `-batchmode`, `-nographics`, and `-quit`. Disposable calibration and `--no-tracker` isolate this validation from personal calibration and the real tracker. The XML and editor log remain local because they can contain private machine paths.

### Prior portable and source checks

- Prepared against viewer commit `9ef8f8051de89a37e2e4f04c334b0be8be845deb`, with technology revision `960d5de55037a3fe2739ed055c8de091b8c54aed`.
- Both patches applied cleanly in either order to the initial partial bundle. All 9 code/metadata outputs matched; all 27 supplied existing files matched the baselines substantively (26 exact, one documentation file with line-ending differences).
- Static scope/delimiter/API, metadata, combined-output, and whitespace checks passed.
- The initial isolated Linux discovery run had 57 passes, 16 platform skips, and one failure caused by a Git LFS pointer in place of the GLB. Subsequent GitHub LFS checkout passed the real geometry test, and the Windows portable run above passed all 74 tests.
- Existing GitHub [push](https://github.com/lollipop190000/manhattan-headtracked-viewer/actions/runs/38010102499) and [pull-request](https://github.com/lollipop190000/manhattan-headtracked-viewer/actions/runs/38010129961) CI passed for the tested source commit: 58 Python passes, 16 Windows-dependent skips, 0 failures, and 8 PowerShell parser assertions. Source/pin/tracked-file checks and Python syntax also passed. GitHub CI itself does not compile C# or run Unity.

## Supplemental perceptual-depth investigation

The [perceptual-depth validation report](perceptual-depth-validation.md) records two temporary visual diagnostics from 2026-10-10, the supporting perception literature and pinned source references, and the proposed human/real-city checks.

- The chamber/minimal-frame comparison used the actual two configurations with fixed cube geometry and matched eye positions, viewport, and AA. Their different openings and occluders were deliberately retained. Mask disagreement of at most 8 pixels is a pixel-area count, not an 8-pixel positional error; one outer rear target was invisible in the chamber condition at every tested pose.
- Temporal inspection covered 60 TAA OFF and 60 ON frames. Actual rendered eye trajectories differed by up to 4.66 mm, so a direct image difference cannot isolate TAA. No residual was detected beyond a 2-pixel contour margin under the chosen threshold, while the narrower 1-pixel margin found up to 160 faint pixels. These are bounded diagnostic results, not proof that TAA never ghosts or that people cannot see artifacts.
- The two temporary harnesses are not included in the product test counts or this PR's code. The recorded 63 EditMode and 25 PlayMode passes remain unchanged.
- Physical viewport/eye calibration is the first investigation candidate. Human depth perception, actual webcam behavior, and real-city rendering/performance have not been accepted. Readback/PNG timings are not FPS or motion-to-photon measurements.

## Runtime acceptance still required

The PR remains draft. Passing synthetic tests does not establish actual webcam behavior, rendered city output, or performance.

1. Check diagnostics OFF/ON, no tracker, fresh/stale input, tracking OFF with continuing observations, depth lock, and the actual basic/rigid estimator modes. Check the reference canvas and actual display size, wrapping, and drawer scrolling.
2. With disposable calibration data, test repeated failed capture, successful explicit capture, and a forced save failure. Check close/reopen behavior and unchanged first setup. Do not overwrite a personal reference for testing.
3. Validate the real webcam, saved calibration, Windows player, enabled-camera output, and GPU/TAA behavior separately. The committed pipeline-test camera is disabled; the separate temporary cube diagnostics do not verify actual city rendering or rendering performance.

### Existing package edge case

Stale tracking is rejected before the calibration mutator, which the stale-capture test covers. Separately, a usable observation with a calculated eye span of at most 1 pixel can mutate in-memory reference fields before `DisplayCalibration.CaptureReference` returns false. The UI therefore reports failure without promising the previous reference remained unchanged, and does not save on false. Atomic capture failure would require a separate package change.

## Manual viewport rulers and follow-up validation, 2026-10-10

This follow-up adds a **measurement aid**, not a completed physical calibration. Historical 63/63 EditMode and 25/25 PlayMode results above refer to authored source `df12acb183746aab50af6c48c728923edfce828b`. The new results below cover that source plus the reviewed five-file viewport-ruler patch (SHA256 `886ae24b759b66941ea67ecebd8b1562607626474dcb0b51f1f66b6e7f746768`). The remote parent `edc0ab049374a44eea240ab8e1a73c492938c2be` changed documentation only.

### Use the measurement aid

1. Open **Settings / Esc**, find **VIEWPORT CHECK**, and explicitly enable **Show manual 10 cm rulers**. The toggle starts OFF on every launch and is not saved as a preference.
2. Measure both horizontal and vertical spans with a real ruler in the actual content area. The spans use the currently applied screen width/height; compare them after applying your intended values.
3. Inspect the displayed camera `pixelRect`, last rendered URP target dimensions, and viewport/applied physical aspect ratios. A frame not yet observed is labeled unavailable/awaiting; an offscreen render target is not offered as a physical display ruler.
4. If measurements disagree, enter the measured content width/height through the existing fields and use the existing explicit **Save physical calibration** action. Also measure eye-to-screen distance separately. The ruler toggle does not apply, capture, correct, or save any values.

Each axis uses `viewportPixels * 0.10 / appliedPhysicalMeters`. Screen endpoints are converted through the actual Canvas transform, so the overlay follows viewport offsets, window dimensions, and Canvas scaling. Rendering at a lower URP render scale does not shorten the physical ruler. EDID and window position do not establish measured screen dimensions. No physical ruler measurement has been completed by this work.

### Follow-up Windows results

- Unity **6000.3.22f1** compilation: **0 errors**.
- EditMode: **78/78 passed**, **0 failures/skips/inconclusive**, including 15 ruler cases. Recorded UTC interval: **2026-10-10 03:50:08Z–2026-10-10 03:50:17Z**.
- Existing PlayMode: **25/25 passed**, **0 failures/skips/inconclusive**. Its exact test-name set matches the earlier 25-case run; no temporary harness is counted. Recorded UTC interval: **2026-10-10 03:51:26Z–2026-10-10 03:51:35Z**.
- Endpoint tests cover both screen coordinates of all four endpoints in offset viewports, lengths, default OFF and ON/OFF active hierarchy, and unchanged calibration. A real `CanvasScaler.ScaleWithScreenSize` case changes camera viewport dimensions/aspect and scaler reference dimensions/aspect.
- A separate temporary GPU check passed **1/1** for full/offset viewport, Canvas scales 0.7/1.4, and URP render scales 1/0.5. This check asserted RectTransform coordinates and captured PNGs; it was **not** itself an automatic pixel-mask test.
- A subsequent independent offline accent-color-mask analysis of the two saved PNGs checked line/end-cap positions. Maximum endpoint coordinate error was **0.925 px** for the full viewport and **0.661 px** for the offset viewport. These are raster coordinate errors, not the pixel-area counts from the chamber experiment, nor physical measurement errors.
- No temporary harness, generated EditorBuildSettings change, personal calibration, XML, private machine path, screenshot, log, build, or license data is committed.

The strengthened PlayMode run used the following precise filter to exclude temporary local harnesses:

```powershell
$env:HEADTRACKED_CALIBRATION_DIRECTORY = "<RESULTS>/disposable-calibration"
& "<UNITY_EDITOR>" -projectPath "<PROJECT>" -runTests -testPlatform PlayMode -testFilter "CityStreamerLifecycleTests;HeadTrackingPipelineTests;ViewerCompositionTests" -testResults "<RESULTS>/playmode-test.xml" --no-tracker -logFile "<RESULTS>/playmode-editor.log"
```

The EditMode run used `-runTests -testPlatform EditMode -assemblyNames Manhattan.Viewer.EditMode` with the same clean-scene/no-tracker/disposable-calibration restrictions. Local Windows Unity results are separate from GitHub portable CI.

### Acceptance still pending

The tool assists real-ruler measurement; it does not prove that applied dimensions or eye distance match the user's physical installation. Actual viewer drawer interaction/layout in the intended player/window, manual measurements, real webcam behavior, real-city rendering/performance, and human depth perception remain pending. Dynamic resolution and multiple displays were not validated. No automatic calibration, city scaling, parallax gain, tracker, package pin, or startup behavior changed. The PR remains draft.
