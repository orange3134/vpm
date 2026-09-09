#!/usr/bin/env python3
"""Rebuild the VPM listing from all published releases; keep every released version."""
import argparse
import json
import os
from pathlib import Path
import re
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


def fetch_json(url, api=False):
    headers = {'User-Agent': 'MEISHI-Pop-Listing'}
    if api and os.environ.get('GH_TOKEN'):
        headers['Authorization'] = 'Bearer ' + os.environ['GH_TOKEN']
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=60) as response:
        return json.load(response)


def releases(repository):
    page = 1
    while True:
        batch = fetch_json(f'https://api.github.com/repos/{repository}/releases?per_page=100&page={page}', api=True)
        if not batch:
            break
        for release in batch:
            if release['draft'] or release['prerelease']:
                continue
            assets = {asset['name']: asset for asset in release['assets']}
            if 'vpm-release.json' not in assets:
                continue  # Ignore pre-automation releases unrelated to these packages.
            metadata = fetch_json(assets['vpm-release.json']['browser_download_url'])
            if release['tag_name'] != 'v' + metadata['version']:
                raise ValueError('Release tag and metadata mismatch')
            for manifest in metadata['packages'].values():
                filename = manifest['url'].rsplit('/', 1)[-1]
                if filename not in assets or assets[filename]['browser_download_url'] != manifest['url']:
                    raise ValueError('Release is missing a referenced VPM ZIP')
            if metadata['unitypackage'] not in assets:
                raise ValueError('Release is missing its Unitypackage')
            yield metadata
        page += 1


def make_listing(source, metadata, expected):
    listing = {key: source[key] for key in ('name', 'id', 'url')}
    listing['author'] = source['author']['name']
    listing['packages'] = {}
    for release in metadata:
        version = release['version']
        if not re.fullmatch(r'\d+\.\d+\.\d+', version):
            raise ValueError('Invalid release version')
        if set(release['packages']) != set(expected):
            raise ValueError('Unexpected public packages')
        for name, manifest in release['packages'].items():
            if manifest['name'] != name or manifest['version'] != version:
                raise ValueError('Manifest version mismatch')
            if not re.fullmatch(r'[0-9a-f]{64}', manifest['zipSHA256']):
                raise ValueError('Missing ZIP integrity hash')
            versions = listing['packages'].setdefault(name, {'versions': {}})['versions']
            if version in versions:
                raise ValueError('Duplicate release version')
            versions[version] = manifest
    if not listing['packages']:
        raise ValueError('Refusing to deploy an empty package listing; publish a release first')
    return listing


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--metadata', type=Path, nargs='+', help='Offline validation instead of GitHub API')
    args = parser.parse_args()
    source = json.loads((ROOT / 'source.json').read_text())
    config = json.loads((ROOT / 'distribution.json').read_text())
    metadata = [json.loads(p.read_text()) for p in args.metadata] if args.metadata else list(releases(config['repository']))
    listing = make_listing(source, metadata, config['packages'])
    content = json.dumps(listing, ensure_ascii=False, indent=2) + '\n'
    for name in ('vpm.json', 'index.json'):
        (ROOT / 'Website' / name).write_text(content)
    print('Listing:', {name: list(item['versions']) for name, item in listing['packages'].items()})
