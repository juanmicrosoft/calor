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
COMPILER="$REPO_ROOT/src/Calor.Compiler/bin/Debug/net8.0/calor"
FIXTURES_DIR="$AGENT_TASKS_DIR/fixtures"
TASKS_DIR="$AGENT_TASKS_DIR/tasks"

# Default timeout for agent invocation (seconds)
DEFAULT_TIMEOUT=120

# Number of runs for majority voting
RUNS_PER_TASK=3
REQUIRED_PASSES=2

# Track if jq is available (set during check_jq)
JQ_AVAILABLE=false

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

# Get a nested value from JSON (e.g., "verification.z3.enabled")
# Usage: json_get_nested "$json" "path.to.key" "default"
json_get_nested() {
    local json="$1"
    local path="$2"
    local default="${3:-}"

    if [[ "$JQ_AVAILABLE" == "true" ]]; then
        local value
        value=$(echo "$json" | jq -r ".$path // empty" 2>/dev/null) || value=""
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

    # Create comprehensive CLAUDE.md with Calor syntax reference for the agent
    cat > "$workspace/CLAUDE.md" << 'CALOR_REFERENCE'
## Calor Syntax Reference

This is a Calor project. Write code in `.calr` files.

### Function Syntax
```
§F{id:Name:pub}
  §I{type:name}      // Input parameter
  §O{type}           // Output/return type
  §Q (condition)     // Precondition (requires)
  §S (condition)     // Postcondition (ensures)
  §E{effects}        // Effects declaration
  §R expression      // Return
§/F{id}
```

### Type Mappings
| C# | Calor |
|----|-------|
| `int` | `i32` |
| `long` | `i64` |
| `string` | `str` |
| `bool` | `bool` |
| `void` | `void` |
| `int?` or `Option<int>` | `Option<i32>` |

### Expression Syntax (Lisp-style, prefix notation)
- Arithmetic: `(+ a b)`, `(- a b)`, `(* a b)`, `(/ a b)`, `(% a b)`
- Comparison: `(== a b)`, `(!= a b)`, `(< a b)`, `(> a b)`, `(<= a b)`, `(>= a b)`
- Logical: `(and a b)`, `(or a b)`, `(not a)`
- Ternary/Conditional: `(? condition then-expr else-expr)`
- Negation: `(- 0 x)` for negative x

### Contracts
- Precondition: `§Q (>= x 0)` or with message: `§Q{"x must be non-negative"} (>= x 0)`
- Postcondition: `§S (>= result 0)` - use `result` to refer to return value
- Multiple contracts: Add multiple §Q or §S lines

### Effects Declaration
Effects declare what side-effects a function may have:
- `cw` = console write (Console.WriteLine, etc.)
- `cr` = console read
- `fs` = file system access
- `net` = network access
- `db` = database access

Example with effects:
```
§F{f001:PrintValue:pub}
  §I{i32:x}
  §O{void}
  §E{cw}
  §C{Console.WriteLine}
    §A x
  §/C
§/F{f001}
```

### Method Calls
External method calls use §C (call) sections:
```
§C{Console.WriteLine}
  §A "Hello"
§/C
```

### Examples

**Simple function with precondition:**
```
§F{f003:SquareRoot:pub}
  §I{i32:x}
  §O{i32}
  §Q (>= x 0)
  §R x
§/F{f003}
```

**Function with postcondition:**
```
§F{f003:Abs:pub}
  §I{i32:x}
  §O{i32}
  §S (>= result 0)
  §R (? (< x 0) (- 0 x) x)
§/F{f003}
```

**Function with effects (console output):**
```
§F{f003:PrintNumber:pub}
  §I{i32:x}
  §O{void}
  §E{cw}
  §C{Console.WriteLine}
    §A x
  §/C
§/F{f003}
```

**Nested ternary (if-else-if):**
```
§R (? (< x 0) (- 1) (? (== x 0) 0 1))
```
This returns -1 if x<0, 0 if x==0, 1 otherwise.

### Option Type (for nullable/optional values)
Return type: `§O{Option<i32>}` for Option<int>

Constructors:
- `§SM value` = Some(value) - wrap value
- `§NN` = None - no value

Example:
```
§F{f001:TryDouble:pub}
  §I{i32:x}
  §O{Option<i32>}
  §IF{if1} (> x 0) → §R §SM (* x 2)
  §EL → §R §NN
  §/I{if1}
§/F{f001}
```

### Result Type (for success/error returns)
Return type: `§O{Result<i32,str>}` for Result<int, string>

Constructors:
- `§OK value` = Ok(value) - success with value
- `§ERR "message"` = Err(message) - error with message

Example:
```
§F{f001:SafeDivide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{Result<i32,str>}
  §IF{if1} (!= b 0) → §R §OK (/ a b)
  §EL → §R §ERR "Division by zero"
  §/I{if1}
§/F{f001}
```

### Control Flow (If/Else with arrow syntax)
```
§IF{id} condition → action
§EI condition → action
§EL → action
§/I{id}
```

### Collections

**List - create empty, add values, iterate:**
```
§LIST{nums:i32}         // Create EMPTY List<int> named nums
§/LIST{nums}            // Close list (empty)
§PUSH{nums} 10          // Add 10 to list
§PUSH{nums} 20          // Add 20 to list
§PUSH{nums} 30          // Add 30 to list
§CNT{nums}              // Get count (returns 3)
§HAS{nums} 20           // Check if contains (returns true)

§EACH{e1:n} nums        // Foreach n in nums
  ...body...
§/EACH{e1}              // Close foreach
```

**Complete function with list iteration:**
```
§F{f001:SumList:pub}
  §O{i32}
  §LIST{nums:i32}
  §/LIST{nums}
  §PUSH{nums} 10
  §PUSH{nums} 20
  §PUSH{nums} 30
  §VAR{sum:i32} 0
  §EACH{e1:n} nums
    §SET{sum} (+ sum n)
  §/EACH{e1}
  §R sum
§/F{f001}
```

**Dictionary - create, add entries, iterate:**
```
§DICT{scores:str:i32}   // Create Dictionary<string, int>
§/DICT{scores}          // Close (empty)
§PUT{scores} "alice" 95 // Add key-value pair
§PUT{scores} "bob" 87   // Add another pair
§HAS{scores} "alice"    // Check if key exists

§EACHKV{e1:k:v} scores  // Foreach key-value
  §P k
  §P v
§/EACHKV{e1}            // Close foreach
```

**HashSet - create, add values:**
```
§HSET{tags:str}         // Create HashSet<string>
§/HSET{tags}            // Close (empty)
§PUSH{tags} "urgent"    // Add to set
§PUSH{tags} "review"    // Add to set
§HAS{tags} "urgent"     // Check membership
```

### Async Functions

**Async function with await:**
```
§AF{af1:ProcessAsync:pub}
  §O{Task<i32>}
  §E{net:r}
  §VAR{data:i32} §AWAIT (FetchDataAsync)
  §R (* data 2)
§/AF{af1}
```

Key points:
- Use `§AF{id:Name:pub}` for async function (not `§F`)
- Return type is `Task<T>`
- Use `§AWAIT expression` to await async operations
- `§E{net:r}` declares network read effect

### Lambdas and Delegates

**Function using inline arrow lambda:**
```
§F{f001:ApplyDouble:pub}
  §I{i32:x}
  §O{i32}
  §VAR{doubler:Func<i32,i32>} (n) → (* n 2)
  §R (doubler x)
§/F{f001}
```

Key lambda syntax:
- `(param) → expression` for inline lambda
- `(a, b) → (+ a b)` for multiple params
- Use `§VAR{name:type} lambda` to bind lambda to variable

**Block lambda (multi-statement):**
```
§LM{lm1:i32:i32}        // Lambda: i32 -> i32
  §R (* x 2)
§/LM{lm1}
```

**Delegate definition:**
```
§DG{dg1:MathOp}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
§/DG{dg1}
```

### Class Methods

**Method in a class:**
```
§CL{cl1:Calculator:pub}
  §MT{mt1:Add:pub}
    §I{i32:a}
    §I{i32:b}
    §O{i32}
    §R (+ a b)
  §/MT{mt1}
§/CL{cl1}
```

### File System Effects

**File read:**
```
§E{fs:r}                // or §E{fs} for file read effect
```

**File write:**
```
§E{fs:w}                // File write effect
```

**Multiple effects (comma-separated):**
```
§E{cw,fs:w}             // Console write AND file write
§E{net,cw}              // Network AND console write
```

### Quantifiers (for contracts)

**Forall quantifier:**
```
§Q (forall i (and (>= i 0) (< i (len arr))) (> (at arr i) 0))
```
Meaning: For all indices i in array, element at i > 0

**Exists quantifier:**
```
§S (exists i (and (>= i 0) (< i (len arr))) (== (at arr i) target))
```
Meaning: There exists an index i where element equals target

CALOR_REFERENCE

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
            --output-format=json \
            "$prompt" > "$output_file" 2>&1 || exit_code=$?
    else
        # No timeout available - run directly with bash timeout via subshell
        (
            claude \
                --print \
                --dangerously-skip-permissions \
                --output-format=json \
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

# Verify compilation with Calor compiler
# Note: We only verify Calor compilation, not dotnet build, because:
# 1. The fixtures don't include Calor.Runtime reference
# 2. dotnet build with MSBuild targets would create duplicate definitions
# The goal is to verify the agent wrote valid Calor syntax
verify_compilation() {
    local workspace="$1"
    local must_succeed="${2:-true}"

    local original_dir
    original_dir=$(pwd)
    cd "$workspace"

    # Find .calr files and compile them
    local compile_failed=false
    local files_found=false

    # Use find with -print0 and while read for bash 3.x compatibility
    while IFS= read -r -d '' calr_file; do
        files_found=true
        local cs_file="${calr_file%.calr}.g.cs"
        log_debug "Compiling $calr_file..."

        # Capture output for debugging
        local compile_output
        compile_output=$("$COMPILER" --input "$calr_file" --output "$cs_file" 2>&1) || {
            log_debug "Calor compilation failed for $calr_file"
            log_debug "Output: $compile_output"
            compile_failed=true
        }

        # Verify output file was created
        if [[ ! -f "$cs_file" ]]; then
            log_debug "Generated file not created: $cs_file"
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

    # Check compilation requirement
    local must_compile
    must_compile=$(json_get_nested "$task_json" "verification.compilation.mustSucceed" "true")

    if [[ "$must_compile" == "true" ]]; then
        if ! verify_compilation "$workspace" "true"; then
            log_debug "Compilation verification failed"
            return 1
        fi
        log_debug "Compilation verification passed"
    fi

    # Check Z3 verification
    local z3_enabled
    z3_enabled=$(json_get_nested "$task_json" "verification.z3.enabled" "false")

    if [[ "$z3_enabled" == "true" ]]; then
        local min_proven max_disproven
        min_proven=$(json_get_nested "$task_json" "verification.z3.minProvenContracts" "0")
        max_disproven=$(json_get_nested "$task_json" "verification.z3.maxDisprovenContracts" "999")

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
