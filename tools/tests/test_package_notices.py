import hashlib
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import package_notices


class PackageNoticeTests(unittest.TestCase):
    def test_copy_preserves_original_and_rejects_altered_terms(self):
        temporary_root = package_notices.ROOT / ".cache/tests"
        temporary_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            source = root / "NYC_Open_Data_Terms.html"
            payload = b"Original terms, attribution, and conditions."
            source.write_bytes(payload)
            expected = dict(bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest())
            target = root / "build/Notices/terms.html"
            copied = package_notices.copy_notice(source, target, expected)
            self.assertEqual(expected["sha256"], copied["sha256"])
            self.assertEqual(payload, target.read_bytes())
            self.assertEqual(payload, source.read_bytes())
            source.write_bytes(payload + b" altered")
            with self.assertRaisesRegex(ValueError, "notice SHA-256/size changed"):
                package_notices.copy_notice(source, target, expected)
            self.assertEqual(payload, target.read_bytes())


if __name__ == "__main__":
    unittest.main()
