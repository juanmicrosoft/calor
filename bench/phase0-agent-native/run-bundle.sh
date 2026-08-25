#!/usr/bin/env bash
# ============================================================================
# WS-W4 Slice-C bundle runner (real-scale agent-native dry-run adapter)
# ============================================================================
#
# ADAPTER over the battle-tested run-pair.sh machinery: it applies the SAME
# proven building blocks (dotnet PATH shim + silent held-out oracle, per-run
# agent.json/result.json, invalid-run detect+retry, watchdog process-group
# kill, --null-agent reference application) to a gen-tasks Slice-C BUNDLE
# instead of an authored pair. The functions marked "REUSED from run-pair.sh"
# are copied verbatim (or all-but-verbatim) from that script; the differences a
# bundle forces are localized to materialize()/the oracle:
#
#   1. Arm workspace = a WHOLE OSS project working copy (bundle's csharp-arm/ or
#      calor-arm/, both plain .cs — the calor arm is round-tripped C#), not a
#      small fixture with a synthesized Src.csproj. It builds itself via its own
#      test project (the regression net), using the Slice-B harness build knobs
#      (TFM + ExtraBuildProperties) keyed by ProjectName.
#   2. Held-out oracle = the bundle's held-out test(s) (a --filter over the
#      project's own suite) + the regression net (the rest of the suite), run
#      SILENTLY by the shim after each build/test and at declared-done. The
#      held-out tests are physically present (filter-based split, per Slice-C);
#      the agent is told the VISIBLE filter and to work the visible suite only.
#   3. Prompt = the scrubbed failing-behavior report (symptom only) from
#      provenance.json; the held-out test is never shown.
#   4. Reference (for --null-agent) is DERIVED, not shipped: see bundle-helpers.py.
#
# Adjudication (per run, at declared-done):
#   caught          = held-out now PASSES  AND regression net still green
#   escaped         = held-out still FAILS (or the final build fails)
#   broke-regression= held-out passes but a previously-green visible test regressed
#   invalid         = agent/plumbing error (retried up to the cap, then recorded)
#
# Usage:
#   ./run-bundle.sh --bundle <dir> --arm csharp|calor [--runs N] [--out <dir>]
#   ./run-bundle.sh --bundle <dir> --arm csharp --null-agent        # apply fix -> expect CAUGHT
#   ./run-bundle.sh --bundle <dir> --arm csharp --null-agent-noop   # do nothing -> expect ESCAPED
#   ./run-bundle.sh ... --corpus <dir> --projects-dir <dir>         # pristine-source roots
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
HELPERS="$SCRIPT_DIR/bundle-helpers.py"
# #881: the ONE derivation of the cost-leg token figure (shared with run-pair.sh).
TOKEN_USAGE="$SCRIPT_DIR/token-usage.py"

MAX_INVALID_RETRIES=2
INVALID_MARKERS=("hit your session limit" "rate limit" "overloaded" "api error")

# -------- REUSED from run-pair.sh (verbatim): invalid-run detection (§0.2) ----
detect_invalid_run() {
    local ws_out="$1" agent_rc="${2:-0}"
    local aj="$ws_out/agent.json"
    if [[ ! -s "$aj" ]]; then echo "agent.json missing or empty"; return 0; fi
    if ! jq -e . "$aj" >/dev/null 2>&1; then echo "agent.json is not valid JSON"; return 0; fi
    local content marker
    content="$( { jq -r '.result // empty' "$aj" 2>/dev/null; cat "$aj"; } | tr '[:upper:]' '[:lower:]')"
    for marker in "${INVALID_MARKERS[@]}"; do
        if [[ "$content" == *"$marker"* ]]; then
            echo "agent output matches error marker: \"$marker\""; return 0
        fi
    done
    # Tightened (review minor): a nonzero agent exit is invalid REGARDLESS of the
    # journal — a crashed/timed-out agent that happened to journal an edit must not
    # be scored escaped. null-agent runs always set AGENT_RC=0, so this only gates
    # real runs (retried on a fresh workspace, then recorded invalid).
    if [[ "$agent_rc" -ne 0 ]]; then
        echo "agent exit code $agent_rc (nonzero -> invalid)"; return 0
    fi
    return 1
}

# -------- REUSED from run-pair.sh (adapted find): declared-done src archival ---
archive_final_src() {
    local ws="$1" ws_out="$2"
    rm -rf "$ws_out/final-src"; mkdir -p "$ws_out/final-src"
    ( cd "$ws/src" && find . -type f -name '*.cs' \
        -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/TestResults/*' -print0 \
      | while IFS= read -r -d '' f; do
            mkdir -p "$ws_out/final-src/$(dirname "$f")"; cp "$f" "$ws_out/final-src/$f"; done )
    return 1   # archival is best-effort here (never run-invalidating for a bundle)
}

# -------- REUSED from run-pair.sh (verbatim): watchdog process-group kill ------
kill_agent_tree() {
    local root="$1" ws_path="$2" out_path="${3:-}"
    local all="" frontier="$root" next p depth
    for depth in 1 2 3 4 5 6; do
        next=""
        for p in $frontier; do next+=" $(pgrep -P "$p" 2>/dev/null || true)"; done
        next="$(echo "$next" | tr -s ' ' | sed 's/^ //')"
        [[ -z "$next" ]] && break
        all+=" $next"; frontier="$next"
    done
    kill -9 -- "-$root" 2>/dev/null || echo "watchdog: pgid kill for -$root failed" >&2
    # shellcheck disable=SC2086
    kill -9 $root $all 2>/dev/null || true
    sleep 1
    if kill -0 "$root" 2>/dev/null || pgrep -f "$ws_path" >/dev/null 2>&1 \
        || { [[ -n "$out_path" ]] && pgrep -f "$out_path" >/dev/null 2>&1; }; then
        echo "watchdog: survivors detected after tree kill; pattern-killing $ws_path" >&2
        pkill -9 -f "$ws_path" 2>/dev/null || true
        [[ -n "$out_path" ]] && pkill -9 -f "$out_path" 2>/dev/null || true
    fi
}

# ---------------------------------------------------------------------------
BUNDLE_DIR=""; ARM=""; RUNS=1
OUT_DIR="$SCRIPT_DIR/epochs/adhoc-bundle"
NULL_AGENT=0; NULL_AGENT_NOOP=0
ITERATION_BUDGET=10; TIMEOUT_SECS=1200
CORPUS_ROOT="$REPO_ROOT/bench/corpus"
PROJECTS_DIR=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --bundle) BUNDLE_DIR="$2"; shift 2 ;;
        --arm) ARM="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --out) OUT_DIR="$2"; shift 2 ;;
        --null-agent) NULL_AGENT=1; shift ;;
        --null-agent-noop) NULL_AGENT=1; NULL_AGENT_NOOP=1; shift ;;
        --iteration-budget) ITERATION_BUDGET="$2"; shift 2 ;;
        --timeout) TIMEOUT_SECS="$2"; shift 2 ;;
        --corpus) CORPUS_ROOT="$2"; shift 2 ;;
        --projects-dir) PROJECTS_DIR="$2"; shift 2 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done
[[ -n "$PROJECTS_DIR" ]] && CORPUS_ROOT="$PROJECTS_DIR"

[[ -n "$BUNDLE_DIR" && -n "$ARM" ]] || { echo "Usage: --bundle <dir> --arm csharp|calor [--runs N] [--null-agent|--null-agent-noop] [--corpus <dir>] [--out <dir>]" >&2; exit 2; }
[[ "$ARM" == "calor" || "$ARM" == "csharp" ]] || { echo "--arm must be calor|csharp" >&2; exit 2; }
BUNDLE_DIR="$(cd "$BUNDLE_DIR" && pwd -P)"
[[ -f "$BUNDLE_DIR/provenance.json" ]] || { echo "ERROR: not a bundle (no provenance.json): $BUNDLE_DIR" >&2; exit 2; }
[[ -d "$BUNDLE_DIR/$ARM-arm" ]] || { echo "ERROR: bundle arm dir missing: $BUNDLE_DIR/$ARM-arm" >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "ERROR: python3 required" >&2; exit 2; }

# ---- Bundle facts from provenance.json ------------------------------------
BUNDLE_ID="$(jq -r '.TaskId' "$BUNDLE_DIR/provenance.json")"
PROJECT="$(jq -r '.ProjectName' "$BUNDLE_DIR/provenance.json")"
REGNET="$(jq -r '.RegressionNetProject' "$BUNDLE_DIR/provenance.json")"
# EXACT-match held-out + visible filters (computed from HeldOut[].FilterName, not
# provenance's substring VisibleTestFilter) — see bundle-helpers.py M1 note. Used
# ONLY in the harness-side oracle, never shown to the agent.
HELDOUT_FILTER="$(python3 "$HELPERS" heldout-filter "$BUNDLE_DIR")"
VISIBLE_FILTER="$(python3 "$HELPERS" visible-filter "$BUNDLE_DIR")"
SYMPTOM="$(jq -r '.FailingBehavior.Symptom // "A previously-correct behavior now produces an incorrect result."' "$BUNDLE_DIR/provenance.json")"
SUBJECT_HINT="$(jq -r '.FailingBehavior.SubjectHint // ""' "$BUNDLE_DIR/provenance.json")"
HELDOUT_COUNT="$(jq -r '.HeldOut | length' "$BUNDLE_DIR/provenance.json")"
[[ -n "$HELDOUT_FILTER" ]] || { echo "ERROR: could not build held-out filter" >&2; exit 3; }
# The test-project directory (relative to the arm root) whose sources carry the
# held-out method(s). The oracle keeps this dir held-out-PRESENT; the agent copy
# has it stripped; the src->oracle sync EXCLUDES it so agent edits never
# overwrite the oracle's held-out tests.
TESTPROJ_DIR="$(dirname "$REGNET")"

# ---- Build knobs by ProjectName (mirrors ProjectConfigs.cs) ----------------
case "$PROJECT" in
    FluentValidation) TFM="net8.0";  EXTRA="-p:NuGetAudit=false -p:TreatWarningsAsErrors=false" ;;
    MediatR)          TFM="net8.0";  EXTRA="-p:NuGetAudit=false -p:TreatWarningsAsErrors=false" ;;
    Serilog)          TFM="net10.0"; EXTRA="-p:NuGetAudit=false -p:TreatWarningsAsErrors=false" ;;
    Synthetic|Synthetic2) TFM="net10.0"; EXTRA="" ;;
    *) TFM=""; EXTRA="" ;;
esac

mkdir -p "$OUT_DIR"; OUT_DIR="$(cd "$OUT_DIR" && pwd)"
ARM_LABEL="$ARM"
[[ $NULL_AGENT_NOOP -eq 1 ]] && ARM_LABEL="$ARM+noop"

echo "bundle=$BUNDLE_ID project=$PROJECT arm=$ARM regnet=$REGNET tfm=$TFM heldout='$HELDOUT_FILTER'" >&2

# ---------------------------------------------------------------------------
# Sync the agent's edited sources into the oracle copy, EXCEPT the test-project
# directory (the oracle keeps its held-out-present test sources). Every non-test
# .cs the agent may have edited propagates so the oracle tests the agent's
# current library; the held-out tests are never overwritten.
# ---------------------------------------------------------------------------
sync_to_oracle() {
    local ws="$1" ws_oracle="$2"
    # Exclude ALL test-source roots (not just dirname(REGNET)): any path under a
    # */test/*, */tests/* or *.Tests/ directory (case-insensitive) — the same set
    # the agent's PreToolUse hook blocks. A test helper the agent could edit must
    # never propagate to the oracle, or the agent could steer the held-out result.
    ( cd "$ws/src" && find . -type f -name '*.cs' \
        -not -path "./$TESTPROJ_DIR/*" \
        -not -ipath '*/test/*' -not -ipath '*/tests/*' -not -ipath '*.tests/*' \
        -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/TestResults/*' -print ) \
      | while IFS= read -r rel; do
            rel="${rel#./}"
            mkdir -p "$ws_oracle/$(dirname "$rel")"
            cp "$ws/src/$rel" "$ws_oracle/$rel"
        done
}

# ---------------------------------------------------------------------------
# Workspace materialization. TWO copies:
#   $ws/src        agent-visible: whole arm working copy, held-out method(s)
#                  PHYSICALLY STRIPPED from the test sources (no oracle leak).
#   $ws_oracle     harness-only (a separate mktemp the agent has no path to):
#                  whole arm working copy with the held-out test(s) PRESENT;
#                  its library is kept in sync with the agent's edits.
# ---------------------------------------------------------------------------
materialize() {
    local ws="$1" ws_out="$2" ws_oracle="$3"
    mkdir -p "$ws/src" "$ws_oracle"
    cp -R "$BUNDLE_DIR/$ARM-arm/." "$ws/src/"
    cp -R "$BUNDLE_DIR/$ARM-arm/." "$ws_oracle/"
    find "$ws/src" "$ws_oracle" -type d \( -name bin -o -name obj -o -name TestResults \) -prune -exec rm -rf {} + 2>/dev/null || true

    # Strip the held-out method(s) from the AGENT copy. Fail-loud: a silent miss
    # would leave the oracle readable/runnable by the agent (fabricated measure).
    if ! python3 "$HELPERS" strip-heldout "$BUNDLE_DIR" "$ws/src" > "$ws_out/strip.txt" 2>&1; then
        echo "FATAL: held-out strip failed for bundle=$BUNDLE_ID arm=$ARM (oracle would leak):" >&2
        cat "$ws_out/strip.txt" >&2
        exit 3
    fi
    cat "$ws_out/strip.txt" >&2

    # Agent-facing task description (symptom only; the held-out test is never shown).
    cat > "$ws/spec.md" <<EOF
# Task: fix the reported regression

A previously-correct behavior in this project now produces an incorrect result.

## Failing-behavior report (the symptom, not the test)

> $SYMPTOM
$( [[ -n "$SUBJECT_HINT" && "$SUBJECT_HINT" != "null" ]] && echo "> " && echo "> Subject hint: $SUBJECT_HINT" )

## What to do

- The defect lives in the project's library source under \`src/\`. Find and fix it.
- Run the test suite with: \`dotnet test $REGNET\`${TFM:+ --framework $TFM}
  (it is the project's own suite; run it as-is).
- Do NOT add, remove, or modify any test, and do not edit the test project.
- Iteration budget: $ITERATION_BUDGET build/test cycles. You are done when the
  library builds cleanly and the test suite is green.
EOF

    # .g.cs / test-edit guard for real agent runs (parity with run-pair.sh §1).
    if [[ $NULL_AGENT -eq 0 ]]; then
        mkdir -p "$ws/.claude"
        cat > "$ws/.claude/settings.json" <<'EOF'
{
  "hooks": { "PreToolUse": [ { "matcher": "Write|Edit", "hooks": [
    { "type": "command",
      "command": "f=$(jq -r '.tool_input.file_path // empty'); grep -qiE '(\\.tests?/|test/|/tests/)' <<<\"$f\" && { echo 'BLOCKED: test sources are the harness-held regression net; not agent-editable' >&2; exit 2; }; exit 0" }
  ] } ] }
}
EOF
    fi

    # Baseline src hash (library sources only) for edit detection.
    local base_hash
    base_hash=$(find "$ws/src" -type f -name '*.cs' \
        -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/TestResults/*' \
        -exec shasum {} + 2>/dev/null | shasum | cut -d' ' -f1)
    echo "$base_hash" > "$ws_out/.lasthash"
    echo 0 > "$ws_out/.itercount"

    # Starting-state oracle baseline: the mutated arm must present held-out
    # FAILING and the visible suite GREEN (the eligibility guarantee). We record
    # the visible-fail baseline so a declared-done visible failure is scored as a
    # regression rather than a pre-existing red.
    run_oracle "$ws" "$ws_oracle" "$ws_out/.baseline" "both"
    cp "$ws_out/.baseline.oracle.json" "$ws_out/baseline-oracle.json" 2>/dev/null || true
}

# ---------------------------------------------------------------------------
# The silent held-out oracle. Syncs the agent's edits into the oracle copy (which
# alone carries the held-out test), builds its regression-net project, then runs
# the held-out filter and (mode=both) the visible filter, writing
# <prefix>.oracle.json with pass/fail counts. The agent never sees this copy.
# ---------------------------------------------------------------------------
run_oracle() {
    local ws="$1" ws_oracle="$2" prefix="$3" mode="$4"
    local real_dotnet ho_pass=0 ho_fail="$HELDOUT_COUNT" vis_pass=0 vis_fail=-1 build_ok=0
    real_dotnet="${REAL_DOTNET:-$(command -v dotnet)}"
    sync_to_oracle "$ws" "$ws_oracle"
    if CALOR_P0_SHIM_OFF=1 "$real_dotnet" build "$ws_oracle/$REGNET" ${TFM:+--framework $TFM} $EXTRA --nologo -v q > "$prefix.build.txt" 2>&1; then
        build_ok=1
        if CALOR_P0_SHIM_OFF=1 "$real_dotnet" test "$ws_oracle/$REGNET" ${TFM:+--framework $TFM} $EXTRA --no-build --filter "$HELDOUT_FILTER" --nologo -v q > "$prefix.ho.txt" 2>&1; then
            ho_fail=0; ho_pass=$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$prefix.ho.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
        else
            ho_fail=$(grep -oE 'Failed:[[:space:]]+[0-9]+' "$prefix.ho.txt" | grep -oE '[0-9]+' | head -1 || echo "$HELDOUT_COUNT")
            ho_pass=$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$prefix.ho.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
        fi
        if [[ "$mode" == "both" && -n "$VISIBLE_FILTER" ]]; then
            if CALOR_P0_SHIM_OFF=1 "$real_dotnet" test "$ws_oracle/$REGNET" ${TFM:+--framework $TFM} $EXTRA --no-build --filter "$VISIBLE_FILTER" --nologo -v q > "$prefix.vis.txt" 2>&1; then
                vis_fail=0; vis_pass=$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$prefix.vis.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
            else
                vis_fail=$(grep -oE 'Failed:[[:space:]]+[0-9]+' "$prefix.vis.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
                vis_pass=$(grep -oE 'Passed:[[:space:]]+[0-9]+' "$prefix.vis.txt" | grep -oE '[0-9]+' | head -1 || echo 0)
            fi
        fi
    fi
    # Integer guards: an unexpected `dotnet test` summary shape can leave a var
    # empty (a no-match grep whose downstream head succeeds), which would make the
    # jq --argjson below fail and drop the record. Default anything non-numeric.
    [[ "$ho_pass" =~ ^[0-9]+$ ]] || ho_pass=0
    [[ "$ho_fail" =~ ^[0-9]+$ ]] || ho_fail="$HELDOUT_COUNT"
    [[ "$vis_pass" =~ ^[0-9]+$ ]] || vis_pass=0
    [[ "$vis_fail" =~ ^-?[0-9]+$ ]] || vis_fail=-1
    jq -n --argjson bo "$build_ok" --argjson hp "$ho_pass" --argjson hf "$ho_fail" \
          --argjson vp "$vis_pass" --argjson vf "$vis_fail" \
        '{build_ok:($bo==1), heldout_pass:$hp, heldout_fail:$hf, visible_pass:$vp, visible_fail:$vf}' \
        > "$prefix.oracle.json"
}

# ---------------------------------------------------------------------------
# Agent-facing dotnet shim (FIRST on the agent's PATH, agent-readable). It ONLY
# runs the real dotnet for the agent's own build/test and journals invocation
# metadata (cmd/exit, edit-hash, iteration ordinal, latency). It holds NO oracle
# path and NO held-out filter (review C) — all oracle work is harness-side.
# ---------------------------------------------------------------------------
write_shim() {
    local ws="$1" ws_out="$2" shim_dir="$3" run_idx="$4"
    local real_dotnet; real_dotnet="$(command -v dotnet)"
    mkdir -p "$shim_dir"
    cat > "$shim_dir/dotnet" <<EOF
#!/usr/bin/env bash
set -uo pipefail
if [[ "\${CALOR_P0_SHIM_OFF:-0}" == "1" ]]; then exec "$real_dotnet" "\$@"; fi
now_ms() { if command -v python3 >/dev/null 2>&1; then python3 -c 'import time,sys;sys.stdout.write(str(int(time.time()*1000)))'; else echo \$(( \$(date +%s) * 1000 )); fi; }
ts_iso="\$(date -u +%Y-%m-%dT%H:%M:%SZ)"; t0=\$(now_ms)
"$real_dotnet" "\$@"; rc=\$?
lat=\$(( \$(now_ms) - t0 )); [[ \$lat -lt 0 ]] && lat=0
case "\${1:-}" in
  build|test|run)
    lock_tries=0
    while ! mkdir "$ws_out/.telemetry.lock" 2>/dev/null; do
      lock_tries=\$((lock_tries+1)); [[ \$lock_tries -gt 1200 ]] && break; sleep 0.05
    done
    trap 'rmdir "$ws_out/.telemetry.lock" 2>/dev/null || true' EXIT
    hash=\$(find "$ws/src" -type f -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -not -path '*/TestResults/*' -exec shasum {} + 2>/dev/null | shasum | cut -d' ' -f1)
    prev=\$(cat "$ws_out/.lasthash" 2>/dev/null || echo none)
    edited=\$([[ "\$hash" != "\$prev" ]] && echo true || echo false)
    echo "\$hash" > "$ws_out/.lasthash"
    iteration=null
    if [[ "\$edited" == "true" ]]; then
      iteration=\$(( \$(cat "$ws_out/.itercount" 2>/dev/null || echo 0) + 1 ))
      echo "\$iteration" > "$ws_out/.itercount"
    fi
    # NO ORACLE INFO HERE (review C). This script is first on the agent's PATH and
    # the agent can \`cat\` it, so it must never contain the oracle tree path or the
    # held-out --filter. The oracle runs entirely in the parent (run-bundle.sh),
    # whose oracle path/filter live only in its own process. The shim journals
    # ONLY invocation metadata over the agent's OWN workspace (its cwd — no secret).
    jq -cn --arg ts "\$ts_iso" --arg bundle "$BUNDLE_ID" --arg arm "$ARM_LABEL" \\
      --argjson run $run_idx --arg cmd "\${1}" --argjson exit "\$rc" \\
      --argjson edited "\$edited" --argjson iteration "\$iteration" \\
      --argjson lat "\$lat" --arg hash "\$hash" \\
      '{schema:"bundle-telemetry/2", ts:\$ts, bundle:\$bundle, arm:\$arm, run:\$run,
        iteration:\$iteration, cmd:\$cmd, exit:\$exit, edited:\$edited,
        feedback_latency_ms:\$lat, src_tree_hash:\$hash}' \\
      >> "$ws_out/journal.jsonl"
    ;;
esac
exit \$rc
EOF
    chmod +x "$shim_dir/dotnet"
}

# ---------------------------------------------------------------------------
run_agent() {
    local ws="$1" ws_out="$2" shim_dir="$3"
    AGENT_RC=0
    local prompt
    prompt="You are working in $ws/src. Read $ws/spec.md and fix the reported regression in the library source under src/, following the conventions already present. Build/test from $ws/src. Do not modify any test. Stop when the library builds cleanly and the visible suite is green."

    if [[ $NULL_AGENT -eq 1 ]]; then
        # Observed starter build (proves the mutated arm builds through the shim).
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" dotnet build "$REGNET" ${TFM:+--framework $TFM} $EXTRA --nologo -v q >/dev/null 2>&1 ) || \
            echo "null-agent: starter build non-zero (bundle=$BUNDLE_ID arm=$ARM)" >&2
        if [[ $NULL_AGENT_NOOP -eq 1 ]]; then
            # NEGATIVE CONTROL: leave the defect in place -> expect ESCAPED.
            echo '{"null_agent":true,"noop":true}' > "$ws_out/agent.json"
            return 0
        fi
        # Apply the derived reference (the un-mutated source = the fix), then one
        # observed build so the shim journals an edited iteration -> expect CAUGHT.
        if ! python3 "$HELPERS" apply-fix "$BUNDLE_DIR" "$ARM" "$ws/src" "$CORPUS_ROOT" "$REPO_ROOT" > "$ws_out/.applyfix.txt" 2>&1; then
            echo "null-agent: reference underivable (bundle=$BUNDLE_ID arm=$ARM) -> SKIP: $(cat "$ws_out/.applyfix.txt")" >&2
            echo '{"null_agent":true,"skipped":true}' > "$ws_out/agent.json"
            # SKIP marker (not a verdict) — extract_metrics emits outcome:skipped.
            printf 'null-agent reference underivable: %s' "$(cat "$ws_out/.applyfix.txt")" > "$ws_out/.skip"
            return 0
        fi
        cat "$ws_out/.applyfix.txt" >&2
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" dotnet build "$REGNET" ${TFM:+--framework $TFM} $EXTRA --nologo -v q >/dev/null 2>&1 ) || true
        echo '{"null_agent":true}' > "$ws_out/agent.json"
        return 0
    fi

    # ---- Real agent (spend). Copied from run-pair.sh: timeout + watchdog. ----
    local timeout_bin=""
    if command -v timeout >/dev/null 2>&1; then timeout_bin="timeout"
    elif command -v gtimeout >/dev/null 2>&1; then timeout_bin="gtimeout"; fi
    local model_args=()
    [[ -n "${CLAUDE_MODEL:-}" && "${CLAUDE_MODEL}" != "default" ]] && model_args=(--model "$CLAUDE_MODEL")
    local rc=0
    if [[ -n "$timeout_bin" ]]; then
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" \
            "$timeout_bin" -k 10 "$TIMEOUT_SECS" \
            claude --print --output-format json --dangerously-skip-permissions "${model_args[@]}" \
            "$prompt" > "$ws_out/agent.json" 2> "$ws_out/agent.err" ) || rc=$?
    else
        set -m
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" \
            claude --print --output-format json --dangerously-skip-permissions "${model_args[@]}" \
            "$prompt" > "$ws_out/agent.json" 2> "$ws_out/agent.err" ) &
        local agent_pid=$!
        local deadline=$(( SECONDS + TIMEOUT_SECS ))
        while kill -0 "$agent_pid" 2>/dev/null; do
            if (( SECONDS >= deadline )); then
                echo "watchdog: agent exceeded ${TIMEOUT_SECS}s; killing $agent_pid" >&2
                kill_agent_tree "$agent_pid" "$ws" "$ws_out"; break
            fi
            sleep 5
        done
        wait "$agent_pid" 2>/dev/null || rc=$?
        set +m
    fi
    AGENT_RC=$rc
    # NB: an if-block (not `[[ ]] && echo`) so this function returns 0 on agent
    # SUCCESS (rc=0). Under `set -e`, a trailing `[[ 0 -ne 0 ]] &&` returns 1 and
    # aborts the whole run before adjudication — a footgun that only manifests on
    # a real (non-null) agent success, which the null-agent verification cannot
    # exercise.
    if [[ $rc -ne 0 ]]; then echo "agent exit: $rc" >> "$ws_out/agent.err"; fi
    return 0
}

# ---------------------------------------------------------------------------
extract_metrics() {
    local ws="$1" ws_out="$2" run_idx="$3" wall="$4" ws_oracle="$5"
    local journal="$ws_out/journal.jsonl"; touch "$journal"

    # SKIP (not a verdict): a null-agent run whose reference could not be derived
    # (e.g. calor arm, mutated line not uniquely locatable). Recorded as skipped
    # so it never counts as caught/escaped — matches the README claim.
    if [[ -f "$ws_out/.skip" ]]; then
        jq -n --arg bundle "$BUNDLE_ID" --arg project "$PROJECT" --arg arm "$ARM_LABEL" \
              --argjson run "$run_idx" --arg reason "$(cat "$ws_out/.skip")" \
              --argjson null_agent "$NULL_AGENT" --argjson noop "$NULL_AGENT_NOOP" \
            '{bundle:$bundle, project:$project, arm:$arm, run:$run, outcome:"skipped",
              skipReason:$reason, invalid:false, escapedBugs:null,
              nullAgent:($null_agent==1), nullAgentNoop:($noop==1)}' \
            > "$ws_out/result.json"
        cat "$ws_out/result.json"; return 0
    fi

    # Final declared-done oracle: held-out + visible (regression) legs.
    run_oracle "$ws" "$ws_oracle" "$ws_out/.final" "both"
    local fbuild fhp fhf fvp fvf
    fbuild=$(jq -r '.build_ok' "$ws_out/.final.oracle.json")
    fhp=$(jq -r '.heldout_pass' "$ws_out/.final.oracle.json")
    fhf=$(jq -r '.heldout_fail' "$ws_out/.final.oracle.json")
    fvp=$(jq -r '.visible_pass' "$ws_out/.final.oracle.json")
    fvf=$(jq -r '.visible_fail' "$ws_out/.final.oracle.json")
    local base_vf; base_vf=$(jq -r '.visible_fail' "$ws_out/baseline-oracle.json" 2>/dev/null || echo 0)
    [[ "$base_vf" =~ ^-?[0-9]+$ ]] || base_vf=0
    [[ "$base_vf" -lt 0 ]] && base_vf=0

    # Adjudication.
    local outcome
    if [[ "$fbuild" != "true" ]]; then
        outcome="escaped"          # non-building declared-done = defect not fixed
    elif [[ "$fhf" -gt 0 ]]; then
        outcome="escaped"
    elif [[ "$fvf" -gt "$base_vf" ]]; then
        outcome="broke-regression"
    else
        outcome="caught"
    fi

    # Iterations to declared-done = journaled edited build/test invocations (the
    # agent stops when it believes it is done). Per-iteration held-out timing was
    # removed with the shim's oracle (review C) — the verdict comes from the
    # declared-done oracle above, and the agent's edit cadence is unaffected.
    local iterations
    iterations=$(jq -s '[.[] | select(.edited==true)] | length' "$journal")

    # Cost + tokens from the agent envelope. Cost = summed total_cost_usd; NEVER
    # hand-price tokens. Tokens come from token-usage.py (#881): the envelope's
    # top-level usage.output_tokens covers only the final turn and under-counts
    # up to 55x on subagent-delegating / compaction-resumed runs; the helper
    # sums modelUsage[*] instead and reports both figures for audit.
    local cost=0 tin=0 tout=0 token_usage='{}'
    if [[ -f "$ws_out/agent.json" ]] && jq -e . "$ws_out/agent.json" >/dev/null 2>&1; then
        cost=$(jq -s 'map(.total_cost_usd // 0) | add // 0' "$ws_out/agent.json" 2>/dev/null || echo 0)
        token_usage=$(python3 "$TOKEN_USAGE" "$ws_out/agent.json" 2>/dev/null || echo '{}')
        tin=$(jq -r '.input_tokens_corrected // 0' <<<"$token_usage")
        tout=$(jq -r '.output_tokens_corrected // 0' <<<"$token_usage")
        if [[ "$(jq -r '.undercount_flagged // false' <<<"$token_usage")" == "true" ]]; then
            echo "WARN (#881): agent.json usage.output_tokens=$(jq -r '.output_tokens_naive' <<<"$token_usage") under-counts; modelUsage total=$tout (origin=$(jq -r '.origin_kind // "none"' <<<"$token_usage")). Recording the corrected figure." >&2
        fi
    fi

    jq -n \
        --arg bundle "$BUNDLE_ID" --arg project "$PROJECT" --arg arm "$ARM_LABEL" --argjson run "$run_idx" \
        --arg outcome "$outcome" \
        --argjson fbuild "$([[ "$fbuild" == "true" ]] && echo true || echo false)" \
        --argjson hp "$fhp" --argjson hf "$fhf" --argjson vp "$fvp" --argjson vf "$fvf" \
        --argjson basevf "$base_vf" --argjson escaped "$fhf" \
        --argjson iterations "$iterations" \
        --argjson cost "$cost" --argjson tin "$tin" --argjson tout "$tout" \
        --argjson token_usage "$token_usage" \
        --argjson wall "$wall" \
        --argjson null_agent "$NULL_AGENT" --argjson noop "$NULL_AGENT_NOOP" \
        --arg mutfile "$(jq -r '.Provenance.MutatedFileRelPath' "$BUNDLE_DIR/provenance.json")" \
        '{bundle:$bundle, project:$project, arm:$arm, run:$run, outcome:$outcome,
          finalBuildOk:$fbuild, heldoutPass:$hp, heldoutFail:$hf,
          visiblePass:$vp, visibleFail:$vf, visibleFailBaseline:$basevf,
          escapedBugs:$escaped, iterations:$iterations, iterationsToDeclaredDone:$iterations,
          costUsd:$cost, tokens:{input:$tin, output:$tout}, tokenUsage:$token_usage,
          wallClockSeconds:$wall,
          invalid:false, nullAgent:($null_agent==1), nullAgentNoop:($noop==1),
          mutatedFile:$mutfile,
          presentationAsymmetry:"Calor arm = machine-converted round-tripped C#; C# arm = idiomatic original. Bias is against Calor."}' \
        > "$ws_out/result.json"
    cat "$ws_out/result.json"
}

write_invalid_result() {
    local ws_out="$1" run_idx="$2"
    jq -n --arg bundle "$BUNDLE_ID" --arg project "$PROJECT" --arg arm "$ARM_LABEL" --argjson run "$run_idx" \
        --argjson escaped "$HELDOUT_COUNT" \
        --argjson null_agent "$NULL_AGENT" --argjson noop "$NULL_AGENT_NOOP" \
        '{bundle:$bundle, project:$project, arm:$arm, run:$run, outcome:"invalid",
          finalBuildOk:false, heldoutFail:$escaped, escapedBugs:$escaped,
          iterations:0, iterationsToDeclaredDone:0,
          costUsd:0, tokens:{input:0, output:0}, wallClockSeconds:0,
          invalid:true, nullAgent:($null_agent==1), nullAgentNoop:($noop==1)}' \
        > "$ws_out/result.json"
    cat "$ws_out/result.json"
}

wipe_ws_out() { find "$1" -mindepth 1 -maxdepth 1 ! -name invalid.txt -exec rm -rf {} +; }

# ---------------------------------------------------------------------------
for (( run=1; run<=RUNS; run++ )); do
    WS_OUT="$OUT_DIR/$BUNDLE_ID/$ARM_LABEL/run-$run"
    mkdir -p "$WS_OUT"
    for (( attempt=0; attempt<=MAX_INVALID_RETRIES; attempt++ )); do
        WS="$(mktemp -d "${TMPDIR:-/tmp}/p0b-${BUNDLE_ID}-${ARM}-XXXXXX")"
        WS="$(cd "$WS" && pwd -P)"
        # The oracle copy lives in a SEPARATE mktemp tree the agent has no path
        # to (never named in prompt/spec/cwd) — the physical hide of the oracle.
        WS_ORACLE="$(mktemp -d "${TMPDIR:-/tmp}/p0bO-${BUNDLE_ID}-${ARM}-XXXXXX")"
        WS_ORACLE="$(cd "$WS_ORACLE" && pwd -P)"
        SHIM_DIR="$WS_OUT/.shim"
        materialize "$WS" "$WS_OUT" "$WS_ORACLE"
        write_shim "$WS" "$WS_OUT" "$SHIM_DIR" "$run"
        _t0=$SECONDS
        run_agent "$WS" "$WS_OUT" "$SHIM_DIR"
        _wall=$(( SECONDS - _t0 ))
        archive_final_src "$WS" "$WS_OUT" || true
        if reason="$(detect_invalid_run "$WS_OUT" "$AGENT_RC")"; then
            printf '%s attempt=%d agent_rc=%d: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$attempt" "$AGENT_RC" "$reason" >> "$WS_OUT/invalid.txt"
            echo "INVALID run (bundle=$BUNDLE_ID arm=$ARM_LABEL run=$run attempt=$attempt): $reason" >&2
            rm -rf "$WS" "$WS_ORACLE"
            if (( attempt < MAX_INVALID_RETRIES )); then wipe_ws_out "$WS_OUT"; continue; fi
            write_invalid_result "$WS_OUT" "$run"; break
        fi
        extract_metrics "$WS" "$WS_OUT" "$run" "$_wall" "$WS_ORACLE"
        # CALOR_BUNDLE_KEEP_WS=1 preserves the trees for the oracle-isolation audit
        # (grep the agent workspace + shim for the oracle path / held-out names).
        if [[ "${CALOR_BUNDLE_KEEP_WS:-0}" == "1" ]]; then
            echo "KEEP_WS agent=$WS oracle=$WS_ORACLE shim=$SHIM_DIR" >&2
        else
            rm -rf "$WS" "$WS_ORACLE"
        fi
        break
    done
done
