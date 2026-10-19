#!/usr/bin/env python3
"""Local-only API test installation: backup, replace known DLLs, verify, rollback on error."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
from datetime import datetime, timezone

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('game', type=Path)
parser.add_argument('--dry-run', action='store_true')
args = parser.parse_args()
repo = Path(__file__).resolve().parent.parent
game = args.game.resolve()
plugins = game / 'BepInEx/plugins'
if not plugins.is_dir() or not (game / 'BepInEx/interop/Assembly-CSharp.dll').is_file():
    raise SystemExit('Not a configured BepInEx IL2CPP game directory.')


def ensure_closed():
    for proc in Path('/proc').iterdir():
        if not proc.name.isdigit():
            continue
        try:
            comm = (proc / 'comm').read_text().strip()
            if 'nivalis' in comm.lower():
                raise SystemExit(f'Close Nivalis before installing (PID {proc.name}, {comm}).')
        except (OSError, UnicodeError):
            continue


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


ensure_closed()
sources = {
    'Nivalis.Cigarette.dll': repo / 'bin/api/Nivalis.Cigarette.dll',
    'Nivalis.Cigarette.Api.dll': repo / 'bin/api/Nivalis.Cigarette.Api.dll',
    'Nivalis.Cigarette.ApiTest.dll': repo / 'dev/api-test/bin/Nivalis.Cigarette.ApiTest.dll',
}
for source in sources.values():
    if not source.is_file():
        raise SystemExit(f'Missing build output: {source}; run make build-api-test first.')
known = {name.lower() for name in sources} | {'cigarette.dll'}
old = sorted(p for p in plugins.rglob('*') if p.is_file() and p.name.lower() in known)
if any(p.is_symlink() or p.resolve().parent != p.parent.resolve() for p in old):
    raise SystemExit('Unexpected symlink in an affected DLL path; inspect before installing.')
print('Game:', game)
for path in old:
    print('Replace/remove old:', path.relative_to(plugins), digest(path))
for name, source in sources.items():
    print('Install:', name, digest(source))
if args.dry_run:
    raise SystemExit(0)

ensure_closed()
backup = repo / 'backups' / ('api-test-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
backup.mkdir(parents=True)
manifest = {'game': str(game), 'old': {}, 'installed': {}}
for path in old:
    relative = path.relative_to(plugins)
    target = backup / 'plugins' / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, target)
    if digest(path) != digest(target):
        raise SystemExit(f'Backup verification failed: {path}')
    manifest['old'][str(relative)] = digest(target)
(backup / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
originals = set(old)
new_targets = [plugins / name for name in sources]
staged = []
try:
    for name, source in sources.items():
        temporary = plugins / ('.api-test-install-' + name + '.tmp')
        staged.append(temporary)
        shutil.copy2(source, temporary)
        if digest(temporary) != digest(source):
            raise RuntimeError('Staged DLL checksum mismatch: ' + name)
        os.replace(temporary, plugins / name)
    for path in old:
        if path not in new_targets:
            path.unlink()
    installed = sorted(p for p in plugins.rglob('*') if p.is_file() and p.name.lower() in known)
    if set(installed) != set(new_targets):
        raise RuntimeError('Unexpected duplicate/legacy Cigarette DLL remains.')
    for name, source in sources.items():
        actual = digest(plugins / name)
        if actual != digest(source):
            raise RuntimeError('Installed checksum mismatch: ' + name)
        manifest['installed'][name] = actual
        print('Verified installed:', name, actual)
    (backup / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
except Exception:
    for path in new_targets:
        if path not in originals:
            path.unlink(missing_ok=True)
    for path in old:
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(backup / 'plugins' / path.relative_to(plugins), path)
    raise
finally:
    for path in staged:
        path.unlink(missing_ok=True)
print('Backup:', backup)
print('Installation verified: exactly one API provider, API library and test DLL; no legacy Cigarette DLL.')
