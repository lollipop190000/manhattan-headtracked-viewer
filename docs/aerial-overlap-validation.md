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

Rebuild `CityOwnership.json` and its digest through the normal build, or use
`tools/bake_ownership.ps1` with the existing built catalog. V5/V6 maps cannot be used
with the V7 runtime. Meshes, UVs, materials, source packs, original bundles,
lighting and comparison controls are unchanged. Base radius values are unchanged;
the explicit joint-partner requests can add Core tiles beyond those radii.

## Validation

- 263 EditMode and 161 PlayMode checks passed with graphics enabled; no failures
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
- Native full-city V7 comparisons are in progress.

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
The normal-product Windows Player built successfully with zero errors/warnings
from rendering source `75c27d617ee050e3e6dfe33cae0236e244d10977`, pinned tracking
revision `c5ddab14d12dcc8acc8b31048f5752686984f3ab`, and all 28 production runtime
source hashes matching the branch. Private opt-in inspection helpers are present;
the build identity therefore declares a dirty diagnostic build.

The fixed-pose test uses synthetic observations to reproduce geometry. It does
not measure webcam distance accuracy or certify every moving-head rendering issue.
