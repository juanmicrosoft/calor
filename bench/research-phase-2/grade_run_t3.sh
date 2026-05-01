#!/usr/bin/env bash
# Usage: grade_run_t2.sh <run-dir> <prompt: T3A|T3B|T3C>
# T2 graders separate Acceptance_* tests from BugDetector_* tests.
# Quality = bug_avoidance × correctness × (1 - regression_rate)

set -uo pipefail

RUN="${1:?run dir required}"
PROMPT="${2:?prompt id required (T3A|T3B|T3C)}"

GRADERS=/c/Users/juanrivera/sources/repos/juanmicrosoft/calor-2/bench/research-phase-2/graders
case "$PROMPT" in
  T3A) GRADER_DIR=$GRADERS/T3.A ;;
  T3B) GRADER_DIR=$GRADERS/T3.B ;;
  T3C) GRADER_DIR=$GRADERS/T3.C ;;
  *) echo "unknown prompt $PROMPT" >&2; exit 1 ;;
esac

cd "$RUN"

if [ -f metrics.json ] && grep -q '"quality"' metrics.json; then
  echo "$RUN: already graded"
  exit 0
fi

# Add WebApplicationFactory if needed
if ! grep -q "Microsoft.AspNetCore.Mvc.Testing" tests/WholesaleOrders.Tests/WholesaleOrders.Tests.csproj; then
  dotnet add tests/WholesaleOrders.Tests/WholesaleOrders.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing >/dev/null 2>&1
fi

# Drop the grader
mkdir -p tests/WholesaleOrders.Tests/Acceptance/$PROMPT
cp $GRADER_DIR/AcceptanceTests.cs tests/WholesaleOrders.Tests/Acceptance/$PROMPT/

# Build
BUILD_OUTPUT=$(dotnet build 2>&1)
if echo "$BUILD_OUTPUT" | grep -q "Build succeeded"; then
  BUILD_OK=true
else
  BUILD_OK=false
fi
WARN=$(echo "$BUILD_OUTPUT" | grep -oE "[0-9]+ Warning" | head -1 | grep -oE "[0-9]+" || echo 0)
ERR=$(echo "$BUILD_OUTPUT" | grep -oE "[0-9]+ Error" | head -1 | grep -oE "[0-9]+" || echo 0)

# Acceptance_* tests
if [ "$BUILD_OK" = true ]; then
  ACC=$(dotnet test --no-build --filter "FullyQualifiedName~Acceptance_" 2>&1)
  ACC_PASS=$(echo "$ACC" | grep -oE "Passed:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
  ACC_FAIL=$(echo "$ACC" | grep -oE "Failed:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
  ACC_TOT=$(echo "$ACC" | grep -oE "Total:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
else
  ACC_PASS=0; ACC_FAIL=0; ACC_TOT=0
fi

# BugDetector_* tests
if [ "$BUILD_OK" = true ]; then
  BUG=$(dotnet test --no-build --filter "FullyQualifiedName~BugDetector_" 2>&1)
  BUG_PASS=$(echo "$BUG" | grep -oE "Passed:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
  BUG_FAIL=$(echo "$BUG" | grep -oE "Failed:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
  BUG_TOT=$(echo "$BUG" | grep -oE "Total:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
else
  BUG_PASS=0; BUG_FAIL=0; BUG_TOT=0
fi

# Existing INV tests (the v3 invariant-preservation set)
if [ "$BUILD_OK" = true ]; then
  REG=$(dotnet test --no-build --filter "FullyQualifiedName!~Acceptance_&FullyQualifiedName!~BugDetector_" 2>&1)
  REG_PASS=$(echo "$REG" | grep -oE "Passed:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
  REG_FAIL=$(echo "$REG" | grep -oE "Failed:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
  REG_TOT=$(echo "$REG" | grep -oE "Total:[ ]+[0-9]+" | head -1 | grep -oE "[0-9]+" || echo 0)
else
  REG_PASS=0; REG_FAIL=0; REG_TOT=42
fi

# Quality calculation per v4 rubric
if [ "$BUILD_OK" = true ] && [ "$ACC_TOT" -gt 0 ]; then
  CORRECTNESS=$(awk "BEGIN { print ($ACC_FAIL == 0) ? 1.0 : 0.0 }")
  if [ "$BUG_TOT" -gt 0 ]; then
    BUG_AVOID=$(awk "BEGIN { print ($BUG_FAIL == 0) ? 1.0 : 0.0 }")
  else
    BUG_AVOID=1.0
  fi
  REG_RATE=$(awk "BEGIN { print ($REG_TOT > 0) ? $REG_FAIL/$REG_TOT : 0 }")
  QUALITY=$(awk "BEGIN { print $BUG_AVOID * $CORRECTNESS * (1 - $REG_RATE) }")
else
  CORRECTNESS=0.0; BUG_AVOID=0.0; REG_RATE=0.0; QUALITY=0.0
fi

ARM=$(echo "$RUN" | grep -oE "(annotated|bare)" | head -1)

cat > metrics.json <<EOF
{
  "run_dir": "$(basename $RUN)",
  "prompt": "$PROMPT",
  "arm": "$ARM",
  "model": "claude-opus-4-7",
  "build_ok": $BUILD_OK,
  "build_warnings": $WARN,
  "build_errors": $ERR,
  "acceptance_passed": $ACC_PASS,
  "acceptance_failed": $ACC_FAIL,
  "acceptance_total": $ACC_TOT,
  "bug_detector_passed": $BUG_PASS,
  "bug_detector_failed": $BUG_FAIL,
  "bug_detector_total": $BUG_TOT,
  "regression_passed": $REG_PASS,
  "regression_failed": $REG_FAIL,
  "regression_total": $REG_TOT,
  "correctness": $CORRECTNESS,
  "bug_avoidance": $BUG_AVOID,
  "regression_rate": $REG_RATE,
  "quality": $QUALITY
}
EOF

echo "$RUN: build=$BUILD_OK acc=$ACC_PASS/$ACC_TOT bug=$BUG_PASS/$BUG_TOT reg_fail=$REG_FAIL/$REG_TOT q=$QUALITY"
