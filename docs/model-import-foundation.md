# Opt-in source-ID import foundation

Status: **source inspection and immutable staging are runnable; Unity import/bake,
material conversion, and live replacement are not implemented by this patch.**
Every generated plan deliberately says `runtimeReady: false`.

This additive proposal targets viewer PR #2 at
`6a2e0c50710ca86a3ee24757457ec5f3e116b97b`. The tracking package remains pinned to
`960d5de55037a3fe2739ed055c8de091b8c54aed`. Existing CityBake, CityCatalog,
CityStreamer, Skyline/Core ownership, Surface comparison, navigation, calibration,
and off-axis projection are unchanged. Nothing discovers or enables this contract
at startup. Existing legacy additive Near assets follow their existing path.

## Runnable commands

Python 3.10+ and the standard library are sufficient. A reviewed manifest and its
**separately pinned exact-byte SHA-256** are required; the tool never trusts a hash
declared inside the manifest itself.

```sh
python tools/model_import.py inspect \
  --manifest /delivery/import-manifest.json \
  --expected-sha256 <approved-64-character-sha256> \
  --source-root /delivery \
  --output /work/import-plan.json

python tools/model_import.py stage \
  --manifest /delivery/import-manifest.json \
  --expected-sha256 <approved-64-character-sha256> \
  --source-root /delivery \
  --output /work/staged-models

python -m unittest discover -s tools/tests -p test_model_import.py -v
```

`inspect` reads only allowlisted sources. `stage` copies only those files into
`<destination>/<datasetId>/<version>/<manifest-sha256>/payload/`, preserving their
full relative paths. It rehashes the copied payload, writes manifest and plan,
and publishes the completed directory by one rename. A failed copy leaves no
published package. Repeating the exact stage verifies and reuses it; altered
payload or plan bytes are rejected. Version and manifest digest jointly identify
the immutable selection. The manifest may select a subset of roots in a GLB;
staging retains the original GLB bytes, **it does not extract or instantiate it**.

Do not point `MANHATTAN_SOURCE_ROOT` or the legacy recursive CityBake at the new
staging directory. That would bypass this contract. A staged plan is not a
CityCatalog or an Addressables catalog. A successful CLI exit means source
inspection/staging succeeded, not that Unity can render the package. Check the
explicit `runtimeReady`, `runtimeBlockers`, and `materialBlockers` fields.

## Manifest v1

Required top-level fields are `schemaVersion` (1), `datasetId`, `version`,
`legacySourceVersion`, `coordinateContract`, `files`, and `selections`. Unknown
contract fields are rejected, including any invented capability-override flag.
Identifiers are explicit strings; filenames never decide Near/Core/Ground roles.

The supported coordinate contract is:

```json
{
  "crs": "EPSG:32618",
  "units": "metres",
  "gltfAxes": "x=east,y=up,z=-north",
  "originEN": [586912.663584656, 4514845.724134378],
  "groundPlacement": "flat-base-zero",
  "importHandednessConversions": 1
}
```

Each file has exactly `path`, `sha256`, `bytes`, and `kind` (`glb` or
`dependency`). Paths are case-unambiguous, portable package-relative paths.
Every external image/buffer URI must resolve to a listed dependency. Relative
`../shared_textures/...` references inside the package remain intact; remote,
absolute, encoded, Windows-escape, symlink, and package-escape paths are rejected.
An allowlisted but entirely unselected GLB or unreferenced dependency is rejected.
Unlisted authoring candidates/checkpoints are not discovered or imported.

Each selection has exactly:

```json
{
  "sourceId": "123_0",
  "file": "exports/selected.glb",
  "rootNode": 7,
  "rootName": "SOURCE_123_0",
  "semantics": "replace-complete-body",
  "contentProfile": "accepted-near-v1",
  "replacesSourceId": "123_0",
  "suppressLegacyTrim": true
}
```

Source IDs must be unique. Root index/name and actual
`extras.source_building_id` must agree. Roots must belong to the active scene;
overlapping ownership and foreign source-ID ancestors/descendants are rejected.
Complete-body replacement explicitly targets the same old source ID and suppresses
its additive trim. For `legacy-additive`, `replacesSourceId` must be null and
`suppressLegacyTrim` false. There is no implicit priority or “last file wins.”

The plan retains the selected node indices, unmodified ancestor transforms,
source metadata (including rings and ground/height annotations), file hashes, and
immutable selection identity. It does not flatten, recenter, subtract ground
elevation, add tile origins, or convert axes. A future Unity importer must preserve
this chain and apply glTFast handedness conversion exactly once. Repartitioning
into native tile-local coordinates requires a separate numerical regression.
This is a static-city contract, not a complete glTF validator: skinned/morph
selected nodes and nontriangle primitives are refused, while full accessor/buffer
and shader validation remains an importer obligation.

## Materials: preserve evidence, never silently approve lost optics

Material identity includes resolved metallic/roughness/base-color/alpha defaults,
texture bindings and content hashes, normal/occlusion/emissive maps, emissive
factor and strength, and every raw extension parameter. Source GLB and external
dependency hashes conservatively bind extension texture slots too, even when the
converter does not understand them. This favors correctness over maximum material
deduplication. Staging copies original assets byte-for-byte; no material is rewritten.

The existing custom Lit bake drops emission and does not preserve transmission/IOR.
The tool reports `LEGACY_BAKE_DROPS_EMISSION`,
`UNSUPPORTED_MATERIAL_EXTENSION:<name>`, and `OPTICAL_PROFILE_REQUIRED`.
No “allow unsupported” switch, alpha substitution, or claimed optical fallback is
provided. `KHR_materials_emissive_strength` is numerically resolved in the report,
not declared converted into a Unity shader.

Static inspection of the accepted 2026-10-11 selection found:

- 140 unchanged SoHo sources + 15 repaired R3 + 6 Howard H1 + 18 regional R4 =
  179 distinct IDs across 21 selected GLBs and 56 dependency files.
- All 179 selections have emission or optical diagnostics. They are blocked from
  activation, although their original source packages can be staged intact.
- 56 file-local selected transmission/IOR materials use OPAQUE alpha mode,
  transmission 1 and IOR approximately 1.48. Alpha alone cannot represent them.
- The repaired full15 includes an emissive-strength lamp material. The meshes
  have POSITION/NORMAL/TEXCOORD_0 but no exported tangent attribute.

The smallest future path is a bounded native-material adapter under the pinned
glTFast/URP versions: copy all resolved ordinary PBR/emission channels; define a
versioned glass profile that explicitly handles transmission and IOR; preserve it
through Surface OFF/ON; then approve that profile only after matched Unity views
and built-player shader checks. Any optical approximation must be documented and
reviewed. No URP upgrade or camera changes are required by this foundation, and
exact Blender optical equivalence is not promised.

The older conservative `new_version2_soho_distance` profile must not stand in for
regional18 or repaired15. Source-ID overlap is insufficient LOD provenance;
accepted Near and fallback/mapping identities must bind the intended immutable
content profiles. Howard street is a separate ground/crop-ownership task and is
not included in the 179 building selections.

## Decision-only runtime contract

`SourceReplacementGate` is a System-only class, not a MonoBehaviour. No existing
component calls it. Its EditMode tests exercise a proposed transactional protocol:

1. Construct one gate for one stable source ID, with immutable legacy fallback,
   exact body mapping, and exact additive-trim mapping identities. A legacy
   mapping must identify only that source; hiding a whole old 500m tile is invalid.
2. Begin an immutable candidate request containing geometry payload identities
   and exactly one optical-material-profile identity. The gate enforces bounded
   payload count and a monotonic readiness deadline.
3. A future loader keeps all candidate renderers hidden. After actual byte-hash,
   geometry ownership, dependency, and material validation, report each exact
   observed identity and measured hash. The gate does not perform those checks.
4. Evaluate a single visibility decision. `Replacement` requires every candidate
   payload plus fallback/body/trim bindings ready. `KeepFallback` requires verified
   fallback bindings. `HoldPublished` means no safe change is available, including
   missing fallback; it must retain any published replacement. `LegacyAdditive`
   leaves the ordinary legacy visibility policy untouched.
5. Revalidate/apply/acknowledge the same decision atomically on the main thread,
   with no intervening callback. Replacing hides exactly the old body and trim;
   rollback hides the replacement and restores both. The gate is not a renderer
   and cannot make asynchronous renderer writes atomic for its caller.
6. Do not release offered/active replacement resources until
   `CanReleaseReplacement` permits it after acknowledged fallback restoration.
   Stale generation completions and stale decision acknowledgments cannot commit.
   A new version passes through fallback before publication. Reentry revalidates
   legacy evidence, and failure/cancel/timeout cannot skip rollback.

An optical-profile identity is a hash-pinned evidence artifact, not a Boolean
permission to ignore unsupported features. This patch supplies no production
profile artifact. A lying caller can misuse a decision-only gate; actual loader,
geometry/material validators and atomic publication remain mandatory.

## Required follow-on wiring and Unity acceptance

Before enabling even one real source ID:

1. Implement an editor adapter consuming the exact pinned plan. Extract selected
   nodes only, preserve ancestry, derive tile-local native meshes, preserve material
   channels, and verify loaded positions/bounds/UVs/normals/tangents numerically.
2. Regenerate authoritative old Core and additive Near batches with exact source-ID
   ownership, or build a verified mapping. Preserve PR #2's legacy Skyline/Core
   guards and immutable sidecar/content identities. No broad tile suppression.
3. Produce native content, Addressables assets and a source representation catalog
   with a verified material profile and exact fallback lineage. Until then the
   plan's three runtime blockers remain unresolved.
4. Add an opt-in streamer adapter with hidden loading, whole-source readiness,
   coherent publication, rollback-before-release, and origin rebasing. Do not run
   independent Near fading over full bodies or register glass as ordinary
   CityDetailLit. Distance policy must consider actual building/surface bounds,
   including rooftop and upper-floor approaches.
5. Run all Unity EditMode/PlayMode tests, then exercise partial/cancelled/failed
   loads, version changes, fallback loss, unload/revisit, rebasing/bookmarks,
   unrelated neighbors, glass/interiors/emission, Surface reversal, and player
   shader variants. Verify real headtracking remains unchanged.
6. Measure CPU/GPU frame times, streaming work P95/P99/max, resident memory including
   textures, draw calls, load hitches and transition flicker on the six-ID pilot
   before enabling 179. No FPS, Unity compile, or pixel acceptance is claimed here.

## Cloud validation scope

The patch includes focused standard-library tests using a tiny generated GLB and
external PNG. Actual accepted sources were also inspected, and two representative
IDs were staged and reverified outside Git. Generated city assets and staged
packages are not committed. The cloud has no Unity, dotnet, Mono or PowerShell;
NUnit cases are authored for the later Unity run, not executed here.

The untouched PR2 portable suite has a known cloud-snapshot failure: its existing
383,800-byte city fixture is an unhydrated 131-byte Git LFS pointer. Shell git-lfs
fetch could not authenticate with existing credentials; no new access was created.
PowerShell-dependent tests also skip in this environment. Compare baseline and
patched logs rather than describing the entire suite as passing.
