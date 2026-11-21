#!/usr/bin/env bash
# Verify native Option conversion against the actual C# out-parameter idiom.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-logic.py" ref008 "$1"
