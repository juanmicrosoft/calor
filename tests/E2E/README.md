# OPAL E2E Test Suite

End-to-end tests verifying OPAL compilation and generated C# correctness.

## Running Tests

### Mac/Linux
```bash
./tests/E2E/run-tests.sh
```

### Windows
```powershell
.\tests\E2E\run-tests.ps1
```

### Clean Generated Files
```bash
./tests/E2E/run-tests.sh --clean
```

## Test Structure

```
tests/E2E/
├── run-tests.sh           # Main runner (Mac/Linux)
├── run-tests.ps1          # Windows runner
├── scenarios/
│   ├── 01_hello_world/
│   │   ├── input.opal     # Source to compile
│   │   └── verify.sh      # Verification script
│   ├── 02_fizzbuzz/
│   ├── 03_contracts/
│   ├── 04_option_result/
│   └── 05_skill_syntax/
└── README.md
```

## Scenario Requirements

Each scenario directory must contain:
- `input.opal` - OPAL source file to compile
- `verify.sh` (optional) - Verification script

The test runner will:
1. Compile `input.opal` to `output.g.cs`
2. Run `verify.sh` if present
3. Report pass/fail status

## Verification Scripts

Verification scripts receive the scenario directory as the first argument:

```bash
#!/usr/bin/env bash
SCENARIO_DIR="$1"
OUTPUT_FILE="$SCENARIO_DIR/output.g.cs"

# Check for expected patterns
grep -q "Console.WriteLine" "$OUTPUT_FILE" || exit 1
```

## Adding New Tests

1. Create a new directory: `scenarios/XX_name/`
2. Add `input.opal` with OPAL source code
3. Add `verify.sh` with verification logic
4. Run `./run-tests.sh` to verify
