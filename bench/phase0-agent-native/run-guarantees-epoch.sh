#!/usr/bin/env bash
# ============================================================================
# Guarantees probe-epoch orchestrator (guarantees plan v0.10, D-G5.2 /
# gates doc Annex A-1.3 + A-1.3.1).
#
# Runs ONE A/B epoch over the A-1.2 D5.1 fixture set (9 pairs), both arms
# CALOR + raw edits, per-arm-NATIVE product configurations:
#   - arm A (control)  = tag guarantees-baseline-v0.9 (ce7708af) PRODUCT
#     CHECKOUT — template, Calor.Tasks/Sdk/Runtime, emitter, CLI all from the
#     baseline worktree (#826 changed Calor.Tasks/Sdk.targets/template, so a
#     --calor-dll-only pin is insufficient per run-pair.sh's own rule).
#     Verify gate ABSENT (A-1.2 registered config excludes the static-verify
#     channel). Label: calor-v09-control.
#   - arm B (treatment) = current checkout (main) with the verify gate armed:
#     CALOR_P0_VERIFY_EXPECTED=1 + CALOR_P0_VERIFY_GATE=1 (A-1.3 item 3;
#     check_pins enforces bidirectionally and effect-checks via the refuted
#     canary + Tasks-root libz3 pin). Label: calor-v10-verify.
#
# The harness (run-pair.sh and this driver) runs from the CURRENT checkout for
# both arms — the per-arm split is the PRODUCT, bound via --arm-repo-root +
# --calor-dll. Gate env is exported ONLY around arm-B invocations.
#
# Adjudication: guarantees-analyze.py over the produced results + final-src
# archives (M-G3 earliness, M-W1 continuity, M-G4 weakening via
# `calor verify --weakening-check`, PP-G3 legs a+b, PP-G4 legs a+b).
#
# Live (non-null) runs consume agent API spend — get explicit authorization
# and pin the model via CLAUDE_MODEL. --null-agent is a zero-spend check.
#
# Usage:
#   CLAUDE_MODEL=claude-opus-4-8 ./run-guarantees-epoch.sh \
#       --epoch guarantees-probe-001 \
#       --arm-a-root <baseline-worktree> \
#       [--runs 5] [--pairs "W5A W5B W5C"] [--null-agent]
# ============================================================================
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

EPOCH=""; ARM_A_ROOT=""; RUNS=5; NULL_FLAG=""; PAIR_FILTER=""
# Frozen A-1.3 set = the A-1.2 D5.1 fixtures, unchanged.
ALL_PAIRS=(W5A-001 W5A-002 W5A-003 W5B-001 W5B-002 W5B-003 W5C-001 W5C-002 W5C-003)

while [[ $# -gt 0 ]]; do
    case "$1" in
        --epoch) EPOCH="$2"; shift 2 ;;
        --arm-a-root) ARM_A_ROOT="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --pairs) PAIR_FILTER="$2"; shift 2 ;;
        --null-agent) NULL_FLAG="--null-agent"; shift ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done
[[ -n "$EPOCH" && -n "$ARM_A_ROOT" ]] || {
    echo "Usage: --epoch <id> --arm-a-root <baseline-worktree> [--runs N] [--pairs \"W5A W5B W5C\"] [--null-agent]" >&2
    exit 2; }

# ---------------------------------------------------------------------------
# Pre-flight. The epoch is void if the arms silently share a product, if the
# control checkout is not the registered tag, or if the treatment gate is not
# EFFECTIVE. Discriminator: --weakening-check exists only in the v0.10 CLI.
# ---------------------------------------------------------------------------
[[ -d "$ARM_A_ROOT/src/Calor.Tasks" ]] || { echo "ERROR: --arm-a-root is not a calor checkout: $ARM_A_ROOT" >&2; exit 2; }
ARM_A_ROOT="$(cd "$ARM_A_ROOT" && pwd -P)"
ARM_A_DLL="$ARM_A_ROOT/src/Calor.Compiler/bin/Release/net10.0/calor.dll"
ARM_B_DLL="$REPO_ROOT/src/Calor.Compiler/bin/Release/net10.0/calor.dll"
[[ -f "$ARM_A_DLL" ]] || { echo "ERROR: arm-A calor.dll not built: $ARM_A_DLL (dotnet build -c Release in the worktree)" >&2; exit 2; }
[[ -f "$ARM_B_DLL" ]] || { echo "ERROR: arm-B calor.dll not built: $ARM_B_DLL" >&2; exit 2; }
[[ "$ARM_A_ROOT" != "$REPO_ROOT" ]] || { echo "ERROR: arm-A root IS the current checkout — no A/B contrast" >&2; exit 2; }

ARM_A_COMMIT="$(git -C "$ARM_A_ROOT" rev-parse HEAD)"
ARM_B_COMMIT="$(git -C "$REPO_ROOT" rev-parse HEAD)"
BASELINE_COMMIT="$(git -C "$REPO_ROOT" rev-parse 'guarantees-baseline-v0.9^{commit}' 2>/dev/null || echo unknown)"
[[ "$ARM_A_COMMIT" == "$BASELINE_COMMIT" ]] || {
    echo "ERROR: arm-A worktree HEAD ($ARM_A_COMMIT) is not tag guarantees-baseline-v0.9 ($BASELINE_COMMIT)" >&2; exit 2; }

a_weak="$(dotnet "$ARM_A_DLL" verify --help 2>/dev/null | grep -c -- '--weakening-check' || true)"
b_weak="$(dotnet "$ARM_B_DLL" verify --help 2>/dev/null | grep -c -- '--weakening-check' || true)"
[[ "$a_weak" == "0" ]] || { echo "ERROR: arm-A dll HAS --weakening-check (expected the v0.9 baseline build) — builds swapped?" >&2; exit 2; }
[[ "$b_weak" -ge 1 ]] || { echo "ERROR: arm-B dll LACKS --weakening-check (expected the v0.10 build) — stale Release build?" >&2; exit 2; }

# Treatment-gate effect check at driver level too (defense in depth on top of
# check_pins): the v0.10 CLI must refute the canary before any run starts.
canary_out="$(dotnet "$ARM_B_DLL" verify "$REPO_ROOT/tests/TestData/Verification/Outcomes/refuted-with-binding.calr" --no-cache --format json 2>&1)" || true
grep -q '"status": "refuted"' <<< "$canary_out" || {
    echo "ERROR: arm-B verify canary did not refute — treatment gate would be dead" >&2; exit 2; }

# The gate env MUST NOT be inherited from the caller: arm A would trip the
# bidirectional pin (or worse, silently drift if the baseline harness ran).
unset CALOR_P0_VERIFY_EXPECTED CALOR_P0_VERIFY_GATE

declare -a PAIRS=()
if [[ -z "$PAIR_FILTER" ]]; then
    PAIRS=("${ALL_PAIRS[@]}")
else
    for grp in $PAIR_FILTER; do
        case "$grp" in
            W5A) PAIRS+=(W5A-001 W5A-002 W5A-003) ;;
            W5B) PAIRS+=(W5B-001 W5B-002 W5B-003) ;;
            W5C) PAIRS+=(W5C-001 W5C-002 W5C-003) ;;
            all) PAIRS+=("${ALL_PAIRS[@]}") ;;
            *) PAIRS+=("$grp") ;;   # explicit pair id (e.g. W5B-002)
        esac
    done
fi

OUT="$SCRIPT_DIR/epochs/$EPOCH"
mkdir -p "$OUT"

# Pins (gates §1 + A-1.3): both products, both commits, per-arm gate config.
jq -n \
    --arg epoch "$EPOCH" \
    --arg model "${CLAUDE_MODEL:-default}" \
    --arg agent_version "$(claude --version 2>/dev/null || echo unavailable)" \
    --arg date "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --arg mode "${NULL_FLAG:-live}" \
    --argjson runs "$RUNS" \
    --arg a_commit "$ARM_A_COMMIT" --arg a_dll "$ARM_A_DLL" --arg a_root "$ARM_A_ROOT" \
    --arg b_commit "$ARM_B_COMMIT" --arg b_dll "$ARM_B_DLL" --arg b_root "$REPO_ROOT" \
    --arg suite_dirty "$(git -C "$REPO_ROOT" status --porcelain "$SCRIPT_DIR" | wc -l | tr -d ' ')" \
    --argjson pairs "$(printf '%s\n' "${PAIRS[@]}" | jq -R . | jq -s .)" \
    '{epochId:$epoch, kind:"guarantees-probe", modelPin:$model, agentVersion:$agent_version,
      startedAt:$date, mode:$mode, runsPerArm:$runs, suiteDirtyFiles:($suite_dirty|tonumber),
      armA:{label:"calor-v09-control", role:"guarantees-baseline-v0.9 native product (A-1.2 config, verify EXCLUDED)",
            commit:$a_commit, calorDll:$a_dll, armRepoRoot:$a_root, editMechanism:"raw", verify:false},
      armB:{label:"calor-v10-verify", role:"v0.10 native product (A-1.3 config, verify gate armed + effect-checked)",
            commit:$b_commit, calorDll:$b_dll, armRepoRoot:$b_root, editMechanism:"raw", verify:true},
      ppG3:{legA:"cross-arm M-W1 not below control (ceiling 9/9)", legB:">=2 of 3 W5-B defects, per-run joint predicate >=3-of-5 (build-proof AND probe-pass AND intactOrStrengthened DETERMINATE)"},
      ppG4:{legA:"iterations-to-green one-sided cluster bootstrap a=0.05, ratio>1 = regression, all 9 pairs", legB:"weakening incidence excess <= 3 runs ABSOLUTE (indeterminate >20% => reported-not-adjudicated)"},
      suite:$pairs, telemetrySchema:"loop-telemetry/2"}' \
    > "$OUT/pins.json"
echo "=== guarantees epoch $EPOCH ==="
echo "arm A (control):   $ARM_A_COMMIT  root=$ARM_A_ROOT"
echo "arm B (treatment): $ARM_B_COMMIT  root=$REPO_ROOT  gate=armed"
echo "pairs (${#PAIRS[@]}): ${PAIRS[*]}"
echo "runs/arm: $RUNS   mode: ${NULL_FLAG:-live}   model: ${CLAUDE_MODEL:-default}"

summarize() {
    jq -c --unbuffered '{pair,arm,run,taskSuccess,iterationsToGreen,defectCaught:(.defect.caught // null),tokensOut:(.tokens.output // 0),censored,invalid:(.invalid // false)}'
}

for pid in "${PAIRS[@]}"; do
    pair_dir="$(echo "$SCRIPT_DIR"/pairs/${pid}-*)"
    [[ -d "$pair_dir" ]] || { echo "WARNING: pair not found, skipping: $pid" >&2; continue; }
    echo "=== $pid / arm A (v0.9 control, gateless) ==="
    "$SCRIPT_DIR/run-pair.sh" --pair "$pair_dir" --arm calor \
        --calor-dll "$ARM_A_DLL" --arm-repo-root "$ARM_A_ROOT" \
        --arm-label calor-v09-control --runs "$RUNS" \
        $NULL_FLAG --out "$OUT" | summarize
    echo "=== $pid / arm B (v0.10 treatment, verify gate armed) ==="
    CALOR_P0_VERIFY_EXPECTED=1 CALOR_P0_VERIFY_GATE=1 \
        "$SCRIPT_DIR/run-pair.sh" --pair "$pair_dir" --arm calor \
        --calor-dll "$ARM_B_DLL" \
        --arm-label calor-v10-verify --runs "$RUNS" \
        $NULL_FLAG --out "$OUT" | summarize
done

echo "--- guarantees collection complete; adjudicate with: bench/phase0-agent-native/guarantees-analyze.py $OUT ---"
