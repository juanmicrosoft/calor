#!/usr/bin/env bash
set -euo pipefail
exec python3 "$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../lib" && pwd)/verify-logic.py" Clamp "${1:?workspace required}"
