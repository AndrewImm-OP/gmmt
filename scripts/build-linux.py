#!/usr/bin/env python3
"""Build self-contained Linux x86_64 GMMT packages without root or games."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def run(*args, **kwargs):
    print('+ ' + ' '.join(map(str, args)), flush=True)
    return subprocess.run(list(map(str, args)), check=True, **kwargs)

def write(path, text, executable=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text)
    path.chmod(0o755 if executable else 0o644)

def copy_tree(source, destination):
    shutil.copytree(source, destination, dirs_exist_ok=True)
    for path in destination.rglob('*'):
        if path.is_file():
            path.chmod(0o755 if path.name in ('gmmt', 'gmmt-desktop') else 0o644)
        elif path.is_dir():
            path.chmod(0o755)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='0.2.0')
    parser.add_argument('--output', type=Path, default=ROOT / 'dist')
    parser.add_argument('--appimagetool', default=os.environ.get('APPIMAGETOOL', 'appimagetool'))
    parser.add_argument('--runtime-file', type=Path, help='Offline AppImage runtime; otherwise appimagetool downloads it')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+', args.version):
        parser.error('version must be X.Y.Z')
    for tool in ('dotnet', 'dpkg-deb', 'rpmbuild', 'tar', 'zstd', args.appimagetool):
        if not shutil.which(tool):
            parser.error(f'missing build tool: {tool}')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    env = os.environ.copy()
    env.setdefault('DOTNET_CLI_HOME', str(ROOT / 'experiments/build/dotnet-home'))
    env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    # /tmp supports executable permissions and symlinks even when checkout is exFAT.
    with tempfile.TemporaryDirectory(prefix='gmmt-build-') as temp:
        work = Path(temp)
        env["NUGET_HTTP_CACHE_PATH"] = str(work / "nuget-http-cache")
        appdir = work / 'GMMT.AppDir'
        payload = appdir / 'usr/lib/gmmt'
        for project in ('Gmmt.Desktop', 'Gmmt.Cli'):
            publish = work / project
            run('dotnet', 'publish', ROOT / f'src/{project}/{project}.csproj', '-c', 'Release',
                '-r', 'linux-x64', '--self-contained', 'true', '-m:1', '-p:NuGetAudit=false',
                '-p:DebugType=None', '-p:DebugSymbols=false', '-p:Version=' + args.version,
                '--packages', ROOT / 'experiments/2026-10-04/nuget-packages', '-o', publish, cwd=ROOT, env=env)
            copy_tree(publish, payload)
        license_dir = payload / 'licenses'
        license_dir.mkdir()
        shutil.copyfile(ROOT / 'extern/UndertaleModTool/LICENSE.txt', license_dir / 'UndertaleModTool-GPL-3.0.txt')
        shutil.copyfile(ROOT / 'extern/UndertaleModTool/Underanalyzer/LICENSE', license_dir / 'Underanalyzer-MPL-2.0.txt')
        # Preserve package-supplied notices, not game data or locally registered runners.
        cache = ROOT / 'experiments/2026-10-04/nuget-packages'
        notices = []
        dependencies = set()
        for deps in payload.glob('*.deps.json'):
            for name, details in json.loads(deps.read_text())['libraries'].items():
                if details['type'] == 'package':
                    dependencies.add(name.lower())
        for pack in (cache / 'microsoft.netcore.app.runtime.linux-x64').glob('*'):
            if pack.is_dir():
                dependencies.add('microsoft.netcore.app.runtime.linux-x64/' + pack.name)
        for dependency in sorted(dependencies):
            version = cache / dependency
            if not version.is_dir():
                continue
            for item in version.iterdir():
                if item.is_file() and re.match(r'(?i)(license|notice|third.?party)', item.name):
                    target = license_dir / f'{dependency.replace("/", "-")}-{item.name}'
                    shutil.copyfile(item, target)
                    notices.append(target.name)
        write(payload / 'THIRD-PARTY-NOTICES.txt', 'Bundled third-party licenses are in licenses/.\n'
              'GMMT source: https://github.com/AndrewImm-OP/gmmt\n'
              'GMMT has no separately assigned license yet.\n' + '\n'.join(notices) + '\n')
        write(appdir / 'AppRun', '''#!/bin/sh
set -eu
APPDIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ "${1:-}" = "--cli" ]; then
    shift
    exec "$APPDIR/usr/lib/gmmt/gmmt" "$@"
fi
exec "$APPDIR/usr/lib/gmmt/gmmt-desktop" "$@"
''', True)
        desktop = (ROOT / 'packaging/gmmt.desktop').read_text()
        write(appdir / 'gmmt.desktop', desktop.replace('Exec=gmmt', 'Exec=AppRun'))
        shutil.copyfile(ROOT / 'packaging/gmmt.svg', appdir / 'gmmt.svg')
        shutil.copyfile(ROOT / 'packaging/gmmt.svg', appdir / '.DirIcon')
        write(appdir / 'README.txt', f'''GMMT {args.version} / Linux x86_64
Launch: ./AppRun
CLI: ./AppRun --cli --help
.NET runtime is included. Desktop requires X11/XWayland, Mesa/OpenGL,
fontconfig, standard glibc/C++ libraries, ICU and OpenSSL 3.
xdelta patches also require xdelta3 from your distribution.
No games, mods, saves or game runners are included.
Build and test instructions: https://github.com/AndrewImm-OP/gmmt
''')
        run('tar', '--zstd', '-cf', output / f'gmmt-{args.version}-linux-x86_64.tar.zst', '-C', work, appdir.name)
        image_env = env | {'ARCH': 'x86_64', 'VERSION': args.version, 'APPIMAGE_EXTRACT_AND_RUN': '1'}
        image_args = [args.appimagetool, '--no-appstream', '--mksquashfs-opt', '-processors', '--mksquashfs-opt', '2']
        if args.runtime_file:
            image_args += ['--runtime-file', str(args.runtime_file.resolve())]
        run(*image_args, appdir, output / f'GMMT-{args.version}-x86_64.AppImage', env=image_env)
        stage = work / 'package'
        copy_tree(payload, stage / 'opt/gmmt')
        write(stage / 'usr/bin/gmmt', '#!/bin/sh\nexec /opt/gmmt/gmmt-desktop "$@"\n', True)
        write(stage / 'usr/bin/gmmt-cli', '#!/bin/sh\nexec /opt/gmmt/gmmt "$@"\n', True)
        write(stage / 'usr/share/applications/gmmt.desktop', desktop)
        write(stage / 'usr/share/icons/hicolor/scalable/apps/gmmt.svg', (ROOT / 'packaging/gmmt.svg').read_text())
        write(stage / 'usr/share/doc/gmmt/README', (appdir / 'README.txt').read_text())
        deb = work / 'deb'
        copy_tree(stage, deb)
        # copy_tree marks only apphosts executable; restore installed shell wrappers.
        for name in ('gmmt', 'gmmt-cli'):
            (deb / 'usr/bin' / name).chmod(0o755)
        write(deb / 'DEBIAN/control', f'''Package: gmmt
Version: {args.version}
Section: utils
Priority: optional
Architecture: amd64
Maintainer: AndrewImm-OP <AndrewImm-OP@users.noreply.github.com>
Installed-Size: {sum(p.stat().st_size for p in stage.rglob('*') if p.is_file()) // 1024}
Depends: libc6 (>= 2.27), libgcc-s1, libstdc++6, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6, libgl1, libicu70 | libicu72 | libicu74 | libicu76 | libicu78, libssl3 | libssl3t64, zlib1g
Recommends: xdelta3
Homepage: https://github.com/AndrewImm-OP/gmmt
Description: Linux package builder for GameMaker mods
 Preserves mod archives and selects locally supplied Linux runners.
 Includes a desktop interface, command-line tool and .NET runtime.
''')
        run('dpkg-deb', '--root-owner-group', '--build', deb, output / f'gmmt_{args.version}_amd64.deb')
        rpm = work / 'rpm'
        for directory in ('BUILD', 'BUILDROOT', 'RPMS', 'SOURCES', 'SPECS', 'SRPMS'):
            (rpm / directory).mkdir(parents=True)
        spec = rpm / 'SPECS/gmmt.spec'
        write(spec, f'''Name: gmmt
Version: {args.version}
Release: 1
Summary: Linux package builder for GameMaker mods
License: LicenseRef-Unspecified AND GPL-3.0-only AND MPL-2.0 AND MIT
URL: https://github.com/AndrewImm-OP/gmmt
BuildArch: x86_64
AutoReqProv: no
Requires: glibc >= 2.27, libgcc, libstdc++, fontconfig, freetype, libX11, libICE, libSM, libglvnd-glx, libicu, openssl-libs, zlib
Recommends: xdelta3
%description
Desktop and CLI to package GameMaker mods using local Linux runners.
The .NET runtime is included. No games or game runners are bundled.
%prep
%build
%install
mkdir -p %{{buildroot}}
cp -a '{stage}/.' %{{buildroot}}/
%files
/opt/gmmt
/usr/bin/gmmt
/usr/bin/gmmt-cli
/usr/share/applications/gmmt.desktop
/usr/share/icons/hicolor/scalable/apps/gmmt.svg
/usr/share/doc/gmmt
''')
        run('rpmbuild', '-bb', '--define', f'_topdir {rpm}', '--define', '_build_id_links none',
            '--define', '__os_install_post %{nil}', spec)
        for package in (rpm / 'RPMS').rglob('*.rpm'):
            shutil.copyfile(package, output / package.name)
        provenance = {'version': args.version, 'rid': 'linux-x64', 'selfContained': True,
                      'commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                      'dirty': bool(subprocess.check_output(['git', '-c', 'core.filemode=false', 'status', '--porcelain'], cwd=ROOT)),
                      'dotnet': subprocess.check_output(['dotnet', '--version'], env=env, text=True).strip()}
        write(output / 'build-info.json', json.dumps(provenance, indent=2) + '\n')
    checksums = []
    for path in sorted(output.iterdir()):
        if path.is_file() and path.name != 'SHA256SUMS':
            checksums.append(f'{hashlib.file_digest(path.open("rb"), "sha256").hexdigest()}  {path.name}')
    write(output / 'SHA256SUMS', '\n'.join(checksums) + '\n')
    print('Packages: ' + str(output))

if __name__ == '__main__':
    main()
