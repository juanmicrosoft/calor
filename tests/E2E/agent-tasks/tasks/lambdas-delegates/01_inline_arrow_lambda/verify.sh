#!/usr/bin/env bash
# Original lam-001 selected-native behavioral admission.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$HERE/../../../lib/verify-logic.py" lam001 "$1"
