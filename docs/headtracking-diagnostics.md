# Head-tracking diagnostics and regression validation

This change adds observability and synthetic regression coverage. It does not establish that a particular webcam or saved calibration is working correctly.

## Scope

- `ViewerSettingsUI` retains the existing diagnostics toggle and 250 ms refresh cadence, shows tracking state, freshness, estimator mode, screen geometry, and raw/input/rendered eye values, and reports the result of the explicit reference-capture action.
- `ViewerTrackingDiagnostics` formats existing public package state without changing calibration, tracking, filtering, or persistence. Projection input is labeled separately from rendered eye position: the package has no independent public filtered-eye getter.
- Reference feedback distinguishes failed capture, captured-and-saved, and session-only capture when saving fails. First setup and capture/save ordering remain unchanged.
- `ViewerTrackingDiagnosticsTests` adds 14 EditMode test methods / 22 NUnit cases.
- `HeadTrackingPipelineTests` adds 9 PlayMode tests with a synthetic observation source. Coverage includes calibrated basic and rigid XYZ motion, screen corners and cube vertices, freeze/resume with continued filtering and flight, hold/loss/staleness, reference capture, floating-origin rebasing, adaptive filtering, and distance correction.
- The test camera does not render. Projection assertions exercise the actual CPU camera matrices against an independent ray/screen oracle; they do not validate GPU rendering or TAA.

The technology package remains pinned to `960d5de55037a3fe2739ed055c8de091b8c54aed`. There are no package, launcher, workflow, generated-asset, automatic-capture, or first-setup changes.

## Safe Unity test execution

Use Unity **6000.3.22f1**, a fresh test process launched with **`-runTests`**, and a clean test scene. Do not run this fixture in an ordinary interactive Test Runner session.

The existing `ManhattanBootstrap.StartEmptyScene` runtime initializer can start the normal viewer and Python bridge when a catalog is available unless the process has `-runTests`. Fixture setup explicitly fails if this argument is absent or an active bootstrap already exists. This guard cannot undo startup that occurred before setup. The argument also does not suppress an authored bootstrap component already present in a scene.

The repository-prescribed commands are:

```powershell
unity test ./ManhattanViewer --editor-version 6000.3.22f1 --mode EditMode --report-format junit --output ./TestResults/editmode.xml --timeout 1800
unity test ./ManhattanViewer --editor-version 6000.3.22f1 --mode PlayMode --report-format junit --output ./TestResults/playmode.xml --timeout 1800
```

Confirm the installed `unity` wrapper launches the editor with `-runTests`; its implementation was not available in the preparation environment. Do not remove the guard to obtain a pass. Treat timing-related **Inconclusive** results as unverified coverage requiring a rerun.

Also run the normal repository checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1 -DataOnly
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test.ps1
```

## Pre-publication evidence, 2026-10-10

- Prepared against viewer commit `9ef8f8051de89a37e2e4f04c334b0be8be845deb`; source/API baselines and added metadata were checked.
- Both patches applied cleanly in either order to the supplied partial source bundle, producing identical content for all 9 code/metadata files. All 27 supplied existing files matched the pinned baselines substantively: 26 matched exactly; `docs/validation.md` differed only in line endings. This does not establish the desktop checkout's full HEAD, dirty status, or untracked files.
- Static scope/delimiter/API checks, exact combined-output comparison, and whitespace checks passed. These checks do not compile C#.
- In an isolated Linux source copy, source-manifest validation, package/editor pin checks, and Python syntax checks passed. Python discovery reported 74 tests: **57 passed, 16 platform-dependent skips, 1 fixture-dependent failure**. The failed geometry test received the 131-byte Git LFS pointer instead of the required 383,800-byte GLB; its geometry coverage remains unverified in that environment. This is not evidence of a product regression.
- The existing GitHub workflow checks out with `lfs: true` and runs portable Python and PowerShell checks. Consult the PR's exact-commit checks for their result. That workflow does not compile C# or run Unity.
- **Not run during preparation:** C# compilation, Unity EditMode/PlayMode, full `tools/test.ps1 -DataOnly` (PowerShell unavailable), Unity UI, Windows player, hardware webcam, and GPU/TAA checks. Keep the change in draft until the relevant runtime validation is completed.

## Runtime acceptance still required

1. Run the new EditMode and PlayMode fixtures and preserve their results; resolve any compilation or setup errors.
2. Check diagnostics OFF/ON, no tracker, fresh/stale input, tracking OFF with continuing observations, depth lock, and the actual basic/rigid estimator modes. Check the reference canvas and actual display size, wrapping, and drawer scrolling.
3. With disposable calibration data, test repeated failed capture, successful explicit capture, and a forced save failure. Check close/reopen behavior and unchanged first setup. Do not overwrite a personal reference for testing.
4. Validate the real webcam, saved calibration, enabled-camera output, and GPU/TAA behavior separately. Synthetic math tests cannot establish those outcomes.

### Existing package edge case

Stale tracking is rejected before the calibration mutator, which the stale-capture test covers. Separately, a usable observation with a calculated eye span of at most 1 pixel can mutate in-memory reference fields before `DisplayCalibration.CaptureReference` returns false. The UI therefore reports failure without promising the previous reference remained unchanged, and does not save on false. Atomic capture failure would require a separate package change.
