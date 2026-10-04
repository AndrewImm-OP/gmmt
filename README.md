# GMMT

Experimental toolkit for analyzing and transferring GameMaker mod changes between game builds, with a CLI and an Avalonia desktop frontend.

## Current status

Work in progress. Full GMS2-to-GMS1 conversion is not implemented. Archive bytecode numbers alone do not establish runtime compatibility. Existing transplantation has known semantic limitations; do not treat its output as a verified playable port.

UTRY 2.1.4 experiments found identical archives reconstructed from its Windows and Linux patches. Windows-origin data renders and accepts input with the official Linux GMS2 runner. This is evidence for one matching runtime/archive pair, not a universal runner substitution guarantee.

## Build

Requires Git, .NET 10 SDK and network access for upstream dependencies/NuGet. On Linux:

```sh
git clone --recurse-submodules https://github.com/AndrewImm-OP/gmmt.git
cd gmmt
bash setup.sh
dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll --help
```

`setup.sh` builds the CLI. The desktop frontend has not been verified in these experiments. Dependencies are pinned as submodules; small local target-framework changes are preserved in `patches/` and applied idempotently. Setup stops on conflicting edits rather than discarding them.

## Development and test handoff

Read [the detailed journal](docs/testing/2026-10-04-undertale-handoff.md) before continuing. It contains inputs, hashes, build issues, launch environments, evidence limits and remaining experiments. Local experiment files, game data, downloaded mods, binaries and saves are excluded from this repository. Provide your own legally obtained game and mod packages.

Test utilities:

- `scripts/testing/inspect_gen8.py`: read archive headers and hashes.
- `scripts/testing/run_native_probe.py`: launch an isolated native copy with private config and per-attempt logs. Requires bubblewrap and optionally a locally installed Steam scout runtime. Inspect arguments with `--help`.

A source-code license for GMMT has not been selected. Upstream dependencies retain their own licenses; this repository grants no rights to game or mod assets or third-party runners.
