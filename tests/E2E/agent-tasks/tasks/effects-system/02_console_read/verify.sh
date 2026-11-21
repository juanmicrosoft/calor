#!/usr/bin/env bash
# Original eff-002 behavior with deterministic supplied stdin.
set -euo pipefail
exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" eff002 "$1"
