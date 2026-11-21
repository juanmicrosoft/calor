#!/usr/bin/env bash
# Verify: Native IRepository signature
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" oop002 "$1"
