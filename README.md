# GMMT

Build a separate native Linux package from a GameMaker mod and a locally supplied compatible Linux runner. The new program keeps the game archive byte-for-byte intact. The previous GMS2-to-GMS1/diff/transplant experiment is preserved in [old/translation](old/translation/README.md).

## Build and open

Requires .NET 10 SDK and Git; xdelta3 is needed for patch input. Initialize the pinned dependencies and build:

```sh
git clone --recurse-submodules https://github.com/AndrewImm-OP/gmmt.git
cd gmmt
bash setup.sh
dotnet src/Gmmt.Desktop/bin/Debug/net10.0/gmmt-desktop.dll
```

The desktop has two tabs: add local runners with their reference archives, then analyze/build a mod package. Select a prepared data.win/game.unx or an xdelta patch with its clean Windows baseline. Add original external resources first and mod resources afterward. Supply a new output folder. Nothing is written into the installed game.

## Runner catalog

A runner is registered with an ELF architecture, SHA256, and metadata/hash of a reference archive known by the user to run with it. Registration does not execute or certify the runner. Automatic selection requires matching engine and bytecode metadata. An exact reference-archive checksum is preferred; multiple remaining candidates require explicit selection. Changed/unavailable binaries, YYC data, incompatible architecture and mismatching versions are rejected.

Catalog default: the user's local application data directory, gmmt/runners.json. Override with `--catalog FILE` in the CLI or `GMMT_CATALOG`; the desktop also has an editable catalog path. Catalogs contain local paths and are not distributed with the project. No runners are downloaded or bundled in the source repository.

```sh
# Substitute your own local files. A reference archive belongs to a game that runs with this runner.
dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll register-runner \
  --id gms2-local --runner "/path/to/linux/runner" \
  --reference "/path/to/reference/game.unx" \
  --steam-runtime "/path/to/steam-runtime/run.sh"

dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll plan --archive "/path/to/mod/data.win"

# Resource folders are merged in order; the main archive is copied separately.
dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll package \
  --patch "/path/to/mod.xdelta" --vanilla "/path/to/clean/windows/data.win" \
  --assets "/path/to/original/assets" --assets "/path/to/mod/assets" \
  --output "/path/to/new/linux-package"

# Prepared archives can use --archive instead of --patch/--vanilla.
# Inspect all available commands:
dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll --help
```

## Package contents and launch

- `runner`: selected Linux ELF binary.
- `assets/game.unx`: exact input archive, or checksum-validated Windows xdelta reconstruction.
- External resources and optional `lib/` dependencies supplied by the user.
- `launch.sh`: sets local library paths and optionally uses the registered Steam scout library selector.
- `gmmt-package.json`: input hashes, runner choice/evidence, warnings, resources and output-file hashes; GameplayVerified remains false.

Run `bash launch.sh` from the output directory. A registered Steam runtime is a local dependency: override its location on another machine with `GMMT_STEAM_RUNTIME`. The launcher falls back to the ELF loader when executable permission bits are unavailable. A package is assembled in a temporary sibling directory and published only after archive/runner checksums match. Existing output folders are never overwritten. Linked resource trees are rejected; Windows installers and patch files are not copied into assets.

## Limits and evidence

Matching metadata is a candidate match, not a guarantee of compatible semantics or full gameplay. Windows native extensions can require real Linux replacements; packaging requires explicit acknowledgement of their review (`--native-extensions-reviewed`, or the desktop checkbox). External media and libraries are supplied manually. No universal runner library or GMS2-to-GMS1 lowering is implemented.

The development tests select the original GMS1 runner for Windows Together and the official UTRY GMS2 runner for Windows UTRY. Both generated packages start through their own launcher; those smoke tests do not establish full gameplay. A GMS2 archive without a ready native port remains a required generality test.

## Tests and handoff

```sh
dotnet build tests/Gmmt.Runtime.Tests/Gmmt.Runtime.Tests.csproj -m:1
dotnet run --project tests/Gmmt.Runtime.Tests --no-build
```

Tests cover runner selection, mismatches, changed files, ambiguous profiles, native-extension acknowledgement, output preservation, atomic failures and symlink rejection. Read the [detailed development/test journal](docs/testing/2026-10-04-undertale-handoff.md) for local reproducible commands, input hashes and known environment issues. Games, media, downloaded mods, binaries, catalogs and experiment outputs stay local.

A source-code license for GMMT has not been selected. Upstream dependencies retain their licenses. Runner registration grants no redistribution rights to game, mod or engine assets.
