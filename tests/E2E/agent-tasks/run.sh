#!/usr/bin/env bash
# ============================================================================
# Phase 2 gate harness adapter (RFC §10, protocol §10.1.a).
# ============================================================================
#
# Invoked by scripts/run_phase_2_gate.py:dispatch_run with:
#
#     bash tests/E2E/agent-tasks/run.sh \
#         --task   <fixture-or-template-dir-basename> \
#         --arm    <A|B|C> \
#         --seed   <int> \
#         --model  <fully-qualified-model-id> \
#         --log    <absolute-path-for-raw-log.jsonl>
#
# Contract — what this script MUST do:
#   1. Locate <task> under either:
#        - tests/E2E/agent-tasks/templates/path-2-gate/<task>/ (preferred:
#          has task.md + setup/ + expected/ + acceptance.sh)
#        - tests/E2E/agent-tasks/fixtures/<task>/ (workspace-only; no task
#          contract — see "Substrate gap" below)
#   2. Materialise a per-trial workspace at <log_dir>/work/.
#   3. Drive the pinned model (Claude Code CLI) at HEAD of the arm-pinned
#      ref (the arm checkout is the caller's responsibility — this script
#      assumes the working tree already reflects the desired arm SHA).
#   4. Run the task's acceptance.sh against the agent output.
#   5. Compute identity_preservation_errors and edit_correctness_errors.
#   6. Emit a single-line JSON summary on stdout as the FINAL non-empty
#      line, exactly the schema expected by dispatch_run:
#
#          {"success": bool, "turn_count": int, "total_output_tokens": int,
#           "identity_preservation_errors": int,
#           "edit_correctness_errors": int}
#
# Substrate gap (recorded for traceability):
#   The 24 fixtures referenced in protocol §1.1 (advanced-calor-project,
#   async-project, etc.) currently have no task.md / setup/ / acceptance.sh.
#   For those, this adapter emits a structured failure record with
#   harness_error="no_task_contract" so the gate driver does not silently
#   record fake successes. Fixing this requires either authoring 24 new
#   task contracts or refactoring run_phase_2_gate.py to iterate the
#   existing tests/E2E/agent-tasks/tasks/<cat>/<task>/task.json schema and
#   pair each task with its declared `fixture` workspace.
#
# Environment variables (optional):
#   CALOR_GATE_DRY_RUN=1   — skip the agent invocation; treat as a synthetic
#                            "harness wiring smoke test" and emit a
#                            success=false record. Used by CI; do NOT use
#                            in the real gate.
#   CALOR_GATE_AGENT       — override the default agent CLI ("claude").
#   CALOR_GATE_TIMEOUT_S   — per-trial timeout (default 600s).
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
FIXTURES_DIR="${SCRIPT_DIR}/fixtures"
TEMPLATES_DIR="${SCRIPT_DIR}/templates/path-2-gate"

TASK=""
ARM=""
SEED=""
MODEL=""
LOG=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --task)  TASK="$2";  shift 2 ;;
    --arm)   ARM="$2";   shift 2 ;;
    --seed)  SEED="$2";  shift 2 ;;
    --model) MODEL="$2"; shift 2 ;;
    --log)   LOG="$2";   shift 2 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

for v in TASK ARM SEED MODEL LOG; do
  if [[ -z "${!v}" ]]; then
    echo "missing required --${v,,}" >&2
    exit 2
  fi
done

if [[ "$ARM" != "A" && "$ARM" != "B" && "$ARM" != "C" ]]; then
  echo "arm must be A, B, or C; got: $ARM" >&2
  exit 2
fi

LOG_DIR="$(dirname "$LOG")"
mkdir -p "$LOG_DIR"
WORK_DIR="${LOG_DIR}/work"

# ----------------------------------------------------------------------------
# JSON emit helpers (no python dependency to keep the harness portable).
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
# Resolve the task directory and verify it has a runnable contract.
# ----------------------------------------------------------------------------
TASK_DIR=""
if [[ -d "${TEMPLATES_DIR}/${TASK}" ]]; then
  TASK_DIR="${TEMPLATES_DIR}/${TASK}"
elif [[ -d "${FIXTURES_DIR}/${TASK}" ]]; then
  TASK_DIR="${FIXTURES_DIR}/${TASK}"
else
  echo "fatal: task dir not found for ${TASK}" >&2
  emit_record false 0 0 0 0 "task_dir_not_found"
  exit 0  # exit 0 so dispatch_run records the JSON instead of treating
          # this as a harness crash; the harness_error field makes the
          # failure auditable.
fi

if [[ ! -f "${TASK_DIR}/task.md" || ! -f "${TASK_DIR}/acceptance.sh" ]]; then
  # Substrate gap — see header comment.
  echo "no task contract under ${TASK_DIR}; emitting structured failure" >&2
  emit_record false 0 0 0 0 "no_task_contract"
  exit 0
fi

# ----------------------------------------------------------------------------
# Materialise the per-trial workspace.
# ----------------------------------------------------------------------------
rm -rf "$WORK_DIR"
mkdir -p "$WORK_DIR"
if [[ -d "${TASK_DIR}/setup" ]]; then
  cp -R "${TASK_DIR}/setup/." "${WORK_DIR}/"
fi

# ----------------------------------------------------------------------------
# Dry-run mode: skip the agent. Useful for CI plumbing checks. Records a
# false success with harness_error="dry_run" so analysers can filter.
# ----------------------------------------------------------------------------
if [[ "${CALOR_GATE_DRY_RUN:-0}" == "1" ]]; then
  emit_record false 0 0 0 0 "dry_run"
  exit 0
fi

# ----------------------------------------------------------------------------
# Real run: invoke the agent. The default agent is Claude Code CLI; the
# concrete invocation here is a placeholder pending operator selection of
# the exact CLI flags for non-interactive batch mode. See operator notes
# in protocol §10.1.a item 5 for the model-pinning contract.
#
# The agent is invoked in --print (one-shot) mode with the task.md content
# as the prompt and the workspace as cwd. We capture stdout (the agent's
# final answer) and stderr (verbose trace), and parse turn-count / token
# usage from the trailing summary block emitted by --json output mode.
# ----------------------------------------------------------------------------
AGENT="${CALOR_GATE_AGENT:-claude}"
TIMEOUT="${CALOR_GATE_TIMEOUT_S:-600}"

if ! command -v "$AGENT" >/dev/null 2>&1; then
  echo "agent CLI not on PATH: $AGENT" >&2
  emit_record false 0 0 0 0 "agent_cli_missing"
  exit 0
fi

PROMPT_FILE="${LOG_DIR}/prompt.txt"
RAW_STDOUT="${LOG_DIR}/agent.stdout"
RAW_STDERR="${LOG_DIR}/agent.stderr"
{
  echo "You are working in ${WORK_DIR}. Apply the task below to the files in"
  echo "that directory. Make minimal edits; do not modify any file not"
  echo "explicitly required by the task. When complete, exit."
  echo ""
  echo "---"
  cat "${TASK_DIR}/task.md"
} > "$PROMPT_FILE"

# Per-trial seed: passed via env so the agent (or its sandbox) can use it
# for sampling determinism if supported. NOT a substitute for the model
# provider's own seeding; documented for operator review.
export CALOR_GATE_SEED="$SEED"

set +e
timeout "${TIMEOUT}s" "$AGENT" \
  --model "$MODEL" \
  --print \
  --output-format json \
  --working-directory "$WORK_DIR" \
  < "$PROMPT_FILE" \
  > "$RAW_STDOUT" 2> "$RAW_STDERR"
AGENT_RC=$?
set -e

if [[ "$AGENT_RC" -ne 0 ]]; then
  echo "agent exited non-zero ($AGENT_RC)" >&2
  emit_record false 0 0 0 0 "agent_nonzero_exit_${AGENT_RC}"
  exit 0
fi

# ----------------------------------------------------------------------------
# Parse the agent's JSON summary. We expect a trailing object with at least
# turn_count and total_tokens. Be defensive — if the format drifts, fall
# back to 0s and let the analyser flag it.
# ----------------------------------------------------------------------------
TURNS=0
TOKENS=0
if command -v jq >/dev/null 2>&1; then
  TURNS=$(jq -r '.num_turns // 0'         "$RAW_STDOUT" 2>/dev/null || echo 0)
  TOKENS=$(jq -r '.total_tokens // 0'     "$RAW_STDOUT" 2>/dev/null || echo 0)
fi

# ----------------------------------------------------------------------------
# Run the task's acceptance check.
# ----------------------------------------------------------------------------
set +e
bash "${TASK_DIR}/acceptance.sh" "$WORK_DIR" > "${LOG_DIR}/acceptance.stdout" 2> "${LOG_DIR}/acceptance.stderr"
ACC_RC=$?
set -e

SUCCESS=false
[[ "$ACC_RC" -eq 0 ]] && SUCCESS=true

# ----------------------------------------------------------------------------
# Identity-preservation check: diff the workspace against setup/. Count
# files modified that are NOT explicitly part of the task target (cheap
# proxy: files outside the directly-edited file set). Implementation here
# is conservative — counts every non-setup file that exists in the
# workspace but not in setup/, plus every setup file modified outside the
# task's documented target list (which we cannot statically extract from
# task.md, so for v1 we count "any unexpected new file"). The analyser
# treats this as a regression signal, not a hard fail.
# ----------------------------------------------------------------------------
ID_ERRORS=0
if [[ -d "${TASK_DIR}/setup" ]]; then
  # Count files present in WORK_DIR but absent in setup/ (unexpected new files).
  while IFS= read -r f; do
    rel="${f#${WORK_DIR}/}"
    [[ -f "${TASK_DIR}/setup/${rel}" ]] || ID_ERRORS=$((ID_ERRORS + 1))
  done < <(find "$WORK_DIR" -type f 2>/dev/null)
fi

# Edit-correctness errors: 0 if acceptance passed, else 1 (binary). The
# acceptance script's exit code is the canonical signal; a richer count
# would require a per-task contract beyond v1 of this adapter.
EDIT_ERRORS=0
[[ "$SUCCESS" == "false" ]] && EDIT_ERRORS=1

emit_record "$SUCCESS" "$TURNS" "$TOKENS" "$ID_ERRORS" "$EDIT_ERRORS"
