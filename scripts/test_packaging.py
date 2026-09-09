import copy
import hashlib
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile

import build_release
from build_listing import make_listing


class PackagingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.output = Path(cls.temp.name)
        cls.metadata = build_release.build(cls.output)
        cls.config = json.loads((build_release.ROOT / 'distribution.json').read_text())
        cls.source = json.loads((build_release.ROOT / 'source.json').read_text())

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def test_archives_preserve_guid_and_migration_roots(self):
        expected = {}
        for name, manifest in self.metadata['packages'].items():
            path = self.output / manifest['url'].rsplit('/', 1)[-1]
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), manifest['zipSHA256'])
            with zipfile.ZipFile(path) as archive:
                self.assertEqual(json.loads(archive.read('package.json'))['name'], name)
                self.assertNotIn('zipSHA256', json.loads(archive.read('package.json')))
                root = self.config['packages'][name]['legacyPath']
                for relative in archive.namelist():
                    if relative.startswith(('Runtime/', 'Editor/')) and not relative.endswith('.meta'):
                        expected[root + '/' + relative] = (archive.read(relative), archive.read(relative + '.meta'))
        actual = {}
        roots = {}
        with tarfile.open(self.output / self.metadata['unitypackage']) as archive:
            for entry in archive.getmembers():
                self.assertTrue(entry.isfile())
                if not entry.name.endswith('/pathname'):
                    continue
                prefix = entry.name.split('/')[0]
                path = archive.extractfile(entry).read().decode()
                self.assertTrue(path.startswith('Assets/MEISHIPop/')) if path != 'Assets/MEISHIPop' else None
                meta = archive.extractfile(prefix + '/asset.meta').read()
                self.assertEqual(build_release.guid(meta), prefix)
                try:
                    actual[path] = (archive.extractfile(prefix + '/asset').read(), meta)
                except KeyError:
                    roots[path] = prefix
        self.assertEqual(actual, expected)
        for item in self.config['packages'].values():
            self.assertEqual(roots[item['legacyPath']], item['legacyGuid'])

    def test_output_is_reproducible(self):
        before = {p.name: p.read_bytes() for p in self.output.iterdir()}
        build_release.build(self.output)
        self.assertEqual(before, {p.name: p.read_bytes() for p in self.output.iterdir()})

    def test_wrong_tag_is_rejected(self):
        with self.assertRaises(ValueError):
            build_release.build(self.output, 'v999.0.0')

    def test_listing_keeps_old_versions(self):
        previous = copy.deepcopy(self.metadata)
        previous['version'] = '0.1.0'
        for manifest in previous['packages'].values():
            manifest['version'] = '0.1.0'
        listing = make_listing(self.source, list(self.metadata['packages'].values()) + list(previous['packages'].values()))
        for package in listing['packages'].values():
            self.assertEqual(set(package['versions']), {self.metadata['version'], '0.1.0'})
        with self.assertRaises(ValueError):
            make_listing(self.source, [])
        with self.assertRaises(ValueError):
            make_listing(self.source, list(self.metadata['packages'].values()) * 2)


if __name__ == '__main__':
    unittest.main()
