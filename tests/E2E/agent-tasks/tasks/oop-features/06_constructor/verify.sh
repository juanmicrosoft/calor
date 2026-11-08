#!/usr/bin/env bash
# Verify: Public Point constructor and actual private field state
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" oop006 "$1"
