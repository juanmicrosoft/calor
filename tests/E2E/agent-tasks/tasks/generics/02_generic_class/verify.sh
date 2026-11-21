#!/usr/bin/env bash
# Verify: Generic class
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" gen002 "$1"
