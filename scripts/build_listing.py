#!/usr/bin/env python3
"""Collect VPM ZIP releases from all repositories in source.json."""
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import re
import urllib.error
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
MAX_ZIP_BYTES = 256 * 1024 * 1024
MAX_MANIFEST_BYTES = 1024 * 1024
VERSION = re.compile(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:\+[0-9A-Za-z.-]+)?')


def fetch_bytes(url, api=False, limit=MAX_ZIP_BYTES):
    headers = {'User-Agent': 'pipipigiken-VPM-Listing'}
    if api and os.environ.get('GH_TOKEN'):
        headers['Authorization'] = 'Bearer ' + os.environ['GH_TOKEN']
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as response:
        data = response.read(limit + 1)
    if len(data) > limit:
        raise ValueError(f'Response exceeds size limit: {url}')
    return data


def fetch_json(url, api=False):
    return json.loads(fetch_bytes(url, api))


def manifest_from_zip(data, url):
    # Read only the root manifest; never extract arbitrary archive paths to disk.
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entries = [item for item in archive.infolist() if item.filename == 'package.json']
        if not entries:
            return None  # Documentation/source archives are not VPM packages.
        if len(entries) != 1 or entries[0].file_size > MAX_MANIFEST_BYTES:
            raise ValueError(f'Ambiguous or oversized package.json: {url}')
        with archive.open(entries[0]) as entry:
            manifest = json.loads(entry.read(MAX_MANIFEST_BYTES + 1))
    manifest['url'] = url
    manifest['zipSHA256'] = hashlib.sha256(data).hexdigest()
    return manifest


def releases(repository, fetch=fetch_json, download=fetch_bytes):
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise ValueError(f'Invalid GitHub repository: {repository}')
    page = 1
    while True:
        batch = fetch(f'https://api.github.com/repos/{repository}/releases?per_page=100&page={page}', api=True)
        if not batch:
            break
        for release in batch:
            if release['draft'] or release['prerelease']:
                continue
            for asset in release['assets']:
                if not asset['name'].lower().endswith('.zip'):
                    continue
                if asset.get('size', 0) > MAX_ZIP_BYTES:
                    raise ValueError(f'ZIP is too large: {asset["name"]}')
                manifest = manifest_from_zip(download(asset['browser_download_url']), asset['browser_download_url'])
                if manifest is not None:
                    yield manifest
        page += 1


def collect(source):
    manifests = []
    for repository in source.get('githubRepos', []):
        manifests.extend(releases(repository))
    # Also support the official template's explicit package ZIP URLs.
    for package in source.get('packages', []):
        for url in package['releases']:
            manifest = manifest_from_zip(fetch_bytes(url), url)
            if manifest is None or manifest['name'] != package['name']:
                raise ValueError(f'Explicit package does not match its ZIP: {url}')
            manifests.append(manifest)
    return manifests


def make_listing(source, manifests):
    listing = {key: source[key] for key in ('name', 'id', 'url')}
    listing['author'] = source['author']['name']
    listing['packages'] = {}
    for manifest in manifests:
        name, version = manifest['name'], manifest['version']
        if not re.fullmatch(r'[a-z0-9][a-z0-9._-]*', name) or not VERSION.fullmatch(version):
            raise ValueError(f'Invalid package name or stable version: {name}@{version}')
        if not re.fullmatch(r'[0-9a-f]{64}', manifest['zipSHA256']):
            raise ValueError('Missing ZIP integrity hash')
        if not manifest['url'].startswith('https://'):
            raise ValueError('Package downloads must use HTTPS')
        versions = listing['packages'].setdefault(name, {'versions': {}})['versions']
        if version in versions:
            raise ValueError(f'Duplicate package version: {name}@{version}')
        versions[version] = copy.deepcopy(manifest)
    if not listing['packages']:
        raise ValueError('Refusing to deploy an empty package listing; publish a release first')
    return listing


def check_preserved(previous, current, replacements=None):
    if previous['id'] != current['id'] or previous['url'] != current['url']:
        raise ValueError('Existing repository ID and URL must remain unchanged')
    for name, package in previous['packages'].items():
        for version, manifest in package['versions'].items():
            updated = current['packages'].get(name, {}).get('versions', {}).get(version)
            if updated is None:
                raise ValueError(f'Published package disappeared: {name}@{version}')
            if updated['url'] != manifest['url'] or updated['zipSHA256'] != manifest['zipSHA256']:
                transition = {
                    'from': {key: manifest[key] for key in ('url', 'zipSHA256')},
                    'to': {key: updated[key] for key in ('url', 'zipSHA256')},
                }
                if (replacements or {}).get(f'{name}@{version}') != transition:
                    raise ValueError(f'Published package was replaced: {name}@{version}')


if __name__ == '__main__':
    source = json.loads((ROOT / 'source.json').read_text())
    listing = make_listing(source, collect(source))
    migration_file = ROOT / 'release-migrations.json'
    replacements = json.loads(migration_file.read_text())['replacements'] if migration_file.exists() else {}
    try:
        previous = fetch_json(source['url'])
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
    else:
        check_preserved(previous, listing, replacements)
    content = json.dumps(listing, ensure_ascii=False, indent=2) + '\n'
    for filename in ('vpm.json', 'index.json'):
        (ROOT / 'Website' / filename).write_text(content)
    print('Listing:', {name: list(item['versions']) for name, item in listing['packages'].items()})
