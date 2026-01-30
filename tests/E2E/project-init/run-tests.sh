#!/usr/bin/env bash
set -euo pipefail

# OPAL Project Init E2E Test Runner
# Tests `opalc init` against real GitHub projects using analyze-driven file selection

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
WORK_DIR="${OPAL_TEST_WORKDIR:-/tmp/opal-e2e-project-init}"
COMPILER="$REPO_ROOT/src/Opal.Compiler/bin/Debug/net8.0/opalc"
RUNTIME_PROJ="$REPO_ROOT/src/Opal.Runtime/Opal.Runtime.csproj"

# Configuration
MIN_SCORE="${OPAL_MIN_SCORE:-30}"           # Minimum analyze score for conversion
MAX_FILES="${OPAL_MAX_FILES:-5}"             # Maximum files to convert per project
CLONE_TIMEOUT="${OPAL_CLONE_TIMEOUT:-120}"   # Seconds to wait for clone

# Colors
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

PASSED=0
FAILED=0
SKIPPED=0

info() { echo -e "${BLUE}[INFO]${NC} $1"; }
pass() { echo -e "${GREEN}[PASS]${NC} $1"; ((PASSED++)) || true; }
fail() { echo -e "${RED}[FAIL]${NC} $1"; ((FAILED++)) || true; }
skip() { echo -e "${YELLOW}[SKIP]${NC} $1"; ((SKIPPED++)) || true; }
step() { echo -e "  ${CYAN}→${NC} $1"; }
detail() { echo -e "    $1"; }

# Build the compiler and runtime
build_compiler() {
    info "Building OPAL compiler and runtime..."
    dotnet build "$REPO_ROOT/src/Opal.Compiler/Opal.Compiler.csproj" -c Debug --nologo -v q || {
        echo "Failed to build compiler"
        exit 1
    }
    dotnet build "$RUNTIME_PROJ" -c Debug --nologo -v q || {
        echo "Failed to build runtime"
        exit 1
    }
    info "Compiler and runtime built successfully"
    echo ""
}

# Clean up work directory
cleanup() {
    if [[ -d "$WORK_DIR" ]]; then
        info "Cleaning up $WORK_DIR..."
        rm -rf "$WORK_DIR"
    fi
}

# Clone a GitHub project (shallow clone for speed)
clone_project() {
    local repo_url="$1"
    local project_dir="$2"
    local branch="${3:-}"

    if [[ -d "$project_dir" ]]; then
        step "Using cached clone: $project_dir"
        return 0
    fi

    step "Cloning $repo_url..."
    local clone_args="--depth 1"
    if [[ -n "$branch" ]]; then
        clone_args="$clone_args --branch $branch"
    fi

    # Use timeout if available, otherwise just run git clone directly
    if command -v timeout > /dev/null 2>&1; then
        if ! timeout "$CLONE_TIMEOUT" git clone $clone_args "$repo_url" "$project_dir" 2>&1; then
            echo "Failed to clone $repo_url"
            return 1
        fi
    else
        if ! git clone $clone_args "$repo_url" "$project_dir" 2>&1; then
            echo "Failed to clone $repo_url"
            return 1
        fi
    fi
}

# Add Opal.Runtime reference to a project (using local project reference)
add_opal_runtime() {
    local csproj="$1"
    dotnet add "$csproj" reference "$RUNTIME_PROJ" > /dev/null 2>&1 || true
}

# Update .csproj to use the local compiler path instead of global opalc
use_local_compiler() {
    local csproj="$1"
    if command -v sed > /dev/null; then
        sed -i.bak "s|>opalc<|>$COMPILER<|g" "$csproj"
        rm -f "${csproj}.bak"
    fi
}

# Analyze a project and return files suitable for conversion
# Returns file paths that meet the threshold (one per line)
analyze_project() {
    local project_dir="$1"
    local threshold="$2"
    local max_files="$3"

    # Run analyze with JSON output
    # Note: analyze may return exit code 1 even on success, so we capture output regardless
    local json_output
    json_output=$("$COMPILER" analyze "$project_dir" --format json --threshold "$threshold" 2>/dev/null) || true

    # Extract file paths from JSON (files with score >= threshold)
    # Using grep and sed for portability (no jq dependency)
    echo "$json_output" | grep -o '"path": "[^"]*"' | sed 's/"path": "//;s/"$//' | head -n "$max_files"
}

# Count files returned by analyze
count_analyzed_files() {
    local project_dir="$1"
    local threshold="$2"

    local files
    files=$(analyze_project "$project_dir" "$threshold" 1000)
    if [[ -z "$files" ]]; then
        echo "0"
    else
        echo "$files" | wc -l | tr -d ' '
    fi
}

# Convert a single C# file to OPAL
convert_file() {
    local cs_file="$1"
    local opal_file="${cs_file%.cs}.opal"

    if "$COMPILER" convert "$cs_file" > /dev/null 2>&1; then
        if [[ -f "$opal_file" ]]; then
            return 0
        fi
    fi
    return 1
}

# Test a real GitHub project
# Args: test_name repo_url project_subdir [branch]
test_github_project() {
    local test_name="$1"
    local repo_url="$2"
    local project_subdir="$3"
    local branch="${4:-}"

    local test_dir="$WORK_DIR/$test_name"
    local project_dir="$test_dir/repo"
    local src_dir="$project_dir/$project_subdir"

    echo -e "\n${BOLD}${BLUE}Test: $test_name${NC}"
    echo -e "${CYAN}  Repository: $repo_url${NC}"
    echo -e "${CYAN}  Source dir: $project_subdir${NC}"

    mkdir -p "$test_dir"

    # Step 1: Clone
    if ! clone_project "$repo_url" "$project_dir" "$branch"; then
        fail "$test_name - failed to clone repository"
        return 0
    fi

    # Remove global.json to avoid SDK version conflicts
    if [[ -f "$project_dir/global.json" ]]; then
        step "Removing global.json (SDK version constraint)..."
        rm "$project_dir/global.json"
    fi

    # Remove version.json to disable Nerdbank.GitVersioning (doesn't work with shallow clones)
    if [[ -f "$project_dir/version.json" ]]; then
        step "Removing version.json (Nerdbank.GitVersioning conflicts with shallow clones)..."
        rm "$project_dir/version.json"
    fi

    # Patch target frameworks to net8.0 if they target newer versions
    step "Patching target frameworks to net8.0..."
    find "$project_dir" -name "*.csproj" -exec sed -i.bak \
        -e 's/<TargetFramework>net9\.0</<TargetFramework>net8.0</g' \
        -e 's/<TargetFramework>net10\.0</<TargetFramework>net8.0</g' \
        -e 's/net9\.0;/net8.0;/g' \
        -e 's/net10\.0;/net8.0;/g' \
        -e 's/;net9\.0/;net8.0/g' \
        -e 's/;net10\.0/;net8.0/g' \
        {} \;
    find "$project_dir" -name "*.csproj.bak" -delete 2>/dev/null || true

    # Remove Polyfill package references (uses C# 13 extension blocks that don't compile)
    # Also remove project references to Analyzer projects that depend on Polyfill
    step "Removing Polyfill dependencies (C# 13 incompatibility)..."
    find "$project_dir" -name "*.csproj" -exec sed -i.bak \
        -e '/<PackageReference Include="Polyfill"/d' \
        -e '/<ProjectReference.*Analyzer.*\.csproj/d' \
        {} \;
    find "$project_dir" -name "*.csproj.bak" -delete 2>/dev/null || true

    # Step 2: Analyze to find conversion candidates
    step "Analyzing project for OPAL conversion candidates (threshold: $MIN_SCORE)..."
    local candidate_count
    candidate_count=$(count_analyzed_files "$src_dir" "$MIN_SCORE")

    if [[ "$candidate_count" -eq 0 ]]; then
        fail "$test_name - no files found with score >= $MIN_SCORE"
        detail "Try lowering OPAL_MIN_SCORE or check if project has suitable files"
        return 0
    fi

    detail "Found $candidate_count files with score >= $MIN_SCORE"

    # Get the files to convert (limited by MAX_FILES)
    local files_to_convert
    files_to_convert=$(analyze_project "$src_dir" "$MIN_SCORE" "$MAX_FILES")
    local convert_count
    convert_count=$(echo "$files_to_convert" | wc -l | tr -d ' ')
    detail "Will convert $convert_count files (max: $MAX_FILES)"

    # Step 3: Find the main .csproj file
    step "Finding project file..."
    local csproj_file
    csproj_file=$(find "$src_dir" -maxdepth 2 -name "*.csproj" ! -name "*Test*" ! -name "*Tests*" ! -name "*Benchmark*" | head -1)

    if [[ -z "$csproj_file" ]]; then
        # Try finding any csproj
        csproj_file=$(find "$project_dir" -name "*.csproj" ! -name "*Test*" ! -name "*Tests*" | head -1)
    fi

    if [[ -z "$csproj_file" ]]; then
        fail "$test_name - no .csproj file found"
        return 0
    fi
    detail "Using: $(basename "$csproj_file")"

    local csproj_dir
    csproj_dir=$(dirname "$csproj_file")

    # Step 4: Run opalc init
    step "Running opalc init --ai claude..."
    cd "$csproj_dir"
    if ! "$COMPILER" init --ai claude --project "$(basename "$csproj_file")" > /dev/null 2>&1; then
        fail "$test_name - opalc init failed"
        return 0
    fi

    # Use local compiler for builds
    use_local_compiler "$(basename "$csproj_file")"

    # Add runtime reference
    add_opal_runtime "$(basename "$csproj_file")"

    # Step 5: Convert files
    step "Converting $convert_count C# files to OPAL..."
    local converted=0
    local failed_conversions=0
    local converted_files=()

    while IFS= read -r rel_path; do
        local full_path="$src_dir/$rel_path"
        if [[ -f "$full_path" ]]; then
            if convert_file "$full_path"; then
                ((converted++)) || true
                converted_files+=("$full_path")
                detail "✓ Converted: $rel_path"
            else
                ((failed_conversions++)) || true
                detail "✗ Failed: $rel_path"
            fi
        fi
    done <<< "$files_to_convert"

    if [[ $converted -eq 0 ]]; then
        fail "$test_name - no files could be converted"
        return 0
    fi
    detail "Successfully converted $converted/$convert_count files"

    # Step 6: Delete original .cs files that were converted
    step "Removing converted .cs files..."
    for cs_file in "${converted_files[@]}"; do
        if [[ -f "$cs_file" ]]; then
            rm "$cs_file"
        fi
    done

    # Step 7: Build the project
    step "Building project with OPAL files..."
    cd "$csproj_dir"
    if ! dotnet build --nologo -v q -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false 2>&1; then
        fail "$test_name - dotnet build failed after conversion"
        return 0
    fi
    detail "Build succeeded"

    # Step 8: Verify generated files exist
    step "Verifying generated .g.cs files..."
    local gen_count
    gen_count=$(find . -path "*/opal/*.g.cs" 2>/dev/null | wc -l | tr -d ' ')
    if [[ "$gen_count" -eq 0 ]]; then
        fail "$test_name - no generated files found in obj/*/opal/"
        return 0
    fi
    detail "Found $gen_count generated files"

    # Step 9: Run tests (if test project exists)
    step "Looking for test projects..."
    local test_csproj
    test_csproj=$(find "$project_dir" -name "*Test*.csproj" -o -name "*Tests*.csproj" 2>/dev/null | head -1)

    if [[ -n "$test_csproj" ]]; then
        detail "Found: $(basename "$test_csproj")"
        step "Running tests..."
        cd "$(dirname "$test_csproj")"

        # Add runtime reference to test project too
        add_opal_runtime "$(basename "$test_csproj")"

        if dotnet test --nologo -v q --no-build 2>&1; then
            detail "Tests passed"
        else
            # Try building and running tests
            if dotnet test --nologo -v q -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false 2>&1; then
                detail "Tests passed (after rebuild)"
            else
                fail "$test_name - tests failed"
                return 0
            fi
        fi
    else
        detail "No test project found, skipping tests"
    fi

    # Step 10: Test clean/rebuild cycle
    step "Testing clean/rebuild cycle..."
    cd "$csproj_dir"
    dotnet clean --nologo -v q > /dev/null 2>&1
    if ! dotnet build --nologo -v q -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false 2>&1; then
        fail "$test_name - rebuild after clean failed"
        return 0
    fi
    detail "Clean/rebuild succeeded"

    pass "$test_name (converted $converted files)"
}

# Simple local tests (no GitHub clone)
test_basic_console_app() {
    local test_name="basic-console-app"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BOLD}${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    step "Creating console project..."
    dotnet new console --name TestApp --output . -f net8.0 > /dev/null 2>&1
    add_opal_runtime "TestApp.csproj"

    step "Running opalc init --ai claude..."
    if ! "$COMPILER" init --ai claude 2>&1 | grep -q "Initialized"; then
        fail "$test_name - opalc init failed"
        return 0
    fi
    use_local_compiler "TestApp.csproj"

    step "Verifying .csproj changes..."
    if ! grep -q "CompileOpalFiles" TestApp.csproj; then
        fail "$test_name - CompileOpalFiles target not found"
        return 0
    fi

    step "Creating test.opal file..."
    cat > test.opal << 'OPAL_EOF'
§M{m001:TestModule}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}
OPAL_EOF

    cat > Program.cs << 'CS_EOF'
var result = TestModule.TestModuleModule.Add(21, 21);
Console.WriteLine($"21 + 21 = {result}");
CS_EOF

    step "Building project..."
    if ! dotnet build --nologo -v q -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false 2>&1; then
        fail "$test_name - dotnet build failed"
        return 0
    fi

    step "Verifying generated files..."
    if ! find obj -name "test.g.cs" 2>/dev/null | grep -q .; then
        fail "$test_name - generated file not found"
        return 0
    fi

    step "Running the app..."
    local output
    output=$(dotnet run --no-build 2>&1) || true
    if [[ "$output" != *"21 + 21 = 42"* ]]; then
        fail "$test_name - unexpected output: $output"
        return 0
    fi

    step "Testing clean/rebuild..."
    dotnet clean --nologo -v q > /dev/null 2>&1
    if ! dotnet build --nologo -v q -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false 2>&1; then
        fail "$test_name - rebuild failed"
        return 0
    fi

    pass "$test_name"
}

test_multiple_projects_detection() {
    local test_name="multiple-projects-detection"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BOLD}${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    step "Creating multiple projects..."
    cat > Project1.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF
    cat > Project2.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF

    step "Running opalc init without --project (should fail)..."
    local output
    output=$("$COMPILER" init --ai claude 2>&1) || true
    if [[ "$output" != *"Multiple"* ]]; then
        fail "$test_name - should have failed with multiple projects"
        return 0
    fi
    detail "Correctly detected multiple projects"

    step "Running opalc init with --project..."
    if ! "$COMPILER" init --ai claude --project Project1.csproj > /dev/null 2>&1; then
        fail "$test_name - opalc init with --project failed"
        return 0
    fi

    if ! grep -q "CompileOpalFiles" Project1.csproj; then
        fail "$test_name - Project1 not modified"
        return 0
    fi
    if grep -q "CompileOpalFiles" Project2.csproj; then
        fail "$test_name - Project2 incorrectly modified"
        return 0
    fi

    pass "$test_name"
}

# Test the full analyze → init → convert → build pipeline with a controlled project
test_full_pipeline() {
    local test_name="full-pipeline"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BOLD}${BLUE}Test: $test_name${NC}"
    echo -e "${CYAN}  Tests full: analyze → init → convert → build pipeline${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    step "Creating project with convertible C# files..."
    dotnet new console --name PipelineTest --output . -f net8.0 > /dev/null 2>&1
    add_opal_runtime "PipelineTest.csproj"

    # Create a C# file that should score well for conversion
    # Uses only constructs supported by the converter (no switch expressions)
    # Uses a flat namespace (no dots) since OPAL converts dots to underscores
    cat > Calculator.cs << 'CSEOF'
using System;

namespace Calculator
{
    public static class Calc
    {
        public static int Add(int a, int b)
        {
            if (a < 0) throw new ArgumentException("a must be non-negative");
            if (b < 0) throw new ArgumentException("b must be non-negative");
            return a + b;
        }

        public static int Subtract(int a, int b)
        {
            return a - b;
        }

        public static int Multiply(int a, int b)
        {
            return a * b;
        }

        public static bool IsPositive(int value)
        {
            return value > 0;
        }
    }
}
CSEOF

    # Step 1: Analyze
    step "Analyzing project..."
    local analyze_tmp="/tmp/opal-analyze-$$.txt"
    # Note: analyze may return non-zero even on success, so ignore exit code
    "$COMPILER" analyze . --top 5 > "$analyze_tmp" 2>&1 || true
    if ! grep -q "Calculator.cs" "$analyze_tmp"; then
        detail "Analyze output:"
        cat "$analyze_tmp" | head -20
        rm -f "$analyze_tmp"
        fail "$test_name - analyze did not find Calculator.cs"
        return 0
    fi
    rm -f "$analyze_tmp"
    detail "Analysis found Calculator.cs"

    # Step 2: Init
    step "Running opalc init..."
    if ! "$COMPILER" init --ai claude > /dev/null 2>&1; then
        fail "$test_name - opalc init failed"
        return 0
    fi
    use_local_compiler "PipelineTest.csproj"

    # Step 3: Convert
    step "Converting Calculator.cs to OPAL..."
    if ! "$COMPILER" convert Calculator.cs > /dev/null 2>&1; then
        fail "$test_name - conversion failed"
        return 0
    fi
    if [[ ! -f "Calculator.opal" ]]; then
        fail "$test_name - OPAL file not created"
        return 0
    fi
    detail "Conversion succeeded"

    # Step 4: Remove original and build
    step "Removing original .cs and building..."
    rm Calculator.cs

    # Use the OPAL-generated namespace (Calculator) and class (Calc - preserved from original)
    cat > Program.cs << 'CSEOF'
using Calculator;

Console.WriteLine($"Add(2,3) = {Calc.Add(2, 3)}");
Console.WriteLine($"Subtract(10,4) = {Calc.Subtract(10, 4)}");
Console.WriteLine($"Multiply(3,4) = {Calc.Multiply(3, 4)}");
Console.WriteLine($"IsPositive(5) = {Calc.IsPositive(5)}");
CSEOF

    if ! dotnet build --nologo -v q -p:EnableSourceLink=false -p:EnableSourceControlManagerQueries=false 2>&1; then
        fail "$test_name - build failed after conversion"
        return 0
    fi
    detail "Build succeeded"

    # Step 5: Run
    step "Running the application..."
    local output
    output=$(dotnet run --no-build 2>&1) || true
    if [[ "$output" != *"Add(2,3) = 5"* ]]; then
        detail "Output: $output"
        fail "$test_name - unexpected output"
        return 0
    fi
    if [[ "$output" != *"Multiply(3,4) = 12"* ]]; then
        detail "Output: $output"
        fail "$test_name - unexpected output"
        return 0
    fi
    detail "Application ran correctly"

    # Step 6: Verify generated file location
    step "Verifying generated file in obj/opal/..."
    if ! find obj -path "*/opal/Calculator.g.cs" 2>/dev/null | grep -q .; then
        fail "$test_name - generated file not in expected location"
        return 0
    fi
    detail "Generated file in correct location"

    pass "$test_name"
}

test_legacy_project_rejection() {
    local test_name="legacy-project-rejection"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BOLD}${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    step "Creating legacy-style .csproj..."
    cat > Legacy.csproj << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
  </PropertyGroup>
</Project>
EOF

    step "Running opalc init (should reject)..."
    local output
    output=$("$COMPILER" init --ai claude 2>&1) || true
    if [[ "$output" != *"Legacy"* ]] && [[ "$output" != *"SDK-style"* ]]; then
        fail "$test_name - should have rejected legacy project"
        return 0
    fi
    detail "Correctly rejected legacy project"

    pass "$test_name"
}

print_summary() {
    echo ""
    echo "================================"
    echo "Project Init E2E Test Summary"
    echo "================================"
    echo -e "Passed:  ${GREEN}$PASSED${NC}"
    echo -e "Failed:  ${RED}$FAILED${NC}"
    echo -e "Skipped: ${YELLOW}$SKIPPED${NC}"
    echo "================================"
    echo ""
    echo "Configuration:"
    echo "  MIN_SCORE=$MIN_SCORE (set OPAL_MIN_SCORE to change)"
    echo "  MAX_FILES=$MAX_FILES (set OPAL_MAX_FILES to change)"
    echo "================================"

    if [[ $FAILED -gt 0 ]]; then
        exit 1
    fi
}

main() {
    echo ""
    echo "========================================"
    echo "OPAL Project Init E2E Tests"
    echo "========================================"
    echo "Tests opalc init + analyze-driven conversion"
    echo "against real GitHub projects"
    echo ""

    # Parse arguments
    local clean_only=false
    local keep_workdir=false
    local quick_only=false
    local run_tier=""
    for arg in "$@"; do
        case $arg in
            --clean) clean_only=true ;;
            --keep) keep_workdir=true ;;
            --quick) quick_only=true ;;
            --tier=*) run_tier="${arg#*=}" ;;
            --tier1) run_tier="1" ;;
            --tier2) run_tier="2" ;;
            --tier3) run_tier="3" ;;
            --all-tiers) run_tier="all" ;;
            --help)
                echo "Usage: $0 [--clean] [--keep] [--quick] [--tier=N] [--help]"
                echo ""
                echo "Options:"
                echo "  --clean      Clean work directory only"
                echo "  --keep       Keep work directory after tests"
                echo "  --quick      Run only basic local tests (no GitHub clones)"
                echo "  --tier=N     Run only tier N GitHub tests (1, 2, 3, or 'all')"
                echo "  --tier1      Shorthand for --tier=1 (small libraries)"
                echo "  --tier2      Shorthand for --tier=2 (medium libraries)"
                echo "  --tier3      Shorthand for --tier=3 (large libraries)"
                echo "  --all-tiers  Run all GitHub project tiers"
                echo "  --help       Show this help"
                echo ""
                echo "Tiers:"
                echo "  Tier 1: Small, focused libraries (Humanizer, Slugify, HashIds, etc.)"
                echo "  Tier 2: Medium complexity (FluentValidation, Polly, AutoMapper, etc.)"
                echo "  Tier 3: Large libraries (Newtonsoft.Json, Dapper, Serilog, etc.)"
                echo ""
                echo "Environment variables:"
                echo "  OPAL_MIN_SCORE     Minimum analyze score for conversion (default: 30)"
                echo "  OPAL_MAX_FILES     Maximum files to convert per project (default: 5)"
                echo "  OPAL_CLONE_TIMEOUT Timeout for git clone in seconds (default: 120)"
                echo "  OPAL_TEST_WORKDIR  Work directory (default: /tmp/opal-e2e-project-init)"
                exit 0
                ;;
        esac
    done

    if $clean_only; then
        cleanup
        info "Cleanup complete"
        exit 0
    fi

    # Setup
    cleanup
    mkdir -p "$WORK_DIR"
    build_compiler

    # Basic local tests (always run)
    test_basic_console_app
    test_multiple_projects_detection
    test_legacy_project_rejection
    test_full_pipeline

    # Determine which tiers to run
    local run_github_tests=true
    local run_tier1=false
    local run_tier2=false
    local run_tier3=false

    if $quick_only; then
        run_github_tests=false
    elif [[ -n "$run_tier" ]]; then
        case "$run_tier" in
            1) run_tier1=true ;;
            2) run_tier2=true ;;
            3) run_tier3=true ;;
            all) run_tier1=true; run_tier2=true; run_tier3=true ;;
            *) echo "Invalid tier: $run_tier"; exit 1 ;;
        esac
    else
        # Default: run tier 1 only (for reasonable test time)
        run_tier1=true
    fi

    if $run_github_tests; then
        echo ""
        info "Running GitHub project integration tests..."
        info "Note: These tests may fail due to converter bugs or project-specific issues"
        echo ""

        # Real GitHub projects - these test the full pipeline:
        # clone → analyze → init → convert → build
        # Failures may indicate converter bugs rather than init bugs
        # Format: test_github_project "name" "repo_url" "src_subdir" [branch]

        # =====================================================================
        # TIER 1: Small, Focused Libraries (7 projects)
        # These are ideal for testing - small, pure functions, well-tested
        # =====================================================================
        if $run_tier1; then
            echo ""
            info "=== TIER 1: Small, Focused Libraries ==="
            echo ""

            # 1. Humanizer - String/date manipulation with pure functions
            test_github_project \
                "humanizer" \
                "https://github.com/Humanizr/Humanizer.git" \
                "src/Humanizer"

            # 2. Slugify - URL slug generation, simple focused logic
            test_github_project \
                "slugify" \
                "https://github.com/ctolkien/Slugify.git" \
                "src/Slugify.Core"

            # 3. StronglyTypedId - Simple ID wrapper types
            test_github_project \
                "stronglytypedid" \
                "https://github.com/andrewlock/StronglyTypedId.git" \
                "src/StronglyTypedId"

            # 4. Bogus - Fake data generator, many pure functions
            test_github_project \
                "bogus" \
                "https://github.com/bchavez/Bogus.git" \
                "Source/Bogus"

            # 5. FluentResults - Result pattern, clean patterns
            test_github_project \
                "fluentresults" \
                "https://github.com/altmann/FluentResults.git" \
                "src/FluentResults"

            # 6. Ardalis.GuardClauses - Guard clauses, simple validation logic
            test_github_project \
                "guardclauses" \
                "https://github.com/ardalis/GuardClauses.git" \
                "src/GuardClauses"

            # 7. UnitsNet - Unit conversion, math-heavy pure functions
            test_github_project \
                "unitsnet" \
                "https://github.com/angularsen/UnitsNet.git" \
                "UnitsNet"
        fi

        # =====================================================================
        # TIER 2: Medium Complexity Libraries (8 projects)
        # More complex but still well-structured
        # =====================================================================
        if $run_tier2; then
            echo ""
            info "=== TIER 2: Medium Complexity Libraries ==="
            echo ""

            # 1. FluentValidation - Validation library with many simple validators
            test_github_project \
                "fluentvalidation" \
                "https://github.com/FluentValidation/FluentValidation.git" \
                "src/FluentValidation"

            # 2. Polly - Resilience library, clean structure
            test_github_project \
                "polly" \
                "https://github.com/App-vNext/Polly.git" \
                "src/Polly.Core"

            # 3. AutoMapper - Object mapping library
            test_github_project \
                "automapper" \
                "https://github.com/AutoMapper/AutoMapper.git" \
                "src/AutoMapper"

            # 4. MediatR - Mediator pattern, clean CQRS patterns
            test_github_project \
                "mediatr" \
                "https://github.com/jbogard/MediatR.git" \
                "src/MediatR"

            # 5. CsvHelper - CSV parsing library
            test_github_project \
                "csvhelper" \
                "https://github.com/JoshClose/CsvHelper.git" \
                "src/CsvHelper"

            # 6. MoreLINQ - LINQ extension methods
            test_github_project \
                "morelinq" \
                "https://github.com/morelinq/MoreLINQ.git" \
                "MoreLinq"

            # 7. Flurl - Fluent URL builder and HTTP client
            test_github_project \
                "flurl" \
                "https://github.com/tmenier/Flurl.git" \
                "src/Flurl"

            # 8. Spectre.Console - Beautiful console applications
            test_github_project \
                "spectre-console" \
                "https://github.com/spectreconsole/spectre.console.git" \
                "src/Spectre.Console"
        fi

        # =====================================================================
        # TIER 3: Large Libraries (6 projects)
        # More complex, stress test the converter
        # =====================================================================
        if $run_tier3; then
            echo ""
            info "=== TIER 3: Large Libraries ==="
            echo ""

            # 1. Newtonsoft.Json - JSON serialization (very mature)
            test_github_project \
                "newtonsoft-json" \
                "https://github.com/JamesNK/Newtonsoft.Json.git" \
                "Src/Newtonsoft.Json"

            # 2. Dapper - Micro ORM
            test_github_project \
                "dapper" \
                "https://github.com/DapperLib/Dapper.git" \
                "Dapper"

            # 3. Serilog - Structured logging
            test_github_project \
                "serilog" \
                "https://github.com/serilog/serilog.git" \
                "src/Serilog"

            # 4. RestSharp - REST client
            test_github_project \
                "restsharp" \
                "https://github.com/restsharp/RestSharp.git" \
                "src/RestSharp"

            # 5. AngleSharp - HTML parser
            test_github_project \
                "anglesharp" \
                "https://github.com/AngleSharp/AngleSharp.git" \
                "src/AngleSharp"

            # 6. Markdig - Markdown parser
            test_github_project \
                "markdig" \
                "https://github.com/xoofx/markdig.git" \
                "src/Markdig"
        fi
    fi

    # Cleanup unless --keep
    if ! $keep_workdir; then
        cleanup
    else
        info "Work directory kept at: $WORK_DIR"
    fi

    print_summary
}

main "$@"
