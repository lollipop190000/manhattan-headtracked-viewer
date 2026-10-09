import hashlib
import json
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[2]


class CityFixtureTests(unittest.TestCase):
    def test_real_glb_header_geometry_and_single_coordinate_conversion(self):
        metadata = json.loads((ROOT / "assets/fixtures/fixture-manifest.json").read_text(encoding="utf-8"))
        source = json.loads((ROOT / "assets/source-manifest.json").read_text(encoding="utf-8"))
        pack = next(p for p in source["packs"] if p["asset"] == metadata["sourcePack"])
        entry = next(f for f in pack["files"] if f["path"] == metadata["sourcePath"])
        self.assertEqual(entry["sha256"], metadata["sourceSha256"])
        self.assertEqual(source["sourceVersion"], metadata["sourceVersion"])
        self.assertEqual("NearTiles/Near_-03_-08.glb", metadata["sourcePath"])
        data = (ROOT / "assets/fixtures" / metadata["file"]).read_bytes()
        self.assertLess(len(data), 10_000_000)
        self.assertEqual(metadata["bytes"], len(data))
        self.assertEqual(metadata["sha256"], hashlib.sha256(data).hexdigest())
        self.assertEqual((b"glTF", 2, len(data)), struct.unpack_from("<4sII", data))
        json_length, json_type = struct.unpack_from("<II", data, 12)
        self.assertEqual(0x4E4F534A, json_type)
        doc = json.loads(data[20:20 + json_length])
        binary_length, binary_type = struct.unpack_from("<II", data, 20 + json_length)
        self.assertEqual(0x004E4942, binary_type)
        binary = data[28 + json_length:]
        self.assertEqual(binary_length, len(binary))
        self.assertEqual([0], doc["scenes"][doc["scene"]]["nodes"])
        self.assertEqual(1, len(doc["nodes"]))
        self.assertFalse({"translation", "rotation", "scale", "matrix"} & doc["nodes"][0].keys())
        primitive = doc["meshes"][0]["primitives"][0]
        accessor = doc["accessors"][primitive["attributes"]["POSITION"]]
        self.assertEqual((5126, "VEC3", metadata["positions"]),
                         (accessor["componentType"], accessor["type"], accessor["count"]))
        view = doc["bufferViews"][accessor["bufferView"]]
        offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        stride = view.get("byteStride", 12)
        points = [struct.unpack_from("<fff", binary, offset + index * stride) for index in range(accessor["count"])]
        minimum = [min(point[axis] for point in points) for axis in range(3)]
        maximum = [max(point[axis] for point in points) for axis in range(3)]
        self.assertEqual(metadata["gltfBounds"]["min"], minimum)
        self.assertEqual(metadata["gltfBounds"]["max"], maximum)
        # This actual southwestern Manhattan source has east <0 and north <0.
        # GLB already reverses north into +Z; Unity reverses +X exactly once.
        self.assertLess(maximum[0], -900)
        self.assertGreater(minimum[2], 3400)
        unity_min = [-maximum[0], minimum[1], minimum[2]]
        unity_max = [-minimum[0], maximum[1], maximum[2]]
        self.assertEqual(metadata["unityBounds"]["min"], unity_min)
        self.assertEqual(metadata["unityBounds"]["max"], unity_max)
        self.assertEqual([1225, 1.85, 3585], metadata["initialUnity"])
        for axis in (0, 2):
            self.assertLessEqual(unity_min[axis], metadata["initialUnity"][axis])
            self.assertGreaterEqual(unity_max[axis], metadata["initialUnity"][axis])
        index_accessor = doc["accessors"][primitive["indices"]]
        index_view = doc["bufferViews"][index_accessor["bufferView"]]
        codes = {5121: "B", 5123: "H", 5125: "I"}
        code = codes[index_accessor["componentType"]]
        index_offset = index_view.get("byteOffset", 0) + index_accessor.get("byteOffset", 0)
        indices = struct.unpack_from("<" + code * index_accessor["count"], binary, index_offset)
        self.assertEqual(metadata["triangles"] * 3, len(indices))
        self.assertLess(max(indices), len(points))


if __name__ == "__main__":
    unittest.main()
