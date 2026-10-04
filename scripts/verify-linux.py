#!/usr/bin/env python3
"""Extract and smoke-test locally built Linux packages without installing them."""
import argparse
import hashlib
import os
from pathlib import Path
import stat
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def run(*args, **kwargs):
    return subprocess.run(list(map(str, args)), check=True, **kwargs)

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='0.2.3')
    parser.add_argument('--dist', type=Path, default=ROOT / 'dist')
    args = parser.parse_args()
    dist = args.dist.resolve()
    env = os.environ | {'DOTNET_ROOT': '/nonexistent-gmmt-test-runtime', 'DOTNET_ROOT_X64': '/nonexistent-gmmt-test-runtime'}
    run('sha256sum', '-c', 'SHA256SUMS', cwd=dist)
    with tempfile.TemporaryDirectory(prefix='gmmt-package-test-') as tmp:
        work = Path(tmp)
        image = dist / f'GMMT-{args.version}-x86_64.AppImage'
        run(image, '--appimage-extract-and-run', '--cli', '--help', env=env)
        run(image, '--appimage-extract', cwd=work, stdout=subprocess.DEVNULL)
        tar = work / 'tar'
        deb = work / 'deb'
        rpm = work / 'rpm'
        for path in (tar, deb, rpm):
            path.mkdir()
        run('tar', '--zstd', '-xf', dist / f'gmmt-{args.version}-linux-x86_64.tar.zst', '-C', tar)
        run('dpkg-deb', '-x', dist / f'gmmt_{args.version}_amd64.deb', deb)
        rpm_file = dist / f'gmmt-{args.version}-1.x86_64.rpm'
        run('rpm', '-qpi', rpm_file)
        stream = subprocess.Popen(['rpm2cpio', str(rpm_file)], stdout=subprocess.PIPE)
        run('cpio', '-idm', '--quiet', '--no-absolute-filenames', stdin=stream.stdout, cwd=rpm)
        stream.stdout.close()
        if stream.wait() != 0:
            raise RuntimeError('rpm2cpio failed')
        variants = {
            'AppImage': work / 'squashfs-root/usr/lib/gmmt',
            'tar.zst': tar / 'GMMT.AppDir/usr/lib/gmmt',
            'deb': deb / 'opt/gmmt',
            'rpm': rpm / 'opt/gmmt',
        }
        baseline = None
        for name, payload in variants.items():
            files = {str(path.relative_to(payload)): digest(path) for path in payload.rglob('*') if path.is_file()}
            assert files and (baseline is None or files == baseline), f'payload mismatch: {name}'
            baseline = files
            assert not any(path.lower().endswith(('.win', '.unx', '.xdelta')) for path in files), 'unexpected game input'
            for executable in ('gmmt', 'gmmt-desktop'):
                assert (payload / executable).stat().st_mode & stat.S_IXUSR, f'not executable: {name}/{executable}'
            run(payload / 'gmmt', '--help', env=env, stdout=subprocess.DEVNULL)
            for native in ('gmmt-desktop', 'libSkiaSharp.so', 'libHarfBuzzSharp.so', 'Magick.Native-Q8-x64.dll.so'):
                dependency_result = run('ldd', payload / native, capture_output=True, text=True)
                assert 'not found' not in dependency_result.stdout, dependency_result.stdout
            if name in ('deb', 'rpm'):
                package_root = payload.parents[1]
                for launcher in ('gmmt', 'gmmt-cli'):
                    assert (package_root / 'usr/bin' / launcher).stat().st_mode & stat.S_IXUSR
                run('desktop-file-validate', package_root / 'usr/share/applications/gmmt.desktop')
            print(f'PASS {name}: identical payload, standalone CLI, executable files, native dependencies', flush=True)
    print('All four package smoke tests passed. No package was installed.')

if __name__ == '__main__':
    main()
