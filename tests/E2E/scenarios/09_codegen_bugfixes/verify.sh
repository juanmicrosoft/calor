#!/usr/bin/env bash
set -euo pipefail

SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Verify file exists
[[ -f "$OUTPUT_FILE" ]] || { echo "Output file not found"; exit 1; }

# Bug 1: §IDX{input} i → input[i], NOT new object[]
grep -q "input\[i\]" "$OUTPUT_FILE" || { echo "Bug 1: Missing input[i]"; exit 1; }
! grep -q "new object\[\]" "$OUTPUT_FILE" || { echo "Bug 1: Found incorrect new object[]"; exit 1; }

# Bug 2: §NEW{Dictionary<char,str>} → new Dictionary<char, string>()
grep -q "new Dictionary<char, string>()" "$OUTPUT_FILE" || { echo "Bug 2: Missing Dictionary<char, string>()"; exit 1; }
! grep -q "Dictionarycharstr" "$OUTPUT_FILE" || { echo "Bug 2: Found incorrect Dictionarycharstr"; exit 1; }

# Bug 3: §B{ConsoleKeyInfo:keyPressed} → ConsoleKeyInfo keyPressed
grep -q "ConsoleKeyInfo keyPressed" "$OUTPUT_FILE" || { echo "Bug 3: Missing ConsoleKeyInfo keyPressed"; exit 1; }

# Bug 4: §ARR{char:buf:256} → new char[256]
grep -q "new char\[256\]" "$OUTPUT_FILE" || { echo "Bug 4: Missing new char[256]"; exit 1; }
! grep -q "new buf\[256\]" "$OUTPUT_FILE" || { echo "Bug 4: Found incorrect new buf[256]"; exit 1; }

# Bug 5: §PROP{...:over} → public override string Name
grep -q "public override string Name" "$OUTPUT_FILE" || { echo "Bug 5: Missing public override"; exit 1; }
grep -q "public abstract string Name" "$OUTPUT_FILE" || { echo "Bug 5: Missing public abstract"; exit 1; }

# Bug 6: Multiple §NEW in §C → separate args
grep -q "new StringBuilder(), new StringBuilder()" "$OUTPUT_FILE" || { echo "Bug 6: Missing two separate StringBuilder args"; exit 1; }

# Bug 7: (char-lit "Y") → 'Y'
grep -q "'Y'" "$OUTPUT_FILE" || { echo "Bug 7: Missing char literal 'Y'"; exit 1; }

echo "All 7 bug fix verifications passed"
exit 0
