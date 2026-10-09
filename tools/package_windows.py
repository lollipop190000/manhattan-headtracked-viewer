"""Package a clean Windows player using an allowlist, embedded identity and SHA-256 receipts."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import struct
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ROOT_FILES = ("ManhattanViewer.exe", "UnityPlayer.dll", "UnityCrashHandler64.exe")
ROOT_TREES = ("ManhattanViewer_Data", "MonoBleedingEdge", "D3D12", "Notices")
RECEIPT_NAME = "INTEGRITY.json"
REQUIRED_FILES = ROOT_FILES + (
    "ManhattanViewer_Data/boot.config", "ManhattanViewer_Data/globalgamemanagers",
    "ManhattanViewer_Data/resources.assets", "ManhattanViewer_Data/Managed/Manhattan.Viewer.Runtime.dll",
    "ManhattanViewer_Data/StreamingAssets/aa/settings.json", "ManhattanViewer_Data/StreamingAssets/aa/catalog.bin",
    "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll", "D3D12/D3D12Core.dll",
)
REQUIRED_NOTICES = (
    "README.txt", "manifest.json", "source-manifest.json", "technology-version.json",
    "Viewer/LICENSE", "Viewer/THIRD_PARTY.md", "HeadTrackedDisplay/LICENSE", "HeadTrackedDisplay/THIRD_PARTY.md",
    "CityDetailRenderer/LICENSE-Unity-URP.md", "CityDetailRenderer/NOTICE.md",
    "City/Manhattan_Unity_Core_and_Ground/NYC_Open_Data_Terms.html",
    "City/Manhattan_Unity_Core_and_Ground/PROVENANCE.md",
    "City/Manhattan_Source_and_Materials/data/PROVENANCE.md",
)
PRIVATE_NAMES = {
    "physical-calibration.json", "viewer-preferences.json", "position-bookmark.json", "calibration.json",
    "calibration.runtime.json", "tracker_runtime_calibration.json", "manhattanviewer.provenance.json", ".env",
}
ENVIRONMENT_DIRS = {".venv", "venv", "env", "__pycache__", "tracker", "tracker-env", "face-models", "models"}


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def checked_relative(value):
    """Reject names that can escape or alias destinations when extracted on Windows."""
    if not isinstance(value, str) or not value or "\\" in value:
        raise ValueError(f"Unsafe relative path: {value!r}")
    path = PurePosixPath(value)
    if path.is_absolute() or str(path) != value:
        raise ValueError(f"Unsafe relative path: {value!r}")
    for part in path.parts:
        stem = part.split(".")[0].upper()
        if (part in (".", "..") or part[-1] in (".", " ") or any(ord(c) < 32 or c in ':<>"|?*' for c in part)
                or stem in {"CON", "PRN", "AUX", "NUL"} or re.fullmatch(r"(?:COM|LPT)[1-9]", stem)):
            raise ValueError(f"Unsafe Windows relative path: {value!r}")
    return path


def reject_link(path):
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0):
        raise ValueError(f"Symlinks and reparse points cannot be shipped: {path}")


def excluded(relative):
    parts = tuple(part.casefold() for part in PurePosixPath(relative).parts)
    return (any(part.endswith("burstdebuginformation_donotship") or part in ENVIRONMENT_DIRS for part in parts)
            or parts[-1] in PRIVATE_NAMES or parts[-1].endswith((".log", ".task", ".heartbeat")))


def collect_files(build_directory):
    reject_link(build_directory)
    if not build_directory.is_dir():
        raise ValueError("Build directory does not exist")
    root = build_directory.resolve()
    files = {}
    aliases = set()

    def add(path):
        relative = path.relative_to(root).as_posix()
        checked_relative(relative)
        if excluded(relative):
            return
        alias = relative.casefold()
        if alias in aliases:
            raise ValueError(f"Case-colliding Windows path: {relative}")
        aliases.add(alias)
        files[relative] = path

    def visit(directory):
        for child in sorted(directory.iterdir(), key=lambda path: path.name):
            reject_link(child)
            relative = child.relative_to(root).as_posix()
            checked_relative(relative)
            if excluded(relative):
                continue
            if child.is_dir():
                if relative.casefold() in aliases:
                    raise ValueError(f"Case-colliding Windows path: {relative}")
                aliases.add(relative.casefold())
                visit(child)
            elif child.is_file():
                add(child)
            else:
                raise ValueError(f"Non-regular build entry: {relative}")

    for name in ROOT_FILES:
        path = root / name
        if path.exists():
            reject_link(path)
            if not path.is_file():
                raise ValueError(f"Required player file is not regular: {name}")
            add(path)
    for name in ROOT_TREES:
        path = root / name
        if path.exists():
            reject_link(path)
            if not path.is_dir():
                raise ValueError(f"Required player tree is not a directory: {name}")
            visit(path)
    for relative in REQUIRED_FILES + tuple("Notices/" + name for name in REQUIRED_NOTICES):
        if relative not in files or files[relative].stat().st_size == 0:
            raise ValueError(f"Required shipping file missing or empty: {relative}")
    if not any(name.startswith("ManhattanViewer_Data/StreamingAssets/aa/") and name.endswith(".bundle") for name in files):
        raise ValueError("Addressable city bundles are missing")
    return files


def no_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"), object_pairs_hook=no_duplicate_keys)


def embedded_identity(resources):
    """Read the named TextAsset's length-prefixed JSON in a Windows Unity serialized asset.

    This is deliberately a narrow TextAsset reader, not an arbitrary JSON/string search or
    a trust in the sidecar provenance file. The Windows payload is little-endian and 4-byte aligned.
    """
    payload = resources.read_bytes()
    name = b"ViewerBuildIdentity"
    found, offset = [], 0
    while True:
        offset = payload.find(name, offset)
        if offset < 0:
            break
        end = (offset + len(name) + 3) & ~3
        if offset >= 4 and end + 4 <= len(payload) and struct.unpack_from("<I", payload, offset - 4)[0] == len(name):
            size = struct.unpack_from("<I", payload, end)[0]
            if 0 < size <= 8192 and end + 4 + size <= len(payload):
                try:
                    identity = json.loads(payload[end + 4:end + 4 + size].decode("utf-8"), object_pairs_hook=no_duplicate_keys)
                    if isinstance(identity, dict) and "viewerRevision" in identity:
                        found.append(identity)
                except (UnicodeDecodeError, json.JSONDecodeError):
                    pass
        offset += len(name)
    if len(found) != 1:
        raise ValueError("Exactly one embedded ViewerBuildIdentity TextAsset is required")
    return found[0], hashlib.sha256(payload).hexdigest()


def validate_identity(identity, viewer_revision, technology_revision, source_hash):
    for name, expected in (("viewerRevision", viewer_revision), ("technologyRevision", technology_revision)):
        if not re.fullmatch(r"[0-9a-f]{40}", str(expected)) or identity.get(name) != expected:
            raise ValueError(f"Embedded {name} does not match expected full commit SHA")
    if identity.get("dirtyWorkingTree") is not False:
        raise ValueError("Embedded build identity is dirty or has no clean-state evidence")
    if identity.get("sourceManifestSha256") != source_hash:
        raise ValueError("Embedded source manifest SHA-256 does not match source inputs")
    try:
        built = datetime.fromisoformat(identity["buildUtc"].replace("Z", "+00:00"))
        if built.tzinfo is None or built.utcoffset() != timezone.utc.utcoffset(built):
            raise ValueError("buildUtc must include UTC timezone")
    except (KeyError, TypeError, AttributeError, ValueError) as error:
        raise ValueError("Embedded buildUtc is missing or invalid") from error


def validate_notices(files, source_manifest, technology_revision):
    index = read_json(files["Notices/manifest.json"])
    source = read_json(source_manifest)
    if index.get("sourceVersion") != source.get("sourceVersion") or index.get("technologyRevision") != technology_revision:
        raise ValueError("Notice index source/technology identity differs from build")
    if sha256_file(files["Notices/source-manifest.json"]) != sha256_file(source_manifest):
        raise ValueError("Bundled source manifest differs from verified source manifest")
    if read_json(files["Notices/technology-version.json"]).get("revision") != technology_revision:
        raise ValueError("Bundled technology version differs from build")
    records = index.get("files", [])
    if not records or not any(str(record.get("path", "")).startswith("UnityPackages/") for record in records):
        raise ValueError("Unity package license notices are missing")
    seen = set()
    for record in records:
        relative = str(checked_relative(record.get("path")))
        if relative.casefold() in seen:
            raise ValueError("Duplicate notice path")
        seen.add(relative.casefold())
        path = files.get("Notices/" + relative)
        if path is None or path.stat().st_size != record.get("bytes") or sha256_file(path) != record.get("sha256"):
            raise ValueError(f"Original notice bytes/hash mismatch: {relative}")
    for relative in REQUIRED_NOTICES:
        if relative not in ("README.txt", "manifest.json") and relative.casefold() not in seen:
            raise ValueError(f"Required notice is absent from notice index: {relative}")


def package_windows(build_directory, output, viewer_revision, technology_revision, source_manifest, readme, overwrite=False):
    files = collect_files(Path(build_directory))
    source_manifest, readme, output = Path(source_manifest), Path(readme), Path(output)
    for path in (source_manifest, readme):
        reject_link(path)
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"Required packaging input missing or empty: {path}")
    identity, resource_hash = embedded_identity(files["ManhattanViewer_Data/resources.assets"])
    validate_identity(identity, viewer_revision, technology_revision, sha256_file(source_manifest))
    validate_notices(files, source_manifest, technology_revision)
    files["README-Windows.md"] = readme
    records = [dict(path=name, bytes=path.stat().st_size, sha256=sha256_file(path)) for name, path in sorted(files.items())]
    if next(record["sha256"] for record in records if record["path"] == "ManhattanViewer_Data/resources.assets") != resource_hash:
        raise ValueError("Embedded identity file changed during validation")
    receipt = dict(schemaVersion=1, platform="Windows64", buildIdentity=identity,
                   fileCount=len(records), totalBytes=sum(record["bytes"] for record in records), files=records,
                   fileTreeSha256=hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest(),
                   fileTreeHashBasis="SHA-256 of UTF-8 json.dumps(files, sort_keys=True, separators=(',', ':'), ensure_ascii=True); files sorted by full relative path; INTEGRITY.json excluded.",
                   receiptSelfExcluded=RECEIPT_NAME)
    receipt_bytes = (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode("utf-8")
    output = output.parent.resolve() / output.name
    if output.suffix.lower() != ".zip" or output.is_relative_to(Path(build_directory).resolve()):
        raise ValueError("Output must be a .zip outside the build directory")
    output.parent.mkdir(parents=True, exist_ok=True)
    receipt_path = output.with_suffix(".integrity.json")
    checksum_path = output.with_name(output.name + ".sha256")
    for path in (output, receipt_path, checksum_path):
        if path.exists() or path.is_symlink():
            reject_link(path)
            if not path.is_file() or not overwrite:
                raise ValueError(f"Output already exists; use --overwrite: {path}")
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=output.parent, prefix=output.name + ".", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
            by_name = {record["path"]: record for record in records}
            for name in sorted(tuple(files) + (RECEIPT_NAME,)):
                info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0)); info.create_system = 0
                info.external_attr = 0x20; info.compress_type = zipfile.ZIP_DEFLATED
                if name == RECEIPT_NAME:
                    archive.writestr(info, receipt_bytes)
                    continue
                digest, size = hashlib.sha256(), 0
                with files[name].open("rb") as source, archive.open(info, "w", force_zip64=True) as destination:
                    for chunk in iter(lambda: source.read(1024 * 1024), b""):
                        destination.write(chunk); digest.update(chunk); size += len(chunk)
                if size != by_name[name]["bytes"] or digest.hexdigest() != by_name[name]["sha256"]:
                    raise ValueError(f"Input changed while packaging: {name}")
        zip_hash = sha256_file(temporary)
        os.replace(temporary, output); temporary = None
        receipt_path.write_bytes(receipt_bytes)
        checksum_path.write_text(zip_hash + "  " + output.name + "\n", encoding="utf-8")
        return dict(zip=str(output), zipBytes=output.stat().st_size, zipSha256=zip_hash,
                    integrityReceipt=str(receipt_path), checksum=str(checksum_path), fileCount=len(records), buildIdentity=identity)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-directory", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--viewer-revision", "--expected-viewer-sha", required=True)
    parser.add_argument("--technology-revision", "--expected-technology-sha")
    parser.add_argument("--source-manifest", type=Path, default=ROOT / "assets/source-manifest.json")
    parser.add_argument("--readme", type=Path, default=ROOT / "docs/windows-release.md")
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args()
    try:
        technology = args.technology_revision or read_json(ROOT / "tools/technology-version.json")["revision"]
        result = package_windows(args.build_directory, args.output, args.viewer_revision, technology, args.source_manifest, args.readme, args.overwrite)
    except (ValueError, OSError, KeyError) as error:
        parser.exit(1, f"Packaging failed: {error}\n")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
