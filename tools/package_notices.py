"""Copy source/component notices into a Windows build without changing originals."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

import source_assets

ROOT = Path(__file__).resolve().parents[1]
CITY_DETAIL_NOTICES = ("LICENSE-Unity-URP.md", "NOTICE.md")


def copy_notice(source, destination, expected=None):
    if source.is_symlink() or not source.is_file():
        raise ValueError(f"Required notice is missing or is a symlink: {source.name}")
    digest = source_assets.sha256_file(source)
    if expected and (source.stat().st_size != expected["bytes"] or digest != expected["sha256"]):
        raise ValueError(f"Original notice SHA-256/size changed: {source.name}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    if destination.stat().st_size != source.stat().st_size or source_assets.sha256_file(destination) != digest:
        raise ValueError(f"Copied notice SHA-256/size differs from original: {source.name}")
    return dict(path=destination.name, bytes=source.stat().st_size, sha256=digest)


def package_notices(build_directory, source_root, technology_root, package_cache):
    manifest = json.loads(source_assets.MANIFEST.read_text(encoding="utf-8"))
    source_assets.validate_manifest(manifest)
    version = json.loads((ROOT / "tools/technology-version.json").read_text(encoding="utf-8"))
    actual = subprocess.run(["git", "-C", str(technology_root), "rev-parse", "HEAD"],
                            check=True, capture_output=True, text=True).stdout.strip()
    if actual != version["revision"]:
        raise ValueError("Notice checkout does not match the tested technology revision")
    output = build_directory.resolve() / "Notices"
    output.mkdir(parents=True, exist_ok=True)
    records = []

    def add(source, relative, expected=None):
        record = copy_notice(source, output / relative, expected)
        record["path"] = relative.as_posix()
        records.append(record)

    add(ROOT / "LICENSE", Path("Viewer/LICENSE"))
    add(ROOT / "THIRD_PARTY.md", Path("Viewer/THIRD_PARTY.md"))
    for name in CITY_DETAIL_NOTICES:
        add(ROOT / "ManhattanViewer/Assets/Shaders" / name, Path("CityDetailRenderer") / name)
    add(source_assets.MANIFEST, Path("source-manifest.json"))
    add(ROOT / "tools/technology-version.json", Path("technology-version.json"))
    for pack in manifest["packs"]:
        for name in sorted(source_assets.REQUIRED_NOTICE_INPUTS.get(pack["asset"], set())):
            expected = next(entry for entry in pack["files"] if entry["path"] == name)
            add(source_root / pack["directory"] / name, Path("City") / pack["directory"] / name, expected)
    for name in ("LICENSE", "THIRD_PARTY.md"):
        add(technology_root / name, Path("HeadTrackedDisplay") / name)
    package_notice_count = 0
    if not package_cache.is_dir():
        raise ValueError("Unity PackageCache is missing; package notices cannot be collected")
    for package in sorted(package_cache.iterdir()):
        if not package.is_dir() or package.is_symlink():
            continue
        for notice in sorted(package.rglob("*")):
            name = notice.name.lower().replace("_", " ").replace("-", " ")
            if notice.is_file() and (name.startswith("license") or name.startswith("third party notice")):
                add(notice, Path("UnityPackages") / package.name / notice.relative_to(package))
                package_notice_count += 1
    if not package_notice_count:
        raise ValueError("No Unity package notices were found after the build")
    index = dict(schemaVersion=1, sourceVersion=manifest["sourceVersion"],
                 technologyRevision=version["revision"], files=records)
    (output / "manifest.json").write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
    (output / "README.txt").write_text(
        "The MIT license covers viewer/tracking code only. Original NYC source data, city derivatives, "
        "Unity packages, adapted shader sources and optional models retain their separate notices and terms.\n"
        "City/ preserves SHA-256-verified original provenance and NYC Open Data Terms. "
        "Viewer/THIRD_PARTY.md records credits and procedural-source limitations.\n"
        "CityDetailRenderer/ preserves the adapted URP shader's Unity license and source notice.\n"
        "Keep this Notices directory with redistributed Windows builds. The downloaded Face Landmarker "
        "task model is not bundled by this notice command.\n", encoding="utf-8")
    return index


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-directory", type=Path, required=True)
    parser.add_argument("--source-root", type=Path, default=ROOT / "3d_modeling")
    parser.add_argument("--technology-root", type=Path, default=ROOT / ".tools/head-tracked-display")
    parser.add_argument("--package-cache", type=Path, default=ROOT / "ManhattanViewer/Library/PackageCache")
    args = parser.parse_args()
    result = package_notices(args.build_directory, args.source_root, args.technology_root, args.package_cache)
    print(f"Packaged {len(result['files'])} notices for {result['sourceVersion']}")


if __name__ == "__main__":
    main()
