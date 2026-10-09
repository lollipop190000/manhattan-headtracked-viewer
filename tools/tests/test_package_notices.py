import hashlib
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import package_notices


class PackageNoticeTests(unittest.TestCase):
    def make_repository_fixture(self, root):
        revision = "1" * 40
        source_root, technology_root, package_cache = root / "source", root / "technology", root / "packages"
        shader_payloads = {
            "LICENSE-Unity-URP.md": b"Original Unity license notice.\r\nUnchanged bytes.\r\n",
            "NOTICE.md": "URP 17.3.0 adapted source notice — original attribution.\r\n".encode("utf-8"),
        }
        inputs = {
            "LICENSE": b"Viewer MIT license.\r\n",
            "THIRD_PARTY.md": b"Viewer component attribution.\r\n",
            "tools/technology-version.json": json.dumps(dict(revision=revision)).encode("utf-8"),
        }
        inputs.update({"ManhattanViewer/Assets/Shaders/" + name: data for name, data in shader_payloads.items()})
        for name, data in inputs.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        technology_root.mkdir()
        for name in ("LICENSE", "THIRD_PARTY.md"):
            (technology_root / name).write_bytes(b"Technology component notice.\r\n")
        package = package_cache / "com.unity.fixture@1.0"
        package.mkdir(parents=True)
        (package / "LICENSE.md").write_bytes(b"Unity package license.\r\n")
        packs = []
        for asset in package_notices.source_assets.ARCHIVES:
            directory = Path(asset).stem
            names = package_notices.source_assets.REQUIRED_NOTICE_INPUTS.get(asset, {"model.glb"})
            entries = []
            for name in sorted(names):
                data = (asset + "/" + name).encode("utf-8")
                path = source_root / directory / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
                entries.append(dict(path=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
            packs.append(dict(asset=asset, directory=directory, bytes=1, sha256="0" * 64,
                              expandedBytes=sum(e["bytes"] for e in entries), files=entries))
        manifest_path = root / "assets/source-manifest.json"
        manifest_path.parent.mkdir()
        manifest_path.write_text(json.dumps(dict(schemaVersion=1, sourceVersion="notice-fixture-v2",
                                                releaseRepository="example/viewer", releaseTag="fixture-v2", packs=packs)), encoding="utf-8")
        return source_root, technology_root, package_cache, manifest_path, revision, shader_payloads

    def package_repository_fixture(self, root, fixture):
        source, technology, cache, manifest, revision, _ = fixture
        with mock.patch.object(package_notices, "ROOT", root), \
             mock.patch.object(package_notices.source_assets, "MANIFEST", manifest), \
             mock.patch.object(package_notices.subprocess, "run", return_value=SimpleNamespace(stdout=revision + "\n")):
            return package_notices.package_notices(root / "build", source, technology, cache)

    def test_packaged_shader_license_and_notice_are_required_and_preserved(self):
        temporary_root = package_notices.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            fixture = self.make_repository_fixture(root)
            index = self.package_repository_fixture(root, fixture)
            self.assertEqual(1, index["schemaVersion"])
            records = {record["path"]: record for record in index["files"]}
            for name, original in fixture[-1].items():
                relative = "CityDetailRenderer/" + name
                target = root / "build/Notices" / relative
                self.assertEqual(original, target.read_bytes())
                self.assertEqual(original, (root / "ManhattanViewer/Assets/Shaders" / name).read_bytes())
                self.assertEqual(len(original), records[relative]["bytes"])
                self.assertEqual(hashlib.sha256(original).hexdigest(), records[relative]["sha256"])
            self.assertEqual(index, json.loads((root / "build/Notices/manifest.json").read_text(encoding="utf-8")))

    def test_missing_either_shader_notice_rejects_build_notice_packaging(self):
        temporary_root = package_notices.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        for missing in package_notices.CITY_DETAIL_NOTICES:
            with self.subTest(missing=missing), tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
                root = Path(temporary)
                fixture = self.make_repository_fixture(root)
                (root / "ManhattanViewer/Assets/Shaders" / missing).unlink()
                with self.assertRaisesRegex(ValueError, "Required notice is missing"):
                    self.package_repository_fixture(root, fixture)
                self.assertFalse((root / "build/Notices/manifest.json").exists())

    def test_copy_rejects_changed_destination_bytes(self):
        temporary_root = package_notices.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            source, target = root / "NOTICE.md", root / "build/NOTICE.md"
            source.write_bytes(b"Original attribution.")
            with mock.patch.object(package_notices.shutil, "copyfile", side_effect=lambda src, dst: dst.write_bytes(b"Changed attribution.")):
                with self.assertRaisesRegex(ValueError, "Copied notice SHA-256/size differs"):
                    package_notices.copy_notice(source, target)
            self.assertEqual(b"Original attribution.", source.read_bytes())

    def test_copy_preserves_original_and_rejects_altered_terms(self):
        temporary_root = package_notices.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            source = root / "NYC_Open_Data_Terms.html"
            payload = b"Original terms, attribution, and conditions."
            source.write_bytes(payload)
            expected = dict(bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest())
            target = root / "build/Notices/terms.html"
            copied = package_notices.copy_notice(source, target, expected)
            self.assertEqual(expected["sha256"], copied["sha256"])
            self.assertEqual(payload, target.read_bytes())
            self.assertEqual(payload, source.read_bytes())
            source.write_bytes(payload + b" altered")
            with self.assertRaisesRegex(ValueError, "notice SHA-256/size changed"):
                package_notices.copy_notice(source, target, expected)
            self.assertEqual(payload, target.read_bytes())


if __name__ == "__main__":
    unittest.main()
