#!/usr/bin/env bash
# Original lam-004 selected-native CLR event admission.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "$HERE/../../../lib/verify-logic.py" lam004 "$1"
