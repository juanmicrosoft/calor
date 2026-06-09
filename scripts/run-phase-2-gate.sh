#!/usr/bin/env bash
# Thin wrapper for scripts/run_phase_2_gate.py.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "${HERE}/run_phase_2_gate.py" "$@"
