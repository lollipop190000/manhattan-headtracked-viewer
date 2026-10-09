"""Restore known generated serialization differences before the CLI's dirty check.

Generated Addressables assets are ignored and receive new GUIDs in clean clones.
BuildAutomation rebinds them after recording build identity. Unity also appends
spaces to two generated material keys. Each repair requires every other byte to
match HEAD. Authored changes remain visible to Git and the Unity CLI.
"""

import argparse
from pathlib import Path
import re
import subprocess


ROOT = Path(__file__).resolve().parents[1]
BUILD_SETTINGS = "ManhattanViewer/ProjectSettings/EditorBuildSettings.asset"
GUID_LINE = re.compile(
    rb"(?m)^[ \t]*com\.unity\.addressableassets: "
    rb"\{fileID: 11400000, guid: (?P<guid>[0-9a-fA-F]{32}), type: 2\}"
    rb"[ \t]*(?:\r?\n|$)"
)
MATERIAL_SPACE_LINES = {
    "ManhattanViewer/Assets/Resources/CityDetailMaterial.mat": (
        re.compile(rb"(?m)^  m_LockedProperties:(?P<spaces> *)(?=\r?$)"),
    ),
    "ManhattanViewer/Assets/Resources/WindowFrameMaterial.mat": (
        re.compile(rb"(?m)^  m_LockedProperties:(?P<spaces> *)(?=\r?$)"),
        re.compile(rb"(?m)^  m_Name:(?P<spaces> *)(?=\r?$)"),
    ),
}


def canonical_text(data: bytes) -> bytes:
    # Match Git's text normalization without parsing or rewriting Unity YAML.
    return data.replace(b"\r\n", b"\n")


def generated_guid_replacement(committed: bytes, working: bytes) -> bytes | None:
    """Return a GUID-only replacement, or None when no safe change is possible."""
    committed_matches = list(GUID_LINE.finditer(committed))
    working_matches = list(GUID_LINE.finditer(working))
    if len(committed_matches) != 1 or len(working_matches) != 1:
        return None
    expected, current = committed_matches[0], working_matches[0]
    if expected.group("guid") == current.group("guid"):
        return None
    # Everything outside this line must match HEAD. Also require the line itself
    # to differ only in its GUID, so fileID/type/indent edits cannot be erased.
    committed_rest = committed[:expected.start()] + committed[expected.end():]
    working_rest = working[:current.start()] + working[current.end():]
    if canonical_text(committed_rest) != canonical_text(working_rest):
        return None
    replacement = working[:current.start("guid")] + expected.group("guid") + working[current.end("guid"):]
    if canonical_text(replacement) != canonical_text(committed):
        return None
    return replacement


def material_space_replacement(relative_path: str, committed: bytes, working: bytes) -> bytes | None:
    """Remove only known serializer spaces when the whole result matches HEAD."""
    patterns = MATERIAL_SPACE_LINES.get(relative_path)
    if patterns is None:
        return None
    replacement = working
    for pattern in patterns:
        expected = list(pattern.finditer(committed))
        current = list(pattern.finditer(replacement))
        if len(expected) != 1 or len(current) != 1 or expected[0].group("spaces"):
            return None
        match = current[0]
        replacement = replacement[:match.start("spaces")] + replacement[match.end("spaces"):]
    if replacement == working or canonical_text(replacement) != canonical_text(committed):
        return None
    return replacement


def run_preflight(repository: Path) -> bool:
    result = subprocess.run(
        ["git", "-C", str(repository), "show", "HEAD:" + BUILD_SETTINGS],
        check=True, capture_output=True,
    )
    path = repository / BUILD_SETTINGS
    if path.is_symlink():
        raise ValueError("Build settings must be a regular project file.")
    working = path.read_bytes()
    replacement = generated_guid_replacement(result.stdout, working)
    changed = replacement is not None
    if replacement is not None:
        path.write_bytes(replacement)
        print("Build preflight: restored only the generated Addressables GUID to HEAD.")
    for relative_path in MATERIAL_SPACE_LINES:
        path = repository / relative_path
        if path.is_symlink():
            raise ValueError("Generated material must be a regular project file.")
        if not path.is_file():
            continue
        try:
            result = subprocess.run(
                ["git", "-C", str(repository), "show", "HEAD:" + relative_path],
                check=True, capture_output=True,
            )
        except subprocess.CalledProcessError:
            # An untracked/generated-only material has no committed canonical
            # bytes. Leave it untouched rather than inventing a baseline.
            continue
        replacement = material_space_replacement(relative_path, result.stdout, path.read_bytes())
        if replacement is not None:
            path.write_bytes(replacement)
            changed = True
            print("Build preflight: restored only known serializer spaces in " + relative_path + ".")
    if not changed:
        print("Build preflight: no eligible generated-only change; Unity CLI dirty check remains enabled.")
    return changed


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=ROOT)
    args = parser.parse_args()
    run_preflight(args.repository_root)


if __name__ == "__main__":
    main()
