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
Monitor binding lasts for the current enabled session; it is not a persisted
hardware identity. Recheck the chosen panel and rulers after restarting or changing
hardware. The saved panel-mode preference does not certify those physical checks.

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
| Updated viewer, live GPU Editor, synthetic observations | PlayMode 126/126 passed, zero skips, including rendered opaque-distance-fade coverage, deferred-ownership coverage/rollback and publication-boundary revalidation |
| Package in minimal consumer without URP/demo | EditMode 53/53 and PlayMode 12/12 passed, zero skips |
| Technology demo EditMode | 53/53 passed, zero skips |
| Runtime-only consumer, no URP/demo/test framework | Windows standalone build and visible synthetic projection probe exited 0 |
| Portable viewer checks | 150 Python cases: 148 passed, 2 skipped for unavailable Windows symlink privilege; 8 PowerShell assertions passed |
| Real Windows source/destination junctions | Both rejected; external target contents preserved |
| Actual built Midtown Skyline/Core cohort | 10 exact built prefabs loaded; partial mask → fallback → reentry → unregister round trip passed |
| Visible Windows Player, isolated 10-tile cohort | 1920×1200, D3D11, RTX 4070 Laptop, TAA ON; all eight comparison combinations and restoration passed recorded invariants |

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

## Built-asset ownership and comparison evidence

Original main's generated catalog and bundles were fingerprinted and remain unchanged;
its ownership sidecar was absent. An initial separate project compiled just `Skyline_2_7`
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
This initial one-group check is not proof that every reported roof/side artifact
is resolved. Subsequent full-city diagnostic builds reuse the exact immutable
catalog and bundles with a separately generated 260-group ownership sidecar;
their measurements and moving-media scope are recorded in
[current streaming validation](current-streaming-validation.md). Original city
meshes, textures and bundles were not rebaked. Historical performance records
do not validate this branch.

A subsequent private Windows diagnostic Player used production viewer source
`34fb631165c941b7cdaed05b817874d7ed677eb8`, the new package pin and the exact
built bundles. Its catalog deliberately contained only this one Skyline plus
nine related Core records; its identity marks it dirty/experimental. This explicit
subset never replaced production assets or changed normal startup. It rendered
at actual 1920×1200 output and camera resolution, Windowed, D3D11, RTX 4070 Laptop,
TAA ON and VSync 1, with the tracker disabled and a neutral synthetic eye.

The baseline, eight Tracking/Window/Surface combinations and restored baseline
produced ten PNGs at one fixed wall pose. All recorded scene/rig/calibration,
lighting, rendering-policy and source-material invariants passed; preferences
were unchanged and original comparison states restored. Separate street, roof,
air-down and far fixed-pose PNGs also completed. Each of these five pose records
had 10 loaded tiles, zero pending loads/failures/unsupported meshes and settled
ownership. All nine Core replacements were present in those poses, so the
complete cohort correctly retired all 6,862 eligible Skyline hull triangles.
The earlier one-Core partial test is separate from this complete-cohort result.

Representative wall, street, roof, air-down and far stills were inspected for gross missing/pink materials and roof/side
geometry. They cannot establish temporal flicker, head-motion perception, general
city coverage, frame-rate targets or the new source-ID replacement loader. The
group contains existing Core/Skyline assets and excludes additive Near, Ground,
Vegetation and the 580-part new delivery. Binary/image/metadata fingerprints are
retained privately with the captures; no large assets, captures or logs are tracked.

## Remaining acceptance work

A subsequent user-authorized live webcam check used the unchanged runtime
`338c090`, an explicit private numeric observer and the same built city inputs.
Its first 180-second trace had fresh valid input, but the copied physical profile
differed from the user's confirmed measurements. It is excluded from physical
perception/metric acceptance. Original profile files remained unchanged.

Only the copied panel width/height and reference distance were corrected for a
separate 95.075-second session. Camera intrinsics, IPD, webcam extrinsics, stored
depth correction and filter settings were not fitted or overwritten. There was no
old basic face reference and no precise camera calibration; rigid estimation was
active. The session recorded 4,796 render observations and 2,839 distinct receive
timestamps. After conservatively omitting the first already-present packet,
2,838 positive sequence observations had no duplicate/reset or receive gap above
100ms. Valid fresh fractions were 100% at both render-frame and unique-observation
sampling; no tile-load failure was recorded.

Actual Tracking OFF and depth-lock intervals were collected, rather than inferred
from sent commands. Tracking OFF kept rendered XYZ fixed while raw/input movement
continued. Depth lock kept input/rendered Z stable after settling while raw Z
changed. Rig XYZ and the aperture remained fixed. The diagnostic limits were
1 micrometre for OFF eye/rig/aperture invariance, 1mm for rendered depth after
0.5-second settling, and at least 5mm of raw/input motion to exercise a comparison.
These are numeric consistency checks, not calibrated distance accuracy. The private
automatic control did not refresh the top comparison button label; the bottom
diagnostics and recorded display flags identify actual state. Normal user clicks
still update that button as before.

The user reported that larger-than-usual movements were okay, but could not
determine an improvement from the physical-profile correction. A depth-lock-specific
preference was not established. No facial images were stored. Numeric observer
overhead excludes this session from performance evidence; rotation contamination,
absolute eye-position accuracy, camera calibration, exposure-to-display latency
and general temporal flicker remain unverified. Both owned tracker sessions ended
and their camera resources were released.

Broader webcam robustness, calibrated physical head-motion perception, moving-head TAA/flicker,
identification of the user's specific offending surfaces, the complete regional
speed/proxy acceptance matrix and the actual source-ID model replacement pilot
remain open. Current full-city 1920×1200 no-tracker diagnostic measurements are
separate from those acceptance requirements. The historical streaming
P95 of 2.1380ms exceeded the 2ms target and is not a measurement of this branch.
The delivery catalog still represents 580 source parts; `runtimeReady` remains
false. No model was regenerated or automatically activated.

After a city Player for this exact revision is available, the short physical check
is: confirm rulers and eye center in each chosen measurement mode, move the head
left/right and forward/back with screen-plane/near/far targets, toggle tracking
OFF/ON, then compare windowed and fullscreen. Record valid observation fraction
and the actual viewport with the result; synthetic evidence cannot replace it.
