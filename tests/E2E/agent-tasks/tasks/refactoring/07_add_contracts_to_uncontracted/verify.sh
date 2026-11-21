#!/usr/bin/env bash
# Verify both exact safe input bounds and the original addition postcondition.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-logic.py" ref007 "$1"
