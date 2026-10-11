"""Opt-in, read-only GLB selection and immutable staging. Never changes CityCatalog.

This is an import contract/preflight, not a replacement renderer or a glTF validator.
Run with --help. Only explicitly listed, hash-pinned files can enter a staged package.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path, PurePosixPath
import re
import shutil
import struct
import stat
import tempfile
from urllib.parse import urlsplit, unquote

from source_assets import safe_relative, sha256_file

SCHEMA = 1
MAX_JSON = 64 * 1024 * 1024
MAX_FILES = 4096
MAX_SELECTIONS = 4096
DIGEST = re.compile(r"[0-9a-f]{64}\Z")
TOKEN = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\Z")
SOURCE_ID = re.compile(r"[0-9]+_[0-9]+\Z")
COORDINATES = {
    "crs": "EPSG:32618", "units": "metres",
    "gltfAxes": "x=east,y=up,z=-north",
    "originEN": [586912.663584656, 4514845.724134378],
    "groundPlacement": "flat-base-zero", "importHandednessConversions": 1,
}
OPTICAL = {"KHR_materials_transmission", "KHR_materials_ior",
           "KHR_materials_volume", "KHR_materials_dispersion",
           "KHR_materials_specular", "KHR_materials_clearcoat",
           "KHR_materials_sheen", "KHR_materials_iridescence",
           "KHR_materials_anisotropy"}


def canonical(data):
    return json.dumps(data, sort_keys=True, separators=(",", ":"),
                      ensure_ascii=False, allow_nan=False).encode("utf-8")


def digest(data):
    return hashlib.sha256(canonical(data)).hexdigest()


def strict_json(data):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError("Duplicate JSON key: " + key)
            result[key] = value
        return result
    def invalid(value):
        raise ValueError("Non-finite JSON number: " + value)
    return json.loads(data, object_pairs_hook=pairs, parse_constant=invalid)


def exact_keys(value, required, optional=()):
    if not isinstance(value, dict) or set(value) - set(required) - set(optional) or set(required) - set(value):
        raise ValueError("Unexpected or missing contract fields; expected " + ", ".join(required))


def plain_path(value):
    if not isinstance(value, str) or value.endswith("/") or any(ord(c) < 32 for c in value):
        raise ValueError("Invalid file path")
    path = safe_relative(value)
    if path.as_posix() != value:
        raise ValueError("Noncanonical file path: " + value)
    return path


def redirected_path(path):
    """Reject Windows junctions and other reparse points as well as POSIX symlinks."""
    if path.is_symlink():
        return True
    try:
        return bool(getattr(path.lstat(), "st_file_attributes", 0) & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0))
    except FileNotFoundError:
        return False


def local_file(root, relative):
    """No symlinks, including parent components. Paths stay in the trusted root."""
    root = Path(root).absolute()
    for parent in [root, *root.parents]:
        if redirected_path(parent):
            raise ValueError("Symlink source/staging root is not supported: " + str(parent))
    current = root
    for part in plain_path(relative).parts:
        current = current / part
        if redirected_path(current):
            raise ValueError("Symlink payload is not supported: " + relative)
    if not current.is_file():
        raise ValueError("Missing payload: " + relative)
    return current


def external_path(model_path, uri):
    """Allow ../shared_textures inside the package, never a URL or path escape."""
    if not isinstance(uri, str) or not uri or unquote(uri) != uri or "\\" in uri or "\x00" in uri:
        raise ValueError("Unsafe/encoded external URI: " + repr(uri))
    parsed = urlsplit(uri)
    if parsed.scheme or parsed.netloc or parsed.query or parsed.fragment or uri.startswith("/"):
        raise ValueError("External URI must be a local relative path: " + uri)
    parts = list(PurePosixPath(model_path).parent.parts)
    for part in uri.split("/"):
        if part == "..":
            if not parts:
                raise ValueError("External URI escapes package: " + uri)
            parts.pop()
        elif part in ("", "."):
            raise ValueError("Noncanonical external URI: " + uri)
        else:
            parts.append(part)
    return plain_path("/".join(parts)).as_posix()


def read_glb(path):
    with Path(path).open("rb") as stream:
        header = stream.read(20)
        if len(header) != 20:
            raise ValueError("Truncated GLB header")
        magic, version, length, size, kind = struct.unpack("<IIIII", header)
        if magic != 0x46546C67 or version != 2 or length != Path(path).stat().st_size or kind != 0x4E4F534A:
            raise ValueError("Invalid GLB 2 header/length/JSON chunk")
        if size > MAX_JSON or size % 4 or size > length - 20:
            raise ValueError("Invalid or oversized GLB JSON chunk")
        raw = stream.read(size)
        if len(raw) != size:
            raise ValueError("Truncated GLB JSON")
        data = strict_json(raw.decode("utf-8"))
        if data.get("asset", {}).get("version") != "2.0":
            raise ValueError("Expected glTF 2.0")
        # Walk bounded chunk headers without loading a multi-gigabyte geometry payload.
        while stream.tell() < length:
            chunk = stream.read(8)
            if len(chunk) != 8:
                raise ValueError("Truncated GLB chunk")
            n, tag = struct.unpack("<II", chunk)
            if n % 4 or n > length - stream.tell() or tag != 0x004E4942:
                raise ValueError("Invalid GLB binary chunk")
            stream.seek(n, 1)
    return data


def graph(data):
    nodes = data.get("nodes", [])
    if not isinstance(nodes, list) or len(nodes) > 1000000:
        raise ValueError("Invalid node graph")
    parent = {}
    for index, node in enumerate(nodes):
        for child in node.get("children", []):
            if type(child) is not int or child < 0 or child >= len(nodes) or child in parent:
                raise ValueError("Invalid/multiply-parented node")
            parent[child] = index
    for index in range(len(nodes)):
        seen = set()
        cursor = index
        while cursor in parent:
            if cursor in seen:
                raise ValueError("Cyclic node graph")
            seen.add(cursor)
            cursor = parent[cursor]
    scene = data.get("scene", 0)
    scenes = data.get("scenes", [])
    if type(scene) is not int or not 0 <= scene < len(scenes):
        raise ValueError("Missing/default scene is invalid")
    roots = scenes[scene].get("nodes", [])
    if not roots or len(set(roots)) != len(roots):
        raise ValueError("Invalid scene roots")
    for root in roots:
        if type(root) is not int or not 0 <= root < len(nodes) or root in parent:
            raise ValueError("Invalid scene root")
    return nodes, parent, roots


def node_transform(node):
    if "matrix" in node and any(k in node for k in ("translation", "rotation", "scale")):
        raise ValueError("Node mixes matrix and TRS")
    values = {k: node[k] for k in ("matrix", "translation", "rotation", "scale") if k in node}
    for key, value in values.items():
        count = {"matrix": 16, "translation": 3, "rotation": 4, "scale": 3}[key]
        if not isinstance(value, list) or len(value) != count or any(type(x) not in (float, int) or not math.isfinite(x) for x in value):
            raise ValueError("Invalid node transform: " + key)
    return values


def at_index(items, index, label):
    if not isinstance(items, list) or type(index) is not int or not 0 <= index < len(items):
        raise ValueError("Invalid glTF " + label + " index")
    return items[index]


def texture_identity(data, info, model_path, files):
    if info is None:
        return None
    texture = at_index(data.get("textures"), info["index"], "texture")
    image = at_index(data.get("images"), texture["source"], "image")
    image_ref = {"mimeType": image.get("mimeType")}
    if "uri" in image:
        if image["uri"].startswith("data:"):
            image_ref["embeddedDataSha256"] = hashlib.sha256(image["uri"].encode()).hexdigest()
        else:
            path = external_path(model_path, image["uri"])
            image_ref.update(path=path, sha256=files[path]["sha256"])
    elif "bufferView" in image:
        image_ref.update(modelSha256=files[model_path]["sha256"], bufferView=image["bufferView"])
    else:
        raise ValueError("Texture image has no payload")
    return {"binding": info, "image": image_ref,
            "textureExtensions": texture.get("extensions", {}),
            "sampler": at_index(data.get("samplers"), texture["sampler"], "sampler") if "sampler" in texture else {}}


def material_identity(data, index, model_path, files):
    raw = at_index(data.get("materials"), index, "material") if index is not None else {}
    pbr = raw.get("pbrMetallicRoughness", {})
    ext = raw.get("extensions", {})
    strength = ext.get("KHR_materials_emissive_strength", {}).get("emissiveStrength", 1)
    emission = raw.get("emissiveFactor", [0, 0, 0])
    resolved = {
        # Conservative provenance prevents optical-extension texture indices from
        # aliasing across different GLBs, including extension slots not converted here.
        "sourcePayloadSha256": files[model_path]["sha256"],
        "externalPayloads": sorted((external_path(model_path, item["uri"]),
                                     files[external_path(model_path, item["uri"])]["sha256"])
                                    for item in [*data.get("images", []), *data.get("buffers", [])]
                                    if "uri" in item and not item["uri"].startswith("data:")),
        "baseColorFactor": pbr.get("baseColorFactor", [1, 1, 1, 1]),
        "metallicFactor": pbr.get("metallicFactor", 1),
        "roughnessFactor": pbr.get("roughnessFactor", 1),
        "emissiveFactor": emission, "emissiveStrength": strength,
        "resolvedEmissiveFactor": [x * strength for x in emission],
        "alphaMode": raw.get("alphaMode", "OPAQUE"), "alphaCutoff": raw.get("alphaCutoff", .5),
        "doubleSided": raw.get("doubleSided", False),
        "extensions": ext,
        "rawMaterial": {k: v for k, v in raw.items() if k != "name"},
        "textures": {key: texture_identity(data, owner.get(key), model_path, files)
                     for owner, key in [(pbr, "baseColorTexture"), (pbr, "metallicRoughnessTexture"),
                                        (raw, "normalTexture"), (raw, "occlusionTexture"), (raw, "emissiveTexture")]},
    }
    unsupported = sorted(set(ext))  # No extension converter is supplied by this foundation.
    diagnostics = ["UNSUPPORTED_MATERIAL_EXTENSION:" + name for name in unsupported]
    if any(emission) or "emissiveTexture" in raw:
        diagnostics.append("LEGACY_BAKE_DROPS_EMISSION")
    if set(ext) & OPTICAL:
        diagnostics.append("OPTICAL_PROFILE_REQUIRED")
    return {"key": digest(resolved), "index": index, "name": raw.get("name", ""),
            "resolved": resolved, "diagnostics": diagnostics}


def validate_manifest(manifest, expected_sha256, source_root):
    raw = Path(manifest).read_bytes()
    if not DIGEST.fullmatch(expected_sha256 or "") or hashlib.sha256(raw).hexdigest() != expected_sha256:
        raise ValueError("Manifest SHA-256 mismatch; an externally pinned hash is required")
    data = strict_json(raw)
    exact_keys(data, ("schemaVersion", "datasetId", "version", "coordinateContract", "legacySourceVersion", "files", "selections"))
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != SCHEMA:
        raise ValueError("Unsupported import schema")
    for key in ("datasetId", "version", "legacySourceVersion"):
        if not isinstance(data[key], str) or not TOKEN.fullmatch(data[key]):
            raise ValueError("Invalid immutable identifier: " + key)
    if canonical(data["coordinateContract"]) != canonical(COORDINATES):
        raise ValueError("Unsupported coordinate contract; preserve transforms and convert handedness exactly once")
    if not isinstance(data["files"], list) or not 0 < len(data["files"]) <= MAX_FILES:
        raise ValueError("File count outside bounded contract")
    files, aliases = {}, set()
    for entry in data["files"]:
        exact_keys(entry, ("path", "sha256", "bytes", "kind"))
        path = plain_path(entry["path"]).as_posix()
        if path.casefold() in aliases:
            raise ValueError("Duplicate/case-aliased payload: " + path)
        aliases.add(path.casefold())
        if not DIGEST.fullmatch(entry["sha256"]) or type(entry["bytes"]) is not int or entry["bytes"] < 0 or entry["kind"] not in ("glb", "dependency"):
            raise ValueError("Invalid payload identity: " + path)
        actual = local_file(source_root, path)
        if actual.stat().st_size != entry["bytes"] or sha256_file(actual) != entry["sha256"]:
            raise ValueError("Payload SHA-256/size mismatch: " + path)
        files[path] = entry
    selections = data["selections"]
    if not isinstance(selections, list) or not 0 < len(selections) <= MAX_SELECTIONS:
        raise ValueError("Selection count outside bounded contract")
    ids, models, claimed, selected_files, used_dependencies, output = set(), {}, {}, set(), set(), []
    for selection in sorted(selections, key=lambda x: x["sourceId"]):
        exact_keys(selection, ("sourceId", "file", "rootNode", "rootName", "semantics", "contentProfile", "replacesSourceId", "suppressLegacyTrim"))
        source_id = selection["sourceId"]
        if not isinstance(source_id, str) or not SOURCE_ID.fullmatch(source_id) or source_id in ids:
            raise ValueError("Invalid/duplicate selected source ID: " + str(source_id))
        ids.add(source_id)
        if not TOKEN.fullmatch(selection["contentProfile"]):
            raise ValueError("Invalid content profile")
        semantics = selection["semantics"]
        if semantics == "replace-complete-body":
            if selection["replacesSourceId"] != source_id or selection["suppressLegacyTrim"] is not True:
                raise ValueError("Complete-body replacement must explicitly suppress same-ID body and trim")
        elif semantics == "legacy-additive":
            if selection["replacesSourceId"] is not None or selection["suppressLegacyTrim"] is not False:
                raise ValueError("Legacy additive content cannot claim replacements")
        else:
            raise ValueError("Unsupported representation semantics")
        path = selection["file"]
        if path not in files or files[path]["kind"] != "glb" or not path.endswith(".glb"):
            raise ValueError("Selection must name an allowlisted GLB")
        selected_files.add(path)
        if path not in models:
            gltf = read_glb(local_file(source_root, path))
            for block in [*gltf.get("images", []), *gltf.get("buffers", [])]:
                if "uri" not in block or block["uri"].startswith("data:"):
                    continue
                dependency = external_path(path, block["uri"])
                if dependency not in files or files[dependency]["kind"] != "dependency":
                    raise ValueError("Unpinned external dependency: " + dependency)
                used_dependencies.add(dependency)
            models[path] = (gltf, *graph(gltf))
        gltf, nodes, parent, scene_roots = models[path]
        root = selection["rootNode"]
        if type(root) is not int or not 0 <= root < len(nodes):
            raise ValueError("Invalid selected root index")
        node = nodes[root]
        if node.get("name") != selection["rootName"] or str(node.get("extras", {}).get("source_building_id", "")) != source_id:
            raise ValueError("Selected root/source-ID metadata mismatch: " + source_id)
        chain = [root]
        while chain[-1] in parent:
            chain.append(parent[chain[-1]])
        if chain[-1] not in scene_roots:
            raise ValueError("Selected root is outside active scene")
        chain.reverse()
        for index in chain:
            owner = nodes[index].get("extras", {}).get("source_building_id")
            if owner is not None and str(owner) != source_id:
                raise ValueError("Selected root has a foreign source-ID ancestor: " + source_id)
        descendants, pending, material_ids = [], [root], set()
        while pending:
            index = pending.pop()
            if (path, index) in claimed:
                raise ValueError("Selected roots overlap: " + source_id)
            claimed[path, index] = source_id
            descendants.append(index)
            descendant = nodes[index]
            owner = descendant.get("extras", {}).get("source_building_id")
            if owner is not None and str(owner) != source_id:
                raise ValueError("Selected subtree contains a foreign source ID: " + source_id)
            node_transform(descendant)
            if "skin" in descendant or "weights" in descendant:
                raise ValueError("Skinned/morph source unsupported in static city contract")
            pending.extend(reversed(descendant.get("children", [])))
            if "mesh" in descendant:
                for primitive in at_index(gltf.get("meshes"), descendant["mesh"], "mesh")["primitives"]:
                    if primitive.get("mode", 4) != 4 or "targets" in primitive:
                        raise ValueError("Only static triangle primitives are supported")
                    # Validate before set insertion: Python False/True alias 0/1.
                    if "material" in primitive:
                        at_index(gltf.get("materials"), primitive["material"], "material")
                    material_ids.add(primitive.get("material"))
        if not material_ids:
            raise ValueError("Selected root has no triangle payload")
        materials = [material_identity(gltf, i, path, files) for i in sorted(material_ids, key=lambda i: -1 if i is None else i)]
        record = dict(selection)
        record.update(assetSha256=files[path]["sha256"],
                      ancestorChain=[dict(nodeIndex=i, name=nodes[i].get("name", ""), transform=node_transform(nodes[i])) for i in chain],
                      selectedNodes=descendants, sourceMetadata=node.get("extras", {}), materials=materials,
                      declaredExtensions=sorted(set(gltf.get("extensionsUsed", [])) | set(gltf.get("extensionsRequired", []))))
        record["identitySha256"] = digest(record)
        output.append(record)
    if selected_files != {p for p, f in files.items() if f["kind"] == "glb"}:
        raise ValueError("Manifest contains an unselected GLB alternative")
    if used_dependencies != {p for p, f in files.items() if f["kind"] == "dependency"}:
        raise ValueError("Manifest contains unused/unreferenced dependencies")
    blockers = [{"sourceId": r["sourceId"], "material": m["name"], "diagnostics": m["diagnostics"]}
                for r in output for m in r["materials"] if m["diagnostics"]]
    return dict(schemaVersion=SCHEMA, datasetId=data["datasetId"], version=data["version"],
                manifestSha256=expected_sha256, legacySourceVersion=data["legacySourceVersion"],
                coordinateContract=data["coordinateContract"], files=sorted(files.values(), key=lambda f: f["path"]),
                selections=output, materialBlockers=blockers, runtimeReady=False,
                runtimeBlockers=["NO_SOURCE_ID_SEGMENTED_LEGACY_BINDING", "NO_NATIVE_IMPORT_MATERIAL_ADAPTER", "NO_STREAMER_PUBLICATION_WIRING"])


def stage(manifest, expected_sha256, source_root, destination):
    plan = validate_manifest(manifest, expected_sha256, source_root)
    destination = Path(destination).absolute()
    # Check existing ancestors before creating any directory through a symlink.
    for path in [destination, *destination.parents]:
        if redirected_path(path):
            raise ValueError("Symlink staging destination is not supported")
    target = destination / plan["datasetId"] / plan["version"] / expected_sha256
    for path in [target, *target.parents]:
        if redirected_path(path):
            raise ValueError("Symlink staging path is not supported")
    plan_bytes = canonical(plan) + b"\n"
    if target.exists():
        actual = validate_manifest(local_file(target, "import-manifest.json"), expected_sha256, target / "payload")
        if canonical(actual) + b"\n" != plan_bytes or local_file(target, "import-plan.json").read_bytes() != plan_bytes:
            raise ValueError("Existing immutable stage differs; use a new version")
        return target, plan
    target.parent.mkdir(parents=True, exist_ok=True)
    scratch = Path(tempfile.mkdtemp(prefix=".staging-", dir=target.parent))
    try:
        for entry in plan["files"]:
            output = scratch / "payload" / entry["path"]
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(local_file(source_root, entry["path"]), output)
        (scratch / "import-manifest.json").write_bytes(Path(manifest).read_bytes())
        # Revalidate copied bytes, closing mutation between inspection and copy.
        copied = validate_manifest(scratch / "import-manifest.json", expected_sha256, scratch / "payload")
        if canonical(copied) + b"\n" != plan_bytes:
            raise ValueError("Source changed during staging")
        (scratch / "import-plan.json").write_bytes(plan_bytes)
        scratch.rename(target)  # All files become visible together; never overwrite a nonempty version.
    finally:
        if scratch.exists():
            shutil.rmtree(scratch)
    return target, plan


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("inspect", "stage"))
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--expected-sha256", required=True)
    parser.add_argument("--source-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="Inspect: report file; stage: destination directory")
    args = parser.parse_args(argv)
    try:
        if args.command == "stage":
            target, plan = stage(args.manifest, args.expected_sha256, args.source_root, args.output)
            print("Staged verified source package: " + str(target))
        else:
            plan = validate_manifest(args.manifest, args.expected_sha256, args.source_root)
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_bytes(canonical(plan) + b"\n")
        print(f"{len(plan['selections'])} selected source IDs; {len(plan['materialBlockers'])} material diagnostics; runtimeReady=false")
        return 0
    except (ValueError, KeyError, IndexError, TypeError, OSError) as error:
        parser.exit(2, "Import preflight failed: " + str(error) + "\n")


if __name__ == "__main__":
    main()
