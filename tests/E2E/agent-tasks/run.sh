#!/usr/bin/env bash
# ============================================================================
# Phase 2 gate harness adapter (RFC §10, protocol v2 §10.1.a).
# ============================================================================
#
# Invoked by scripts/run_phase_2_gate.py:dispatch_run with:
#
#     bash tests/E2E/agent-tasks/run.sh \
#         --trial-id    <kind>:<id>             # auditing handle only
#         --kind        task|template
#         --task-dir    <absolute path>
#         [--fixture-dir <absolute path>]       # required when kind=task
#         --arm         A|B|C
#         --seed        <int>
#         --model       <fully-qualified-model-id>
#         --log         <absolute path to log.jsonl>
#
# Contract — what this script MUST do:
#   1. Materialise a per-trial workspace at <log_dir>/work/.
#       - kind=template: copy task-dir/setup/ → work/
#       - kind=task:     copy fixture-dir/   → work/
#   2. Build a prompt:
#       - kind=template: use task-dir/task.md verbatim
#       - kind=task:     extract the "prompt" field from task-dir/task.json
#   3. Drive the pinned model (Claude Code CLI) at the arm's checkout SHA.
#      The caller is responsible for the worktree state (we trust HEAD).
#   4. Run the task's acceptance check:
#       - kind=template: bash task-dir/acceptance.sh <work_dir>
#       - kind=task:     parse task.json verification.compilation.mustSucceed;
#                        on true, run `calor` compile and treat exit 0 as pass
#   5. Compute identity_preservation_errors and edit_correctness_errors.
#   6. Emit a single-line JSON summary on stdout as the FINAL non-empty
#      line, exactly the schema dispatch_run expects:
#
#          {"success": bool, "turn_count": int, "total_output_tokens": int,
#           "identity_preservation_errors": int,
#           "edit_correctness_errors": int [, "harness_error": "<tag>"]}
#
#   7. Exit 0 even on substrate-level failure (missing CLI, missing task.json,
#      timeout, dry-run). The harness_error field carries the failure tag so
#      the analyser distinguishes substrate gaps from agent failures.
#      Exit non-zero ONLY for usage errors before the JSON record is emitted.
#
# Environment variables (optional):
#   CALOR_GATE_DRY_RUN=1     — skip the agent invocation; emit a synthetic
#                              record with harness_error="dry_run". Used by
#                              CI for plumbing tests; never in the real gate.
#   CALOR_GATE_AGENT=<cli>   — override the default agent CLI (default:
#                              "claude" — the Claude Code CLI).
#   CALOR_GATE_TIMEOUT_S=<n> — per-trial timeout (default 600s).
#   CALOR_GATE_CALOR=<cli>   — override the calor CLI (default: "calor").
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"

TRIAL_ID=""
KIND=""
TASK_DIR=""
FIXTURE_DIR=""
ARM=""
SEED=""
MODEL=""
LOG=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --trial-id)    TRIAL_ID="$2";    shift 2 ;;
    --kind)        KIND="$2";        shift 2 ;;
    --task-dir)    TASK_DIR="$2";    shift 2 ;;
    --fixture-dir) FIXTURE_DIR="$2"; shift 2 ;;
    --arm)         ARM="$2";         shift 2 ;;
    --seed)        SEED="$2";        shift 2 ;;
    --model)       MODEL="$2";       shift 2 ;;
    --log)         LOG="$2";         shift 2 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

for v in TRIAL_ID KIND TASK_DIR ARM SEED MODEL LOG; do
  if [[ -z "${!v}" ]]; then
    echo "missing required --${v,,}" >&2
    exit 2
  fi
done

if [[ "$KIND" != "task" && "$KIND" != "template" ]]; then
  echo "kind must be 'task' or 'template'; got: $KIND" >&2
  exit 2
fi
if [[ "$KIND" == "task" && -z "$FIXTURE_DIR" ]]; then
  echo "--fixture-dir is required when --kind=task" >&2
  exit 2
fi
if [[ "$ARM" != "A" && "$ARM" != "B" && "$ARM" != "C" ]]; then
  echo "arm must be A, B, or C; got: $ARM" >&2
  exit 2
fi

LOG_DIR="$(dirname "$LOG")"
mkdir -p "$LOG_DIR"
WORK_DIR="${LOG_DIR}/work"

# ----------------------------------------------------------------------------
# JSON emit helper — no python dep, keeps the harness portable.
# ----------------------------------------------------------------------------
emit_record() {
  # $1=success(true|false) $2=turns $3=tokens $4=id_err $5=edit_err
  #   [$6=harness_error_string]
  local success="$1" turns="$2" tokens="$3" id_err="$4" edit_err="$5"
  local err="${6:-}"
  if [[ -n "$err" ]]; then
    printf '{"success": %s, "turn_count": %s, "total_output_tokens": %s, "identity_preservation_errors": %s, "edit_correctness_errors": %s, "harness_error": "%s"}\n' \
      "$success" "$turns" "$tokens" "$id_err" "$edit_err" "$err"
  else
    printf '{"success": %s, "turn_count": %s, "total_output_tokens": %s, "identity_preservation_errors": %s, "edit_correctness_errors": %s}\n' \
      "$success" "$turns" "$tokens" "$id_err" "$edit_err"
  fi
}

# ----------------------------------------------------------------------------
# Validate the task directory has the contract we need for its kind.
# ----------------------------------------------------------------------------
if [[ ! -d "$TASK_DIR" ]]; then
  echo "task_dir does not exist: $TASK_DIR" >&2
  emit_record false 0 0 0 0 "task_dir_not_found"
  exit 0
fi

if [[ "$KIND" == "template" ]]; then
  if [[ ! -f "${TASK_DIR}/task.md" || ! -f "${TASK_DIR}/acceptance.sh" ]]; then
    echo "template missing task.md or acceptance.sh: $TASK_DIR" >&2
    emit_record false 0 0 0 0 "no_task_contract"
    exit 0
  fi
else  # kind=task
  if [[ ! -f "${TASK_DIR}/task.json" ]]; then
    echo "task missing task.json: $TASK_DIR" >&2
    emit_record false 0 0 0 0 "no_task_json"
    exit 0
  fi
  if [[ ! -d "$FIXTURE_DIR" ]]; then
    echo "fixture_dir does not exist: $FIXTURE_DIR" >&2
    emit_record false 0 0 0 0 "fixture_dir_not_found"
    exit 0
  fi
fi

# ----------------------------------------------------------------------------
# Materialise the per-trial workspace.
# ----------------------------------------------------------------------------
rm -rf "$WORK_DIR"
mkdir -p "$WORK_DIR"

if [[ "$KIND" == "template" ]]; then
  if [[ -d "${TASK_DIR}/setup" ]]; then
    cp -R "${TASK_DIR}/setup/." "${WORK_DIR}/"
  fi
else  # kind=task
  # Copy the entire fixture workspace into work/. The fixtures under
  # tests/E2E/agent-tasks/fixtures/ are pre-built calor projects (with
  # *.calr files, calor.toml etc.) ready for the agent to edit.
  cp -R "${FIXTURE_DIR}/." "${WORK_DIR}/"
fi

# ----------------------------------------------------------------------------
# Build the prompt.
# ----------------------------------------------------------------------------
PROMPT_FILE="${LOG_DIR}/prompt.txt"

if [[ "$KIND" == "template" ]]; then
  {
    echo "You are working in ${WORK_DIR}. Apply the task below to the files in"
    echo "that directory. Make minimal edits; do not modify any file not"
    echo "explicitly required by the task. When complete, exit."
    echo ""
    echo "---"
    cat "${TASK_DIR}/task.md"
  } > "$PROMPT_FILE"
else  # kind=task
  # Extract .prompt from task.json. Prefer jq if present; otherwise a
  # python one-liner. Both are widely available; we never assume both.
  if command -v jq >/dev/null 2>&1; then
    PROMPT_TEXT=$(jq -r '.prompt' "${TASK_DIR}/task.json")
  elif command -v python3 >/dev/null 2>&1; then
    PROMPT_TEXT=$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['prompt'])" "${TASK_DIR}/task.json")
  elif command -v python >/dev/null 2>&1; then
    PROMPT_TEXT=$(python -c "import json,sys;print(json.load(open(sys.argv[1]))['prompt'])" "${TASK_DIR}/task.json")
  else
    echo "neither jq nor python available to parse task.json" >&2
    emit_record false 0 0 0 0 "no_json_parser"
    exit 0
  fi
  {
    echo "You are working in ${WORK_DIR}. Apply the task below by editing the"
    echo ".calr files in that directory. Make minimal edits and do not modify"
    echo "any file not explicitly required by the task. When complete, exit."
    echo ""
    echo "---"
    echo "$PROMPT_TEXT"
  } > "$PROMPT_FILE"
fi

# Snapshot the pre-agent file state for the identity-preservation diff.
PRE_SNAPSHOT="${LOG_DIR}/pre-snapshot"
mkdir -p "$PRE_SNAPSHOT"
cp -R "${WORK_DIR}/." "${PRE_SNAPSHOT}/"

# ----------------------------------------------------------------------------
# Dry-run mode: skip the agent. Useful for CI plumbing checks. Records a
# false success with harness_error="dry_run" so analysers can filter it out.
# ----------------------------------------------------------------------------
if [[ "${CALOR_GATE_DRY_RUN:-0}" == "1" ]]; then
  emit_record false 0 0 0 0 "dry_run"
  exit 0
fi

# ----------------------------------------------------------------------------
# Real run: invoke the agent. Default is Claude Code CLI in non-interactive
# --print mode. The exact flag set matches the existing helpers.sh:invoke_agent
# convention in this repo (lib/helpers.sh:1407-1410) for consistency.
# ----------------------------------------------------------------------------
AGENT="${CALOR_GATE_AGENT:-claude}"
TIMEOUT="${CALOR_GATE_TIMEOUT_S:-600}"

if ! command -v "$AGENT" >/dev/null 2>&1; then
  echo "agent CLI not on PATH: $AGENT" >&2
  emit_record false 0 0 0 0 "agent_cli_missing"
  exit 0
fi

if ! command -v timeout >/dev/null 2>&1; then
  echo "GNU coreutils 'timeout' missing — required for bounded agent runs" >&2
  emit_record false 0 0 0 0 "no_timeout_cmd"
  exit 0
fi

RAW_STDOUT="${LOG_DIR}/agent.stdout"
RAW_STDERR="${LOG_DIR}/agent.stderr"
PROMPT_CONTENT="$(cat "$PROMPT_FILE")"

# Per-trial seed: exposed via env so the agent (or its sandbox) can use it
# for sampling determinism if supported. NOT a substitute for the model
# provider's own seeding; documented for operator review.
export CALOR_GATE_SEED="$SEED"

# Run claude from the workspace directory so any file operations are
# scoped there. The agent is invoked with --print (one-shot) and
# --dangerously-skip-permissions (so file edits are not gated by
# interactive prompts). Output format = json so we can parse the
# turn-count / token-usage summary block deterministically.
set +e
( cd "$WORK_DIR" && \
    timeout "${TIMEOUT}s" "$AGENT" \
      --print \
      --output-format json \
      --dangerously-skip-permissions \
      --model "$MODEL" \
      "$PROMPT_CONTENT" \
  ) > "$RAW_STDOUT" 2> "$RAW_STDERR"
AGENT_RC=$?
set -e

if [[ "$AGENT_RC" -eq 124 ]]; then
  echo "agent timed out after ${TIMEOUT}s" >&2
  emit_record false 0 0 0 0 "agent_timeout"
  exit 0
fi
if [[ "$AGENT_RC" -ne 0 ]]; then
  echo "agent exited non-zero ($AGENT_RC)" >&2
  emit_record false 0 0 0 0 "agent_nonzero_exit_${AGENT_RC}"
  exit 0
fi

# ----------------------------------------------------------------------------
# Parse the agent's JSON summary. The Claude Code CLI's --output-format=json
# emits a top-level result object with fields:
#   .num_turns                          — iterations the agent ran
#   .usage.output_tokens                — tokens the agent emitted
#   .usage.input_tokens                 — tokens in the user message
#   .total_cost_usd                     — dollar cost of this trial
# The protocol's criterion 2 measures TURNS and OUTPUT TOKENS (RFC §10.1.2),
# so we record num_turns and usage.output_tokens. total_cost_usd is captured
# in the agent.stdout file for budget tracking but not reported as a metric.
#
# Defensive: if the format drifts, fall back to 0 and let the analyser flag
# it (the analyser already treats zeros as a data-quality signal per
# phase-2-validation-criteria.md §3.1).
# ----------------------------------------------------------------------------
TURNS=0
TOKENS=0
if command -v jq >/dev/null 2>&1; then
  TURNS=$(jq -r '.num_turns // 0'           "$RAW_STDOUT" 2>/dev/null || echo 0)
  TOKENS=$(jq -r '.usage.output_tokens // 0' "$RAW_STDOUT" 2>/dev/null || echo 0)
fi

# ----------------------------------------------------------------------------
# Acceptance check.
# ----------------------------------------------------------------------------
SUCCESS=false

if [[ "$KIND" == "template" ]]; then
  set +e
  bash "${TASK_DIR}/acceptance.sh" "$WORK_DIR" \
    > "${LOG_DIR}/acceptance.stdout" 2> "${LOG_DIR}/acceptance.stderr"
  ACC_RC=$?
  set -e
  [[ "$ACC_RC" -eq 0 ]] && SUCCESS=true
else  # kind=task
  # task.json verification.compilation.mustSucceed — default true. If true,
  # run `calor` (or override) compile from the workspace and treat exit 0
  # as pass. v1 of this adapter does not run Z3 verification even for
  # tasks that request it; that requires the calor verify subcommand and
  # task-specific minProvenContracts thresholds — out of scope for v6 gate.
  MUST_COMPILE=$(
    (command -v jq >/dev/null 2>&1 && jq -r '.verification.compilation.mustSucceed // true' "${TASK_DIR}/task.json") \
    || echo true
  )
  if [[ "$MUST_COMPILE" == "true" ]]; then
    CALOR="${CALOR_GATE_CALOR:-calor}"
    if ! command -v "$CALOR" >/dev/null 2>&1; then
      echo "calor CLI not on PATH: $CALOR" >&2
      emit_record false "$TURNS" "$TOKENS" 0 1 "calor_cli_missing"
      exit 0
    fi
    set +e
    ( cd "$WORK_DIR" && "$CALOR" build ) \
      > "${LOG_DIR}/calor-build.stdout" 2> "${LOG_DIR}/calor-build.stderr"
    BUILD_RC=$?
    set -e
    [[ "$BUILD_RC" -eq 0 ]] && SUCCESS=true
  else
    # Task doesn't require compile; treat agent-completion as success.
    SUCCESS=true
  fi
fi

# ----------------------------------------------------------------------------
# Identity-preservation check (v1): diff the workspace against the
# pre-agent snapshot. Counts files added and files whose content changed
# but were NOT in the documented edit set (which we cannot statically
# infer from task.md/task.json prose, so v1 counts ALL changes outside a
# small whitelist of expected-target patterns). The analyser treats this
# as a regression signal, not a hard fail.
#
# A v2 of this metric should consume an explicit `targets:` field in
# task.json / task.md front-matter; until then this is a conservative
# upper bound (i.e., it over-reports identity churn). See protocol v2
# §3.1 for the formal definition.
# ----------------------------------------------------------------------------
ID_ERRORS=0
while IFS= read -r f; do
  rel="${f#${WORK_DIR}/}"
  pre="${PRE_SNAPSHOT}/${rel}"
  if [[ ! -f "$pre" ]]; then
    ID_ERRORS=$((ID_ERRORS + 1))
  elif ! cmp -s "$f" "$pre"; then
    ID_ERRORS=$((ID_ERRORS + 1))
  fi
done < <(find "$WORK_DIR" -type f 2>/dev/null)
# Also count files removed (in pre but missing in work).
while IFS= read -r f; do
  rel="${f#${PRE_SNAPSHOT}/}"
  [[ ! -f "${WORK_DIR}/${rel}" ]] && ID_ERRORS=$((ID_ERRORS + 1))
done < <(find "$PRE_SNAPSHOT" -type f 2>/dev/null)

# Edit-correctness errors: 0 if acceptance passed, else 1 (binary). The
# acceptance / compile exit code is the canonical signal; a richer count
# would require per-task contract structure beyond v1 of this adapter.
EDIT_ERRORS=0
[[ "$SUCCESS" == "false" ]] && EDIT_ERRORS=1

emit_record "$SUCCESS" "$TURNS" "$TOKENS" "$ID_ERRORS" "$EDIT_ERRORS"
