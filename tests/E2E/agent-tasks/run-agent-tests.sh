#!/usr/bin/env bash
# ============================================================================
# Calor E2E Agent Task Test Runner
# ============================================================================
#
# Runs automated tests that invoke Claude Code CLI to perform coding tasks,
# then verifies the results via compilation and Z3 contract verification.
#
# Compatible with bash 3.x (macOS default) and bash 4+
#
# Usage:
#   ./run-agent-tests.sh                     # Run all tasks (3 runs each, pass if 2/3)
#   ./run-agent-tests.sh --category basic    # Run tasks in specific category
#   ./run-agent-tests.sh --task basic-001    # Run single task
#   ./run-agent-tests.sh --single-run        # Single run (no majority voting)
#   ./run-agent-tests.sh --clean             # Clean up workspaces only
#   ./run-agent-tests.sh --list              # List all available tasks
#   ./run-agent-tests.sh --verbose           # Enable verbose output
#
# ============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Source helper library
source "$SCRIPT_DIR/lib/helpers.sh"

# ============================================================================
# COUNTERS
# ============================================================================

PASSED=0
FAILED=0
SKIPPED=0

# ============================================================================
# TASK DISCOVERY (bash 3.x compatible)
# ============================================================================

# Find all task directories and store in a temp file
# Usage: find_all_tasks > tasks.txt; while read task_dir; do ...; done < tasks.txt
find_all_tasks() {
    find "$TASKS_DIR" -name "task.json" -type f 2>/dev/null | while IFS= read -r task_file; do
        dirname "$task_file"
    done | sort
}

# Find tasks by category
find_tasks_by_category() {
    local category="$1"
    local category_dir="$TASKS_DIR/$category"

    if [[ ! -d "$category_dir" ]]; then
        return
    fi

    find "$category_dir" -name "task.json" -type f 2>/dev/null | while IFS= read -r task_file; do
        dirname "$task_file"
    done | sort
}

# Find task by ID - returns task directory path or empty string
# Fixed: Uses a temp file to avoid subshell issues
find_task_by_id() {
    local task_id="$1"
    local result=""

    while IFS= read -r task_file; do
        local json
        json=$(cat "$task_file" 2>/dev/null) || continue
        local id
        id=$(json_get "$json" "id" "")
        if [[ "$id" == "$task_id" ]]; then
            dirname "$task_file"
            return 0
        fi
    done < <(find "$TASKS_DIR" -name "task.json" -type f 2>/dev/null)

    return 1
}

# List all tasks with their details
list_tasks() {
    echo ""
    echo "Available Agent Tasks"
    echo "====================="
    echo ""

    local current_category=""

    find "$TASKS_DIR" -name "task.json" -type f 2>/dev/null | sort | while IFS= read -r task_file; do
        local task_dir
        task_dir=$(dirname "$task_file")
        local category
        category=$(basename "$(dirname "$task_dir")")

        if [[ "$category" != "$current_category" ]]; then
            echo ""
            echo -e "${BOLD}Category: $category${NC}"
            echo "----------------------------------------"
            current_category="$category"
        fi

        local json
        json=$(cat "$task_file")
        local id name
        id=$(json_get "$json" "id" "unknown")
        name=$(json_get "$json" "name" "$id")

        printf "  %-15s  %s\n" "$id" "$name"
    done

    echo ""
}

# ============================================================================
# TEST EXECUTION (bash 3.x compatible)
# ============================================================================

# Run tasks from a temp file containing task directories
run_tasks_from_file() {
    local tasks_file="$1"

    while IFS= read -r task_dir; do
        if [[ -z "$task_dir" || ! -d "$task_dir" ]]; then
            continue
        fi

        local result=0
        run_task_with_voting "$task_dir" "$SINGLE_RUN" || result=$?

        case $result in
            0) ((PASSED++)) || true ;;
            2) ((SKIPPED++)) || true ;;
            *) ((FAILED++)) || true ;;
        esac

        echo ""
    done < "$tasks_file"
}

# ============================================================================
# REPORTING
# ============================================================================

print_summary() {
    local total=$((PASSED + FAILED + SKIPPED))

    echo ""
    echo "============================================"
    echo "Agent Task Test Summary"
    echo "============================================"
    echo -e "Passed:  ${GREEN}$PASSED${NC}"
    echo -e "Failed:  ${RED}$FAILED${NC}"
    echo -e "Skipped: ${YELLOW}$SKIPPED${NC}"
    echo "--------------------------------------------"
    echo "Total:   $total"
    echo "============================================"

    if [[ $total -gt 0 ]]; then
        local pass_rate=$((PASSED * 100 / total))
        echo -e "Pass Rate: ${BOLD}${pass_rate}%${NC}"

        if [[ $pass_rate -ge 80 ]]; then
            echo -e "${GREEN}SUCCESS: Pass rate meets 80% threshold${NC}"
        else
            echo -e "${RED}WARNING: Pass rate below 80% threshold${NC}"
        fi
    fi

    echo "============================================"
    echo ""

    if [[ $FAILED -gt 0 ]]; then
        return 1
    fi
    return 0
}

# ============================================================================
# CLEANUP
# ============================================================================

cleanup() {
    log_info "Cleaning up temporary workspaces..."
    # Clean up any lingering temp directories
    find "${TMPDIR:-/tmp}" -maxdepth 1 -name "calor-agent-*" -type d -mmin +60 2>/dev/null | while IFS= read -r dir; do
        rm -rf "$dir" 2>/dev/null || true
    done
    find "${TMPDIR:-/tmp}" -maxdepth 1 -name "calor-clone-*" -type d -mmin +60 2>/dev/null | while IFS= read -r dir; do
        rm -rf "$dir" 2>/dev/null || true
    done
    log_info "Cleanup complete"
}

# Check system requirements
check_requirements() {
    local missing=()

    # Check for dotnet
    if ! command -v dotnet &> /dev/null; then
        missing+=("dotnet SDK (required for compilation)")
    fi

    # Check for timeout command
    if ! command -v timeout &> /dev/null && ! command -v gtimeout &> /dev/null; then
        log_warn "timeout command not found - install coreutils for reliable timeouts"
        log_warn "  macOS: brew install coreutils"
    fi

    # Report missing requirements
    if [[ ${#missing[@]} -gt 0 ]]; then
        log_fail "Missing required tools:"
        for tool in "${missing[@]}"; do
            echo "  - $tool"
        done
        exit 1
    fi
}

# ============================================================================
# USAGE
# ============================================================================

print_usage() {
    cat << EOF
Calor E2E Agent Task Test Runner

Usage: $0 [OPTIONS]

Options:
  --category <name>   Run only tasks in specified category
                      Categories: basic-syntax, contract-writing, logic-implementation,
                                  calor-idioms, github-projects
  --task <id>         Run only the task with specified ID
  --single-run        Run each task only once (skip majority voting)
  --list              List all available tasks
  --clean             Clean up temporary workspaces only
  --verbose           Enable verbose debug output
  --help              Show this help message

Examples:
  $0                              # Run all tasks with majority voting
  $0 --category contract-writing  # Run only contract-writing tasks
  $0 --task contract-001          # Run specific task
  $0 --single-run                 # Quick run without voting (for debugging)

Majority Voting:
  Each task runs 3 times by default. A task passes if 2/3 runs succeed.
  This handles non-determinism in agent output.

Requirements:
  - Claude CLI (claude command)
  - jq (for reliable JSON parsing)
  - .NET 8 SDK (for dotnet build)
  - Z3 (optional, for contract verification)

Exit Codes:
  0  All tests passed
  1  Some tests failed
EOF
}

# ============================================================================
# MAIN
# ============================================================================

main() {
    local category=""
    local task_id=""
    local clean_only=false
    local list_only=false
    SINGLE_RUN=false
    VERBOSE=false

    # Parse arguments
    while [[ $# -gt 0 ]]; do
        case $1 in
            --category)
                category="$2"
                shift 2
                ;;
            --task)
                task_id="$2"
                shift 2
                ;;
            --single-run)
                SINGLE_RUN=true
                shift
                ;;
            --clean)
                clean_only=true
                shift
                ;;
            --list)
                list_only=true
                shift
                ;;
            --verbose|-v)
                VERBOSE=true
                export VERBOSE
                shift
                ;;
            --help|-h)
                print_usage
                exit 0
                ;;
            *)
                echo "Unknown option: $1"
                print_usage
                exit 1
                ;;
        esac
    done

    echo ""
    echo "============================================"
    echo "Calor E2E Agent Task Test Suite"
    echo "============================================"
    echo ""

    # Handle special modes
    if $clean_only; then
        cleanup
        exit 0
    fi

    if $list_only; then
        check_jq  # Initialize JQ_AVAILABLE for JSON parsing
        list_tasks
        exit 0
    fi

    # Pre-flight checks
    check_requirements
    check_jq
    check_claude_cli
    build_compiler

    # Create temp file for task list (bash 3.x compatible)
    local tasks_file
    tasks_file=$(mktemp "${TMPDIR:-/tmp}/calor-tasks-XXXXXX")

    # Collect tasks to run
    if [[ -n "$task_id" ]]; then
        local task_dir
        task_dir=$(find_task_by_id "$task_id") || true
        if [[ -z "$task_dir" ]]; then
            log_fail "Task not found: $task_id"
            rm -f "$tasks_file"
            exit 1
        fi
        echo "$task_dir" > "$tasks_file"
    elif [[ -n "$category" ]]; then
        find_tasks_by_category "$category" > "$tasks_file"
        if [[ ! -s "$tasks_file" ]]; then
            log_fail "No tasks found in category: $category"
            rm -f "$tasks_file"
            exit 1
        fi
    else
        find_all_tasks > "$tasks_file"
    fi

    # Count tasks
    local task_count
    task_count=$(wc -l < "$tasks_file" | tr -d ' ')

    log_info "Running $task_count task(s)..."
    if $SINGLE_RUN; then
        log_info "Mode: Single run (no majority voting)"
    else
        log_info "Mode: Majority voting ($RUNS_PER_TASK runs, $REQUIRED_PASSES required to pass)"
    fi
    echo ""

    # Run tasks
    run_tasks_from_file "$tasks_file"

    # Cleanup temp file
    rm -f "$tasks_file"

    # Print summary and exit
    print_summary
}

main "$@"
