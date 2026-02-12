#!/usr/bin/env bash
# Verify: Event subscription
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for EventPublisher class
grep -q "EventPublisher" "$CALR_FILE" || { echo "EventPublisher class not found"; exit 1; }

# Check for event declaration §EVT{
grep -q "§EVT{" "$CALR_FILE" || { echo "Event declaration (§EVT) not found"; exit 1; }

# Check for event subscription §SUB
grep -q "§SUB" "$CALR_FILE" || { echo "Event subscription (§SUB) not found"; exit 1; }

echo "Verification passed: EventPublisher class with event subscription found"
exit 0
