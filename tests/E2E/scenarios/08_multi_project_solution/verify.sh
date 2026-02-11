#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"

# Read the work directory from run.sh
WORK_DIR_FILE="$SCENARIO_DIR/.workdir"
[[ -f "$WORK_DIR_FILE" ]] || { echo "No .workdir file - run.sh must run first"; exit 1; }
WORK_DIR=$(cat "$WORK_DIR_FILE")
[[ -d "$WORK_DIR" ]] || { echo "Work directory not found: $WORK_DIR"; exit 1; }

ERRORS=0
check() {
    if ! eval "$1" >/dev/null 2>&1; then
        echo "FAIL: $2"
        ERRORS=$((ERRORS + 1))
    fi
}

# --- 1. CLAUDE.md placement ---
check '[[ -f "$WORK_DIR/CLAUDE.md" ]]' "CLAUDE.md missing from solution root"
check '! [[ -f "$WORK_DIR/WebApi/CLAUDE.md" ]]' "CLAUDE.md should NOT be in WebApi/"
check '! [[ -f "$WORK_DIR/DataAccess/CLAUDE.md" ]]' "CLAUDE.md should NOT be in DataAccess/"
check '! [[ -f "$WORK_DIR/Shared/CLAUDE.md" ]]' "CLAUDE.md should NOT be in Shared/"

# --- 2. Claude skills ---
check '[[ -d "$WORK_DIR/.claude/skills" ]]' ".claude/skills/ missing from solution root"
check '[[ -f "$WORK_DIR/.claude/skills/calor/SKILL.md" ]]' "calor skill missing"
check '[[ -f "$WORK_DIR/.claude/skills/calor-convert/SKILL.md" ]]' "calor-convert skill missing"
check '[[ -f "$WORK_DIR/.claude/skills/calor-semantics/SKILL.md" ]]' "calor-semantics skill missing"

# --- 3. Claude settings ---
check '[[ -f "$WORK_DIR/.claude/settings.json" ]]' ".claude/settings.json missing"

# --- 4. Calor MSBuild tasks in each .csproj ---
for project in WebApi DataAccess Shared; do
    csproj="$WORK_DIR/$project/$project.csproj"
    check 'grep -q "CompileCalorFiles" "$csproj"' "$project.csproj missing CompileCalorFiles target"
    check 'grep -q "IncludeCalorGeneratedFiles" "$csproj"' "$project.csproj missing IncludeCalorGeneratedFiles target"
    check 'grep -q "CleanCalorFiles" "$csproj"' "$project.csproj missing CleanCalorFiles target"
done

# --- 5. Generated .g.cs files exist ---
check '[[ -f "$WORK_DIR/WebApi/api_handler.g.cs" ]]' "WebApi/api_handler.g.cs not generated"
check '[[ -f "$WORK_DIR/DataAccess/repository.g.cs" ]]' "DataAccess/repository.g.cs not generated"
check '[[ -f "$WORK_DIR/Shared/utils.g.cs" ]]' "Shared/utils.g.cs not generated"

# --- 6. Generated code correctness ---
# WebApi
check 'grep -q "namespace ApiHandler" "$WORK_DIR/WebApi/api_handler.g.cs"' "WebApi: missing namespace ApiHandler"
check 'grep -q "HandleRequest" "$WORK_DIR/WebApi/api_handler.g.cs"' "WebApi: missing HandleRequest method"
check 'grep -q "ValidateStatus" "$WORK_DIR/WebApi/api_handler.g.cs"' "WebApi: missing ValidateStatus method"
check 'grep -q "ContractViolationException" "$WORK_DIR/WebApi/api_handler.g.cs"' "WebApi: missing contract enforcement"

# DataAccess
check 'grep -q "namespace Repository" "$WORK_DIR/DataAccess/repository.g.cs"' "DataAccess: missing namespace Repository"
check 'grep -q "GetById" "$WORK_DIR/DataAccess/repository.g.cs"' "DataAccess: missing GetById method"
check 'grep -q "Count" "$WORK_DIR/DataAccess/repository.g.cs"' "DataAccess: missing Count method"
check 'grep -q "ContractViolationException" "$WORK_DIR/DataAccess/repository.g.cs"' "DataAccess: missing contract enforcement"

# Shared
check 'grep -q "namespace Utils" "$WORK_DIR/Shared/utils.g.cs"' "Shared: missing namespace Utils"
check 'grep -q "Add" "$WORK_DIR/Shared/utils.g.cs"' "Shared: missing Add method"
check 'grep -q "IsPositive" "$WORK_DIR/Shared/utils.g.cs"' "Shared: missing IsPositive method"
check 'grep -q "Clamp" "$WORK_DIR/Shared/utils.g.cs"' "Shared: missing Clamp method"

# Cleanup
rm -rf "$WORK_DIR"
rm -f "$SCENARIO_DIR/.workdir"

if [[ $ERRORS -gt 0 ]]; then
    echo "$ERRORS verification(s) failed"
    exit 1
fi

exit 0
