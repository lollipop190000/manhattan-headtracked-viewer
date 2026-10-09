"""Validate the baked catalog report without Unity or rendering dependencies."""
import argparse
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LAYERS = {"Skyline", "Core", "Ground", "Near", "Vegetation", "Collision"}


def vector(value, name, nonnegative=False):
    if not isinstance(value, dict) or set(value) != {"x", "y", "z"}:
        raise ValueError(f"Invalid {name} vector")
    for coordinate in value.values():
        if not isinstance(coordinate, (int, float)) or not math.isfinite(coordinate):
            raise ValueError(f"Non-finite {name}")
        if nonnegative and coordinate < 0:
            raise ValueError(f"Negative {name}")


def validate_catalog(report, source_version, require_full=False):
    if report.get("sourceVersion") != source_version:
        raise ValueError("Catalog source version differs from source manifest")
    tiles = report.get("tiles", [])
    if not tiles or report.get("tileCount") != len(tiles):
        raise ValueError("Catalog tile count is empty or inconsistent")
    if not isinstance(report.get("buildingCount"), int) or report["buildingCount"] <= 0:
        raise ValueError("Catalog has no building metadata")
    ids, addresses, layers = set(), set(), set()
    for tile in tiles:
        identifier, address = tile.get("id"), tile.get("address")
        if not identifier or identifier in ids or not address or address in addresses:
            raise ValueError("Duplicate or empty tile ID/address")
        ids.add(identifier)
        addresses.add(address)
        layer = tile.get("layer")
        if layer not in LAYERS:
            raise ValueError(f"Unknown city layer: {layer}")
        layers.add(layer)
        bounds = tile.get("cityBounds", {})
        vector(bounds.get("center"), "bounds center")
        vector(bounds.get("size"), "bounds size", nonnegative=True)
        vector(tile.get("cityOrigin"), "city origin")
        if all(coordinate == 0 for coordinate in bounds["size"].values()):
            raise ValueError("Tile bounds have no extent")
        for key in ("residentBytes", "triangles"):
            if not isinstance(tile.get(key), int) or tile[key] <= 0:
                raise ValueError(f"Tile has invalid {key}: {identifier}")
        if not isinstance(tile.get("replaces"), list):
            raise ValueError("Tile replacements must be an explicit list")
        if type(tile.get("requiredReplacementCount")) is not int or tile["requiredReplacementCount"] < 0:
            raise ValueError("Skyline replacement count must be an explicit nonnegative integer")
        if layer != "Skyline" and tile["requiredReplacementCount"] != 0:
            raise ValueError("Only skyline tiles can require replacement coverage")
        if layer in {"Near", "Ground", "Vegetation", "Collision", "Skyline"} and tile["replaces"]:
            raise ValueError("Additive layers cannot hide city bodies")
    by_id = {tile["id"]: tile for tile in tiles}
    incoming_core = {tile["id"]: 0 for tile in tiles if tile["layer"] == "Skyline"}
    for tile in tiles:
        if len(tile["replaces"]) != len(set(tile["replaces"])):
            raise ValueError("Duplicate skyline replacement")
        for replacement in tile["replaces"]:
            if replacement not in by_id or by_id[replacement]["layer"] != "Skyline":
                raise ValueError(f"Replacement must identify an existing skyline tile: {replacement}")
            incoming_core[replacement] += 1
    for identifier, expected in incoming_core.items():
        if by_id[identifier]["requiredReplacementCount"] != expected:
            raise ValueError(f"Skyline required replacement count differs from incoming Core coverage: {identifier}")
    if report.get("triangles") != sum(t["triangles"] for t in tiles):
        raise ValueError("Catalog triangle total differs from tile records")
    if require_full and layers != LAYERS:
        raise ValueError(f"Full catalog is missing required layers: {LAYERS - layers}")
    return dict(tiles=len(tiles), triangles=report["triangles"], buildings=report["buildingCount"], layers=sorted(layers))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, nargs="?", default=ROOT / "Builds/city-bake.json")
    parser.add_argument("--require-full", action="store_true")
    args = parser.parse_args()
    manifest = json.loads((ROOT / "assets/source-manifest.json").read_text(encoding="utf-8"))
    report = json.loads(args.report.read_text(encoding="utf-8-sig"))
    result = validate_catalog(report, manifest["sourceVersion"], args.require_full)
    print("Catalog valid: " + json.dumps(result))


if __name__ == "__main__":
    main()
