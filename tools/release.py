"""Verify payload integrity or build the reproducible Windows distribution."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import manager


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['manifest', 'verify', 'build', 'package'])
    args = parser.parse_args()
    if args.action == 'manifest':
        info = manager.load(ROOT / 'module.json')
        info['files'] = {p.relative_to(ROOT / 'payload').as_posix(): manager.digest(p.read_bytes())
                         for p in sorted((ROOT / 'payload').rglob('*')) if p.is_file()}
        manager.write_json(ROOT / 'module.json', info)
        print('Refreshed', len(info['files']), 'payload hashes')
        return
    info = manager.manifest(ROOT)
    actual = {p.relative_to(ROOT / 'payload').as_posix() for p in (ROOT / 'payload').rglob('*') if p.is_file()}
    if actual != set(info['files']):
        raise RuntimeError('Payload contains unlisted files; review before refreshing manifest.')
    print('Verified', len(actual), 'payload files')
    if args.action not in ['build', 'package']:
        return
    build_inputs = {'manager': manager.digest((ROOT / 'manager.py').read_bytes()),
                    'manifest': manager.digest((ROOT / 'module.json').read_bytes())}
    if args.action == 'build':
        subprocess.run([sys.executable, '-m', 'PyInstaller', '--noconfirm', '--clean', '--onefile',
                    '--name', 'GambleChoiceManager', '--add-data', str(ROOT / 'payload') + ';payload',
                    '--add-data', str(ROOT / 'module.json') + ';.', '--distpath', str(ROOT / 'dist'),
                    '--workpath', str(ROOT / 'build'), '--specpath', str(ROOT / 'build'), str(ROOT / 'manager.py')],
                       cwd=ROOT, check=True)
        manager.write_json(ROOT / 'dist/exe-build.json', build_inputs)
    elif not (ROOT / 'dist/exe-build.json').exists() or manager.load(ROOT / 'dist/exe-build.json') != build_inputs:
        raise RuntimeError('Manager source/payload changed; rebuild the EXE before packaging.')
    dest = ROOT / 'dist' / ('GambleChoice-' + info['version'] + '-Windows.zip')
    includes = ['manager.py', 'module.json', 'README.md', 'LICENSE', 'CHANGELOG.md', '.gitignore', '.gitattributes',
                'payload', 'docs', 'examples', 'fixtures', 'analysis', 'tests', 'tools', '.github']
    with zipfile.ZipFile(dest, 'w', zipfile.ZIP_DEFLATED) as archive:
        archive.write(ROOT / 'dist/GambleChoiceManager.exe', 'GambleChoiceManager.exe')
        for name in includes:
            path = ROOT / name
            for source in ([path] if path.is_file() else sorted(path.rglob('*'))):
                if source.is_file() and '__pycache__' not in source.parts:
                    archive.write(source, source.relative_to(ROOT).as_posix())
    sha = hashlib.sha256(dest.read_bytes()).hexdigest()
    dest.with_suffix('.zip.sha256').write_text(sha + '  ' + dest.name + '\n', encoding='utf-8')
    print(json.dumps({'zip': str(dest), 'sha256': sha, 'bytes': dest.stat().st_size}))


if __name__ == '__main__':
    main()
