import copy
import hashlib
import io
import json
import unittest
import zipfile

from build_listing import check_preserved, make_listing, manifest_from_zip, releases

SOURCE = {'name': 'Test tools', 'id': 'jp.example.vpm', 'url': 'https://example.jp/vpm.json', 'author': {'name': 'Test'}}


def manifest(name, version):
    return {'name': name, 'version': version, 'displayName': name, 'url': f'https://example.jp/{name}-{version}.zip', 'zipSHA256': 'a' * 64}


def package_zip(value, nested=False):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w') as archive:
        archive.writestr('source/package.json' if nested else 'package.json', json.dumps(value))
    return buffer.getvalue()


class MultiRepositoryTests(unittest.TestCase):
    def test_independent_tools_and_versions(self):
        one = manifest('jp.example.first', '1.2.3')
        two = manifest('jp.example.second', '0.1.0')
        old = manifest('jp.example.first', '1.0.0')
        listing = make_listing(SOURCE, [one, two, old])
        self.assertEqual(set(listing['packages']), {'jp.example.first', 'jp.example.second'})
        self.assertEqual(set(listing['packages']['jp.example.first']['versions']), {'1.2.3', '1.0.0'})
        self.assertEqual(listing['packages']['jp.example.second']['versions']['0.1.0'], two)

    def test_standard_zip_without_custom_metadata(self):
        value = manifest('jp.example.first', '1.2.3')
        value['url'] = 'https://stale.example.com/old.zip'
        data = package_zip(value)
        actual = manifest_from_zip(data, 'https://example.jp/current.zip')
        self.assertEqual(actual['url'], 'https://example.jp/current.zip')
        self.assertEqual(actual['zipSHA256'], hashlib.sha256(data).hexdigest())
        self.assertIsNone(manifest_from_zip(package_zip(value, nested=True), 'https://example.jp/source.zip'))

    def test_all_pages_excluding_drafts_and_prereleases(self):
        value = manifest('jp.example.first', '1.2.3')
        def release(draft=False, prerelease=False):
            return {'draft': draft, 'prerelease': prerelease, 'assets': [
                {'name': 'tool.zip', 'browser_download_url': 'https://example.jp/tool.zip'},
                {'name': 'tool.unitypackage', 'browser_download_url': 'https://example.jp/tool.unitypackage'}]}
        pages = iter([[release(True), release(prerelease=True)], [release()], []])
        urls, downloads = [], []
        def fetch(url, api=False):
            self.assertTrue(api)
            urls.append(url)
            return next(pages)
        def download(url):
            downloads.append(url)
            return package_zip(value)
        result = list(releases('owner/tool', fetch, download))
        self.assertEqual(len(result), 1)
        self.assertTrue(urls[-1].endswith('page=3'))
        self.assertEqual(downloads, ['https://example.jp/tool.zip'])

    def test_duplicates_fail_instead_of_overwriting(self):
        item = manifest('jp.example.first', '1.2.3')
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            make_listing(SOURCE, [item, item])

    def test_existing_versions_and_identity_are_preserved(self):
        old = manifest('jp.example.first', '1.2.3')
        previous = make_listing(SOURCE, [old])
        current = make_listing(SOURCE, [old, manifest('jp.example.second', '0.1.0')])
        check_preserved(previous, current)
        for modification in ('remove', 'url', 'hash', 'identity'):
            broken = copy.deepcopy(current)
            if modification == 'remove':
                del broken['packages']['jp.example.first']
            elif modification == 'identity':
                broken['id'] = 'new-id'
            else:
                item = broken['packages']['jp.example.first']['versions']['1.2.3']
                item['url' if modification == 'url' else 'zipSHA256'] = 'changed'
            with self.assertRaises(ValueError):
                check_preserved(previous, broken)

    def test_approved_migration_requires_exact_old_and_new_hashes(self):
        old = manifest('jp.example.first', '1.2.3')
        new = copy.deepcopy(old)
        new['url'] = 'https://example.jp/new-location.zip'
        new['zipSHA256'] = 'b' * 64
        previous = make_listing(SOURCE, [old])
        current = make_listing(SOURCE, [new])
        transition = {'jp.example.first@1.2.3': {
            'from': {key: old[key] for key in ('url', 'zipSHA256')},
            'to': {key: new[key] for key in ('url', 'zipSHA256')},
        }}
        with self.assertRaises(ValueError):
            check_preserved(previous, current)
        check_preserved(previous, current, transition)
        for side in ('from', 'to'):
            incorrect = copy.deepcopy(transition)
            incorrect['jp.example.first@1.2.3'][side]['zipSHA256'] = 'c' * 64
            with self.assertRaises(ValueError):
                check_preserved(previous, current, incorrect)
        with self.assertRaises(ValueError):
            check_preserved(current, previous, transition)


if __name__ == '__main__':
    unittest.main()
