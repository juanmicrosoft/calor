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
# Per-arm edit mechanism. Defaults reproduce the frozen M5 shape (arm A raw / arm B
# mcp-file), where the mechanism delta IS the thing PP-L5 measures. PP-W5 is a different
# question — a TOOLCHAIN parity gate whose frozen row requires `raw` on BOTH arms — so
# running it on the M5 defaults would confound the toolchain delta with the tooling delta
# and answer PP-L5's question instead. Hence these are parameters, not constants.
ARM_A_MECH="raw"; ARM_B_MECH="mcp-file"
ARM_A_LABEL="calor"; ARM_B_LABEL="calor+mcp-file"
ARM_A_ROLE="baseline (WS1-only)"; ARM_B_ROLE="baseline + WS2/WS3 isolation"
KIND="m5-comparison"
RUNS=5; NULL_FLAG=""; PAIR_FILTER=""
# Frozen M5 sets (loop-m5-comparison.md §3): warm = PP-L5, neutral N1 = PP-L6(b).
WARM_PAIRS=(W2-001 W2-002 W2-003 W2-004 W2-005 W3-001 W3-002 W3-003 W3-004)
NEUTRAL_PAIRS=(N1-001 N1-002 N1-003 N1-005)

while [[ $# -gt 0 ]]; do
    case "$1" in
        --epoch) EPOCH="$2"; shift 2 ;;
        --arm-a-mech) ARM_A_MECH="$2"; shift 2 ;;
        --arm-b-mech) ARM_B_MECH="$2"; shift 2 ;;
        --arm-a-label) ARM_A_LABEL="$2"; shift 2 ;;
        --arm-b-label) ARM_B_LABEL="$2"; shift 2 ;;
        --arm-a-role) ARM_A_ROLE="$2"; shift 2 ;;
        --arm-b-role) ARM_B_ROLE="$2"; shift 2 ;;
        --kind) KIND="$2"; shift 2 ;;
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
# General contrast check, applied to EVERY epoch kind: the two arms must be
# genuinely different binaries. Content hash rather than --version, because the
# version in Directory.Build.props is only bumped at release — v0.10.0 and today's
# main both report 0.10.0, so a version probe would pass two identical builds and
# would FAIL two legitimately different ones. The hash cannot do either.
a_sha="$(shasum "$ARM_A_DLL" | awk '{print $1}')"
b_sha="$(shasum "$ARM_B_DLL" | awk '{print $1}')"
[[ "$ARM_A_LABEL" != "$ARM_B_LABEL" ]] || { echo "ERROR: arm labels are identical ('$ARM_A_LABEL') — run-pair writes both arms to the same directory and the second overwrites the first" >&2; exit 2; }
[[ "$a_sha" != "$b_sha" ]] || { echo "ERROR: arm-A and arm-B dll are byte-identical — no A/B contrast, epoch void" >&2; exit 2; }

# M5-SPECIFIC swap guard. The WS2 write path (`calor mcp --root`) discriminates M5's
# two arms — baseline lacks it, isolation has it. It is NOT a general invariant: a
# PP-W5 parity epoch runs v0.10.0 against main, and BOTH have that path, so applying
# this probe there would reject a correctly configured epoch.
if [[ "$KIND" == "m5-comparison" ]]; then
    a_root="$(dotnet "$ARM_A_DLL" mcp --help 2>/dev/null | grep -c -- '--root' || true)"
    b_root="$(dotnet "$ARM_B_DLL" mcp --help 2>/dev/null | grep -c -- '--root' || true)"
    [[ "$a_root" == "0" ]] || { echo "ERROR: arm-A dll unexpectedly HAS the WS2 --root write path (expected the baseline build without it) — dlls swapped?" >&2; exit 2; }
    [[ "$b_root" -ge "1" ]] || { echo "ERROR: arm-B dll LACKS the WS2 --root write path (expected the WS2/WS3 isolation build) — wrong build?" >&2; exit 2; }
fi

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
    --arg kind "$KIND" \
    --arg a_mech "$ARM_A_MECH" --arg b_mech "$ARM_B_MECH" \
    --arg a_label "$ARM_A_LABEL" --arg b_label "$ARM_B_LABEL" \
    --arg a_role "$ARM_A_ROLE" --arg b_role "$ARM_B_ROLE" \
    '{epochId:$epoch, kind:$kind, modelPin:$model, agentVersion:$agent_version,
      startedAt:$date, mode:$mode, runsPerArm:$runs, suiteDirtyFiles:($suite_dirty|tonumber),
      armA:{label:$a_label, role:$a_role, commit:$a_commit, calorDll:$a_dll, editMechanism:$a_mech},
      armB:{label:$b_label, role:$b_role, commit:$b_commit, calorDll:$b_dll, editMechanism:$b_mech},
      ppL5:{metric:"tokens-to-green", threshold:0.85, note:">=15% median paired-ratio reduction, one-sided cluster bootstrap a=0.05 (gates Annex A)", pairs:$warm},
      ppL6:{check:"neutral iterations-to-green parity + config invariance", pairs:$neutral},
      ppW5:(if $kind == "pp-w5-parity" then
              {metric:"output-tokens-to-green", pointGate:1.25, lowerBoundGate:1.0,
               note:"FAILS iff BOTH the one-sided 95% cluster-bootstrap LOWER bound of the median paired ratio (treatment/control) exceeds 1.0 AND the point estimate exceeds 1.25 (gates Annex A, A-1.4 tranche 1)",
               pairs:$neutral} else null end),
      suite:$pairs, telemetrySchema:"loop-telemetry/2"}' \
    > "$OUT/pins.json"
echo "=== M5 epoch $EPOCH ==="
echo "arm A: $ARM_A_COMMIT ($ARM_A_MECH)  $ARM_A_DLL"
echo "arm B: $ARM_B_COMMIT ($ARM_B_MECH)  $ARM_B_DLL"
echo "pairs (${#PAIRS[@]}): ${PAIRS[*]}"
echo "runs/arm: $RUNS   mode: ${NULL_FLAG:-live}   model: ${CLAUDE_MODEL:-default}"

run_arm() {  # <pair_dir> <mechanism> <dll> <label>
    local pair_dir="$1" mech="$2" dll="$3" label="$4"
    # --arm-label is REQUIRED, not cosmetic: run-pair writes to
    # $OUT/$PAIR/$ARM_LABEL/run-N, so two arms sharing a label overwrite each other.
    # M5 got away without it only because its arms differ in edit mechanism, which
    # run-pair appends to the label automatically. A parity epoch runs `raw` on both
    # arms, so both would land on "calor" and the second silently destroys the first —
    # observed in the w5 null pre-flight: 4 result.json files where 8 were expected,
    # every one of them the treatment build.
    "$SCRIPT_DIR/run-pair.sh" --pair "$pair_dir" --arm calor --arm-label "$label" \
        --edit-mechanism "$mech" --calor-dll "$dll" --runs "$RUNS" \
        $NULL_FLAG --out "$OUT" \
        | jq -c --unbuffered '{pair,arm,run,taskSuccess,iterationsToGreen,editMechanism,calorDll:((.calorDll // "")|split("/")|.[-4:]|join("/")),tokensOut:(.tokens.output // 0),censored,invalid}'
}

for pid in "${PAIRS[@]}"; do
    pair_dir="$(echo "$SCRIPT_DIR"/pairs/${pid}-*)"
    [[ -d "$pair_dir" ]] || { echo "WARNING: pair not found, skipping: $pid" >&2; continue; }
    echo "=== $pid / arm A ($ARM_A_ROLE, $ARM_A_MECH) ==="
    run_arm "$pair_dir" "$ARM_A_MECH" "$ARM_A_DLL" "$ARM_A_LABEL"
    echo "=== $pid / arm B ($ARM_B_ROLE, $ARM_B_MECH) ==="
    run_arm "$pair_dir" "$ARM_B_MECH" "$ARM_B_DLL" "$ARM_B_LABEL"
done

echo "--- M5 collection complete; adjudicate with: bench/phase0-agent-native/m5-analyze.py $OUT ---"
