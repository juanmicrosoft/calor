#!/usr/bin/env bash
# Verify: Interface definition
set -euo pipefail

WORKSPACE="$1"
CALR_FILE="$WORKSPACE/Domain.calr"

[[ -f "$CALR_FILE" ]] || { echo "Domain.calr not found"; exit 1; }

# Check for IRepository interface
grep -q "IRepository" "$CALR_FILE" || { echo "IRepository interface not found"; exit 1; }

# Check for interface definition §IFACE{
grep -q "§IFACE{" "$CALR_FILE" || { echo "Interface definition (§IFACE) not found"; exit 1; }

# Check for interface closing tag
grep -q "§/IFACE{" "$CALR_FILE" || { echo "Interface closing tag not found"; exit 1; }

echo "Verification passed: IRepository interface definition found"
exit 0
