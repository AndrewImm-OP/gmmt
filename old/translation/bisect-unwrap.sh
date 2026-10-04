#!/usr/bin/env bash
# Bisect GMMT_UNWRAP_LIMIT to find the first failing unwrap.
# Usage: ./bisect-unwrap.sh [LIMITS...]
#   default limits: 0 1 2 4 8 16 32 64 128 256 426
#
# After each build, deploys to Undertale assets and waits for you to test
# in-game, then asks OK/FAIL before moving to the next limit.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

PATCH="/home/andrew/Загрузки/ut-red-and-yellow/Undertale 1.08 to UTRY 2.1.4.xdelta"
TARGET="/home/andrew/.local/share/Steam/steamapps/common/Undertale/assets/gameog.unx"
DEPLOY="/home/andrew/.local/share/Steam/steamapps/common/Undertale/assets/game.unx"

if [ ! -f "$PATCH" ]; then echo "PATCH not found: $PATCH" >&2; exit 1; fi
if [ ! -f "$TARGET" ]; then echo "TARGET not found: $TARGET" >&2; exit 1; fi

# Default bisect schedule
LIMITS=("$@")
if [ ${#LIMITS[@]} -eq 0 ]; then
    LIMITS=(0 1 2 4 8 16 32 64 128 256 426)
fi

echo "=== Building once ==="
dotnet build -f net10.0 -c Debug src/Gmmt.Cli/Gmmt.Cli.csproj | tail -3

mkdir -p /tmp/gmmt-bisect

for N in "${LIMITS[@]}"; do
    OUT="/tmp/gmmt-bisect/u${N}.unx"
    LOG="/tmp/gmmt-bisect/u${N}.log"
    echo ""
    echo "=== GMMT_UNWRAP_LIMIT=$N ==="
    GMMT_UNWRAP_LIMIT="$N" \
        dotnet run -f net10.0 --no-build \
        --project src/Gmmt.Cli/Gmmt.Cli.csproj -- \
        translate-xdelta "$PATCH" "$TARGET" -o "$OUT" \
        --skip-diff-patches \
        2> "$LOG" || { echo "PIPELINE FAILED for N=$N — see $LOG"; exit 2; }

    UNWRAPS=$(grep -c '\[Unwrap\] script_execute' "$LOG" || true)
    REMAPS=$(grep -c '\[Remap#' "$LOG" || true)
    echo "  → built: unwraps=$UNWRAPS remaps=$REMAPS"
    echo "  → output: $OUT"

    cp "$OUT" "$DEPLOY"
    echo "  → deployed to $DEPLOY"
    echo ""
    echo "  TEST IN GAME NOW. When done, type:"
    echo "    ok    — game ran past intro (no SIGSEGV)"
    echo "    fail  — crashed"
    echo "    skip  — skip and continue"
    echo "    quit  — stop bisect"
    read -r -p "  result for N=$N? " RESULT
    case "$RESULT" in
        ok)   echo "  [OK] N=$N passed"   >> /tmp/gmmt-bisect/results.txt ;;
        fail) echo "  [FAIL] N=$N crashed" >> /tmp/gmmt-bisect/results.txt ;;
        skip) echo "  [SKIP] N=$N"         >> /tmp/gmmt-bisect/results.txt ;;
        quit) echo "  [QUIT after N=$N]"   >> /tmp/gmmt-bisect/results.txt; break ;;
        *)    echo "  [?] N=$N: $RESULT"   >> /tmp/gmmt-bisect/results.txt ;;
    esac
done

echo ""
echo "=== Bisect results ==="
cat /tmp/gmmt-bisect/results.txt
