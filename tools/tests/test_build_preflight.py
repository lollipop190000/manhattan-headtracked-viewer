from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import build_preflight


COMMITTED = (
    b"%YAML 1.1\nEditorBuildSettings:\n"
    b"  m_Scenes:\n  - enabled: 1\n    path: Assets/Scenes/ManhattanViewer.unity\n"
    b"    guid: " + b"3" * 32 + b"\n  m_configObjects:\n"
    b"    com.unity.addressableassets: {fileID: 11400000, guid: " + b"1" * 32 + b", type: 2}\n"
    b"    another.config: {fileID: 11400000, guid: " + b"4" * 32 + b", type: 2}\n"
    b"  m_UseUCBPForAssetBundles: 0\n"
)
GENERATED = COMMITTED.replace(b"1" * 32, b"2" * 32)


class BuildPreflightTests(unittest.TestCase):
    def test_only_generated_guid_is_normalized(self):
        self.assertEqual(COMMITTED, build_preflight.generated_guid_replacement(COMMITTED, GENERATED))
        self.assertIsNone(build_preflight.generated_guid_replacement(COMMITTED, COMMITTED))

    def test_git_text_normalization_preserves_working_line_endings(self):
        working = GENERATED.replace(b"\n", b"\r\n")
        self.assertEqual(COMMITTED.replace(b"\n", b"\r\n"),
                         build_preflight.generated_guid_replacement(COMMITTED, working))

    def test_authored_scene_changes_with_generated_guid_are_not_normalized(self):
        for old, new in ((b"enabled: 1", b"enabled: 0"),
                         (b"Assets/Scenes/ManhattanViewer.unity", b"Assets/Scenes/TeamLevel.unity"),
                         (b"3" * 32, b"5" * 32)):
            with self.subTest(change=new):
                edited = GENERATED.replace(old, new)
                self.assertIsNone(build_preflight.generated_guid_replacement(COMMITTED, edited))

    def test_other_config_and_addressables_type_edits_are_not_normalized(self):
        edits = (
            GENERATED.replace(b"4" * 32, b"5" * 32),
            GENERATED.replace(b"m_UseUCBPForAssetBundles: 0", b"m_UseUCBPForAssetBundles: 1"),
            GENERATED.replace(b"fileID: 11400000, guid: " + b"2" * 32,
                              b"fileID: 11400001, guid: " + b"2" * 32),
            GENERATED.replace(b"2" * 32 + b", type: 2", b"2" * 32 + b", type: 3"),
        )
        for edited in edits:
            with self.subTest(edited=edited):
                self.assertIsNone(build_preflight.generated_guid_replacement(COMMITTED, edited))

    def test_missing_duplicate_or_malformed_addressables_reference_is_rejected(self):
        line = next(match.group(0) for match in build_preflight.GUID_LINE.finditer(GENERATED))
        for edited in (GENERATED.replace(line, b""), GENERATED + line,
                       GENERATED.replace(b"2" * 32, b"not-a-guid")):
            with self.subTest(edited=edited):
                self.assertIsNone(build_preflight.generated_guid_replacement(COMMITTED, edited))

    def test_preflight_only_reads_git_head_and_never_disables_attributes(self):
        temporary_root = build_preflight.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            target = root / build_preflight.BUILD_SETTINGS
            target.parent.mkdir(parents=True)
            target.write_bytes(GENERATED)
            authored = root / "authored.cs"
            attributes = root / ".gitattributes"
            authored.write_bytes(b"team-authored code change")
            attributes.write_bytes(b"* text=auto eol=lf\n")
            with mock.patch.object(build_preflight.subprocess, "run", return_value=SimpleNamespace(stdout=COMMITTED)) as git:
                self.assertTrue(build_preflight.run_preflight(root))
            git.assert_called_once_with(["git", "-C", str(root), "show", "HEAD:" + build_preflight.BUILD_SETTINGS],
                                        check=True, capture_output=True)
            self.assertEqual(COMMITTED, target.read_bytes())
            self.assertEqual(b"team-authored code change", authored.read_bytes())
            self.assertEqual(b"* text=auto eol=lf\n", attributes.read_bytes())
            edited = GENERATED.replace(b"enabled: 1", b"enabled: 0")
            target.write_bytes(edited)
            with mock.patch.object(build_preflight.subprocess, "run", return_value=SimpleNamespace(stdout=COMMITTED)):
                self.assertFalse(build_preflight.run_preflight(root))
            self.assertEqual(edited, target.read_bytes(), "The CLI must still see and reject the authored scene edit.")


if __name__ == "__main__":
    unittest.main()
