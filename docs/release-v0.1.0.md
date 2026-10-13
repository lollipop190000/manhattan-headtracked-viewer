# v0.1.0 — Windows release validation

This release brings the current Manhattan viewer, physical viewport mapping,
wall/roof overlap fixes, afternoon lighting and wider tree visibility together.
The reusable tracking package remains pinned to
`c5ddab14d12dcc8acc8b31048f5752686984f3ab`. The city source is
`manhattan-2026-10-10-v2`; runtime ownership uses v7 joint coverage.

## Thin white lines

The reported stationary shimmer was reproduced in native Windows captures.
With the camera and city fixed, disabling TAA jitter removed almost all temporal
variation. The production fix retains TAA and its subpixel sampling rather than
removing white trim or altering the imported road/building geometry.

| TAA setting | Previous | v0.1.0 |
|---|---|---|
| Quality | Medium | Very High, including current-sample filtering |
| History weight | .75 | .95 |
| Jitter scale | .75 | .75 |
| Variance clamp | .9 | 1.2 |
| Sharpening / mip bias | 0 / 0 | unchanged |

URP's history weight and clamp trade stability against history retention;
[Unity's camera reference](https://docs.unity.cn/6000.5/Documentation/Manual/urp/camera-component-reference.html)
describes these controls. The pinned URP 17.3.0 implementation was inspected.
Existing explicit history resets on teleports, calibration and origin shifts
remain in place; normal repeated preference application preserves history.

Two poses were settled, then each setting received 120 warm-up frames followed
by a 64-frame stationary measurement. Pixel values are the mean RGB byte value;
the metric averages each pixel's temporal standard deviation across the ROI.

| Region | Mean temporal standard deviation, before → after | Reduction |
|---|---:|---:|
| Roofs | .5262 → .1162 | 77.9% |
| Street/crosswalk region | .7700 → .2173 | 71.8% |

Native D3D11, RTX 4070 Laptop, 1920×1200, synthetic eye
(-.188, -.058, -.797)m. Roof pose: (700,400,3200), pitch18/yaw138;
ROI bottom-left (580,220), size640×480. Street pose: (1225,1.85,3585),
pitch0/yaw-151.5; ROI (450,200), size750×450. These are region metrics, not a
claim of that reduction on every individual line or complete removal of aliasing.
Camera positions matched exactly across each sequence. No city load failed.

Movement was checked separately at both poses with normal motion input:
121 frames of lateral travel (9.6m total) and a 3-degree turn, followed by a
stationary hold. Captures were saved at travel frames 30/60/120 and stop frames
1/8/64. Representative movement and stopping images were inspected.
No persistent doubled roof or road edge was apparent in the
inspected captures. The stronger history can retain a small transient after
movement; this is not a claim of zero ghosting. At stop frame 8 versus 64,
the candidate's mean absolute RGB byte difference was .675 in the street view
and .476 in the roof view, excluding the surrounding frame.

## Tests and setup

All **180 PlayMode cases and 263 EditMode cases passed** after integrating
the current main branch's controller support and Unity entity-ID API update.
PlayMode ran in a graphics-enabled interactive Editor,
including tracking, physical viewport, comparison, streaming, wall/roof depth
and sunlight checks. No cases were skipped. The first whole-suite run exposed
an existing tiny-budget starvation condition: timer overhead could consume a
positive allowance before the first resumable ownership step. Positive budgets
now advance one step; zero still performs no work. Existing coverage and atomic
publication tests passed after the fix.

The checked-in render pipeline settings now match the existing sunlight
profile applied by the build and runtime. This avoids an outdated Inspector
configuration; it does not introduce a second lighting preset. Controller
mapping and disabled-input behavior passed automated tests; a physical BLE
controller was not exercised during this release check.

Portable tooling validation passed 153 Python cases (two existing skips) and
eight PowerShell parser assertions. The GitHub workflow runs these portable
checks separately; it does not replace the local Unity and Windows checks.

The tracker setup was exercised in a new checkout with Python 3.13.5. Pinned
dependencies installed, `pip check` passed, the face model digest was verified,
17 tracker tests passed and the CPU model smoke check completed. This did not
open a physical webcam.

## Distribution and scope

The release ZIP contains the Unity player, streamed city and attribution notices.
It excludes source archives, caches, local diagnostics, tracker environments,
face models, logs and personal calibration. `INTEGRITY.json` records file hashes
and the embedded build identity; the accompanying `.sha256` covers the ZIP.
Use the Windows ZIP rather than GitHub's automatic source-code archive to run.

Windows x64 / DirectX 11 is the tested target. Webcam head tracking is optional
and requires setup and measurement on the target PC. Physical webcam motion,
every GPU and globally flicker-free rendering are not certified by these tests.
Finite streaming/shadow distances remain visible at long range. Experimental
replacement-model import stays explicit and does not run at startup.

Earlier focused reports remain available: [viewport](physical-viewport-validation.md),
[walls](aerial-overlap-validation.md), [roofs](roof-flicker-validation.md),
[sunlight](sunlight-validation.md), [trees](vegetation-visibility-validation.md).
