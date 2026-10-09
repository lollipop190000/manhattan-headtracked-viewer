"""Version, validate, and safely extract the private Manhattan source release.

Only Python's standard library is required. Archive hashes protect transport;
per-file hashes make the exact Unity bake inputs reproducible as well.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "assets" / "source-manifest.json"
ARCHIVES = (
    "Manhattan_Unity_Core_and_Ground.zip",
    "Manhattan_Near_Downtown.zip",
    "Manhattan_Near_Midtown.zip",
    "Manhattan_Near_Uptown.zip",
    "Manhattan_Optional_Vegetation.zip",
    "Manhattan_Source_and_Materials.zip",
)
DIGEST = re.compile(r"^[a-f0-9]{64}$")
RESERVED = re.compile(r"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", re.I)
REQUIRED_NOTICE_INPUTS = {
    "Manhattan_Unity_Core_and_Ground.zip": {"PROVENANCE.md", "NYC_Open_Data_Terms.html"},
    "Manhattan_Source_and_Materials.zip": {"data/PROVENANCE.md"},
}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_relative(name: str) -> PurePosixPath:
    """Reject both POSIX and Windows escape/alias names, on any host OS."""
    if not name or "\\" in name or "\x00" in name or name.startswith("/"):
        raise ValueError(f"Unsafe archive path: {name!r}")
    raw_parts = name.rstrip("/").split("/")
    if any(part in ("", ".", "..") or ":" in part or part.rstrip(" .") != part
           or RESERVED.match(part) for part in raw_parts):
        raise ValueError(f"Unsafe archive path: {name!r}")
    return PurePosixPath(*raw_parts)


def archive_members(archive: zipfile.ZipFile) -> list[zipfile.ZipInfo]:
    members = []
    names = set()
    for item in archive.infolist():
        path = safe_relative(item.filename)
        mode = item.external_attr >> 16
        if stat.S_ISLNK(mode):
            raise ValueError(f"Archive symlinks are not supported: {item.filename}")
        if item.flag_bits & 1:
            raise ValueError(f"Encrypted source archive: {item.filename}")
        if item.is_dir():
            continue
        key = path.as_posix().casefold()
        if key in names:
            raise ValueError(f"Duplicate/case-aliased archive path: {item.filename}")
        names.add(key)
        members.append(item)
    return members


def digest_member(archive: zipfile.ZipFile, item: zipfile.ZipInfo) -> str:
    digest = hashlib.sha256()
    with archive.open(item) as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def make_manifest(archive_root: Path, source_version: str, release_tag: str) -> dict:
    packs = []
    for name in ARCHIVES:
        path = archive_root / name
        with zipfile.ZipFile(path) as archive:
            files = [dict(path=item.filename, bytes=item.file_size,
                          sha256=digest_member(archive, item))
                     for item in sorted(archive_members(archive), key=lambda i: i.filename)]
        packs.append(dict(asset=name, directory=path.stem, bytes=path.stat().st_size,
                          sha256=sha256_file(path), expandedBytes=sum(f["bytes"] for f in files),
                          files=files))
    return dict(schemaVersion=1, sourceVersion=source_version,
                releaseRepository="lollipop190000/manhattan-headtracked-viewer",
                releaseTag=release_tag, sourceCRS="EPSG:32618",
                sourceOriginLongitudeLatitude=[-73.97, 40.78],
                sourceAxes="Blender X east Y north Z up; metres; GLB Y-up",
                packs=packs)


def validate_manifest(data: dict) -> None:
    if data.get("schemaVersion") != 1 or not data.get("sourceVersion"):
        raise ValueError("Unsupported or incomplete source manifest")
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", data.get("releaseRepository", "")):
        raise ValueError("Invalid release repository")
    if not re.fullmatch(r"[\w.-]+", data.get("releaseTag", "")):
        raise ValueError("Invalid release tag")
    packs = data.get("packs", [])
    if len(packs) != len(ARCHIVES) or {p.get("asset") for p in packs} != set(ARCHIVES):
        raise ValueError("Manifest must identify exactly the six versioned source packs")
    for pack in packs:
        if pack["directory"] != Path(pack["asset"]).stem:
            raise ValueError("Pack destination must match its archive stem")
        if not DIGEST.fullmatch(pack.get("sha256", "")) or pack.get("bytes", 0) <= 0:
            raise ValueError(f"Invalid archive fingerprint: {pack['asset']}")
        names = set()
        files = pack.get("files", [])
        if not files:
            raise ValueError("A source pack cannot be empty")
        for entry in files:
            key = safe_relative(entry["path"]).as_posix().casefold()
            if key in names:
                raise ValueError(f"Duplicate manifest path: {entry['path']}")
            names.add(key)
            if not DIGEST.fullmatch(entry.get("sha256", "")) or entry.get("bytes", -1) < 0:
                raise ValueError(f"Invalid file fingerprint: {entry['path']}")
        if pack.get("expandedBytes") != sum(f["bytes"] for f in files):
            raise ValueError(f"Invalid expanded byte total: {pack['asset']}")
        required_notices = REQUIRED_NOTICE_INPUTS.get(pack["asset"], set())
        if not required_notices.issubset({entry["path"] for entry in files}):
            raise ValueError(f"Source pack is missing original provenance or reuse terms: {pack['asset']}")


def verify_archive(path: Path, pack: dict, full: bool = False) -> None:
    if path.stat().st_size != pack["bytes"] or sha256_file(path) != pack["sha256"]:
        raise ValueError(f"Archive SHA-256/size differs from pinned source: {path.name}")
    with zipfile.ZipFile(path) as archive:
        actual = {i.filename: i for i in archive_members(archive)}
        expected = {f["path"]: f for f in pack["files"]}
        if actual.keys() != expected.keys():
            raise ValueError(f"Archive entries differ: {path.name}")
        for name, item in actual.items():
            entry = expected[name]
            if item.file_size != entry["bytes"] or (full and digest_member(archive, item) != entry["sha256"]):
                raise ValueError(f"Archive member differs: {name}")


def verify_extracted(destination: Path, pack: dict) -> None:
    if destination.is_symlink():
        raise ValueError(f"Source destination cannot be a symlink: {destination}")
    destination = destination.resolve()
    for entry in pack["files"]:
        target = destination.joinpath(*safe_relative(entry["path"]).parts)
        if not target.resolve().is_relative_to(destination):
            raise ValueError(f"Source path escapes pack: {entry['path']}")
        if target.is_symlink() or not target.is_file() or target.stat().st_size != entry["bytes"]:
            raise ValueError(f"Missing or changed source file: {target}")
        if sha256_file(target) != entry["sha256"]:
            raise ValueError(f"Changed source SHA-256: {target}")


def extract_pack(archive_path: Path, source_root: Path, pack: dict) -> None:
    verify_archive(archive_path, pack)
    source_root = source_root.resolve()
    source_root.mkdir(parents=True, exist_ok=True)
    destination = source_root / pack["directory"]
    if destination.exists() or destination.is_symlink():
        # Extra local files may coexist; versioned inputs themselves must be exact.
        verify_extracted(destination, pack)
        print(f"Already verified: {pack['directory']}")
        return
    staging = Path(tempfile.mkdtemp(prefix=".extract-", dir=source_root))
    try:
        with zipfile.ZipFile(archive_path) as archive:
            for item in archive_members(archive):
                target = staging.joinpath(*safe_relative(item.filename).parts)
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(item) as incoming, target.open("xb") as output:
                    shutil.copyfileobj(incoming, output, length=1024 * 1024)
        verify_extracted(staging, pack)
        # A concurrent setup must not silently replace another target.
        if destination.exists():
            raise ValueError(f"Destination appeared during extraction: {destination}")
        staging.rename(destination)
    finally:
        if staging.exists():
            if not staging.resolve().is_relative_to(source_root) or not staging.name.startswith(".extract-"):
                raise ValueError("Extraction cleanup target escaped its source root")
            shutil.rmtree(staging)
    print(f"Extracted and verified: {pack['directory']}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("make-manifest", "validate", "verify", "extract"))
    parser.add_argument("--manifest", type=Path, default=MANIFEST)
    parser.add_argument("--archive-root", type=Path, default=ROOT / ".cache" / "source-downloads")
    parser.add_argument("--source-root", type=Path, default=ROOT / "3d_modeling")
    parser.add_argument("--source-version", help="Explicit immutable source version when creating a manifest")
    parser.add_argument("--release-tag", help="Matching private release tag when creating a manifest")
    parser.add_argument("--archives", action="store_true", help="Verify ZIPs, including every uncompressed member")
    parser.add_argument("--extracted", action="store_true", help="Verify all extracted source inputs")
    args = parser.parse_args()
    if args.command == "make-manifest":
        if not args.source_version or not args.release_tag:
            parser.error("make-manifest requires --source-version and --release-tag")
        data = make_manifest(args.archive_root, args.source_version, args.release_tag)
        validate_manifest(data)
        args.manifest.parent.mkdir(parents=True, exist_ok=True)
        args.manifest.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"Pinned {len(data['packs'])} source packs: {args.manifest}")
        return
    data = json.loads(args.manifest.read_text(encoding="utf-8-sig"))
    validate_manifest(data)
    if args.command == "extract":
        for pack in data["packs"]:
            extract_pack(args.archive_root / pack["asset"], args.source_root, pack)
    elif args.command == "verify":
        if not (args.archives or args.extracted):
            parser.error("verify requires --archives and/or --extracted")
        for pack in data["packs"]:
            if args.archives:
                verify_archive(args.archive_root / pack["asset"], pack, full=True)
            if args.extracted:
                verify_extracted(args.source_root / pack["directory"], pack)
            print(f"Verified: {pack['asset']}")
    else:
        print(f"Manifest valid: {data['sourceVersion']}")


if __name__ == "__main__":
    main()
