#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"
command -v dotnet >/dev/null || { echo "Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" >&2; exit 1; }
dotnet --list-sdks | grep -q '^10\.' || { echo ".NET 10 SDK required" >&2; exit 1; }
UTMT_DIR="extern/UndertaleModTool"
UTMT_COMMIT="27afc16500c6c561a0cf5920186fce63ca041b7a"
if [[ -d "$UTMT_DIR/UndertaleModLib" ]]; then
    [[ "$(git -C "$UTMT_DIR" rev-parse HEAD)" == "$UTMT_COMMIT" ]] || { echo "Unexpected dependency revision; inspect before setup" >&2; exit 1; }
fi
git submodule update --init --recursive
[[ "$(git -C "$UTMT_DIR" rev-parse HEAD)" == "$UTMT_COMMIT" ]] || { echo "Unexpected UndertaleModTool revision; preserve changes and inspect" >&2; exit 1; }
apply_patch_once() {
    local repo="$1" patch="$2"
    if git -C "$repo" apply --reverse --check "$SCRIPT_DIR/$patch" 2>/dev/null; then
        echo "Already applied: $patch"
    else
        git -C "$repo" apply --check "$SCRIPT_DIR/$patch"
        git -C "$repo" apply "$SCRIPT_DIR/$patch"
    fi
}
apply_patch_once "$UTMT_DIR" patches/undertale-mod-tool-frameworks.patch
apply_patch_once "$UTMT_DIR/Underanalyzer" patches/underanalyzer-frameworks.patch
dotnet restore src/Gmmt.Cli/Gmmt.Cli.csproj
dotnet build src/Gmmt.Cli/Gmmt.Cli.csproj -c Debug --no-restore -m:1
echo "CLI ready: dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll --help"
