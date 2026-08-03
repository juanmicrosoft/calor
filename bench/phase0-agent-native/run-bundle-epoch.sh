#!/usr/bin/env bash
# ============================================================================
# WS-W4 Slice-C bundle dry-run EPOCH driver
# ============================================================================
#
# Orchestrates run-bundle.sh over every bundle under a bundles root, in BOTH
# arms (csharp, calor), N runs each, then aggregates with analyze-bundle-epoch.py.
# Mirrors the block structure of epochs/e1a-attribution/run-driver.sh but does
# NOT git-commit: bundles + workspaces are ephemeral (task constraint) and the
# real (spend) dry-run is separately authorized.
#
# The default here is --null-agent-noop in BOTH arms — a ZERO-SPEND smoke of the
# whole epoch plumbing that must adjudicate ESCAPED everywhere (the defect is
# left in place). Pass --real to launch the actual spend dry-run (spawns claude).
#
# Usage:
#   ./run-bundle-epoch.sh --bundles <root> --out <epoch_dir> [--runs N]
#                         [--real | --null-caught | --null-noop(default)]
#                         [--corpus <dir>]
# ============================================================================
set -uo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

BUNDLES_ROOT=""; OUT_DIR=""; RUNS=1; MODE="null-noop"
CORPUS_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)/bench/corpus"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --bundles) BUNDLES_ROOT="$2"; shift 2 ;;
        --out) OUT_DIR="$2"; shift 2 ;;
        --runs) RUNS="$2"; shift 2 ;;
        --real) MODE="real"; shift ;;
        --null-caught) MODE="null-caught"; shift ;;
        --null-noop) MODE="null-noop"; shift ;;
        --corpus) CORPUS_ROOT="$2"; shift 2 ;;
        *) echo "Unknown arg: $1" >&2; exit 2 ;;
    esac
done
[[ -n "$BUNDLES_ROOT" && -n "$OUT_DIR" ]] || { echo "Usage: --bundles <root> --out <epoch_dir> [--runs N] [--real|--null-caught|--null-noop]" >&2; exit 2; }
mkdir -p "$OUT_DIR"; OUT_DIR="$(cd "$OUT_DIR" && pwd)"
LOG="$OUT_DIR/driver.log"
log() { printf '%s %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" | tee -a "$LOG"; }

MODE_FLAG=""
case "$MODE" in
    real)        MODE_FLAG="" ;;                    # spawns claude (spend)
    null-caught) MODE_FLAG="--null-agent" ;;        # apply fix -> expect caught
    null-noop)   MODE_FLAG="--null-agent-noop" ;;   # do nothing -> expect escaped
esac

# Discover bundles (dirs containing provenance.json). -L so an operator can
# assemble a bundle set out of symlinks to individual bundle dirs.
mapfile -t BUNDLES < <(find -L "$BUNDLES_ROOT" -type f -name provenance.json -exec dirname {} \; | sort)
[[ ${#BUNDLES[@]} -gt 0 ]] || { echo "No bundles under $BUNDLES_ROOT" >&2; exit 3; }

log "bundle epoch starting (pid $$) mode=$MODE runs=$RUNS bundles=${#BUNDLES[@]}"
for bundle in "${BUNDLES[@]}"; do
    bid="$(basename "$bundle")"
    for arm in csharp calor; do
        log "BLOCK START bundle=$bid arm=$arm runs=$RUNS mode=$MODE"
        if "$SCRIPT_DIR/run-bundle.sh" --bundle "$bundle" --arm "$arm" --runs "$RUNS" \
                --out "$OUT_DIR" --corpus "$CORPUS_ROOT" $MODE_FLAG >>"$LOG" 2>&1; then
            log "BLOCK DONE  bundle=$bid arm=$arm"
        else
            log "ERROR run-bundle.sh nonzero: bundle=$bid arm=$arm (continuing)"
        fi
    done
done

log "bundle epoch complete; aggregating"
python3 "$SCRIPT_DIR/analyze-bundle-epoch.py" "$OUT_DIR" | tee -a "$LOG"
python3 "$SCRIPT_DIR/analyze-bundle-epoch.py" "$OUT_DIR" --json > "$OUT_DIR/epoch-summary.json"
log "summary -> $OUT_DIR/epoch-summary.json"
