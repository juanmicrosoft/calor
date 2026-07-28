#!/usr/bin/env bash
# ============================================================================
# M5 comparison-epoch orchestrator (loop plan v0.9 milestone M5).
#
# Runs ONE simultaneous A/B epoch over two calor toolchain BUILDS against
# identical pairs/pins/fixtures, per docs/plans/loop-m5-comparison.md:
#   - arm A = loop-baseline-ws1 build      -> --edit-mechanism raw   (cold)
#   - arm B = baseline + WS2/WS3 isolation -> --edit-mechanism mcp-file (warm)
# Both are the CALOR arm; they differ only in the pinned build (--calor-dll,
# #813) and the edit mechanism — which is the WS2/WS3 tooling delta PP-L5
# measures. Everything else (pairs, task specs, held-out suites, harness) is
# the current checkout, so tasks/pins are byte-identical across arms.
#
# Adjudication is done by m5-analyze.py on the produced results:
#   - PP-L5: median paired tokens-to-green ratio (armB/armA) <= 0.85 on the warm
#            set (W2+W3), one-sided cluster bootstrap alpha=0.05 (gates §6.1).
#   - PP-L6: neutral-set (N1) iterations-to-green parity + censored caps; this
#            driver also stamps per-run build provenance (calorDll) via #813.
#
# Live (non-null) runs consume agent API spend — get explicit authorization and
# pin the model via CLAUDE_MODEL. --null-agent is a zero-spend plumbing check.
#
# Usage:
#   CLAUDE_MODEL=claude-opus-4-8 ./run-m5-epoch.sh \
#       --epoch m5-compare-001 \
#       --arm-a-dll <baseline-build>/calor.dll \
#       --arm-b-dll <isolation-build>/calor.dll \
#       [--arm-a-commit <sha>] [--arm-b-commit <sha>] \
#       [--runs 5] [--pairs "W2 W3 N1"] [--null-agent]
# ============================================================================
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

EPOCH=""; ARM_A_DLL=""; ARM_B_DLL=""; ARM_A_COMMIT=""; ARM_B_COMMIT=""
RUNS=5; NULL_FLAG=""; PAIR_FILTER=""
# Frozen M5 sets (loop-m5-comparison.md §3): warm = PP-L5, neutral N1 = PP-L6(b).
WARM_PAIRS=(W2-001 W2-002 W2-003 W2-004 W2-005 W3-001 W3-002 W3-003 W3-004)
NEUTRAL_PAIRS=(N1-001 N1-002 N1-003 N1-005)

while [[ $# -gt 0 ]]; do
    case "$1" in
        --epoch) EPOCH="$2"; shift 2 ;;
        --arm-a-dll) ARM_A_DLL="$2"; shift 2 ;;
        --arm-b-dll) ARM_B_DLL="$2"; shift 2 ;;
        --arm-a-commit) ARM_A_COMMIT="$2"; shift 2 ;;
        --arm-b-commit) ARM_B_COMMIT="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --pairs) PAIR_FILTER="$2"; shift 2 ;;
        --null-agent) NULL_FLAG="--null-agent"; shift ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done
[[ -n "$EPOCH" && -n "$ARM_A_DLL" && -n "$ARM_B_DLL" ]] || {
    echo "Usage: --epoch <id> --arm-a-dll <path> --arm-b-dll <path> [--arm-a-commit <sha>] [--arm-b-commit <sha>] [--runs N] [--pairs \"W2 W3 N1\"] [--null-agent]" >&2
    exit 2; }

# Default provenance shas (loop-baseline-ws1 is an annotated tag -> resolve to
# its commit; arm B defaults to the current checkout's HEAD).
[[ -n "$ARM_A_COMMIT" ]] || ARM_A_COMMIT="$(git -C "$REPO_ROOT" rev-parse 'loop-baseline-ws1^{commit}' 2>/dev/null || echo unknown)"
[[ -n "$ARM_B_COMMIT" ]] || ARM_B_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD)"

# ---------------------------------------------------------------------------
# Pre-flight: both builds must exist, be runnable, and be GENUINELY different
# (the whole epoch is void if both arms silently share a build). The WS2 write
# path (`calor mcp --root`) is the discriminator: arm A (baseline) lacks it,
# arm B (isolation) has it.
# ---------------------------------------------------------------------------
[[ -f "$ARM_A_DLL" ]] || { echo "ERROR: --arm-a-dll not found: $ARM_A_DLL" >&2; exit 2; }
[[ -f "$ARM_B_DLL" ]] || { echo "ERROR: --arm-b-dll not found: $ARM_B_DLL" >&2; exit 2; }
ARM_A_DLL="$(cd "$(dirname "$ARM_A_DLL")" && pwd -P)/$(basename "$ARM_A_DLL")"
ARM_B_DLL="$(cd "$(dirname "$ARM_B_DLL")" && pwd -P)/$(basename "$ARM_B_DLL")"
[[ "$ARM_A_DLL" != "$ARM_B_DLL" ]] || { echo "ERROR: arm-A and arm-B dll are the same path — no A/B contrast" >&2; exit 2; }
a_root="$(dotnet "$ARM_A_DLL" mcp --help 2>/dev/null | grep -c -- '--root' || true)"
b_root="$(dotnet "$ARM_B_DLL" mcp --help 2>/dev/null | grep -c -- '--root' || true)"
[[ "$a_root" == "0" ]] || { echo "ERROR: arm-A dll unexpectedly HAS the WS2 --root write path (expected the baseline build without it) — dlls swapped?" >&2; exit 2; }
[[ "$b_root" -ge "1" ]] || { echo "ERROR: arm-B dll LACKS the WS2 --root write path (expected the WS2/WS3 isolation build) — wrong build?" >&2; exit 2; }

# Resolve the pair set (default: warm + neutral = full M5).
declare -a PAIRS=()
if [[ -z "$PAIR_FILTER" ]]; then
    PAIRS=("${WARM_PAIRS[@]}" "${NEUTRAL_PAIRS[@]}")
else
    for grp in $PAIR_FILTER; do
        case "$grp" in
            W2) PAIRS+=(W2-001 W2-002 W2-003 W2-004 W2-005) ;;
            W3) PAIRS+=(W3-001 W3-002 W3-003 W3-004) ;;
            N1) PAIRS+=("${NEUTRAL_PAIRS[@]}") ;;
            warm) PAIRS+=("${WARM_PAIRS[@]}") ;;
            neutral) PAIRS+=("${NEUTRAL_PAIRS[@]}") ;;
            *) PAIRS+=("$grp") ;;   # explicit pair id (e.g. W2-003)
        esac
    done
fi

OUT="$SCRIPT_DIR/epochs/$EPOCH"
mkdir -p "$OUT"

# Pins (gates §1) — records BOTH builds so the record proves arm A ran the
# baseline and arm B ran the isolation (pins.json elsewhere carries one commit).
jq -n \
    --arg epoch "$EPOCH" \
    --arg model "${CLAUDE_MODEL:-default}" \
    --arg agent_version "$(claude --version 2>/dev/null || echo unavailable)" \
    --arg date "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --arg mode "${NULL_FLAG:-live}" \
    --argjson runs "$RUNS" \
    --arg a_commit "$ARM_A_COMMIT" --arg a_dll "$ARM_A_DLL" \
    --arg b_commit "$ARM_B_COMMIT" --arg b_dll "$ARM_B_DLL" \
    --arg suite_dirty "$(git -C "$REPO_ROOT" status --porcelain "$SCRIPT_DIR" | wc -l | tr -d ' ')" \
    --argjson pairs "$(printf '%s\n' "${PAIRS[@]}" | jq -R . | jq -s .)" \
    --argjson warm "$(printf '%s\n' "${WARM_PAIRS[@]}" | jq -R . | jq -s .)" \
    --argjson neutral "$(printf '%s\n' "${NEUTRAL_PAIRS[@]}" | jq -R . | jq -s .)" \
    '{epochId:$epoch, kind:"m5-comparison", modelPin:$model, agentVersion:$agent_version,
      startedAt:$date, mode:$mode, runsPerArm:$runs, suiteDirtyFiles:($suite_dirty|tonumber),
      armA:{label:"calor", role:"baseline (WS1-only)", commit:$a_commit, calorDll:$a_dll, editMechanism:"raw"},
      armB:{label:"calor+mcp-file", role:"baseline + WS2/WS3 isolation", commit:$b_commit, calorDll:$b_dll, editMechanism:"mcp-file"},
      ppL5:{metric:"tokens-to-green", threshold:0.85, note:">=15% median paired-ratio reduction, one-sided cluster bootstrap a=0.05 (gates Annex A)", pairs:$warm},
      ppL6:{check:"neutral iterations-to-green parity + config invariance", pairs:$neutral},
      suite:$pairs, telemetrySchema:"loop-telemetry/2"}' \
    > "$OUT/pins.json"
echo "=== M5 epoch $EPOCH ==="
echo "arm A: $ARM_A_COMMIT (raw)      $ARM_A_DLL"
echo "arm B: $ARM_B_COMMIT (mcp-file) $ARM_B_DLL"
echo "pairs (${#PAIRS[@]}): ${PAIRS[*]}"
echo "runs/arm: $RUNS   mode: ${NULL_FLAG:-live}   model: ${CLAUDE_MODEL:-default}"

run_arm() {  # <pair_dir> <mechanism> <dll>
    local pair_dir="$1" mech="$2" dll="$3"
    "$SCRIPT_DIR/run-pair.sh" --pair "$pair_dir" --arm calor \
        --edit-mechanism "$mech" --calor-dll "$dll" --runs "$RUNS" \
        $NULL_FLAG --out "$OUT" \
        | jq -c --unbuffered '{pair,arm,run,taskSuccess,iterationsToGreen,editMechanism,calorDll:((.calorDll // "")|split("/")|.[-4:]|join("/")),tokensOut:(.tokens.output // 0),censored,invalid}'
}

for pid in "${PAIRS[@]}"; do
    pair_dir="$(echo "$SCRIPT_DIR"/pairs/${pid}-*)"
    [[ -d "$pair_dir" ]] || { echo "WARNING: pair not found, skipping: $pid" >&2; continue; }
    echo "=== $pid / arm A (baseline, raw) ==="
    run_arm "$pair_dir" raw "$ARM_A_DLL"
    echo "=== $pid / arm B (isolation, mcp-file) ==="
    run_arm "$pair_dir" mcp-file "$ARM_B_DLL"
done

echo "--- M5 collection complete; adjudicate with: bench/phase0-agent-native/m5-analyze.py $OUT ---"
