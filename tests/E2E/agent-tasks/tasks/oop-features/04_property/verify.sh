#!/usr/bin/env bash
# Verify: Native Product.Price auto-property and CLR state
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" oop004 "$1"
