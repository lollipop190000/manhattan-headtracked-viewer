import hashlib
import json
import os
from pathlib import Path
import stat
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import package_windows


class WindowsPackageTests(unittest.TestCase):
    def setUp(self):
        scratch = package_windows.ROOT / ".cache/tests"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.assertTrue(self.root.resolve().is_relative_to(scratch.resolve()))
        self.build = self.root / "build"
        self.viewer, self.technology = "1" * 40, "2" * 40
        self.source = self.root / "source-manifest.json"
        self.source.write_text(json.dumps(dict(sourceVersion="shipping-fixture-v1")), encoding="utf-8")
        self.readme = self.root / "windows-release.md"
        self.readme.write_bytes(b"Windows guide.\r\nKeep Notices with the player.\r\n")
        self.identity = dict(viewerRevision=self.viewer, technologyRevision=self.technology,
                             sourceManifestSha256=package_windows.sha256_file(self.source),
                             buildUtc="2026-10-10T00:00:00.0000000Z", dirtyWorkingTree=False)
        for name in package_windows.REQUIRED_FILES:
            self.write(name, b"fixture runtime bytes")
        self.write("ManhattanViewer_Data/StreamingAssets/aa/StandaloneWindows64/city.bundle", b"city tile payload")
        self.write_identity(self.identity)
        notice_records = []
        for name in package_windows.REQUIRED_NOTICES:
            if name == "manifest.json":
                continue
            payload = ("Original attribution " + name).encode()
            if name == "source-manifest.json":
                payload = self.source.read_bytes()
            elif name == "technology-version.json":
                payload = json.dumps(dict(revision=self.technology)).encode()
            path = self.write("Notices/" + name, payload)
            if name != "README.txt":
                notice_records.append(dict(path=name, bytes=len(payload), sha256=package_windows.sha256_file(path)))
        path = self.write("Notices/UnityPackages/com.unity.fixture@1.0/LICENSE.md", b"Original Unity license")
        notice_records.append(dict(path=path.relative_to(self.build / "Notices").as_posix(),
                                   bytes=path.stat().st_size, sha256=package_windows.sha256_file(path)))
        self.write("Notices/manifest.json", json.dumps(dict(schemaVersion=1, sourceVersion="shipping-fixture-v1",
                                                          technologyRevision=self.technology, files=notice_records)).encode())

    def write(self, relative, payload):
        path = self.build / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
        return path

    @staticmethod
    def asset_bytes(identity):
        name = b"ViewerBuildIdentity"
        header = b"\0" * 16 + struct.pack("<I", len(name)) + name
        header += b"\0" * ((-len(header)) % 4)
        payload = json.dumps(identity, indent=2).encode()
        serialized = header + struct.pack("<I", len(payload)) + payload
        return serialized + b"\0" * ((-len(serialized)) % 4)

    def write_identity(self, identity):
        return self.write("ManhattanViewer_Data/resources.assets", self.asset_bytes(identity))

    def package(self, directory="release", **kwargs):
        return package_windows.package_windows(self.build, self.root / directory / "Windows.zip", self.viewer,
                                               self.technology, self.source, self.readme, **kwargs)

    def test_zip_is_reproducible_and_receipt_hashes_exact_allowed_payload(self):
        private = ("Player.log", "tracker-error.log", "ManhattanViewer.provenance.json",
                   "ManhattanViewer_BurstDebugInformation_DoNotShip/debug.pdb", "outside/unlisted.txt",
                   "ManhattanViewer_Data/physical-calibration.json", "ManhattanViewer_Data/tracker-output.log",
                   "ManhattanViewer_Data/tracker_runtime_calibration.json", "ManhattanViewer_Data/nested/Tracker.HEARTBEAT",
                   "ManhattanViewer_Data/face_landmarker.task", "ManhattanViewer_Data/.venv/private.txt")
        for name in private:
            self.write(name, b"PRIVATE MUST NOT SHIP")
        first, second = self.package("release1"), self.package("release2")
        self.assertEqual(first["zipSha256"], second["zipSha256"])
        self.assertEqual(Path(first["zip"]).read_bytes(), Path(second["zip"]).read_bytes())
        with zipfile.ZipFile(first["zip"]) as archive:
            names = archive.namelist()
            self.assertEqual(sorted(names), names)
            self.assertFalse(set(private) & set(names))
            self.assertTrue(all(info.date_time == (1980, 1, 1, 0, 0, 0) for info in archive.infolist()))
            receipt_bytes = archive.read(package_windows.RECEIPT_NAME)
            self.assertEqual(receipt_bytes, Path(first["integrityReceipt"]).read_bytes())
            receipt = json.loads(receipt_bytes)
            self.assertEqual(receipt["buildIdentity"], self.identity)
            self.assertEqual(len(names) - 1, receipt["fileCount"])
            self.assertEqual(hashlib.sha256(json.dumps(receipt["files"], sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest(), receipt["fileTreeSha256"])
            self.assertNotIn(package_windows.RECEIPT_NAME, [record["path"] for record in receipt["files"]])
            self.assertEqual(self.readme.read_bytes(), archive.read("README-Windows.md"))
            for record in receipt["files"]:
                payload = archive.read(record["path"])
                self.assertEqual(len(payload), record["bytes"])
                self.assertEqual(hashlib.sha256(payload).hexdigest(), record["sha256"])
        self.assertEqual(first["zipSha256"] + "  Windows.zip\n", Path(first["checksum"]).read_text(encoding="utf-8"))

    def test_dirty_missing_mismatched_or_ambiguous_embedded_identity_cannot_ship(self):
        cases = ({**self.identity, "dirtyWorkingTree": True}, {**self.identity, "dirtyWorkingTree": "false"},
                 {**self.identity, "viewerRevision": "3" * 40}, {**self.identity, "technologyRevision": "4" * 40},
                 {**self.identity, "sourceManifestSha256": "5" * 64})
        self.write("ManhattanViewer.provenance.json", json.dumps(self.identity).encode())
        for identity in cases:
            with self.subTest(identity=identity):
                self.write_identity(identity)
                with self.assertRaises(ValueError):
                    self.package()
        self.write("ManhattanViewer_Data/resources.assets", b"sidecar identity is insufficient")
        with self.assertRaisesRegex(ValueError, "embedded ViewerBuildIdentity"):
            self.package()
        self.write("ManhattanViewer_Data/resources.assets", self.asset_bytes(self.identity) * 2)
        with self.assertRaisesRegex(ValueError, "Exactly one"):
            self.package()
        self.assertFalse((self.root / "release/Windows.zip").exists())

    def test_original_notice_changes_and_missing_runtime_reject_before_output(self):
        notice = self.build / "Notices/CityDetailRenderer/NOTICE.md"
        original = notice.read_bytes(); notice.write_bytes(original + b"altered")
        with self.assertRaisesRegex(ValueError, "notice bytes/hash mismatch"):
            self.package()
        notice.write_bytes(original)
        (self.build / "UnityPlayer.dll").unlink()
        with self.assertRaisesRegex(ValueError, "Required shipping file"):
            self.package()
        self.assertFalse((self.root / "release/Windows.zip").exists())

    def test_windows_traversal_absolute_and_alias_names_are_rejected(self):
        for value in ("../escape.txt", "/absolute", "C:/escape", "folder\\..\\escape", "folder/../escape", "folder//file",
                      "folder/file.", "folder/file ", "folder/NUL.txt", "folder/COM1", "folder/x:y", "folder/\x01secret"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                package_windows.checked_relative(value)
        self.assertEqual("Notices/City/NYC Terms.html", str(package_windows.checked_relative("Notices/City/NYC Terms.html")))
        index = self.build / "Notices/manifest.json"
        data = json.loads(index.read_text(encoding="utf-8")); data["files"][0]["path"] = "../UnityPlayer.dll"
        index.write_text(json.dumps(data), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            self.package()

    def test_symlink_inside_allowlisted_tree_is_rejected(self):
        target = self.root / "private.txt"; target.write_bytes(b"private")
        link = self.build / "ManhattanViewer_Data/leak.txt"
        try:
            os.symlink(target, link)
        except (OSError, NotImplementedError) as error:
            if os.name != "nt":
                self.skipTest("Filesystem cannot create the symlink fixture: " + str(error))
            # Sandboxed Windows may deny creating both links and junctions. Exercise the
            # real packaging path with the same reparse metadata returned by Windows lstat.
            link.write_bytes(b"must not be read")
            original_lstat = Path.lstat

            def reparse_lstat(path, *args, **kwargs):
                info = original_lstat(path, *args, **kwargs)
                return SimpleNamespace(st_mode=info.st_mode, st_file_attributes=stat.FILE_ATTRIBUTE_REPARSE_POINT) if path == link else info

            with mock.patch.object(Path, "lstat", reparse_lstat), self.assertRaisesRegex(ValueError, "Symlinks"):
                self.package()
            return
        with self.assertRaisesRegex(ValueError, "Symlinks"):
            self.package()

    def test_input_change_during_zip_write_never_publishes_a_receipt_or_archive(self):
        original_zip = zipfile.ZipFile

        def changed_input(*args, **kwargs):
            (self.build / "ManhattanViewer.exe").write_bytes(b"unexpected changed executable")
            return original_zip(*args, **kwargs)

        with mock.patch.object(package_windows.zipfile, "ZipFile", side_effect=changed_input):
            with self.assertRaisesRegex(ValueError, "Input changed while packaging"):
                self.package()
        self.assertFalse((self.root / "release/Windows.zip").exists())
        self.assertFalse((self.root / "release/Windows.integrity.json").exists())
        self.assertEqual([], list((self.root / "release").glob("*.tmp")))


if __name__ == "__main__":
    unittest.main()
