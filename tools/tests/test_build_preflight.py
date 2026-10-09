from pathlib import Path
import subprocess
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
CITY_MATERIAL = "ManhattanViewer/Assets/Resources/CityDetailMaterial.mat"
WINDOW_MATERIAL = "ManhattanViewer/Assets/Resources/WindowFrameMaterial.mat"
MATERIAL = (
    b"%YAML 1.1\nMaterial:\n  m_Name: CityDetailMaterial\n"
    b"  m_Shader: {fileID: 4800000, guid: " + b"6" * 32 + b", type: 3}\n"
    b"  m_CustomRenderQueue: -1\n  m_LockedProperties:\n"
    b"  m_SavedProperties:\n    m_Colors:\n    - _BaseColor: {r: 1, g: 1, b: 1, a: 1}\n"
)
WINDOW = MATERIAL.replace(b"CityDetailMaterial", b"WindowFrameMaterial")


def serializer_spaces(material: bytes, *, name: bool = False) -> bytes:
    result = material.replace(b"m_LockedProperties:\n", b"m_LockedProperties: \n")
    if name:
        result = result.replace(b"m_Name: WindowFrameMaterial\n", b"m_Name: WindowFrameMaterial \n")
    return result


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

    def test_known_material_serializer_spaces_are_idempotently_normalized(self):
        for relative_path, committed, working in (
            (CITY_MATERIAL, MATERIAL, serializer_spaces(MATERIAL)),
            (WINDOW_MATERIAL, WINDOW, serializer_spaces(WINDOW)),
            (WINDOW_MATERIAL, WINDOW, serializer_spaces(WINDOW, name=True)),
            (WINDOW_MATERIAL, WINDOW, WINDOW.replace(b"WindowFrameMaterial\n", b"WindowFrameMaterial \n")),
        ):
            with self.subTest(path=relative_path, working=working):
                replacement = build_preflight.material_space_replacement(relative_path, committed, working)
                self.assertEqual(committed, replacement)
                self.assertIsNone(build_preflight.material_space_replacement(relative_path, committed, replacement))
                self.assertIsNone(build_preflight.material_space_replacement(relative_path, committed, committed))

    def test_material_repair_preserves_working_crlf(self):
        working = serializer_spaces(WINDOW, name=True).replace(b"\n", b"\r\n")
        self.assertEqual(WINDOW.replace(b"\n", b"\r\n"),
                         build_preflight.material_space_replacement(WINDOW_MATERIAL, WINDOW, working))

    def test_authored_material_changes_with_serializer_spaces_are_not_normalized(self):
        edits = (
            (b"6" * 32, b"7" * 32),
            (b"m_CustomRenderQueue: -1", b"m_CustomRenderQueue: 2450"),
            (b"{r: 1, g: 1, b: 1, a: 1}", b"{r: 0.5, g: 1, b: 1, a: 1}"),
            (b"WindowFrameMaterial", b"TeamWindowFrame"),
            (b"m_LockedProperties: ", b"m_LockedProperties: _BaseColor "),
        )
        for old, new in edits:
            with self.subTest(change=new):
                working = serializer_spaces(WINDOW, name=True).replace(old, new)
                self.assertIsNone(build_preflight.material_space_replacement(WINDOW_MATERIAL, WINDOW, working))

    def test_only_exact_paths_and_known_space_keys_are_eligible(self):
        unknown_path = CITY_MATERIAL.replace("Resources/", "TeamMaterials/")
        self.assertIsNone(build_preflight.material_space_replacement(unknown_path, MATERIAL, serializer_spaces(MATERIAL)))
        for working in (
            serializer_spaces(MATERIAL).replace(b"CityDetailMaterial\n", b"CityDetailMaterial \n"),
            MATERIAL.replace(b"m_LockedProperties:\n", b"m_LockedProperties:\t\n"),
            serializer_spaces(MATERIAL).replace(b"m_CustomRenderQueue: -1\n", b"m_CustomRenderQueue: -1 \n"),
        ):
            with self.subTest(working=working):
                self.assertIsNone(build_preflight.material_space_replacement(CITY_MATERIAL, MATERIAL, working))

    def test_ambiguous_material_keys_or_noncanonical_head_are_not_normalized(self):
        working = serializer_spaces(WINDOW, name=True)
        for edited in (
            working + b"  m_LockedProperties: \n",
            working.replace(b"  m_LockedProperties: \n", b""),
            working.replace(b"  m_Name: WindowFrameMaterial \n", b""),
        ):
            with self.subTest(working=edited):
                self.assertIsNone(build_preflight.material_space_replacement(WINDOW_MATERIAL, WINDOW, edited))
        self.assertIsNone(build_preflight.material_space_replacement(WINDOW_MATERIAL, serializer_spaces(WINDOW), working))

    def test_materials_are_checked_even_when_settings_guid_is_unchanged(self):
        temporary_root = build_preflight.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            settings = root / build_preflight.BUILD_SETTINGS
            settings.parent.mkdir(parents=True)
            settings.write_bytes(COMMITTED)
            city = root / CITY_MATERIAL
            city.parent.mkdir(parents=True)
            city.write_bytes(serializer_spaces(MATERIAL))
            window = root / WINDOW_MATERIAL
            authored = serializer_spaces(WINDOW, name=True).replace(b"r: 1", b"r: 0.5")
            window.write_bytes(authored)
            other = city.parent / "TeamMaterial.mat"
            other.write_bytes(serializer_spaces(MATERIAL))
            head = {build_preflight.BUILD_SETTINGS: COMMITTED, CITY_MATERIAL: MATERIAL, WINDOW_MATERIAL: WINDOW}

            def git_show(arguments, **options):
                self.assertEqual({"check": True, "capture_output": True}, options)
                return SimpleNamespace(stdout=head[arguments[-1].removeprefix("HEAD:")])

            with mock.patch.object(build_preflight.subprocess, "run", side_effect=git_show) as git:
                self.assertTrue(build_preflight.run_preflight(root))
                self.assertFalse(build_preflight.run_preflight(root))
            self.assertEqual(6, git.call_count)
            self.assertEqual(MATERIAL, city.read_bytes())
            self.assertEqual(authored, window.read_bytes(), "A real material edit must remain dirty, including its spaces.")
            self.assertEqual(serializer_spaces(MATERIAL), other.read_bytes())
            self.assertEqual(COMMITTED, settings.read_bytes())

    def test_untracked_known_material_is_left_untouched(self):
        temporary_root = build_preflight.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            settings = root / build_preflight.BUILD_SETTINGS
            settings.parent.mkdir(parents=True)
            settings.write_bytes(COMMITTED)
            city = root / CITY_MATERIAL
            city.parent.mkdir(parents=True)
            working = serializer_spaces(MATERIAL)
            city.write_bytes(working)
            missing_head = subprocess.CalledProcessError(128, ["git", "show", "HEAD:" + CITY_MATERIAL])
            with mock.patch.object(build_preflight.subprocess, "run", side_effect=[SimpleNamespace(stdout=COMMITTED), missing_head]):
                self.assertFalse(build_preflight.run_preflight(root))
            self.assertEqual(working, city.read_bytes())


if __name__ == "__main__":
    unittest.main()
