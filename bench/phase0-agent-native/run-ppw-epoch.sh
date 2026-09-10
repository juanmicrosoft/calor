#!/usr/bin/env bash
# Redesigned one-compiler policy contrast. No default epoch, N, model, or budget.
# See README-ppw-instrument.md. Historical A-1.12 code remains explicitly separate.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$SCRIPT_DIR/ppw-instrument.py" run "$@"
