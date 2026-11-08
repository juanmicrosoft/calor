#!/usr/bin/env bash
# Original eff-006 must append and print the supplied message.
set -euo pipefail
exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" eff006 "$1"
