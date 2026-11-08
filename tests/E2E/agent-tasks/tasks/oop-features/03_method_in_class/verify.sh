#!/usr/bin/env bash
# Verify: Public Calculator.Add signed sums
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" oop003 "$1"
