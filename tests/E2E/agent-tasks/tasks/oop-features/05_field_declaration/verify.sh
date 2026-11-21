#!/usr/bin/env bash
# Verify: Actual private Counter._count integer field
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" oop005 "$1"
