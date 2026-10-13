# Asset and dependency attribution

The root MIT license applies to this project's code. It does not relicense original city data, optional downloaded models, Unity packages or their dependencies.

## Manhattan geometry and procedural art

The public source release preserves the delivered `PROVENANCE.md`, `NYC_Open_Data_Terms.html`, raw/metric geometry, metadata and original procedural-authoring records. Source acquisition date is **2026-10-09**; underlying surveys and dataset revisions have mixed dates. Buildings are footprint/height representations; windows, signs, roof details, trees and facade treatments are procedural approximations rather than survey-accurate facade imagery.

Credits retained from the source pack:

- NYC Office of Technology and Innovation: [building footprints](https://data.cityofnewyork.us/d/5zhs-2jue), [street centerlines](https://data.cityofnewyork.us/d/inkn-q76z), [waterbodies](https://data.cityofnewyork.us/d/pjs3-c3z5), [roadbeds](https://data.cityofnewyork.us/d/i36f-5ih7), [sidewalks](https://data.cityofnewyork.us/d/52n9-sdep), [elevation points](https://data.cityofnewyork.us/d/9uxf-ng6q).
- NYC Parks: [park properties](https://data.cityofnewyork.us/d/enfh-gkve).
- NYC Department of City Planning: [borough land boundaries](https://data.cityofnewyork.us/d/gthc-hcne), version 26b in the source pack.
- [NYC geometry metadata](https://github.com/CityOfNewYork/nyc-geo-metadata) and [planimetric capture rules](https://github.com/CityOfNewYork/nyc-planimetrics/blob/master/Capture_Rules.md).

Original NYC sources retain [NYC Open Data Terms of Use](https://data.cityofnewyork.us/stories/s/Terms-of-Use/k9k7-3cje), preserved in the release. The portal does not uniformly declare a named CC/ODC license. Do not relabel the source data CC0 or public domain. Keep these credits and source terms with redistributed city derivatives. No Google Maps imagery, 3D tiles, meshes, photogrammetry or facade photographs are used in the delivered city.

`assets/fixtures/NearestNearDetail.glb` is the unmodified glazing-mullion mesh subset of the nearest source tile `Near_-03_-08.glb` from `Manhattan_Near_Downtown.zip`. Its source SHA-256 and subset fingerprint are recorded in the fixture manifest. The same NYC source credits and retained provenance apply; the root MIT code license does not relicense this city derivative.

## Tracking and Unity components

- [Head Tracked Display](https://github.com/lollipop190000/head-tracked-display) is MIT licensed. A tested Git revision supplies the UPM package, launcher and Python tracker. Its own `LICENSE` and `THIRD_PARTY.md` remain with the checkout.
- MediaPipe Python and OpenCV are installed locally according to their package licenses/notices. The [Face Landmarker task model](https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task) is separately downloaded and SHA-256 verified; it is ignored by Git. Preserve applicable model terms when distributing an application.
- The unmodified [MediaPipe canonical face model](https://github.com/google-ai-edge/mediapipe/blob/master/mediapipe/modules/face_geometry/data/canonical_face_model.obj) in the technology repository retains its Apache 2.0 notice.
- Filtering follows the published [One Euro filter](https://gery.casiez.net/1euro/); the implementation belongs to the MIT tracking package.
- Unity URP, Input System, Addressables, glTFast and Unity Test Framework retain their package licenses and notices. The generated city uses original source PBR maps and UVs; importing does not transfer their ownership to this code license.
- `ManhattanViewer/Assets/Shaders/CityDetailLit.shader` and `CityDetailLitInput.hlsl` adapt the pinned URP 17.3.0 Lit renderer and retain the Unity Companion License. Original license bytes and upstream source hashes are in that folder's `LICENSE-Unity-URP.md` and `NOTICE.md`; Windows builds carry both under `Notices/CityDetailRenderer/`.

No example vase, furniture or studio HDRI from the separate technical demo is required by the city viewer. If team-authored content imports additional external assets, add source, author, license and file fingerprint here before merging it.

## Portable webcam application distribution

The v0.1.1 Windows ZIP includes a freshly assembled CPython embeddable runtime,
verified PyPI wheels, the pinned tracker, canonical geometry and the unmodified
Face Landmarker float16 version-1 task bundle. It does not copy a machine-local
development virtual environment. These generated files are excluded from Git.
See [portable tracker notices](docs/portable-tracker-notices.md); the ZIP retains
Python's original license/SBOM, dependency licenses and the three Apache-2.0
model cards. The optional Microsoft prerequisite action downloads directly from
Microsoft; its installer is not redistributed inside our ZIP.
