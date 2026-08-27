#!/usr/bin/env bash
# ============================================================================
# Phase 0 two-arm pair runner (agent-native benchmark)
# ============================================================================
#
# Runs ONE pair in ONE arm, N times, per the gates doc
# (docs/plans/agent-native-gates.md). Produces per-run JSON results and a
# journal of harness-observed iterations with silent held-out test outcomes.
#
# Usage:
#   ./run-pair.sh --pair pairs/W3-001-audit-log --arm calor            # 1 run
#   ./run-pair.sh --pair <dir> --arm csharp --runs 3                   # N runs
#   ./run-pair.sh --pair <dir> --arm calor --null-agent                # plumbing
#         validation: applies the reference solution instead of invoking the
#         agent — zero API spend, exercises workspaces/shims/tests/metrics
#   ./run-pair.sh ... --out epochs/dry-run-001                         # results dir
#
# Iteration definition (gates doc §2): one harness-observed build-or-test
# invocation following >=1 workspace edit. Observed via a `dotnet` PATH shim
# that journals invocations, hashes the src tree to detect edits, and silently
# runs the held-out suite after each build/test.
#
# Per-turn capture (roadmap v0.16 §3.1 W1; §2.1 S1 mechanics): the agent runs
# under `claude --print --verbose --output-format stream-json
# --forward-subagent-text`, streamed as
#     tee transcript.jsonl | jq -c 'select(.type=="result")' > agent.json
# so agent.json keeps exactly the result envelope it always held (token-usage.py
# and detect_invalid_run keep their input) and transcript.jsonl archives every
# event. A run without transcript.jsonl is INVALID (gate 8). result.json gains
# `turns.assistantMessages` (distinct assistant message.id — harness-capture.py),
# `agent-builds.jsonl` (the agent's own dotnet build/test + calor calls with
# their output) and `compilerHash` from the workspace's obj/calor/
# .calor-build-state.json (#1094), archived as calor-build-state.json.
#
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
# W1: the ONE reader of transcript.jsonl / .calor-build-state.json / the arm
# config pin (shared with run-bundle.sh and the python tests).
HARNESS_CAPTURE="$SCRIPT_DIR/harness-capture.py"

# ---------------------------------------------------------------------------
# Invalid-run detection (gates doc §0.2): invalid, crashed, or API-errored
# runs (e.g. "You've hit your session limit" — epoch feasibility-dry-001) are
# auto-detected, re-run on a fresh workspace up to MAX_INVALID_RETRIES times,
# and after the cap counted as task failure with "invalid": true.
#
# Prints the detection reason and returns 0 if the run is INVALID; returns 1
# if the run looks valid. Args: <ws_out> [agent_exit_code].
# ---------------------------------------------------------------------------
MAX_INVALID_RETRIES=2
INVALID_MARKERS=("hit your session limit" "rate limit" "overloaded" "api error")

detect_invalid_run() {
    local ws_out="$1" agent_rc="${2:-0}"
    local aj="$ws_out/agent.json"

    if [[ ! -s "$aj" ]]; then
        echo "agent.json missing or empty"
        return 0
    fi
    if ! jq -e . "$aj" >/dev/null 2>&1; then
        echo "agent.json is not valid JSON"
        return 0
    fi
    # Rate-limit / API-error markers, case-insensitive, checked in both the
    # parsed .result field and the raw file content
    local content marker
    content="$( { jq -r '.result // empty' "$aj" 2>/dev/null; cat "$aj"; } | tr '[:upper:]' '[:lower:]')"
    for marker in "${INVALID_MARKERS[@]}"; do
        if [[ "$content" == *"$marker"* ]]; then
            echo "agent output matches error marker: \"$marker\""
            return 0
        fi
    done
    # Crashed agent that produced no observed work
    if [[ "$agent_rc" -ne 0 && ! -s "$ws_out/journal.jsonl" ]]; then
        echo "agent exit code $agent_rc with empty journal.jsonl"
        return 0
    fi
    # W1 pin (roadmap v0.16 §3.1 W1, gate 8): a run without its per-turn
    # transcript is invalid — the turn-attribution instrument (gate 12) has no
    # input, and "why more turns" would again be unanswerable from the archive.
    # Checked AFTER the marker scan so a rate-limited run still reports the
    # marker as its reason.
    if [[ ! -s "$ws_out/transcript.jsonl" ]]; then
        echo "transcript.jsonl missing or empty (W1: a run without a per-turn transcript is invalid)"
        return 0
    fi
    return 1
}

# Annex A-1.3 instrumentation item 4 (#826 review M2): archive the
# declared-done source verbatim to $ws_out/final-src/ — M-G4's weakening
# check diffs this against the frozen seeded declaration. Recursive (agents
# may nest files) and fail-loud: an incomplete archive invalidates the run,
# because M-G4 without its input would misread as weakened-by-rule.
# Convention matches detect_invalid_run: return 0 + print reason = invalid.
archive_final_src() {
    local ws="$1" ws_out="$2"
    rm -rf "$ws_out/final-src"
    mkdir -p "$ws_out/final-src"
    ( cd "$ws/src" && find . -type f \( -name '*.calr' -o -name '*.cs' \) \
        -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
      | while IFS= read -r -d '' f; do
            mkdir -p "$ws_out/final-src/$(dirname "$f")"
            cp "$f" "$ws_out/final-src/$f"
        done )
    local src_count arch_count
    src_count=$(find "$ws/src" -type f \( -name '*.calr' -o -name '*.cs' \) \
        -not -path '*/obj/*' -not -path '*/bin/*' | wc -l | tr -d ' ')
    arch_count=$(find "$ws_out/final-src" -type f | wc -l | tr -d ' ')
    if [[ "$arch_count" != "$src_count" ]]; then
        echo "declared-done archival incomplete ($arch_count/$src_count files)"
        return 0
    fi
    return 1
}

# #1094 (W1): archive the compiler's own attestation of which product built
# the agent's code — obj/calor/.calor-build-state.json, carrying
# compilerHash — before the workspace is deleted. This turns arm provenance
# from "the operator passed a flag" into "the compiler said so" (the
# distinction between the void w5-parity-001 and the valid w5-parity-002).
# Not run-invalidating: the C# arm has none, and a calor-arm agent may never
# have built (a censored run); extract_metrics then falls back to the
# harness's own final build state, labelled harness-final-build.
archive_build_state() {
    local ws="$1" ws_out="$2" source="${3:-agent-workspace}"
    local state="$ws/src/obj/calor/.calor-build-state.json"
    [[ -s "$state" ]] || return 0
    cp "$state" "$ws_out/calor-build-state.json"
    echo "$source" > "$ws_out/.build-state-source"
    return 0
}

# Test entrypoint: ./run-pair.sh --detect-invalid <ws_out> [agent_exit_code]
# Exits 0 (and prints the reason) if the run directory is invalid, 1 if valid.
if [[ "${1:-}" == "--detect-invalid" ]]; then
    [[ -n "${2:-}" ]] || { echo "Usage: --detect-invalid <ws_out> [agent_exit_code]" >&2; exit 2; }
    if reason="$(detect_invalid_run "$2" "${3:-0}")"; then
        echo "INVALID: $reason"
        exit 0
    fi
    echo "VALID"
    exit 1
fi

# Test entrypoint: ./run-pair.sh --check-pair-config <pair.json> <arm-config-key> [calor|csharp]
# Runs the same admission check_pins applies (via harness-capture.py) and
# exits 0 (admitted, JSON on stdout) or 3 (rejected, reason in the JSON) —
# the exit code check_pins uses for a pin violation.
if [[ "${1:-}" == "--check-pair-config" ]]; then
    [[ -n "${2:-}" && -n "${3:-}" ]] || { echo "Usage: --check-pair-config <pair.json> <arm-config-key> [calor|csharp]" >&2; exit 2; }
    arm_arg=()
    [[ -n "${4:-}" ]] && arm_arg=(--arm "$4")
    if python3 "$HARNESS_CAPTURE" pair-config "$2" "$3" ${arm_arg[@]+"${arm_arg[@]}"}; then
        exit 0
    fi
    exit 3
fi

PAIR_DIR=""
ARM=""
RUNS=1
# Starting run index. Interleaved epochs invoke run-pair once per run per arm, and
# without an offset every invocation would restart at run-1 and overwrite the previous
# one — the same silent-data-loss shape as two arms sharing an output directory.
RUN_OFFSET=0
OUT_DIR="$SCRIPT_DIR/epochs/adhoc"
NULL_AGENT=0
ITERATION_BUDGET=10
TIMEOUT_SECS=600
EXEMPLAR_FILE=""
EDIT_MECHANISM="raw"
CALOR_DLL_OVERRIDE=""
ARM_REPO_ROOT=""
ARM_LABEL_OVERRIDE=""
# W1 / roadmap §4.1: which `arms.<key>` entry of pair.json this invocation
# runs under. Defaults to the arm language (`calor` / `csharp`), which is every
# pre-0.16 pair. PP-W-rows runs two CALOR arms from one pair — the pre-rows
# control arm (`calor-pre-rows`: permissive, controlArmKind "pre-rows", its
# own `before/` starter) and the strict arm (`calor`) — so the key is a
# parameter. The entry's `fixture` names the starter directory under the pair.
ARM_CONFIG_KEY=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --pair) PAIR_DIR="$2"; shift 2 ;;
        --arm) ARM="$2"; shift 2 ;;
        --arm-config) ARM_CONFIG_KEY="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --out) OUT_DIR="$2"; shift 2 ;;
        --null-agent) NULL_AGENT=1; shift ;;
        --exemplar) EXEMPLAR_FILE="$2"; shift 2 ;;
        --edit-mechanism) EDIT_MECHANISM="$2"; shift 2 ;;
        --calor-dll) CALOR_DLL_OVERRIDE="$2"; shift 2 ;;
        --arm-repo-root) ARM_REPO_ROOT="$2"; shift 2 ;;
        --run-offset) RUN_OFFSET="$2"; shift 2 ;;
        --arm-label) ARM_LABEL_OVERRIDE="$2"; shift 2 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

[[ -n "$PAIR_DIR" && -n "$ARM" ]] || { echo "Usage: --pair <dir> --arm calor|csharp [--arm-config <arms.key>] [--runs N] [--null-agent] [--exemplar <file>] [--edit-mechanism raw|mcp-file|mcp-node] [--calor-dll <path>] [--arm-repo-root <path>] [--out <dir>]" >&2; exit 2; }

# Per-arm PRODUCT checkout (guarantees plan G5, A-1.3 epoch): --calor-dll pins
# only the CLI/MCP/envelope build; the workspace's own `dotnet build` binds
# Calor.Tasks / Calor.Sdk / Calor.Runtime and the emitter to the repo root
# substituted into the CalorArm template. A workstream that changes those (the
# v0.10 verify gate did — #826 touched Calor.Tasks + Sdk.targets + template)
# needs the whole PRODUCT bound per arm: --arm-repo-root points the template,
# the Tasks-libz3 pin, and the verify canary at that checkout, while THIS
# script (the measurement harness) keeps running from the current checkout.
if [[ -n "$ARM_REPO_ROOT" ]]; then
    [[ -d "$ARM_REPO_ROOT/src/Calor.Tasks" && -d "$ARM_REPO_ROOT/bench/phase0-agent-native/templates" ]] \
        || { echo "ERROR: --arm-repo-root is not a calor checkout: $ARM_REPO_ROOT" >&2; exit 2; }
    ARM_REPO_ROOT="$(cd "$ARM_REPO_ROOT" && pwd -P)"
else
    ARM_REPO_ROOT="$REPO_ROOT"
fi
[[ "$ARM" == "calor" || "$ARM" == "csharp" ]] || { echo "--arm must be calor|csharp" >&2; exit 2; }
[[ -n "$ARM_CONFIG_KEY" ]] || ARM_CONFIG_KEY="$ARM"
case "$EDIT_MECHANISM" in
    raw) ;;
    mcp-file)
        # UNGATED (loop plan M3 PR 4): WS2 D2.4 shipped the transactional MCP
        # write path (calor_file_write, #797) with write-path robustness
        # (#798), and the harness now registers the calor MCP server for this
        # arm (see the mcp-config block in prep and the --mcp-config args on
        # the claude invocation). Enforcement stays two-sided: the PreToolUse
        # hook blocks raw Edit/Write on .calr, and the prompt states the
        # constraint. Live runs are calor-arm only — the C# arm has no .calr
        # surface to constrain. calor.dll presence is checked after CLI
        # resolution below.
        if [[ "$NULL_AGENT" != "1" && "$ARM" != "calor" ]]; then
            echo "ERROR: --edit-mechanism mcp-file applies to the calor arm only (the C# arm has no .calr edit surface)" >&2
            exit 2
        fi
        ;;
    mcp-node)
        # Accepted for forward-compatible labeling only: node-level MCP edit
        # tools are descoped per Call 1/E1, so there is nothing to enforce —
        # no hook, no prompt constraint. Records/labels still carry the value.
        echo "WARNING: --edit-mechanism mcp-node is descoped (Call 1/E1): accepted for labeling only, no enforcement exists for node tools" >&2
        ;;
    *) echo "--edit-mechanism must be raw|mcp-file|mcp-node" >&2; exit 2 ;;
esac

# --exemplar <file>: append the file's content to the agent prompt (E1a
# attribution experiment, machine-zone.md §9). Results for the exemplar arm
# are labeled "<arm>+exemplar" so they never collide with the baseline arm.
ARM_LABEL="$ARM"
if [[ -n "$EXEMPLAR_FILE" ]]; then
    [[ -s "$EXEMPLAR_FILE" ]] || { echo "--exemplar file missing or empty: $EXEMPLAR_FILE" >&2; exit 2; }
    EXEMPLAR_FILE="$(cd "$(dirname "$EXEMPLAR_FILE")" && pwd)/$(basename "$EXEMPLAR_FILE")"
    ARM_LABEL="${ARM}+exemplar"
fi
# Arm-constraint labeling (loop plan D4.2): a non-default edit mechanism is a
# distinct arm variant, suffixed like the exemplar pattern so results never
# collide with the unconstrained baseline (e.g. "calor+mcp-file").
if [[ "$EDIT_MECHANISM" != "raw" ]]; then
    ARM_LABEL="${ARM_LABEL}+${EDIT_MECHANISM}"
fi
# Explicit arm-label override (guarantees plan G5): the A-1.3 epoch runs the
# SAME arm/mechanism (calor+raw) in both arms — the contrast is the product
# build + verify gate, so the caller must name the arms to keep results from
# colliding (e.g. calor-v09-control / calor-v10-verify).
if [[ -n "$ARM_LABEL_OVERRIDE" ]]; then
    ARM_LABEL="$ARM_LABEL_OVERRIDE"
fi
PAIR_DIR="$(cd "$PAIR_DIR" && pwd)"
PAIR_ID="$(jq -r .id "$PAIR_DIR/pair.json")"
# Arm config resolution (W1): the admission verdict is applied by check_pins
# below (exit 3 on rejection, the pre-0.16 behaviour for a pin violation);
# the fixture / controlArmKind / permissive fields are needed by materialize
# and result.json, so they are resolved once here.
ARM_CONFIG_JSON="$(python3 "$HARNESS_CAPTURE" pair-config "$PAIR_DIR/pair.json" "$ARM_CONFIG_KEY" --arm "$ARM" || true)"
[[ -n "$ARM_CONFIG_JSON" ]] || { echo "ERROR: harness-capture.py pair-config produced no output" >&2; exit 2; }
FIXTURE_DIR="$(jq -r '.fixture // empty' <<<"$ARM_CONFIG_JSON")"
[[ -n "$FIXTURE_DIR" ]] || FIXTURE_DIR="$ARM"
CONTROL_ARM_KIND="$(jq -r '.controlArmKind // empty' <<<"$ARM_CONFIG_JSON")"
PERMISSIVE_EFFECTS="$(jq -r 'if .permissiveEffects == true then "true" else "false" end' <<<"$ARM_CONFIG_JSON")"
TIMEOUT_SECS="$(jq -r '.timeoutSeconds // 600' "$PAIR_DIR/pair.json")"
# Test hook: lets watchdog behavior be exercised without a 10+ minute wait.
[[ -n "${CALOR_P0_TIMEOUT_OVERRIDE:-}" ]] && TIMEOUT_SECS="$CALOR_P0_TIMEOUT_OVERRIDE"

# Escaped-bugs sentinel for non-compiling / never-tested states: the pair's
# actual held-out test count ([Fact] + [InlineData] cases), not a magic 999
# that would distort escaped-bug aggregates across pairs of different sizes.
HELDOUT_TEST_COUNT=$(( $( { grep -ho '\[Fact\]' "$PAIR_DIR"/tests/*.cs 2>/dev/null || true; } | wc -l) \
                     + $( { grep -ho '\[InlineData' "$PAIR_DIR"/tests/*.cs 2>/dev/null || true; } | wc -l) ))
[[ $HELDOUT_TEST_COUNT -gt 0 ]] || { echo "No held-out tests found in $PAIR_DIR/tests" >&2; exit 3; }
mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"

# ---------------------------------------------------------------------------
# Loop-telemetry v2 support (loop plan D4.2). The calor CLI is used by the
# shim for (a) the envelope capture (`--format json` -> diagnostics +
# envelope_valid) and (b) `ids index` for edit_target_ids attribution. The
# arm's MSBuild pipeline drives compilation through Calor.Tasks, so the CLI
# dll is located directly (Release preferred, Debug fallback) — built with
# `dotnet build src/Calor.Compiler -c Release`.
# ---------------------------------------------------------------------------
TELEMETRY_HELPERS="$SCRIPT_DIR/telemetry-helpers.py"
# #881: the ONE derivation of the cost-leg token figure (shared with run-bundle.sh).
# shellcheck source=token-usage.sh
source "$SCRIPT_DIR/token-usage.sh"
CALOR_CLI_DLL=""
if [[ -n "$CALOR_DLL_OVERRIDE" ]]; then
    # Per-arm build-pin (loop plan M5). Pins the calor CLI / MCP-server /
    # envelope build: the dll that drives envelope generation, `ids index`,
    # and (for mcp-file arms) the calor MCP write server. It does NOT repoint
    # the arm's own `dotnet build` — the CalorArm csproj template binds
    # Calor.Tasks / Calor.Sdk / Calor.Runtime and the emitter to __REPO_ROOT__
    # (the current checkout). For the M5 A/B this is exactly correct: WS2/WS3's
    # src/ delta lives entirely in the MCP / session / envelope / parse-tolerant
    # surface this pin governs and touches NONE of Calor.Tasks/Sdk/Runtime or
    # CodeGen (verified empty diff loop-baseline-ws1..main), so both arms
    # compile identically and the whole arm-A-vs-arm-B delta is captured by the
    # pin. A workstream that changed codegen/Calor.Tasks would instead require
    # a per-arm checkout, not just --calor-dll. When unset, auto-detect the
    # current checkout's build (Release preferred).
    [[ -f "$CALOR_DLL_OVERRIDE" ]] || { echo "ERROR: --calor-dll path not found: $CALOR_DLL_OVERRIDE" >&2; exit 2; }
    CALOR_CLI_DLL="$(cd "$(dirname "$CALOR_DLL_OVERRIDE")" && pwd -P)/$(basename "$CALOR_DLL_OVERRIDE")"
    # Reject a path that exists but is not a runnable calor CLI: a live mcp-file
    # arm would otherwise register `dotnet <bogus> mcp` (a server that never
    # starts), strand the agent behind the .calr edit hook, and fabricate
    # guaranteed-failure data — the hazard the mcp-file gate below guards.
    if ! dotnet "$CALOR_CLI_DLL" --help >/dev/null 2>&1; then
        echo "ERROR: --calor-dll is not a runnable calor CLI (dotnet '$CALOR_CLI_DLL' --help failed)" >&2
        exit 2
    fi
else
    for cli_cfg in Release Debug; do
        if [[ -f "$REPO_ROOT/src/Calor.Compiler/bin/$cli_cfg/net10.0/calor.dll" ]]; then
            CALOR_CLI_DLL="$REPO_ROOT/src/Calor.Compiler/bin/$cli_cfg/net10.0/calor.dll"
            break
        fi
    done
fi
if [[ "$ARM" == "calor" && -z "$CALOR_CLI_DLL" ]]; then
    echo "WARNING: calor.dll not found (build src/Calor.Compiler first); calor-arm records will carry envelope_valid=null and empty edit_target_ids" >&2
fi
# A live mcp-file arm cannot run without the MCP server binary: the hook
# blocks raw .calr edits, so a missing server would strand the agent with no
# edit path at all and fabricate guaranteed-failure data.
if [[ "$EDIT_MECHANISM" == "mcp-file" && $NULL_AGENT -eq 0 && -z "$CALOR_CLI_DLL" ]]; then
    echo "ERROR: --edit-mechanism mcp-file needs calor.dll to register the MCP server (build src/Calor.Compiler -c Release first)" >&2
    exit 2
fi

# ---------------------------------------------------------------------------
# Template resolution (A-1.3 per-arm-native, amended by W1). The template comes
# from the arm's own product checkout — EXCEPT for the pre-rows control arm
# (roadmap §4.1), whose registered checkout (the v0.14.3 tag) predates the
# <CalorPermissiveEffects> property: its template is the HARNESS checkout's,
# with __REPO_ROOT__ still bound to the arm's product. result.json records
# templateSource so the provenance is auditable.
# ---------------------------------------------------------------------------
TEMPLATE_SOURCE="arm-repo-root"
TEMPLATE_PATH="$ARM_REPO_ROOT/bench/phase0-agent-native/templates/calor-arm/CalorArm.csproj.template"
if [[ "$CONTROL_ARM_KIND" == "pre-rows" ]]; then
    TEMPLATE_SOURCE="harness"
    TEMPLATE_PATH="$SCRIPT_DIR/templates/calor-arm/CalorArm.csproj.template"
fi
render_template() {  # <dest csproj path>
    sed -e "s|__REPO_ROOT__|$ARM_REPO_ROOT|g" \
        -e "s|__CALOR_PERMISSIVE_EFFECTS__|$PERMISSIVE_EFFECTS|g" \
        "$TEMPLATE_PATH" > "$1"
}

# ---------------------------------------------------------------------------
# Pre-rows canary (roadmap §4.1; the #826 C3 lesson — config proves intent,
# not effect). The arm's own Calor.Tasks build must actually HONOR
# <CalorPermissiveEffects>: compile a canary whose strict compile is an
# `error Calor0410` and whose permissive compile is the same code as a
# WARNING, through the exact template + product root this arm's workspace
# will use. Without this the "pre-rows" arm could silently run STRICT and
# the epoch would measure nothing — the w5-parity-001 shape again.
# Prints the reason and returns 1 when the arm cannot honor the property.
# ---------------------------------------------------------------------------
PERMISSIVE_CANARY="$SCRIPT_DIR/templates/calor-arm/permissive-canary.calr"
permissive_canary() {
    local dir out rc=0
    dir="$(mktemp -d "${TMPDIR:-/tmp}/p0-prerows-canary-XXXXXX")"
    mkdir -p "$dir/src"
    render_template "$dir/src/Src.csproj"
    cp "$PERMISSIVE_CANARY" "$dir/src/canary.calr"
    out="$(cd "$dir/src" && CALOR_P0_SHIM_OFF=1 DOTNET_CLI_UI_LANGUAGE=en dotnet build --nologo -v q 2>&1)" || rc=$?
    rm -rf "$dir"
    if [[ $rc -ne 0 ]] || grep -q 'error Calor0410' <<<"$out" || ! grep -q 'warning Calor0410' <<<"$out"; then
        echo "INVALID: pre-rows control arm — the arm's Calor.Tasks build does not honor <CalorPermissiveEffects> (canary build exit $rc; expected a successful build carrying 'warning Calor0410', got: $(grep -o 'error Calor[0-9]*\|warning Calor[0-9]*' <<<"$out" | sort | uniq -c | tr -s ' \n' ' ' || true)). The arm would run STRICT; refusing before spend (roadmap §4.1)." >&2
        return 1
    fi
    echo "pre-rows canary OK: Calor0410 is a warning under the arm's Calor.Tasks build ($ARM_REPO_ROOT)" >&2
    return 0
}

# ---------------------------------------------------------------------------
# Config pin check (gates doc §0.2): calor arm must run enforced, strict,
# contract-debug, Z3 present — OR the additive pre-rows control arm (roadmap
# §4.1: permissive true ONLY together with controlArmKind "pre-rows"; every
# other config still exits 3). Admission is harness-capture.py pair-config,
# so the rule has one home and the python tests observe it.
# ---------------------------------------------------------------------------
check_pins() {
    if [[ "$ARM" == "calor" ]]; then
        if [[ "$(jq -r '.admitted' <<<"$ARM_CONFIG_JSON")" != "true" ]]; then
            echo "INVALID: pair.json arms.$ARM_CONFIG_KEY config violates gates-doc pin: $(jq -r '.reason' <<<"$ARM_CONFIG_JSON")" >&2
            exit 3
        fi
        if [[ "$CONTROL_ARM_KIND" == "pre-rows" ]]; then
            permissive_canary || exit 3
        fi
    fi
    # Annex A-1.3 instrumentation item 3: when the epoch declares the verify
    # gate for this arm (CALOR_P0_VERIFY_EXPECTED=1), a run without the gate
    # actually armed is INVALID — the M-G3 build-proof channel would be
    # silently absent (§0.2 semantics). Bidirectional (#826 review M1): a
    # gate armed on an arm that declared verify OFF is equally invalid — it
    # silently drifts the control arm's registered configuration.
    if [[ "${CALOR_P0_VERIFY_EXPECTED:-0}" == "1" && -z "${CALOR_P0_VERIFY_GATE:-}" ]]; then
        echo "INVALID: verify gate expected for this arm but CALOR_P0_VERIFY_GATE is unset (A-1.3 item 3)" >&2
        exit 3
    fi
    if [[ "${CALOR_P0_VERIFY_EXPECTED:-0}" != "1" && -n "${CALOR_P0_VERIFY_GATE:-}" ]]; then
        echo "INVALID: verify gate is armed but this arm's config does not declare it (A-1.3 item 3, control-arm drift)" >&2
        exit 3
    fi
    # #826 review C3: env checks prove intent, not effect. When the gate is
    # expected, require the arm's compiler to actually produce a Calor0712
    # refutation on a known-refuted canary — an armed gate whose solver is
    # silently unavailable (missing libz3 in the Tasks/CLI context) must be
    # caught here, before any spend.
    if [[ "${CALOR_P0_VERIFY_EXPECTED:-0}" == "1" ]]; then
        local canary="$REPO_ROOT/tests/TestData/Verification/Outcomes/refuted-with-binding.calr"
        local canary_out
        # `calor verify` exits NON-ZERO on a refutation, so the exit code is
        # ignored and the verdict is read from the JSON. The match must be the
        # refutation-specific status token — a bare "refuted" matches JSON KEY
        # NAMES ("refuted": 0) even in solver-unavailable output, which would
        # pass a dead solver: the exact inversion this canary exists to catch
        # (#826 re-verification C-new-1).
        canary_out="$(dotnet "$CALOR_CLI_DLL" verify "$canary" --no-cache --format json 2>&1)" || true
        if ! grep -q '"status": "refuted"' <<< "$canary_out"; then
            echo "INVALID: verify-gate canary did not refute — solver ineffective in this arm's compiler context (A-1.3 item 3 / #826 C3)" >&2
            exit 3
        fi
        # The CLI canary proves the CLI/journal channel; the workspace Tasks
        # build binds its own copy of the compiler, so also require the native
        # solver next to the Calor.Tasks.dll the template references (#826
        # re-verification recommendation — closes the Tasks-context gap the
        # Calor0710 warning surfaces but does not invalidate).
        local tasks_dir="$ARM_REPO_ROOT/src/Calor.Tasks/bin/Release/net10.0"
        if [[ -d "$tasks_dir" ]] \
           && ! compgen -G "$tasks_dir/libz3.*" > /dev/null; then
            echo "INVALID: no libz3 native library beside Calor.Tasks.dll — the workspace verify gate would be silently dead (A-1.3 item 3 / #826 C3)" >&2
            exit 3
        fi
    fi
}

# ---------------------------------------------------------------------------
# Workspace materialization. Layout:
#   $ws/src/       agent-visible: fixture + spec.md + arm project file
#   $ws_out/heldout/  harness-only: tests + shim + csproj referencing src
# ---------------------------------------------------------------------------
materialize() {
    local ws="$1" ws_out="$2"
    mkdir -p "$ws/src" "$ws_out/heldout"

    # The starter comes from the arm entry's fixture directory (W1 / §4.1:
    # PP-W-rows' two calor arms start from different programs).
    [[ -d "$PAIR_DIR/$FIXTURE_DIR" ]] || { echo "ERROR: fixture directory missing: $PAIR_DIR/$FIXTURE_DIR (arms.$ARM_CONFIG_KEY.fixture)" >&2; exit 2; }
    cp -R "$PAIR_DIR/$FIXTURE_DIR/." "$ws/src/"
    cp "$PAIR_DIR/spec.md" "$ws/spec.md"

    if [[ "$ARM" == "calor" ]]; then
        # Per-arm-native (A-1.3): the TEMPLATE comes from the arm's own product
        # checkout — the control arm's registered configuration includes its
        # template exactly as archived (pre-verify-gate), not main's. The
        # pre-rows exception is documented at render_template.
        render_template "$ws/src/Src.csproj"
    else
        cat > "$ws/src/Src.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
EOF
    fi

    # Held-out project: tests + the arm's shim, outside the agent's workspace
    cp "$PAIR_DIR"/tests/*.cs "$ws_out/heldout/" 2>/dev/null || true
    cp "$PAIR_DIR/tests/shims/TestShim.$ARM.cs" "$ws_out/heldout/TestShim.cs"
    cat > "$ws_out/heldout/HeldOut.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <!-- Reference the built assembly, not the project: keeps the held-out
         build fully decoupled from the agent's workspace builds -->
    <Reference Include="Src">
      <HintPath>$ws/src/bin/Debug/net10.0/Src.dll</HintPath>
    </Reference>
    <Reference Include="Calor.Runtime" Condition="Exists('$ws/src/bin/Debug/net10.0/Calor.Runtime.dll')">
      <HintPath>$ws/src/bin/Debug/net10.0/Calor.Runtime.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
EOF

    # WS5 probe pairs (loop plan D5.1): arm-shared, agent-visible smoke
    # suite, materialized next to src with its own starting-surface shim so
    # it compiles against the starter fixture from iteration zero. Same
    # suite bytes in both arms — the probe's fairness requirement.
    if [[ -d "$PAIR_DIR/smoke" ]]; then
        mkdir -p "$ws/smoke"
        cp "$PAIR_DIR"/smoke/*.cs "$ws/smoke/" 2>/dev/null || true
        cp "$PAIR_DIR/smoke/shims/SmokeShim.$ARM.cs" "$ws/smoke/SmokeShim.cs"
        cat > "$ws/smoke/Smoke.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Src">
      <HintPath>$ws/src/bin/Debug/net10.0/Src.dll</HintPath>
    </Reference>
    <Reference Include="Calor.Runtime" Condition="Exists('$ws/src/bin/Debug/net10.0/Calor.Runtime.dll')">
      <HintPath>$ws/src/bin/Debug/net10.0/Calor.Runtime.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
EOF
        # Integrity baseline: "arm-shared, not agent-editable" is enforced by
        # (a) the PreToolUse hook below and (b) this hash, re-checked at
        # extract time and surfaced as smokeTampered (review of #810
        # finding 4) — the hook covers the primary agent tools, the hash
        # catches everything else (shell edits, deletion). obj/ and bin/ are
        # excluded: the hash covers smoke SOURCE only — building Smoke.csproj
        # emits obj/**/*.g.cs (GlobalUsings/AssemblyInfo), which would land in
        # the re-check but not this pre-build baseline and spuriously flag
        # smokeTampered on every arm that compiles smoke (ws5-probe-001).
        # Excluding obj/bin does not open a tamper hole: the .NET SDK's
        # DefaultItemExcludes drops obj/ and bin/ from the Compile glob, so a
        # .cs planted there never enters the smoke assembly; the real smoke
        # source lives outside obj/bin and is still hashed.
        find "$ws/smoke" -type f -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -exec shasum {} + | shasum | cut -d' ' -f1 \
            > "$ws_out/.smokehash"
    fi

    # WS5 probe project (review of #810 findings 1+8): the defect probe test
    # compiles against the STARTING public surface (it exercises the
    # defective function, which exists from iteration zero) with its own
    # csproj — decoupled from the held-out project, whose full-surface shim
    # cannot compile until the task's new functions exist. This makes
    # "caught" measure the defect, not task completion.
    if [[ -d "$PAIR_DIR/tests/probe" ]]; then
        mkdir -p "$ws_out/probe"
        cp "$PAIR_DIR"/tests/probe/*.cs "$ws_out/probe/"
        cp "$PAIR_DIR/smoke/shims/SmokeShim.$ARM.cs" "$ws_out/probe/SmokeShim.cs"
        sed "s|__WS__|$ws|g" > "$ws_out/probe/Probe.csproj" <<'PROBEPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Src"><HintPath>__WS__/src/bin/Debug/net10.0/Src.dll</HintPath></Reference>
    <Reference Include="Calor.Runtime" Condition="Exists('__WS__/src/bin/Debug/net10.0/Calor.Runtime.dll')">
      <HintPath>__WS__/src/bin/Debug/net10.0/Calor.Runtime.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
PROBEPROJ
    fi

    # .g.cs write-block for the calor arm (gates doc §1) via Claude hook config.
    # With --edit-mechanism mcp-file the same PreToolUse hook additionally
    # blocks direct Edit/Write on .calr files, steering the agent to the calor
    # MCP file tools (arm-constraint enforcement, loop plan D4.2). mcp-node is
    # descoped (Call 1/E1) and gets no enforcement; raw is the no-op default.
    # Probe pairs additionally block edits under the arm-shared smoke suite
    # in BOTH arms — "fixed, not agent-editable" must be enforced, not
    # asserted (review of #810 finding 4); the .smokehash re-check at
    # extract time backstops non-hook edit paths.
    if [[ ($NULL_AGENT -eq 0) && ( "$ARM" == "calor" || -d "$PAIR_DIR/smoke" ) ]]; then
        mkdir -p "$ws/.claude"
        local calr_block=""
        if [[ "$EDIT_MECHANISM" == "mcp-file" ]]; then
            # NB: this fragment is spliced into a JSON string value below, so
            # the shell double-quotes around \$f must land JSON-escaped (\")
            calr_block="grep -q '\\\\.calr$' <<<\\\"\$f\\\" && { echo 'BLOCKED: this arm requires the calor MCP file tools for .calr edits (edit-mechanism: mcp-file); direct Edit/Write on .calr files is disabled' >&2; exit 2; }; "
        fi
        local smoke_block=""
        if [[ -d "$PAIR_DIR/smoke" ]]; then
            smoke_block="grep -q '/smoke/' <<<\\\"\$f\\\" && { echo 'BLOCKED: the smoke suite is harness-provided and arm-shared; it is not agent-editable' >&2; exit 2; }; "
        fi
        cat > "$ws/.claude/settings.json" <<EOF
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Write|Edit",
        "hooks": [
          { "type": "command",
            "command": "f=\$(jq -r '.tool_input.file_path // empty'); grep -q '\\\\.g\\\\.cs$' <<<\"\$f\" && { echo 'BLOCKED: .g.cs files are generated; edit the .calr source' >&2; exit 2; }; ${calr_block}${smoke_block}exit 0" }
        ]
      }
    ]
  }
}
EOF
    fi

    # MCP server registration for the live mcp-file arm (loop plan M3 PR 4):
    # exactly one server, passed via --mcp-config + --strict-mcp-config so
    # user-level MCP config cannot bleed into the arm. claude runs from
    # $ws/src, so the server's working directory — calor_file_write's
    # no-session write-confinement root — is the task tree itself. Write
    # telemetry (M-L2) and reject payloads (M-L4/D4.6) land in the run's out
    # dir via the env the server is started with.
    # Built with jq (not a heredoc) so paths are JSON-escaped, and the write
    # root is pinned via --root rather than trusting the client to spawn the
    # server with a particular CWD (review of #799 item 1): a wrong implicit
    # root would silently reject every task-tree write and fabricate the
    # guaranteed-failure data the old gate existed to prevent.
    # NB: --root also puts the workspace path in the server's ARGV, which is
    # what lets kill_agent_tree's pattern escalation reach an orphaned server
    # (pkill -f matches command lines, not env) — do not move it to env-only.
    if [[ "$ARM" == "calor" && "$EDIT_MECHANISM" == "mcp-file" && $NULL_AGENT -eq 0 ]]; then
        jq -n --arg dll "$CALOR_CLI_DLL" --arg root "$ws/src" \
              --arg log "$ws_out/mcp-writes.jsonl" --arg rej "$ws_out/rejects" \
            '{mcpServers: {calor: {
                command: "dotnet",
                args: [$dll, "mcp", "--stdio", "--root", $root],
                env: {CALOR_MCP_WRITE_LOG: $log, CALOR_MCP_REJECT_DIR: $rej}}}}' \
            > "$ws_out/mcp-config.json"
    fi

    # Baseline src-tree hash (mirrors the shim's computation): without it the
    # first observed build always compares against "none" and journals a
    # phantom edited:true iteration even when the agent has changed nothing.
    local base_hash
    base_hash=$(find "$ws/src" -type f \( -name '*.cs' -o -name '*.calr' \) -not -path '*/obj/*' -not -path '*/bin/*' -exec shasum {} + 2>/dev/null | shasum | cut -d' ' -f1)
    echo "$base_hash" > "$ws_out/.lasthash"
    # Iteration counter (v2 telemetry): 1-based ordinal among edited
    # invocations, persisted next to .lasthash so the shim can resume it.
    echo 0 > "$ws_out/.itercount"
    # Previous-iteration .calr snapshot for edit_target_ids attribution:
    # seeded from the starter fixture so the first edited build diffs against
    # the state the agent actually started from.
    rm -rf "$ws_out/.prev-src"
    mkdir -p "$ws_out/.prev-src"
    # (portable relative-path copy: BSD cp has no --parents)
    (cd "$ws/src" && find . -name '*.calr' -not -path '*/obj/*' -not -path '*/bin/*' | while IFS= read -r f; do
        mkdir -p "$ws_out/.prev-src/$(dirname "$f")"; cp "$f" "$ws_out/.prev-src/$f"; done)
}

# ---------------------------------------------------------------------------
# dotnet shim: journals build/test invocations as loop-telemetry/2 records
# (loop plan D4.2, schema: loop-telemetry.schema.json), detects edits via
# src-tree hash, and runs the held-out suite silently after each build/test.
# ---------------------------------------------------------------------------
write_shim() {
    local ws="$1" ws_out="$2" shim_dir="$3" run_idx="$4"
    local real_dotnet
    real_dotnet="$(command -v dotnet)"
    mkdir -p "$shim_dir"
    cat > "$shim_dir/dotnet" <<EOF
#!/usr/bin/env bash
set -uo pipefail
if [[ "\${CALOR_P0_SHIM_OFF:-0}" == "1" ]]; then exec "$real_dotnet" "\$@"; fi

# Portable millisecond clock: BSD date has no %N (the harness runs on macOS
# AND Linux), so prefer perl/python3 and degrade to whole seconds last.
now_ms() {
  if command -v perl >/dev/null 2>&1; then
    perl -MTime::HiRes=time -e 'printf("%d", time()*1000)'
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import time,sys;sys.stdout.write(str(int(time.time()*1000)))'
  else
    echo \$(( \$(date +%s) * 1000 ))
  fi
}

arm="$ARM"
ts_iso="\$(date -u +%Y-%m-%dT%H:%M:%SZ)"
t0=\$(now_ms)
"$real_dotnet" "\$@"; rc=\$?
# feedback_latency_ms is stamped HERE — the moment the agent-visible dotnet
# returns. Everything below (silent src rebuild, held-out suite, envelope
# capture, id attribution) is harness observation the agent never sees in this
# invocation's output, and the held-out cost is arm-asymmetric (the calor arm
# build drives the full Calor.Tasks pipeline) — including it would bias the
# latency comparison (review of #758 item 2; CLAUDE.md benchmark-fairness).
lat=\$(( \$(now_ms) - t0 )); [[ \$lat -lt 0 ]] && lat=0
case "\${1:-}" in
  build|test|run)
    # Serialize the telemetry section: .lasthash/.itercount are read-modify-
    # write and journal.jsonl is appended — concurrent dotnet invocations
    # would corrupt iteration ordinals (review of #758 minor 2). mkdir is the
    # portable atomic lock; on timeout we proceed unlocked (degrades to the
    # pre-lock behavior rather than deadlocking the agent).
    lock_tries=0
    while ! mkdir "$ws_out/.telemetry.lock" 2>/dev/null; do
      lock_tries=\$((lock_tries+1))
      [[ \$lock_tries -gt 1200 ]] && break
      sleep 0.05
    done
    trap 'rmdir "$ws_out/.telemetry.lock" 2>/dev/null || true' EXIT

    # bin/obj are excluded: generated outputs (e.g. the calor arm's obj/calor/
    # *.g.cs) would otherwise flip the hash on the first build and journal a
    # phantom edited:true iteration with zero agent edits
    hash=\$(find "$ws/src" -type f \\( -name '*.cs' -o -name '*.calr' \\) -not -path '*/obj/*' -not -path '*/bin/*' -exec shasum {} + 2>/dev/null | shasum | cut -d' ' -f1)
    prev=\$(cat "$ws_out/.lasthash" 2>/dev/null || echo none)
    edited=\$([[ "\$hash" != "\$prev" ]] && echo true || echo false)
    echo "\$hash" > "$ws_out/.lasthash"

    # iteration: 1-based ordinal among edited invocations (counter persisted
    # next to .lasthash); JSON null for unedited observations (gates doc §2)
    iteration=null
    if [[ "\$edited" == "true" ]]; then
      iteration=\$(( \$(cat "$ws_out/.itercount" 2>/dev/null || echo 0) + 1 ))
      echo "\$iteration" > "$ws_out/.itercount"
    fi

    # Optional src-tree snapshot keyed by src_tree_hash (reject-replay, D4.6).
    # Off by default; enabled with CALOR_LOOP_SNAPSHOTS=1.
    if [[ "\${CALOR_LOOP_SNAPSHOTS:-0}" == "1" && ! -d "$ws_out/snapshots/\$hash" ]]; then
      mkdir -p "$ws_out/snapshots"
      cp -R "$ws/src" "$ws_out/snapshots/.tmp-\$hash" 2>/dev/null || true
      find "$ws_out/snapshots/.tmp-\$hash" -type d \\( -name bin -o -name obj \\) -prune -exec rm -rf {} + 2>/dev/null || true
      mv "$ws_out/snapshots/.tmp-\$hash" "$ws_out/snapshots/\$hash" 2>/dev/null || true
    fi

    ho_pass=0; ho_fail=$HELDOUT_TEST_COUNT
    # Fresh, decoupled src build; only if it succeeds is the dll current and
    # the held-out result meaningful (non-compiling state = all failing)
    if CALOR_P0_SHIM_OFF=1 "$real_dotnet" build "$ws/src/Src.csproj" --nologo -v q > "$ws_out/.src_build.txt" 2>&1; then
      if CALOR_P0_SHIM_OFF=1 "$real_dotnet" test "$ws_out/heldout/HeldOut.csproj" --nologo -v q > "$ws_out/.ho_last.txt" 2>&1; then
        ho_fail=0
        ho_pass=\$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$ws_out/.ho_last.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
      else
        ho_fail=\$(grep -oE 'Failed:[[:space:]]+[0-9]+' "$ws_out/.ho_last.txt" | grep -oE '[0-9]+' | head -1 || echo $HELDOUT_TEST_COUNT)
        ho_pass=\$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$ws_out/.ho_last.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
      fi
    fi

    edit_ids="[]"; diags="[]"; dtrunc=false; env_valid=null

    # edit_target_ids (calor arm, edited only): per changed .calr file, run
    # \`calor ids index\` on the CURRENT file and take the IDs whose
    # approximate line span intersects the changed-line ranges vs the
    # previous-iteration copy under .prev-src/ (line-range attribution;
    # precision limits documented in telemetry-helpers.py). Files deleted
    # since the previous iteration have no current-file IDs to attribute and
    # are skipped. C# arm: no .calr files change, so this stays [].
    if [[ "\$arm" == "calor" && "\$edited" == "true" && -n "$CALOR_CLI_DLL" ]] && command -v python3 >/dev/null 2>&1; then
      while IFS= read -r rel; do
        cur="$ws/src/\$rel"; prevf="$ws_out/.prev-src/\$rel"
        if [[ ! -f "\$prevf" ]] || ! cmp -s "\$prevf" "\$cur"; then
          if CALOR_P0_SHIM_OFF=1 "$real_dotnet" "$CALOR_CLI_DLL" ids index "\$cur" -o "$ws_out/.ids-index.json" >/dev/null 2>&1; then
            prev_arg="-"; [[ -f "\$prevf" ]] && prev_arg="\$prevf"
            file_ids=\$(python3 "$TELEMETRY_HELPERS" edit-targets "\$prev_arg" "\$cur" "$ws_out/.ids-index.json" 2>/dev/null || echo "[]")
            edit_ids=\$(jq -cn --argjson a "\$edit_ids" --argjson b "\$file_ids" '\$a + \$b | unique')
          fi
          mkdir -p "\$(dirname "\$prevf")"; cp "\$cur" "\$prevf"
        fi
      done < <(cd "$ws/src" && find . -name '*.calr' -not -path '*/obj/*' -not -path '*/bin/*' | sed 's|^\\./||')
    fi

    # diagnostics + envelope_valid (calor arm only): compile the .calr set
    # directly with the calor CLI under the pair's pinned flags
    # (enforce-effects on, permissive off, contract-mode debug) with
    # --format json, and extract up to 50 {code, declarationId} entries.
    # Inputs are COPIES under .envelope-src/ — compiling in place would drop
    # .g.cs next to the agent's sources and flip the next src-tree hash.
    # Cached per src state: re-run only when the tree changed.
    if [[ "\$arm" == "calor" && -n "$CALOR_CLI_DLL" ]] && command -v python3 >/dev/null 2>&1; then
      if [[ "\$edited" == "true" || ! -s "$ws_out/.envelope-meta.json" ]]; then
        rm -rf "$ws_out/.envelope-src"; mkdir -p "$ws_out/.envelope-src"
        inputs=()
        while IFS= read -r rel; do
          mkdir -p "$ws_out/.envelope-src/\$(dirname "\$rel")"
          cp "$ws/src/\$rel" "$ws_out/.envelope-src/\$rel"
          inputs+=(--input "$ws_out/.envelope-src/\$rel")
        done < <(cd "$ws/src" && find . -name '*.calr' -not -path '*/obj/*' -not -path '*/bin/*' | sed 's|^\\./||')
        if [[ \${#inputs[@]} -gt 0 ]]; then
          # Annex A-1.3 instrumentation item 2: the Guarantees-probe v0.10 arm
          # adds --verify so journal diagnostics carry Calor0711/0712 events
          # (M-G3 build-proof channel). Off unless the arm config sets it.
          CALOR_P0_SHIM_OFF=1 "$real_dotnet" "$CALOR_CLI_DLL" "\${inputs[@]}" \\
            --enforce-effects --contract-mode debug \${CALOR_P0_VERIFY_GATE:+--verify} --no-telemetry --format json \\
            > "$ws_out/.envelope.json" 2> "$ws_out/.envelope.err" || true
          python3 "$TELEMETRY_HELPERS" envelope "$ws_out/.envelope.json" > "$ws_out/.envelope-meta.json" 2>/dev/null \\
            || echo '{"diagnostics":[],"diagnostics_truncated":false,"envelope_valid":false}' > "$ws_out/.envelope-meta.json"
        fi
      fi
      if [[ -s "$ws_out/.envelope-meta.json" ]]; then
        diags=\$(jq -c .diagnostics "$ws_out/.envelope-meta.json")
        dtrunc=\$(jq -c .diagnostics_truncated "$ws_out/.envelope-meta.json")
        env_valid=\$(jq -c .envelope_valid "$ws_out/.envelope-meta.json")
      fi
    fi

    # apply_verdict / rejected_edit: reserved for the WS2 transactional apply
    # path — always null in the baseline harness (schema doc).
    jq -cn \\
      --arg ts "\$ts_iso" --arg pair "$PAIR_ID" --arg armlabel "$ARM_LABEL" \\
      --argjson run $run_idx --arg cmd "\${1}" --argjson exit "\$rc" \\
      --argjson edited "\$edited" --argjson iteration "\$iteration" \\
      --argjson lat "\$lat" --argjson hp "\$ho_pass" --argjson hf "\$ho_fail" \\
      --arg hash "\$hash" --arg mech "$EDIT_MECHANISM" \\
      --argjson ids "\$edit_ids" --argjson diags "\$diags" \\
      --argjson dtrunc "\$dtrunc" --argjson envv "\$env_valid" \\
      '{schema:"loop-telemetry/2", ts:\$ts, pair:\$pair, arm:\$armlabel,
        run:\$run, iteration:\$iteration, cmd:\$cmd, exit:\$exit,
        edited:\$edited, feedback_latency_ms:\$lat,
        heldout_pass:\$hp, heldout_fail:\$hf, src_tree_hash:\$hash,
        edit_mechanism:\$mech, edit_target_ids:\$ids,
        diagnostics:\$diags, diagnostics_truncated:\$dtrunc,
        envelope_valid:\$envv, apply_verdict:null, rejected_edit:null}' \\
      >> "$ws_out/journal.jsonl"
    ;;
esac
exit \$rc
EOF
    chmod +x "$shim_dir/dotnet"
}

# ---------------------------------------------------------------------------
# Agent invocation (or null-agent reference-solution application)
# ---------------------------------------------------------------------------
# Kill an agent process and everything under it, with verification.
# $1 = root pid (the agent subshell, a process-group leader under set -m);
# $2 = the run's workspace dir — a mktemp path unique to this run;
# $3 (optional) = the run's out dir, equally unique.
# The pattern escalation matches ARGV, not env (pkill/pgrep -f semantics —
# review of #800 item 1): it reaches the MCP server only because the server
# registration passes the workspace root on the command line
# (`mcp --stdio --root $ws/src` — see the jq config block), and reaches the
# shim wrapper via its $ws_out/.shim path. If either stops being
# argv-visible, this fallback silently loses that process — keep them
# coupled.
kill_agent_tree() {
    local root="$1" ws_path="$2" out_path="${3:-}"
    # Collect descendants BEFORE killing: killing the root first would
    # reparent children to init and lose them.
    local all="" frontier="$root" next p depth
    for depth in 1 2 3 4 5 6; do
        next=""
        for p in $frontier; do
            next+=" $(pgrep -P "$p" 2>/dev/null || true)"
        done
        next="$(echo "$next" | tr -s ' ' | sed 's/^ //')"
        [[ -z "$next" ]] && break
        all+=" $next"
        frontier="$next"
    done

    kill -9 -- "-$root" 2>/dev/null || echo "watchdog: pgid kill for -$root failed" >&2
    # shellcheck disable=SC2086 — pid lists are intentionally word-split
    kill -9 $root $all 2>/dev/null || true

    sleep 1
    if kill -0 "$root" 2>/dev/null || pgrep -f "$ws_path" >/dev/null 2>&1 \
        || { [[ -n "$out_path" ]] && pgrep -f "$out_path" >/dev/null 2>&1; }; then
        echo "watchdog: survivors detected after tree kill; pattern-killing processes matching $ws_path" >&2
        pkill -9 -f "$ws_path" 2>/dev/null || true
        [[ -n "$out_path" ]] && pkill -9 -f "$out_path" 2>/dev/null || true
    fi
}

run_agent() {
    local ws="$1" ws_out="$2" shim_dir="$3"
    AGENT_RC=0
    local prompt
    local test_rule="Do not create test files; do not modify the project file."
    if [[ -f "$PAIR_DIR/defect.json" ]]; then
        # WS5 probe pairs (loop plan D5.1): the C# arm's registered toolkit
        # includes agent-generated tests (strategy §0), and the probe's
        # fairness story depends on not foreclosing them (review of #810
        # finding 3). Scratch tests are permitted in both arms and are not
        # graded; only the project sources under src/ are.
        test_rule="You may write your own scratch tests anywhere under the workspace (they are not graded; only the sources in src/ are). Do not modify the project file or the provided smoke tests under the smoke directory."
    fi
    prompt="You are working in $ws/src. Read $ws/spec.md and complete the task it describes — implementing missing operations and/or modifying existing behavior as specified — in the existing source files, following the conventions already present. The iteration budget is $ITERATION_BUDGET build/test cycles. Build with 'dotnet build' from $ws/src to check your work. $test_rule Stop when the spec is fully satisfied and the project builds cleanly (the starter already builds, so a clean build alone does not mean you are done)."
    if [[ -n "$EXEMPLAR_FILE" ]]; then
        prompt+=$'\n\n'"$(cat "$EXEMPLAR_FILE")"
    fi
    # Arm-constraint prompt injection (loop plan D4.2). mcp-node is descoped
    # (Call 1/E1): no constraint text is fabricated for tools that don't exist.
    if [[ "$EDIT_MECHANISM" == "mcp-file" ]]; then
        prompt+=$'\n\n'"Edit-mechanism constraint (mcp-file): direct Edit/Write of .calr files is blocked in this workspace by policy. Modify .calr sources exclusively through the calor MCP file tools; other files and commands are unaffected."
    fi

    if [[ $NULL_AGENT -eq 1 ]]; then
        # First build the starter as shipped (observed, through the shim) so
        # every null-agent run also proves the starting fixture compiles.
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" dotnet build --nologo -v q >/dev/null 2>&1 ) || {
            echo "null-agent: starter fixture failed to build (pair=$PAIR_ID arm=$ARM)" >&2
        }
        # Then apply the reference solution and do one observed build (validates
        # shim + held-out wiring end to end with zero API spend)
        [[ -d "$PAIR_DIR/reference/$FIXTURE_DIR" ]] || { echo "null-agent: reference solution missing: $PAIR_DIR/reference/$FIXTURE_DIR" >&2; }
        cp -R "$PAIR_DIR/reference/$FIXTURE_DIR/." "$ws/src/"
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" dotnet build --nologo -v q >/dev/null 2>&1 ) || true
        echo '{"null_agent":true}' > "$ws_out/agent.json"
        # W1: the transcript pin holds for every run. A null-agent run has no
        # agent, so its transcript is the one synthetic result event; turns
        # count 0 assistant messages.
        echo '{"type":"result","subtype":"null_agent","null_agent":true}' > "$ws_out/transcript.jsonl"
        return 0
    fi

    # Portable timeout with a spend guarantee: prefer coreutils timeout/gtimeout
    # (kills the claude process itself, -k grace for cleanup). The bash-watchdog
    # fallback must kill the PROCESS GROUP: agent_pid is the subshell, and
    # SIGKILL on it alone leaves the claude child reparented to init and still
    # consuming API budget past TIMEOUT_SECS (review of #758 item 3). Job
    # control (set -m) makes the backgrounded subshell a group leader so
    # kill -- -$agent_pid reaches every descendant.
    local timeout_bin=""
    if command -v timeout >/dev/null 2>&1; then timeout_bin="timeout"
    elif command -v gtimeout >/dev/null 2>&1; then timeout_bin="gtimeout"; fi

    # Model pin: CLAUDE_MODEL was previously recorded in pins.json but never
    # passed to the agent — the pin was documentation, not enforcement. Pass it
    # explicitly so epoch pins are true.
    local model_args=()
    [[ -n "${CLAUDE_MODEL:-}" && "${CLAUDE_MODEL}" != "default" ]] && model_args=(--model "$CLAUDE_MODEL")

    # MCP registration args (written by prep for the live mcp-file arm only):
    # --strict-mcp-config keeps the arm hermetic — the agent sees exactly the
    # calor server, never user-level MCP config.
    local mcp_args=()
    [[ -f "$ws_out/mcp-config.json" ]] && mcp_args=(--mcp-config "$ws_out/mcp-config.json" --strict-mcp-config)

    # bash 3.2 (macOS's /bin/bash) treats "${arr[@]}" on an EMPTY array as an unbound
    # variable under `set -u`, so the two invocations below must expand through the
    # ${arr[@]+...} guard. Without it the raw arm — which registers no MCP server, so
    # mcp_args is empty — dies BEFORE claude is invoked: no agent.json, exit 1, every
    # run counted invalid, zero API spend. Found by the PP-W5 parity epoch, which is
    # `raw` on both arms and so hit it 40/40; M5 never did because its arm B uses
    # mcp-file (populating the array) and its arm A ran where `timeout` existed,
    # taking the other branch. Both branches are guarded here — the timeout branch has
    # the identical expansion and fails the same way when it is the live one.

    # W1 (roadmap §2.1 S1 mechanics): stream-json (Claude Code 2.1.243+
    # requires --verbose with it) tee'd to transcript.jsonl; the result event
    # alone is filtered into agent.json so its content is exactly what
    # `--output-format json` produced — detect_invalid_run and token-usage.py
    # keep their input. --forward-subagent-text makes subagent turns visible
    # in the transcript (parent_tool_use_id set); harness-capture.py counts
    # them separately and in the total, matching the corrected-token rule
    # (A-1.9.1) which already includes subagent tokens. pipefail (set at the
    # top) makes rc claude's exit when it fails, not tee's or jq's.
    local claude_args=(--print --verbose --output-format stream-json --forward-subagent-text --dangerously-skip-permissions)
    local rc=0
    if [[ -n "$timeout_bin" ]]; then
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" \
            "$timeout_bin" -k 10 "$TIMEOUT_SECS" \
            claude "${claude_args[@]}" ${model_args[@]+"${model_args[@]}"} ${mcp_args[@]+"${mcp_args[@]}"} \
            "$prompt" 2> "$ws_out/agent.err" \
            | tee "$ws_out/transcript.jsonl" | jq -c 'select(.type? == "result")' > "$ws_out/agent.json" ) || rc=$?
    else
        # Bash-watchdog fallback, hardened after ws2-exit-e2e-001 run 1: the
        # previous one-shot `sleep && kill -9 -- -pgid 2>/dev/null` provably
        # failed to land there (the agent ran 105 minutes against a 900 s
        # budget during an API incident) and its silenced stderr left no
        # evidence why. This version polls a deadline in the parent (no
        # separate watchdog process to lose), collects the agent's descendant
        # tree BEFORE killing (a dead root reparents children), kills group +
        # tree, then VERIFIES death and escalates to a workspace-scoped
        # pattern kill — logging every step to stderr.
        set -m
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" \
            claude "${claude_args[@]}" ${model_args[@]+"${model_args[@]}"} ${mcp_args[@]+"${mcp_args[@]}"} \
            "$prompt" 2> "$ws_out/agent.err" \
            | tee "$ws_out/transcript.jsonl" | jq -c 'select(.type? == "result")' > "$ws_out/agent.json" ) &
        local agent_pid=$!
        local deadline=$(( SECONDS + TIMEOUT_SECS ))
        while kill -0 "$agent_pid" 2>/dev/null; do
            if (( SECONDS >= deadline )); then
                echo "watchdog: agent exceeded ${TIMEOUT_SECS}s; killing pid $agent_pid and descendants" >&2
                kill_agent_tree "$agent_pid" "$ws" "$ws_out"
                break
            fi
            sleep 5
        done
        wait "$agent_pid" 2>/dev/null || rc=$?
        set +m
    fi
    AGENT_RC=$rc
    if [[ $rc -ne 0 ]]; then echo "agent exit: $rc" >> "$ws_out/agent.err"; fi
}

# ---------------------------------------------------------------------------
# Metrics extraction (gates doc §2) -> result.json
# ---------------------------------------------------------------------------
extract_metrics() {
    local ws="$1" ws_out="$2" run_idx="$3"
    local journal="$ws_out/journal.jsonl"
    touch "$journal"

    # Final silent held-out run = declared-done state (non-compiling = all fail)
    local final_pass=0 final_fail=$HELDOUT_TEST_COUNT final_build_ok=0
    if CALOR_P0_SHIM_OFF=1 dotnet build "$ws/src/Src.csproj" --nologo -v q > "$ws_out/.src_final.txt" 2>&1; then
        final_build_ok=1
        if CALOR_P0_SHIM_OFF=1 dotnet test "$ws_out/heldout/HeldOut.csproj" --nologo -v q > "$ws_out/.ho_final.txt" 2>&1; then
            final_fail=0
            final_pass=$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$ws_out/.ho_final.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
        else
            final_fail=$(grep -oE 'Failed:[[:space:]]+[0-9]+' "$ws_out/.ho_final.txt" | grep -oE '[0-9]+' | head -1 || echo "$HELDOUT_TEST_COUNT")
            final_pass=$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$ws_out/.ho_final.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
        fi
    fi

    # Iterations = journaled build/test invocations with edited=true
    local iterations iters_to_green censored
    iterations=$(jq -s '[.[] | select(.edited==true)] | length' "$journal")
    # Ordinal among edited iterations (journal entries with edited=false —
    # e.g. the observed null-agent starter build — must not inflate this)
    iters_to_green=$(jq -s '[.[] | select(.edited==true)] | to_entries
        | ([.[] | select(.value.heldout_fail==0)] | first // null)
        | if . == null then -1 else (.key + 1) end' "$journal" 2>/dev/null || echo -1)
    censored=false
    if [[ "$iters_to_green" == "-1" ]]; then
        iters_to_green=$((ITERATION_BUDGET + 1)); censored=true
    fi

    # Tokens via token-usage.py (#881): usage.output_tokens covers only the
    # final top-level turn and under-counted 55x on a subagent-delegating run
    # (w5-parity-002 N1-001 treatment run-4); the helper sums modelUsage[*]
    # and reports both figures so the correction is auditable in result.json.
    # token-usage.sh gates on file presence only and lets the helper decide;
    # a helper failure falls back to the naive read, never a silent 0.
    local tokens_in tokens_out token_usage
    token_usage_collect "$ws_out/agent.json" "$PAIR_ID/$ARM_LABEL/run-$run_idx"
    tokens_in=$TOKENS_IN; tokens_out=$TOKENS_OUT; token_usage=$TOKEN_USAGE_JSON

    # W1: per-turn fields from transcript.jsonl. turns.assistantMessages =
    # distinct assistant message.id (the field A-1.12 registers — stream-json
    # emits one event per content block, so events are not turns); num_turns
    # from the result envelope stays archived beside it as turns.numTurns.
    local turns num_turns agent_builds
    turns="$(python3 "$HARNESS_CAPTURE" turns "$ws_out/transcript.jsonl" 2>/dev/null)" \
        || turns='{"assistantMessages":null,"assistantMessagesTopLevel":null,"assistantMessagesSubagent":null,"source":"helper-error"}'
    num_turns="$(jq -c '.num_turns // null' "$ws_out/agent.json" 2>/dev/null || echo null)"
    [[ -n "$num_turns" ]] || num_turns=null
    turns="$(jq -c --argjson nt "$num_turns" '. + {numTurns: $nt, transcript: "transcript.jsonl"}' <<<"$turns")"
    # The agent's own `dotnet build` / `dotnet test` / `calor` calls with
    # their tool_result output (§0.2: never archived before) -> agent-builds.jsonl
    python3 "$HARNESS_CAPTURE" builds "$ws_out/transcript.jsonl" > "$ws_out/agent-builds.jsonl" 2>/dev/null \
        || : > "$ws_out/agent-builds.jsonl"
    agent_builds="$(jq -n --argjson n "$(grep -c . "$ws_out/agent-builds.jsonl" || true)" \
        '{count: $n, file: "agent-builds.jsonl"}')"

    # #1094: compilerHash. The agent-workspace copy was archived by the main
    # loop right after the agent stopped; if the agent never built, the
    # harness's final build above has just produced one — take it, labelled.
    local build_state compiler_hash build_state_from
    [[ -f "$ws_out/calor-build-state.json" ]] || archive_build_state "$ws" "$ws_out" "harness-final-build"
    build_state="$(python3 "$HARNESS_CAPTURE" build-state "$ws_out/calor-build-state.json" 2>/dev/null || echo '{"compilerHash":null,"source":"helper-error"}')"
    build_state_from="$(cat "$ws_out/.build-state-source" 2>/dev/null || echo none)"
    build_state="$(jq -c --arg from "$build_state_from" '. + {archivedFrom: $from, file: (if $from == "none" then null else "calor-build-state.json" end)}' <<<"$build_state")"
    compiler_hash="$(jq -c '.compilerHash' <<<"$build_state")"

    # WS5 defect probe (loop plan D5.1, Annex A-1.2 M-W1): pass/fail of the
    # per-defect held-out probe test at declared-done. caught=true iff the
    # probe test passes against the final state; a non-compiling final
    # state counts as not-caught (consistent with the all-failing held-out
    # rule). Runs as its own filtered dotnet-test so the per-test outcome
    # is crisp — the aggregate stream only carries counts. null for pairs
    # without a defect manifest.
    local defect=null
    if [[ -f "$PAIR_DIR/defect.json" ]]; then
        local probe_test defect_caught=false smoke_tampered=false
        probe_test=$(jq -r '.probeTest' "$PAIR_DIR/defect.json")
        # caught is adjudicated by the dedicated probe PROJECT (compiles
        # against the starting surface — decoupled from task completion,
        # review of #810 finding 8) and ONLY when the final build succeeded:
        # without that gate the probe would run against a stale Src.dll from
        # an earlier silent build and could report a false catch on a
        # non-compiling final state (review of #810 finding 1, demonstrated).
        # caught=true demands the probe test to have RUN and PASSED — the
        # explicit "Passed: 1" summary line — because exit codes are
        # unreliable (zero-match filters and failed builds can exit 0).
        if [[ $final_build_ok -eq 1 && -d "$ws_out/probe" ]]; then
            CALOR_P0_SHIM_OFF=1 DOTNET_CLI_UI_LANGUAGE=en dotnet test \
                "$ws_out/probe/Probe.csproj" --nologo \
                > "$ws_out/.probe_final.txt" 2>&1 || true
            if grep -qE 'Passed:[[:space:]]+1\b' "$ws_out/.probe_final.txt" \
               && ! grep -qE 'Failed:[[:space:]]+[1-9]' "$ws_out/.probe_final.txt"; then
                defect_caught=true
            fi
        fi
        # Smoke-suite integrity re-check (review of #810 finding 4): the
        # hook blocks agent-tool edits; this catches shell edits/deletions.
        # Mirrors the materialize baseline exactly — obj/ and bin/ excluded so
        # build-generated *.g.cs don't spuriously trip smokeTampered.
        if [[ -f "$ws_out/.smokehash" ]]; then
            local smoke_now
            smoke_now=$(find "$ws/smoke" -type f -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -exec shasum {} + 2>/dev/null | shasum | cut -d' ' -f1)
            [[ "$smoke_now" == "$(cat "$ws_out/.smokehash")" ]] || smoke_tampered=true
        fi
        defect=$(jq -n --arg id "$(jq -r '.id' "$PAIR_DIR/defect.json")" \
                       --arg class "$(jq -r '.class' "$PAIR_DIR/defect.json")" \
                       --arg test "$probe_test" --argjson caught "$defect_caught" \
                       --argjson tampered "$smoke_tampered" \
                       '{id:$id, class:$class, probeTest:$test, caught:$caught, smokeTampered:$tampered}')
    fi

    # v2 telemetry surfacing (loop plan D4.2): mean agent-visible feedback
    # latency over the run's v2 records, and — calor arm only — whether every
    # record's build produced a valid envelope. null when no v2 records exist
    # (e.g. the agent never invoked build/test) or on the C# arm respectively.
    local mean_lat envelope_valid_all
    mean_lat=$(jq -s '[.[] | select(.schema=="loop-telemetry/2") | .feedback_latency_ms]
        | if length==0 then null else (add/length | round) end' "$journal")
    if [[ "$ARM" == "calor" ]]; then
        envelope_valid_all=$(jq -s '[.[] | select(.schema=="loop-telemetry/2")]
            | if length==0 then null else all(.envelope_valid == true) end' "$journal")
    else
        envelope_valid_all=null
    fi

    # M-L2 per-attempt stream (loop plan M3 PR 4): calor_file_write journals
    # one record per attempt into mcp-writes.jsonl; applied/attempts is
    # first-apply validity for the mcp-file mechanism. null when the arm ran
    # without the MCP write path (raw arms, older builds).
    local mcp_writes
    if [[ -s "$ws_out/mcp-writes.jsonl" ]]; then
        # appliedUnhealed nets auto-heal out of M-L2: applied counts writes
        # the tool healed first, so applied/attempts is first-apply-AFTER-
        # AUTOHEAL validity; appliedUnhealed/attempts is the strict form.
        mcp_writes=$(jq -s '{attempts: length,
                             applied: [.[] | select(.applied)] | length,
                             appliedUnhealed: [.[] | select(.applied and (.healApplied | not))] | length,
                             rejected: [.[] | select(.applied | not)] | length,
                             healed: [.[] | select(.healApplied)] | length}' \
                     "$ws_out/mcp-writes.jsonl")
    else
        mcp_writes=null
    fi

    jq -n \
        --arg pair "$PAIR_ID" --arg arm "$ARM_LABEL" --argjson run "$run_idx" \
        --argjson success "$([[ $final_fail -eq 0 ]] && echo true || echo false)" \
        --argjson escaped "$final_fail" --argjson passed "$final_pass" \
        --argjson iterations "$iterations" --argjson itg "$iters_to_green" \
        --argjson censored "$censored" \
        --argjson tin "$tokens_in" --argjson tout "$tokens_out" \
        --argjson token_usage "$token_usage" \
        --argjson null_agent "$NULL_AGENT" \
        --argjson mean_lat "$mean_lat" --argjson env_all "$envelope_valid_all" \
        --argjson mcp_writes "$mcp_writes" \
        --argjson defect "$defect" \
        --arg calor_dll "$CALOR_CLI_DLL" --arg edit_mech "$EDIT_MECHANISM" \
        --arg arm_repo_root "$ARM_REPO_ROOT" \
        --argjson turns "$turns" --argjson agent_builds "$agent_builds" \
        --argjson compiler_hash "$compiler_hash" --argjson build_state "$build_state" \
        --arg arm_config_key "$ARM_CONFIG_KEY" --arg control_arm_kind "$CONTROL_ARM_KIND" \
        --argjson permissive "$PERMISSIVE_EFFECTS" --arg fixture "$FIXTURE_DIR" \
        --arg template_source "$TEMPLATE_SOURCE" \
        '{pair:$pair, arm:$arm, run:$run, taskSuccess:$success,
          escapedBugs:$escaped, heldoutPassed:$passed,
          iterations:$iterations, iterationsToGreen:$itg, censored:$censored,
          invalid:false,
          meanFeedbackLatencyMs:$mean_lat, envelopeValidAll:$env_all,
          mcpWrites:$mcp_writes, defect:$defect,
          calorDll:$calor_dll, armRepoRoot:$arm_repo_root, editMechanism:$edit_mech,
          compilerHash:$compiler_hash, buildState:$build_state,
          armConfigKey:$arm_config_key,
          controlArmKind:(if $control_arm_kind == "" then null else $control_arm_kind end),
          permissiveEffects:$permissive, fixture:$fixture, templateSource:$template_source,
          turns:$turns, agentBuilds:$agent_builds,
          tokens:{input:$tin, output:$tout}, tokenUsage:$token_usage,
          nullAgent:($null_agent==1)}' \
        > "$ws_out/result.json"
    cat "$ws_out/result.json"
}

# ---------------------------------------------------------------------------
# Invalid-slot result (gates doc §0.2): a slot still invalid after the retry
# cap counts as task failure for the arm, marked "invalid": true.
# ---------------------------------------------------------------------------
write_invalid_result() {
    local ws_out="$1" run_idx="$2"
    jq -n \
        --arg pair "$PAIR_ID" --arg arm "$ARM_LABEL" --argjson run "$run_idx" \
        --argjson itg "$((ITERATION_BUDGET + 1))" \
        --argjson escaped "$HELDOUT_TEST_COUNT" \
        --argjson null_agent "$NULL_AGENT" \
        --arg calor_dll "$CALOR_CLI_DLL" --arg edit_mech "$EDIT_MECHANISM" \
        --arg arm_repo_root "$ARM_REPO_ROOT" \
        --arg arm_config_key "$ARM_CONFIG_KEY" --arg control_arm_kind "$CONTROL_ARM_KIND" \
        --argjson permissive "$PERMISSIVE_EFFECTS" --arg fixture "$FIXTURE_DIR" \
        --arg template_source "$TEMPLATE_SOURCE" \
        --argjson has_transcript "$([[ -s "$ws_out/transcript.jsonl" ]] && echo true || echo false)" \
        '{pair:$pair, arm:$arm, run:$run, taskSuccess:false,
          escapedBugs:$escaped, heldoutPassed:0,
          iterations:0, iterationsToGreen:$itg, censored:true,
          invalid:true, defect:null,
          calorDll:$calor_dll, armRepoRoot:$arm_repo_root, editMechanism:$edit_mech,
          compilerHash:null, buildState:{compilerHash:null, source:"invalid", archivedFrom:"none", file:null},
          armConfigKey:$arm_config_key,
          controlArmKind:(if $control_arm_kind == "" then null else $control_arm_kind end),
          permissiveEffects:$permissive, fixture:$fixture, templateSource:$template_source,
          turns:{assistantMessages:null, assistantMessagesTopLevel:null, assistantMessagesSubagent:null,
                 numTurns:null, source:"invalid", transcript:(if $has_transcript then "transcript.jsonl" else null end)},
          agentBuilds:{count:0, file:null},
          tokens:{input:0, output:0}, tokenUsage:{source:"invalid"},
          nullAgent:($null_agent==1)}' \
        > "$ws_out/result.json"
    cat "$ws_out/result.json"
}

# Wipe a run's ws_out for a fresh re-attempt, preserving the invalid.txt log
wipe_ws_out() {
    local ws_out="$1"
    find "$ws_out" -mindepth 1 -maxdepth 1 ! -name invalid.txt -exec rm -rf {} +
}

# ---------------------------------------------------------------------------
check_pins
for (( run=RUN_OFFSET+1; run<=RUN_OFFSET+RUNS; run++ )); do
    WS_OUT="$OUT_DIR/$PAIR_ID/$ARM_LABEL/run-$run"
    mkdir -p "$WS_OUT"

    # Invalid-run re-attempt loop (gates doc §0.2): a detected-invalid run is
    # logged, its ws_out wiped, and the slot re-run on a fresh workspace, up
    # to MAX_INVALID_RETRIES re-attempts; after the cap the slot counts as
    # task failure with "invalid": true.
    for (( attempt=0; attempt<=MAX_INVALID_RETRIES; attempt++ )); do
        WS="$(mktemp -d "${TMPDIR:-/tmp}/p0-${PAIR_ID}-${ARM}-XXXXXX")"
        # Canonicalize (macOS: $TMPDIR lives behind the /var -> /private/var
        # symlink). Agent builds run from the *physical* cwd while shim/metrics
        # builds pass the *logical* $WS path; MSBuild treats the two spellings as
        # different project identities, and the identity flip makes incremental
        # clean delete ProjectReference outputs (Calor.Runtime.dll) from src/bin —
        # every contract-bearing calor-arm pair then fails held-out runs with
        # FileNotFoundException. One physical path removes the ambiguity.
        WS="$(cd "$WS" && pwd -P)"
        SHIM_DIR="$WS_OUT/.shim"

        materialize "$WS" "$WS_OUT"
        write_shim "$WS" "$WS_OUT" "$SHIM_DIR" "$run"
        run_agent "$WS" "$WS_OUT" "$SHIM_DIR"

        # #1094: the agent's declared-done build state, before anything
        # below rebuilds the workspace.
        archive_build_state "$WS" "$WS_OUT" "agent-workspace"

        # Declared-done archival runs immediately after the agent stops —
        # before the final build below can rewrite anything — and its failure
        # is run-invalidating (A-1.3 item 4, #826 M2).
        if reason="$(archive_final_src "$WS" "$WS_OUT")" \
           || reason="$(detect_invalid_run "$WS_OUT" "$AGENT_RC")"; then
            printf '%s attempt=%d agent_rc=%d: %s\n' \
                "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$attempt" "$AGENT_RC" "$reason" \
                >> "$WS_OUT/invalid.txt"
            echo "INVALID run detected (pair=$PAIR_ID arm=$ARM run=$run attempt=$attempt): $reason" >&2
            rm -rf "$WS"
            if (( attempt < MAX_INVALID_RETRIES )); then
                wipe_ws_out "$WS_OUT"   # fresh re-attempt, keep invalid.txt
                continue
            fi
            echo "Retry cap reached; counting run $run as task failure (invalid)" >&2
            write_invalid_result "$WS_OUT" "$run"
            break
        fi

        extract_metrics "$WS" "$WS_OUT" "$run"
        rm -rf "$WS"
        break
    done
done
