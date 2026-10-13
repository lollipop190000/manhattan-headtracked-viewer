# Earlier tree visibility — 2026-10-13

Branch: `codex/tree-visibility-20261013`, based on `codex/city-sunlight-20261013`.
Runtime change: `5322bda`.

## Cause and change

Trees previously became visible only within 250m of their tile bounds, although
the streets and buildings remained visible out to 800m. This was a visibility
cutoff, not evidence of slow tree decoding. Because distance includes height,
a 400m-high observer could have no visible vegetation even directly overhead.

| Vegetation policy | Before | After |
|---|---:|---:|
| Show within | 250m | 800m |
| Hide beyond (hysteresis) | 270m | 820m |
| Start preloading within | 350m | 1,000m |
| Release beyond | 450m | 1,200m |

Distances are measured to tile bounds in 3D, using the existing two-second
motion prediction for requests. The 200m preload band gives four seconds of
lead distance at the maximum 50m/s flight speed; this is not a loading-time
guarantee. Vegetation now shares the streetscape's range. The existing memory
and frame-work budgets, two pending loads, one instantiation per frame, request
priority and boundary hysteresis remain unchanged.

No new shader, transparency/fade, city bake or geometry is introduced. Trees
still have a finite tile-based cutoff; this moves the transition farther away,
without claiming continuous distant forest LOD. Lighting, shadow settings,
wall/roof fixes, comparison switches and the tracking package are unchanged.

## Automated checks

The graphics-enabled interactive PlayMode run passed **47/47**, without skips:
streaming lifecycle, composition, sunlight and wall/rounded-roof depth tests.
The new lifecycle regression exercises preload while hidden, visibility at
750m, boundary jitter and re-entry, an aerial observer, retention, release and
reload through the real Addressables handle path with a synthetic provider.

An initial batch attempt was unsuitable for the existing GPU regressions:
three were skipped and a Near material pass assertion failed. The interactive
run above used the same code and passed all cases; no assertion was weakened.

Portable checks passed **153 tests**, with two existing skips, plus five GPU
parser and three benchmark-environment assertions.

EditMode policy and evaluator tests passed **51/51**, including randomized
current/predicted positions and all radius boundaries. Total related Unity
coverage is 98 cases, not a claim that the entire project suite was rerun.

## Native validation

Two Windows D3D11 players used identical private capture helpers and the same
read-only built city. Baseline runtime was `b840188`; candidate was `5322bda`.
The candidate build had zero warnings/errors, 120,058,437 bytes, and 34 verified
production-source/material fingerprints. Normal startup activates none of the
private capture helpers. No local players, captures or generated city data are
committed.

RTX 4070 Laptop, 1920×1200, webcam disabled, fixed synthetic eye, same sunlight:

| Pose (city XYZ; pitch/yaw) | Active vegetation tiles before → after | Additional estimated tree residency |
|---|---:|---:|
| Park aerial (0, 180, 0; 24/30) | 19 → 138 | 23.24 MiB |
| Midtown aerial (1225, 250, 3585; 26/0) | 4 → 113 | 13.31 MiB |
| Midtown street (1225, 1.85, 3585; 0/-151.5) | 12 → 122 | 13.43 MiB |

Counts are tile instances, not individual trees or visible screen pixels.
Every tile required by the candidate's visible radius was instantiated and
active, with zero pending loads and zero failures at capture. The three native
candidate images were inspected; the park pair directly shows the previously
empty distant area gaining its canopy. Estimated total residency remained
below the unchanged 1,536 MiB budget. These catalog estimates are not measured
process RAM or VRAM.

After settling, 180-frame VSync-capped samples retained approximately 16.667ms
median / 16.683ms P95 in both players at all three poses. This is a stationary
smoke check, not uncapped GPU headroom or continuous travel acceptance. The
helper required at least ten seconds and sixty stable frames before sampling;
its wait duration is not a time-to-first-tree benchmark. More preloaded tiles
can increase initial settling work.

Local evidence is under the task's `evidence/baseline`, `evidence/candidate`,
test XMLs and `native-verification.json`. The source catalog retained SHA-256
`56b7e281f8171c55b5f6049b7b960fd31b6a90a86370c4c08f48053b26b07828`, and all seven
existing personal JSON files retained their pre-task hashes. The existing
local launcher now points to the verified candidate; its previous player and
a copy of the launcher remain available.
