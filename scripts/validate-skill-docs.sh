#!/bin/bash

# =============================================================================
# Skill Documentation Validator
# =============================================================================
# This script validates the calor.md skill documentation against the actual
# Lexer.cs implementation to ensure documentation accuracy and completeness.
#
# It performs the following checks:
# 1. Extracts all tokens from Lexer.cs Keywords dictionary
# 2. Extracts all documented tokens from calor.md
# 3. Reports coverage percentage and lists undocumented tokens
# 4. Extracts and test-parses all complete Calor code blocks from calor.md
# 5. Outputs a summary report
#
# Usage: ./scripts/validate-skill-docs.sh
# Exit codes:
#   0 - All validations passed
#   1 - Validation failures detected
# =============================================================================

set -euo pipefail

# Colors for output (disabled if not a terminal)
if [[ -t 1 ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    BOLD='\033[1m'
    NC='\033[0m' # No Color
else
    RED=''
    GREEN=''
    YELLOW=''
    BLUE=''
    BOLD=''
    NC=''
fi

# =============================================================================
# Configuration
# =============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

LEXER_PATH="$REPO_ROOT/src/Calor.Compiler/Parsing/Lexer.cs"
CALOR_MD_PATH="$REPO_ROOT/src/Calor.Compiler/Resources/Skills/calor.md"

# Temporary files for processing
TEMP_DIR=$(mktemp -d)
LEXER_TOKENS="$TEMP_DIR/lexer_tokens.txt"
DOC_TOKENS="$TEMP_DIR/doc_tokens.txt"
CODE_BLOCKS="$TEMP_DIR/code_blocks"
trap 'rm -rf "$TEMP_DIR"' EXIT

# Counters for summary
TOTAL_LEXER_TOKENS=0
DOCUMENTED_TOKENS=0
UNDOCUMENTED_TOKENS=0
TOTAL_CODE_BLOCKS=0
VALID_CODE_BLOCKS=0
INVALID_CODE_BLOCKS=0

# =============================================================================
# Helper Functions
# =============================================================================

log_header() {
    echo -e "\n${BOLD}${BLUE}=== $1 ===${NC}\n"
}

log_success() {
    echo -e "${GREEN}[PASS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[FAIL]${NC} $1"
}

log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

# =============================================================================
# Step 1: Extract tokens from Lexer.cs
# =============================================================================

extract_lexer_tokens() {
    log_header "Step 1: Extracting tokens from Lexer.cs"

    if [[ ! -f "$LEXER_PATH" ]]; then
        log_error "Lexer.cs not found at: $LEXER_PATH"
        exit 1
    fi

    # Extract keywords from the Keywords dictionary
    # Pattern matches lines like: ["M"] = TokenKind.Module,
    # We extract the keyword part (e.g., "M", "/F", "IF", etc.)
    grep -E '^\s*\["[^"]+"\]\s*=' "$LEXER_PATH" | \
        sed -E 's/.*\["([^"]+)"\].*/\1/' | \
        sort -u > "$LEXER_TOKENS"

    TOTAL_LEXER_TOKENS=$(wc -l < "$LEXER_TOKENS" | tr -d ' ')

    log_info "Found $TOTAL_LEXER_TOKENS unique tokens in Lexer.cs"

    # Display categorized tokens
    echo ""
    echo "Token categories:"

    # Single-letter tokens
    local single_letter=$(grep -E '^[A-Z]$' "$LEXER_TOKENS" | wc -l | tr -d ' ')
    echo "  - Single-letter tokens: $single_letter"

    # Closing tags
    local closing=$(grep -E '^/' "$LEXER_TOKENS" | wc -l | tr -d ' ')
    echo "  - Closing tags (§/X): $closing"

    # Multi-letter tokens
    local multi=$((TOTAL_LEXER_TOKENS - single_letter - closing))
    echo "  - Multi-letter tokens: $multi"
}

# =============================================================================
# Step 2: Extract documented tokens from calor.md
# =============================================================================

extract_documented_tokens() {
    log_header "Step 2: Extracting documented tokens from calor.md"

    if [[ ! -f "$CALOR_MD_PATH" ]]; then
        log_error "calor.md not found at: $CALOR_MD_PATH"
        exit 1
    fi

    # Extract all token references from the documentation
    # Patterns to match:
    # - §X, §XX, §XXX (token markers)
    # - §/X, §/XX (closing tags)
    # - §??, §?., §^ (special character tokens)
    # We look for these patterns in both prose and code blocks
    {
        grep -oE '§/?[A-Za-z_]+[0-9]*' "$CALOR_MD_PATH"
        grep -oE '§\?\?' "$CALOR_MD_PATH"
        grep -oE '§\?\.' "$CALOR_MD_PATH"
        grep -oE '§\^' "$CALOR_MD_PATH"
    } | sed 's/§//' | sort -u > "$DOC_TOKENS"

    local doc_count=$(wc -l < "$DOC_TOKENS" | tr -d ' ')
    log_info "Found $doc_count unique token references in documentation"
}

# =============================================================================
# Step 3: Compare and report coverage
# =============================================================================

compare_coverage() {
    log_header "Step 3: Token Coverage Analysis"

    # Find documented tokens that exist in lexer
    DOCUMENTED_TOKENS=0
    UNDOCUMENTED_TOKENS=0

    local undocumented_list=""

    while IFS= read -r token; do
        # Check if this token is documented
        if grep -q "^${token}$" "$DOC_TOKENS"; then
            ((DOCUMENTED_TOKENS++)) || true
        else
            ((UNDOCUMENTED_TOKENS++)) || true
            undocumented_list="$undocumented_list  - §$token\n"
        fi
    done < "$LEXER_TOKENS"

    # Calculate coverage percentage
    local coverage
    if [[ $TOTAL_LEXER_TOKENS -gt 0 ]]; then
        coverage=$(echo "scale=1; $DOCUMENTED_TOKENS * 100 / $TOTAL_LEXER_TOKENS" | bc)
    else
        coverage="0.0"
    fi

    echo "Coverage Summary:"
    echo "  Total Lexer Tokens:    $TOTAL_LEXER_TOKENS"
    echo "  Documented Tokens:     $DOCUMENTED_TOKENS"
    echo "  Undocumented Tokens:   $UNDOCUMENTED_TOKENS"
    echo "  Coverage Percentage:   ${coverage}%"
    echo ""

    if [[ $coverage == "100.0" ]]; then
        log_success "Full documentation coverage achieved!"
    elif (( $(echo "$coverage >= 80" | bc -l) )); then
        log_warning "Good coverage, but some tokens are undocumented"
    else
        log_error "Coverage below 80% - documentation needs attention"
    fi

    if [[ $UNDOCUMENTED_TOKENS -gt 0 ]]; then
        echo ""
        echo -e "${YELLOW}Undocumented tokens:${NC}"
        echo -e "$undocumented_list"
    fi
}

# =============================================================================
# Step 4: Extract and validate code blocks
# =============================================================================

extract_code_blocks() {
    log_header "Step 4: Extracting Calor code blocks from documentation"

    mkdir -p "$CODE_BLOCKS"

    # Extract code blocks marked with ```calor
    local in_block=false
    local block_num=0
    local current_block=""
    local line_num=0

    while IFS= read -r line || [[ -n "$line" ]]; do
        ((line_num++)) || true

        if [[ "$line" == '```calor' ]]; then
            in_block=true
            ((block_num++)) || true
            current_block=""
        elif [[ "$line" == '```' ]] && [[ "$in_block" == true ]]; then
            in_block=false
            # Save the block
            echo "$current_block" > "$CODE_BLOCKS/block_$block_num.calor"
        elif [[ "$in_block" == true ]]; then
            if [[ -z "$current_block" ]]; then
                current_block="$line"
            else
                current_block="$current_block"$'\n'"$line"
            fi
        fi
    done < "$CALOR_MD_PATH"

    TOTAL_CODE_BLOCKS=$block_num
    log_info "Found $TOTAL_CODE_BLOCKS Calor code blocks in documentation"
}

validate_code_blocks() {
    log_header "Step 5: Validating code blocks (structural check)"

    VALID_CODE_BLOCKS=0
    INVALID_CODE_BLOCKS=0
    local invalid_list=""

    for block_file in "$CODE_BLOCKS"/*.calor; do
        [[ -e "$block_file" ]] || continue

        local block_name=$(basename "$block_file")
        local issues=""

        # Structural validation checks
        local content=$(cat "$block_file")

        # Check 1: Verify matching open/close tags for major constructs
        local module_opens=$(grep -c '§M{' "$block_file" 2>/dev/null || echo "0")
        local module_closes=$(grep -c '§/M{' "$block_file" 2>/dev/null || echo "0")

        if [[ "$module_opens" != "$module_closes" ]]; then
            issues="${issues}Module tag mismatch (opens: $module_opens, closes: $module_closes); "
        fi

        local func_opens=$(grep -c '§F{' "$block_file" 2>/dev/null || echo "0")
        local func_closes=$(grep -c '§/F{' "$block_file" 2>/dev/null || echo "0")

        if [[ "$func_opens" != "$func_closes" ]]; then
            issues="${issues}Function tag mismatch (opens: $func_opens, closes: $func_closes); "
        fi

        local class_opens=$(grep -c '§CL{' "$block_file" 2>/dev/null || echo "0")
        local class_closes=$(grep -cE '§/(CL|CLASS){' "$block_file" 2>/dev/null || echo "0")

        if [[ "$class_opens" != "$class_closes" ]]; then
            issues="${issues}Class tag mismatch (opens: $class_opens, closes: $class_closes); "
        fi

        local if_opens=$(grep -c '§IF{' "$block_file" 2>/dev/null || echo "0")
        local if_closes=$(grep -c '§/I{' "$block_file" 2>/dev/null || echo "0")

        if [[ "$if_opens" != "$if_closes" ]]; then
            issues="${issues}If tag mismatch (opens: $if_opens, closes: $if_closes); "
        fi

        local loop_opens=$(grep -c '§L{' "$block_file" 2>/dev/null || echo "0")
        local loop_closes=$(grep -c '§/L{' "$block_file" 2>/dev/null || echo "0")

        if [[ "$loop_opens" != "$loop_closes" ]]; then
            issues="${issues}Loop tag mismatch (opens: $loop_opens, closes: $loop_closes); "
        fi

        local try_opens=$(grep -c '§TR{' "$block_file" 2>/dev/null || echo "0")
        local try_closes=$(grep -c '§/TR{' "$block_file" 2>/dev/null || echo "0")

        if [[ "$try_opens" != "$try_closes" ]]; then
            issues="${issues}Try tag mismatch (opens: $try_opens, closes: $try_closes); "
        fi

        local switch_opens=$(grep -c '§W{' "$block_file" 2>/dev/null || echo "0")
        local switch_closes=$(grep -c '§/W{' "$block_file" 2>/dev/null || echo "0")

        if [[ "$switch_opens" != "$switch_closes" ]]; then
            issues="${issues}Switch tag mismatch (opens: $switch_opens, closes: $switch_closes); "
        fi

        local enum_opens=$(grep -c '§EN{' "$block_file" 2>/dev/null || echo "0")
        local enum_closes=$(grep -c '§/EN{' "$block_file" 2>/dev/null || echo "0")

        if [[ "$enum_opens" != "$enum_closes" ]]; then
            issues="${issues}Enum tag mismatch (opens: $enum_opens, closes: $enum_closes); "
        fi

        # Check 2: Verify no obvious syntax errors
        # (Empty blocks that should have content)
        if [[ -z "$(cat "$block_file" | tr -d '[:space:]')" ]]; then
            issues="${issues}Empty code block; "
        fi

        # Determine validity
        if [[ -z "$issues" ]]; then
            ((VALID_CODE_BLOCKS++)) || true
            log_success "$block_name - structure valid"
        else
            ((INVALID_CODE_BLOCKS++)) || true
            log_error "$block_name - $issues"
            invalid_list="${invalid_list}  - $block_name: $issues\n"
        fi
    done

    echo ""
    echo "Code Block Validation Summary:"
    echo "  Total Blocks:    $TOTAL_CODE_BLOCKS"
    echo "  Valid Blocks:    $VALID_CODE_BLOCKS"
    echo "  Invalid Blocks:  $INVALID_CODE_BLOCKS"
}

# =============================================================================
# Step 6: Try compiling code blocks (if calor CLI is available)
# =============================================================================

try_compile_blocks() {
    log_header "Step 6: Attempting compilation (if calor CLI available)"

    # Check if calor CLI is available
    if command -v dotnet &> /dev/null; then
        local calor_project="$REPO_ROOT/src/Calor.Compiler/Calor.Compiler.csproj"

        if [[ -f "$calor_project" ]]; then
            log_info "Calor compiler project found, checking for CLI..."

            # Try to find the built CLI
            local cli_path="$REPO_ROOT/src/Calor.Cli/bin/Debug/net8.0/calor"
            local cli_dll="$REPO_ROOT/src/Calor.Cli/bin/Debug/net8.0/Calor.Cli.dll"

            if [[ -f "$cli_path" ]] || [[ -f "$cli_dll" ]]; then
                log_info "Calor CLI found, testing compilation..."

                local compile_success=0
                local compile_fail=0

                for block_file in "$CODE_BLOCKS"/*.calor; do
                    [[ -e "$block_file" ]] || continue

                    local block_name=$(basename "$block_file")

                    # Try to compile (suppress output, just check exit code)
                    if [[ -f "$cli_path" ]]; then
                        if "$cli_path" check "$block_file" &> /dev/null; then
                            ((compile_success++)) || true
                        else
                            ((compile_fail++)) || true
                            log_warning "$block_name - compilation check failed"
                        fi
                    fi
                done

                echo ""
                echo "Compilation Check Summary:"
                echo "  Successful: $compile_success"
                echo "  Failed:     $compile_fail"
            else
                log_info "Calor CLI not built. Run 'dotnet build' to enable compilation checks."
                log_info "Skipping compilation validation..."
            fi
        else
            log_info "Calor compiler project not found at expected location."
            log_info "Skipping compilation validation..."
        fi
    else
        log_info "dotnet CLI not available - skipping compilation validation"
    fi
}

# =============================================================================
# Summary Report
# =============================================================================

print_summary() {
    log_header "Validation Summary Report"

    local exit_code=0

    echo "Token Documentation Coverage"
    echo "----------------------------"
    local coverage
    if [[ $TOTAL_LEXER_TOKENS -gt 0 ]]; then
        coverage=$(echo "scale=1; $DOCUMENTED_TOKENS * 100 / $TOTAL_LEXER_TOKENS" | bc)
    else
        coverage="0.0"
    fi
    echo "  Coverage:              ${coverage}%"
    echo "  Documented/Total:      $DOCUMENTED_TOKENS/$TOTAL_LEXER_TOKENS"
    echo ""

    echo "Code Block Validation"
    echo "---------------------"
    echo "  Total Blocks:          $TOTAL_CODE_BLOCKS"
    echo "  Structurally Valid:    $VALID_CODE_BLOCKS"
    echo "  Structurally Invalid:  $INVALID_CODE_BLOCKS"
    echo ""

    # Determine overall status
    if [[ $UNDOCUMENTED_TOKENS -gt 0 ]]; then
        log_warning "There are $UNDOCUMENTED_TOKENS undocumented tokens"
        exit_code=1
    fi

    if [[ $INVALID_CODE_BLOCKS -gt 0 ]]; then
        log_error "There are $INVALID_CODE_BLOCKS structurally invalid code blocks"
        exit_code=1
    fi

    if [[ $exit_code -eq 0 ]]; then
        echo ""
        log_success "All validations passed!"
    else
        echo ""
        log_error "Some validations failed - please review the output above"
    fi

    return $exit_code
}

# =============================================================================
# Main Execution
# =============================================================================

main() {
    echo -e "${BOLD}Calor Skill Documentation Validator${NC}"
    echo "======================================"
    echo "Repository: $REPO_ROOT"
    echo "Lexer:      $LEXER_PATH"
    echo "Docs:       $CALOR_MD_PATH"

    extract_lexer_tokens
    extract_documented_tokens
    compare_coverage
    extract_code_blocks
    validate_code_blocks
    try_compile_blocks
    print_summary
}

main "$@"
