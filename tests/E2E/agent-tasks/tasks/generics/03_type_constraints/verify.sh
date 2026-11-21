#!/usr/bin/env bash
# Verify: Type constraints
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" gen003 "$1"
