# Legacy Skyline primitive ownership

Legacy Skyline bundles combine several buildings. Hiding a complete Skyline tile as soon as one Core arrives removes buildings that still need fallback coverage. Keeping every coincident hull produces overlapping surfaces. Ownership masks only the proven, coincident legacy hull triangles on private mesh variants; unrelated fallback geometry stays visible.

Near remains additional facade/window detail over the Core building. This change does not migrate the separate replacement-style source dataset or infer ownership from the presence of Near assets.

## Offline correspondence

`SkylineOwnershipBake` compiles one Skyline and its linked Core group at a time from the exact built Addressables prefab locations. Catalog and prefab loads are awaited on the Editor thread. Group cleanup releases instances and handles, then lets the Editor process pending bundle unloads before loading the next group. This avoids re-entering dependency operations through synchronous `WaitForCompletion`. The runtime does not build a city-wide vertex/triangle dictionary or compare Core and Skyline geometry on each load. `BuildAutomation.BuildWindows` awaits this same step after the Addressables content build, before the Player build.

The derived `Resources/CityOwnership.json` and `CityOwnershipHash.txt` are ignored by Git. Rebuild both when the city content changes. The sidecar binds source version, asset location/provider/type, bundle dependency content hashes, mesh layout and local transforms. Startup verifies the JSON digest; residency checks also require the loaded source-prefab mesh references. Missing, ambiguous or mismatched identity preserves the full fallback.

The current rule is `legacy-flat-hull-v6-coplanar-coverage`, still using schema 2.
The offline compiler retains the vertex-wise 1mm, cyclic-winding match as a fast
path. Remaining hull triangles are tested against the union of coplanar opaque
Core triangles belonging to the same catalog replacement. A spatial index only
finds candidates; positive winding, plane distance and complete polygon coverage
establish ownership. This handles different wall subdivisions and diagonals at
partial-cohort boundaries. Overlapping providers are not counted twice, and
uncovered interior holes remain fallback.

Double-precision polygon subtraction retains every uncovered convex fragment.
Each projected intersection must also pass a 3D distance check at every corner;
projection alone cannot hide an angled depth gap. This tests the bounded overlap,
not remote provider vertices beyond a short hull edge. Rounding slivers are
accepted only when every vertex of a fragment is within
1mm Euclidean 3D distance of one provider triangle. Convex distance bounds the
entire fragment, rather than inferring coverage from samples or total area.
Disjoint intersections leave the original polygon intact, avoiding arbitrary cuts
from unrelated faces. Excessive fragmentation fails conservatively. Tile relationships and material
eligibility still apply. No vertex is moved, no texture is rewritten and no
geometry matching is added to runtime loads.

**Rebuild the correspondence after updating from v4 or v5.** The runtime rejects
those old rules and retains fallback until an explicit v6 rebuild. Recompiling this
derived sidecar from existing exact built bundles does not require a source mesh,
texture or Addressables bundle rebake. Keep a copy of prior diagnostics outside
the project if they need to remain reproducible.

Generate explicitly from an existing built catalog in an owned project:

```powershell
.\tools\bake_ownership.ps1 -CatalogPath '<built Addressables catalog.bin or catalog.json>'
```

For a short validation of one group, use separate output outside `Assets`:

```powershell
.\tools\bake_ownership.ps1 -CatalogPath '<built Addressables catalog.bin or catalog.json>' `
    -SkylineId 'Skyline_0_8' -ValidationOutput '<separate validation directory>'
```

This requires the existing generated city catalog and built bundles. It does not bake or migrate the source assets. The wrapper restores its environment variables when it exits. It and `tools/build.ps1` locate the pinned Editor or accept `-EditorPath`, launch only their owned hidden batch process, and enforce a timeout. The async entrypoint exits the batch Editor after success or failure; do not add `-quit` or invoke it through `unity run/build`, which supplies `-quit` and can terminate before awaited work completes. Interactive calls await `FromBuildSettingsAsync` or `ValidateBuiltGroupAsync` and leave the Editor open.

Run this offline scope without unrelated concurrent Addressables requests. Normal
first-use Addressables initialization settles before the scope borrows global
state. Catalog loading also adds dynamic locators and catalog-declared providers;
cleanup removes registrations introduced by this scope and restores its path
transform while retaining pre-existing object references and order. The output
pair is staged after all groups finish. Ordinary publication/import exceptions
restore previous bytes and remove staging files; hard process termination between
the two file replacements can still leave a mismatched pair, which startup digest
verification rejects safely.

## Coverage and lifetime

Only imported, untextured legacy `Skyline` URP Lit hulls are eligible. Intentional materials, textures, extra material overlays on the last submesh, alpha/displaced/custom surfaces, incompatible culling and shadow coverage retain fallback. Eligibility is captured before Surface OFF creates its comparison materials, so that switch cannot turn a textured imported surface into removable hull geometry.

Imported shared meshes are immutable while ownership is active. Replacing a source mesh, removing a required filter, or changing the bound transform revokes the prepared mask. External code that edits shared mesh buffers in place must first revoke ownership. The legacy correspondence assumes axis-aligned roots in metres; exact root rotation/scale checks reject changed frames, while a common floating-origin translation remains valid.

Prepare masks and private mesh variants incrementally. The final validation, mesh-pointer changes and Core activation share one no-yield publication. Shared material contracts are cached only inside that publication and cleared on every Tick exit. Late Skyline is gated until coverage is coherent. Cancellation, failure and unload restore fallback before releasing Core; failed preparation also publishes the visibility change in the same streamer tick.

After the final owner leaves a group, its private native mesh variants and decoded staging/index arrays are released. The compact correspondence itself remains available for later residency. Mesh copy/read/upload calls and final atomic publication are indivisible, so the configured streaming budget is a scheduling target, not a hard maximum for every frame.

The streamer schedules ownership against 60% of its total work budget, less work
already spent that frame. The remaining headroom accounts for an indivisible
preparation step and the visibility update after publication; ownership previously
consumed the entire remainder before that update. A cohort can wait more frames,
with the previous representation retained. Load/radius policy, geometry, materials,
quality and the indivisible publication are unchanged. This does not impose a hard
deadline on mesh uploads or a large atomic publication.

Ordinary replacement preparation explicitly ends its streaming tick before final
revalidation and publication, even when some allowance remains. Publication starts on a later
tick, avoiding the combined preparation and commit costs in one frame. The
captured renderer/mesh/material state is rechecked after that boundary; a late
coverage change restores fallback. Mesh pointers and Core activation still
publish together without yielding, and release still waits for restored fallback.
Departing/revoked Core coverage and an initially gated late Skyline bypass the
extra boundary, preserving immediate fallback recovery and its existing tests.

## Validation limits

Tests compile their small fixture correspondence explicitly before runtime residency. They cover real Addressables provider lifetime, partial replacement, late loads, cancellation, failure, origin rebasing, imported channels/materials, extra overlays, mismatched source identity, missing filters, small root changes and final-owner departure/revisit.

Hidden CPU route diagnostics separate application startup/warmup from the measured repeated route. Filesystem/driver caches are not forcibly flushed. All-frame P95 contains many idle frames; active-work subsets and worst work intervals must also be inspected. Whole-process managed heap and working-set counters are not ownership-only live allocations. Hidden runs with zero GPU/draw counters do not establish visible 60fps performance or moving-camera rendering quality.

The original reported aerial flicker and real webcam head motion still require their own reproduction. A fixed-pose geometry A/B and synthetic observations cannot certify those behaviors.
