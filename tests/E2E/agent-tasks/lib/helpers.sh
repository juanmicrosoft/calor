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
COMPILER="$REPO_ROOT/src/Calor.Compiler/bin/Debug/net10.0/calor"
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

    # Create comprehensive CLAUDE.md with Calor syntax reference for the agent
    cat > "$workspace/CLAUDE.md" << 'CALOR_REFERENCE'
## Calor Syntax Reference

This is a Calor project. Write code in `.calr` files.

**Tip:** If you encounter unfamiliar Calor syntax, use these MCP tools:
- `calor_syntax_help` — get syntax reference for a feature (e.g., "events", "pattern-matching", "async")
- `calor_syntax_lookup` — look up Calor equivalent for any C# construct (e.g., "switch expression", "event handler")

### Quick Reference

| Syntax | Purpose | C# Equivalent |
|--------|---------|---------------|
| `§M{id:Name}...§/M{id}` | Module | `namespace` |
| `§F{id:Name:pub}...§/F{id}` | Function | `static method` |
| `§I{type:name}` | Input parameter | method parameter |
| `§O{type}` | Output/return type | return type |
| `§R expr` | Return | `return expr;` |
| `§Q (cond)` / `§S (cond)` | Pre/postcondition | assert/contract |
| `§E{cw,fs:w}` | Effects (comma-separated, ONE line) | — |
| `§B{type:name} value` | Bind variable | `type name = value;` |
| `§ASSIGN name expr` | Assignment | `name = expr;` |
| `§IF{id} cond → action` / `§EL` / `§/I{id}` | Conditional | `if/else` |
| `§L{id:i:start:end:step}...§/L{id}` | For loop | `for` |
| `§WH{id} cond...§/WH{id}` | While loop | `while` |
| `§R §W{id} expr §K val → result §K _ → default §/W{id}` | Switch expression | `switch` |
| `§CL{id:Name:pub}...§/CL{id}` | Class | `class` |
| `§MT{id:Name:pub}...§/MT{id}` | Method | instance method |
| `§AF{id:Name:pub} §O{Task<T>}...§/AF{id}` | Async function | `async Task<T>` |
| `§AMT{id:Name:pub}...§/AMT{id}` | Async method (in class) | `async` method |
| `§AWAIT §C{Method} §/C` | Await call | `await Method()` |
| `§C{Method} §A arg §/C` | Method call | `Method(arg)` |
| `§LIST{name:type}§/LIST{name}` | Create list | `new List<T>()` |
| `§PUSH{name} val` | Add to list | `.Add(val)` |
| `§DICT{name:K:V}§/DICT{name}` | Create dictionary | `new Dictionary<K,V>()` |
| `§PUT{name} key val` | Add to dictionary | `[key] = val` |
| `§EACH{id:var} coll...§/EACH{id}` | Foreach | `foreach` |
| `§EACHKV{id:k:v} dict...§/EACHKV{id}` | Foreach key-value | `foreach (var (k,v))` |
| `§CNT{name}` | Count | `.Count` |
| `§SM value` / `§NN` | Some/None | `Some(v)` / `None` |
| `§OK value` / `§ERR msg` | Ok/Error | `Ok(v)` / `Err(msg)` |
| `§EVT{id:Name:pub:Type}` | Event | `event Type Name` |
| `§SUB event handler` | Subscribe | `event += handler` |
| `§LAM{id:param:type} expr §/LAM{id}` | Lambda (use inside §B) | `(param) => expr` |
| `§DEL{id:Name}...§/DEL{id}` | Delegate | `delegate` |
| `§P value` | Print | `Console.WriteLine` |
| `(+ a b)`, `(== a b)`, `(? c t f)` | Expressions (prefix) | `a + b`, `a == b`, `c ? t : f` |

## Contract-First Methodology

**Before writing implementation, extract and express constraints as contracts.** This is your primary tool for ensuring correctness.

### Step 1: Identify Input Constraints (Preconditions)

Read the requirement and find ALL constraints on inputs:
- "must be positive" → `§Q (> n 0)`
- "must not be zero" → `§Q (!= n 0)`
- "X must not exceed Y" → `§Q (<= x y)`
- Any division or modulo operation → `§Q (!= divisor 0)`

### Step 2: Identify Output Guarantees (Postconditions)

Find what the function promises about its result:
- "result is never negative" → `§S (>= result 0)`
- "result is at least 1" → `§S (>= result 1)`
- "result is bounded by input" → `§S (<= result n)`

### Step 3: Write Contracts BEFORE Implementation

The contracts become your specification. **If you can't satisfy a postcondition, your implementation is wrong.**

```calor
// FIRST: Write the contracts
§F{f001:MyFunction:pub}
  §I{i32:n}
  §O{i32}
  §Q (> n 0)           // Input constraint from requirement
  §S (>= result 0)     // Output guarantee from requirement
  // THEN: Implement logic that satisfies the contracts
  §R ...
§/F{f001}
```

### Common Contract Patterns

| Requirement Pattern | Calor Contract |
|---------------------|----------------|
| "must be positive" | `§Q (> n 0)` |
| "must be non-negative" | `§Q (>= n 0)` |
| "must not be zero" | `§Q (!= n 0)` |
| "must be between X and Y (inclusive)" | `§Q (>= n X)` and `§Q (<= n Y)` |
| "must be even" | `§Q (== (% n 2) 0)` |
| "X must not exceed Y" | `§Q (<= x y)` |
| division or modulo by Y | Always `§Q (!= y 0)` |
| "result is never negative" | `§S (>= result 0)` |
| "result is always positive" | `§S (> result 0)` |
| "result is at least 1" | `§S (>= result 1)` |
| "result is bounded by input" | `§S (<= result n)` |

### Self-Verification with Contracts

Use contracts to verify your implementation:
- **If you can't make a postcondition true** → Your implementation is wrong
- **If a precondition seems impossible** → You misunderstood the requirement
- **If contracts conflict** → The requirement has contradictions

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
- Implication in postcondition: `§S (-> (>= x 0) (== result x))` — use `->` operator (NOT `implies`)
- Multiple contracts: Add multiple §Q or §S lines

### Effects Declaration
Effects declare what side-effects a function may have:
- `cw` = console write (Console.WriteLine, etc.)
- `cr` = console read
- `fs` = file system access (append `:r` for read, `:w` for write, `:rw` for both)
- `net` = network access (append `:r` for read, `:w` for write, `:rw` for both)
- `db` = database access

Effect modes: `§E{fs:r}` (read), `§E{fs:w}` (write), `§E{fs:rw}` (read-write), `§E{net:rw}` (network read-write)

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
External method calls AND internal function calls use §C (call) sections:
```
§C{Console.WriteLine}
  §A "Hello"
§/C
```

**IMPORTANT: Function calls in expressions**
When calling a function inside an expression (e.g., in a ternary or return), you MUST use §C{...} syntax:
```
§R (? §C{ValidateIndex}
    §A index
    §A length
  §/C index (- 0 1))
```

WRONG (will not compile):
```
§R (? (ValidateIndex index length) index (- 0 1))
```

Function names are NOT operators - always use §C{FunctionName} with §A arguments.

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

**Option with char parsing (TryParseDigit pattern):**
```
§F{f001:TryParseDigit:pub}
  §I{char:c}
  §O{Option<i32>}
  §IF{if1} (and (>= c '0') (<= c '9'))
    §R §SM (- c '0')
  §EL
    §R §NN
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

### Control Flow

**Arrow syntax (single statement per branch):**
```
§IF{id} condition → action
§EI condition → action
§EL → action
§/I{id}
```

**Block syntax (multiple statements per branch):**
```
§IF{id} condition
  statement1
  statement2
§EI condition
  statement3
§EL
  statement4
§/I{id}
```

**Example - Arrow syntax (Classify function):**
```
§F{f001:Classify:pub}
  §I{i32:n}
  §O{str}
  §IF{if1} (< n 0) → §R "negative"
  §EI (== n 0) → §R "zero"
  §EL → §R "positive"
  §/I{if1}
§/F{f001}
```

**Example - Arrow syntax (Max function):**
```
§F{f001:Max:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §IF{if1} (>= a b) → §R a
  §EL → §R b
  §/I{if1}
§/F{f001}
```

**PREFER arrow syntax** `→` (or `->`) for simple conditionals with single return statements.

**Example - Block syntax (GetGrade function):**
```
§F{f001:GetGrade:pub}
  §I{i32:score}
  §O{str}
  §B{str:grade} "F"
  §IF{if1} (>= score 90)
    §ASSIGN grade "A"
  §EI (>= score 80)
    §ASSIGN grade "B"
  §EI (>= score 70)
    §ASSIGN grade "C"
  §EL
    §ASSIGN grade "F"
  §/I{if1}
  §R grade
§/F{f001}
```

### While Loops

**While loop syntax:**
```
§WH{id} condition
  ...body...
§/WH{id}
```

**Example - Countdown:**
```
§F{f001:Countdown:pub}
  §O{void}
  §E{cw}
  §B{i32:count} 5
  §WH{wh1} (> count 0)
    §C{Console.WriteLine}
      §A count
    §/C
    §ASSIGN count (- count 1)
  §/WH{wh1}
§/F{f001}
```

Key while loop syntax:
- `§WH{id} condition` — begin while loop with unique id and boolean condition
- `§/WH{id}` — end while loop (id MUST match opening tag)
- Use `§B` before the loop to declare the loop variable
- Use `§ASSIGN` inside the loop body to update the variable

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
  §B{i32:sum} 0
  §EACH{e1:n} nums
    §ASSIGN sum (+ sum n)
  §/EACH{e1}
  §R sum
§/F{f001}
```

**Function returning collection count:**
```
§F{f001:GetListLength:pub}
  §I{i32:n}
  §O{i32}
  §Q (>= n 0)
  §LIST{nums:i32}
  §/LIST{nums}
  §L{for1:i:1:n:1}
    §PUSH{nums} i
  §/L{for1}
  §R §CNT{nums}
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

**Function taking a Dictionary parameter and iterating it:**
```
§F{f001:PrintDictionary:pub}
  §I{Dictionary<str,i32>:scores}
  §O{void}
  §E{cw}
  §EACHKV{e1:k:v} scores
    §P k
    §P v
  §/EACHKV{e1}
§/F{f001}
```

**HashSet - create, add values:**
```
§HSET{unique:i32}       // Create HashSet<int>
§/HSET{unique}          // Close (empty)
§ADD{unique} 1          // Add to set (use §ADD for sets)
§ADD{unique} 2          // Add another value
§ADD{unique} 2          // Duplicates ignored
§HAS{unique} 1          // Check membership
§CNT{unique}            // Get count (returns 2)
```

### Async Functions

**Simple async function (no await needed):**
```
§AF{af1:GetDataAsync:pub}
  §O{Task<i32>}
  §R 42
§/AF{af1}
```

**Async function with await:**
```
§AF{af1:ProcessAsync:pub}
  §O{Task<i32>}
  §E{net:r}
  §B{i32:data} §AWAIT §C{FetchDataAsync} §/C
  §R (* data 2)
§/AF{af1}
```

Key points:
- Use `§AF{id:Name:pub}` for async function (not `§F`)
- **ALWAYS use `§O{Task<T>}` for return type** (e.g., `§O{Task<i32>}`, `§O{Task<str>}`)
- Use `§AWAIT` before async calls: `§AWAIT §C{MethodAsync} §A arg §/C`
- `§E{net:r}` declares network read effect

**ConfigureAwait(false) for library code:**
```
§AF{af1:BackgroundProcessAsync:pub}
  §O{Task<i32>}
  §E{net:r}
  §B{i32:result} §AWAIT{false} §C{SlowOperationAsync} §/C
  §R result
§/AF{af1}
```

Use `§AWAIT{false}` to add ConfigureAwait(false) for library code.

**Async method in a class:**
```
§CL{cl1:DataService:pub}
  §AMT{amt1:LoadAsync:pub}
    §O{Task<i32>}
    §E{net:rw}
    §B{i32:data} §AWAIT §C{HttpClient.GetAsync} §/C
    §R 100
  §/AMT{amt1}
§/CL{cl1}
```

Use `§AMT{` for async method in class (not `§AF{` or `§MT{`).
Note: HttpClient methods typically need `§E{net:rw}` (read-write), not just `§E{net:r}`.

### Switch / Pattern Matching

**Switch expression (return + arrow syntax):**
Use `§R §W{id} target` to switch and return the result. Cases use `→` (arrow) with just the value (no `§R` inside cases):
```
§F{f001:GetStatus:pub}
  §I{i32:code}
  §O{str}
  §R §W{sw1} code
    §K 200 → "OK"
    §K 404 → "Not Found"
    §K 500 → "Server Error"
    §K _ → "Unknown"
  §/W{sw1}
§/F{f001}
```

**Complete function with switch (DayName example):**
```
§F{f001:DayName:pub}
  §I{i32:day}
  §O{i32}
  §R §W{sw1} day
    §K 1 → 100
    §K 2 → 100
    §K 3 → 100
    §K 4 → 100
    §K 5 → 100
    §K 6 → 200
    §K 7 → 200
    §K _ → 0
  §/W{sw1}
§/F{f001}
```

**Switch statement (block syntax with explicit §R):**
Use `§W{id} target` without `§R` prefix. Each case uses block form:
```
§W{sw1} n
  §K 0
    §R "zero"
  §K 1
    §R "one"
  §K _
    §R "other"
§/W{sw1}
```

Key switch syntax:
- `§R §W{id} target` - switch expression (return inline, cases use `→`)
- `§W{id} target` - switch statement (cases use block with explicit `§R`)
- `§K value → result` - case with arrow (NO `§R` — arrow implies return)
- `§K value` (block) - case start, followed by statements on next lines
- `§K _` - wildcard/default case (MUST have space before `_`)
- `§/W{id}` - close switch

**Relational patterns with §PREL:**
```
§F{f001:GetGrade:pub}
  §I{i32:score}
  §O{str}
  §R §W{sw1} score
    §K §PREL{gte} 90 → "A"
    §K §PREL{gte} 80 → "B"
    §K §PREL{gte} 70 → "C"
    §K _ → "F"
  §/W{sw1}
§/F{f001}
```

Relational operators: `gte` (>=), `gt` (>), `lte` (<=), `lt` (<)

**Variable pattern with guard (§VAR + §WHEN):**
```
§F{f001:Describe:pub}
  §I{i32:x}
  §O{str}
  §R §W{sw1} x
    §K §VAR{n} §WHEN (> n 100) → "large"
    §K §VAR{n} §WHEN (< n 0) → "negative"
    §K 0 → "zero"
    §K _ → "normal"
  §/W{sw1}
§/F{f001}
```

**Boolean result switch (IsSingleDigit example):**
```
§F{f001:IsSingleDigit:pub}
  §I{i32:n}
  §O{bool}
  §R §W{sw1} n
    §K 0 → true
    §K 1 → true
    §K 2 → true
    §K 3 → true
    §K 4 → true
    §K 5 → true
    §K 6 → true
    §K 7 → true
    §K 8 → true
    §K 9 → true
    §K _ → false
  §/W{sw1}
§/F{f001}
```

### Events

**Event declaration in a class:**
```
§CL{cl1:EventPublisher:pub}
  §EVT{ev1:OnDataReceived:pub:EventHandler}
  §MT{mt1:Subscribe:pub}
    §I{EventHandler:handler}
    §O{void}
    §SUB OnDataReceived handler
  §/MT{mt1}
§/CL{cl1}
```

Event syntax:
- `§EVT{id:Name:visibility:DelegateType}` - declare event
- `§SUB eventName handler` - subscribe handler to event (`+=`)
- `§UNSUB eventName handler` - unsubscribe handler from event (`-=`)

### Lambdas and Delegates

**Function using inline arrow lambda:**
```
§F{f001:ApplyDouble:pub}
  §I{i32:x}
  §O{i32}
  §B{Func<i32,i32>:doubler} (n) → (* n 2)
  §R §C{doubler} §A x §/C
§/F{f001}
```

Key lambda syntax:
- `(param) → expression` for inline arrow lambda
- `(a, b) → (+ a b)` for multiple params
- Use `§B{type:name} lambda` to bind lambda to variable

**Block lambda (named, reusable):**
```
§F{f001:ApplyComplex:pub}
  §I{i32:x}
  §O{i32}
  §B{Func<i32,i32>:process} §LAM{lam1:n:i32}
    (? (> n 0) (* n 2) 0)
  §/LAM{lam1}
  §R §C{process} §A x §/C
§/F{f001}
```

Block lambda syntax:
- `§B{type:name} §LAM{id:param:paramType} body §/LAM{id}` - bind lambda to variable
- Lambda body is an expression (use ternary `(? cond then else)` for conditionals)
- Call with `§C{name} §A arg §/C`

**Delegate definition:**
```
§DEL{del1:MathOperation}
  §I{i32:x}
  §I{i32:y}
  §O{i32}
§/DEL{del1}
```

Delegate syntax: `§DEL{id:Name}...§/DEL{id}`

### Variable Binding and Assignment

**Mutable variables:**
```
§F{f001:Accumulate:pub}
  §I{i32:n}
  §O{i32}
  §B{i32:acc} 0           // Bind variable acc = 0
  §ASSIGN acc (+ acc n)   // Assign acc = acc + n
  §ASSIGN acc (+ acc n)   // Assign again
  §ASSIGN acc (+ acc n)   // And again
  §R acc
§/F{f001}
```

Key syntax:
- `§B{type:name} value` - bind (declare) mutable variable (type first, then name)
- `§ASSIGN name expression` - assign new value to variable

### Interfaces

**Interface definition:**
```
§IFACE{i001:IRepository}
  §MT{m001:GetById}
    §I{i32:id}
    §O{Option<Entity>}
  §/MT{m001}
  §MT{m002:Save}
    §I{Entity:entity}
    §O{void}
  §/MT{m002}
§/IFACE{i001}
```

Interface syntax: `§IFACE{id:Name}...§/IFACE{id}`
- Methods have signatures only (no body, no `§R`)
- Use `§MT{id:Name}` for method signatures (no visibility needed in interface)

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

**Generic class with type parameter:**
```
§CL{cl1:Container:pub}<T>
  §FLD{_value:T:priv}
  §MT{mt1:Get:pub}
    §O{T}
    §R _value
  §/MT{mt1}
  §MT{mt2:Set:pub}
    §I{T:value}
    §O{void}
    §ASSIGN _value value
  §/MT{mt2}
§/CL{cl1}
```

**Generic class with type constraint:**
```
§CL{cl1:Repository:pub}<T>
  §WHERE T : class
  §FLD{List<T>:_items:priv}
  §MT{mt1:Add:pub}
    §I{T:item}
    §O{void}
    §C{_items.Add} §A item §/C
  §/MT{mt1}
§/CL{cl1}
```

Type constraint syntax: `§WHERE T : constraint`
- `§WHERE T : class` - reference type
- `§WHERE T : struct` - value type
- `§WHERE T : new()` - has parameterless constructor
- `§WHERE T : IComparable<T>` - implements interface

### Properties

**Property with getter and setter:**
```
§CL{cl1:Product:pub}
  §FLD{f64:_price:priv}
  §PROP{p001:Price:f64:pub}
    §GET
      §R _price
    §/GET
    §SET
      §ASSIGN _price value
    §/SET
  §/PROP{p001}
§/CL{cl1}
```

**Auto-property (simple getter/setter):**
```
§CL{cl1:Person:pub}
  §PROP{p001:Name:str:pub}
    §GET
    §SET
  §/PROP{p001}
§/CL{cl1}
```

Property syntax: `§PROP{id:Name:type:visibility}...§/PROP{id}`

### Constructors

**Constructor in a class:**
```
§CL{cl1:Person:pub}
  §FLD{str:_name:priv}
  §FLD{i32:_age:priv}
  §CTOR{ctor1:pub}
    §I{str:name}
    §I{i32:age}
    §ASSIGN _name name
    §ASSIGN _age age
  §/CTOR{ctor1}
  §MT{mt1:GetName:pub}
    §O{str}
    §R _name
  §/MT{mt1}
§/CL{cl1}
```

Constructor syntax: `§CTOR{id:visibility}...§/CTOR{id}`

### Multiple Postconditions (Strengthened Contracts)

When fully specifying behavior, use multiple §S postconditions:
```
§F{f001:AbsoluteValue:pub}
  §I{i32:x}
  §O{i32}
  §S (>= result 0)
  §S (-> (>= x 0) (== result x))
  §S (-> (< x 0) (== result (- 0 x)))
  §R (? (< x 0) (- 0 x) x)
§/F{f001}
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

**Multiple effects (comma-separated in ONE §E declaration):**
```
§E{cw,fs:w}             // Console write AND file write
§E{net,cw}              // Network AND console write
```

**IMPORTANT:** Always combine multiple effects in a SINGLE `§E{}` with commas. Do NOT use multiple `§E` lines.

**Function with multiple effects:**
```
§F{f001:LogToFileAndConsole:pub}
  §I{str:path}
  §I{str:message}
  §O{void}
  §E{cw,fs:w}
  §C{Console.WriteLine}
    §A message
  §/C
  §C{File.AppendAllText}
    §A path
    §A message
  §/C
§/F{f001}
```

### StringBuilder Operations

StringBuilder uses **functional-style calls** (NOT method calls like `§C{sb.Append}`):

**Create and use StringBuilder:**
```
§F{f001:BuildGreeting:pub}
  §I{str:name}
  §O{str}
  §B{StringBuilder:sb} (sb-new)
  (sb-append sb "Hello, ")
  (sb-append sb name)
  (sb-append sb "!")
  §R (sb-tostring sb)
§/F{f001}
```

StringBuilder functions (Lisp-style, NOT method calls):
- `(sb-new)` - create new StringBuilder
- `(sb-append sb value)` - append value to StringBuilder
- `(sb-tostring sb)` - convert to string

WRONG: `§C{sb.Append} §A "text" §/C` — do NOT use method call syntax for StringBuilder

### Array Types

Use `[type]` for array types:
```
§I{[i32]:arr}           // Integer array parameter
§I{[str]:names}         // String array parameter
§I{[[i32]]:matrix}      // Nested array (2D)
```

### Quantifiers (for contracts)

**Forall quantifier - all elements positive:**
```
§F{f001:AllPositive:pub}
  §I{[i32]:arr}
  §O{bool}
  §S (-> result (forall ((i i32)) (> arr{i} 0)))
  §B{bool:allPos} true
  §L{for1:i:0:(- (len arr) 1):1}
    §IF{if1} (<= arr{i} 0) → §ASSIGN allPos false
    §/I{if1}
  §/L{for1}
  §R allPos
§/F{f001}
```

**Exists quantifier - has negative element:**
```
§F{f001:HasNegative:pub}
  §I{[i32]:arr}
  §O{bool}
  §S (== result (exists ((i i32)) (< arr{i} 0)))
  §L{for1:i:0:(- (len arr) 1):1}
    §IF{if1} (< arr{i} 0) → §R true
    §/I{if1}
  §/L{for1}
  §R false
§/F{f001}
```

Key quantifier syntax:
- `(forall ((var type)) body)` - universal quantifier with typed variable
- `(exists ((var type)) body)` - existential quantifier with typed variable
- `(-> condition consequence)` - implication (if condition then consequence)
- `arr{i}` - array element access at index i
- `(len arr)` - array length

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
verify_calor_compilation() {
    local workspace="$1"
    local must_succeed="${2:-true}"

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
        compile_output=$("$COMPILER" --input "$calr_file" --output "$cs_file" 2>&1) || compile_status=$?

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

    local project_type
    project_type=$(detect_project_type "$workspace")

    log_debug "Detected project type: $project_type"

    case "$project_type" in
        calor)
            verify_calor_compilation "$workspace" "$must_succeed"
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
        if ! verify_compilation "$workspace" "true"; then
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
