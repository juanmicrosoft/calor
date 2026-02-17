#!/bin/bash
# Sync skill files from canonical source to embedded resource
#
# The canonical source of truth is:
#   tests/Calor.Evaluation/Skills/calor-language-skills.md
#
# This is copied to:
#   src/Calor.Compiler/Resources/Skills/calor.md
#
# Usage: ./scripts/sync-skill-files.sh [--check]
#   --check: Only check if files are in sync (for CI)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

CANONICAL_FILE="$PROJECT_ROOT/tests/Calor.Evaluation/Skills/calor-language-skills.md"
EMBEDDED_FILE="$PROJECT_ROOT/src/Calor.Compiler/Resources/Skills/calor.md"

if [ ! -f "$CANONICAL_FILE" ]; then
    echo "Error: Canonical skill file not found: $CANONICAL_FILE"
    exit 1
fi

if [ "$1" = "--check" ]; then
    # Check mode - just verify files are in sync
    if [ ! -f "$EMBEDDED_FILE" ]; then
        echo "Error: Embedded skill file not found: $EMBEDDED_FILE"
        exit 1
    fi

    if diff -q "$CANONICAL_FILE" "$EMBEDDED_FILE" > /dev/null 2>&1; then
        echo "Skill files are in sync."
        exit 0
    else
        echo "Error: Skill files are out of sync!"
        echo ""
        echo "Canonical source: $CANONICAL_FILE"
        echo "Embedded copy:    $EMBEDDED_FILE"
        echo ""
        echo "To fix, run: ./scripts/sync-skill-files.sh"
        echo ""
        echo "Diff:"
        diff "$CANONICAL_FILE" "$EMBEDDED_FILE" || true
        exit 1
    fi
else
    # Sync mode - copy canonical to embedded
    cp "$CANONICAL_FILE" "$EMBEDDED_FILE"
    echo "Synced skill files:"
    echo "  From: $CANONICAL_FILE"
    echo "  To:   $EMBEDDED_FILE"
fi
