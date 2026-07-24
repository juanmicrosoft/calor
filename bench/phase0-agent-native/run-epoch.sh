#!/usr/bin/env bash
# ============================================================================
# Phase 0 epoch orchestrator: all pairs x both arms x N runs, with pins.
#
# Usage:
#   ./run-epoch.sh --epoch dry-run-001 --runs 3 [--null-agent] [--pairs "W3-001 N1-002"]
#
# An epoch is one gate's complete paired run (gates doc §0.1). pins.json
# records model IDs, agent-tool version, compiler commit, and suite state.
# Live (non-null) epochs consume agent API spend — get explicit authorization
# before running one.
# ============================================================================
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

EPOCH=""; RUNS=1; NULL_FLAG=""; PAIR_FILTER=""; EDIT_MECHANISM="raw"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --epoch) EPOCH="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --null-agent) NULL_FLAG="--null-agent"; shift ;;
        --pairs) PAIR_FILTER="$2"; shift 2 ;;
        --edit-mechanism) EDIT_MECHANISM="$2"; shift 2 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done
[[ -n "$EPOCH" ]] || { echo "--epoch <id> required" >&2; exit 2; }
case "$EDIT_MECHANISM" in raw|mcp-file|mcp-node) ;; *) echo "--edit-mechanism must be raw|mcp-file|mcp-node" >&2; exit 2 ;; esac

OUT="$SCRIPT_DIR/epochs/$EPOCH"
mkdir -p "$OUT"

# Pins (gates doc §1)
jq -n \
    --arg compiler_commit "$(git -C "$REPO_ROOT" rev-parse HEAD)" \
    --arg suite_dirty "$(git -C "$REPO_ROOT" status --porcelain "$SCRIPT_DIR" | wc -l | tr -d ' ')" \
    --arg agent_version "$(claude --version 2>/dev/null || echo unavailable)" \
    --arg model "${CLAUDE_MODEL:-default}" \
    --arg date "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    --arg null "${NULL_FLAG:-live}" \
    --arg mech "$EDIT_MECHANISM" \
    '{compilerCommit:$compiler_commit, suiteDirtyFiles:($suite_dirty|tonumber),
      agentVersion:$agent_version, modelPin:$model, startedAt:$date, mode:$null,
      editMechanism:$mech, telemetrySchema:"loop-telemetry/2"}' \
    > "$OUT/pins.json"

for pair_json in "$SCRIPT_DIR"/pairs/*/pair.json; do
    pair_dir="$(dirname "$pair_json")"
    pair_id="$(jq -r .id "$pair_json")"
    if [[ -n "$PAIR_FILTER" ]] && ! grep -q "$(basename "$pair_dir" | cut -d- -f1-2)" <<< "$PAIR_FILTER"; then
        continue
    fi
    for arm in csharp calor; do
        echo "=== $pair_id / $arm ==="
        "$SCRIPT_DIR/run-pair.sh" --pair "$pair_dir" --arm "$arm" --runs "$RUNS" \
            --edit-mechanism "$EDIT_MECHANISM" \
            $NULL_FLAG --out "$OUT" | jq -c '{pair,arm,run,taskSuccess,escapedBugs,iterationsToGreen,censored}'
    done
done

# Roll-up
# (division fully parenthesized: jq 1.7 rejects `(A) / B` as an object value)
find "$OUT" -name result.json -exec cat {} + | jq -s \
    'group_by(.arm) | map({arm: .[0].arm,
        runs: length,
        successRate: ((map(select(.taskSuccess)) | length) / length),
        meanEscaped: (map(.escapedBugs) | add / length),
        censoredFraction: ((map(select(.censored)) | length) / length)})' \
    > "$OUT/rollup.json"
echo "--- rollup ---"; cat "$OUT/rollup.json"
