"""Portable repository checks used before pushes and on pull requests."""
import json
from pathlib import Path
import re
import subprocess

import source_assets

ROOT = Path(__file__).resolve().parents[1]


def main():
    manifest = json.loads(source_assets.MANIFEST.read_text(encoding="utf-8"))
    source_assets.validate_manifest(manifest)
    version = json.loads((ROOT / "tools/technology-version.json").read_text(encoding="utf-8"))
    if not re.fullmatch(r"[a-f0-9]{40}", version["revision"]):
        raise ValueError("Technology must be pinned to a full tested Git commit")
    packages = json.loads((ROOT / "ManhattanViewer/Packages/manifest.json").read_text(encoding="utf-8"))
    dependency = packages["dependencies"]["com.headtracked.display"]
    expected = f"{version['repository']}?path=/{version['packagePath'].lstrip('/')}#{version['revision']}"
    if dependency != expected:
        raise ValueError("Unity package and launcher/tracker checkout revisions differ")
    locks = json.loads((ROOT / "ManhattanViewer/Packages/packages-lock.json").read_text(encoding="utf-8"))
    locked = locks["dependencies"]["com.headtracked.display"]
    if locked.get("source") != "git" or locked.get("version") != expected or locked.get("hash") != version["revision"]:
        raise ValueError("Unity package lock must resolve the same tested Git commit")
    if any(value.startswith("file:") for value in packages["dependencies"].values()):
        raise ValueError("A shared project cannot depend on a local file package")
    editor = (ROOT / "ManhattanViewer/ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    if "m_EditorVersion: 6000.3.22f1" not in editor:
        raise ValueError("Unexpected Unity Editor version")
    result = subprocess.run(["git", "-C", str(ROOT), "ls-files", "-z"], check=True, capture_output=True)
    forbidden = re.compile(r"(^|/)(3d_modeling|need_to_change|Library|Temp|Obj|UserSettings|Builds|Logs|\.cache|\.tools|\.bootstrap|\.verification|\.venv|ManhattanGenerated|SourceModels|CitySource|AddressableAssetsData)(/|$)|Assets/Generated/City(/|$)|Assets/Resources/CityCatalog\.asset", re.I)
    for path in result.stdout.decode("utf-8").split("\0"):
        filename = Path(path).name.lower()
        personal_file = filename in {"physical-calibration.json", "viewer-preferences.json", "position-bookmark.json"} or filename.startswith(".env")
        if forbidden.search(path) or path.endswith("face_landmarker.task") or path.endswith("runtime_calibration.json") or personal_file:
            raise ValueError(f"Generated, private, or downloaded file was tracked: {path}")
    print("Repository pins, city source manifest, and tracked-file policy verified")


if __name__ == "__main__":
    main()
