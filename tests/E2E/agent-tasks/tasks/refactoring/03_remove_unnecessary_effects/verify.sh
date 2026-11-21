#!/usr/bin/env bash
# Verify the original pure addition task with the selected compiler.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-logic.py" ref003 "$1"
