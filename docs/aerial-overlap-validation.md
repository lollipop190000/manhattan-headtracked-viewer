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

Rebuild `CityOwnership.json` and its digest through the normal build, or use
`tools/bake_ownership.ps1` with the existing built catalog. V5/V6 maps cannot be used
with the V7 runtime. Meshes, UVs, materials, source packs, original bundles,
lighting, visibility distances and comparison controls are unchanged.

## Validation

- 259 EditMode and 159 PlayMode checks passed with graphics enabled; no failures
  or skips. These include split-wall conjunctions, departure/reload round trips
  with Surface ON/OFF, and a provider mesh or relationship changing immediately
  before publication.
- Tests include the measured aerial wall, different diagonals/subdivisions,
  duplicate overlap, interior holes, reversed winding, Euclidean corner distance,
  plane separation, catalog ownership, no-op disjoint candidates and preservation
  of adjacent fallback.
- Portable checks passed: 153 Python tests, two skips, plus five GPU-results
  parser checks and three benchmark-environment checks.
- Native full-city V7 comparisons are in progress.

The fixed-pose test uses synthetic observations to reproduce geometry. It does
not measure webcam distance accuracy or certify every moving-head rendering issue.
