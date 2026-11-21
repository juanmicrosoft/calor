#!/usr/bin/env bash
# Verify: Option None return
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" type002 "$1"
