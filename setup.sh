#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== GMMT Setup ==="

# 1. Check for .NET SDK
if ! command -v dotnet &>/dev/null; then
    echo "ERROR: .NET SDK not found."
    echo "Install .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0"
    echo "Or run: curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 9.0"
    exit 1
fi

DOTNET_VER=$(dotnet --version)
echo "Found .NET SDK: $DOTNET_VER"

# 2. Clone UndertaleModTool at a known-good commit
#    Pinned to 27afc165 (2025-12-14, pre-.NET-10 migration) which is the last
#    commit before the net10.0 upgrade and has stable GMS1 archive parsing.
UTMT_DIR="extern/UndertaleModTool"
UTMT_COMMIT="27afc165"
if [ ! -d "$UTMT_DIR/UndertaleModLib" ]; then
    echo "Cloning UndertaleModTool..."
    mkdir -p extern
    git clone --recurse-submodules https://github.com/UnderminersTeam/UndertaleModTool.git "$UTMT_DIR"
    (cd "$UTMT_DIR" && git checkout "$UTMT_COMMIT" && git submodule update --init --recursive)
else
    # Verify we're on the pinned commit
    CURRENT=$(cd "$UTMT_DIR" && git rev-parse --short HEAD)
    if [ "$CURRENT" != "$UTMT_COMMIT" ]; then
        echo "UndertaleModTool at $CURRENT, resetting to pinned $UTMT_COMMIT..."
        (cd "$UTMT_DIR" && git fetch && git checkout "$UTMT_COMMIT" && git submodule update --init --recursive)
    else
        echo "UndertaleModTool already at pinned commit $UTMT_COMMIT"
    fi
fi

# 3. Patch extern .csproj files to match our target framework
OUR_TFM="net9.0"

# Patch any csproj that doesn't already target our TFM.
# Handles net8.0, net10.0, and multi-target (<TargetFrameworks>) forms.
while IFS= read -r -d '' csproj; do
    changed=false
    for old_tfm in "net10.0" "net8.0"; do
        if grep -q "$old_tfm" "$csproj" 2>/dev/null; then
            sed -i "s|<TargetFramework>${old_tfm}</TargetFramework>|<TargetFramework>$OUR_TFM</TargetFramework>|g" "$csproj"
            sed -i "s|${old_tfm}|$OUR_TFM|g" "$csproj"
            changed=true
        fi
    done
    if [ "$changed" = true ]; then
        echo "Patched $csproj → $OUR_TFM"
    fi
done < <(find "$UTMT_DIR" -name '*.csproj' -print0)

# 4. Restore and build
echo ""
echo "Restoring packages..."
dotnet restore gmmt.sln

echo ""
echo "Building..."
dotnet build gmmt.sln -c Debug --no-restore

echo ""
echo "=== Setup complete ==="
echo "Run: dotnet run --project src/Gmmt.Cli -- inspect <path-to-data.win>"
echo "Run: dotnet run --project src/Gmmt.Cli -- compare-names <data.win> <game.unx>"
