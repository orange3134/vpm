#!/usr/bin/env python3
"""Build deterministic VPM ZIPs and a GUID-preserving Unitypackage without Unity."""
import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path
import re
import tarfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
ALLOWED = {'.cs', '.asmdef', '.json', '.meta', '.md', '.txt'}


def guid(meta):
    match = re.search(rb'^guid: ([0-9a-f]{32})\s*$', meta, re.MULTILINE)
    if not match:
        raise ValueError('Missing or invalid asset GUID')
    return match[1].decode()


def folder_meta(value):
    return f'fileFormatVersion: 2\nguid: {value}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'.encode()


def tar_entry(archive, path, data):
    info = tarfile.TarInfo(path)
    info.size = len(data)
    info.mode = 0o644
    info.mtime = 0
    archive.addfile(info, io.BytesIO(data))


def build(output, tag=None):
    config = json.loads((ROOT / 'distribution.json').read_text())
    output.mkdir(parents=True, exist_ok=True)
    manifests = {}
    sources = {}
    seen = set()
    for name in config['packages']:
        source = ROOT / 'Packages' / name
        manifest = json.loads((source / 'package.json').read_text())
        if manifest['name'] != name:
            raise ValueError('Unexpected package name')
        if not re.fullmatch(r'\d+\.\d+\.\d+', manifest['version']):
            raise ValueError('Release version must be x.y.z')
        files = sorted(p for p in source.rglob('*') if p.is_file())
        for p in source.rglob('*'):
            if p.is_symlink() or (p.is_file() and p.suffix not in ALLOWED):
                raise ValueError(f'Unexpected package content: {p}')
        for file in files:
            if file.suffix == '.meta':
                value = guid(file.read_bytes())
                if value in seen:
                    raise ValueError(f'Duplicate GUID: {file}')
                seen.add(value)
            elif not file.with_name(file.name + '.meta').is_file():
                raise ValueError(f'Missing meta: {file}')
        manifests[name] = manifest
        sources[name] = (source, files)
    versions = {p['version'] for p in manifests.values()}
    if len(versions) != 1:
        raise ValueError('Package version mismatch')
    version = versions.pop()
    if tag is not None and tag != 'v' + version:
        raise ValueError(f'Tag {tag} does not match package version {version}')
    exporter = manifests['com.avatarnamecard.exporter']
    if exporter['vpmDependencies']['com.avatarnamecard.avatar-package'] != version:
        raise ValueError('Shared format dependency mismatch')
    for name, manifest in manifests.items():
        filename = f'{name}-{version}.zip'
        expected_url = f"https://github.com/{config['repository']}/releases/download/v{version}/{filename}"
        if manifest['url'] != expected_url:
            raise ValueError('Outdated release URL; run sync_from_app.py')
        migration = config['packages'][name]
        if manifest['legacyFolders'] != {migration['legacyPath']: migration['legacyGuid']}:
            raise ValueError('Unitypackage migration mismatch')
        source, files = sources[name]
        with zipfile.ZipFile(output / filename, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            for file in files:
                info = zipfile.ZipInfo(file.relative_to(source).as_posix(), (2020, 1, 1, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o100644 << 16
                archive.writestr(info, file.read_bytes())
        manifest['zipSHA256'] = hashlib.sha256((output / filename).read_bytes()).hexdigest()

    # Unity's package archive uses one GUID directory per asset (asset/asset.meta/pathname).
    # Never follow Unity IncludeDependencies: SDKs, shaders and avatars are not bundled.
    unity_name = f'MEISHI-Pop-Exporter-{version}.unitypackage'
    unity_assets = {}
    def add(path, meta, data=None):
        value = guid(meta)
        if value in unity_assets:
            raise ValueError(f'Duplicate Unitypackage GUID: {path}')
        unity_assets[value] = (path, meta, data)
    add('Assets/MEISHIPop', folder_meta(config['unityRootGuid']))
    for name, (source, files) in sources.items():
        migration = config['packages'][name]
        legacy = migration['legacyPath']
        add(legacy, folder_meta(migration['legacyGuid']))
        for meta in files:
            if meta.suffix != '.meta':
                continue
            asset = meta.with_suffix('')
            relative = asset.relative_to(source)
            if relative.parts[0] not in ('Runtime', 'Editor'):
                continue
            add(legacy + '/' + relative.as_posix(), meta.read_bytes(), asset.read_bytes() if asset.is_file() else None)
    with (output / unity_name).open('wb') as raw:
        with gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode='w', format=tarfile.USTAR_FORMAT) as archive:
                for value, (path, meta, data) in sorted(unity_assets.items()):
                    tar_entry(archive, value + '/pathname', path.encode())
                    tar_entry(archive, value + '/asset.meta', meta)
                    if data is not None:
                        tar_entry(archive, value + '/asset', data)
    metadata = {'version': version, 'packages': manifests, 'unitypackage': unity_name}
    (output / 'vpm-release.json').write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + '\n')
    paths = [output / f'{name}-{version}.zip' for name in manifests] + [output / unity_name, output / 'vpm-release.json']
    (output / 'SHA256SUMS.txt').write_text(''.join(f'{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.name}\n' for p in paths))
    print(f'Built {version}: 2 VPM ZIPs, Unitypackage ({len(unity_assets)} assets), metadata and checksums')
    return metadata


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'dist')
    parser.add_argument('--tag')
    args = parser.parse_args()
    build(args.output, args.tag)
