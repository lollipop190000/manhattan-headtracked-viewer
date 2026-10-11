# Legacy Skyline primitive ownership

Legacy Skyline bundles combine several buildings. Hiding a complete Skyline tile as soon as one Core arrives removes buildings that still need fallback coverage. Keeping every coincident hull produces overlapping surfaces. Ownership masks only the proven, coincident legacy hull triangles on private mesh variants; unrelated fallback geometry stays visible.

Near remains additional facade/window detail over the Core building. This change does not migrate the separate replacement-style source dataset or infer ownership from the presence of Near assets.

## Offline correspondence

`SkylineOwnershipBake` compiles one Skyline and its linked Core group at a time from the exact built Addressables prefab locations. The runtime does not build a city-wide vertex/triangle dictionary or compare Core and Skyline geometry on each load. `BuildAutomation.BuildWindows` runs this step after the Addressables content build, before the Player build.

The derived `Resources/CityOwnership.json` and `CityOwnershipHash.txt` are ignored by Git. Rebuild both when the city content changes. The sidecar binds source version, asset location/provider/type, bundle dependency content hashes, mesh layout and local transforms. Startup verifies the JSON digest; residency checks also require the loaded source-prefab mesh references. Missing, ambiguous or mismatched identity preserves the full fallback.

Generate explicitly from an existing built catalog in an owned project:

```powershell
$env:MANHATTAN_OWNERSHIP_CATALOG_PATH = '<built Addressables catalog.bin or catalog.json>'
unity run '<project>' --editor-version 6000.3.22f1 -- -executeMethod Manhattan.Editor.SkylineOwnershipBake.FromEnvironment
```

For a short validation of one group, use separate output outside `Assets`:

```powershell
$env:MANHATTAN_OWNERSHIP_VALIDATE_SKYLINE_ID = 'Skyline_0_8'
$env:MANHATTAN_OWNERSHIP_VALIDATION_OUTPUT = '<separate validation directory>'
unity run '<project>' --editor-version 6000.3.22f1 -- -executeMethod Manhattan.Editor.SkylineOwnershipBake.FromEnvironment
```

This requires the existing generated city catalog and built bundles. It does not bake or migrate the source assets. Remove the two validation environment variables before generating the complete sidecar.

## Coverage and lifetime

Only imported, untextured legacy `Skyline` URP Lit hulls are eligible. Intentional materials, textures, extra material overlays on the last submesh, alpha/displaced/custom surfaces, incompatible culling and shadow coverage retain fallback. Eligibility is captured before Surface OFF creates its comparison materials, so that switch cannot turn a textured imported surface into removable hull geometry.

Imported shared meshes are immutable while ownership is active. Replacing a source mesh, removing a required filter, or changing the bound transform revokes the prepared mask. External code that edits shared mesh buffers in place must first revoke ownership. The legacy correspondence assumes axis-aligned roots in metres; exact root rotation/scale checks reject changed frames, while a common floating-origin translation remains valid.

Prepare masks and private mesh variants incrementally. The final validation, mesh-pointer changes and Core activation share one no-yield publication. Shared material contracts are cached only inside that publication and cleared on every Tick exit. Late Skyline is gated until coverage is coherent. Cancellation, failure and unload restore fallback before releasing Core; failed preparation also publishes the visibility change in the same streamer tick.

After the final owner leaves a group, its private native mesh variants and decoded staging/index arrays are released. The compact correspondence itself remains available for later residency. Mesh copy/read/upload calls and final atomic publication are indivisible, so the configured streaming budget is a scheduling target, not a hard maximum for every frame.

The streamer schedules ownership against 70% of its total work budget, less work
already spent that frame. The remaining headroom accounts for an indivisible
preparation step and the visibility update after publication; ownership previously
consumed the entire remainder before that update. A cohort can wait more frames,
with the previous representation retained. Load/radius policy, geometry, materials,
quality and the indivisible publication are unchanged. This does not impose a hard
deadline on mesh uploads or a large atomic publication.

## Validation limits

Tests compile their small fixture correspondence explicitly before runtime residency. They cover real Addressables provider lifetime, partial replacement, late loads, cancellation, failure, origin rebasing, imported channels/materials, extra overlays, mismatched source identity, missing filters, small root changes and final-owner departure/revisit.

Hidden CPU route diagnostics separate application startup/warmup from the measured repeated route. Filesystem/driver caches are not forcibly flushed. All-frame P95 contains many idle frames; active-work subsets and worst work intervals must also be inspected. Whole-process managed heap and working-set counters are not ownership-only live allocations. Hidden runs with zero GPU/draw counters do not establish visible 60fps performance or moving-camera rendering quality.

The original reported aerial flicker and real webcam head motion still require their own reproduction. A fixed-pose geometry A/B and synthetic observations cannot certify those behaviors.
