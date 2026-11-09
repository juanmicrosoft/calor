#!/usr/bin/env bash
# Verify the original zero-inclusive Abs contract repair.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-logic.py" ref006 "$1"
