import copy
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / 'validate_model_delivery_catalog.py'
spec = importlib.util.spec_from_file_location('delivery_catalog', TOOL)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class DeliveryCatalogTests(unittest.TestCase):
    def setUp(self):
        self.catalog = json.loads(m.DEFAULT_CATALOG.read_text(encoding='utf-8'))
        self.baseline = self.catalog['acceptedBaseline']['profileIds']

    def rejected(self, mutate, selection=None):
        mutate(self.catalog)
        with self.assertRaises((ValueError, KeyError, TypeError)):
            m.validate_selection(self.catalog, selection or self.baseline)

    def test_current_baseline_is580_disjoint(self):
        result = m.validate_selection(self.catalog, self.baseline)
        self.assertEqual(result['acceptedSourceParts'], 580)
        self.assertFalse(result['runtimeReady'])
        self.assertEqual([len(self.catalog['profiles'][p]['sourceIds']) for p in self.baseline], [140, 15, 6, 18, 162, 118, 121])

    def test_duplicate_baseline_source_rejected(self):
        self.rejected(lambda c: c['profiles']['north118_c']['sourceIds'].__setitem__(0, c['profiles']['north162_b_r2']['sourceIds'][0]))

    def test_duplicate_within_profile_rejected(self):
        self.rejected(lambda c: c['profiles'][self.baseline[0]]['sourceIds'].append(c['profiles'][self.baseline[0]]['sourceIds'][0]))

    def test_duplicate_selected_profile_rejected(self):
        with self.assertRaises(ValueError):
            m.validate_selection(self.catalog, self.baseline + [self.baseline[0]])

    def test_missing_parent_rejected(self):
        with self.assertRaises(ValueError):
            m.validate_selection(self.catalog, ['north162_b_r2'])

    def test_optional_replacements_keep580_order_independent(self):
        extras = ['block474_flat_city_preview', 'facade598463_r3']
        for names in [self.baseline + extras, extras + list(reversed(self.baseline))]:
            result = m.validate_selection(self.catalog, names)
            self.assertEqual(result['selectedSourceParts'], 580)
            self.assertEqual(result['acceptedSourceParts'], 580)
            self.assertFalse(result['runtimeReady'])

    def test_missing797579_companion_rejected(self):
        self.rejected(lambda c: c['profiles']['block474_flat_city_preview']['sourceIds'].remove('797579_0'))

    def test_wrong_superseding_parent_rejected(self):
        self.rejected(lambda c: c['profiles']['facade598463_r3']['supersedes'].update({'598463_0': 'north162_b_r2'}))

    def test_duplicate_optional_owner_rejected(self):
        self.catalog['profiles']['facade_duplicate'] = copy.deepcopy(self.catalog['profiles']['facade598463_r3'])
        with self.assertRaises(ValueError):
            m.validate_selection(self.catalog, self.baseline + ['facade598463_r3', 'facade_duplicate'])

    def test_block233_latest_parents_blocked(self):
        with self.assertRaisesRegex(ValueError, 'blocked stale parent'):
            m.validate_selection(self.catalog, self.baseline + ['block233_flat_city_preview'])
        checks = self.catalog['artifacts']['block233']['parentAssetChecks']
        self.assertEqual(sum(not c['matches'] for c in checks), 8)

    def test_stale_parent_cannot_be_marked_matching(self):
        self.rejected(lambda c: c['profiles']['block233_flat_city_preview'].update(compatibility='parent_hashes_match_only'))

    def test_missing_or_partial_parent_checks_rejected(self):
        for alteration in ['delete', 'empty', 'drop_target', 'duplicate']:
            c = copy.deepcopy(self.catalog)
            a = c['artifacts']['block474']
            if alteration == 'delete':
                del a['parentAssetChecks']
            elif alteration == 'empty':
                a['parentAssetChecks'] = []
            elif alteration == 'drop_target':
                a['parentAssetChecks'] = a['parentAssetChecks'][1:]
            else:
                a['parentAssetChecks'].append(copy.deepcopy(a['parentAssetChecks'][0]))
            with self.subTest(alteration=alteration), self.assertRaises(ValueError):
                m.validate_selection(c, self.baseline + ['block474_flat_city_preview'])

    def test_missing_context_check_rejected(self):
        self.rejected(lambda c: c['artifacts']['block474']['parentAssetChecks'].pop())

    def test_optional_overlay_cannot_replace_another_pinned_context(self):
        c = self.catalog
        check = copy.deepcopy(next(x for x in c['artifacts']['block474']['parentAssetChecks'] if x['role'] == 'context'))
        check['role'] = 'target'
        source_id = check['sourceId']
        c['artifacts']['test_neighbor'] = copy.deepcopy(c['artifacts']['facade598463'])
        c['artifacts']['test_neighbor']['parentAssetChecks'] = [check]
        p = copy.deepcopy(c['profiles']['facade598463_r3'])
        p.update(artifact='test_neighbor', sourceIds=[source_id], requires=['north162_b_r2'],
                 supersedes={source_id: 'north162_b_r2'}, requiredContextIds=[])
        c['profiles']['test_neighbor'] = p
        with self.assertRaisesRegex(ValueError, 'context owner changed'):
            m.validate_selection(c, self.baseline + ['block474_flat_city_preview', 'test_neighbor'])

    def test_composition_is_bound_to_profiles(self):
        self.rejected(lambda c: c['acceptedBaseline'].update(original179Composition=[179]))

    def test_parent_hash_result_must_be_truthful(self):
        self.rejected(lambda c: c['artifacts']['block233']['parentAssetChecks'][0].update(matches=True))

    def test_historical_prototypes_outside580(self):
        result = m.validate_selection(self.catalog, ['historical_four_flat_city_preview'])
        self.assertEqual(result['selectedSourceParts'], 4)
        self.assertEqual(result['acceptedSourceParts'], 0)

    def test_profiles_cannot_mix(self):
        for selection in [['historical_four_flat_city_preview', 'historical_four_source_absolute'],
                          self.baseline + ['block474_source_absolute'],
                          self.baseline + ['distance179_r2_research']]:
            with self.subTest(selection=selection), self.assertRaises(ValueError):
                m.validate_selection(self.catalog, selection)

    def test_old_distance_research_adds_no_coverage(self):
        result = m.validate_selection(self.catalog, ['distance179_r2_research'])
        self.assertEqual(result['selectedSourceParts'], 179)
        self.assertEqual(result['acceptedSourceParts'], 0)

    def test_invalid_size_or_hash_rejected(self):
        for field, value in [('bytes', True), ('bytes', 0), ('bytes', -1), ('sha256', 'abc')]:
            with self.subTest(field=field, value=value):
                c = copy.deepcopy(self.catalog)
                c['artifacts']['east_d3'][field] = value
                with self.assertRaises(ValueError):
                    m.validate_catalog(c)

    def test_unverified_or_mismatched_link_rejected(self):
        for changes in [{'remoteReadbackVerified': False}, {'fileId': 'invented'},
                        {'url': 'https://drive.google.com.evil.invalid/file/d/x/view'},
                        {'folderUrl': 'http://drive.google.com/drive/folders/x'}]:
            with self.subTest(changes=changes):
                c = copy.deepcopy(self.catalog)
                c['artifacts']['east_d3'].update(changes)
                with self.assertRaises(ValueError):
                    m.validate_catalog(c)

    def test_unsafe_manifest_path_rejected(self):
        for path in ['/tmp/private.json', '../private.json', 'C:/private.json', 'folder\\private.json']:
            c = copy.deepcopy(self.catalog)
            c['artifacts']['east_d3']['identityManifest']['path'] = path
            with self.subTest(path=path), self.assertRaises(ValueError):
                m.validate_catalog(c)

    def test_runtime_ready_cannot_be_enabled(self):
        self.rejected(lambda c: c.update(runtimeReady=True))

    def test_r2_baseline_selection_preserves580_and_r1_block(self):
        names = self.baseline + ['block233_r2_flat_city_preview']
        for selected in [names, list(reversed(names))]:
            result = m.validate_selection(self.catalog, selected)
            self.assertEqual(result['selectedSourceParts'], 580)
            self.assertEqual(result['acceptedSourceParts'], 580)
            self.assertFalse(result['runtimeReady'])
        self.assertEqual(self.catalog['profiles']['block233_flat_city_preview']['compatibility'], 'blocked_stale_parent')
        self.assertEqual(self.catalog['artifacts']['block233_r2']['supersedesArtifact'], 'block233')

    def test_r2_rejects_untested_optional_combinations(self):
        for other in ['block474_flat_city_preview', 'facade598463_r3']:
            for selected in [self.baseline + ['block233_r2_flat_city_preview', other],
                             [other, 'block233_r2_flat_city_preview'] + self.baseline]:
                with self.subTest(other=other), self.assertRaisesRegex(ValueError, 'other optional profiles'):
                    m.validate_selection(self.catalog, selected)

    def test_r2_requires_entire_baseline(self):
        names = self.baseline[:-1] + ['block233_r2_flat_city_preview']
        with self.assertRaisesRegex(ValueError, 'missing dependency'):
            m.validate_selection(self.catalog, names)
        self.rejected(lambda c: c['profiles']['block233_r2_flat_city_preview']['requires'].pop())

    def test_r2_cannot_omit_composition_policy(self):
        self.rejected(lambda c: c['profiles']['block233_r2_flat_city_preview'].pop('compositionPolicy'))

    def test_r2_source_absolute_is_isolated_only(self):
        name = 'block233_r2_source_absolute'
        result = m.validate_selection(self.catalog, [name])
        self.assertEqual(result['selectedSourceParts'], 4)
        self.assertEqual(result['acceptedSourceParts'], 0)
        for names in [self.baseline + [name], [name, 'block233_r2_flat_city_preview'],
                      [name, 'block233_source_absolute']]:
            with self.subTest(names=names), self.assertRaises(ValueError):
                m.validate_selection(self.catalog, names)

    def test_r2_rejects_stale_or_incomplete_rollback(self):
        for mode in ['stale_hash', 'missing_target', 'wrong_parent']:
            c = copy.deepcopy(self.catalog)
            rollback = c['artifacts']['block233_r2']['rollbackTargets']
            if mode == 'stale_hash':
                rollback[0]['sha256'] = c['artifacts']['block233']['parentAssetChecks'][0]['pinnedSha256']
            elif mode == 'missing_target':
                rollback.pop()
            else:
                rollback[0]['profile'] = 'soho_expansion_unchanged140'
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                m.validate_catalog(c)

    def test_r2_bound_manifest_hash_format(self):
        self.rejected(lambda c: c['artifacts']['block233_r2']['boundManifests']['current580Parents'].update(sha256='bad'))

    def test_downloaded_archive_verification(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'fixture.zip'
            path.write_bytes(b'fixture')
            artifact = {'bytes': 7, 'sha256': hashlib.sha256(b'fixture').hexdigest()}
            m.verify_archive(artifact, path)
            path.write_bytes(b'changed')
            with self.assertRaisesRegex(ValueError, 'SHA256'):
                m.verify_archive(artifact, path)
            path.write_bytes(b'short')
            with self.assertRaisesRegex(ValueError, 'byte count'):
                m.verify_archive(artifact, path)


if __name__ == '__main__':
    unittest.main()
