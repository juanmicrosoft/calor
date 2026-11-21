#!/usr/bin/env bash
# Verify: Empty public Person class
set -euo pipefail

exec python3 "$(dirname "$0")/../../../lib/verify-logic.py" oop001 "$1"
