#!/usr/bin/env bash
# Usage: grade_run.sh <run-dir> <prompt: T1A|T1B|T1C>
# Builds the run, applies the appropriate grader, runs tests, writes metrics.json.

set -uo pipefail

RUN="${1:?run dir required}"
PROMPT="${2:?prompt id required (T1A|T1B|T1C)}"

if [ ! -d "$RUN" ]; then
  echo "no such dir: $RUN" >&2; exit 1
fi

GRADERS=/c/Users/juanrivera/sources/repos/juanmicrosoft/calor-2/bench/research-phase-0/graders

cd "$RUN"

# Skip if already graded
if [ -f metrics.json ] && grep -q '"acceptance_passed"' metrics.json; then
  echo "$RUN: already graded"
  exit 0
fi

# Add WebApplicationFactory package if not already
if ! grep -q "Microsoft.AspNetCore.Mvc.Testing" tests/WholesaleOrders.Tests/WholesaleOrders.Tests.csproj; then
  dotnet add tests/WholesaleOrders.Tests/WholesaleOrders.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing >/dev/null 2>&1
fi

# Drop in the grader
mkdir -p tests/WholesaleOrders.Tests/Acceptance/${PROMPT/T1/T1}
case "$PROMPT" in
  T1A) cp $GRADERS/T1.A/AcceptanceTests.cs tests/WholesaleOrders.Tests/Acceptance/T1A/ ;;
  T1B) cp $GRADERS/T1.B/AcceptanceTests.cs tests/WholesaleOrders.Tests/Acceptance/T1B/ ;;
  T1C) cp $GRADERS/T1.C/AcceptanceTests.cs tests/WholesaleOrders.Tests/Acceptance/T1C/ ;;
esac

# Build
BUILD_OUTPUT=$(dotnet build 2>&1 || true)
if echo "$BUILD_OUTPUT" | grep -q "Build succeeded"; then
  BUILD_OK=true
  WARNINGS=$(echo "$BUILD_OUTPUT" | grep -oE "[0-9]+ Warning" | head -1 | grep -oE "[0-9]+" || echo "0")
  ERRORS=0
else
  BUILD_OK=false
  WARNINGS=$(echo "$BUILD_OUTPUT" | grep -oE "[0-9]+ Warning" | head -1 | grep -oE "[0-9]+" || echo "0")
  ERRORS=$(echo "$BUILD_OUTPUT" | grep -oE "[0-9]+ Error" | head -1 | grep -oE "[0-9]+" || echo "1")
fi

# Run acceptance tests
if [ "$BUILD_OK" = true ]; then
  ACC=$(dotnet test --no-build --filter "FullyQualifiedName~${PROMPT}" 2>&1 | grep -oE "Passed:[ ]+[0-9]+|Failed:[ ]+[0-9]+|Total:[ ]+[0-9]+" | head -3)
  ACC_PASSED=$(echo "$ACC" | grep "Passed:" | grep -oE "[0-9]+" || echo "0")
  ACC_FAILED=$(echo "$ACC" | grep "Failed:" | grep -oE "[0-9]+" || echo "0")
  ACC_TOTAL=$(echo "$ACC" | grep "Total:" | grep -oE "[0-9]+" || echo "0")
else
  ACC_PASSED=0; ACC_FAILED=0; ACC_TOTAL=0
fi

# Run existing tests (regression)
if [ "$BUILD_OK" = true ]; then
  REG=$(dotnet test --no-build --filter "FullyQualifiedName!~${PROMPT}" 2>&1 | grep -oE "Passed:[ ]+[0-9]+|Failed:[ ]+[0-9]+|Total:[ ]+[0-9]+" | head -3)
  REG_PASSED=$(echo "$REG" | grep "Passed:" | grep -oE "[0-9]+" || echo "0")
  REG_FAILED=$(echo "$REG" | grep "Failed:" | grep -oE "[0-9]+" || echo "0")
  REG_TOTAL=$(echo "$REG" | grep "Total:" | grep -oE "[0-9]+" || echo "0")
else
  REG_PASSED=0; REG_FAILED=0; REG_TOTAL=42
fi

# Quality calculation
if [ "$BUILD_OK" = true ] && [ "$ACC_TOTAL" -gt 0 ]; then
  CORRECTNESS=$(awk "BEGIN { print ($ACC_FAILED == 0) ? 1.0 : 0.0 }")
  REG_RATE=$(awk "BEGIN { print ($REG_TOTAL > 0) ? $REG_FAILED/$REG_TOTAL : 0 }")
  INVARIANT=1.0  # Approximated; encoded in regression set
  QUALITY=$(awk "BEGIN { print $CORRECTNESS * $INVARIANT * (1 - $REG_RATE) }")
else
  CORRECTNESS=0.0; REG_RATE=0.0; INVARIANT=0.0; QUALITY=0.0
fi

# Save final-diff
ARM=$(echo "$RUN" | grep -oE "(annotated|bare)" | head -1)
case "$ARM" in
  annotated) BASELINE=/c/Users/juanrivera/sources/repos/juanmicrosoft/calor-2/bench/research-phase-0/csharp-baseline ;;
  bare)      BASELINE=/c/Users/juanrivera/sources/repos/juanmicrosoft/calor-2/bench/research-phase-0/csharp-bare ;;
  *)         BASELINE="" ;;
esac
if [ -n "$BASELINE" ]; then
  diff -ur "$BASELINE/src" ./src 2>&1 | grep -v -E "(^Only in.*obj|^Only in.*bin|^diff -ur.*obj|^diff -ur.*bin)" > final-diff.patch || true
fi

cat > metrics.json <<EOF
{
  "run_dir": "$(basename $RUN)",
  "prompt": "$PROMPT",
  "arm": "$ARM",
  "model": "claude-opus-4-7",
  "build_ok": $BUILD_OK,
  "build_warnings": $WARNINGS,
  "build_errors": $ERRORS,
  "acceptance_passed": $ACC_PASSED,
  "acceptance_failed": $ACC_FAILED,
  "acceptance_total": $ACC_TOTAL,
  "regression_passed": $REG_PASSED,
  "regression_failed": $REG_FAILED,
  "regression_total": $REG_TOTAL,
  "correctness": $CORRECTNESS,
  "invariant_preservation": $INVARIANT,
  "regression_rate": $REG_RATE,
  "quality": $QUALITY
}
EOF

echo "$RUN: build=$BUILD_OK, acc=$ACC_PASSED/$ACC_TOTAL, reg=$REG_FAILED fail / $REG_TOTAL, quality=$QUALITY"
