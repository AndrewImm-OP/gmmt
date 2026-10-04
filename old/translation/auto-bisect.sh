#!/usr/bin/env bash
# Auto-bisect GMMT_UNWRAP_LIMIT without interactive prompts.
# For each N: builds .unx, deploys, runs runner with timeout, captures
# crash signature from coredumpctl, records result.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

PATCH="/home/andrew/Загрузки/ut-red-and-yellow/Undertale 1.08 to UTRY 2.1.4.xdelta"
TARGET="/home/andrew/.local/share/Steam/steamapps/common/Undertale/assets/gameog.unx"
GAMEDIR="/home/andrew/.local/share/Steam/steamapps/common/Undertale"
DEPLOY="$GAMEDIR/assets/game.unx"
RUNTIME_SEC=30  # seconds to let runner live before killing
OUTDIR="/tmp/gmmt-bisect"

mkdir -p "$OUTDIR"
: > "$OUTDIR/results.txt"

LIMITS=("$@")
if [ ${#LIMITS[@]} -eq 0 ]; then
    LIMITS=(0 1 2 4 8 16 64 426)
fi

# Get the timestamp before each run so we can find the coredump for *this* run
get_latest_runner_dump() {
    local since="$1"
    coredumpctl list --since="$since" --no-pager 2>/dev/null \
        | awk '/runner/ {print $1" "$2}' | tail -1
}

run_one() {
    local N="$1"
    local OUT="$OUTDIR/u${N}.unx"
    local LOG="$OUTDIR/u${N}.pipeline.log"
    local GAMELOG="$OUTDIR/u${N}.game.log"

    echo ""
    echo "=============================================="
    echo "=== N=$N ==="
    echo "=============================================="

    # 1. Build
    GMMT_UNWRAP_LIMIT="$N" \
        dotnet run -f net10.0 --no-build \
        --project src/Gmmt.Cli/Gmmt.Cli.csproj -- \
        translate-xdelta "$PATCH" "$TARGET" -o "$OUT" \
        --skip-diff-patches \
        > "$LOG" 2>&1
    local PIPE_RC=$?
    if [ $PIPE_RC -ne 0 ]; then
        echo "  PIPELINE FAILED rc=$PIPE_RC — see $LOG"
        echo "N=$N PIPELINE_FAILED" >> "$OUTDIR/results.txt"
        return
    fi

    local UNWRAPS REMAPS
    UNWRAPS=$(grep -c '\[Unwrap\] script_execute' "$LOG" 2>/dev/null || echo 0)
    REMAPS=$(grep -c '\[Remap#' "$LOG" 2>/dev/null || echo 0)
    echo "  built: unwraps=$UNWRAPS remaps=$REMAPS  output=$OUT"

    # 2. Deploy
    cp "$OUT" "$DEPLOY"

    # 3. Run runner standalone with timeout (no Steam wrapping)
    local START_TS
    START_TS=$(date +%s)
    local START_HUMAN
    START_HUMAN=$(date '+%Y-%m-%d %H:%M:%S')

    (
        cd "$GAMEDIR" || exit 99
        # Use Steam runtime wrapper (32-bit runner needs libcrypto.so.1.0.0
        # which only Steam runtime supplies).
        timeout --preserve-status --signal=TERM "$RUNTIME_SEC" \
            /home/andrew/.local/share/Steam/ubuntu12_32/steam-runtime/run.sh \
            bash -c 'LD_LIBRARY_PATH="./lib:$LD_LIBRARY_PATH" ./runner' \
            > "$GAMELOG" 2>&1
        echo $? > "$OUTDIR/u${N}.rc"
    )
    local RUNNER_RC
    RUNNER_RC=$(cat "$OUTDIR/u${N}.rc" 2>/dev/null || echo "?")

    # 4. Classify
    sleep 1  # let coredumpctl catch up
    local DUMP_HEADER=""
    if [ "$RUNNER_RC" = "139" ] || [ "$RUNNER_RC" = "$((128+11))" ]; then
        DUMP_HEADER=$(coredumpctl info --since="$START_HUMAN" 2>/dev/null \
            | grep -E "Signal|Stack trace|Command Line|0x00000000ffffffff" \
            | head -8 | tr '\n' ' | ')
        echo "  SIGSEGV  rc=$RUNNER_RC"
        echo "N=$N SIGSEGV rc=$RUNNER_RC  [$DUMP_HEADER]" >> "$OUTDIR/results.txt"
    elif [ "$RUNNER_RC" = "143" ] || [ "$RUNNER_RC" = "124" ]; then
        # SIGTERM from timeout: runner survived the test window
        echo "  SURVIVED (killed by timeout after ${RUNTIME_SEC}s) — likely ok / GML error"
        echo "N=$N SURVIVED rc=$RUNNER_RC" >> "$OUTDIR/results.txt"
    else
        # Voluntary exit — could be normal quit or GML error popup
        local LAST_TAIL
        LAST_TAIL=$(tail -c 400 "$GAMELOG" 2>/dev/null | tr '\n' ' ')
        echo "  EXITED rc=$RUNNER_RC  tail=[$LAST_TAIL]"
        echo "N=$N EXITED rc=$RUNNER_RC  tail=[$LAST_TAIL]" >> "$OUTDIR/results.txt"
    fi
}

echo "=== Build once ==="
dotnet build -f net10.0 -c Debug src/Gmmt.Cli/Gmmt.Cli.csproj 2>&1 | tail -3

for N in "${LIMITS[@]}"; do
    run_one "$N"
done

echo ""
echo "=============================================="
echo "=== ALL RESULTS ==="
echo "=============================================="
cat "$OUTDIR/results.txt"
