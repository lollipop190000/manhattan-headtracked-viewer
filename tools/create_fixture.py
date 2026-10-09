"""Reproduce a small exact mesh subset of the nearest canonical near tile."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import struct

from source_assets import ROOT, sha256_file


def read_glb(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from("<4sII", data)
    if magic != b"glTF" or version != 2 or length != len(data):
        raise ValueError("Invalid canonical GLB header")
    json_length, json_type = struct.unpack_from("<II", data, 12)
    if json_type != 0x4E4F534A:
        raise ValueError("GLB does not start with JSON")
    doc = json.loads(data[20:20 + json_length])
    binary_offset = 20 + json_length
    binary_length, binary_type = struct.unpack_from("<II", data, binary_offset)
    if binary_type != 0x004E4942 or binary_offset + 8 + binary_length != len(data):
        raise ValueError("Expected a single embedded binary chunk")
    return doc, data[binary_offset + 8:]


def create(source_root, fixture_root):
    manifest = json.loads((ROOT / "assets/source-manifest.json").read_text(encoding="utf-8"))
    logical = json.loads((source_root / "Manhattan_Unity_Core_and_Ground/tile_manifest.json").read_text(encoding="utf-8"))
    initial = [-1225.0, -3585.0, 1.85]
    nearest = min(logical["tiles"], key=lambda t: (t["centerBlender"][0] - initial[0]) ** 2 +
                  (t["centerBlender"][1] - initial[1]) ** 2)
    matches = [(pack, entry) for pack in manifest["packs"] if pack["asset"].startswith("Manhattan_Near_")
               for entry in pack["files"] if entry["path"] == nearest["file"]]
    if len(matches) != 1:
        raise ValueError("Nearest canonical near tile is not uniquely versioned")
    pack, entry = matches[0]
    source = source_root / pack["directory"] / entry["path"]
    if sha256_file(source) != entry["sha256"]:
        raise ValueError("Nearest source tile differs from the pinned release")
    doc, binary = read_glb(source)
    if doc.get("images") or doc.get("skins") or len(doc.get("buffers", [])) != 1:
        raise ValueError("Fixture extraction only supports this simple canonical no-texture detail tile")
    candidates = []
    for index, mesh in enumerate(doc["meshes"]):
        if len(mesh["primitives"]) != 1:
            continue
        primitive = mesh["primitives"][0]
        accessors = list(primitive["attributes"].values()) + [primitive["indices"]]
        bytes_used = sum(doc["bufferViews"][doc["accessors"][accessor]["bufferView"]]["byteLength"]
                         for accessor in accessors)
        candidates.append((bytes_used, index))
    _, selected = min(candidates)
    node = next(node for node in doc["nodes"] if node.get("mesh") == selected)
    if any(key in node for key in ("translation", "rotation", "scale", "matrix", "children", "skin")):
        raise ValueError("Fixture must retain source vertices at the shared origin without node transforms")
    mesh = copy.deepcopy(doc["meshes"][selected])
    primitive = mesh["primitives"][0]
    old_accessors = sorted(set(primitive["attributes"].values()) | {primitive["indices"]})
    old_views = sorted({doc["accessors"][index]["bufferView"] for index in old_accessors})
    accessor_map = {old: new for new, old in enumerate(old_accessors)}
    view_map = {old: new for new, old in enumerate(old_views)}
    views, compact_binary = [], bytearray()
    for index in old_views:
        view = copy.deepcopy(doc["bufferViews"][index])
        offset = view.get("byteOffset", 0)
        payload = binary[offset:offset + view["byteLength"]]
        compact_binary.extend(b"\0" * (-len(compact_binary) % 4))
        view["byteOffset"] = len(compact_binary)
        view["buffer"] = 0
        compact_binary.extend(payload)
        views.append(view)
    accessors = [copy.deepcopy(doc["accessors"][index]) for index in old_accessors]
    for accessor in accessors:
        if "sparse" in accessor:
            raise ValueError("Sparse fixture accessors are not supported")
        accessor["bufferView"] = view_map[accessor["bufferView"]]
    material = copy.deepcopy(doc["materials"][primitive["material"]])
    primitive["attributes"] = {name: accessor_map[index] for name, index in primitive["attributes"].items()}
    primitive["indices"] = accessor_map[primitive["indices"]]
    primitive["material"] = 0
    fixture = dict(asset=copy.deepcopy(doc["asset"]), scene=0, scenes=[dict(name="Nearest detail validation", nodes=[0])],
                   nodes=[dict(mesh=0, name=node.get("name", "Nearest detail"))], meshes=[mesh], materials=[material],
                   accessors=accessors, bufferViews=views, buffers=[dict(byteLength=len(compact_binary))])
    for key in ("extensionsUsed", "extensionsRequired"):
        if key in doc:
            fixture[key] = doc[key]
    encoded_json = json.dumps(fixture, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    encoded_json += b" " * (-len(encoded_json) % 4)
    compact_binary.extend(b"\0" * (-len(compact_binary) % 4))
    content = (struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(encoded_json) + 8 + len(compact_binary)) +
               struct.pack("<II", len(encoded_json), 0x4E4F534A) + encoded_json +
               struct.pack("<II", len(compact_binary), 0x004E4942) + compact_binary)
    if len(content) > 10_000_000:
        raise ValueError("Validation fixture exceeds the 10MB budget")
    position = accessors[primitive["attributes"]["POSITION"]]
    gltf_min, gltf_max = position["min"], position["max"]
    fixture_root.mkdir(parents=True, exist_ok=True)
    filename = "NearestNearDetail.glb"
    (fixture_root / filename).write_bytes(content)
    metadata = dict(schemaVersion=1, sourceVersion=manifest["sourceVersion"], sourcePack=pack["asset"],
                    sourcePath=entry["path"], sourceSha256=entry["sha256"], sourceMeshIndex=selected,
                    sourceMeshName=doc["meshes"][selected].get("name"), file=filename, bytes=len(content),
                    sha256=hashlib.sha256(content).hexdigest(), positions=position["count"],
                    triangles=accessors[primitive["indices"]]["count"] // 3,
                    gltfBounds=dict(min=gltf_min, max=gltf_max),
                    unityBounds=dict(min=[-gltf_max[0], gltf_min[1], gltf_min[2]],
                                     max=[-gltf_min[0], gltf_max[1], gltf_max[2]]),
                    initialBlender=initial, initialUnity=[-initial[0], initial[2], -initial[1]],
                    conversion="Blender(east,north,up) -> glTF(east,up,-north) -> Unity(-east,up,-north)",
                    subset="Exact smallest independent material mesh from the nearest tile; vertex/index bytes and material retained; no recentering or scaling")
    (fixture_root / "fixture-manifest.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(f"Reproduced {filename}: {len(content)} bytes, {metadata['triangles']} triangles, {metadata['sourceMeshName']}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT / "3d_modeling")
    parser.add_argument("--output", type=Path, default=ROOT / "assets/fixtures")
    args = parser.parse_args()
    create(args.source_root, args.output)
