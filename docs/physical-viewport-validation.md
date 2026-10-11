# Physical viewport integration — 2026-10-12

This integration starts from viewer PR #4 commit
`99ee22f40a0864a09bddb9ce3a69d1c2af8d656a`. Its four predecessor PRs form one
ancestry chain; this branch does not replay them. The reusable package is pinned
in the UPM manifest, lock and launcher to
`2a33e1c0115bbc6b9c8e5d9709c3aff7c0dbb329`.
Unity remains 6000.3.22f1, URP 17.3.0 and glTFast 6.20.0.

## Reproduced defect and corrected contract

The old package projected every camera through the full centered physical screen,
even when `Camera.rect` selected only part of it. A synthetic observation sent
through the real estimation/filter/projection path produced a normalized-screen
error of 0.183106 at a point in front of the screen in the independent cropped
viewport test. This test failed before the package change.

The package now composes a normalized display-surface rectangle with `Camera.rect`
to obtain an aperture in the original physical screen coordinate frame. The eye
remains in that frame. It projects through the aperture's actual bounds rather
than recentering or scaling the eye. The minimal window frame and ruler use the
same aperture. Screen-height inference uses the uncropped render surface aspect
and cannot infer panel dimensions from a windowed panel mapping.

For an eye at `(ex, ey, -d)` and a point `(X, Y, D)`, the independent oracle uses
`xs = (d*X + D*ex)/(d+D)` and the corresponding vertical expression. The hit is
then normalized against the physical aperture. Distant points correctly approach
unit sensitivity to eye translation. No per-object parallax gain was added.

## Two explicit measurement modes

- **Measured render surface** remains the default, including existing saved
  calibration. Width/height describe the full game client surface before any
  camera crop. Recheck rulers after resizing. The eye reference is its center.
- **Full monitor panel** is opt-in under Settings → Viewport check. Width/height
  describe that monitor's complete panel and the eye reference is its center.
  The Windows Player maps its physical client position/size to the panel and
  then composes the camera crop. Scoped per-monitor DPI-aware reads use device
  pixels independently of rendering resolution. The prior thread context is
  restored immediately.

Editor/non-Windows use, render textures, minimized windows, partially off-screen
or spanning windows, and a move to another monitor cannot establish a valid panel
mapping. The last aperture is retained and Settings gives a reason. Measurement
confirmation requires a valid mapping and is invalidated by an aperture change,
even when camera pixel dimensions are unchanged. Return to the calibrated monitor
or explicitly remeasure and re-enable panel mode on the new panel. Calibration
values, observer pose and comparison preferences are not rewritten by these reads.

This adapter covers the normal Windows client and `Camera.rect`. A future internal
letterbox/render-texture compositor must explicitly supply its actual aperture;
the adapter cannot infer hidden compositor geometry. Mixed-monitor hardware and
multiple OS scale settings still require additional physical checks.

## Evidence and boundaries

All local reports use isolated projects/worktrees and disposable calibration.
Original working directories and the prior GOAT validation copy were preserved.
The latter's 81 dirty paths were classified privately: 53 exact published files,
6 published after newline normalization, 20 local/unpublished files, and 2
different files. They were not copied wholesale into this branch.

| Context | Actual result |
|---|---|
| Unmodified PR #4, absolute viewer project path | EditMode 210/210 passed, including the 32 source-replacement gate cases |
| Updated viewer and published package pin | EditMode 220/220 passed, zero skips |
| Updated viewer, live GPU Editor, synthetic observations | PlayMode 122/122 passed, zero skips, including rendered opaque-distance-fade coverage |
| Package in minimal consumer without URP/demo | EditMode 53/53 and PlayMode 12/12 passed, zero skips |
| Technology demo EditMode | 53/53 passed, zero skips |
| Runtime-only consumer, no URP/demo/test framework | Windows standalone build and visible synthetic projection probe exited 0 |
| Portable viewer checks | 149 Python cases: 147 passed, 2 skipped for unavailable Windows symlink privilege; 8 PowerShell assertions passed |
| Real Windows source/destination junctions | Both rejected; external target contents preserved |
| Actual built Midtown Skyline/Core cohort | 10 exact built prefabs loaded; partial mask → fallback → reentry → unregister round trip passed |

The visible standalone probe used synthetic eye coordinates, a synthetic physical
screen and an independent ray/screen intersection oracle. Windowed, cropped-camera
and fullscreen modes all reported a valid mapping. Maximum normalized-coordinate
error was below `1.7e-7`. The fullscreen client occupied the 2880×1800 monitor while
the rendering size remained 960×600, exercising the distinction between physical
pixels and render resolution. Earlier hidden/off-screen probe attempts were
invalid and are excluded from this result.

The full live Editor report is separate from the earlier batch runs. Batch mode
could not resolve the active URP shader passes in the streaming fixture and skips
the explicitly guarded GPU coverage test; it is not rendering acceptance. The
speed/report lifecycle test now explicitly omits its unnecessary screenshot,
avoiding an unsupported batch `WaitForEndOfFrame` and subsequent fixture leakage.
The production benchmark's default screenshot behavior is retained. Original NUnit reports, standalone output
and fingerprints are retained locally in ignored verification storage. Captures,
large city assets, logs and personal calibration are not part of this PR.

## Remaining acceptance work

There is no new full-city catalog, full-city ownership sidecar or city Player bake
in this integration. Original main's generated catalog and bundles were fingerprinted;
its ownership sidecar was absent. A separate project compiled just `Skyline_2_7`
from its exact built catalog and nine related Core prefabs. The compiled mapping
contained 1,690 matching triangle ordinals across five Core owners. With only
`Core_Manhattan_Core_2_7` presented, runtime ownership masked its 1,605 matching
triangles and retained the other 5,257 of the 6,862 Skyline triangles. Departure
restored the original mesh pointers, reentry masked the same 1,605, and unregister
restored the full hull. Imported indices remained unchanged. The separate sidecar
SHA-256 is `5b62ae1d805865c527c1a79464dddc4f0940095aff603a77d92a48712f3a8083`.

This is a real built-asset check with synthetic resident state transitions. The
initial offline compiler encountered an unrelated missing default Editor
initialization location before loading the explicitly supplied built catalog;
the follow-up runtime round trip used that catalog's explicit initialization data.
It is not a full-city bake, normal-window moving render or proof that every
reported roof/side artifact is resolved. Old performance records do not validate
this branch.

Real webcam freshness, physical head-motion perception, moving-head TAA/flicker,
fixed-pose city surface identification, paired 1920×1200 performance runs and the
actual source-ID model replacement pilot remain open. The historical streaming
P95 of 2.1380ms exceeded the 2ms target and is not a measurement of this branch.
The delivery catalog still represents 580 source parts; `runtimeReady` remains
false. No model was regenerated or automatically activated.

After a city Player for this exact revision is available, the short physical check
is: confirm rulers and eye center in each chosen measurement mode, move the head
left/right and forward/back with screen-plane/near/far targets, toggle tracking
OFF/ON, then compare windowed and fullscreen. Record valid observation fraction
and the actual viewport with the result; synthetic evidence cannot replace it.
