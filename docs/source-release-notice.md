# Manhattan source v2 — manhattan-2026-10-10-v2

Immutable source inputs for the Manhattan Head Tracked Viewer. Source version: `manhattan-2026-10-10-v2`. Release tag: `city-source-v2`.

Download these six archives through the authenticated setup tool:

- `Manhattan_Unity_Core_and_Ground.zip`
- `Manhattan_Source_and_Materials.zip`
- `Manhattan_Near_Downtown.zip`
- `Manhattan_Near_Midtown.zip`
- `Manhattan_Near_Uptown.zip`
- `Manhattan_Optional_Vegetation.zip`

The release revision's [`assets/source-manifest.json`](https://github.com/lollipop190000/manhattan-headtracked-viewer/blob/city-source-v2/assets/source-manifest.json) records every archive/file SHA-256 and byte count. Setup verifies the archives before extraction and every extracted input before conversion. The six packs contain 370 files, including 243 additive near-detail GLBs. Use the matching repository revision and manifest when reproducing a build; changing a release asset requires a new source version and release tag.

V2 replaces core/ground and procedural-source packs with corrected surface/courtyard triangulation. The other four archives are unchanged. Original footprint/height data, common metre-scale origin, material properties and embedded textures are retained. The source is flat-ground geometry with procedural exterior details; it is not a survey of facade appearance. See the [source audit](https://github.com/lollipop190000/manhattan-headtracked-viewer/blob/city-source-v2/docs/source-v2-audit.md).

NYC Office of Technology and Innovation, NYC Parks, NYC Department of City Planning and the original datasets retain their credits and [NYC Open Data Terms of Use](https://data.cityofnewyork.us/stories/s/Terms-of-Use/k9k7-3cje). `PROVENANCE.md`, `NYC_Open_Data_Terms.html`, original metadata and authoring records are preserved inside these packs. The viewer's MIT code license does **not** relicense this city data or its derivatives. Preserve the terms and credits when redistributing derived content; do not relabel these sources CC0 or public domain. Full source links and component/model notices are in [THIRD_PARTY.md](https://github.com/lollipop190000/manhattan-headtracked-viewer/blob/city-source-v2/THIRD_PARTY.md).

This source release contains no downloaded face task model, local tracker environment, authentication information or personal viewer calibration. It does not include Google Maps imagery, Google 3D tiles or photogrammetry. Do not upload original ZIPs plus their expanded city contents into Git; generated Unity tiles are rebuilt locally.
