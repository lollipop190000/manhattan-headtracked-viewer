"""Build an application-local Python tracker from verified upstream archives, not a local venv."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
from urllib.request import urlopen
import zipfile

from package_windows import checked_relative, reject_link, sha256_file

ROOT = Path(__file__).resolve().parents[1]
RUNTIME_FILES = ('tracker.py','pose_estimator.py','camera_control.py','session_lifetime.py',
                 'models/canonical_face_model.obj','models/LICENSE.mediapipe')
LAUNCHER_FILES = ('tools/run_demo.ps1','tools/launcher_common.ps1','Run-HeadTrackedDemo.cmd','LICENSE','THIRD_PARTY.md')
PYTHON_PATHS = 'python313.zip\n.\n..\\Lib\\site-packages\\\n..\\..\\\n'


def read_json(path): return json.loads(Path(path).read_text(encoding='utf-8'))


def fetch_checked(item, folder):
    path = folder / item['filename']
    if not path.exists():
        with urlopen(item['url'],timeout=90) as response: path.write_bytes(response.read())
    reject_link(path)
    if sha256_file(path) != item['sha256']: raise ValueError('Upstream checksum differs: '+path.name)
    return path


def extract_checked(archive, destination):
    with zipfile.ZipFile(archive) as data:
        seen = set()
        for entry in data.infolist():
            name = entry.filename.rstrip('/')
            checked_relative(name)
            if name.casefold() in seen: raise ValueError('Duplicate archive path')
            seen.add(name.casefold())
            if (entry.external_attr >> 16) & 0o170000 == 0o120000: raise ValueError('Archive symlink')
        data.extractall(destination)


def build_tracker(output, downloads, technology_root):
    version = read_json(ROOT/'tools/technology-version.json')
    if sys.platform!='win32' or platform.python_version()!=version['pythonVersion']:
        raise ValueError('Build the portable tracker on Windows with Python '+version['pythonVersion'])
    pinned = read_json(ROOT/'tools/portable-runtime.json')
    output = Path(output).resolve()
    if output.exists(): raise ValueError('Use a new empty runtime output directory')
    if output.is_relative_to(ROOT/'tools') or output == ROOT: raise ValueError('Runtime output must be outside source tools')
    actual = subprocess.check_output(['git','-C',str(technology_root),'rev-parse','HEAD'],text=True).strip()
    if actual != version['revision']: raise ValueError('Technology checkout differs from the pinned revision')
    downloads.mkdir(parents=True,exist_ok=True)
    python_zip = fetch_checked(pinned['python'], downloads)
    model = fetch_checked(pinned['faceModel'],downloads)
    output.mkdir(parents=True)
    tracker = output/'.runtime/tracker'
    interpreter = tracker/'.venv/Scripts'
    site_packages = tracker/'.venv/Lib/site-packages'
    interpreter.mkdir(parents=True)
    extract_checked(python_zip, interpreter)
    # Isolated _pth ignores installed Python, PYTHONPATH and registry entries.
    # Trailing separators keep Windows path normalization from trimming a final
    # '..'; omitting import site prevents installed user packages being appended.
    (interpreter/'python313._pth').write_text(PYTHON_PATHS,encoding='utf-8')
    wheelhouse = downloads/'wheels'
    subprocess.run([sys.executable,'-m','pip','download','--only-binary=:all:','--dest',str(wheelhouse),
                    '-r',str(ROOT/'tools/tracker-requirements.lock.txt')],check=True)
    wheels = []
    requirements = [line for line in (ROOT/'tools/tracker-requirements.lock.txt').read_text().splitlines() if line and not line.startswith('#')]
    for requirement in requirements:
        name, expected = requirement.split('==')
        candidates = [p for p in wheelhouse.glob('*.whl') if p.name.split('-')[0].casefold().replace('_','-') == name.casefold().replace('_','-') and p.name.split('-')[1] == expected]
        if len(candidates) != 1: raise ValueError('Expected exactly one pinned Windows wheel: '+requirement)
        wheel = candidates[0]
        with urlopen(f'https://pypi.org/pypi/{name}/{expected}/json',timeout=45) as response: metadata = json.load(response)
        remote = next((entry for entry in metadata['urls'] if entry['filename']==wheel.name),None)
        if remote is None or sha256_file(wheel) != remote['digests']['sha256']: raise ValueError('PyPI wheel checksum differs: '+wheel.name)
        wheels.append({'name':name,'version':expected,'filename':wheel.name,'sha256':sha256_file(wheel),'url':remote['url']})
    subprocess.run([sys.executable,'-m','pip','install','--no-index','--no-deps','--no-compile','--find-links',str(wheelhouse),
                    '--target',str(site_packages),'-r',str(ROOT/'tools/tracker-requirements.lock.txt')],check=True)
    for relative in RUNTIME_FILES:
        destination = tracker/relative
        destination.parent.mkdir(parents=True,exist_ok=True)
        destination.write_bytes(subprocess.check_output(['git','-C',str(technology_root),'show',version['revision']+':python_tracker/'+relative]))
    shutil.copyfile(model,tracker/'face_landmarker.task')
    shutil.copyfile(ROOT/'tools/tracker-requirements.lock.txt',tracker/'tracker-requirements.lock.txt')
    technology = output/'.runtime/technology'
    for relative in LAUNCHER_FILES:
        destination = output/relative if relative.endswith('.cmd') else technology/relative
        destination.parent.mkdir(parents=True,exist_ok=True)
        destination.write_bytes(subprocess.check_output(['git','-C',str(technology_root),'show',version['revision']+':'+relative]))
    settings = output/'.runtime/viewer/ProjectSettings/ProjectSettings.asset'
    settings.parent.mkdir(parents=True)
    settings.write_text('PlayerSettings:\n  companyName: HeadTrackedDisplay\n  productName: ManhattanViewer\n',encoding='utf-8')
    (output/'tools').mkdir()
    for source, destination in [('portable_check.py','portable_check.py'),('run_demo.ps1','run_demo.ps1')]:
        shutil.copyfile(ROOT/'tools/portable_release'/source,output/'tools'/destination)
    shutil.copyfile(ROOT/'tools/portable-runtime.json',output/'tools/portable-runtime.json')
    manifest = {'schemaVersion':1,'pythonVersion':version['pythonVersion'],'pythonArchiveSha256':pinned['python']['sha256'],
                'technologyRevision':version['revision'],'modelSha256':version['faceModelSha256'],'wheels':wheels,
                'layout':'Fresh CPython embeddable distribution and verified wheels; not a copied development venv'}
    (tracker/'portable-tracker.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
    notices = output/'Notices/PortableTracker'
    notices.mkdir(parents=True)
    shutil.copyfile(interpreter/'LICENSE.txt',notices/'LICENSE-Python.txt')
    shutil.copyfile(tracker/'models/LICENSE.mediapipe',notices/'LICENSE-MediaPipe.txt')
    shutil.copyfile(technology/'LICENSE',notices/'LICENSE-HeadTrackedDisplay.txt')
    for item_name in ('pythonSbom','faceDetectorCard','faceMeshCard','blendshapeCard'):
        item = pinned[item_name]
        shutil.copyfile(fetch_checked(item,downloads),notices/item['filename'])
    for dist_info in site_packages.glob('*.dist-info'):
        for path in dist_info.rglob('*'):
            if path.is_file() and (any('license' in part.casefold() or 'notice' in part.casefold() for part in path.relative_to(dist_info).parts)):
                target = notices/'Dependencies'/dist_info.name/path.relative_to(dist_info)
                target.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(path,target)
    shutil.copyfile(ROOT/'docs/portable-tracker-notices.md',notices/'README.md')
    print('Portable tracker staged at '+str(output),flush=True)
    return manifest


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--technology-root',type=Path,required=True)
    parser.add_argument('--downloads',type=Path,default=ROOT/'.cache/portable-downloads')
    args=parser.parse_args()
    build_tracker(args.output,args.downloads,args.technology_root)
