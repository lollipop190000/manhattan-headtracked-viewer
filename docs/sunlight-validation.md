# Afternoon sunlight and shadow validation — 2026-10-13

Branch: `codex/city-sunlight-20261013`, based on `codex/roof-flicker-20261013`.
Runtime implementation: `a7cf9369af69f43a8719dce4dd0015f50ad5fc4a`, with the
player shader variant retained by `f93acab3fd743f369c3a1ec572d682118685cf3e`.
The pinned tracking package, imported city, v7 ownership sidecar and 1mm roof
depth treatment are unchanged.

## What was observed

The native city has one directional sun, outside the observer hierarchy. No
enabled renderer outside the city casts shadows. A fixed sun retained exactly
the same world direction after a 12-degree camera turn and a 6m translation.
Thus these checks did not find a camera-shaped shadow caster.

The old realtime shadow range ended only 150m from the camera. Its moving
coverage boundary can look like lighting follows the observer. URP still uses
camera-relative cascades; this change extends and softens the distant fade,
not an infinite or camera-independent shadow map. See Unity's
[shadow distance documentation](https://docs.unity.com/en-us/engine/6000.5/manual/lighting-overview/shadows/shadow-realtime/shadow-distance)
and [URP resolution tradeoff](https://docs.unity.com/en-us/engine/6000.3/manual/lighting-overview/lighting/shadows-in-urp/shadow-resolution-urp).

There was also a concrete window-dressing mismatch: the package sets
`Renderer.receiveShadows = false`, but URP Lit controls receipt through the
material `_ReceiveShadows` property and `_RECEIVE_SHADOWS_OFF` keyword. The
demo now sets both on its private frame material. Previously the frame darkened
when sun shadows were enabled. Its shadow casting was already disabled.
The authored frame template declares the same keyword so player builds retain
the local shader variant. Runtime-only keyword changes passed Editor checks but
failed the first native build because the unused variant was stripped; native
shadow-on/off frame checks are required in addition to the property assertions.
The reusable package and imported city materials are not modified.

## Restrained visual changes

| Setting | Before | After |
|---|---|---|
| Sun direction at startup | elevation 28°, yaw 225° | unchanged |
| Sun color / intensity | (1, .81, .62) / 2 | (1, .882, .756) / 1.75 |
| Ambient sky | (.47, .58, .73) | (.58, .68, .80) |
| Ambient equator | (.36, .39, .43) | (.48, .51, .56) |
| Ambient ground | (.18, .16, .15) | (.28, .27, .27) |
| Shadow range / atlas | 150m / 2048 | 800m / 4096 |
| Four-cascade split fractions | .08 / .22 / .50 | .035 / .13 / .40 |
| Final cascade fade border | .20 | .35 |
| Sun depth / normal bias | .025 / .20 | .15 / .30 |

ACES, exposure, contrast, saturation, TAA, SSAO, sky material, fog, city
transforms and imported materials retain their settings. The softer color
balance comes from sunlight and ambient fill, without bloom or camera lights.
The higher shadow atlas increases shadow memory/work; captures are scoped
smoke checks, not a new global performance acceptance.

## Optional sun movement

Settings → LIGHTING offers Move sun slowly / Pause sun movement, an
Earlier–Later slider, and Reset afternoon light. Scrubbing pauses movement.
The illustrative afternoon arc runs from elevation 52° / yaw 180° to elevation
12° / yaw 255° over five minutes for its full range. Default position is 60%,
so starting there takes about two minutes to reach the end. It stops at the end
without wrapping or jumping. Color and intensity change gently along the arc.

This is a daylight preview, not date/location astronomy or a night cycle.
Startup is always the original afternoon direction, paused. No file stores the
sun position or moving flag. Calibration, comparison preferences and bookmarks
remain separate. Comparison switches neither start nor pause the sun; pause it
explicitly for repeatable comparison captures.

## Tests

The interactive GPU run `playmode-gpu-sunlight-retained-final.xml` passed 27/27 cases
with no skips, including five new sunlight tests, existing comparison round
trips, and the wall/rounded-roof depth regressions.

The rendered shadow regression samples the same ground receiver in two camera
poses, then changes only the sun and checks both the released and newly
shadowed receiver. Other checks cover all eight comparison combinations while
paused and moving; start/pause/scrub/end/reset; environment cleanup; actual
Settings callbacks; no persistence; and paused restart. The frame template
retains the required shader variant and runtime interactions do not mutate it.

Portable checks: 153 Python tests passed, two existing skips; five GPU parser
and three benchmark-environment assertions passed.

The unchanged tracking/measurement lifecycle classes were also run separately
in a graphics-enabled batch Editor: 19/19 passed, with no skips or inconclusive
cases (`playmode-gpu-sunlight-lifecycles.xml`, 67.963s). These two test runs cover
46 PlayMode cases; this is not a claim that the entire repository suite was rerun.

## Verified Windows player

The handoff player was built from `f93acab3fd743f369c3a1ec572d682118685cf3e`:
zero build errors/warnings, 120,047,338 bytes, 23.684s. Its 34 source fingerprints
cover all production runtime source files and the authored frame material.
It includes opt-in private capture helpers; normal launching activates none.

Native D3D11, RTX 4070 Laptop, 1920×1200, synthetic eye
(-.188, -.058, -.797)m, fixed city poses:

| Capture | City XYZ / pitch / yaw | Settled tiles |
|---|---|---|
| Street | (1225, 1.85, 3585) / 0° / -151.5° | 383 |
| Previous roof area | (700, 400, 3200) / 18° / 138° | 334 |
| Midtown aerial | (1225, 250, 3585) / 22° / 0° | 347 |

All samples had zero pending loads/failures and zero non-city shadow casters.
The 17 images include matched before/after pairs and street ablations: sun
shadows disabled, frame hidden, SSAO disabled, camera turn/translation, earlier
and later sun positions, moving/paused/still-paused, and the Settings drawer.
No Editor ran concurrently with these final native captures.

In the street frame, switching sun shadows on/off changed the city interior by
8.690 RGB byte levels on average while the top and left frame sample rectangles
had **zero** difference. Hiding the frame changed the interior by only 0.024
levels (TAA/edge variation), preserving city shadows. The fixed sun direction
was identical before and after camera motion, and remained identical between
two paused captures. These image checks catch the player-only shader stripping
failure that property-only Editor tests missed.

VSync-capped 90-frame samples before and after each pose had approximately
16.667ms median / 16.683ms P95. These short stationary samples do not measure
uncapped GPU headroom or establish streaming-route performance. Local evidence
is kept outside Git under `verified-street`, `verified-roofs`, `verified-midtown`
and `native-verification.json`.

The original built city catalog hash remains
`56b7e281f8171c55b5f6049b7b960fd31b6a90a86370c4c08f48053b26b07828`.
All seven existing personal JSON files matched their pre-task hashes.

## Limits

No physical webcam test was performed. Realtime shadows are limited to the
configured distance and currently resident shadow-casting city geometry;
existing distant Skyline hulls keep their authored casting policy. Distant
cascade resolution and silhouette aliasing are not eliminated. This work does
not establish globally flicker-free rendering, full-city indirect illumination,
or release acceptance on every GPU.
