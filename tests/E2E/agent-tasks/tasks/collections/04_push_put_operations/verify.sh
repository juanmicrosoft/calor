#!/usr/bin/env bash
set -euo pipefail
exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" coll004 "$1"
