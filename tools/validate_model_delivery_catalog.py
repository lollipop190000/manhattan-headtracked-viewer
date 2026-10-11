"""Offline delivery-catalog checks only. Never imports or activates a model."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import urlsplit

DEFAULT_CATALOG = Path(__file__).resolve().parents[1] / 'docs/model-delivery-catalog.json'
SHA256 = re.compile(r'[0-9a-f]{64}\Z')
SOURCE_ID = re.compile(r'[0-9]+_[0-9]+\Z')
KINDS = {'baseline', 'optional_replacement', 'isolated_prototype', 'historical_research'}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def unique_strings(values, label):
    require(isinstance(values, list) and all(isinstance(v, str) and v for v in values), label)
    require(len(values) == len(set(values)), 'duplicate ' + label)
    return set(values)


def relative_path(value):
    return (isinstance(value, str) and value and not value.startswith(('/', '\\'))
            and '\\' not in value and ':' not in value
            and all(p not in ('', '.', '..') for p in value.split('/')))


def validate_catalog(catalog):
    require(type(catalog.get('schemaVersion')) is int and catalog['schemaVersion'] == 1, 'schemaVersion')
    require(catalog.get('runtimeReady') is False, 'runtimeReady must remain false')
    require(re.fullmatch(r'[0-9a-f]{40}', catalog.get('repositoryBaseCommit', '')), 'base commit')
    artifacts, profiles = catalog['artifacts'], catalog['profiles']
    require(isinstance(artifacts, dict) and artifacts and isinstance(profiles, dict) and profiles, 'empty catalog')
    for key, artifact in artifacts.items():
        require(type(artifact['bytes']) is int and artifact['bytes'] > 0, key + ': byte count')
        require(SHA256.fullmatch(artifact['sha256']), key + ': SHA256')
        require(relative_path(artifact['filename']) and '/' not in artifact['filename'], key + ': filename')
        require(artifact.get('remoteReadbackVerified') is True, key + ': missing delivery verification')
        for field, prefix, id_field in [('url', '/file/d/', 'fileId'), ('folderUrl', '/drive/folders/', 'folderId')]:
            url = urlsplit(artifact[field])
            suffix = '/view' if field == 'url' else ''
            require(url.scheme == 'https' and url.netloc == 'drive.google.com'
                    and re.fullmatch(r'[A-Za-z0-9_-]+', artifact[id_field])
                    and url.path == prefix + artifact[id_field] + suffix and not url.fragment,
                    key + ': Drive link identity')
        manifest = artifact['identityManifest']
        require(relative_path(manifest['path']) and SHA256.fullmatch(manifest['sha256']), key + ': manifest identity')
        for bound in artifact.get('boundManifests', {}).values():
            require(relative_path(bound['path']) and SHA256.fullmatch(bound['sha256']), key + ': bound manifest identity')
        if 'supersedesArtifact' in artifact:
            require(artifact['supersedesArtifact'] in artifacts and artifact['supersedesArtifact'] != key, key + ': superseded artifact')
        checks = artifact.get('parentAssetChecks', [])
        require(isinstance(checks, list), key + ': parent checks')
        checked_ids = set()
        for check in checks:
            require(check['sourceId'] not in checked_ids and check['role'] in {'target', 'context'}, key + ': duplicate/invalid parent check')
            checked_ids.add(check['sourceId'])
            require(check['currentProfile'] in profiles, key + ': parent profile')
            require(check['sourceId'] in profiles[check['currentProfile']]['sourceIds'], key + ': parent source ID')
            require(SHA256.fullmatch(check['pinnedSha256']) and SHA256.fullmatch(check['currentSha256']), key + ': parent hash')
            require(check['matches'] is (check['pinnedSha256'] == check['currentSha256']), key + ': parent result')
    for key, profile in profiles.items():
        require(profile['artifact'] in artifacts and profile['kind'] in KINDS, key + ': artifact/kind')
        ids = unique_strings(profile['sourceIds'], key + ' source IDs')
        require(ids and all(SOURCE_ID.fullmatch(s) for s in ids), key + ': source ID format')
        requires = unique_strings(profile['requires'], key + ' dependencies')
        require(requires <= profiles.keys() and key not in requires, key + ': unknown/self dependency')
        require(profile.get('activeByDefault') is False, key + ': automatic activation forbidden')
        contribution = profile['acceptedCoverageContribution']
        require(type(contribution) is int and contribution == (len(ids) if profile['kind'] == 'baseline' else 0), key + ': coverage contribution')
        if profile['kind'] == 'optional_replacement':
            supersedes = profile['supersedes']
            require(set(supersedes) == ids, key + ': incomplete superseding map')
            for source_id, parent in supersedes.items():
                require(parent in requires and profiles[parent]['kind'] == 'baseline'
                        and source_id in profiles[parent]['sourceIds'], key + ': invalid superseding parent')
            companions = unique_strings(profile['requiredCompanionIds'], key + ' companion IDs')
            require(companions <= ids, key + ': missing companion')
            checks = artifacts[profile['artifact']].get('parentAssetChecks', [])
            targets = {c['sourceId']: c for c in checks if c['role'] == 'target'}
            context = {c['sourceId'] for c in checks if c['role'] == 'context'}
            require(unique_strings(profile['requiredContextIds'], key + ' context IDs') == context, key + ': incomplete context checks')
            require(checks and set(targets) == ids, key + ': missing/extra parent target checks')
            require(all(targets[s]['currentProfile'] == supersedes[s] for s in ids), key + ': parent check superseding identity')
            policy = artifacts[profile['artifact']].get('compositionPolicy', 'catalog_plan')
            require(policy in {'catalog_plan', 'full_baseline_single_optional'}, key + ': composition policy')
            require(profile.get('compositionPolicy', 'catalog_plan') == policy, key + ': missing composition policy')
            if policy == 'full_baseline_single_optional':
                require(requires == set(catalog['acceptedBaseline']['profileIds']), key + ': complete baseline required')
                rollback = artifacts[profile['artifact']]['rollbackTargets']
                rollback_ids = unique_strings([r['sourceId'] for r in rollback], key + ' rollback IDs')
                require(rollback_ids == ids, key + ': incomplete rollback')
                for item in rollback:
                    require(relative_path(item['path']) and item['profile'] == supersedes[item['sourceId']]
                            and item['sha256'] == targets[item['sourceId']]['currentSha256'], key + ': stale rollback')
                    require(type(item['externalImageCount']) is int and item['externalImageCount'] >= 0, key + ': rollback images')
            mismatches = any(not c['matches'] for c in checks)
            require(profile['compatibility'] in {'blocked_stale_parent', 'parent_hashes_match_only'}, key + ': compatibility status')
            require(not mismatches or profile['compatibility'] == 'blocked_stale_parent', key + ': stale parent must block')
    baseline = catalog['acceptedBaseline']
    names = unique_strings(baseline['profileIds'], 'baseline profiles')
    require(names <= profiles.keys(), 'unknown baseline profile')
    require(names == {key for key, p in profiles.items() if p['kind'] == 'baseline'}, 'baseline inventory')
    ids = [source_id for name in names for source_id in profiles[name]['sourceIds']]
    require(len(ids) == len(set(ids)), 'baseline source IDs overlap')
    require(type(baseline['sourcePartCount']) is int and len(ids) == baseline['sourcePartCount'], 'baseline count')
    require(all(type(n) is int and n > 0 for n in baseline['arithmetic'])
            and sum(baseline['arithmetic']) == len(ids), 'coverage arithmetic')
    composition = baseline['original179Composition']
    require(isinstance(composition, list) and composition and all(type(n) is int and n > 0 for n in composition), 'original179 composition')
    sizes = [len(profiles[n]['sourceIds']) for n in baseline['profileIds']]
    require(sizes[:len(composition)] == composition
            and [sum(composition)] + sizes[len(composition):] == baseline['arithmetic'], 'original179 composition/profile arithmetic')
    for name, profile in profiles.items():
        if name.startswith('historical_four_'):
            require(not set(profile['sourceIds']) & set(ids), 'historical prototypes overlap accepted coverage')
    return profiles


def validate_selection(catalog, selected):
    profiles = validate_catalog(catalog)
    chosen = unique_strings(selected, 'selected profiles')
    require(chosen and chosen <= profiles.keys(), 'unknown/empty selection')
    groups = set()
    for name in selected:
        p = profiles[name]
        require(set(p['requires']) <= chosen, name + ': missing dependency')
        require(p.get('compatibility') != 'blocked_stale_parent', name + ': blocked stale parent')
        if p['kind'] in {'isolated_prototype', 'historical_research'}:
            require(len(selected) == 1, name + ': isolated profile cannot mix with current selection')
        if p.get('compositionPolicy') == 'full_baseline_single_optional':
            require(sum(profiles[n]['kind'] == 'optional_replacement' for n in selected) == 1,
                    name + ': other optional profiles not reviewed')
        group = p.get('exclusiveGroup')
        require(not group or group not in groups, name + ': mutually exclusive profiles')
        if group:
            groups.add(group)
    owners = {}
    # A superseding selection is explicit and order-independent; both bodies must never be loaded.
    for name in sorted(selected, key=lambda n: profiles[n]['kind'] == 'optional_replacement'):
        p = profiles[name]
        for source_id in p['sourceIds']:
            if p['kind'] == 'optional_replacement':
                require(owners.get(source_id) == p['supersedes'][source_id], name + ': duplicate or wrong superseding owner')
            else:
                require(source_id not in owners, name + ': duplicate source owner')
            owners[source_id] = name
    for name in selected:
        p = profiles[name]
        if p['kind'] == 'optional_replacement':
            for check in catalog['artifacts'][p['artifact']]['parentAssetChecks']:
                if check['role'] == 'context':
                    require(owners.get(check['sourceId']) == check['currentProfile'], name + ': context owner changed')
    accepted = {s for n in catalog['acceptedBaseline']['profileIds'] for s in profiles[n]['sourceIds']}
    count = len(set(owners) & accepted) if not all(profiles[n]['kind'] in {'isolated_prototype', 'historical_research'} for n in selected) else 0
    return {'selectedProfiles': selected, 'selectedSourceParts': len(owners), 'acceptedSourceParts': count,
            'runtimeReady': False, 'scope': 'offline catalog consistency only; no scene inspected or changed'}


def verify_archive(artifact, path):
    path = Path(path)
    require(path.stat().st_size == artifact['bytes'], 'archive byte count mismatch')
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    require(digest.hexdigest() == artifact['sha256'], 'archive SHA256 mismatch')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalog', type=Path, default=DEFAULT_CATALOG)
    parser.add_argument('--selection', nargs='+', help='Explicit profile IDs; default is the current580 baseline')
    parser.add_argument('--archive', action='append', default=[], metavar='ARTIFACT=PATH', help='Check downloaded ZIP bytes without extracting/importing')
    args = parser.parse_args()
    try:
        catalog = json.loads(args.catalog.read_text(encoding='utf-8'))
        result = validate_selection(catalog, args.selection if args.selection is not None else catalog['acceptedBaseline']['profileIds'])
        for item in args.archive:
            name, path = item.split('=', 1)
            verify_archive(catalog['artifacts'][name], path)
        print(json.dumps(result, indent=2))
    except (ValueError, KeyError, TypeError, OSError) as error:
        parser.exit(1, 'Catalog rejected: ' + str(error) + '\n')


if __name__ == '__main__':
    main()
