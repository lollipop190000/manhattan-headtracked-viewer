import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import package_portable_windows as portable
from build_portable_tracker import PYTHON_PATHS, extract_checked


class PortableReleaseTests(unittest.TestCase):
    def setUp(self):
        scratch=portable.ROOT/'.cache/tests'; scratch.mkdir(parents=True,exist_ok=True)
        self.temp=tempfile.TemporaryDirectory(dir=scratch); self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name); self.stage=self.root/'stage'; self.stage.mkdir()
        (self.root/'tools').mkdir()
        self.viewer='1'*40; self.technology='2'*40; self.revision='3'*40
        self.model=b'fixture model'
        self.model_hash=hashlib.sha256(self.model).hexdigest()
        (self.root/'tools/technology-version.json').write_text(json.dumps({'revision':self.technology,'pythonVersion':'3.13.5','faceModelSha256':self.model_hash}))
        self.patch=mock.patch.object(portable,'ROOT',self.root); self.patch.start(); self.addCleanup(self.patch.stop)
        self.archive=self.root/'base.zip'
        self.base_data={'ManhattanViewer.exe':b'fixture player','README-Windows.md':b'old guide'}
        self.write_base()
        for name in portable.SHIPPING_TOOLS: self.write(name,b'fixture launcher')
        self.write('README-Windows.md',b'English Windows guide')
        self.write('.runtime/tracker/face_landmarker.task',self.model)
        self.write('.runtime/tracker/portable-tracker.json',json.dumps({'technologyRevision':self.technology,'pythonVersion':'3.13.5'}).encode())
        self.write('.runtime/tracker/.venv/Scripts/python313._pth',PYTHON_PATHS.encode())
        self.write('.runtime/tracker/.venv/Scripts/python.exe',b'fixture interpreter')
        for name in ('LICENSE-Python.txt','LICENSE-MediaPipe.txt','README.md'): self.write('Notices/PortableTracker/'+name,b'original notice')

    def write(self,name,data):
        path=self.stage/name; path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(data); return path

    def write_base(self,wrong_digest=False):
        records=[{'path':name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()} for name,data in self.base_data.items()]
        if wrong_digest: records[0]['sha256']='0'*64
        receipt={'buildIdentity':{'viewerRevision':self.viewer,'technologyRevision':self.technology,'dirtyWorkingTree':False},'files':records}
        with zipfile.ZipFile(self.archive,'w') as z:
            for name,data in self.base_data.items(): z.writestr(name,data)
            z.writestr('INTEGRITY.json',json.dumps(receipt))
        (self.root/'tools/portable-player.json').write_text(json.dumps({'sha256':portable.base.sha256_file(self.archive),'viewerRevision':self.viewer}))

    def package(self): return portable.package(self.archive,self.stage,self.root/'release.zip',self.revision)

    def test_both_modes_ship_and_every_receipt_hash_matches(self):
        result=self.package()
        with zipfile.ZipFile(result['zip']) as z:
            receipt=json.loads(z.read('INTEGRITY.json'))
            self.assertEqual(receipt['distributionRevision'],self.revision)
            self.assertEqual(receipt['buildIdentity']['viewerRevision'],self.viewer)
            for record in receipt['files']:
                self.assertEqual(hashlib.sha256(z.read(record['path'])).hexdigest(),record['sha256'])
            self.assertEqual(z.read('README-Windows.md'),b'English Windows guide')
            self.assertIn('Run-HeadTrackedDemo.cmd',z.namelist())
            self.assertIn('.runtime/tracker/face_landmarker.task',z.namelist())

    def test_personal_state_and_generated_caches_are_excluded(self):
        for name in ('.runtime/tracker/physical-calibration.json','.runtime/tracker/tracker_camera_preference.json',
                     '.runtime/tracker/__pycache__/tracker.pyc','.runtime/tracker/viewer-preferences.json'):
            self.write(name,b'private fixture')
        with zipfile.ZipFile(self.package()['zip']) as z:
            self.assertFalse(any('private fixture' in z.read(name).decode(errors='ignore') for name in z.namelist()))

    def test_unlisted_staging_content_is_rejected(self):
        self.write('source-archive.zip',b'unlisted')
        with self.assertRaisesRegex(ValueError,'Unlisted'): self.package()

    def test_changed_model_and_machine_specific_python_paths_are_rejected(self):
        self.write('.runtime/tracker/face_landmarker.task',b'wrong')
        with self.assertRaisesRegex(ValueError,'model digest'): self.package()
        self.write('.runtime/tracker/face_landmarker.task',self.model)
        self.write('.runtime/tracker/.venv/Scripts/python313._pth',b'C:\\Python313\nimport site\n')
        with self.assertRaisesRegex(ValueError,'isolated and relative'): self.package()

    def test_base_archive_change_and_internal_hash_mismatch_are_rejected(self):
        self.archive.write_bytes(self.archive.read_bytes()+b'changed')
        with self.assertRaisesRegex(ValueError,'checksum'): self.package()
        self.write_base(wrong_digest=True)
        with self.assertRaisesRegex(ValueError,'Shipping input differs'): self.package()
        self.assertFalse((self.root/'release.zip').exists())

    def test_dirty_or_different_base_identity_is_rejected(self):
        pin=json.loads((self.root/'tools/portable-player.json').read_text()); pin['viewerRevision']='9'*40
        (self.root/'tools/portable-player.json').write_text(json.dumps(pin))
        with self.assertRaisesRegex(ValueError,'identity'): self.package()

    def test_download_archive_traversal_and_case_alias_are_rejected(self):
        for names in (('../outside.txt',),('python.exe','PYTHON.exe')):
            archive=self.root/'unsafe.zip'
            with zipfile.ZipFile(archive,'w') as z:
                for name in names: z.writestr(name,b'fixture')
            with self.assertRaises(ValueError): extract_checked(archive,self.root/'unpacked')
        self.assertFalse((self.root/'outside.txt').exists())


if __name__=='__main__': unittest.main()
