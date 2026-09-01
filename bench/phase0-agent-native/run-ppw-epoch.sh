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
#       [--epoch w-rows-001] [--runs 8] [--pairs "W-004 W-005 W-006"] \
#       [--leg-b-pairs "W-001 W-002 W-003 W-004 W-006"] \
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
PAIRS_OVERRIDDEN=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --epoch) EPOCH="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --arm-a-repo-root) ARM_A_ROOT="$2"; shift 2 ;;
        --arm-b-repo-root) ARM_B_ROOT="$2"; shift 2 ;;
        --pairs) read -r -a PAIRS <<< "$2"; PAIRS_OVERRIDDEN=1; shift 2 ;;
        --leg-b-pairs) LEG_B_PAIRS="$2"; shift 2 ;;
        --blind-pairs) BLIND_PAIRS="$2"; shift 2 ;;
        --null-agent) NULL_FLAG="--null-agent"; shift ;;
        --confirm-paid-epoch) CONFIRM=1; shift ;;
        -h|--help) sed -n '2,52p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

# A null-agent run is a plumbing check and must never land in a directory an analyzer
# could read as a live epoch — whatever id was passed, not only the registered one.
# DOT-PREFIXED on purpose: gate 12's turn-attribution instrument
# (ppe1-turn-attribution.py) counts every non-dot entry under epochs/ as part of its
# frozen denominator, so a leftover null epoch reds two of its exact-equality tests —
# and the failing assertion helpfully suggests regenerating, which would bake the null
# run into the instrument. A dot entry is skipped there by construction
# (ppe1-turn-attribution.py's `name.startswith(".")`, pinned by
# test_dotfiles_in_epochs_root_are_not_entries), so the trap cannot spring.
if [[ -n "$NULL_FLAG" && "$EPOCH" != .* ]]; then
    EPOCH=".null-${EPOCH#.}"
    EPOCH="${EPOCH%-null}"
    echo "NOTE: --null-agent writes to the dot-prefixed epoch '$EPOCH' (scratch; invisible to the archive instruments, and never a live epoch id)"
fi

# A SUBSET IS A DRY-RUN AFFORDANCE ONLY. The registered epoch's denominator is the
# six pairs A-1.12 froze; running fewer under that id would publish a ledger whose
# `pairs` field is a promise the collection did not keep. A dry epoch may run a
# subset — that is how a collection an API refusal truncated gets completed — and
# ppw-analyze.py --dry-run emits no verdict for one either way.
if [[ $PAIRS_OVERRIDDEN -eq 1 && "$EPOCH" == "w-rows-001" ]]; then
    echo "ERROR: --pairs may not narrow the registered epoch w-rows-001. A-1.12 freezes its six pairs; a subset is a dry epoch, run under its own id." >&2
    exit 2
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
done
for p in "$LEG_B_PAIRS" "$BLIND_PAIRS"; do
    for id in $p; do
        found=0; for q in "${PAIRS[@]}"; do [[ "$q" == "$id" ]] && found=1; done
        [[ $found -eq 1 ]] || problems+=("'$id' (in --leg-b-pairs/--blind-pairs) is not one of ${PAIRS[*]}")
    done
done
[[ -n "$LEG_B_PAIRS" ]] || problems+=("--leg-b-pairs is empty (the leg-B denominator is registered, never defaulted to nothing)")
# Blind floor (roadmap §4.1): the leg-A verdict is read on the blind cells and below two
# of them the PP reads NOT-ADJUDICATED (route a'). A typo that shrinks the set must be
# caught here, not after the epoch has been paid for.
blind_count=0
for _ in $BLIND_PAIRS; do blind_count=$((blind_count + 1)); done
(( blind_count >= 2 )) || problems+=("--blind-pairs has $blind_count entries ('$BLIND_PAIRS'); the registered floor is 2 — below it the PP reads NOT-ADJUDICATED (route a'), so this is refused before spend")
if [[ ${#problems[@]} -gt 0 ]]; then
    echo "ERROR: PP-W-rows pair set is not runnable (${#problems[@]} problem(s)); refusing before any spend:" >&2
    printf '  - %s\n' "${problems[@]}" >&2
    exit 2
fi

if [[ -z "$NULL_FLAG" && ( -z "${CLAUDE_MODEL:-}" || "${CLAUDE_MODEL:-}" == "default" ) ]]; then
    echo "ERROR: CLAUDE_MODEL must name the pinned model ('default' is not a pin)." >&2
    exit 2
fi

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
# A commit pin alone is not provenance: the PRODUCT is built from the WORKING TREE, so an
# uncommitted edit in either checkout compiles into the arm while every pin above still
# reads clean. Both arms must therefore be pristine, and the counts are stamped into
# pins.json so an analyzer can see they were checked.
ARM_A_DIRTY="$(git -C "$ARM_A_ROOT" status --porcelain | wc -l | tr -d ' ')"
ARM_B_DIRTY="$(git -C "$ARM_B_ROOT" status --porcelain | wc -l | tr -d ' ')"
for arm in A B; do
    root_var="ARM_${arm}_ROOT"; dirty_var="ARM_${arm}_DIRTY"
    if [[ "${!dirty_var}" != "0" ]]; then
        echo "ERROR: arm $arm's checkout has ${!dirty_var} uncommitted change(s) (${!root_var}). The product is built from the working tree, so the commit pin would certify a build that does not match the commit. Commit, stash or discard them." >&2
        git -C "${!root_var}" status --porcelain | head -20 >&2
        exit 2
    fi
done
echo "arm A provenance verified: $ARM_A_BASE_TAG + $ARM_A_EXPECTED_COMMIT, diff confined to {$arm_a_diff}, both checkouts clean"
[[ "$ARM_B_DESCRIBE" == "$ARM_B_TAG" ]] || {
    echo "ERROR: arm B is not checked out at the $ARM_B_TAG tag (git describe: $ARM_B_DESCRIBE). §4.1 names the $ARM_B_TAG release tag as treatment; a different build is a different experiment." >&2
    exit 2; }

# ---------------------------------------------------------------------------
# PRE-FLIGHT ARM PROOF — before the plan is printed, and before any spend.
#
# One MSBuild worker shutdown first: outside the measurement (no agent has started),
# a second's cost, and it clears a worker holding an assembly from an earlier build.
# It is NOT the cause of the MSB4064 story — workers reload from disk (probed) — but a
# passing canary only warms the workers it happened to use.
#
# Then one canary build per arm, through that arm's own template and product
# (run-pair.sh --canary-only). Two things come out of it that nothing else could give
# before an agent runs:
#   1. the arm honours its declared policy (permissive vs strict) — the per-run canary
#      repeats this, and stays, to catch mid-epoch drift;
#   2. the arm's compilerHash, read from the canary's own obj/calor/.calor-build-state.json
#      — the same file #1094 archives per run.
# (2) is what makes the disjointness assert a PRE-flight. The post-collection assert
# stays as the mid-epoch guard, but on its own it can only report a mis-pointed arm B
# after the epoch has been paid for; this refuses in about five seconds instead.
#
# GATE ORDER, by cost: everything above this point is free (git metadata, file hashes,
# pair resolution) and refuses with a more specific message, so it runs first. Only then
# are two builds spent, and only then is the plan printed — with these hashes in it,
# because the plan block is what a reader checks before typing --confirm-paid-epoch.
# ---------------------------------------------------------------------------
if command -v dotnet >/dev/null 2>&1; then
    echo "--- dotnet build-server shutdown (once, before the pre-flight) ---"
    dotnet build-server shutdown >/dev/null 2>&1 || true
fi

preflight_pair_dir=""
for d in "$SCRIPT_DIR"/pairs/"${PAIRS[0]}"-*; do [[ -d "$d" ]] && preflight_pair_dir="$d"; done
[[ -n "$preflight_pair_dir" ]] || { echo "ERROR: cannot resolve a pair directory for the pre-flight" >&2; exit 2; }

preflight_arm() {  # <arm-config-key> <repo-root> <arm name for messages>
    local key="$1" root="$2" name="$3" out err rc=0
    # stdout and stderr are kept APART: stdout is the JSON document (pretty-printed, so
    # it spans lines), stderr carries the canary's own narration.
    err="$(mktemp "${TMPDIR:-/tmp}/ppw-preflight-XXXXXX")"
    # --out to a TEMP dir: run-pair.sh mkdir -p's its output directory before anything
    # else, and its default is epochs/adhoc — which would add a non-dot entry to
    # epochs/ and so to gate 12's frozen denominator, just by pre-flighting.
    local scratch; scratch="$(mktemp -d "${TMPDIR:-/tmp}/ppw-preflight-out-XXXXXX")"
    out="$("$SCRIPT_DIR/run-pair.sh" --pair "$preflight_pair_dir" --arm calor \
            --arm-config "$key" --arm-repo-root "$root" --canary-only \
            --out "$scratch" 2>"$err")" || rc=$?
    rm -rf "$scratch"
    if [[ $rc -ne 0 ]]; then
        echo "ERROR: pre-flight canary FAILED for arm $name ($root). No agent was invoked; nothing was spent." >&2
        tail -6 "$err" >&2
        rm -f "$err"
        return 1
    fi
    cat "$err" >&2
    rm -f "$err"
    jq -r '.compilerHash // empty' <<<"$out"
}

echo "--- pre-flight arm proof (one canary build per arm; no agent runs) ---"
PREFLIGHT_A_HASH="$(preflight_arm "$ARM_A_CONFIG" "$ARM_A_ROOT" "A")" || exit 5
PREFLIGHT_B_HASH="$(preflight_arm "$ARM_B_CONFIG" "$ARM_B_ROOT" "B")" || exit 5
if [[ -z "$PREFLIGHT_A_HASH" || -z "$PREFLIGHT_B_HASH" ]]; then
    echo "ERROR: a pre-flight canary produced no compilerHash (A='${PREFLIGHT_A_HASH:-<none>}' B='${PREFLIGHT_B_HASH:-<none>}'). The arm's build state could not be read, so the arms cannot be proven distinct before spending." >&2
    exit 5
fi
if [[ "$PREFLIGHT_A_HASH" == "$PREFLIGHT_B_HASH" ]]; then
    echo "ERROR: both arms' canaries report the SAME compilerHash ($PREFLIGHT_A_HASH) — the arms would run the same compiler, whatever the pins and Calor.Tasks hashes say. This is the failure the per-run canary structurally cannot catch (arm B loading arm A's task assembly passes a superset of parameters, so nothing errors). Refusing BEFORE any spend." >&2
    exit 5
fi
echo "pre-flight OK: arm A compilerHash ${PREFLIGHT_A_HASH:0:12} != arm B ${PREFLIGHT_B_HASH:0:12}"

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
              Calor.Tasks ${a_tasks:0:12}   compilerHash ${PREFLIGHT_A_HASH:0:12} (pre-flight canary)
arm B:        $ARM_B_LABEL  $ARM_B_ROLE
              root   $ARM_B_ROOT
              commit $ARM_B_COMMIT  tag $ARM_B_DESCRIBE
              pair.json arms.$ARM_B_CONFIG (strict)
              Calor.Tasks ${b_tasks:0:12}   compilerHash ${PREFLIGHT_B_HASH:0:12} (pre-flight canary)
mode:         ${NULL_FLAG:-live (PAID: $((RUNS * 2 * ${#PAIRS[@]})) agent runs)}
output:       $SCRIPT_DIR/epochs/$EPOCH  (pins.json with ppW.legBPairs/blindPairs; per-run result.json, agent.json,
              transcript.jsonl, agent-builds.jsonl, calor-build-state.json)
adjudication: bench/phase0-agent-native/ppw-analyze.py (W2) reading pins.json ppW.legBPairs
PLAN

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
       --arg harness_commit "$(git -C "$REPO_ROOT" rev-parse HEAD)" \
       --arg harness_dirty "$(git -C "$REPO_ROOT" status --porcelain -- "$SCRIPT_DIR" | wc -l | tr -d ' ')" \
       --argjson legb "$(printf '%s\n' $LEG_B_PAIRS | jq -R . | jq -s .)" \
       --argjson blind "$(printf '%s\n' $BLIND_PAIRS | jq -R . | jq -s .)" \
       --arg a_cfg "$ARM_A_CONFIG" --arg b_cfg "$ARM_B_CONFIG" \
       --arg a_dirty "${ARM_A_DIRTY:-0}" --arg b_dirty "${ARM_B_DIRTY:-0}" \
       '. + {harnessCommit: $harness_commit,
             harnessDirtyFiles: ($harness_dirty|tonumber),
             ppW: {gate: "PP-W-rows (roadmap v0.16 §4.1; annex A-1.12)",
                   legA: {metric: "median over blind pairs of the per-pair escape-rate delta (A - B)", bar: "one-sided 95% lower bound > 0", effectSize: 0.5, blindFloor: 2},
                   legB: {metric: "output-tokens-to-green (token-usage.py corrected, A-1.9.1)", lowerBoundGate: 1.0,
                          marginRule: "the 0.05 grid line above (null p95 + Monte-Carlo half-width); population and number per ppw-margin-derivation.txt / A-1.12",
                          note: "FAILS iff BOTH the one-sided 95% cluster-bootstrap LOWER bound of the median paired ratio (arm B / arm A) exceeds 1.0 AND the point exceeds the registered margin"},
                   pairs: $pairs, legBPairs: $legb, blindPairs: $blind,
                   excludedFromLegB: ($pairs - $legb),
                   controlArm: {armConfig: $a_cfg, controlArmKind: "pre-rows", permissiveEffects: true},
                   armDirtyFiles: {armA: ($a_dirty|tonumber), armB: ($b_dirty|tonumber)},
                   treatmentArm: {armConfig: $b_cfg, controlArmKind: null, permissiveEffects: false},
                   validity: "every run must archive transcript.jsonl (W1, gate 8); both arms must record distinct compilerHash values (#1094, witnessing different compilers) AND arm A\u0027s buildState.optionsHash must differ from arm B\u0027s, which is what witnesses the permissive policy — a control arm built from the right commit but run STRICT leaves compilerHash unchanged and moves only optionsHash",
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

# run-m5-epoch.sh re-globs the pair ids and WARNS-and-skips one it cannot resolve, which
# would silently shrink the denominator after the spend. The pre-flight above proves every
# id resolves; this proves every id actually produced runs.
missing_results=()
for pid in "${PAIRS[@]}"; do
    find "$OUT" -path "*/${pid}-*/*/run-*/result.json" -print -quit 2>/dev/null | grep -q . \
        || missing_results+=("$pid")
done
if [[ ${#missing_results[@]} -gt 0 ]]; then
    echo "ERROR: no result.json under $OUT for: ${missing_results[*]} — the epoch is incomplete and must not be adjudicated as if it were the registered six." >&2
    exit 4
fi

# M4 — the only witness that each arm ran ITS OWN compiler. The canary cannot catch the
# dangerous direction: arm A's Sdk.targets passes PermissiveEffects and arm B's does not,
# so arm B loading arm A's assembly is a strict SUPERSET of parameters — no MSB4064, no
# error, and arm B silently measured with the v0.14.3 compiler. pins.json states the rule
# in prose for ppw-analyze.py, which does not exist yet; assert it here, now.
compiler_hashes() {  # <arm label>
    find "$OUT" -path "*/$1/run-*/result.json" -exec jq -r '.compilerHash // empty' {} + 2>/dev/null \
        | sort -u | grep -v '^$' || true
}
a_hashes="$(compiler_hashes "$ARM_A_LABEL")"; b_hashes="$(compiler_hashes "$ARM_B_LABEL")"
a_count="$(printf '%s\n' "$a_hashes" | grep -c . || true)"
b_count="$(printf '%s\n' "$b_hashes" | grep -c . || true)"
if [[ "$a_count" != "1" || "$b_count" != "1" ]]; then
    echo "ERROR: each arm must record exactly ONE compilerHash across its runs (arm A: $a_count distinct, arm B: $b_count distinct). A second hash means the arm's product changed mid-epoch; zero means no run archived one." >&2
    printf '  arm A: %s\n  arm B: %s\n' "${a_hashes:-<none>}" "${b_hashes:-<none>}" >&2
    exit 5
fi
if [[ "$a_hashes" == "$b_hashes" ]]; then
    echo "ERROR: both arms recorded the SAME compilerHash ($a_hashes) — the arms did not run different compilers, whatever the pins say. This is the failure the canary structurally cannot catch (arm B loading arm A's task assembly passes a superset of parameters, so nothing errors). Epoch void." >&2
    exit 5
fi
echo "arm compilers verified distinct: A ${a_hashes:0:12} vs B ${b_hashes:0:12}"

echo "--- collection complete; verify the registered leg-B denominator was stamped ---"
python3 "$HARNESS_CAPTURE" leg-b-pairs "$OUT/pins.json"

# Route (a) of the frozen outcome map: every UNMUTATED starter must reproduce the
# multiset A-1.12 froze for it, ON ITS ARM, severity and exit included. That is a
# re-execution, not a citation, so the epoch records its own starter compiles with
# the two arm products it actually ran — ppw-analyze.py refuses to adjudicate
# without them (an adjudication that never re-checked the starters is not one).
echo "--- recording route (a)'s starter compiles on both arms ---"
python3 "$SCRIPT_DIR/ppw-compile.py" \
    --arm-a-dll "$ARM_A_DLL" --arm-b-dll "$ARM_B_DLL" \
    --arm-a-commit "$ARM_A_COMMIT" --arm-b-commit "$ARM_B_COMMIT" \
    --out "$OUT/ppw-starter-compiles.json" \
    || echo "WARNING: starter compiles not recorded — ppw-analyze.py will refuse route (a)" >&2
if [[ -f "$SCRIPT_DIR/ppw-analyze.py" ]]; then
    echo "--- adjudicating (ppw-analyze.py) ---"
    ANALYZE_FLAGS=()
    [[ -z "$NULL_FLAG" && "$EPOCH" == "$REGISTERED_EPOCH" ]] || ANALYZE_FLAGS=(--dry-run)
    python3 "$SCRIPT_DIR/ppw-analyze.py" "$OUT" ${ANALYZE_FLAGS[@]+"${ANALYZE_FLAGS[@]}"}
else
    echo "NOTE: ppw-analyze.py (W2) is not present yet; adjudicate once it lands: python3 bench/phase0-agent-native/ppw-analyze.py $OUT"
fi
