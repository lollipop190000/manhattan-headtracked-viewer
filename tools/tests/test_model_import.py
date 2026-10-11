import base64
import copy
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import model_import as adapter


PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aH1cAAAAASUVORK5CYII=")


def glb_bytes(data):
    raw = json.dumps(data, separators=(",", ":")).encode()
    raw += b" " * (-len(raw) % 4)
    binary = struct.pack("<9f", 0, 0, 0, 1, 0, 0, 0, 1, 0)
    return struct.pack("<IIIII", 0x46546C67, 2, 28 + len(raw) + len(binary), len(raw), 0x4E4F534A) + raw + struct.pack("<II", len(binary), 0x004E4942) + binary


class ModelImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "source"
        (self.source / "exports").mkdir(parents=True)
        (self.source / "shared_textures").mkdir()
        (self.source / "shared_textures/albedo.png").write_bytes(PNG)
        self.gltf = {
            "asset": {"version": "2.0"}, "scene": 0, "scenes": [{"nodes": [0]}],
            "nodes": [
                {"name": "METRIC_TILE", "translation": [-2560, 0, 6784], "children": [1, 3]},
                {"name": "SOURCE_123_0", "translation": [31.125, 0, -8.25], "children": [2],
                 "extras": {"source_building_id": "123_0", "ground_m": 7.5, "source_rings_json": "[[[0,0],[1,0],[0,1]],[[.1,.1],[.2,.1],[.1,.2]]]"}},
                {"name": "wall", "translation": [0, 2, 0], "mesh": 0},
                {"name": "SOURCE_456_0", "mesh": 0, "extras": {"source_building_id": "456_0"}},
            ],
            "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "material": 0}]}],
            "buffers": [{"byteLength": 36}], "bufferViews": [{"buffer": 0, "byteLength": 36}],
            "accessors": [{"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0]}],
            "materials": [{"name": "PBR", "pbrMetallicRoughness": {"baseColorTexture": {"index": 0}}}],
            "textures": [{"source": 0}], "images": [{"uri": "../shared_textures/albedo.png"}],
        }
        self.manifest = {
            "schemaVersion": 1, "datasetId": "fixture", "version": "v1", "legacySourceVersion": "legacy-v2",
            "coordinateContract": copy.deepcopy(adapter.COORDINATES), "files": [],
            "selections": [{"sourceId": "123_0", "file": "exports/model.glb", "rootNode": 1,
                            "rootName": "SOURCE_123_0", "semantics": "replace-complete-body",
                            "contentProfile": "accepted-near-v1", "replacesSourceId": "123_0", "suppressLegacyTrim": True}],
        }
        self.manifest_path = self.root / "manifest.json"
        self.write_source()

    def write_source(self):
        (self.source / "exports/model.glb").write_bytes(glb_bytes(self.gltf))
        self.manifest["files"] = [
            {"path": p, "sha256": adapter.sha256_file(self.source / p), "bytes": (self.source / p).stat().st_size,
             "kind": "glb" if p.endswith(".glb") else "dependency"}
            for p in ("exports/model.glb", "shared_textures/albedo.png")]
        return self.write_manifest()

    def write_manifest(self):
        self.manifest_path.write_bytes(adapter.canonical(self.manifest) + b"\n")
        self.sha = adapter.sha256_file(self.manifest_path)
        return self.sha

    def inspect(self):
        return adapter.validate_manifest(self.manifest_path, self.sha, self.source)

    def test_explicit_selection_preserves_source_id_ancestors_and_metadata(self):
        plan = self.inspect()
        item = plan["selections"][0]
        self.assertEqual([1, 2], item["selectedNodes"])
        self.assertEqual([-2560, 0, 6784], item["ancestorChain"][0]["transform"]["translation"])
        self.assertEqual([31.125, 0, -8.25], item["ancestorChain"][1]["transform"]["translation"])
        self.assertEqual(7.5, item["sourceMetadata"]["ground_m"])
        self.assertIn("source_rings_json", item["sourceMetadata"])
        self.assertFalse(plan["runtimeReady"])
        self.assertEqual([], plan["materialBlockers"])

    def test_source_root_and_working_directory_do_not_change_content_identity(self):
        import shutil
        clone = self.root / "clone"
        shutil.copytree(self.source, clone)
        self.assertEqual(self.inspect(), adapter.validate_manifest(self.manifest_path, self.sha, clone))

    def test_requires_external_manifest_pin(self):
        with self.assertRaisesRegex(ValueError, "Manifest SHA-256"):
            adapter.validate_manifest(self.manifest_path, "0" * 64, self.source)

    def test_changed_payload_is_rejected(self):
        (self.source / "shared_textures/albedo.png").write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "Payload SHA-256"):
            self.inspect()

    def test_manifest_version_cannot_float_silently(self):
        old_sha = self.sha
        self.manifest["version"] = "v2"
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "Manifest SHA-256"):
            adapter.validate_manifest(self.manifest_path, old_sha, self.source)

    def test_no_recursive_alternative_discovery(self):
        (self.source / "exports/candidate.glb").write_bytes(b"not a model")
        self.assertEqual(1, len(self.inspect()["selections"]))

    def test_allowlisted_but_unselected_alternative_rejected(self):
        model = self.manifest["files"][0].copy()
        model["path"] = "exports/alternative.glb"
        (self.source / model["path"]).write_bytes((self.source / "exports/model.glb").read_bytes())
        self.manifest["files"].append(model)
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "unselected GLB"):
            self.inspect()

    def test_duplicate_source_ids_rejected(self):
        self.manifest["selections"] *= 2
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "duplicate selected source ID"):
            self.inspect()

    def test_wrong_node_name_and_metadata_rejected(self):
        for key, value in [("rootName", "not-the-root"), ("sourceId", "456_0")]:
            old = copy.deepcopy(self.manifest)
            self.manifest["selections"][0][key] = value
            if key == "sourceId":
                self.manifest["selections"][0]["replacesSourceId"] = value
            self.write_manifest()
            with self.assertRaisesRegex(ValueError, "metadata mismatch"):
                self.inspect()
            self.manifest = old

    def test_overlapping_root_ownership_rejected(self):
        self.gltf["nodes"][2]["extras"] = {"source_building_id": "456_0"}
        self.gltf["nodes"][2]["name"] = "SOURCE_456_0"
        second = self.manifest["selections"][0].copy()
        second.update(sourceId="456_0", replacesSourceId="456_0", rootNode=2, rootName="SOURCE_456_0")
        self.manifest["selections"].append(second)
        self.write_source()
        with self.assertRaisesRegex(ValueError, "overlap|foreign source"):
            self.inspect()

    def test_source_node_must_belong_to_active_scene(self):
        self.gltf["nodes"][0]["children"] = [3]
        self.write_source()
        with self.assertRaisesRegex(ValueError, "outside active scene"):
            self.inspect()

    def test_foreign_source_in_implicit_subtree_is_rejected(self):
        self.gltf["nodes"][2]["extras"] = {"source_building_id": "456_0"}
        self.write_source()
        with self.assertRaisesRegex(ValueError, "foreign source ID"):
            self.inspect()

    def test_foreign_source_ancestor_is_rejected(self):
        self.gltf["nodes"][0]["extras"] = {"source_building_id": "456_0"}
        self.write_source()
        with self.assertRaisesRegex(ValueError, "foreign source-ID ancestor"):
            self.inspect()

    def test_negative_gltf_indices_are_rejected(self):
        for change in (lambda: self.gltf["nodes"][2].update(mesh=-1),
                       lambda: self.gltf["meshes"][0]["primitives"][0].update(material=-1),
                       lambda: self.gltf["textures"][0].update(source=-1)):
            old = copy.deepcopy(self.gltf)
            change()
            self.write_source()
            with self.assertRaisesRegex(ValueError, "Invalid glTF"):
                self.inspect()
            self.gltf = old

    def test_boolean_material_index_cannot_alias_valid_integer(self):
        self.gltf["meshes"][0]["primitives"].append({"attributes": {"POSITION": 0}, "material": False})
        self.write_source()
        with self.assertRaisesRegex(ValueError, "Invalid glTF material index"):
            self.inspect()

    def test_cycles_and_multiple_parents_rejected(self):
        self.gltf["nodes"][2]["children"] = [0]
        self.write_source()
        with self.assertRaisesRegex(ValueError, "Cyclic"):
            self.inspect()
        self.gltf["nodes"][2]["children"] = [3]
        self.write_source()
        with self.assertRaisesRegex(ValueError, "multiply-parented"):
            self.inspect()

    def test_legacy_additive_cannot_suppress_complete_body(self):
        item = self.manifest["selections"][0]
        item["semantics"] = "legacy-additive"
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "cannot claim replacements"):
            self.inspect()
        item.update(replacesSourceId=None, suppressLegacyTrim=False)
        self.write_manifest()
        self.assertEqual("legacy-additive", self.inspect()["selections"][0]["semantics"])

    def test_replacement_must_explicitly_suppress_same_id_body_and_trim(self):
        for key, value in [("replacesSourceId", "456_0"), ("suppressLegacyTrim", False)]:
            old = copy.deepcopy(self.manifest)
            self.manifest["selections"][0][key] = value
            self.write_manifest()
            with self.assertRaisesRegex(ValueError, "same-ID body and trim"):
                self.inspect()
            self.manifest = old

    def test_external_uri_preserves_parent_relative_paths(self):
        target, _ = adapter.stage(self.manifest_path, self.sha, self.source, self.root / "stage")
        self.assertEqual(PNG, (target / "payload/exports/../shared_textures/albedo.png").read_bytes())
        self.assertEqual((self.source / "exports/model.glb").read_bytes(), (target / "payload/exports/model.glb").read_bytes())

    def test_unpinned_external_texture_rejected(self):
        self.manifest["files"] = self.manifest["files"][:1]
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "Unpinned external dependency"):
            self.inspect()

    def test_uri_escape_remote_windows_encoded_and_query_rejected(self):
        for uri in ("../../secret.png", "https://example.com/a.png", "file:///tmp/a.png", "C:/a.png", "../shared_textures/%61lbedo.png", "../shared_textures/a.png?x=1", "..\\a.png"):
            with self.subTest(uri=uri), self.assertRaises(ValueError):
                adapter.external_path("exports/model.glb", uri)

    def test_case_alias_and_traversal_payloads_rejected(self):
        self.manifest["files"].append({**self.manifest["files"][0], "path": "EXPORTS/MODEL.GLB"})
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "case-aliased"):
            self.inspect()
        self.manifest["files"][-1]["path"] = "../escape.glb"
        self.write_manifest()
        with self.assertRaises(ValueError):
            self.inspect()

    def test_symlink_source_payload_rejected(self):
        payload = self.source / "shared_textures/albedo.png"
        outside = self.root / "outside.png"
        payload.rename(outside)
        payload.symlink_to(outside)
        with self.assertRaisesRegex(ValueError, "Symlink payload"):
            self.inspect()

    def test_symlink_destination_rejected(self):
        real = self.root / "real"
        real.mkdir()
        link = self.root / "link"
        link.symlink_to(real, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "Symlink staging"):
            adapter.stage(self.manifest_path, self.sha, self.source, link)
        self.assertEqual([], list(real.iterdir()))

    def test_stage_idempotent_but_tampering_fails_closed(self):
        target, plan = adapter.stage(self.manifest_path, self.sha, self.source, self.root / "stage")
        self.assertEqual((target, plan), adapter.stage(self.manifest_path, self.sha, self.source, self.root / "stage"))
        (target / "import-plan.json").write_text("{}")
        with self.assertRaisesRegex(ValueError, "immutable stage differs"):
            adapter.stage(self.manifest_path, self.sha, self.source, self.root / "stage")

    def test_copy_failure_does_not_publish_or_leave_scratch(self):
        with patch.object(adapter.shutil, "copyfile", side_effect=OSError("disk full")):
            with self.assertRaisesRegex(OSError, "disk full"):
                adapter.stage(self.manifest_path, self.sha, self.source, self.root / "stage")
        self.assertEqual([], list((self.root / "stage").rglob("import-plan.json")))
        self.assertEqual([], list((self.root / "stage").rglob(".staging-*")))

    def test_source_mutation_during_copy_cannot_publish(self):
        real_copy = adapter.shutil.copyfile
        def tamper(source, target):
            result = real_copy(source, target)
            if str(target).endswith(".png"):
                Path(target).write_bytes(b"corrupt")
            return result
        with patch.object(adapter.shutil, "copyfile", side_effect=tamper):
            with self.assertRaisesRegex(ValueError, "Payload SHA-256"):
                adapter.stage(self.manifest_path, self.sha, self.source, self.root / "stage")
        self.assertEqual([], list((self.root / "stage").rglob("import-plan.json")))

    def test_resolved_pbr_defaults_and_texture_identity(self):
        material = self.inspect()["selections"][0]["materials"][0]
        self.assertEqual(1, material["resolved"]["metallicFactor"])
        self.assertEqual([1, 1, 1, 1], material["resolved"]["baseColorFactor"])
        self.assertEqual(adapter.sha256_file(self.source / "shared_textures/albedo.png"), material["resolved"]["textures"]["baseColorTexture"]["image"]["sha256"])

    def test_emission_strength_and_optical_parameters_affect_identity_and_block(self):
        initial = self.inspect()["selections"][0]["materials"][0]["key"]
        self.gltf["materials"][0].update(emissiveFactor=[.5, 1, 2], alphaMode="OPAQUE", extensions={
            "KHR_materials_emissive_strength": {"emissiveStrength": 2.2},
            "KHR_materials_transmission": {"transmissionFactor": 1}, "KHR_materials_ior": {"ior": 1.48}})
        self.write_source()
        item = self.inspect()["selections"][0]["materials"][0]
        self.assertNotEqual(initial, item["key"])
        self.assertEqual([1.1, 2.2, 4.4], item["resolved"]["resolvedEmissiveFactor"])
        self.assertEqual(1.48, item["resolved"]["extensions"]["KHR_materials_ior"]["ior"])
        self.assertIn("OPTICAL_PROFILE_REQUIRED", item["diagnostics"])
        self.assertIn("LEGACY_BAKE_DROPS_EMISSION", item["diagnostics"])
        self.assertEqual("OPAQUE", item["resolved"]["alphaMode"])

    def test_unknown_material_extensions_are_not_silently_dropped(self):
        self.gltf["materials"][0]["extensions"] = {"VENDOR_new_glass": {"dispersion": 12}}
        self.write_source()
        item = self.inspect()["selections"][0]["materials"][0]
        self.assertEqual({"dispersion": 12}, item["resolved"]["extensions"]["VENDOR_new_glass"])
        self.assertIn("UNSUPPORTED_MATERIAL_EXTENSION:VENDOR_new_glass", item["diagnostics"])

    def test_optical_texture_payload_change_changes_identity(self):
        self.gltf["materials"][0]["extensions"] = {"KHR_materials_transmission": {"transmissionTexture": {"index": 0}}}
        self.write_source()
        before = self.inspect()["selections"][0]["materials"][0]["key"]
        (self.source / "shared_textures/albedo.png").write_bytes(PNG + b"another version")
        self.write_source()
        self.assertNotEqual(before, self.inspect()["selections"][0]["materials"][0]["key"])

    def test_coordinate_and_unknown_contract_changes_rejected(self):
        self.manifest["coordinateContract"]["importHandednessConversions"] = 2
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "coordinate contract"):
            self.inspect()
        self.manifest["coordinateContract"] = adapter.COORDINATES
        self.manifest["allowUnsupportedOptics"] = True
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "Unexpected or missing"):
            self.inspect()

    def test_boolean_coordinate_count_cannot_alias_integer(self):
        self.manifest["coordinateContract"]["importHandednessConversions"] = True
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "coordinate contract"):
            self.inspect()

    def test_invalid_glb_length_and_duplicate_json_keys_rejected(self):
        file = self.source / "exports/model.glb"
        file.write_bytes(file.read_bytes() + b"extra")
        self.manifest["files"][0].update(bytes=file.stat().st_size, sha256=adapter.sha256_file(file))
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "GLB 2 header"):
            self.inspect()
        with self.assertRaisesRegex(ValueError, "Duplicate JSON key"):
            adapter.strict_json('{"a":1,"a":2}')

    def test_mixed_matrix_and_trs_rejected(self):
        self.gltf["nodes"][1]["matrix"] = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
        self.write_source()
        with self.assertRaisesRegex(ValueError, "mixes matrix and TRS"):
            self.inspect()


if __name__ == "__main__":
    unittest.main()
