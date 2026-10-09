import copy
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from catalog_validation import validate_catalog


def tile(identifier, layer, replaces=None):
    return dict(id=identifier, address=identifier, layer=layer,
                cityBounds=dict(center=dict(x=0, y=20, z=0), size=dict(x=500, y=40, z=500)),
                cityOrigin=dict(x=0, y=0, z=0), residentBytes=1024, triangles=12, replaces=replaces or [],
                requiredReplacementCount=1 if layer == "Skyline" else 0)


class CatalogValidationTests(unittest.TestCase):
    def setUp(self):
        self.report = dict(sourceVersion="source-v1", tileCount=3, buildingCount=1, triangles=36,
                           tiles=[tile("skyline", "Skyline"), tile("core", "Core", ["skyline"]), tile("windows", "Near")])

    def test_replacement_and_additive_catalog(self):
        self.assertEqual(3, validate_catalog(self.report, "source-v1")["tiles"])

    def test_duplicate_address_and_stale_source_rejected(self):
        bad = copy.deepcopy(self.report)
        bad["tiles"][2]["address"] = "core"
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            validate_catalog(bad, "source-v1")
        with self.assertRaisesRegex(ValueError, "source version"):
            validate_catalog(self.report, "updated-source")

    def test_additive_windows_must_not_hide_building_body(self):
        bad = copy.deepcopy(self.report)
        bad["tiles"][2]["replaces"] = ["skyline"]
        with self.assertRaisesRegex(ValueError, "Additive"):
            validate_catalog(bad, "source-v1")

    def test_invalid_bounds_and_replacement_rejected(self):
        bad = copy.deepcopy(self.report)
        bad["tiles"][0]["cityBounds"]["center"]["x"] = float("nan")
        with self.assertRaisesRegex(ValueError, "Non-finite"):
            validate_catalog(bad, "source-v1")
        bad = copy.deepcopy(self.report)
        bad["tiles"][0]["cityBounds"]["size"]["y"] = -1
        with self.assertRaisesRegex(ValueError, "Negative"):
            validate_catalog(bad, "source-v1")
        bad = copy.deepcopy(self.report)
        bad["tiles"][1]["replaces"] = ["missing"]
        with self.assertRaisesRegex(ValueError, "existing skyline"):
            validate_catalog(bad, "source-v1")

    def test_full_catalog_requires_every_layer(self):
        with self.assertRaisesRegex(ValueError, "missing required layers"):
            validate_catalog(self.report, "source-v1", require_full=True)
        for layer in ("Ground", "Vegetation", "Collision"):
            self.report["tiles"].append(tile(layer, layer))
        self.report["tileCount"] = 6
        self.report["triangles"] = 72
        self.assertEqual(6, validate_catalog(self.report, "source-v1", require_full=True)["tiles"])

    def test_skyline_waits_for_exact_number_of_core_replacements(self):
        self.report["tiles"].append(tile("core2", "Core", ["skyline"]))
        self.report["tileCount"] = 4
        self.report["triangles"] = 48
        with self.assertRaisesRegex(ValueError, "incoming Core coverage"):
            validate_catalog(self.report, "source-v1")
        self.report["tiles"][0]["requiredReplacementCount"] = 2
        self.assertEqual(4, validate_catalog(self.report, "source-v1")["tiles"])

    def test_unreplaced_skyline_stays_visible_with_zero_count(self):
        self.report["tiles"][1]["replaces"] = []
        self.report["tiles"][0]["requiredReplacementCount"] = 0
        self.assertEqual(3, validate_catalog(self.report, "source-v1")["tiles"])
        self.report["tiles"][0]["requiredReplacementCount"] = -1
        with self.assertRaisesRegex(ValueError, "nonnegative"):
            validate_catalog(self.report, "source-v1")


if __name__ == "__main__":
    unittest.main()
