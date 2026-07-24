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
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

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
    return 1
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

PAIR_DIR=""
ARM=""
RUNS=1
OUT_DIR="$SCRIPT_DIR/epochs/adhoc"
NULL_AGENT=0
ITERATION_BUDGET=10
TIMEOUT_SECS=600
EXEMPLAR_FILE=""
EDIT_MECHANISM="raw"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --pair) PAIR_DIR="$2"; shift 2 ;;
        --arm) ARM="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --out) OUT_DIR="$2"; shift 2 ;;
        --null-agent) NULL_AGENT=1; shift ;;
        --exemplar) EXEMPLAR_FILE="$2"; shift 2 ;;
        --edit-mechanism) EDIT_MECHANISM="$2"; shift 2 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done

[[ -n "$PAIR_DIR" && -n "$ARM" ]] || { echo "Usage: --pair <dir> --arm calor|csharp [--runs N] [--null-agent] [--exemplar <file>] [--edit-mechanism raw|mcp-file|mcp-node] [--out <dir>]" >&2; exit 2; }
[[ "$ARM" == "calor" || "$ARM" == "csharp" ]] || { echo "--arm must be calor|csharp" >&2; exit 2; }
case "$EDIT_MECHANISM" in
    raw|mcp-file) ;;
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
PAIR_DIR="$(cd "$PAIR_DIR" && pwd)"
PAIR_ID="$(jq -r .id "$PAIR_DIR/pair.json")"
TIMEOUT_SECS="$(jq -r '.timeoutSeconds // 600' "$PAIR_DIR/pair.json")"

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
CALOR_CLI_DLL=""
for cli_cfg in Release Debug; do
    if [[ -f "$REPO_ROOT/src/Calor.Compiler/bin/$cli_cfg/net10.0/calor.dll" ]]; then
        CALOR_CLI_DLL="$REPO_ROOT/src/Calor.Compiler/bin/$cli_cfg/net10.0/calor.dll"
        break
    fi
done
if [[ "$ARM" == "calor" && -z "$CALOR_CLI_DLL" ]]; then
    echo "WARNING: calor.dll not found (build src/Calor.Compiler first); calor-arm records will carry envelope_valid=null and empty edit_target_ids" >&2
fi

# ---------------------------------------------------------------------------
# Config pin check (gates doc §0.2): calor arm must run enforced, strict,
# contract-debug, Z3 present. Violations are invalid runs, detected up front.
# ---------------------------------------------------------------------------
check_pins() {
    if [[ "$ARM" == "calor" ]]; then
        local cfg
        cfg="$(jq -r '.arms.calor.config | "\(.enforceEffects) \(.permissiveEffects) \(.contractMode) \(.z3Required)"' "$PAIR_DIR/pair.json")"
        [[ "$cfg" == "true false debug true" ]] || { echo "INVALID: pair.json calor config violates gates-doc pin: $cfg" >&2; exit 3; }
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

    cp -R "$PAIR_DIR/$ARM/." "$ws/src/"
    cp "$PAIR_DIR/spec.md" "$ws/spec.md"

    if [[ "$ARM" == "calor" ]]; then
        sed "s|__REPO_ROOT__|$REPO_ROOT|g" \
            "$SCRIPT_DIR/templates/calor-arm/CalorArm.csproj.template" > "$ws/src/Src.csproj"
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

    # .g.cs write-block for the calor arm (gates doc §1) via Claude hook config.
    # With --edit-mechanism mcp-file the same PreToolUse hook additionally
    # blocks direct Edit/Write on .calr files, steering the agent to the calor
    # MCP file tools (arm-constraint enforcement, loop plan D4.2). mcp-node is
    # descoped (Call 1/E1) and gets no enforcement; raw is the no-op default.
    if [[ "$ARM" == "calor" && $NULL_AGENT -eq 0 ]]; then
        mkdir -p "$ws/.claude"
        local calr_block=""
        if [[ "$EDIT_MECHANISM" == "mcp-file" ]]; then
            # NB: this fragment is spliced into a JSON string value below, so
            # the shell double-quotes around \$f must land JSON-escaped (\")
            calr_block="grep -q '\\\\.calr$' <<<\\\"\$f\\\" && { echo 'BLOCKED: this arm requires the calor MCP file tools for .calr edits (edit-mechanism: mcp-file); direct Edit/Write on .calr files is disabled' >&2; exit 2; }; "
        fi
        cat > "$ws/.claude/settings.json" <<EOF
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Write|Edit",
        "hooks": [
          { "type": "command",
            "command": "f=\$(jq -r '.tool_input.file_path // empty'); grep -q '\\\\.g\\\\.cs$' <<<\"\$f\" && { echo 'BLOCKED: .g.cs files are generated; edit the .calr source' >&2; exit 2; }; ${calr_block}exit 0" }
        ]
      }
    ]
  }
}
EOF
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
case "\${1:-}" in
  build|test|run)
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

    # feedback_latency_ms is stamped HERE: the envelope capture and id
    # attribution below are harness bookkeeping run after the agent-visible
    # portion, so they are excluded from the loop-latency measure (schema doc
    # "the shim records the split when it can"; D4.2 keeps them off the
    # latency-critical path by construction).
    lat=\$(( \$(now_ms) - t0 )); [[ \$lat -lt 0 ]] && lat=0

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
          CALOR_P0_SHIM_OFF=1 "$real_dotnet" "$CALOR_CLI_DLL" "\${inputs[@]}" \\
            --enforce-effects --contract-mode debug --no-telemetry --format json \\
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
run_agent() {
    local ws="$1" ws_out="$2" shim_dir="$3"
    AGENT_RC=0
    local prompt
    prompt="You are working in $ws/src. Read $ws/spec.md and complete the task it describes — implementing missing operations and/or modifying existing behavior as specified — in the existing source files, following the conventions already present. The iteration budget is $ITERATION_BUDGET build/test cycles. Build with 'dotnet build' from $ws/src to check your work. Do not create test files; do not modify the project file. Stop when the spec is fully satisfied and the project builds cleanly (the starter already builds, so a clean build alone does not mean you are done)."
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
        cp -R "$PAIR_DIR/reference/$ARM/." "$ws/src/"
        ( cd "$ws/src" && PATH="$shim_dir:$PATH" dotnet build --nologo -v q >/dev/null 2>&1 ) || true
        echo '{"null_agent":true}' > "$ws_out/agent.json"
        return 0
    fi

    # Portable timeout: coreutils timeout/gtimeout when present, else a bash
    # watchdog (macOS ships neither by default — 42 runs learned this once)
    ( cd "$ws/src" && PATH="$shim_dir:$PATH" \
        claude --print --output-format json --dangerously-skip-permissions \
        "$prompt" > "$ws_out/agent.json" 2> "$ws_out/agent.err" ) &
    local agent_pid=$!
    ( sleep "$TIMEOUT_SECS" && kill -9 "$agent_pid" 2>/dev/null ) &
    local watchdog_pid=$!
    local rc=0
    wait "$agent_pid" 2>/dev/null || rc=$?
    kill "$watchdog_pid" 2>/dev/null || true
    wait "$watchdog_pid" 2>/dev/null || true
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
    local final_pass=0 final_fail=$HELDOUT_TEST_COUNT
    if CALOR_P0_SHIM_OFF=1 dotnet build "$ws/src/Src.csproj" --nologo -v q > "$ws_out/.src_final.txt" 2>&1; then
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

    local tokens_in=0 tokens_out=0
    if [[ -f "$ws_out/agent.json" ]] && jq -e '.usage' "$ws_out/agent.json" >/dev/null 2>&1; then
        tokens_in=$(jq -r '.usage.input_tokens // 0' "$ws_out/agent.json")
        tokens_out=$(jq -r '.usage.output_tokens // 0' "$ws_out/agent.json")
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

    jq -n \
        --arg pair "$PAIR_ID" --arg arm "$ARM_LABEL" --argjson run "$run_idx" \
        --argjson success "$([[ $final_fail -eq 0 ]] && echo true || echo false)" \
        --argjson escaped "$final_fail" --argjson passed "$final_pass" \
        --argjson iterations "$iterations" --argjson itg "$iters_to_green" \
        --argjson censored "$censored" \
        --argjson tin "$tokens_in" --argjson tout "$tokens_out" \
        --argjson null_agent "$NULL_AGENT" \
        --argjson mean_lat "$mean_lat" --argjson env_all "$envelope_valid_all" \
        '{pair:$pair, arm:$arm, run:$run, taskSuccess:$success,
          escapedBugs:$escaped, heldoutPassed:$passed,
          iterations:$iterations, iterationsToGreen:$itg, censored:$censored,
          invalid:false,
          meanFeedbackLatencyMs:$mean_lat, envelopeValidAll:$env_all,
          tokens:{input:$tin, output:$tout}, nullAgent:($null_agent==1)}' \
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
        '{pair:$pair, arm:$arm, run:$run, taskSuccess:false,
          escapedBugs:$escaped, heldoutPassed:0,
          iterations:0, iterationsToGreen:$itg, censored:true,
          invalid:true,
          tokens:{input:0, output:0}, nullAgent:($null_agent==1)}' \
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
for (( run=1; run<=RUNS; run++ )); do
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

        if reason="$(detect_invalid_run "$WS_OUT" "$AGENT_RC")"; then
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
