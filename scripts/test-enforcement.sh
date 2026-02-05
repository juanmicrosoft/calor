#!/bin/bash
set -e

echo "Running Calor Enforcement Tests..."
echo "=================================="

# Run enforcement-specific tests
echo ""
echo "1. Running enforcement tests..."
dotnet test tests/Calor.Enforcement.Tests -c Release --logger "console;verbosity=normal"

# Run existing compiler tests to ensure no regressions
echo ""
echo "2. Running compiler tests..."
dotnet test tests/Calor.Compiler.Tests -c Release --logger "console;verbosity=normal"

# Run evaluation tests
echo ""
echo "3. Running evaluation tests..."
dotnet test tests/Calor.Evaluation -c Release --logger "console;verbosity=normal"

echo ""
echo "=================================="
echo "All tests passed!"
