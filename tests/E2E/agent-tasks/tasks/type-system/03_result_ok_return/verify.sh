#!/usr/bin/env bash
# Verify: Result Ok return
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" type003 "$1"
