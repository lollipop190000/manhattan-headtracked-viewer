# Partial-cohort aerial wall overlap

The three screenshots reported on 2026-10-12 show flat Skyline faces covering
parts of textured Core buildings, with jagged depth-conflict boundaries.
This also occurs in the current team Player; it is not explained by launching
the older GitHub draft release.

## Reproduced cause

A native Windows Player built from the current team rendering sources reproduced
the overlap at city position `(1225, 250, 3585)`, pitch 5 degrees and yaw 208.5
degrees, on RTX 4070 Laptop at 1920 x 1200. Streaming settled at 340 tiles,
zero pending loads and zero failures. The v5 map masked 39,677 hull triangles.

Two sampled rays hit `Skyline_1_6` immediately in front of the textured wall in
`Core_Manhattan_Core_1_6`, child `16`, at about 690m. The wall planes differ by a
fraction of a millimetre, but their edges have different subdivisions: one
Skyline wall triangle lies across two larger Core wall triangles. V5's cyclic
vertex match cannot establish that coverage. The complete-cohort shortcut also
cannot apply while the linked `Core_Manhattan_Core_0_5` is outside visibility.
The other Core providers are bound, active and opaque; this is not a missing
texture or a failed asset load.

## Correction

V7 proves complete coverage by the union of coplanar, correctly wound opaque
triangles from catalog-linked Core providers. The distance proof applies to
the bounded overlap, including every corner of projected intersections, so
remote provider vertices do not reject a short rounded hull edge. A projected
intersection with a real depth gap cannot claim coverage. Exact vertex matching remains
a fast path. Conservative polygon subtraction handles different diagonals and
edge subdivisions while retaining real holes, missing providers and unsupported
surfaces. Euclidean 1mm tolerance covers import rounding without snapping source
geometry. Empty projected intersections remain no-ops, avoiding artificial
polygon fragmentation from unrelated faces.

A remaining wall in `Skyline_0_6` needs both `Core_Manhattan_Core_0_6` and
`Core_Manhattan_Core_1_6`: the source wall's two triangles are stored in different
Core tiles. Hull ordinal 398 crosses their diagonal. V7 records both as required
providers instead of assigning full ownership to either half. Runtime checks
their bindings, coverage and relationships again before atomic publication;
fallback returns before either half departs. No geometry search occurs at runtime.

An additional distant sample at `(498.344, 124.393, 2512.451)` exposed a loading
boundary: `Skyline_0_4` ordinal 284 needs Core `0_5` and `1_5`, while only `1_5`
was visible under the radius policy. Ordinary visible Core tiles now request
their directly required joint partners. Secondary partners cannot seed further
requests or feed back into radius hysteresis, and the residency byte budget
still applies. This extends Core residency only for proved shared walls.

A further outer boundary around `Core_Manhattan_Core_1_4` retains a partial
wall from `Skyline_0_4`. Extending residency again would merely move that boundary.
The retained legacy opaque hull therefore receives a private material variant
with `Offset 0, 8`, without slope bias. This gives detailed geometry priority for
near-coplanar depth ties while preserving uncovered pixels. The source triangles
remain present, so CPU ray coincidences at this boundary are expected; they are
not evidence of a failed depth-priority render. The shader retains the pinned
URP Lit lighting and passes, with zero offset for shadows, Meta and 2D.
Surface ON/OFF both retain this priority and unregister restores the original
material. The offset is measured in device-dependent depth units, not millimetres.
Only imported opaque, untextured legacy hulls qualify; the runtime variant cannot
establish imported provenance or act as a detailed Core coverage provider.

Rebuild `CityOwnership.json` and its digest through the normal build, or use
`tools/bake_ownership.ps1` with the existing built catalog. V5/V6 maps cannot be used
with the V7 runtime. Imported meshes, UVs, materials, source packs and original bundles are unchanged.
Runtime hull material variants change depth priority; lighting and comparison
controls retain their behavior. Base radius values are unchanged;
the explicit joint-partner requests can add Core tiles beyond those radii.

## Validation

- 263 EditMode and 171 PlayMode checks passed with graphics enabled; no failures
  or skips. These include split-wall conjunctions, departure/reload round trips
  with Surface ON/OFF, and a provider mesh or relationship changing immediately
  before publication, one-hop request/release behavior and residency limits.
  One initial PlayMode run hit an observation-pause guard in the unchanged
  measurement workflow; the complete repeat passed without changing that test
  or relaxing the guard.
- Tests include the measured aerial wall, different diagonals/subdivisions,
  duplicate overlap, interior holes, reversed winding, Euclidean corner distance,
  plane separation, catalog ownership, no-op disjoint candidates and preservation
  of adjacent fallback.
- Portable checks passed: 153 Python tests, two skips, plus five GPU-results
  parser checks and three benchmark-environment checks.
- The native captures retain the same city position, camera projection, physical
  eye, lighting, resolution and GPU as their v5 controls. The three original
  24-ray grids have no remaining cross-layer near-coincidences after ownership
  masking. Additional distant probes still find the deliberately retained partial
  hull; its depth priority is validated by GPU output, not by claiming every
  source triangle has disappeared.
- The GPU fixture renders overlapping red fallback and green detailed half-walls
  at 10m, 1km and 4km. Detail wins the tie, uncovered fallback remains, a 2cm
  foreground hull retains occlusion, and Surface ON/OFF/ON round trips pass.
  Shared variant release/revisit and unsupported material exclusions also pass.
- Distant buildings without resident detailed geometry still use plain fallback
  faces. A sharp triangular transition can remain where only one source wall
  triangle is available; this is missing detail at the outer residency boundary,
  distinct from the jagged alternating depth conflict corrected here.
- Final Windows D3D11 Player: source `60162ea`, RTX 4070 Laptop, 1920 x 1200,
  347 settled tiles, zero pending loads/failures, and 198,554 masked hull
  triangles (v5 control: 340 tiles and 39,677 masks). All shared sampled source
  meshes/indices/material properties remain identical, apart from the intended
  runtime Skyline shader substitution.
- A 60-second, 100m/s native route completed five 500m-out/500m-back circuits.
  Streaming failures: 0; frames at least 50ms: 0; streaming P95:
  1.9523ms; frame P95: 16.6832ms; maximum frame:
  16.6862ms. Residency ranged from 331 to 338 tiles, with repeated
  mask publication/release. This run uses NoTracker and vsync, and has no usable
  graphics-memory counter. `performanceTargetPassed` remains **false**; these
  localized measurements are not full benchmark-suite or webcam acceptance.

## Local artifact preparation

All 10,043 files in the existing built Addressables directory (1,735,776,462
bytes) matched the 2026-10-12 preservation manifest by SHA-256, with no added,
missing or changed files. The built catalog digest remains
`56b7e281f8171c55b5f6049b7b960fd31b6a90a86370c4c08f48053b26b07828`.

One long, uninterrupted offline run failed to load `Skyline_3_8`; a fresh Editor
loaded and compiled that same bundle successfully. The initial load failure's
cause was not established. Recovery uses separate Editor runs and saved group
checkpoints. A private incremental helper reuses the already proven v6c singleton
correspondence only after each built binding matches exactly, then calculates
every joint claim from the loaded bundled geometry. Results for `Skyline_0_6`
and `Skyline_3_8` exactly match independent complete v7 compiler runs. The
production compiler is restored before building the Player; the incremental
helper is not part of the runtime or the public build path.

The completed map has 260 groups and 522 unchanged built bindings. It preserves
all 1,538,441 existing singleton assignments and adds 805 joint records covering
16,531 hull ordinals. Its SHA-256 is
`11c80be019cd105fb623cb454bc9f24bd542872e71147831e7e34dba9501e151`.
The normal-product Windows Player BuildReport records zero errors/warnings
from rendering source `60162eaf91ad1e9f74c95e3acb200e6dfb3e0ce1`, pinned tracking
revision `c5ddab14d12dcc8acc8b31048f5752686984f3ab`, and all 30 production runtime
source hashes matching the branch. Private opt-in inspection helpers are present;
the build identity therefore declares a dirty diagnostic build.

The fixed-pose test uses synthetic observations to reproduce geometry. It does
not measure webcam distance accuracy or certify every moving-head rendering issue.

The existing local `Run-LocalBranch.ps1` now selects this verified Player. Only
its PlayerPath changed; tracker, camera arguments, project identity and calibration
directory are preserved. The previous Player and launcher bytes are retained.
All seven existing personal calibration/preference JSON files still match their
pre-validation hashes. The public team guide selects the same feature branch;
no local build, private diagnostic evidence or personal data is committed.
