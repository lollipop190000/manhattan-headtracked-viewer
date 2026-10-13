"""Combine the verified city player and freshly built portable webcam runtime."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile
import zipfile

import package_windows as base
from build_portable_tracker import PYTHON_PATHS, CMD_ENTRY, MODEL_LOADING_PATCH

ROOT = Path(__file__).resolve().parents[1]
SHIPPING_TOOLS = {'Run-HeadTrackedDemo.cmd','tools/run_demo.ps1','tools/portable_check.py','tools/portable-runtime.json'}
PRIVATE = base.PRIVATE_NAMES | {'monitor_hint.json','direct_url.json','pyvenv.cfg'}


def omitted(name):
    parts = Path(name).parts
    return (any(part.casefold()=='__pycache__' for part in parts) or parts[-1].casefold() in PRIVATE or
            parts[-1].casefold().startswith('tracker_camera_') or name.lower().endswith(('.pyc','.log','.heartbeat')) or
            name.startswith('.runtime/tracker/.venv/Lib/site-packages/bin/'))


def staged_files(stage):
    stage = Path(stage).resolve()
    base.reject_link(stage)
    found = {}
    aliases = set()
    for path in sorted(stage.rglob('*')):
        base.reject_link(path)
        if not path.is_file(): continue
        name = path.relative_to(stage).as_posix()
        base.checked_relative(name)
        if omitted(name): continue
        if name.casefold() in aliases: raise ValueError('Case-colliding release path')
        aliases.add(name.casefold())
        allowed = (name in SHIPPING_TOOLS or name=='README-Windows.md' or name.startswith('Notices/PortableTracker/') or
                   name=='.runtime/viewer/ProjectSettings/ProjectSettings.asset' or
                   name in {'.runtime/technology/tools/run_demo.ps1','.runtime/technology/tools/launcher_common.ps1',
                            '.runtime/technology/LICENSE','.runtime/technology/THIRD_PARTY.md'} or
                   name in {'.runtime/tracker/'+p for p in ('tracker.py','pose_estimator.py','camera_control.py','session_lifetime.py',
                              'models/canonical_face_model.obj','models/LICENSE.mediapipe','face_landmarker.task',
                              'tracker-requirements.lock.txt','portable-tracker.json')} or
                   name.startswith('.runtime/tracker/.venv/Scripts/') or name.startswith('.runtime/tracker/.venv/Lib/site-packages/'))
        if not allowed: raise ValueError('Unlisted staged file: '+name)
        found[name]=path
    return found


def read_base(archive, pin):
    if base.sha256_file(archive)!=pin['sha256']: raise ValueError('Base player archive checksum differs')
    with zipfile.ZipFile(archive) as data:
        aliases=set()
        for entry in data.infolist():
            base.checked_relative(entry.filename)
            if entry.filename.casefold() in aliases: raise ValueError('Base archive has duplicate/case-colliding names')
            aliases.add(entry.filename.casefold())
            if entry.is_dir(): raise ValueError('Base shipping archive must contain files only')
        receipt=json.loads(data.read('INTEGRITY.json'),object_pairs_hook=base.no_duplicate_keys)
        identity=receipt['buildIdentity']
        if identity.get('viewerRevision')!=pin['viewerRevision'] or identity.get('dirtyWorkingTree') is not False:
            raise ValueError('Base player identity differs or is dirty')
        records={record['path']:record for record in receipt['files']}
        if len(records)!=len(receipt['files']) or set(data.namelist()) != set(records)|{'INTEGRITY.json'}:
            raise ValueError('Base archive and receipt do not match')
        for name in records:
            if base.excluded(name): raise ValueError('Private file in base player archive')
            entry=data.getinfo(name)
            if entry.file_size!=records[name]['bytes']: raise ValueError('Base file size differs: '+name)
    return receipt, records


def validate_runtime(files):
    if files['Run-HeadTrackedDemo.cmd'].read_bytes()!=CMD_ENTRY:
        raise ValueError('Portable CMD must use Windows PowerShell directly and preserve its exit status')
    technology=json.loads((ROOT/'tools/technology-version.json').read_text(encoding='utf-8'))
    manifest=json.loads(files['.runtime/tracker/portable-tracker.json'].read_text(encoding='utf-8'))
    if manifest['technologyRevision']!=technology['revision'] or manifest['pythonVersion']!=technology['pythonVersion']:
        raise ValueError('Portable runtime differs from the technology pin')
    if (manifest.get('trackerCompatibilityPatch') != MODEL_LOADING_PATCH or
            base.sha256_file(files['.runtime/tracker/tracker.py']) != manifest.get('portableTrackerSha256')):
        raise ValueError('Portable tracker compatibility patch differs')
    if base.sha256_file(files['.runtime/tracker/face_landmarker.task'])!=technology['faceModelSha256']:
        raise ValueError('Portable model digest differs')
    if files['.runtime/tracker/.venv/Scripts/python313._pth'].read_text(encoding='utf-8')!=PYTHON_PATHS:
        raise ValueError('Portable Python path configuration is not isolated and relative')
    for required in ('.runtime/tracker/.venv/Scripts/python.exe','Notices/PortableTracker/LICENSE-Python.txt',
                     'Notices/PortableTracker/LICENSE-MediaPipe.txt','Notices/PortableTracker/README.md'):
        if required not in files: raise ValueError('Required portable file missing: '+required)
    return manifest


def package(archive, stage, output, revision, version='v0.1.1'):
    if not re.fullmatch('[0-9a-f]{40}',revision): raise ValueError('Distribution revision must be a full Git SHA')
    pin=json.loads((ROOT/'tools/portable-player.json').read_text(encoding='utf-8'))
    inherited, records=read_base(archive,pin)
    files=staged_files(stage)
    manifest=validate_runtime(files)
    if 'README-Windows.md' not in files: raise ValueError('English Windows guide must be staged')
    if inherited['buildIdentity']['technologyRevision']!=manifest['technologyRevision']: raise ValueError('Player and tracker technology versions differ')
    added=[dict(path=name,bytes=path.stat().st_size,sha256=base.sha256_file(path)) for name,path in sorted(files.items())]
    merged=dict(records)
    for record in added:
        if record['path'] in merged and record['path']!='README-Windows.md': raise ValueError('Staged file overwrites a base player file')
        merged[record['path']]=record
    all_records=[merged[name] for name in sorted(merged)]
    receipt=dict(schemaVersion=2,distributionVersion=version,distributionRevision=revision,
                 buildIdentity=inherited['buildIdentity'],baseArchiveSha256=pin['sha256'],
                 trackerTechnologyRevision=manifest['technologyRevision'],fileCount=len(all_records),
                 totalBytes=sum(r['bytes'] for r in all_records),files=all_records,
                 fileTreeSha256=hashlib.sha256(json.dumps(all_records,sort_keys=True,separators=(',',':')).encode()).hexdigest(),
                 receiptSelfExcluded='INTEGRITY.json')
    output=Path(output).resolve()
    if output.suffix!='.zip' or output.exists() or output.is_relative_to(Path(stage).resolve()): raise ValueError('Use a new ZIP outside staging')
    output.parent.mkdir(parents=True,exist_ok=True)
    receipt_data=(json.dumps(receipt,indent=2,sort_keys=True)+'\n').encode('utf-8')
    with tempfile.NamedTemporaryFile(dir=output.parent,prefix=output.name+'.',suffix='.tmp',delete=False) as temp:
        temporary=Path(temp.name)
    try:
        with zipfile.ZipFile(archive) as source, zipfile.ZipFile(temporary,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as destination:
            for name in sorted(set(merged)|{'INTEGRITY.json'}):
                info=zipfile.ZipInfo(name,(1980,1,1,0,0,0)); info.create_system=0; info.external_attr=0x20; info.compress_type=zipfile.ZIP_DEFLATED
                if name=='INTEGRITY.json': destination.writestr(info,receipt_data); continue
                read=files[name].open('rb') if name in files else source.open(name)
                digest=hashlib.sha256(); size=0
                with read, destination.open(info,'w',force_zip64=True) as write:
                    for chunk in iter(lambda:read.read(1024*1024),b''):
                        write.write(chunk); digest.update(chunk); size+=len(chunk)
                if size!=merged[name]['bytes'] or digest.hexdigest()!=merged[name]['sha256']: raise ValueError('Shipping input differs: '+name)
        temporary.replace(output)
        output.with_suffix('.integrity.json').write_bytes(receipt_data)
        checksum=base.sha256_file(output)
        output.with_name(output.name+'.sha256').write_text(checksum+'  '+output.name+'\n',encoding='utf-8')
    finally: temporary.unlink(missing_ok=True)
    return {'zip':str(output),'zipBytes':output.stat().st_size,'zipSha256':checksum,'fileCount':len(all_records),
            'distributionRevision':revision,'playerRevision':inherited['buildIdentity']['viewerRevision']}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-player-zip',type=Path,required=True)
    parser.add_argument('--runtime-stage',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    revision=subprocess.check_output(['git','-C',str(ROOT),'rev-parse','HEAD'],text=True).strip()
    if subprocess.check_output(['git','-C',str(ROOT),'status','--porcelain','--untracked-files=normal'],text=True).strip():
        raise SystemExit('Commit release tooling and guides before packaging.')
    pin=json.loads((ROOT/'tools/portable-player.json').read_text(encoding='utf-8'))
    subprocess.run(['git','-C',str(ROOT),'diff','--exit-code',pin['viewerRevision'],revision,'--','ManhattanViewer','assets/source-manifest.json','tools/technology-version.json'],check=True)
    print(json.dumps(package(args.base_player_zip,args.runtime_stage,args.output,revision),indent=2))
