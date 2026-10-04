# Linux distribution builds

## Scope

Version 0.2.3 ships Linux x86_64 desktop and CLI as self-contained .NET 10 applications. AppImage, deb, rpm and tar.zst are generated from the same merged publish directory. No game archive, mod, runner, catalog, save or experiment resource is read by the packaging script.

`python3 scripts/build-linux.py --version 0.2.3` runs `dotnet publish` sequentially for the desktop and CLI, with `linux-x64`, self-contained true, Release and no debug symbols. It includes third-party license notices, the desktop entry and the project SVG icon. Intermediate files and NuGet HTTP cache live in a disposable `/tmp/gmmt-build-*` directory; the normal NuGet package cache remains under ignored `experiments/`.

Build requirements: .NET 10 SDK, Python 3.11+, dpkg-deb, rpmbuild, tar, zstd and appimagetool. The first publish requires NuGet access. appimagetool may download its official runtime unless `--runtime-file` supplies one. Override its executable with `--appimagetool` or `APPIMAGETOOL`.

## Outputs

- `GMMT-0.2.3-x86_64.AppImage`: desktop default; `--cli` selects the command line. Supports the runtime's `--appimage-extract-and-run` fallback on systems without FUSE.
- `gmmt_0.2.3_amd64.deb`: `/opt/gmmt` payload; `/usr/bin/gmmt` and `/usr/bin/gmmt-cli` launchers, application-menu entry and scalable icon. Uses root ownership without root installation.
- `gmmt-0.2.3-1.x86_64.rpm`: same installed layout. Dependency metadata is explicit. Automatic RPM dependency generation is disabled to avoid host-specific dependencies and bundling accidental build-host ABI requirements in metadata; native dependency support still needs target-distro tests.
- `gmmt-0.2.3-linux-x86_64.tar.zst`: portable `GMMT.AppDir`, launched by `AppRun`, CLI with `AppRun --cli`.
- `SHA256SUMS`: hashes of all outputs, including build-info.json.
- `build-info.json`: version, RID, self-contained flag, SDK version, source commit and dirty-worktree flag. These are rebuild instructions/provenance, not a claim of bit-identical reproducible artifacts.

## Dependencies and limitations

The .NET runtime is bundled. OS libraries are not: glibc/C++ runtime, X11/XWayland, OpenGL/Mesa, fontconfig/freetype, ICU, OpenSSL 3 and zlib are needed. The patch workflow invokes the system `xdelta3`; deb/rpm recommend it. None of this supplies the dependencies of a selected game runner.

Only x86_64 is built. No ARM, musl, Windows, macOS or Flatpak artifact is claimed. deb/rpm are unsigned. Names of native dependencies can differ between distributions; the current validation host is CachyOS. Do not claim a clean Debian/Fedora installation test until one is actually recorded.

## Validation recipe

```sh
cd dist
sha256sum -c SHA256SUMS
./GMMT-0.2.3-x86_64.AppImage --appimage-extract-and-run --cli --help
dpkg-deb --info gmmt_0.2.3_amd64.deb
rpm -qpi gmmt-0.2.3-1.x86_64.rpm
```

Extract each format into separate temporary directories and run its CLI apphost with `DOTNET_ROOT` pointing to a nonexistent directory. Check that the binaries and shell launchers are executable, icons and desktop entries exist, no game files are present, and shared publish content matches by SHA256. Launch the desktop from AppImage extraction mode and exercise an error path and catalog reading. Check native library dependencies with `ldd` on the apphost, libSkiaSharp.so, libHarfBuzzSharp.so and the ImageMagick native library. Record exact results and outstanding target-platform work in the development journal.

Do not install test packages into the user's system merely to check them; extraction is sufficient for local smoke tests. Test actual installation/dependency resolution in a clean VM/container for each target distro before advertising that distro as verified.

## Sources

- [.NET deployment models](https://learn.microsoft.com/en-us/dotnet/core/deploying/).
- [AppImage architecture / AppRun](https://docs.appimage.org/reference/architecture.html).
- [AppImage without FUSE](https://docs.appimage.org/user-guide/troubleshooting/fuse.html).
- [appimagetool](https://github.com/AppImage/appimagetool).

Automated extraction check: `python3 scripts/verify-linux.py --version 0.2.3`. Requires rpm2cpio, cpio, desktop-file-validate and ldd in addition to extraction tools. It compares every payload file across all four formats, checks shell launchers, and runs the CLI with an invalid DOTNET_ROOT.
