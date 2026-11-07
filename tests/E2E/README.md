# Calor E2E Test Suite

End-to-end tests verifying Calor compilation and generated C# correctness.

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
│   │   ├── input.calr     # Source to compile
│   │   └── verify.sh      # Verification script
│   ├── 02_fizzbuzz/
│   ├── 03_contracts/
│   ├── 04_option_result/
│   └── 05_skill_syntax/
└── README.md
```

## Scenario Requirements

Each scenario directory must contain:
- `input.calr` - Calor source file to compile
- `verify.sh` (optional) - Verification script

The test runner will:
1. Compile `input.calr` to `output.g.cs`
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
2. Add `input.calr` with Calor source code
3. Add `verify.sh` with verification logic
4. Run `./run-tests.sh` to verify

## Logic-task behavioral verification

The four `agent-tasks/tasks/logic-implementation/*/verify.sh` entry points take a
workspace containing `Calculator.calr`. They do not call agents or provider APIs.
Their unchanged `task.json` prompts define the required signatures, contracts,
and behavior.

Build the compiler first, then run the executable verifier controls:

```bash
# On a fresh clone, bootstrap the verified Z3 assets if the build requests them.
src/Calor.Compiler/scripts/download-z3.sh
dotnet build src/Calor.Compiler/
python3 tests/E2E/agent-tasks/test-logic-verifiers.py
```

For a paired experiment, explicitly select each prebuilt compiler checkout:

```bash
CALOR_COMPILER_ROOT=/absolute/path/to/old-root \
  tests/E2E/agent-tasks/tasks/logic-implementation/01_factorial/verify.sh /path/to/workspace
CALOR_COMPILER_ROOT=/absolute/path/to/new-root \
  tests/E2E/agent-tasks/tasks/logic-implementation/01_factorial/verify.sh /path/to/workspace
```

`CALOR_COMPILER_CONFIGURATION` defaults to `Debug`; both the shared runner's
compilation check and these verifiers honor the selected root/configuration.
Without a selection, they use this checkout. A missing or incompatible selected
compiler fails explicitly, without falling back to the integration compiler.
The test-only helper builds in a disposable directory, referencing only the
selected compiler's built assemblies and native Z3 assets. It requires Python 3,
the .NET 10 SDK, and the selected compiler's public compilation, AST, Roslyn,
and contract-translator APIs.

The verifier checks the named module function's public signature, proves that
its entry domain matches the prompt, and proves that its postconditions imply
every required result predicate. A separate symbolic oracle checks that the
postconditions accept every required output, preventing vacuous contracts.
Contract proofs cover signed 32-bit inputs, not just runtime samples. They use
the selected compiler's AST and Z3 translator, with a five-second solver limit;
unsupported expressions and unknown solver results fail. Proof input is limited
to total native comparisons, boolean operators, conditionals, implication,
references, and integer/boolean literals; calls and arithmetic are refused rather
than treated as proof.

The same selected compiler generates the C# that is compiled and executed with
runtime contracts enabled. Factorial runs all six permitted inputs. Clamp runs
726 lower/upper/interior/boundary combinations; IsPositive runs 11 signed inputs
including zero; Max runs 121 pairs including ties and integer boundaries.
These finite runtime oracles are behavioral checks, not a universal proof of
arbitrary function bodies. Statement and expression implementations both pass.
Compilation errors, execution errors, wrong results, missing inputs, and
timeouts fail. `CALOR_LOGIC_TIMEOUT_SECONDS` may shorten the overall 120-second
build-and-execution deadline; timed-out process groups are terminated.

The controls exercise the real entry points with valid alternatives, missing
and stub declarations, misleading comments and unrelated contracts, wrong
signatures and boundary outputs, missing/weakened/vacuous contracts, runtime
errors, timeouts, and missing/corrupt/alternate selected compiler roots. They
do not establish agent nonregression or complete the paired release experiment.
