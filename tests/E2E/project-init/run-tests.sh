#!/usr/bin/env bash
set -euo pipefail

# OPAL Project Init E2E Test Runner
# Tests `opalc init` against real GitHub projects

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
WORK_DIR="${OPAL_TEST_WORKDIR:-/tmp/opal-e2e-project-init}"
COMPILER="$REPO_ROOT/src/Opal.Compiler/bin/Debug/net8.0/opalc"
RUNTIME_PROJ="$REPO_ROOT/src/Opal.Runtime/Opal.Runtime.csproj"

# Colors
if [[ -t 1 ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    CYAN='\033[0;36m'
    NC='\033[0m'
else
    RED='' GREEN='' YELLOW='' BLUE='' CYAN='' NC=''
fi

PASSED=0
FAILED=0
SKIPPED=0

info() { echo -e "${BLUE}[INFO]${NC} $1"; }
pass() { echo -e "${GREEN}[PASS]${NC} $1"; ((PASSED++)) || true; }
fail() { echo -e "${RED}[FAIL]${NC} $1"; ((FAILED++)) || true; }
skip() { echo -e "${YELLOW}[SKIP]${NC} $1"; ((SKIPPED++)) || true; }
step() { echo -e "  ${CYAN}→${NC} $1"; }

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

# Add Opal.Runtime reference to a project (using local project reference)
add_opal_runtime() {
    local csproj="$1"
    dotnet add "$csproj" reference "$RUNTIME_PROJ" > /dev/null 2>&1
}

# Update .csproj to use the local compiler path instead of global opalc
use_local_compiler() {
    local csproj="$1"
    # Replace OpalCompilerPath default value with the full path to our local compiler
    if command -v sed > /dev/null; then
        sed -i.bak "s|>opalc<|>$COMPILER<|g" "$csproj"
        rm -f "${csproj}.bak"
    fi
}

# Clean up work directory
cleanup() {
    if [[ -d "$WORK_DIR" ]]; then
        info "Cleaning up $WORK_DIR..."
        rm -rf "$WORK_DIR"
    fi
}

# Clone a project (shallow clone for speed)
clone_project() {
    local repo_url="$1"
    local project_dir="$2"
    local commit="${3:-}"

    if [[ -d "$project_dir" ]]; then
        step "Using cached clone: $project_dir"
        return 0
    fi

    step "Cloning $repo_url..."
    if [[ -n "$commit" ]]; then
        git clone --depth 1 --single-branch "$repo_url" "$project_dir" 2>/dev/null || {
            echo "Failed to clone $repo_url"
            return 1
        }
        cd "$project_dir"
        git fetch --depth 1 origin "$commit" 2>/dev/null || true
        git checkout "$commit" 2>/dev/null || true
        cd - > /dev/null
    else
        git clone --depth 1 "$repo_url" "$project_dir" 2>/dev/null || {
            echo "Failed to clone $repo_url"
            return 1
        }
    fi
}

# Test: Basic console app
test_basic_console_app() {
    local test_name="basic-console-app"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    # Create a new console project
    step "Creating console project..."
    dotnet new console --name TestApp --output . -f net8.0 > /dev/null 2>&1

    # Add Opal.Runtime reference (required by generated code)
    add_opal_runtime "TestApp.csproj"

    # Run opalc init
    step "Running opalc init --ai claude..."
    if ! "$COMPILER" init --ai claude 2>&1; then
        fail "$test_name - opalc init failed"
        return 0
    fi

    # Use local compiler (ensures we test the current build, not installed version)
    use_local_compiler "TestApp.csproj"

    # Verify .csproj has OPAL targets
    step "Verifying .csproj changes..."
    if ! grep -q "CompileOpalFiles" TestApp.csproj; then
        fail "$test_name - CompileOpalFiles target not found"
        return 0
    fi

    if ! grep -q "OpalOutputDirectory" TestApp.csproj; then
        fail "$test_name - OpalOutputDirectory not found"
        return 0
    fi

    # Create an OPAL file with simple code (no runtime-specific features)
    step "Creating test.opal file..."
    cat > test.opal << 'OPAL_EOF'
§M[m001:TestModule]
§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §BODY
    §RETURN (+ a b)
  §END_BODY
§/F[f001]
§/M[m001]
OPAL_EOF

    # Update Program.cs to use the generated code
    cat > Program.cs << 'CS_EOF'
var result = TestModule.TestModuleModule.Add(21, 21);
Console.WriteLine($"21 + 21 = {result}");
CS_EOF

    # Build the project
    step "Building project..."
    if ! dotnet build --nologo -v q 2>&1; then
        fail "$test_name - dotnet build failed"
        return 0
    fi

    # Verify generated file exists in obj/opal/
    step "Verifying generated files..."
    local gen_file
    gen_file=$(find obj -name "test.g.cs" 2>/dev/null | head -1)
    if [[ -z "$gen_file" ]]; then
        fail "$test_name - generated file not found in obj/"
        return 0
    fi

    # Run the app
    step "Running the app..."
    local output
    output=$(dotnet run --no-build 2>&1) || true
    if [[ "$output" != *"21 + 21 = 42"* ]]; then
        fail "$test_name - unexpected output: $output"
        return 0
    fi

    # Test clean cycle
    step "Testing dotnet clean..."
    dotnet clean --nologo -v q > /dev/null 2>&1

    if [[ -d "obj/Debug/net8.0/opal" ]]; then
        fail "$test_name - opal directory not cleaned"
        return 0
    fi

    # Rebuild after clean
    step "Rebuilding after clean..."
    if ! dotnet build --nologo -v q 2>&1; then
        fail "$test_name - rebuild after clean failed"
        return 0
    fi

    pass "$test_name"
}

# Test: Project with subdirectories
test_subdirectory_structure() {
    local test_name="subdirectory-structure"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    # Create a new console project
    step "Creating console project with subdirectories..."
    dotnet new console --name SubdirApp --output . -f net8.0 > /dev/null 2>&1
    mkdir -p Services Models

    # Add Opal.Runtime reference
    add_opal_runtime "SubdirApp.csproj"

    # Run opalc init
    step "Running opalc init --ai claude..."
    "$COMPILER" init --ai claude > /dev/null 2>&1

    # Use local compiler
    use_local_compiler "SubdirApp.csproj"

    # Create OPAL files in subdirectories
    step "Creating OPAL files in subdirectories..."
    cat > Services/Calculator.opal << 'OPAL_EOF'
§M[m001:Calculator]
§F[f001:Add:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §BODY
    §RETURN (+ a b)
  §END_BODY
§/F[f001]
§/M[m001]
OPAL_EOF

    cat > Models/Person.opal << 'OPAL_EOF'
§M[m001:Person]
§F[f001:GetAge:pub]
  §O[i32]
  §BODY
    §RETURN 42
  §END_BODY
§/F[f001]
§/M[m001]
OPAL_EOF

    # Update Program.cs - use generated namespaces (module name only, not path)
    cat > Program.cs << 'CS_EOF'
Console.WriteLine(Calculator.CalculatorModule.Add(2, 3));
Console.WriteLine(Person.PersonModule.GetAge());
CS_EOF

    # Build the project
    step "Building project..."
    if ! dotnet build --nologo -v q 2>&1; then
        fail "$test_name - dotnet build failed"
        return 0
    fi

    # Verify generated files preserve directory structure
    step "Verifying directory structure in obj/..."
    if ! find obj -path "*/opal/Services/Calculator.g.cs" 2>/dev/null | grep -q .; then
        fail "$test_name - Services subdirectory not preserved"
        return 0
    fi

    if ! find obj -path "*/opal/Models/Person.g.cs" 2>/dev/null | grep -q .; then
        fail "$test_name - Models subdirectory not preserved"
        return 0
    fi

    # Run and verify output
    step "Running the app..."
    local output
    output=$(dotnet run --no-build 2>&1)
    if [[ "$output" != *"5"* ]] || [[ "$output" != *"42"* ]]; then
        fail "$test_name - unexpected output: $output"
        return 0
    fi

    pass "$test_name"
}

# Test: Incremental build
test_incremental_build() {
    local test_name="incremental-build"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    # Create a project
    step "Creating console project..."
    dotnet new console --name IncrApp --output . -f net8.0 > /dev/null 2>&1

    # Add Opal.Runtime reference
    add_opal_runtime "IncrApp.csproj"

    # Run opalc init
    "$COMPILER" init --ai claude > /dev/null 2>&1

    # Use local compiler
    use_local_compiler "IncrApp.csproj"

    # Create OPAL file
    cat > math.opal << 'OPAL_EOF'
§M[m001:Math]
§F[f001:Double:pub]
  §I[i32:x]
  §O[i32]
  §BODY
    §RETURN (* x 2)
  §END_BODY
§/F[f001]
§/M[m001]
OPAL_EOF

    cat > Program.cs << 'CS_EOF'
Console.WriteLine(Math.MathModule.Double(21));
CS_EOF

    # Initial build
    step "Initial build..."
    dotnet build --nologo -v q > /dev/null 2>&1

    # Get timestamp of generated file
    local gen_file
    gen_file=$(find obj -name "math.g.cs" 2>/dev/null | head -1)
    local ts1
    ts1=$(stat -f %m "$gen_file" 2>/dev/null || stat -c %Y "$gen_file" 2>/dev/null)

    # Build again without changes
    step "Rebuilding without changes..."
    sleep 1
    dotnet build --nologo -v q > /dev/null 2>&1

    local ts2
    ts2=$(stat -f %m "$gen_file" 2>/dev/null || stat -c %Y "$gen_file" 2>/dev/null)

    if [[ "$ts1" != "$ts2" ]]; then
        fail "$test_name - file regenerated without source changes"
        return 0
    fi

    # Modify source and rebuild
    step "Modifying source and rebuilding..."
    sleep 1
    cat > math.opal << 'OPAL_EOF'
§M[m001:Math]
§F[f001:Double:pub]
  §I[i32:x]
  §O[i32]
  §BODY
    §RETURN (* x 3)
  §END_BODY
§/F[f001]
§/M[m001]
OPAL_EOF

    dotnet build --nologo -v q > /dev/null 2>&1

    local ts3
    ts3=$(stat -f %m "$gen_file" 2>/dev/null || stat -c %Y "$gen_file" 2>/dev/null)

    if [[ "$ts2" == "$ts3" ]]; then
        fail "$test_name - file not regenerated after source change"
        return 0
    fi

    pass "$test_name"
}

# Test: Multiple project detection
test_multiple_projects() {
    local test_name="multiple-projects"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    # Create two projects
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

    # Run opalc init without --project (should fail)
    step "Running opalc init without --project..."
    local output
    output=$("$COMPILER" init --ai claude 2>&1) || true
    if [[ "$output" != *"Multiple"* ]] && [[ "$output" != *"specify"* ]]; then
        fail "$test_name - should have failed with multiple projects"
        return 0
    fi
    step "Correctly failed with multiple projects"

    # Run opalc init with --project
    step "Running opalc init --ai claude --project Project1.csproj..."
    if ! "$COMPILER" init --ai claude --project Project1.csproj 2>&1; then
        fail "$test_name - opalc init with --project failed"
        return 0
    fi

    # Verify only Project1 was modified
    if ! grep -q "CompileOpalFiles" Project1.csproj; then
        fail "$test_name - Project1.csproj not modified"
        return 0
    fi

    if grep -q "CompileOpalFiles" Project2.csproj; then
        fail "$test_name - Project2.csproj was incorrectly modified"
        return 0
    fi

    pass "$test_name"
}

# Test: Idempotency
test_idempotency() {
    local test_name="idempotency"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    # Create project
    step "Creating console project..."
    dotnet new console --name IdempApp --output . -f net8.0 > /dev/null 2>&1

    # Run opalc init twice
    step "Running opalc init first time..."
    "$COMPILER" init --ai claude > /dev/null 2>&1

    step "Running opalc init second time (without force)..."
    local output
    output=$("$COMPILER" init --ai claude 2>&1)

    # Should indicate already initialized
    if [[ "$output" != *"already"* ]]; then
        fail "$test_name - did not detect existing configuration"
        return 0
    fi

    # Verify only one set of targets
    local target_count
    target_count=$(grep -c 'Name="CompileOpalFiles"' IdempApp.csproj || echo 0)
    if [[ "$target_count" != "1" ]]; then
        fail "$test_name - targets duplicated (count: $target_count)"
        return 0
    fi

    pass "$test_name"
}

# Test: Legacy project rejection
test_legacy_project_rejection() {
    local test_name="legacy-project-rejection"
    local test_dir="$WORK_DIR/$test_name"

    echo -e "\n${BLUE}Test: $test_name${NC}"

    mkdir -p "$test_dir"
    cd "$test_dir"

    # Create legacy-style .csproj
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

    # Run opalc init (should fail)
    step "Running opalc init (should reject legacy project)..."
    local output
    output=$("$COMPILER" init --ai claude 2>&1 || true)
    local exit_code=$?

    # Check that it failed (non-zero exit or error message present)
    if [[ "$output" != *"Legacy"* ]] && [[ "$output" != *"SDK-style"* ]] && [[ "$output" != *"Error"* ]]; then
        fail "$test_name - should have rejected legacy project"
        return 0
    fi
    step "Correctly rejected legacy project"

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

    if [[ $FAILED -gt 0 ]]; then
        exit 1
    fi
}

main() {
    echo ""
    echo "OPAL Project Init E2E Tests"
    echo "============================"
    echo ""

    # Parse arguments
    local clean_only=false
    local keep_workdir=false
    for arg in "$@"; do
        case $arg in
            --clean) clean_only=true ;;
            --keep) keep_workdir=true ;;
            --help)
                echo "Usage: $0 [--clean] [--keep] [--help]"
                echo "  --clean  Clean work directory only"
                echo "  --keep   Keep work directory after tests"
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

    # Setup
    cleanup
    mkdir -p "$WORK_DIR"
    build_compiler

    # Run tests
    test_basic_console_app
    test_subdirectory_structure
    test_incremental_build
    test_multiple_projects
    test_idempotency
    test_legacy_project_rejection

    # Cleanup unless --keep
    if ! $keep_workdir; then
        cleanup
    else
        info "Work directory kept at: $WORK_DIR"
    fi

    print_summary
}

main "$@"
