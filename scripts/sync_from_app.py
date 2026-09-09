#!/usr/bin/env python3
"""Copy only the two exporter packages, never app assets or repository history."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[1]
SOURCES = {
    'com.avatarnamecard.avatar-package': 'Packages/com.avatarnamecard.avatar-package',
    'com.avatarnamecard.exporter': 'Exporter~/com.avatarnamecard.exporter',
}
ALLOWED = {'.cs', '.asmdef', '.json', '.meta', '.md', '.txt'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True, help='Private app checkout (read only)')
    args = parser.parse_args()
    config = json.loads((ROOT / 'distribution.json').read_text())
    # Validate every source before modifying the public snapshot.
    snapshots = {}
    for name, relative in SOURCES.items():
        source = args.source.resolve() / relative
        files = sorted(source.rglob('*'))
        if not (source / 'package.json').is_file():
            raise ValueError(f'Missing package: {name}')
        for file in files:
            if file.is_symlink() or (file.is_file() and file.suffix not in ALLOWED):
                raise ValueError(f'Unexpected source file: {file}')
        manifest = json.loads((source / 'package.json').read_text())
        if manifest['name'] != name:
            raise ValueError(f'Package name mismatch: {name}')
        snapshots[name] = (source, files, manifest)
    versions = {item[2]['version'] for item in snapshots.values()}
    if len(versions) != 1:
        raise ValueError('Package versions must match')
    version = versions.pop()
    for name, (source, files, manifest) in snapshots.items():
        destination = ROOT / 'Packages' / name
        destination.mkdir(parents=True, exist_ok=True)
        expected = {file.relative_to(source) for file in files if file.is_file()}
        for file in destination.rglob('*'):
            if file.is_file() and file.relative_to(destination) not in expected:
                file.unlink()  # Only stale files in these two generated package snapshots.
        for file in files:
            if file.is_file():
                target = destination / file.relative_to(source)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(file, target)
        manifest.update(config['manifestDefaults'])
        manifest.pop('dependencies', None)  # VPM dependencies must not use the UPM registry.
        manifest['url'] = f"https://github.com/{config['repository']}/releases/download/v{version}/{name}-{version}.zip"
        manifest['legacyFolders'] = {config['packages'][name]['legacyPath']: config['packages'][name]['legacyGuid']}
        if name.endswith('.exporter'):
            manifest['vpmDependencies'] = {
                'com.avatarnamecard.avatar-package': version,
                'com.vrchat.avatars': '>=3.10.5 <3.11.0-a',
                'nadena.dev.ndmf': '>=1.14.8 <2.0.0-a',
            }
        (destination / 'package.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    print(f'Synced exporter-only packages {version}. Review git diff before publishing.')


if __name__ == '__main__':
    main()
