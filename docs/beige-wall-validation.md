# Textured wall hidden by a coincident Skyline hull

This is the historical v5 fix for one static street-level wall. It did not fix
the later aerial report where the Skyline and Core subdivide the same wall
differently. That case requires the v6 coplanar coverage rule described in
[Skyline ownership](skyline-ownership.md); see [aerial validation](aerial-overlap-validation.md).

The reported left-rear tall beige building had a narrow textured strip and a
large flat face. The texture was present in the original Core asset. The flat
Skyline face was slightly nearer and covered it.

## Reproduction and cause

The matched private Windows Player reproduction uses the existing catalog's
startup observer pose and a fixed synthetic screenshot observation through the
production estimator/filter/projection. Both native backbuffer captures are
1920×1200 on Unity 6000.3.22f1, URP 17.3.0, glTFast 6.20.0 and RTX 4070 Laptop.
Both settle at 376 loaded tiles, zero pending loads and zero load failures.
Recorded observer, rendered eye, origin and camera transforms match exactly.
UI is hidden only while capturing evidence. The copied screenshot calibration
is for geometric reproduction and is excluded from physical measurement
acceptance; original personal files remain unchanged.

The overlapping surfaces are `Skyline_1_6`, child `0`, original triangle ordinal
7081, and `Core_Manhattan_Core_2_6`, child `17`, triangle 100. The Core uses
`Facade / dark_modern` with a 3072×3072 BaseMap plus normal/metallic maps. Two
interior test rays find the flat Skyline face roughly 0.34–0.40mm in front of the
textured face. Corresponding raw vertices differ by at most 0.576mm.

The previous compiler independently rounded city vertices to 1mm integers. A
pair straddled a rounding boundary, so the correspondence was absent despite
being within the intended tolerance. The v5 compiler uses nearby centroid cells
only for candidate discovery, then verifies every cyclic corresponding vertex
with Euclidean distance ≤1mm and positive winding. It also rejects >1mm diagonal
matches that the previous rounded key could accept. Imported vertices, UVs,
textures and positions are not edited.

## Executed native result

Before runtime: `338c090bb278ecd0f353f3e9cd8ae3ebf9a805a7`, recorded v4 mapping.
After runtime: `9deb78fbd1d8e01a52c406c6656f1bb197ae0c33`, v5 mapping.
The latter commit also fixes the offline generator's asynchronous lifecycle.

The entire 260-group correspondence was rebuilt from the unchanged exact
10,038-tile catalog and existing bundles. No source/texture/bundle rebake occurred.
The resulting sidecar is 4,394,261 bytes, SHA-256
`bf64909d83f2919896c15e25f4a6dbf8bf870179561972835e6a0e6cf82ae661`.
Source catalog SHA-256 remains
`1a53e1256a19e0f4ff38921687ed9065f07741e19eb963805381d6de3b69518d`;
built catalog SHA-256 remains
`56b7e281f8171c55b5f6049b7b960fd31b6a90a86370c4c08f48053b26b07828`.

The new mapping includes ordinal 7081 for the verified Core owner. Both test rays
now find the textured Core facade first, with no coincident Skyline candidate
in front. Core original/published index hashes and material/texture properties
are unchanged; its index SHA-256 is
`c011e03586a79041f0b26a7810f146410f1c095746fa7200fd54fe748ede713a`.
All 32,400 pixels in the selected wall-interior ROI change, and the new native
image shows the facade grid over the full formerly flat area. Both complete
native images were visually inspected. Image SHA-256 values are:

- Before: `161213b95f612b68e4b40853e99f2692eb87b5c6d8d5b855a8100fbe2e22a0fc`
- After: `eb6d702fff405738b6e41bd2d72713fb07c71fab5e9b0bc17cd3ca0d3d7ab856`

## Failure records and limits

The first full recompilation stopped around 100 groups when synchronous
`WaitForCompletion` re-entered an Addressables dependency list during bundle
unload. Its old output was preserved. Awaited loading, per-group cleanup/yield,
owned batch completion and scoped registration restoration fix that path. A
controlled unknown-group failure followed by an exact ten-prefab retry verifies
borrowed state and file preservation. See [generation](skyline-ownership.md) and
[executed regression checks](validation.md).

An earlier diagnostic used a different pitch and hit a foreground brownstone;
its raw evidence is retained and excluded. A same-name CLI bootstrap selection
ran one unrelated test and is likewise excluded; the 235-case final EditMode
record uses the absolute intended project path and isolated calibration.

CPU rays are supporting surface candidates, not a general proof of alpha/dither
pixel visibility. This case is also supported by opaque materials and the native
backbuffer image. It establishes the reported static wall fix, not all temporal
roof/side flicker, metric webcam depth, regional performance or release readiness.
Unrelated fallback is retained by the existing coverage/lifecycle regressions.
Builds, images and personal diagnostic data remain private and untracked.
