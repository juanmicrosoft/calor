#!/usr/bin/env bash
set -euo pipefail

# Multi-project solution E2E test
# Copies solution to temp dir, runs calor init, then compiles each .calr file

SCENARIO_DIR="$1"
COMPILER="$2"
SOLUTION_TEMPLATE="$SCENARIO_DIR/solution"

# Create temp working directory
WORK_DIR=$(mktemp -d)
# No trap here — verify.sh handles cleanup via .workdir file

# Copy solution template to work dir
cp -r "$SOLUTION_TEMPLATE"/* "$WORK_DIR/"

# Run calor init on the solution
"$COMPILER" init --solution "$WORK_DIR/MultiProject.sln" --ai claude || {
    echo "calor init failed"
    exit 1
}

# Compile each project's .calr files
for calr_file in "$WORK_DIR"/*//*.calr; do
    project_dir=$(dirname "$calr_file")
    filename=$(basename "$calr_file" .calr)
    output_file="$project_dir/${filename}.g.cs"

    "$COMPILER" --input "$calr_file" --output "$output_file" || {
        echo "Compilation failed for $calr_file"
        exit 1
    }
done

# Store work dir path for verify.sh
echo "$WORK_DIR" > "$SCENARIO_DIR/.workdir"

exit 0
