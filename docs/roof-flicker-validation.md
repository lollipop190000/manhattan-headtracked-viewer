# Distant roof flicker — 2026-10-13

This follow-up is on `codex/roof-flicker-20261013`, based on the existing
`codex/city-overlap-20261012` wall fix. The source city, tracking package,
v7 ownership map, residency policy and comparison controls are unchanged.

## Reproduction and cause

The rounded roof and elongated rounded-end roof were reproduced together at
city `(700, 400, 3200)`, pitch 18°, yaw 138°. This is a controlled reconstruction
of the reported roof region, not a claim to recover the screenshot's exact
unrecorded camera pose. The native Windows D3D11 captures use 1920×1200,
RTX 4070 Laptop GPU, synthetic physical eye `(-0.188, -0.058, -0.797)m`,
normal TAA, lighting and surface details. Residency settles at 334 tiles with
zero pending loads/failures and 174,870 masked Skyline triangles.

CPU geometry inspection identifies:

| Roof | Fallback | Detailed provider | Geometry |
|---|---|---|---|
| Rounded, about 1.4km away | `Skyline_2_3/0` | `Core_Manhattan_Core_2_3/18` | Hull Y 188.518997m; Core Y 188.518799m |
| Elongated, about 1.0km away | `Skyline_2_4/0` | `Core_Manhattan_Core_2_4/10` | Both planes Y 176.022003m, with different narrow/large triangles |

These partially retained hull roofs compete with the detailed charcoal roof.
The first also has about 0.2mm of source height rounding. Float CPU ray distances
on the narrow triangles are not precise enough to infer millimetre plane gaps;
the second roof's vertex planes coincide.

Disabling sun shadows or SSAO in the private diagnostic leaves the alternating
patches. Excluding only the fallback hulls removes them. Disabling TAA freezes
the interference pattern but does not fix the competing surfaces. The roofs
are also beyond the normal 150m shadow distance.

## Change

The existing private material variant recesses eligible, closed legacy hulls by
**1mm in world Y during camera rendering**. Recessing the whole closed hull
keeps its seams together. The detailed roof, imported mesh/material, tile root,
city coordinates, calibration and package projection are untouched. Existing
`Offset 0, 8` wall priority remains, without a slope term.

Forward, GBuffer, depth and depth-normal passes share the position helper.
Motion passes apply the same recess to raster positions and both current and
previous unjittered projections. Shadow, Meta and 2D passes retain stock
positions. Eligibility, private material reference counting and Surface
ON/OFF/ON restoration remain unchanged. This is a rendering allowance for the
rounded, untextured fallback, not a change to source building heights.

No ownership rebake or Addressables rebuild is needed when continuing from the
v7 wall-fix build. An older map still needs the existing v7 upgrade procedure.

## Evidence

The new GPU regression reproduces the measured 188.519/188.5188m roof pair.
The previous shader fails it at 50m, 5° grazing angle. The fixed shader is checked
at 50m, 1.25km and 4km; 5°, 10° and 30°; four jitter/head-offset samples; and
Surface ON/OFF/ON. Detail wins the small overlap, uncovered fallback survives,
and a genuinely 2cm higher roof retains foreground occlusion. Existing wall
depth, unsupported-material and material-lifetime tests are retained.

In 12 native stationary frames per phase, sampled roof interiors have the
following mean temporal standard deviation per RGB channel (0–255 bytes):

| Region, top-left pixel rectangle | Before | Fixed | Fixed with hull excluded (diagnostic control) |
|---|---:|---:|---:|
| Rounded `(833,310)–(887,317)` | 16.562 | 0.473 | 0.420 |
| Elongated `(471,521)–(501,528)` | 10.111 | 0.020 | 0.020 |

The remaining small variation includes normal antialiasing and roof-equipment
edges. These bounded samples and native frame inspection establish removal of
the alternating roof patches at this view; they are not a claim that all image
pixels are temporally constant. Raw diagnostic files and local builds stay
outside Git.

- 263 EditMode checks passed, with no failures or skips.
- All 172 PlayMode cases have passing results across the GPU suite and an
  isolated batch run of the 19 tracking/measurement lifecycle cases. The hidden
  interactive runner had observation-pause/steady-hold timing failures (and one
  stall-related inconclusive result). The 19 unchanged cases passed in batch
  mode, with graphics enabled and without changing their assertions or timing
  guards. This is not a claim that one interactive run passed all 172 cases.
- The focused GPU material/depth group passed 9/9, including the new roof
  regression and existing wall/2cm foreground checks.
- Portable validation passed: 153 Python checks, two existing skips, five GPU
  parser assertions and three benchmark-environment assertions.

Real webcam accuracy, whole-city performance acceptance, other graphics APIs
and XR are outside this rendering fix's validation. The pinned tracking package
is unchanged, so this change introduces no new runtime-package dependencies.

## Final local player

The Windows Player is built from `9fb5e71b56c0eb74f376da8cd0e8147420dbadbd`,
with all 32 runtime C#/shader/include/assembly-definition fingerprints matching
the branch. Unity's BuildReport records zero errors/warnings, 120,030,769 bytes
and 13.99 seconds. The identity explicitly marks this as a dirty diagnostic
build because private, opt-in evidence helpers are present. Normal startup does
not activate those helpers.

A final 48-frame native capture covers 7.08 seconds of synthetic left/right
head motion, requesting ±12cm about the reference eye. The filtered rendered
X range is -0.307348 to -0.068896m, about ±11.9cm around -0.188m. The two roofs
retain their detailed surfaces through the inspected motion frames; normal
subpixel boundary/antialiasing variation remains. Residency stays at 334 with
zero pending loads/failures. Capture overhead is not a performance benchmark,
and these are synthetic observations rather than webcam acceptance.

The existing local `Run-LocalBranch.ps1` selects `FullCityRoofPlayer` by changing
only its PlayerPath. The previous launcher bytes and Player are retained.
Personal calibration/preference files are not written by this workflow. The
public team guide selects the new feature branch; local executables, evidence
frames and calibration are not published to GitHub.
