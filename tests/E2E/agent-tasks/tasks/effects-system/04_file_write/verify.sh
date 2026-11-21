#!/usr/bin/env bash
# Original eff-004 behavior using exclusively owned temporary files.
set -euo pipefail
exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" eff004 "$1"
