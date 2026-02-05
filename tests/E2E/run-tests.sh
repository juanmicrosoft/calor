#!/usr/bin/env bash
set -euo pipefail

# Calor E2E Test Runner
# Runs all scenarios in tests/E2E/scenarios/

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
COMPILER="$REPO_ROOT/src/Calor.Compiler/bin/Debug/net8.0/calor"
SCENARIOS_DIR="$SCRIPT_DIR/scenarios"

# Colors
if [[ -t 1 ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    NC='\033[0m'
else
    RED='' GREEN='' YELLOW='' BLUE='' NC=''
fi

PASSED=0
FAILED=0
SKIPPED=0

info() { echo -e "${BLUE}[INFO]${NC} $1"; }
pass() { echo -e "${GREEN}[PASS]${NC} $1"; ((PASSED++)) || true; }
fail() { echo -e "${RED}[FAIL]${NC} $1"; ((FAILED++)) || true; }
skip() { echo -e "${YELLOW}[SKIP]${NC} $1"; ((SKIPPED++)) || true; }

build_compiler() {
    info "Building Calor compiler..."
    dotnet build "$REPO_ROOT/src/Calor.Compiler/Calor.Compiler.csproj" -c Debug --nologo -v q || {
        echo "Failed to build compiler"
        exit 1
    }
    info "Compiler built successfully"
    echo ""
}

run_scenario() {
    local scenario_dir="$1"
    local scenario_name
    scenario_name=$(basename "$scenario_dir")

    local input_file="$scenario_dir/input.calr"
    local verify_script="$scenario_dir/verify.sh"
    local output_file="$scenario_dir/output.g.cs"

    # Check for required files
    if [[ ! -f "$input_file" ]]; then
        skip "$scenario_name - no input.calr"
        return 0
    fi

    echo -e "${BLUE}Running: $scenario_name${NC}"

    # Compile Calor to C#
    if ! "$COMPILER" --input "$input_file" --output "$output_file"; then
        fail "$scenario_name - compilation failed"
        return 0
    fi

    # Run verification script if present
    if [[ -f "$verify_script" ]]; then
        chmod +x "$verify_script"
        if ! "$verify_script" "$scenario_dir"; then
            fail "$scenario_name - verification failed"
            return 0
        fi
    fi

    pass "$scenario_name"
    return 0
}

cleanup() {
    info "Cleaning up generated files..."
    find "$SCENARIOS_DIR" -name "*.g.cs" -delete 2>/dev/null || true
    find "$SCENARIOS_DIR" -name "bin" -type d -exec rm -rf {} + 2>/dev/null || true
    find "$SCENARIOS_DIR" -name "obj" -type d -exec rm -rf {} + 2>/dev/null || true
}

print_summary() {
    echo ""
    echo "================================"
    echo "E2E Test Summary"
    echo "================================"
    echo -e "Passed:  ${GREEN}$PASSED${NC}"
    echo -e "Failed:  ${RED}$FAILED${NC}"
    echo -e "Skipped: ${YELLOW}$SKIPPED${NC}"
    echo "================================"

    if [[ $FAILED -gt 0 ]]; then
        exit 1
    fi
}

main() {
    echo ""
    echo "Calor E2E Test Suite"
    echo "===================="
    echo ""

    # Parse arguments
    local clean_only=false
    for arg in "$@"; do
        case $arg in
            --clean) clean_only=true ;;
            --help)
                echo "Usage: $0 [--clean] [--help]"
                echo "  --clean  Clean generated files only"
                echo "  --help   Show this help"
                exit 0
                ;;
        esac
    done

    if $clean_only; then
        cleanup
        info "Cleanup complete"
        exit 0
    fi

    build_compiler

    # Run each scenario
    for scenario in "$SCENARIOS_DIR"/*/; do
        if [[ -d "$scenario" ]]; then
            run_scenario "$scenario"
        fi
    done

    print_summary
}

main "$@"
