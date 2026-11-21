#!/usr/bin/env bash
# Original eff-001 behavior; no greeting prefix is prescribed.
set -euo pipefail
exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" eff001 "$1"
