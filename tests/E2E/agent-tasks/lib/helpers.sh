#!/usr/bin/env bash
# Shared helper functions for agent task testing
# Sourced by run-agent-tests.sh
#
# Compatible with bash 3.x (macOS default) and bash 4+

set -euo pipefail

# ============================================================================
# CONFIGURATION
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AGENT_TASKS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$AGENT_TASKS_DIR/../../.." && pwd)"
COMPILER="${CALOR_COMPILER_ROOT:-$REPO_ROOT}/src/Calor.Compiler/bin/${CALOR_COMPILER_CONFIGURATION:-Debug}/net10.0/calor"
FIXTURES_DIR="$AGENT_TASKS_DIR/fixtures"
TASKS_DIR="$AGENT_TASKS_DIR/tasks"

# Default timeout for agent invocation (seconds)
DEFAULT_TIMEOUT=120

# Number of runs for majority voting
RUNS_PER_TASK=3
REQUIRED_PASSES=2

# Track if jq is available (set during check_jq)
JQ_AVAILABLE=false

# Initialize jq availability immediately
# Note: check_jq is defined below, so we call it after sourcing completes
# For now, do inline check
if command -v jq &> /dev/null; then
    JQ_AVAILABLE=true
fi

# Track active workspace for cleanup on interrupt
ACTIVE_WORKSPACE=""

# ============================================================================
# SIGNAL HANDLING
# ============================================================================

# Cleanup handler for interrupts
cleanup_on_interrupt() {
    echo ""
    echo -e "${YELLOW}[INTERRUPT]${NC} Caught signal, cleaning up..."
    if [[ -n "$ACTIVE_WORKSPACE" && -d "$ACTIVE_WORKSPACE" ]]; then
        rm -rf "$ACTIVE_WORKSPACE" 2>/dev/null || true
    fi
    # Kill any child processes
    jobs -p | xargs -r kill 2>/dev/null || true
    exit 130
}

# Set up signal handlers
trap cleanup_on_interrupt SIGINT SIGTERM

# ============================================================================
# COLORS
# ============================================================================

if [[ -t 1 ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    CYAN='\033[0;36m'
    BOLD='\033[1m'
    NC='\033[0m'
else
    RED='' GREEN='' YELLOW='' BLUE='' CYAN='' BOLD='' NC=''
fi

# ============================================================================
# LOGGING
# ============================================================================

log_info()    { echo -e "${BLUE}[INFO]${NC} $1"; }
log_pass()    { echo -e "${GREEN}[PASS]${NC} $1"; }
log_fail()    { echo -e "${RED}[FAIL]${NC} $1"; }
log_skip()    { echo -e "${YELLOW}[SKIP]${NC} $1"; }
log_warn()    { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_debug()   { [[ "${VERBOSE:-false}" == "true" ]] && echo -e "${CYAN}[DEBUG]${NC} $1" || true; }

# ============================================================================
# JSON PARSING
# Requires jq for reliable parsing. Will fail gracefully without it.
# ============================================================================

# Check if jq is available and set JQ_AVAILABLE
check_jq() {
    if command -v jq &> /dev/null; then
        JQ_AVAILABLE=true
        log_debug "jq found - JSON parsing enabled"
    else
        JQ_AVAILABLE=false
        log_warn "jq not found - install jq for reliable JSON parsing"
        log_warn "  macOS: brew install jq"
        log_warn "  Ubuntu: apt-get install jq"
    fi
}

# Get a simple string value from JSON
# Usage: json_get "$json" "key" "default"
json_get() {
    local json="$1"
    local key="$2"
    local default="${3:-}"

    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        local value
        value=$(echo "$json" | jq -r ".$key // empty" 2>/dev/null) || value=""
        echo "${value:-$default}"
    else
        # Fallback: simple grep for "key": "value" or "key": value patterns
        local value
        # Try quoted value first
        value=$(echo "$json" | grep -o "\"$key\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" | head -1 | sed 's/.*:[[:space:]]*"\([^"]*\)".*/\1/' 2>/dev/null) || value=""
        if [[ -z "$value" ]]; then
            # Try unquoted value (numbers, booleans)
            value=$(echo "$json" | grep -o "\"$key\"[[:space:]]*:[[:space:]]*[^,}]*" | head -1 | sed 's/.*:[[:space:]]*//' | tr -d ' ' 2>/dev/null) || value=""
        fi
        echo "${value:-$default}"
    fi
}

# Get a boolean value from JSON
# Usage: json_get_bool "$json" "key" "default"
json_get_bool() {
    local json="$1"
    local key="$2"
    local default="${3:-false}"

    local value
    value=$(json_get "$json" "$key" "$default")
    [[ "$value" == "true" ]] && echo "true" || echo "false"
}

# Get an integer value from JSON
# Usage: json_get_int "$json" "key" "default"
json_get_int() {
    local json="$1"
    local key="$2"
    local default="${3:-0}"

    local value
    value=$(json_get "$json" "$key" "$default")
    # Sanitize to digits only, handle negative numbers
    value=$(echo "$value" | tr -cd '0-9-' | head -c 12)
    echo "${value:-$default}"
}

# Get a nested value from JSON file (e.g., "verification.z3.enabled")
# Usage: json_get_nested_file "$json_file" "path.to.key" "default"
json_get_nested_file() {
    local json_file="$1"
    local path="$2"
    local default="${3:-}"

    if [[ "$JQ_AVAILABLE" == "true" && -f "$json_file" ]]; then
        local value
        # Use jq without // empty to properly handle boolean false values
        value=$(jq -r ".$path | if . == null then \"\" else tostring end" "$json_file" 2>/dev/null) || value=""
        if [[ -z "$value" ]]; then
            echo "$default"
        else
            echo "$value"
        fi
    else
        echo "$default"
    fi
}

# Get a nested value from JSON (e.g., "verification.z3.enabled")
# Usage: json_get_nested "$json" "path.to.key" "default"
# NOTE: For complex JSON with newlines, prefer json_get_nested_file
json_get_nested() {
    local json="$1"
    local path="$2"
    local default="${3:-}"

    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        local value
        value=$(printf '%s' "$json" | jq -r ".$path // empty" 2>/dev/null) || value=""
        echo "${value:-$default}"
    else
        # Fallback: Parse path and extract nested values
        # This is limited but handles common cases like "verification.z3.enabled"
        local IFS='.'
        local keys=($path)
        local current_json="$json"

        for key in "${keys[@]}"; do
            # Extract the object/value for this key
            if [[ "$JQ_AVAILABLE" != "true" ]]; then
                # Very basic extraction - find "key": { ... } or "key": value
                local extracted
                # Try to get a nested object
                extracted=$(echo "$current_json" | grep -o "\"$key\"[[:space:]]*:[[:space:]]*{[^}]*}" | head -1 | sed "s/\"$key\"[[:space:]]*:[[:space:]]*//" 2>/dev/null) || extracted=""
                if [[ -n "$extracted" ]]; then
                    current_json="$extracted"
                else
                    # Try to get a simple value
                    local value
                    value=$(echo "$current_json" | grep -o "\"$key\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" | head -1 | sed 's/.*:[[:space:]]*"\([^"]*\)".*/\1/' 2>/dev/null) || value=""
                    if [[ -z "$value" ]]; then
                        value=$(echo "$current_json" | grep -o "\"$key\"[[:space:]]*:[[:space:]]*[^,}\"]*" | head -1 | sed 's/.*:[[:space:]]*//' | tr -d ' ' 2>/dev/null) || value=""
                    fi
                    echo "${value:-$default}"
                    return
                fi
            fi
        done
        echo "$default"
    fi
}

# ============================================================================
# WORKSPACE MANAGEMENT
# ============================================================================

# Create a temporary workspace for a task
create_workspace() {
    local task_id="$1"
    local workspace
    workspace=$(mktemp -d "${TMPDIR:-/tmp}/calor-agent-${task_id}-XXXXXX")
    echo "$workspace"
}

# Copy fixture to workspace
copy_fixture() {
    local fixture_name="$1"
    local workspace="$2"

    local fixture_path="$FIXTURES_DIR/$fixture_name"
    if [[ ! -d "$fixture_path" ]]; then
        log_fail "Fixture not found: $fixture_name"
        return 1
    fi

    cp -r "$fixture_path"/* "$workspace/"
    log_debug "Copied fixture '$fixture_name' to workspace"
}

# Clone GitHub repository to workspace (simplified - just creates placeholder)
# For realistic testing, we use local fixtures instead
clone_github() {
    local url="$1"
    local tag="$2"
    local subdir="${3:-}"
    local workspace="$4"

    log_debug "Cloning $url (tag: $tag)..."

    local temp_clone
    temp_clone=$(mktemp -d "${TMPDIR:-/tmp}/calor-clone-XXXXXX")

    # Shallow clone for speed
    if ! git clone --depth 1 --branch "$tag" "$url" "$temp_clone" 2>/dev/null; then
        log_debug "Failed to clone $url at tag $tag"
        rm -rf "$temp_clone"
        return 1
    fi

    # Copy subdir or entire repo
    if [[ -n "$subdir" && -d "$temp_clone/$subdir" ]]; then
        cp -r "$temp_clone/$subdir"/* "$workspace/"
    else
        # Copy all except .git
        find "$temp_clone" -maxdepth 1 -mindepth 1 ! -name ".git" -exec cp -r {} "$workspace/" \;
    fi

    rm -rf "$temp_clone"
    log_debug "Cloned repository to workspace"
}

# Initialize Calor in workspace
# Note: We skip `calor init` for test workspaces because:
# 1. It adds MSBuild targets that auto-compile .calr files, causing duplicate definitions
# 2. The agent hooks aren't needed for automated verification
# Instead, we create a minimal CLAUDE.md for the agent to reference
init_calor() {
    local workspace="$1"

    # If fixture already has CLAUDE.md, preserve it (may have task-specific instructions)
    if [[ -f "$workspace/CLAUDE.md" ]]; then
        log_debug "Preserving existing CLAUDE.md from fixture"
        return 0
    fi

    # Use the same generated reference that packaged help and evaluation consume.
    # Check before copying: never launch an agent with stale teaching output.
    "$COMPILER" self-check docs --teaching-only --root "$REPO_ROOT" || return 1
    cp "$REPO_ROOT/src/Calor.Compiler/Resources/Teaching/Generated/current-language-reference.md" "$workspace/CLAUDE.md" || return 1

    log_debug "Created CLAUDE.md in workspace"
}

# Setup workspace (copy fixture OR clone GitHub, then init)
setup_workspace() {
    local task_json="$1"
    local workspace="$2"

    # Check for fixture vs github
    local fixture
    fixture=$(json_get "$task_json" "fixture" "")

    local github_url
    github_url=$(json_get_nested "$task_json" "github.url" "")

    if [[ -n "$fixture" ]]; then
        copy_fixture "$fixture" "$workspace" || return 1
    elif [[ -n "$github_url" ]]; then
        local tag subdir
        tag=$(json_get_nested "$task_json" "github.tag" "main")
        subdir=$(json_get_nested "$task_json" "github.subdir" "")
        clone_github "$github_url" "$tag" "$subdir" "$workspace" || return 1
    else
        log_fail "Task must specify either 'fixture' or 'github'"
        return 1
    fi

    init_calor "$workspace"
}

# Cleanup workspace
cleanup_workspace() {
    local workspace="$1"
    # KEEP_WORKSPACE=1 preserves the sandbox (agent output, generated .calr, build logs)
    # so a failing task can actually be diagnosed. Without it a failure leaves nothing
    # behind but "0/1 passed", which says nothing about why.
    if [[ "${KEEP_WORKSPACE:-0}" == "1" ]]; then
        log_info "KEEP_WORKSPACE=1 - preserving $workspace"
        if [[ "$ACTIVE_WORKSPACE" == "$workspace" ]]; then
            ACTIVE_WORKSPACE=""
        fi
        return 0
    fi
    if [[ -d "$workspace" && "$workspace" == *calor-agent-* ]]; then
        rm -rf "$workspace"
    fi
    # Clear active workspace tracker
    if [[ "$ACTIVE_WORKSPACE" == "$workspace" ]]; then
        ACTIVE_WORKSPACE=""
    fi
}

# Validate task.json has required fields
validate_task_json() {
    local task_json="$1"

    local id prompt
    id=$(json_get "$task_json" "id" "")
    prompt=$(json_get "$task_json" "prompt" "")

    if [[ -z "$id" ]]; then
        log_debug "Task JSON missing 'id' field"
        return 1
    fi

    if [[ -z "$prompt" ]]; then
        log_debug "Task JSON missing 'prompt' field"
        return 1
    fi

    # Check for fixture or github source
    local fixture github_url
    fixture=$(json_get "$task_json" "fixture" "")
    github_url=$(json_get_nested "$task_json" "github.url" "")

    if [[ -z "$fixture" && -z "$github_url" ]]; then
        log_debug "Task JSON missing 'fixture' or 'github' field"
        return 1
    fi

    return 0
}

# ============================================================================
# MULTI-TASK JSON SUPPORT
# Functions to handle JSON files with multiple tasks in a "tasks" array
# ============================================================================

# Check if a JSON file contains a "tasks" array (multi-task format)
is_multi_task_json() {
    local json_file="$1"
    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        local has_tasks
        has_tasks=$(jq -r 'has("tasks")' "$json_file" 2>/dev/null) || has_tasks="false"
        [[ "$has_tasks" == "true" ]]
    else
        grep -q '"tasks"[[:space:]]*:' "$json_file" 2>/dev/null
    fi
}

# Get task count from a multi-task JSON file
get_multi_task_count() {
    local json_file="$1"
    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        jq -r '.tasks | length' "$json_file" 2>/dev/null || echo 0
    else
        echo 0
    fi
}

# Extract a single task from a multi-task JSON file by index
# Usage: extract_task_by_index "file.json" 0
extract_task_by_index() {
    local json_file="$1"
    local index="$2"
    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        jq -c ".tasks[$index]" "$json_file" 2>/dev/null
    else
        echo ""
    fi
}

# Extract a single task from a multi-task JSON file by ID
# Usage: extract_task_by_id "file.json" "task-id"
extract_task_by_id() {
    local json_file="$1"
    local task_id="$2"
    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        jq -c ".tasks[] | select(.id == \"$task_id\")" "$json_file" 2>/dev/null
    else
        echo ""
    fi
}

# List all task IDs in a multi-task JSON file
list_multi_task_ids() {
    local json_file="$1"
    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        jq -r '.tasks[].id' "$json_file" 2>/dev/null
    fi
}

# ============================================================================
# AGENT INVOCATION
# ============================================================================

# Invoke Claude Code CLI with task prompt
# Returns: 0 on success, 124 on timeout, other on error
invoke_agent() {
    local prompt="$1"
    local workspace="$2"
    local timeout_secs="${3:-$DEFAULT_TIMEOUT}"
    local output_file="$4"

    local original_dir
    original_dir=$(pwd)
    cd "$workspace"

    log_debug "Invoking Claude Code agent (timeout: ${timeout_secs}s)..."
    log_debug "Prompt: ${prompt:0:80}..."

    local exit_code=0

    # Use timeout command (gtimeout on macOS if coreutils installed)
    local timeout_cmd="timeout"
    if ! command -v timeout &> /dev/null; then
        if command -v gtimeout &> /dev/null; then
            timeout_cmd="gtimeout"
        else
            log_warn "timeout command not found - agent may hang indefinitely"
            timeout_cmd=""
        fi
    fi

    # Run Claude in non-interactive mode
    if [[ -n "$timeout_cmd" ]]; then
        $timeout_cmd "$timeout_secs" claude \
            --print \
            --dangerously-skip-permissions \
            "$prompt" > "$output_file" 2>&1 || exit_code=$?
    else
        # No timeout available - run directly with bash timeout via subshell
        (
            claude \
                --print \
                --dangerously-skip-permissions \
                "$prompt" > "$output_file" 2>&1
        ) &
        local pid=$!
        local count=0
        while kill -0 $pid 2>/dev/null; do
            sleep 1
            ((count++))
            if [[ $count -ge $timeout_secs ]]; then
                kill -9 $pid 2>/dev/null || true
                exit_code=124
                break
            fi
        done
        wait $pid 2>/dev/null || exit_code=$?
    fi

    cd "$original_dir"

    if [[ $exit_code -eq 124 ]]; then
        log_debug "Agent timed out after ${timeout_secs}s"
        return 124
    fi

    # Check if output file has content
    if [[ ! -s "$output_file" ]]; then
        log_debug "Agent produced no output"
        return 1
    fi

    return $exit_code
}

# ============================================================================
# VERIFICATION
# ============================================================================

# Detect project type based on files present
# Returns: "calor", "csharp", or "unknown"
detect_project_type() {
    local workspace="$1"

    # Check for .calr files (Calor project)
    if find "$workspace" -name "*.calr" -type f 2>/dev/null | grep -q .; then
        echo "calor"
        return
    fi

    # Check for .cs files (C# project)
    if find "$workspace" -name "*.cs" -type f 2>/dev/null | grep -q .; then
        echo "csharp"
        return
    fi

    echo "unknown"
}

# Verify Calor compilation
# $3 (optional): extra compiler flags, e.g. "--permissive-effects" for tasks
# whose subject matter is outside the enforced first-order wedge (WS-W2:
# delegate invocation is a Calor0418 error under the default-on enforcement;
# the per-task flag is the explicit migration-policy waiver).
verify_calor_compilation() {
    local workspace="$1"
    local must_succeed="${2:-true}"
    local extra_flags="${3:-}"

    local original_dir
    original_dir=$(pwd)
    cd "$workspace"

    local compile_failed=false
    local files_found=false

    # Use find with -print0 and while read for bash 3.x compatibility
    while IFS= read -r -d '' calr_file; do
        files_found=true
        local cs_file="${calr_file%.calr}.g.cs"
        log_debug "Compiling $calr_file..."

        # Capture output for debugging
        local compile_output
        local compile_status=0
        # shellcheck disable=SC2086 — extra_flags is intentionally word-split
        compile_output=$("$COMPILER" --input "$calr_file" --output "$cs_file" $extra_flags 2>&1) || compile_status=$?

        if [[ $compile_status -ne 0 ]]; then
            log_debug "Calor compilation failed for $calr_file (exit code: $compile_status)"
            log_debug "Compiler output: $compile_output"
            compile_failed=true
        fi

        # Verify output file was created
        if [[ ! -f "$cs_file" ]]; then
            log_debug "Generated file not created: $cs_file"
            log_debug "Calor file contents:"
            log_debug "$(cat "$calr_file" 2>/dev/null | head -50)"
            if [[ -n "$compile_output" ]]; then
                log_debug "Compiler output: $compile_output"
            fi
            compile_failed=true
        fi
    done < <(find . -name "*.calr" -type f -print0 2>/dev/null)

    if [[ "$files_found" == "false" ]]; then
        log_debug "No .calr files found in workspace"
        compile_failed=true
    fi

    cd "$original_dir"

    if [[ "$compile_failed" == "true" && "$must_succeed" == "true" ]]; then
        return 1
    fi

    return 0
}

# Verify C# compilation using dotnet build
verify_csharp_compilation() {
    local workspace="$1"
    local must_succeed="${2:-true}"

    local original_dir
    original_dir=$(pwd)
    cd "$workspace"

    local compile_failed=false
    local csproj_found=false

    # Find .csproj files and build them
    while IFS= read -r -d '' csproj_file; do
        csproj_found=true
        log_debug "Building $csproj_file..."

        # Run dotnet build
        local build_output
        build_output=$(dotnet build "$csproj_file" --nologo -v q 2>&1) || {
            log_debug "C# compilation failed for $csproj_file"
            log_debug "Output: $build_output"
            compile_failed=true
        }
    done < <(find . -name "*.csproj" -type f -print0 2>/dev/null)

    if [[ "$csproj_found" == "false" ]]; then
        log_debug "No .csproj files found in workspace"
        # If no .csproj but .cs files exist, try to compile them directly
        local cs_count
        cs_count=$(find . -name "*.cs" -type f 2>/dev/null | wc -l | tr -d ' ')
        if [[ "$cs_count" -gt 0 ]]; then
            log_debug "Found $cs_count .cs files, checking syntax..."
            # Use dotnet script or csc if available, otherwise just check file exists
            # For now, we'll consider it passed if files exist (basic check)
            log_debug "C# syntax check: files present, assuming valid"
        else
            compile_failed=true
        fi
    fi

    cd "$original_dir"

    if [[ "$compile_failed" == "true" && "$must_succeed" == "true" ]]; then
        return 1
    fi

    return 0
}

# Verify compilation - auto-detects project type
# For Calor projects: runs calor compiler
# For C# projects: runs dotnet build
verify_compilation() {
    local workspace="$1"
    local must_succeed="${2:-true}"
    local calor_extra_flags="${3:-}"

    local project_type
    project_type=$(detect_project_type "$workspace")

    log_debug "Detected project type: $project_type"

    case "$project_type" in
        calor)
            verify_calor_compilation "$workspace" "$must_succeed" "$calor_extra_flags"
            ;;
        csharp)
            verify_csharp_compilation "$workspace" "$must_succeed"
            ;;
        *)
            log_debug "Unknown project type - no compilation files found"
            if [[ "$must_succeed" == "true" ]]; then
                return 1
            fi
            return 0
            ;;
    esac
}

# Verify Z3 contracts
verify_z3() {
    local workspace="$1"
    local min_proven="${2:-0}"
    local max_disproven="${3:-999}"

    local original_dir
    original_dir=$(pwd)
    cd "$workspace"

    local total_proven=0
    local total_disproven=0

    # Find and verify .calr files
    while IFS= read -r -d '' calr_file; do
        log_debug "Verifying contracts in $calr_file..."

        # Run calor verify command
        local verify_output
        verify_output=$("$COMPILER" verify "$calr_file" --format json 2>/dev/null) || verify_output=""

        if [[ -n "$verify_output" && "$JQ_AVAILABLE" == "true" ]]; then
            local proven disproven
            proven=$(echo "$verify_output" | jq -r '.summary.proven // 0' 2>/dev/null) || proven=0
            disproven=$(echo "$verify_output" | jq -r '.summary.disproven // 0' 2>/dev/null) || disproven=0
            total_proven=$((total_proven + proven))
            total_disproven=$((total_disproven + disproven))
        elif [[ -n "$verify_output" ]]; then
            # Fallback parsing without jq
            local proven disproven
            proven=$(echo "$verify_output" | grep -o '"proven"[[:space:]]*:[[:space:]]*[0-9]*' | head -1 | grep -o '[0-9]*$') || proven=0
            disproven=$(echo "$verify_output" | grep -o '"disproven"[[:space:]]*:[[:space:]]*[0-9]*' | head -1 | grep -o '[0-9]*$') || disproven=0
            total_proven=$((total_proven + ${proven:-0}))
            total_disproven=$((total_disproven + ${disproven:-0}))
        fi
    done < <(find . -name "*.calr" -type f -print0 2>/dev/null)

    cd "$original_dir"

    log_debug "Z3 results: proven=$total_proven, disproven=$total_disproven (need: proven>=$min_proven, disproven<=$max_disproven)"

    # Check constraints
    if [[ $total_proven -lt $min_proven ]]; then
        log_debug "Insufficient proven contracts: $total_proven < $min_proven"
        return 1
    fi

    if [[ $total_disproven -gt $max_disproven ]]; then
        log_debug "Too many disproven contracts: $total_disproven > $max_disproven"
        return 1
    fi

    return 0
}

# Run custom verification script
run_verify_script() {
    local task_dir="$1"
    local workspace="$2"

    local verify_script="$task_dir/verify.sh"
    if [[ -f "$verify_script" ]]; then
        chmod +x "$verify_script"
        log_debug "Running custom verification script..."
        if ! "$verify_script" "$workspace"; then
            log_debug "Custom verification script failed"
            return 1
        fi
    fi

    return 0
}

# Full verification pipeline
verify_task() {
    local task_json="$1"
    local task_dir="$2"
    local workspace="$3"

    # Use file-based JSON parsing to avoid issues with special characters
    local task_file="$task_dir/task.json"

    # Check compilation requirement
    local must_compile
    must_compile=$(json_get_nested_file "$task_file" "verification.compilation.mustSucceed" "true")

    if [[ "$must_compile" == "true" ]]; then
        # WS-W2: per-task opt-in waiver for code outside the enforced
        # first-order wedge (delegate/lambda invocation tasks).
        local permissive_effects
        permissive_effects=$(json_get_nested_file "$task_file" "verification.compilation.permissiveEffects" "false")
        local calor_extra_flags=""
        if [[ "$permissive_effects" == "true" ]]; then
            calor_extra_flags="--permissive-effects"
        fi
        if ! verify_compilation "$workspace" "true" "$calor_extra_flags"; then
            log_debug "Compilation verification failed"
            return 1
        fi
        log_debug "Compilation verification passed"
    fi

    # Check Z3 verification
    local z3_enabled
    z3_enabled=$(json_get_nested_file "$task_file" "verification.z3.enabled" "false")

    if [[ "$z3_enabled" == "true" ]]; then
        local min_proven max_disproven
        min_proven=$(json_get_nested_file "$task_file" "verification.z3.minProvenContracts" "0")
        max_disproven=$(json_get_nested_file "$task_file" "verification.z3.maxDisprovenContracts" "999")

        if ! verify_z3 "$workspace" "$min_proven" "$max_disproven"; then
            log_debug "Z3 verification failed"
            return 1
        fi
        log_debug "Z3 verification passed"
    fi

    # Run custom verification script
    if ! run_verify_script "$task_dir" "$workspace"; then
        return 1
    fi

    return 0
}

# ============================================================================
# TASK EXECUTION
# ============================================================================

# Run a single task once
run_task_once() {
    local task_dir="$1"
    local task_json="$2"
    local run_number="$3"

    local task_id
    task_id=$(json_get "$task_json" "id" "unknown")

    # Create workspace
    local workspace
    workspace=$(create_workspace "${task_id}-run${run_number}")

    # Track for cleanup on interrupt
    ACTIVE_WORKSPACE="$workspace"

    log_debug "Workspace: $workspace"

    # Setup workspace
    if ! setup_workspace "$task_json" "$workspace"; then
        cleanup_workspace "$workspace"
        return 1
    fi

    # Get prompt and timeout
    local prompt timeout
    prompt=$(json_get "$task_json" "prompt" "")
    timeout=$(json_get "$task_json" "timeout" "$DEFAULT_TIMEOUT")

    if [[ -z "$prompt" ]]; then
        log_fail "Task $task_id has no prompt"
        cleanup_workspace "$workspace"
        return 1
    fi

    # Create output file for agent response
    local agent_output="$workspace/.agent-output.json"

    # Invoke agent
    local agent_exit=0
    invoke_agent "$prompt" "$workspace" "$timeout" "$agent_output" || agent_exit=$?

    if [[ $agent_exit -eq 124 ]]; then
        log_debug "Run $run_number: Agent timed out"
        cleanup_workspace "$workspace"
        return 1
    fi

    if [[ $agent_exit -ne 0 ]]; then
        log_debug "Run $run_number: Agent exited with code $agent_exit"
        # Don't fail immediately - agent may have made partial progress
    fi

    # Verify results
    if ! verify_task "$task_json" "$task_dir" "$workspace"; then
        cleanup_workspace "$workspace"
        return 1
    fi

    cleanup_workspace "$workspace"
    return 0
}

# Run task with majority voting
# Returns: 0=pass, 1=fail, 2=skip
run_task_with_voting() {
    local task_dir="$1"
    local single_run="${2:-false}"

    local task_file="$task_dir/task.json"
    if [[ ! -f "$task_file" ]]; then
        log_skip "$(basename "$task_dir") - no task.json"
        return 2  # Skip
    fi

    local task_json
    task_json=$(cat "$task_file") || {
        log_skip "$(basename "$task_dir") - cannot read task.json"
        return 2
    }

    # Validate task.json structure
    if ! validate_task_json "$task_json"; then
        log_skip "$(basename "$task_dir") - invalid task.json"
        return 2
    fi

    local task_id task_name
    task_id=$(json_get "$task_json" "id" "unknown")
    task_name=$(json_get "$task_json" "name" "$task_id")

    echo -e "${BLUE}Running: ${BOLD}$task_name${NC} ($task_id)"

    local passes=0
    local runs=$RUNS_PER_TASK

    if [[ "$single_run" == "true" ]]; then
        runs=1
    fi

    for ((i=1; i<=runs; i++)); do
        log_debug "Run $i of $runs..."
        if run_task_once "$task_dir" "$task_json" "$i"; then
            ((passes++)) || true
            log_debug "Run $i: PASSED"
        else
            log_debug "Run $i: FAILED"
        fi
    done

    # Determine result
    local required=$REQUIRED_PASSES
    if [[ "$single_run" == "true" ]]; then
        required=1
    fi

    if [[ $passes -ge $required ]]; then
        log_pass "$task_id: $passes/$runs passed"
        return 0
    else
        log_fail "$task_id: $passes/$runs passed (need $required)"
        return 1
    fi
}

# ============================================================================
# BUILD
# ============================================================================

build_compiler() {
    log_info "Building Calor compiler..."
    if ! dotnet build "$REPO_ROOT/src/Calor.Compiler/Calor.Compiler.csproj" -c Debug --nologo -v q 2>/dev/null; then
        log_fail "Failed to build compiler"
        exit 1
    fi
    log_info "Compiler built successfully"
    echo ""
}

# Check if Claude CLI is available
check_claude_cli() {
    if ! command -v claude &> /dev/null; then
        log_fail "Claude CLI not found. Install from https://claude.ai/code"
        exit 1
    fi
    log_debug "Claude CLI found: $(which claude)"
}

# ============================================================================
# REFACTORING VERIFICATION
# Functions for verifying refactoring-specific properties
# ============================================================================

# Count contracts (§Q and §S) in a Calor file
# Usage: count_contracts "path/to/file.calr"
count_contracts() {
    local file="$1"
    if [[ ! -f "$file" ]]; then
        echo 0
        return
    fi
    local count=0
    # Count §Q (preconditions) and §S (postconditions)
    local q_count s_count
    q_count=$(grep -c '§Q' "$file" 2>/dev/null) || q_count=0
    s_count=$(grep -c '§S' "$file" 2>/dev/null) || s_count=0
    echo $((q_count + s_count))
}

# Count effects (§E{...}) in a Calor file
# Usage: count_effects "path/to/file.calr"
count_effects() {
    local file="$1"
    if [[ ! -f "$file" ]]; then
        echo 0
        return
    fi
    local count
    count=$(grep -c '§E{' "$file" 2>/dev/null) || count=0
    echo "$count"
}

# Extract unique IDs from a Calor file
# Usage: extract_ids "path/to/file.calr"
extract_ids() {
    local file="$1"
    if [[ ! -f "$file" ]]; then
        return
    fi
    # Extract function IDs (f001, f002, etc.), module IDs (m001), and variable IDs (v001)
    grep -oE '§[FMV]\{[^:]+:' "$file" 2>/dev/null | sed 's/§[FMV]{//' | sed 's/:$//' | sort -u
}

# Verify contract preservation after refactoring
# Usage: verify_contract_preservation "before_file" "after_file"
# Returns 0 if contracts are preserved or increased, 1 otherwise
verify_contract_preservation() {
    local before_file="$1"
    local after_file="$2"

    local before_contracts after_contracts
    before_contracts=$(count_contracts "$before_file")
    after_contracts=$(count_contracts "$after_file")

    log_debug "Contract preservation: before=$before_contracts, after=$after_contracts"

    # Contracts should be preserved or increased (propagated to new functions)
    if [[ $after_contracts -ge $before_contracts ]]; then
        log_debug "Contract preservation: PASSED (contracts preserved or increased)"
        return 0
    else
        log_debug "Contract preservation: FAILED (contracts decreased from $before_contracts to $after_contracts)"
        return 1
    fi
}

# Verify effect preservation after refactoring
# Usage: verify_effect_preservation "before_file" "after_file"
# Returns 0 if effects are preserved, 1 otherwise
verify_effect_preservation() {
    local before_file="$1"
    local after_file="$2"

    local before_effects after_effects
    before_effects=$(count_effects "$before_file")
    after_effects=$(count_effects "$after_file")

    log_debug "Effect preservation: before=$before_effects, after=$after_effects"

    # Effects should be preserved (same count or distributed across functions)
    if [[ $after_effects -ge $before_effects ]]; then
        log_debug "Effect preservation: PASSED"
        return 0
    else
        log_debug "Effect preservation: FAILED (effects decreased from $before_effects to $after_effects)"
        return 1
    fi
}

# Verify ID stability after refactoring (Calor only)
# Usage: verify_id_stability "before_file" "after_file"
# Returns 0 if original IDs are preserved, 1 otherwise
verify_id_stability() {
    local before_file="$1"
    local after_file="$2"

    # Get IDs from before and after
    local before_ids after_ids
    before_ids=$(extract_ids "$before_file" | tr '\n' ' ')
    after_ids=$(extract_ids "$after_file" | tr '\n' ' ')

    log_debug "ID stability: before=[$before_ids], after=[$after_ids]"

    # Check that all original IDs still exist
    local missing_ids=false
    for id in $before_ids; do
        if ! echo "$after_ids" | grep -q "$id"; then
            log_debug "ID stability: Missing ID '$id'"
            missing_ids=true
        fi
    done

    if [[ "$missing_ids" == "false" ]]; then
        log_debug "ID stability: PASSED (all original IDs preserved)"
        return 0
    else
        log_debug "ID stability: FAILED (some IDs were lost)"
        return 1
    fi
}

# ============================================================================
# C# CONTRACT COMMENT VERIFICATION
# Verifies comment-based contracts in C# code (// Requires:, // Ensures:, etc.)
# ============================================================================

# Count C# contract comments in a file
# Recognizes patterns like:
#   // Requires: x > 0
#   // Ensures: result >= 0
#   // Contract: ...
#   /// <requires>...</requires>
#   [ContractAttribute] (simplified detection)
# Usage: count_csharp_contract_comments "path/to/file.cs"
count_csharp_contract_comments() {
    local file="$1"
    if [[ ! -f "$file" ]]; then
        echo 0
        return
    fi

    local count=0

    # Count // Requires: and // Ensures: comments
    local requires_count ensures_count contract_count
    requires_count=$(grep -ciE '//\s*(requires|precondition):' "$file" 2>/dev/null) || requires_count=0
    ensures_count=$(grep -ciE '//\s*(ensures|postcondition):' "$file" 2>/dev/null) || ensures_count=0

    # Count generic // Contract: comments
    contract_count=$(grep -ciE '//\s*contract:' "$file" 2>/dev/null) || contract_count=0

    # Count XML doc contract tags
    local xml_requires xml_ensures
    xml_requires=$(grep -ciE '<requires>' "$file" 2>/dev/null) || xml_requires=0
    xml_ensures=$(grep -ciE '<ensures>' "$file" 2>/dev/null) || xml_ensures=0

    # Count Code Contracts attributes (simplified)
    local attr_count
    attr_count=$(grep -ciE '\[Contract' "$file" 2>/dev/null) || attr_count=0

    # Count Debug.Assert/Trace.Assert as informal contracts
    local assert_count
    assert_count=$(grep -ciE '(Debug|Trace)\.Assert\s*\(' "$file" 2>/dev/null) || assert_count=0

    echo $((requires_count + ensures_count + contract_count + xml_requires + xml_ensures + attr_count + assert_count))
}

# Count C# contract comments in all .cs files in a directory
# Usage: count_csharp_contracts_in_dir "path/to/dir"
count_csharp_contracts_in_dir() {
    local dir="$1"
    local total=0

    while IFS= read -r -d '' file; do
        local file_count
        file_count=$(count_csharp_contract_comments "$file")
        total=$((total + file_count))
    done < <(find "$dir" -name "*.cs" -type f -print0 2>/dev/null)

    echo "$total"
}

# Verify C# contract comment preservation after refactoring
# Usage: verify_csharp_contract_preservation "before_dir" "after_dir"
# Returns 0 if contracts are preserved or increased, 1 otherwise
verify_csharp_contract_preservation() {
    local before_dir="$1"
    local after_dir="$2"

    local before_contracts after_contracts
    before_contracts=$(count_csharp_contracts_in_dir "$before_dir")
    after_contracts=$(count_csharp_contracts_in_dir "$after_dir")

    log_debug "C# contract preservation: before=$before_contracts, after=$after_contracts"

    if [[ $after_contracts -ge $before_contracts ]]; then
        log_debug "C# contract preservation: PASSED"
        return 0
    else
        log_debug "C# contract preservation: FAILED (contracts decreased from $before_contracts to $after_contracts)"
        return 1
    fi
}

# Extract contract content from C# files (for detailed comparison)
# Usage: extract_csharp_contracts "path/to/file.cs"
extract_csharp_contracts() {
    local file="$1"
    if [[ ! -f "$file" ]]; then
        return
    fi

    # Extract all contract-like lines
    grep -iE '//\s*(requires|ensures|precondition|postcondition|contract):|<(requires|ensures)>|\[Contract' "$file" 2>/dev/null | sort
}

# Compare C# contract content between files
# Usage: compare_csharp_contracts "before_file" "after_file"
# Returns 0 if all before contracts exist in after, 1 otherwise
compare_csharp_contracts() {
    local before_file="$1"
    local after_file="$2"

    local before_contracts after_contracts
    before_contracts=$(extract_csharp_contracts "$before_file")
    after_contracts=$(extract_csharp_contracts "$after_file")

    # If no contracts in before, always pass
    if [[ -z "$before_contracts" ]]; then
        return 0
    fi

    # Check each before contract exists in after
    local missing=false
    while IFS= read -r contract; do
        if ! echo "$after_contracts" | grep -qF "$contract"; then
            log_debug "Missing contract: $contract"
            missing=true
        fi
    done <<< "$before_contracts"

    if [[ "$missing" == "true" ]]; then
        return 1
    fi
    return 0
}

# Count functions in a workspace (for both Calor and C#)
# Usage: count_functions "workspace_path" "calor|csharp"
count_functions() {
    local workspace="$1"
    local lang="${2:-calor}"

    local count=0
    if [[ "$lang" == "calor" ]]; then
        # Count §F{ patterns in .calr files
        while IFS= read -r -d '' file; do
            local file_count
            file_count=$(grep -c '§F{' "$file" 2>/dev/null) || file_count=0
            count=$((count + file_count))
        done < <(find "$workspace" -name "*.calr" -type f -print0 2>/dev/null)
    else
        # Count method declarations in .cs files (simplified)
        while IFS= read -r -d '' file; do
            local file_count
            file_count=$(grep -cE '(public|private|protected|internal)\s+\w+\s+\w+\s*\(' "$file" 2>/dev/null) || file_count=0
            count=$((count + file_count))
        done < <(find "$workspace" -name "*.cs" -type f -print0 2>/dev/null)
    fi

    echo "$count"
}

# Verify refactoring metrics for a workspace
# Usage: verify_refactoring_metrics "workspace" "before_snapshot_dir" "language"
# Returns JSON with metrics
get_refactoring_metrics() {
    local workspace="$1"
    local before_dir="$2"
    local lang="${3:-calor}"

    if [[ "$lang" == "calor" ]]; then
        local before_contracts=0 after_contracts=0
        local before_effects=0 after_effects=0
        local before_funcs=0 after_funcs=0

        # Count in before snapshot
        while IFS= read -r -d '' file; do
            before_contracts=$((before_contracts + $(count_contracts "$file")))
            before_effects=$((before_effects + $(count_effects "$file")))
            before_funcs=$((before_funcs + $(grep -c '§F{' "$file" 2>/dev/null || echo 0)))
        done < <(find "$before_dir" -name "*.calr" -type f -print0 2>/dev/null)

        # Count in after (workspace)
        while IFS= read -r -d '' file; do
            after_contracts=$((after_contracts + $(count_contracts "$file")))
            after_effects=$((after_effects + $(count_effects "$file")))
            after_funcs=$((after_funcs + $(grep -c '§F{' "$file" 2>/dev/null || echo 0)))
        done < <(find "$workspace" -name "*.calr" -type f -print0 2>/dev/null)

        cat << EOF
{
  "language": "calor",
  "contractsBefore": $before_contracts,
  "contractsAfter": $after_contracts,
  "contractsPreserved": $([[ $after_contracts -ge $before_contracts ]] && echo "true" || echo "false"),
  "effectsBefore": $before_effects,
  "effectsAfter": $after_effects,
  "effectsPreserved": $([[ $after_effects -ge $before_effects ]] && echo "true" || echo "false"),
  "functionsBefore": $before_funcs,
  "functionsAfter": $after_funcs
}
EOF
    else
        # C# metrics - function count, compilation, and comment-based contracts
        local before_funcs=0 after_funcs=0
        local before_contracts=0 after_contracts=0

        while IFS= read -r -d '' file; do
            before_funcs=$((before_funcs + $(grep -cE '(public|private|protected)\s+\w+\s+\w+\s*\(' "$file" 2>/dev/null || echo 0)))
            before_contracts=$((before_contracts + $(count_csharp_contract_comments "$file")))
        done < <(find "$before_dir" -name "*.cs" -type f -print0 2>/dev/null)

        while IFS= read -r -d '' file; do
            after_funcs=$((after_funcs + $(grep -cE '(public|private|protected)\s+\w+\s+\w+\s*\(' "$file" 2>/dev/null || echo 0)))
            after_contracts=$((after_contracts + $(count_csharp_contract_comments "$file")))
        done < <(find "$workspace" -name "*.cs" -type f -print0 2>/dev/null)

        cat << EOF
{
  "language": "csharp",
  "functionsBefore": $before_funcs,
  "functionsAfter": $after_funcs,
  "contractCommentsBefore": $before_contracts,
  "contractCommentsAfter": $after_contracts,
  "contractsPreserved": $([[ $after_contracts -ge $before_contracts ]] && echo "true" || echo "false")
}
EOF
    fi
}
