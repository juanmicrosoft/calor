#!/usr/bin/env bash
# ============================================================================
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# E1a attribution experiment driver (machine-zone.md §1/§9/§12.4).
#
# 2 pairs x 3 arms x 30 sequential runs = 180 runs. Blocks of 30 (one
# pair x arm) are checkpoint-committed and pushed as they complete.
# Archived in the epoch dir for provenance; runs from the bench dir.
# ============================================================================
set -uo pipefail

BENCH="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
REPO="$(cd "$BENCH/../.." && pwd)"
OUT="$BENCH/epochs/e1a-attribution"
LOG="$OUT/driver.log"

log() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" | tee -a "$LOG"; }

checkpoint() {
    local msg="$1"
    ( cd "$REPO" \
      && git add bench/phase0-agent-native/epochs/e1a-attribution \
      && git -c commit.gpgsign=false commit -m "$msg

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01MP98bPvHVGbADabAjtSYpg" \
      && git push origin e1a-attribution ) >>"$LOG" 2>&1 \
      && log "checkpoint pushed: $msg" \
      || log "WARN: checkpoint commit/push failed for: $msg"
}

run_block() {
    local pair="$1" arm="$2" exemplar="$3" label="$4"
    log "BLOCK START pair=$pair arm=$label runs=30"
    local args=(--pair "$BENCH/pairs/$pair" --arm "$arm" --runs 30 --out "$OUT")
    [[ -n "$exemplar" ]] && args+=(--exemplar "$exemplar")
    if "$BENCH/run-pair.sh" "${args[@]}" >>"$LOG" 2>&1; then
        log "BLOCK DONE pair=$pair arm=$label"
    else
        log "ERROR: run-pair.sh exited nonzero for pair=$pair arm=$label (partial block committed as-is)"
    fi
    checkpoint "data: E1a checkpoint — $pair / $label (30-run block)"
}

log "E1a driver starting (pid $$)"
for pair in N1-003-csv-row W3-003-kv-journal; do
    run_block "$pair" csharp ""                    csharp
    run_block "$pair" calor  ""                    calor
    run_block "$pair" calor  "$SCRIPT_DIR/exemplar-as-run.md"  calor+exemplar
done
log "E1a driver complete"
