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

# --- 2. Current teaching artifacts (MCP guidance replaces installed skills) ---
check '! [[ -d "$WORK_DIR/.claude/skills" ]]' "Retired slash skills should not be installed"
check 'grep -q "<!-- BEGIN CalorC SECTION" "$WORK_DIR/CLAUDE.md"' "Managed teaching section missing"
check 'grep -q "<!-- END CalorC SECTION -->" "$WORK_DIR/CLAUDE.md"' "Managed teaching section is incomplete"
if ! python3 - "$WORK_DIR" <<'PY'
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
config = json.loads((root / ".home/.claude.json").read_text())
projects = config["projects"]
assert len(projects) == 1, "Expected exactly the disposable solution's MCP entry"
project_path, project = next(iter(projects.items()))
assert Path(project_path).resolve() == root.resolve(), "MCP configuration belongs to a different project"
calor = project["mcpServers"]["calor"]
assert calor["type"] == "stdio"
assert calor["command"] == "calor"
assert calor["args"] == ["mcp", "--stdio"]
settings = json.loads((root / ".claude/settings.json").read_text())
assert "mcpServers" not in settings, "MCP must stay in the isolated user configuration"
hooks = settings["hooks"]
assert "PreToolUse" in hooks and "PostToolUse" in hooks
for command in ("validate-write", "validate-edit", "validate-calr-content", "validate-ids", "post-write-lint"):
    assert "calor hook " + command in json.dumps(hooks), command
PY
then
    echo "FAIL: isolated MCP configuration or Calor-first hooks are incomplete"
    ERRORS=$((ERRORS + 1))
fi

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
