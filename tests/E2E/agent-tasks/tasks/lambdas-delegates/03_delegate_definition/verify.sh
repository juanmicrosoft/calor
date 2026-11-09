#!/usr/bin/env bash
# Original lam-003 selected-native CLR delegate admission.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$HERE/../../../lib/verify-logic.py" lam003 "$1"
