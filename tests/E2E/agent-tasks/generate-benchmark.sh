#!/usr/bin/env bash

set -euo pipefail

cat >&2 <<'EOF'
This generator is archived and cannot refresh agent-benchmark-results.json.

The published file is a historical 2026-02-16 snapshot. Its exact model and
runtime configuration are unknown, and the old generator accepted missing
runner output before substituting fallback counts. A new benchmark needs a
separately reviewed, fail-closed collector and a new provenance record.
EOF

exit 1
