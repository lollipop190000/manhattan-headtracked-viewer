# Source v2 replacement verification

The selected source version is `manhattan-2026-10-10-v2`, published under the private `city-source-v2` release tag. Before replacement, both revised ZIPs were read and every member compared by byte count and SHA-256 against the provided extracted directories. Both passed. The approved canonical paths were resolved and checked inside `3d_modeling` before removing the two old ZIPs/directories and moving the verified replacements into those exact paths. The source README was replaced with the v2 README and its copy hash verified.

| Revised archive | Bytes | SHA-256 | Files |
|---|---:|---|---:|
| Manhattan_Unity_Core_and_Ground.zip | 170,543,999 | `9ad691d02a6462ab8e7a97b826aa5c522e99fbe159a83593849271813e55c29f` | 23 |
| Manhattan_Source_and_Materials.zip | 81,434,566 | `9d703c29ec6dc1192249a26e1d3577b1ca06beffddb61f12e43db1ded86f47b0` | 94 |

The three original Near ZIPs and Optional Vegetation ZIP are unchanged. The combined six-archive manifest now contains **370 entries**, including 243 near-detail GLBs. All canonical extracted files passed the regenerated manifest checks after replacement.

Independent comparisons against v1 established:

- All 18 shared geodata/provenance/terms files retain identical SHA-256, including building footprints/heights, roadbeds, sidewalks, parks, island boundary, elevations and original NYC terms. `tile_manifest.json` and core provenance are also unchanged.
- Core retains 5,546 meshes and 29 materials; ground retains 957 meshes and 7 materials. All material properties are semantically identical after resolving texture indices to image hashes. Embedded image-byte multisets are unchanged: 25 core and 9 ground images. GLB array order and some repaired mesh names changed, so raw JSON array equality alone would report a false material difference.
- Scene declarations and the identity node-transform population are preserved; no GLB root transform was added. Common-origin tile metadata and source coordinate data are unchanged. The retained native integration reports document preservation of the original scene coordinates.
- Core triangles increased from 5,632,665 to 5,688,461; ground increased from 2,104,476 to 2,159,786. The updated total is 7,848,247 triangles. This is corrected surface triangulation, not additional facade coverage.

The release retains the supplied constrained-triangulation, native integration and float32 precision audit reports. Their statements about unaffected native geometry were preserved as source evidence; the independent checks above do not claim to rerun Blender's native repair pipeline. Those source reports also explicitly state that Unity runtime performance was not measured by their author.

The 383,800-byte validation fixture remains byte-identical because its Near source ZIP and selected mesh are unchanged. Its metadata now names v2. Twelve portable tests passed after the replacement, including decoding every fixture vertex and index to check the one-time coordinate conversion. Rendering and performance acceptance are recorded separately in [validation.md](validation.md).
