#!/usr/bin/env bash
# Verify: Option Some return
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" type001 "$1"
