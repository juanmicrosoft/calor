#!/usr/bin/env bash
# Verify original file-read/console-write effects and supplied-content coupling.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-logic.py" ref002 "$1"
