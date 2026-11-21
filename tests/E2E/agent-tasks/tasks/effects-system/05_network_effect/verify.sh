#!/usr/bin/env bash
# Original eff-005 is string simulation, never an actual HTTP request.
set -euo pipefail
exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" eff005 "$1"
