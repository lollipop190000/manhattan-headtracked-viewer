# Current ownership WIP, 2026-10-10

This correctness followup has parent `9f4de233451b7690eefef491ce0746a8ecb0b12a` on `goat/ownership-wip-20261010`, draft PR #2. Main and PR #1 remain unchanged.

## Mutable replacement relation correctness

Previously an entry remembered only its registration-time relation. A valid A-to-B-to-A relation sequence could leave group B masked after the provider departed, although coverage stayed true. Invalidation now unions the immediately previous and current dependencies, then clones the current relation for the next notification. Shared caller arrays cannot rewrite that history. Partial ordinal and complete-catalog replacement use the same invalidation rule.

Call `CityStreamer.InvalidateOwnership(tileRoot)` after changing a resident replacement relation, renderer state, material, or sharedMesh pointer, before the next StreamingTick. Pass null to invalidate all resident groups. Surface and origin notifications use the same public API. In-place sharedMesh vertex/index contents remain immutable and unsupported. This does not add arbitrary mutable-mesh support or change material eligibility, geometry tolerances, coverage checks or atomic publication guards.

## Verification

Four new relation cases fail on the preceding 9f4 mask logic: A-to-B-to-A departure for partial/complete contracts and repeated registration with/without visibility cancellation. Each failure identifies the stale B mask. The fixed full local PlayMode suite passes **124/124** and EditMode **178/178**, zero failures/skips. A fifth case tests the public material-change notification API. Exact frozen Windows and LF publication hashes plus XML hashes are in `tools/ownership_wip/relation-history-manifest.json`; CRLF-to-LF normalization explains differing byte hashes.

The same private offscreen URP request helper and fixed street pose render both the preceding logic and this fix, with current v2 assets, no tracker and separate synthetic settings. This checks a minimal actual render; it is not a normal-window presentation, motion/flicker or webcam acceptance. Private helpers, PNGs, builds and logs are excluded from Git.

## Historical performance and remaining work

The **9f4** 90s Midtown100m/s run measured streaming P95 **2.1380ms**, P99 **2.8103ms**, max **4.3521ms**. The original 2ms gate failed. These are historical results for 9f4, not measurements of this correctness followup; no new 90s benchmark was run. The preceding 119/178 test and native still/16s clip evidence remains scoped to 9f4. Global flicker and valid webcam observations remain unaccepted.

Windowframe versus facade-pattern diagnosis remains separate. A visible limestone Core triangle matches original GLB positions exactly, and UVs exactly after importer V conversion; this alone does not prove every Near rail is correct or settle authoring alignment. No new geometry, frames, asset migration, model/engine/package change or structural streaming optimization is included.

Use existing `tools/test.ps1` full Unity and `-DataOnly` commands in an owned checkout, preserving the current verified catalog/sidecar/bundles. Notify mutable resident changes through the public API. Original projects and personal calibration remain preserved.
