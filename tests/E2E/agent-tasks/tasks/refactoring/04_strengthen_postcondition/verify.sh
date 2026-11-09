#!/usr/bin/env bash
# Verify all original absolute-value postcondition requirements.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-logic.py" ref004 "$1"
