#!/usr/bin/env bash
# Verify: Generic function
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" gen001 "$1"
