#!/usr/bin/env bash
# ============================================================================
# PP-W-rows epoch runner (roadmap v0.16 §3.1 W1/W2, §4.1; annex A-1.12 when
# registered) — the runner for the six `W-00x` pair ids.
#
# Why a dedicated runner rather than a `W)` group in run-m5-epoch.sh's case:
# run-m5-epoch.sh's `*)` fallback already takes explicit ids, so id resolution
# was never the gap. What PP-W-rows needs and the m5 driver cannot carry is
# the epoch SHAPE the row freezes — two calor arms from ONE pair with different
# `arms.<key>` entries (the pre-rows control arm vs the strict arm, W1's
# --arm-config), the arm-A/arm-B tag expectations (v0.14.3 / v0.15.0), a
# loud pre-spend failure when any of the six pair directories is missing (m5
# WARNs and skips, which would silently shrink the denominator), and the
# `ppW` pins block carrying the registered `legBPairs` / `blindPairs` that
# ppw-analyze.py reads instead of a script default. run-ppe1-epoch.sh is the
# same pattern for PP-E1; this mirrors its safety rails.
#
# Shape (roadmap §4.1, transcribed; nothing is a parameter unless the row
# leaves it open):
#   - six pairs W-001 … W-006 (EXACT-id directory match `pairs/W-00x-*`; the
#     existing W1-…/W2-…/W3-…/W5A-… directories cannot collide)
#   - N runs/arm (set by the A:81 dry run; default 8), INTERLEAVED one run per
#     arm at a time (run-m5-epoch.sh's non-m5 branch), same pinned model
#   - edit mechanism `raw` on BOTH arms; identical harness configuration
#   - arm A = control  = tag `v0.14.3` + commit 283ec9f9 (branch
#     `arm/v0.14.3-pre-rows`, never merged; diff confined to Calor.Tasks +
#     Sdk.targets, threading only the existing --permissive-effects policy
#     through MSBuild; compiler semantics v0.14.3's), built as a PRODUCT, run
#     under pair.json `arms["calor-pre-rows"]` (permissiveEffects true +
#     controlArmKind "pre-rows", starter `before/`) — run-pair.sh proves the
#     arm's Calor.Tasks honours <CalorPermissiveEffects> with a canary first
#     arm B = treatment = the `v0.15.0` release tag, strict, `arms.calor`
#     (starter `after/`)
#   - per-arm --arm-repo-root; pins.json records both roots AND both distinct
#     Calor.Tasks.dll hashes; run-pair.sh stamps armRepoRoot, compilerHash
#     (#1094), controlArmKind and turns.assistantMessages into every result.json
#   - pins.json `ppW.legBPairs` (default W-001 W-002 W-003 W-004 W-006; W-005
#     is leg-A only — its arm-B starter does not build) and `ppW.blindPairs`
#     (default W-001 W-004 W-006)
#
# THIS IS A PAID AGENT EPOCH (6 pairs x N x 2 arms). The script prints its
# plan and REFUSES to run without --confirm-paid-epoch. `--null-agent` (zero
# spend) needs no confirmation and never lands in a registered epoch id.
#
# Usage:
#   CLAUDE_MODEL=<pinned model> bench/phase0-agent-native/run-ppw-epoch.sh \
#       --arm-a-repo-root <v0.14.3 checkout, built Release> \
#       --arm-b-repo-root <v0.15.0 checkout, built Release> \
#       [--epoch w-rows-001] [--runs 8] [--leg-b-pairs "W-001 W-002 W-003 W-004 W-006"] \
#       [--blind-pairs "W-001 W-004 W-006"] [--null-agent] --confirm-paid-epoch
#
# Build each arm's PRODUCT first, in its own checkout (never the harness
# checkout for either arm):
#   git worktree add <root> v0.14.3   # arm A;  arm B: v0.15.0
#   dotnet build <root>/src/Calor.Compiler -c Release
#   dotnet build <root>/src/Calor.Tasks    -c Release
#   dotnet build <root>/src/Calor.Runtime  -c Release
# ============================================================================
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
HARNESS_CAPTURE="$SCRIPT_DIR/harness-capture.py"

EPOCH="w-rows-001"
REGISTERED_EPOCH="w-rows-001"
RUNS=8
ARM_A_ROOT=""; ARM_B_ROOT=""
NULL_FLAG=""; CONFIRM=0
ARM_A_LABEL="calor+v0.14.3-pre-rows"; ARM_B_LABEL="calor+v0.15.0"
ARM_A_ROLE="control (v0.14.3 release tag, pre-rows arm: permissive effects)"
ARM_B_ROLE="treatment (v0.15.0 release tag, strict)"
ARM_A_CONFIG="calor-pre-rows"; ARM_B_CONFIG="calor"
# Arm A is NOT the bare v0.14.3 tag: the tag's Calor.Tasks cannot receive a permissive
# policy from MSBuild, so the registered control arm is tag v0.14.3 + ONE commit on
# branch `arm/v0.14.3-pre-rows` (never merged) whose diff is confined to
# src/Calor.Tasks/CompileCalor.cs and src/Calor.Sdk/Sdk/Sdk.targets and only threads
# the compiler's EXISTING --permissive-effects policy through MSBuild
# (<CalorPermissiveEffects>); compiler semantics are v0.14.3's. The runner requires
# arm A's checkout to be exactly that commit, and run-pair.sh's canary still proves the
# property is honoured before spend.
ARM_A_TAG="v0.14.3"; ARM_B_TAG="v0.15.0"
ARM_A_BASE_TAG="v0.14.3"
ARM_A_EXPECTED_COMMIT="283ec9f9964ddd5b21da15b646a0dd77d53de99e"
ARM_A_BRANCH="arm/v0.14.3-pre-rows"
KIND="pp-w-rows"
PAIRS=(W-001 W-002 W-003 W-004 W-005 W-006)
LEG_B_PAIRS="W-001 W-002 W-003 W-004 W-006"
BLIND_PAIRS="W-001 W-004 W-006"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --epoch) EPOCH="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --arm-a-repo-root) ARM_A_ROOT="$2"; shift 2 ;;
        --arm-b-repo-root) ARM_B_ROOT="$2"; shift 2 ;;
        --leg-b-pairs) LEG_B_PAIRS="$2"; shift 2 ;;
        --blind-pairs) BLIND_PAIRS="$2"; shift 2 ;;
        --null-agent) NULL_FLAG="--null-agent"; shift ;;
        --confirm-paid-epoch) CONFIRM=1; shift ;;
        -h|--help) sed -n '2,52p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

# A null-agent run never lands in the registered directory: it is a plumbing
# check, and the analyzer must refuse to adjudicate an epoch that holds one.
if [[ -n "$NULL_FLAG" && "$EPOCH" == "$REGISTERED_EPOCH" ]]; then
    EPOCH="${REGISTERED_EPOCH}-null"
    echo "NOTE: --null-agent forces the epoch id to '$EPOCH' (the registered id is for the live epoch only)"
fi

[[ -n "$ARM_A_ROOT" && -n "$ARM_B_ROOT" ]] || {
    echo "ERROR: --arm-a-repo-root (v0.14.3 product build) and --arm-b-repo-root (v0.15.0 product build) are required." >&2
    echo "       Both arms MUST be per-arm checkouts; the harness checkout is neither arm." >&2
    exit 2; }
for r in "$ARM_A_ROOT" "$ARM_B_ROOT"; do
    [[ -d "$r/src/Calor.Tasks" ]] || { echo "ERROR: not a calor checkout: $r" >&2; exit 2; }
done
ARM_A_ROOT="$(cd "$ARM_A_ROOT" && pwd -P)"; ARM_B_ROOT="$(cd "$ARM_B_ROOT" && pwd -P)"
[[ "$ARM_A_ROOT" != "$ARM_B_ROOT" ]] || { echo "ERROR: both arms share a repo root — no product contrast" >&2; exit 2; }
ARM_A_DLL="$ARM_A_ROOT/src/Calor.Compiler/bin/Release/net10.0/calor.dll"
ARM_B_DLL="$ARM_B_ROOT/src/Calor.Compiler/bin/Release/net10.0/calor.dll"
ARM_A_COMMIT="$(git -C "$ARM_A_ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
ARM_B_COMMIT="$(git -C "$ARM_B_ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
ARM_A_DESCRIBE="$(git -C "$ARM_A_ROOT" describe --tags --exact-match 2>/dev/null || echo '(not on a tag)')"
ARM_B_DESCRIBE="$(git -C "$ARM_B_ROOT" describe --tags --exact-match 2>/dev/null || echo '(not on a tag)')"

for need in "$ARM_A_DLL" "$ARM_B_DLL" \
            "$ARM_A_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" \
            "$ARM_B_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" \
            "$ARM_A_ROOT/src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll" \
            "$ARM_B_ROOT/src/Calor.Runtime/bin/Release/net10.0/Calor.Runtime.dll"; do
    [[ -f "$need" ]] || { echo "ERROR: missing $need — build the PRODUCT per arm (see header)" >&2; exit 2; }
done
a_tasks="$(shasum "$ARM_A_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" | awk '{print $1}')"
b_tasks="$(shasum "$ARM_B_ROOT/src/Calor.Tasks/bin/Release/net10.0/Calor.Tasks.dll" | awk '{print $1}')"

# ---------------------------------------------------------------------------
# Pair-set pre-flight: every one of the six ids must resolve to EXACTLY one
# directory `pairs/<id>-*`, and each pair.json must carry both arm entries
# with admitted configs (the pre-rows arm's pin, W1). Missing or malformed
# pairs are listed together and the runner exits BEFORE any spend — never a
# warn-and-skip that would silently shrink the registered denominator.
# ---------------------------------------------------------------------------
declare -a PAIR_DIRS=()
problems=()
for pid in "${PAIRS[@]}"; do
    matches=()
    for d in "$SCRIPT_DIR"/pairs/"$pid"-*; do [[ -d "$d" ]] && matches+=("$d"); done
    if [[ ${#matches[@]} -eq 0 ]]; then problems+=("$pid: no directory pairs/$pid-*"); continue; fi
    if [[ ${#matches[@]} -gt 1 ]]; then problems+=("$pid: ambiguous — ${matches[*]}"); continue; fi
    d="${matches[0]}"
    [[ -f "$d/pair.json" && -f "$d/spec.md" ]] || { problems+=("$pid: $d lacks pair.json/spec.md"); continue; }
    for key in "$ARM_A_CONFIG" "$ARM_B_CONFIG"; do
        if ! verdict="$(python3 "$HARNESS_CAPTURE" pair-config "$d/pair.json" "$key" --arm calor)"; then
            problems+=("$pid: arms.$key rejected — $(jq -r '.reason' <<<"$verdict" 2>/dev/null || echo "$verdict")")
            continue
        fi
        fixture="$(jq -r '.fixture' <<<"$verdict")"
        [[ -d "$d/$fixture" ]] || problems+=("$pid: arms.$key fixture directory missing: $d/$fixture")
        if [[ "$key" == "$ARM_A_CONFIG" && "$(jq -r '.controlArmKind' <<<"$verdict")" != "pre-rows" ]]; then
            problems+=("$pid: arms.$key must be the pre-rows control arm (controlArmKind \"pre-rows\")")
        fi
        if [[ "$key" == "$ARM_B_CONFIG" && "$(jq -r '.controlArmKind' <<<"$verdict")" != "null" ]]; then
            problems+=("$pid: arms.$key must be the strict arm (no controlArmKind)")
        fi
    done
    PAIR_DIRS+=("$d")
done
for p in "$LEG_B_PAIRS" "$BLIND_PAIRS"; do
    for id in $p; do
        found=0; for q in "${PAIRS[@]}"; do [[ "$q" == "$id" ]] && found=1; done
        [[ $found -eq 1 ]] || problems+=("'$id' (in --leg-b-pairs/--blind-pairs) is not one of ${PAIRS[*]}")
    done
done
[[ -n "$LEG_B_PAIRS" ]] || problems+=("--leg-b-pairs is empty (the leg-B denominator is registered, never defaulted to nothing)")
if [[ ${#problems[@]} -gt 0 ]]; then
    echo "ERROR: PP-W-rows pair set is not runnable (${#problems[@]} problem(s)); refusing before any spend:" >&2
    printf '  - %s\n' "${problems[@]}" >&2
    exit 2
fi

if [[ -z "$NULL_FLAG" && ( -z "${CLAUDE_MODEL:-}" || "${CLAUDE_MODEL:-}" == "default" ) ]]; then
    echo "ERROR: CLAUDE_MODEL must name the pinned model ('default' is not a pin)." >&2
    exit 2
fi

cat <<PLAN
=== PP-W-rows — epoch plan ===
epoch:        $EPOCH   (kind $KIND; registered epoch: $REGISTERED_EPOCH)
pairs:        ${PAIRS[*]}   (six registered W-00x pairs, exact-id match)
leg B pairs:  $LEG_B_PAIRS   (W-005 leg A only unless overridden)
blind pairs:  $BLIND_PAIRS   (floor two)
runs/arm:     $RUNS   interleaved, one run per arm at a time
model:        ${CLAUDE_MODEL:-default}
mechanism:    raw on both arms
arm A:        $ARM_A_LABEL  $ARM_A_ROLE
              root   $ARM_A_ROOT
              commit $ARM_A_COMMIT  (registered: $ARM_A_BASE_TAG + $ARM_A_EXPECTED_COMMIT on $ARM_A_BRANCH; describe: $ARM_A_DESCRIBE)
              pair.json arms.$ARM_A_CONFIG (permissive, controlArmKind pre-rows)
              Calor.Tasks ${a_tasks:0:12}
arm B:        $ARM_B_LABEL  $ARM_B_ROLE
              root   $ARM_B_ROOT
              commit $ARM_B_COMMIT  tag $ARM_B_DESCRIBE
              pair.json arms.$ARM_B_CONFIG (strict)
              Calor.Tasks ${b_tasks:0:12}
mode:         ${NULL_FLAG:-live (PAID: $((RUNS * 2 * ${#PAIRS[@]})) agent runs)}
output:       $SCRIPT_DIR/epochs/$EPOCH  (pins.json with ppW.legBPairs/blindPairs; per-run result.json, agent.json,
              transcript.jsonl, agent-builds.jsonl, calor-build-state.json)
adjudication: bench/phase0-agent-native/ppw-analyze.py (W2) reading pins.json ppW.legBPairs
PLAN

[[ "$a_tasks" != "$b_tasks" ]] || {
    echo "ERROR: both arms' Calor.Tasks.dll hash to the same value — the agent-visible compiler is identical on both arms. Epoch void before it starts." >&2
    exit 2; }
# Arm A provenance (A-1.12's exact statement): tag v0.14.3 + commit $ARM_A_EXPECTED_COMMIT,
# whose diff against the tag touches only Calor.Tasks + Sdk.targets. Both halves are
# checked here, before spend; a bare v0.14.3 checkout is refused (its Tasks build cannot
# honour the property — the canary would refuse it too, but this names the cause).
if [[ "$ARM_A_COMMIT" != "$ARM_A_EXPECTED_COMMIT" ]]; then
    echo "ERROR: arm A must be checked out at $ARM_A_BRANCH = $ARM_A_EXPECTED_COMMIT (tag $ARM_A_BASE_TAG + the Tasks permissive passthrough); got $ARM_A_COMMIT ($ARM_A_DESCRIBE)." >&2
    exit 2
fi
arm_a_diff="$(git -C "$ARM_A_ROOT" diff --name-only "$ARM_A_BASE_TAG" "$ARM_A_EXPECTED_COMMIT" 2>/dev/null | sort | tr '\n' ' ')"
[[ "$arm_a_diff" == "src/Calor.Sdk/Sdk/Sdk.targets src/Calor.Tasks/CompileCalor.cs " ]] || {
    echo "ERROR: arm A's diff against $ARM_A_BASE_TAG is not confined to src/Calor.Tasks/CompileCalor.cs + src/Calor.Sdk/Sdk/Sdk.targets: [$arm_a_diff]" >&2
    exit 2; }
echo "arm A provenance verified: $ARM_A_BASE_TAG + $ARM_A_EXPECTED_COMMIT, diff confined to {$arm_a_diff}"
[[ "$ARM_B_DESCRIBE" == "$ARM_B_TAG" ]] || echo "WARNING: arm B is not checked out at the $ARM_B_TAG tag (git describe: $ARM_B_DESCRIBE). §4.1 names the $ARM_B_TAG release tag as treatment." >&2

if [[ -z "$NULL_FLAG" && $CONFIRM -ne 1 ]]; then
    echo
    echo "REFUSING TO RUN: this is a paid $((RUNS * 2 * ${#PAIRS[@]}))-run agent epoch. Re-run with --confirm-paid-epoch" >&2
    echo "once the spend has been authorized (or --null-agent for a zero-spend plumbing check)." >&2
    exit 3
fi

OUT="$SCRIPT_DIR/epochs/$EPOCH"
if [[ -d "$OUT" ]] && find "$OUT" -name result.json | grep -q .; then
    echo "ERROR: $OUT already holds results. An epoch is run once; a re-run is a new epoch id (append-only evidence)." >&2
    exit 2
fi

# The ppW block is what ppw-analyze.py reads; it is appended to pins.json even
# if collection dies part-way, so a partial epoch is still labelled as what it
# was. legBPairs / blindPairs live HERE (registered at A-1.12), never as a
# script default in the analyzer.
stamp_pins() {
    [[ -f "$OUT/pins.json" ]] || return 0
    jq --argjson pairs "$(printf '%s\n' "${PAIRS[@]}" | jq -R . | jq -s .)" \
       --argjson legb "$(printf '%s\n' $LEG_B_PAIRS | jq -R . | jq -s .)" \
       --argjson blind "$(printf '%s\n' $BLIND_PAIRS | jq -R . | jq -s .)" \
       --arg a_cfg "$ARM_A_CONFIG" --arg b_cfg "$ARM_B_CONFIG" \
       '. + {ppW: {gate: "PP-W-rows (roadmap v0.16 §4.1; annex A-1.12)",
                   legA: {metric: "median over blind pairs of the per-pair escape-rate delta (A - B)", bar: "one-sided 95% lower bound > 0", effectSize: 0.5, blindFloor: 2},
                   legB: {metric: "output-tokens-to-green (token-usage.py corrected, A-1.9.1)", lowerBoundGate: 1.0,
                          marginRule: "the 0.05 grid line above (null p95 + Monte-Carlo half-width); population and number per ppw-margin-derivation.txt / A-1.12",
                          note: "FAILS iff BOTH the one-sided 95% cluster-bootstrap LOWER bound of the median paired ratio (arm B / arm A) exceeds 1.0 AND the point exceeds the registered margin"},
                   pairs: $pairs, legBPairs: $legb, blindPairs: $blind,
                   excludedFromLegB: ($pairs - $legb),
                   controlArm: {armConfig: $a_cfg, controlArmKind: "pre-rows", permissiveEffects: true},
                   treatmentArm: {armConfig: $b_cfg, controlArmKind: null, permissiveEffects: false},
                   validity: "every run must archive transcript.jsonl (W1, gate 8) and both arms must record distinct compilerHash values (#1094)",
                   runner: "bench/phase0-agent-native/run-ppw-epoch.sh"}}' \
       "$OUT/pins.json" > "$OUT/pins.json.tmp" && mv "$OUT/pins.json.tmp" "$OUT/pins.json"
}
trap stamp_pins EXIT

"$SCRIPT_DIR/run-m5-epoch.sh" \
    --epoch "$EPOCH" --kind "$KIND" \
    --arm-a-dll "$ARM_A_DLL" --arm-b-dll "$ARM_B_DLL" \
    --arm-a-commit "$ARM_A_COMMIT" --arm-b-commit "$ARM_B_COMMIT" \
    --arm-a-repo-root "$ARM_A_ROOT" --arm-b-repo-root "$ARM_B_ROOT" \
    --arm-a-mech raw --arm-b-mech raw \
    --arm-a-config "$ARM_A_CONFIG" --arm-b-config "$ARM_B_CONFIG" \
    --arm-a-label "$ARM_A_LABEL" --arm-b-label "$ARM_B_LABEL" \
    --arm-a-role "$ARM_A_ROLE" --arm-b-role "$ARM_B_ROLE" \
    --runs "$RUNS" --pairs "${PAIRS[*]}" $NULL_FLAG

stamp_pins
trap - EXIT

echo "--- collection complete; verify the registered leg-B denominator was stamped ---"
python3 "$HARNESS_CAPTURE" leg-b-pairs "$OUT/pins.json"
if [[ -f "$SCRIPT_DIR/ppw-analyze.py" ]]; then
    echo "--- adjudicating (ppw-analyze.py) ---"
    ANALYZE_FLAGS=()
    [[ -z "$NULL_FLAG" && "$EPOCH" == "$REGISTERED_EPOCH" ]] || ANALYZE_FLAGS=(--dry-run)
    python3 "$SCRIPT_DIR/ppw-analyze.py" "$OUT" ${ANALYZE_FLAGS[@]+"${ANALYZE_FLAGS[@]}"}
else
    echo "NOTE: ppw-analyze.py (W2) is not present yet; adjudicate once it lands: python3 bench/phase0-agent-native/ppw-analyze.py $OUT"
fi
