#!/usr/bin/env bash
# Verify unrestricted multiplication and both original sign implications.
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)/lib/verify-logic.py" ref005 "$1"
