"""Restore only a regenerated Addressables GUID before the CLI's dirty check.

Generated Addressables assets are ignored and receive new GUIDs in clean clones.
BuildAutomation rebinds them after recording build identity. All authored changes
remain visible to Git and the Unity CLI; this does not bypass the dirty check.
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
    if replacement is None:
        print("Build preflight: no eligible GUID-only change; Unity CLI dirty check remains enabled.")
        return False
    path.write_bytes(replacement)
    print("Build preflight: restored only the generated Addressables GUID to HEAD.")
    return True


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository-root", type=Path, default=ROOT)
    args = parser.parse_args()
    run_preflight(args.repository_root)


if __name__ == "__main__":
    main()
