# Head-tracking diagnostics and regression validation

This change adds observability and synthetic regression coverage. It does not establish that a particular webcam or saved calibration is working correctly.

## Scope

- `ViewerSettingsUI` retains the existing diagnostics toggle and 250 ms refresh cadence, shows tracking state, freshness, estimator mode, screen geometry, and raw/input/rendered eye values, and reports the result of the explicit reference-capture action.
- `ViewerTrackingDiagnostics` formats existing public package state without changing calibration, tracking, filtering, or persistence. Projection input is labeled separately from rendered eye position: the package has no independent public filtered-eye getter.
- The original diagnostic change distinguished failed capture, captured-and-saved, and session-only capture when saving failed. The opt-in workflow below supersedes the settings drawer's combined capture/save action with separate preview, apply, revert and explicit save actions. First physical setup is unchanged.
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

Stale tracking is rejected before the calibration mutator, which the stale-capture test covers. Separately, a usable observation with a calculated eye span of at most 1 pixel can mutate in-memory reference fields before `DisplayCalibration.CaptureReference` returns false. The original UI therefore did not promise unchanged in-memory values on failure. The workflow below previews capture on a clone, leaving live calibration unchanged on failure; the package mutator remains unchanged.

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

## Opt-in measured tracking workflow and reference preview

This follow-up replaces the drawer's combined face-reference capture/save action. It does not change the tracker, package pin, estimator, city scale or startup. First physical setup still saves only the explicitly entered physical values.

### Ruler declarations and marked holds

1. Match both viewport rulers to a real ruler. Enter the measured eye-to-screen distance and the intended measured RIGHT motion under **MEASURED TRACKING CHECK**. The initial values are suggestions from existing settings, not observations. Optional distance/motion uncertainty is a user declaration; negative values mean **UNKNOWN**, not zero error.
2. Explicitly confirm the current ruler values and both viewport spans. Confirmation belongs to the applied screen width/height and the actual camera `pixelRect`. A changed physical size or viewport invalidates it. Editing distance, motion or uncertainty cancels the current measurement, clears its holds and discards an unapplied reference preview. Confirm again and begin a new session.
3. Wait for two seconds of fresh valid steady input, then choose **Start A / B / RETURN check**. Flight input is paused for the session. At A, press **F8** when steady and hold still for five seconds. Move RIGHT by the declared ruler distance while keeping face direction and depth; wait until steady, press F8 at B and hold five seconds. Return LEFT to A, settle and press F8 for a separate five-second RETURN hold. **F9** cancels.
4. Inspect signed raw/input/rendered XYZ changes, reported pitch/yaw/roll changes, direction, Y/Z contamination and return agreement. The workflow never takes an absolute X ratio to hide a reversed direction and never fits a gain. Missing orientation is **UNKNOWN** and blocks metric interpretation. A/B data alone does not complete a report.
5. Completed reports are numeric-only local files under `measurement-sessions` beside the normal calibration directory. Names are unique and existing outputs are preserved. A report does not save calibration; no image or face landmarks are recorded or uploaded.

The gate counts only distinct positive observation sequences, not rendered frames. Stale/invalid/non-finite input, a viewing-range boundary, source sequence/time discontinuity, or tracking loss clears prior holds. Calibration/filter-context changes cancel the session. The readiness window is two seconds; a recorded hold is five seconds with at least 20 unique observations. Raw, input and rendered standard deviations must be within 5/5/15 mm in XYZ; supplied orientation must be within 2 degrees per axis. Lateral interpretation also screens Y/Z changes above 10 mm, angular changes above 3 degrees, signed direction and return disagreement above 10 mm. These are engineering diagnostic thresholds, not measured human tolerances or perceptual acceptance criteria.

Reported age is the packet's reported processing age plus elapsed receive age. Exposure timestamps are unavailable; neither true pose age nor motion-to-photon latency is established. User ruler declarations, configured IPD/FOV, estimator output and rendered response remain separate quantities. One lateral pair cannot identify IPD, camera intrinsics and XYZ scale simultaneously. Unknown uncertainty stays unknown. No metric calibration or perceptual pass is inferred from consistent lateral checks.

### Reference preview, apply, revert and save

- **Preview current face reference** requires fresh steady input and confirmed ruler declarations. Capture runs on a clone; failure leaves live settings and the saved file unchanged. The preview shows old/new focal pixels because reference capture can change the shared camera intrinsics supplied to rigid PnP, even though the rigid C# packet conversion applies no direct translation gain. IPD and camera pose remain configured assumptions.
- **Apply preview for this session** is a separate action. It rejects unconfirmed or changed declared distance, changed calibration or input dimensions, and a discarded preview. Reconfirming after an edit does not resurrect the old candidate. Applying updates session calibration and clears measurement samples, without writing the calibration file.
- **Revert applied preview** restores its previous session settings only when later calibration edits would not be overwritten. It does not save. Editing declarations retains an already-applied preview for this explicit rollback.
- **Save physical calibration** is the only drawer action that writes calibration. Unsaved changes disappear on a new launch, which loads the previously saved file. After reverting a saved candidate, explicitly save again if the restored settings should persist.

### Validation scope

The authored tests cover uniqueness, freshness, loss/restart, raw/rendered stability, wrapped and missing orientation, signed direction, Y/Z and rotation contamination, independent return, uncertainty declarations, cloned capture, changed preview context/declaration, explicit persistence and unsaved restart. Synthetic UI lifecycle tests exercise production composition with `--no-tracker`, including local report creation and the three declaration/preview/viewport invalidation regressions. No actual webcam is started by these tests.

Use a clean scene and the earlier safe Editor invocation with `-runTests`, `--no-tracker` and a disposable calibration directory. The new PlayMode filter is:

```text
CityStreamerLifecycleTests;HeadTrackingPipelineTests;ViewerCompositionTests;MeasurementWorkflowLifecycleTests
```

The workflow fixture asserts both safety flags and no pre-existing active bootstrap. The local test preparation harness used to create an empty scene is excluded from the public patch. Windows synthetic UI checks do not establish real camera calibration, actual movement distance, human depth perception or real-city performance. GitHub portable CI remains separate from Windows Unity verification.

### Reviewed follow-up Windows results, 2026-10-10

- Unity 6000.3.22f1 EditMode: **119/119 passed**, with 0 failures/skips/inconclusive, recorded **07:30:08Z–07:30:19Z**. The exact prior 78-test set is retained; 41 measurement/reference cases are added.
- PlayMode: **32/32 passed**, with 0 failures/skips/inconclusive, recorded **07:31:48Z–07:32:53Z**. The exact existing 25-test set is retained; seven synthetic production-UI lifecycle tests are added. They include actual A/B/RETURN sample collection, one numeric report saved once, unchanged calibration until explicit save, and the declaration/preview/viewport invalidation regressions.
- Existing offline Python pose tests: **5/5 passed**. A separate nine-case canonical-mesh probe used independently known intrinsics, translations and rotations: focal pixels derived from a synthetic eye-span reference recovered the known poses within 10 micrometres. This exact-model diagnostic does not validate real facial proportions, IPD or intrinsics and is not included in the Unity case counts.
- Portable Windows checks: **74/74 Python tests plus 8/8 PowerShell assertions passed** under the normal user account. A prior sandbox run failed on temporary-file write permissions; its output was preserved and does not constitute a product test failure.
- Synthetic settings and completed-result screenshots were inspected for wrapping and clipping. This verifies the tested Editor viewport only; intended-player layout and human interaction remain pending.
- Original tracked source files and saved personal calibration were preserved. Temporary scene-preparation code, generated settings, private calibration, images, logs, result XML, runtime reports and the offline probe are excluded from the public patch. The PR remains draft and this follow-up awaits independent review before publication.

The final targeted review also found a cancelled-session HUD remaining visible after a viewport change with settings closed. Visibility now refreshes before the idle early return. The additional synthetic regression checks cancellation, hidden instructions and restored flight input. The resulting PlayMode run passed **33/33**, with 0 failures/skips/inconclusive, recorded **07:46:24Z–07:47:32Z**; its test-name set retains all 32 cases from the preceding run. This remains a synthetic UI result, without a real webcam.

Final-source EditMode also passed **119/119**, with 0 failures/skips/inconclusive, recorded **07:48:59Z–07:49:07Z** after the HUD fix.

