#!/usr/bin/env bash
# ============================================================================
# PP-E1 leg B epoch runner — the REGISTERED runner for `e1-rows-parity-001`
# (annex A-1.11, §A.2 row PP-E1; roadmap §4.4 gate 4, "Who runs it, and when").
#
# Shape, frozen by the row and transcribed here (nothing is a parameter unless
# the row leaves it open):
#   - the four registered N1 neutral pairs: N1-001-string-utils, N1-002-inventory,
#     N1-003-csv-row, N1-005-order-pipeline
#   - 5 runs/arm, INTERLEAVED one run per arm at a time (run-m5-epoch.sh's
#     non-m5 branch), same pinned model (CLAUDE_MODEL, required)
#   - edit mechanism `raw` on BOTH arms; identical harness configuration
#   - arm A = control  = the `v0.14.3` release tag, built as a PRODUCT
#     (Calor.Compiler + Calor.Tasks + Calor.Runtime, Release)
#     arm B = treatment = the 0.15.0 release build (the release PR's commit)
#   - per-arm --arm-repo-root, so the agent's own `dotnet build` binds each
#     arm's Calor.Tasks; pins.json records both repo roots AND both
#     Calor.Tasks.dll hashes, and run-pair.sh stamps armRepoRoot into every
#     run's result.json (the w5-parity-001 void is what this pin exists for)
#   - per-run figure = token-usage.py's corrected output tokens (A-1.9.1);
#     adjudicated by ppe1-analyze.py into <epoch>/ppe1-analysis.json
#
# WHO RUNS IT, AND WHEN: the 0.15.0 release PR's author, before the release
# tag. `create-release` does not proceed to the tag until ppe1-analysis.json
# exists and the PP-E1 verdict (derived by EffectRowsProbeLedgerTests from
# the ledger) is written into the release notes.
#
# THIS IS A PAID 40-RUN AGENT EPOCH. The script prints its plan and REFUSES
# to run without an explicit --confirm-paid-epoch. `--null-agent` (zero-spend
# plumbing check) needs no confirmation.
#
# Usage:
#   CLAUDE_MODEL=<pinned model> bench/phase0-agent-native/run-ppe1-epoch.sh \
#       --arm-a-repo-root <v0.14.3 checkout, built Release> \
#       --arm-b-repo-root <0.15.0 release-commit checkout, built Release> \
#       [--epoch e1-rows-parity-001] [--runs 5] [--null-agent] --confirm-paid-epoch
#
# Build each arm's PRODUCT first, in its own checkout (never the harness
# checkout for either arm):
#   git worktree add <root> v0.14.3   # arm A;  arm B: the release commit
#   dotnet build <root>/src/Calor.Compiler -c Release
#   dotnet build <root>/src/Calor.Tasks    -c Release
#   dotnet build <root>/src/Calor.Runtime  -c Release
# ============================================================================
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

EPOCH="e1-rows-parity-001"
RUNS=5
ARM_A_ROOT=""; ARM_B_ROOT=""
NULL_FLAG=""; CONFIRM=0
ARM_A_LABEL="calor+v0.14.3"; ARM_B_LABEL="calor+0.15.0"
ARM_A_ROLE="control (v0.14.3 release tag)"; ARM_B_ROLE="treatment (0.15.0 release build)"
KIND="pp-e1-rows-parity"
PAIRS=(N1-001 N1-002 N1-003 N1-005)

while [[ $# -gt 0 ]]; do
    case "$1" in
        --epoch) EPOCH="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --arm-a-repo-root) ARM_A_ROOT="$2"; shift 2 ;;
        --arm-b-repo-root) ARM_B_ROOT="$2"; shift 2 ;;
        --null-agent) NULL_FLAG="--null-agent"; shift ;;
        --confirm-paid-epoch) CONFIRM=1; shift ;;
        -h|--help) sed -n '2,45p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

# A null-agent run never lands in the registered directory: it is a plumbing
# check, and ppe1-analyze.py refuses to adjudicate an epoch that holds one.
if [[ -n "$NULL_FLAG" && "$EPOCH" == "e1-rows-parity-001" ]]; then
    EPOCH="e1-rows-parity-001-null"
    echo "NOTE: --null-agent forces the epoch id to '$EPOCH' (the registered id is for the live epoch only)"
fi

[[ -n "$ARM_A_ROOT" && -n "$ARM_B_ROOT" ]] || {
    echo "ERROR: --arm-a-repo-root (v0.14.3 product build) and --arm-b-repo-root (0.15.0 product build) are required." >&2
    echo "       Both arms MUST be per-arm checkouts; the harness checkout is neither arm." >&2
    exit 2; }
for r in "$ARM_A_ROOT" "$ARM_B_ROOT"; do
    [[ -d "$r/src/Calor.Tasks" ]] || { echo "ERROR: not a calor checkout: $r" >&2; exit 2; }
done
ARM_A_ROOT="$(cd "$ARM_A_ROOT" && pwd -P)"; ARM_B_ROOT="$(cd "$ARM_B_ROOT" && pwd -P)"
ARM_A_DLL="$ARM_A_ROOT/src/Calor.Compiler/bin/Release/net10.0/calor.dll"
ARM_B_DLL="$ARM_B_ROOT/src/Calor.Compiler/bin/Release/net10.0/calor.dll"
ARM_A_COMMIT="$(git -C "$ARM_A_ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
ARM_B_COMMIT="$(git -C "$ARM_B_ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
ARM_A_DESCRIBE="$(git -C "$ARM_A_ROOT" describe --tags --exact-match 2>/dev/null || echo '(not on a tag)')"

for need in "$ARM_A_DLL" "$ARM_B_DLL" \
            "$ARM_A_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" \
            "$ARM_B_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" \
            "$ARM_A_ROOT/src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll" \
            "$ARM_B_ROOT/src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll"; do
    [[ -f "$need" ]] || { echo "ERROR: missing $need — build the PRODUCT per arm (see header)" >&2; exit 2; }
done
a_tasks="$(shasum "$ARM_A_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" | awk '{print $1}')"
b_tasks="$(shasum "$ARM_B_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" | awk '{print $1}')"

if [[ -z "$NULL_FLAG" && ( -z "${CLAUDE_MODEL:-}" || "${CLAUDE_MODEL:-}" == "default" ) ]]; then
    echo "ERROR: CLAUDE_MODEL must name the pinned model (the row says 'same pinned model'; 'default' is not a pin)." >&2
    exit 2
fi

cat <<PLAN
=== PP-E1 leg B — epoch plan ===
epoch:        $EPOCH   (kind $KIND; registered epoch: e1-rows-parity-001)
pairs:        ${PAIRS[*]}   (the four registered N1 neutral pairs)
runs/arm:     $RUNS   interleaved, one run per arm at a time
model:        ${CLAUDE_MODEL:-default}
mechanism:    raw on both arms
arm A:        $ARM_A_LABEL  $ARM_A_ROLE
              root   $ARM_A_ROOT
              commit $ARM_A_COMMIT  tag $ARM_A_DESCRIBE
              Calor.Tasks ${a_tasks:0:12}
arm B:        $ARM_B_LABEL  $ARM_B_ROLE
              root   $ARM_B_ROOT
              commit $ARM_B_COMMIT
              Calor.Tasks ${b_tasks:0:12}
mode:         ${NULL_FLAG:-live (PAID: $((RUNS * 2 * ${#PAIRS[@]})) agent runs)}
output:       $SCRIPT_DIR/epochs/$EPOCH  (pins.json, per-run result.json + agent.json, ppe1-analysis.json)
adjudication: bench/phase0-agent-native/ppe1-analyze.py, then EffectRowsProbeLedgerTests
              (CALOR_REGENERATE_PPE1_LEDGER=1 dotnet test --filter EffectRowsProbeLedger) derives the
              four-valued verdict; publish it in the 0.15.0 release notes whatever it says.
PLAN

[[ "$a_tasks" != "$b_tasks" ]] || {
    echo "ERROR: both arms' Calor.Tasks.dll hash to the same value — the agent-visible compiler is identical on both arms. Epoch void before it starts." >&2
    exit 2; }
[[ "$ARM_A_DESCRIBE" == "v0.14.3" ]] || echo "WARNING: arm A is not checked out at the v0.14.3 tag (git describe: $ARM_A_DESCRIBE). The row names the v0.14.3 release tag as control." >&2

if [[ -z "$NULL_FLAG" && $CONFIRM -ne 1 ]]; then
    echo
    echo "REFUSING TO RUN: this is a paid $((RUNS * 2 * ${#PAIRS[@]}))-run agent epoch. Re-run with --confirm-paid-epoch" >&2
    echo "once the 0.15.0 release PR's author has authorized the spend (or --null-agent for a zero-spend plumbing check)." >&2
    exit 3
fi

OUT="$SCRIPT_DIR/epochs/$EPOCH"
if [[ -d "$OUT" ]] && find "$OUT" -name result.json | grep -q .; then
    echo "ERROR: $OUT already holds results. An epoch is run once; a re-run is a new epoch id (append-only evidence)." >&2
    exit 2
fi

# The ppE1 block is what ppe1-analyze.py and the ledger read; it is appended
# to pins.json even if collection dies part-way, so a partial epoch is still
# labelled as what it was.
stamp_pins() {
    [[ -f "$OUT/pins.json" ]] || return 0
    jq --argjson pairs "$(printf '%s\n' "${PAIRS[@]}" | jq -R . | jq -s .)" \
       '. + {ppE1: {gate: "PP-E1 leg B (annex A-1.11)", metric: "output-tokens-to-green (token-usage.py corrected, A-1.9.1)",
                    pointGate: 1.35, lowerBoundGate: 1.0, cvCap: 0.66,
                    note: "FAILS iff BOTH the one-sided 95% cluster-bootstrap LOWER bound of the median paired ratio (0.15.0 / v0.14.3) exceeds 1.0 AND the point estimate exceeds 1.35; UNDERPOWERED if point > 1.35 with the bound not firing or realized median within-cell CV > 0.66",
                    pairs: $pairs, runner: "bench/phase0-agent-native/run-ppe1-epoch.sh"}}' \
       "$OUT/pins.json" > "$OUT/pins.json.tmp" && mv "$OUT/pins.json.tmp" "$OUT/pins.json"
}
trap stamp_pins EXIT

"$SCRIPT_DIR/run-m5-epoch.sh" \
    --epoch "$EPOCH" --kind "$KIND" \
    --arm-a-dll "$ARM_A_DLL" --arm-b-dll "$ARM_B_DLL" \
    --arm-a-commit "$ARM_A_COMMIT" --arm-b-commit "$ARM_B_COMMIT" \
    --arm-a-repo-root "$ARM_A_ROOT" --arm-b-repo-root "$ARM_B_ROOT" \
    --arm-a-mech raw --arm-b-mech raw \
    --arm-a-label "$ARM_A_LABEL" --arm-b-label "$ARM_B_LABEL" \
    --arm-a-role "$ARM_A_ROLE" --arm-b-role "$ARM_B_ROLE" \
    --runs "$RUNS" --pairs "N1" $NULL_FLAG

stamp_pins
trap - EXIT

echo "--- collection complete; adjudicating leg B ---"
# ppe1-analyze.py refuses any epoch that is not the registered e1-rows-parity-001
# (kind pp-e1-rows-parity) unless told it is a dry run. A --null-agent plumbing
# check, or an epoch under another id, is exactly that: exercise the arithmetic,
# label the output, never record it as leg B.
ANALYZE_FLAGS=()
if [[ -n "$NULL_FLAG" || "$EPOCH" != "e1-rows-parity-001" ]]; then
    ANALYZE_FLAGS=(--dry-run)
    echo "(dry run: ${NULL_FLAG:+null-agent }epoch '$EPOCH' is not the registered leg-B epoch; output is labelled dryRun and cannot be recorded)"
fi
python3 "$SCRIPT_DIR/ppe1-analyze.py" "$OUT" ${ANALYZE_FLAGS[@]+"${ANALYZE_FLAGS[@]}"}
echo "--- now regenerate the ledger in the release PR: CALOR_REGENERATE_PPE1_LEDGER=1 dotnet test tests/Calor.Compiler.Tests --filter EffectRowsProbeLedger ---"
