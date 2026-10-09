import copy
import hashlib
import json
from pathlib import Path
import stat
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import source_assets as source


class SourceAssetsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary_root = source.ROOT / ".cache" / "tests"
        cls.temporary_root.mkdir(parents=True, exist_ok=True)

    def make_pack(self, root, name=None, entries=None):
        name = name or source.ARCHIVES[0]
        entries = entries or {"city/model.glb": b"sample glb payload", "PROVENANCE.md": b"source credit"}
        path = root / name
        with zipfile.ZipFile(path, "w") as archive:
            for key, value in entries.items():
                archive.writestr(key, value)
        files = [dict(path=key, bytes=len(value), sha256=hashlib.sha256(value).hexdigest())
                 for key, value in sorted(entries.items())]
        pack = dict(asset=name, directory=Path(name).stem, bytes=path.stat().st_size,
                    sha256=source.sha256_file(path), expandedBytes=sum(f["bytes"] for f in files), files=files)
        return path, pack

    def test_versioned_manifest_has_all_packs_and_detail_tiles(self):
        data = json.loads(source.MANIFEST.read_text(encoding="utf-8"))
        source.validate_manifest(data)
        detail = [f for p in data["packs"] if p["asset"].startswith("Manhattan_Near_")
                  for f in p["files"] if f["path"].endswith(".glb")]
        self.assertEqual(243, len(detail))
        names = [f["path"] for f in detail]
        self.assertEqual(len(names), len(set(names)))
        core = data["packs"][0]
        self.assertTrue({"Manhattan_Core.glb", "Manhattan_Streets_Parks.glb", "PROVENANCE.md",
                         "NYC_Open_Data_Terms.html", "tile_manifest.json"}.issubset({f["path"] for f in core["files"]}))

    def test_transport_or_entry_changes_are_rejected(self):
        with tempfile.TemporaryDirectory(dir=self.temporary_root) as temporary:
            path, pack = self.make_pack(Path(temporary))
            source.verify_archive(path, pack, full=True)
            bad = copy.deepcopy(pack)
            bad["files"][0]["sha256"] = "0" * 64
            with self.assertRaisesRegex(ValueError, "member differs"):
                source.verify_archive(path, bad, full=True)
            with path.open("ab") as stream:
                stream.write(b"modified")
            with self.assertRaisesRegex(ValueError, "SHA-256/size"):
                source.verify_archive(path, pack)

    def test_extract_is_idempotent_and_preserves_local_edits(self):
        with tempfile.TemporaryDirectory(dir=self.temporary_root) as temporary:
            root = Path(temporary)
            path, pack = self.make_pack(root)
            target = root / "sources"
            source.extract_pack(path, target, pack)
            source.extract_pack(path, target, pack)
            model = target / pack["directory"] / "city" / "model.glb"
            model.write_bytes(b"team edit")
            with self.assertRaisesRegex(ValueError, "changed source file"):
                source.extract_pack(path, target, pack)
            self.assertEqual(b"team edit", model.read_bytes())

    def test_rejects_posix_and_windows_escape_and_alias_paths(self):
        unsafe = ("../model.glb", "/model.glb", "C:/model.glb", "city/../../model.glb",
                  "city\\model.glb", "city//model.glb", "city/./model.glb", "CON.txt",
                  "city/model.glb.", "city/model.glb ", "city/model.glb:stream")
        for path in unsafe:
            with self.subTest(path=path), self.assertRaises(ValueError):
                source.safe_relative(path)

    def test_symlink_and_case_alias_zip_entries_rejected(self):
        with tempfile.TemporaryDirectory(dir=self.temporary_root) as temporary:
            path = Path(temporary) / "bad.zip"
            with zipfile.ZipFile(path, "w") as archive:
                item = zipfile.ZipInfo("city/link.glb")
                item.create_system = 3
                item.external_attr = (stat.S_IFLNK | 0o777) << 16
                archive.writestr(item, "../../outside")
            with zipfile.ZipFile(path) as archive, self.assertRaisesRegex(ValueError, "symlinks"):
                source.archive_members(archive)
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("city/model.glb", b"a")
                archive.writestr("CITY/MODEL.glb", b"b")
            with zipfile.ZipFile(path) as archive, self.assertRaisesRegex(ValueError, "case-aliased"):
                source.archive_members(archive)

    def test_manifest_rejects_invalid_totals_and_destination(self):
        data = json.loads(source.MANIFEST.read_text(encoding="utf-8"))
        bad = copy.deepcopy(data)
        bad["packs"][0]["expandedBytes"] += 1
        with self.assertRaisesRegex(ValueError, "expanded byte"):
            source.validate_manifest(bad)
        bad = copy.deepcopy(data)
        bad["packs"][0]["directory"] = "../outside"
        with self.assertRaisesRegex(ValueError, "destination"):
            source.validate_manifest(bad)

    def test_manifest_requires_original_reuse_terms(self):
        data = json.loads(source.MANIFEST.read_text(encoding="utf-8"))
        core = data["packs"][0]
        core["files"] = [entry for entry in core["files"] if entry["path"] != "NYC_Open_Data_Terms.html"]
        core["expandedBytes"] = sum(entry["bytes"] for entry in core["files"])
        with self.assertRaisesRegex(ValueError, "provenance or reuse terms"):
            source.validate_manifest(data)


if __name__ == "__main__":
    unittest.main()
