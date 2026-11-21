#!/usr/bin/env bash
# Verify: Result Err return
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" type004 "$1"
