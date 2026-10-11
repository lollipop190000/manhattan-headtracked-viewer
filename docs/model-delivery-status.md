# Model delivery and integration status

Snapshot: **11 October 2026**. This is a delivery index and offline selection
check, not a Unity asset catalog or runtime loader. `runtimeReady=false`.

The [machine-readable catalog](model-delivery-catalog.json) records 12 sealed
ZIPs with verified Drive file/folder links, exact bytes, SHA-256, an in-archive
identity-manifest hash, source-ID selections, dependencies and rollback notes.
Drive metadata and downloaded remote bytes were checked against the sealed
archives before publication. The offline checker does not re-fetch Drive.

## Current accepted detailed baseline

**580 unique source parts = 179 + 162 + 118 + 121**, from a main-island source
inventory of **44,567 parts**. These are source-part counts, not necessarily
physical buildings, a visual-quality score, or citywide completion.

- Original 179 current selection: **140 unchanged + 15 clean R3 + 6 Howard H1 +
  18 Regional R4**. Select only those 140 IDs from the original 179 ZIP. Its other
  bodies are superseded. Do not import every file/root from all four packages.
- B: 162 disjoint IDs, delivered `FULL162_R2`.
- C: 118 disjoint IDs, delivered C118.
- D: 121 disjoint IDs, final D3 archive. Its 72,458,189-byte ZIP was uploaded and
  remotely hash-verified. The catalog pins that exact archive, not a draft freeze.

The catalog's seven baseline profiles define the exact union. Every source ID
has one selected baseline owner. Original archives remain immutable for recovery.

## Four distinct kinds of evidence

1. **Source coverage:** the selected baseline ID sets are disjoint and total 580.
2. **Geometry review:** each accepted package has its own exported-geometry,
   source, material-data and bounded Blender evidence. The exact scope is inside
   its sealed archive. This change does not rerun those geometry audits.
3. **Load-profile compatibility:** matching an ID is insufficient. Parent
   generation/hash, placement profile, neighboring bodies, texture dependencies
   and complete-body suppression must all be reconciled. Optional overlays
   remain opt-in and are not a newly validated combined 580 composition.
4. **Runtime readiness:** Unity import/material fidelity, EditMode/PlayMode,
   streamed replacement, floating origin, built-player shaders, headtracking and
   measured FPS remain unverified here. No Unity screenshot was captured; actual
   editor capture remains blocked by the offline laptop.

Package pictures are Blender evidence. D's night image is a dark native-emission
diagnostic, not finished night-street or material-beauty proof. Geometry counts,
portable packaging and passing Python tests do not establish engine readiness.

## Optional and historical packages

**Block233: blocked against current 580.** Its four source IDs already belong to
accepted coverage. The sealed replacement contract pins older SoHo candidates.
Eight target/context hashes differ from the latest selected parents:

- Targets 118578_0,424380_0,496807_0: current parent `regional_identity_r4`.
- Context 142866_0,434031_0,730170_0: current parent `regional_identity_r4`.
- Context 4939_0,547_0: current parent `howard_h1`.
- Target 166968_0 and the other four context pins still match unchanged SoHo.

Exact old/current hashes and parent profiles are in `artifacts.block233`.
Parent-asset checks throughout this catalog fingerprint per-source authoring
candidate GLBs. Grouped delivery GLBs are separate files; candidate hashes are
generation-associated evidence and are not promised as ZIP members. Archive
identity, exported-root compatibility and actual scene ownership are separate checks.
The checker rejects `block233_flat_city_preview` in a current-baseline selection.
Do not rewrite the sealed package or claim a matching source-ID set fixes this.
A newly sealed, independently reviewed reconciliation and verified delivery
would need a separate catalog revision.

**Block474: optional atomic four-part replacement.** Roof IDs 1074413_0,
1082144_0 and 841918_0 require companion 797579_0. The companion removes exactly
one unsupported 28-triangle authored pipe. All four pinned parent assets and
three context assets match current B162 R2 hashes. Omitting the companion is
rejected. This is static parent verification, not integrated580 or Unity proof.
Restore all four original B162 bodies together when rolling back.

**Facade598463_0: optional one-part replacement.** The candidate's baseline hash
matches the current C118 body; six checked B/C neighbor candidates and their
checked grouped exports also retain the review's hashes. Baseline and candidate
are mutually exclusive;
coverage growth is zero. These parent-hash checks do not establish new combined
580 geometry or runtime compatibility. Preserve the C118 original for rollback.

**Four historical roof prototypes:** 671438_0,347404_0,718355_0 and 697751_0 are
outside accepted 580. They add no accepted coverage. Each package profile is
explicitly chosen: `source_absolute` is isolated-only; `flat_city_preview` uses
one documented reversible rigid base translation per building. Never load both.
The catalog permits these prototypes only as an isolated selection.

**Conservative 179 Mid/Far r2:** older-profile research. It does not match all
latest clean R3/Regional R4 bodies or the complete580 set. Keep it isolated;
do not silently combine it with current Near bodies or treat it as runtime LOD.

Historical source-absolute variants for Block233/474 are likewise isolated-only.
No profile activates automatically. Catalog selection is a planning operation,
not a statement about the contents of an actual Unity scene.

## Coordinates, source assumptions and rights

Modern packages retain source footprints/height records and procedural facade,
interior and equipment assumptions, with metre units, EPSG:32618 city coordinates,
origin EN (586912.663584656,4514845.724134378), glTF x=east/y=up/z=-north, flat
base 0, and separate ground-elevation metadata. Preserve each archive's exact
height policy, local roots and ancestor transforms. Convert handedness once.

Historical stepped roofs derive from NYC OTI's 2014 aerial-survey 3-D Building
Model, released 2016. They are not current surveyed facades. Preserve each
package's exact coordinate conversion, attribution and source-use caveats.

The [existing attribution](../THIRD_PARTY.md) and archive notices govern source
provenance. Project code licensing does not relicense NYC geometry. Do not label
that source CC0, public domain, Apache-2.0 or wholly original. Generated source
geometry, textures, ZIPs and original source datasets stay outside Git.

## Reproduce checks and recover safely

From the repository root:

```sh
python tools/validate_model_delivery_catalog.py
python -m unittest discover -s tools/tests -p test_model_delivery_catalog.py -v
python tools/validate_model_delivery_catalog.py --archive east_d3=/path/to/Manhattan_East_Expansion_121_D.zip
python tools/validate_model_delivery_catalog.py --selection historical_four_source_absolute
```

The checker validates ID uniqueness/disjointness, dependency presence, explicit
supersession, exclusive/isolated profiles, stale-parent rejection, companion
membership, positive byte counts, hash formats and Drive link identities.
`--archive` streams a supplied local ZIP to verify its complete size and hash;
it never downloads, extracts, imports, changes materials or modifies a scene.
For an explicit current-baseline-plus-optional plan, pass all seven baseline
profile IDs plus the optional profile with `--selection`.

Download into a new staging directory outside Git; check bytes/hash before
extracting. Retain original archives and complete relative texture folders.
Never bulk-import overlapping body generations. A future adapter must inspect
actual destination ownership, suppress both prior body and additive trim only
for selected IDs, preserve neighbors, and acknowledge fallback visibility before
releasing a replacement. A rollback restores exactly the previous pinned bodies
and dependencies without changing the baseline archives.

## Repository boundary

This four-file draft is stacked on import-foundation PR #3 at
`a6a507ebdd59cb897664d3e5aced473867eb660c`. [The foundation contract](model-import-foundation.md)
remains authoritative about missing native integration. Main, existing PRs,
CityBake/CityStreamer, materials and the tracking package pin are unchanged.
Merging this documentation would not activate models; merge/deploy is not part
of this change. New package revisions should use new archive hashes and reviewed
catalog changes, preserving old entries or an explicit recovery trail.
