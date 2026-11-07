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
The shared runner's build step also uses that root and configuration, and reports
build errors without retrying against this checkout. Its no-provider selection
controls run with `python3 tests/E2E/agent-tasks/test-compiler-selection.py`.
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
Integer signatures use the selected compiler's type normalization, so `int`,
`i32`, and mixed aliases pass the same CLR `Int32` signature check; IsPositive
still requires a CLR boolean result.
Compilation errors, execution errors, wrong results, missing inputs, and
timeouts fail. `CALOR_LOGIC_TIMEOUT_SECONDS` may shorten the overall 120-second
build-and-execution deadline; timed-out process groups are terminated.
Termination and output cleanup share that deadline. Escaped descendants holding
stdout open cannot cause an unbounded EOF wait; cancellation also terminates the
original process group and bounds cleanup.

The controls exercise the real entry points with valid alternatives, missing
and stub declarations, misleading comments and unrelated contracts, wrong
signatures and boundary outputs, missing/weakened/vacuous contracts, runtime
errors, timeouts, and missing/corrupt/alternate selected compiler roots. They
do not establish agent nonregression or complete the paired release experiment.

Actual old-release API compatibility is a separate executable control:

```bash
python3 tests/E2E/agent-tasks/test-logic-old-release.py
```

It requires Python 3.12+, Git history containing v0.29.0 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, a prebuilt selected current compiler,
its bootstrapped Z3 assets, the .NET 10 SDK, and NuGet restore access or cached
locked dependencies. It archives that exact Git tree into a disposable directory,
validates copied native build prerequisites against the old release's own
manifest, restores locked packages, and builds the unchanged old compiler source.
It runs 49 real verifier controls against actual old and current compilers:
public integer/boolean signatures, statement and expression implementations,
`int`/`i32` aliases, missing/weakened contracts, wrong signatures and runtime
results, and current-parser rejection of historical source IDs.
Historical IDs are used only in temporary old-parser inputs; current inputs
remain ID-free. Both helpers link exclusively to their selected arm and use the
shared public `AttributeHelper.ExpandType` API; no reflection-based API fallback
or integration-compiler substitution is used.

The archive/build/control run has a 600-second deadline. Missing history, assets,
dependencies, incompatible APIs, failed builds, or failed controls are errors,
not skips. No historical files or ledgers are changed. This is a v0.29
compatibility control, not a selection of the final experimental baseline,
an agent run, or agent nonregression evidence.

## Control-flow task behavioral verification

The `cf-001` through `cf-004` entry points under
`agent-tasks/tasks/control-flow/` use the same selected-compiler helper and
120-second overall deadline as the logic tasks. The first three read
`Effects.calr`; `Classify` reads `Calculator.calr`. Task definitions and original
fixtures remain unchanged.

The selected compiler's parsed AST must contain the requested native `for`,
`while`, `do-while`, or complete `if`/`elseif`/`else` construct inside the named
public module function. The loop functions must declare console-write effects
on that function. Effects are checked on the parsed declaration before compiler
passes can synthesize them. Compilation and effect parsing share one immutable
source string read by the helper. The AST walker accepts nested statement containers,
but does not count constructs in separate local functions or lambda expressions.
Comments, unrelated functions, and source spellings are not structural evidence.

Generated C# must compile, and reflection must confirm the actual public/static
CLR scalar signature: no arguments and `void` for the loop functions, or one
`Int32` argument and `Int32` result for `Classify`. Runtime checks require exactly
`1..5`, `5..1`, and `0..2` on stdout, one number per line, in that order.
Platform CRLF/LF differences are normalized, but extra text, missing lines, and
wrong ordering fail. Captured loop output is capped at 4096 characters so an
infinite printing loop cannot allocate unbounded memory. `Classify` runs 11
negative/zero/positive cases, including both signed 32-bit boundaries and their
neighbors, against an independent sign oracle.

Presence alone is not a behavior witness. A second executable check uses the
selected emitter's `#line` mappings to locate each required AST construct in C#.
The mapped emission must be syntactically equivalent to the original generated
C#, and each witness must have an unambiguous mapping; otherwise verification
fails explicitly. Test-only scope probes require every captured loop-output
character to originate during a required loop body, including nested statements
and calls. Dormant loops and executed nonprinting loops followed by hardcoded
stdout fail. Header evaluation is not counted as loop-body output.

For `Classify`, a required full conditional must execute on every sampled input.
A separate compiled counterfactual replaces only the mapped required conditionals
with no-ops and appends a sentinel return for direct-return bodies. At least one
sampled classification must change. Dormant conditionals and executed unused
conditionals followed by an equivalent ternary fail; branches that assign a
variable returned afterward remain supported. Instrumented/counterfactual
compilation or execution failures are verification errors, not successful
coupling evidence. These extra runs share the same bounded wrapper deadline.

Run the current controls and actual old-release compatibility controls:

```bash
python3 tests/E2E/agent-tasks/test-control-flow-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --control-flow
```

The compatibility command reuses the unchanged v0.29 source archive, locked
restore, verified native assets, and bounded build process described above. It
runs 67 real old/current control-flow entry-point controls, with historical
source IDs and do-while closer spellings only in temporary old-parser inputs.
Current controls cover aliases, nesting, misleading comments, missing/private/
stub functions, wrong signatures, misplaced constructs/effects, complete branch
requirements, stdout errors, boundary misclassification, generated compilation
and runtime failures, infinite loops, output limits, timeouts, and missing/corrupt
selected roots. Both arms also reject dormant/unused construct witnesses while
retaining nested and assigning implementations. The existing logic suite retains its contract proofs and shared
escaped-output/cancellation cleanup controls.

These are structural and finite executable checks, not universal proofs of
arbitrary control-flow bodies or evidence of agent nonregression. No additional
active `.calr` corpus inputs are enrolled: test snippets live in strings and
disposable workspaces. Completing these four verifiers does not complete the
release or authorize an experiment baseline, paid evaluation, or publication.

### Remaining control-flow prompts (`cf-005` through `cf-008`)

These four entries use the same helper, selected assemblies, debug-contract
runtime, and bounded process cleanup. Empty root/configuration selections and
configuration path traversal reject explicitly; every profile checks the
selected native Z3 asset and loads it, including profiles needing no Z3 proof.
`cf-005` checks `Max(a,b)` on 121 signed
integer pairs. Unlike the separate logic `Max` task, its prompt requires no
postconditions or particular arrow spelling. Optional contracts cannot restrict
the required entry domain or exclude correct outputs; the logic task retains
its mandatory postcondition proofs.

`GradeToLetter(grade)` runs all 101 valid grades and four out-of-domain contract
failures. Z3 proves that the declared preconditions describe exactly `0..100`
over signed 32-bit inputs. Native nested conditionals must execute on valid
inputs, and removing the mapped nested conditionals must change a result.
Native nested ternaries returned directly or stored in a binding also qualify:
their owning statement and exact nested-expression count are mapped, and only
the nested expressions are observed and neutralized. Dormant expression arms
and computed-but-unused nested expressions are not behavior evidence.
This is complete behavior coverage of the finite valid grade domain, not a
proof of arbitrary bodies outside it.

`PrintEvenNumbers()` must declare console-write effects, produce exactly
`2,4,6,8,10` on separate lines, and produce that output inside native `for`
iterations. At least one AST-mapped inner conditional must take both decisions
and affect the output under both constant-decision counterfactuals. Inverted
tests and `continue` guards are supported; dormant or executed unused tests
plus independent correct printing are rejected. Every expected printed value
must disappear under at least one such decision counterfactual; gating just
one value while independently printing the others is insufficient.

`PrintDictionary(Dictionary<string,int>)` permits any parameter name, normalized
string/integer aliases, native ordinary or deconstructing foreach, and any pair
order. Five independent inputs cover empty/singleton/multiple dictionaries,
changed key/value associations, zero, negatives, and both signed integer limits.
The oracle retains independent input copies even if the function mutates its
argument. Instrumented native iterations must visit each actual input pair once
and print that pair during its own iteration. An unrelated/dormant foreach or
one iteration printing the entire dictionary is insufficient.

Dictionary formatting has a deliberately bounded interpretation: each pair is
two adjacent key/integer tokens, in either orientation, separated by whitespace
or punctuation (also `=`). Separate lines, colon/comma separators, tuple-like
and JSON-like records, and optional `Key`/`Value` labels are accepted without
selecting a parameter name, ordering, or separator style.
Extra/missing/duplicated/mispaired tokens are errors. The
samples use distinct nonnumeric keys: the prompt supplies no escaping or record
grammar with which to resolve arbitrary numeric keys, embedded record text, or
arbitrary prose labels unambiguously. Those formats and arbitrary dictionaries are not
claimed as universally verified. Ambiguous native binding/mapping, nested
instrumented foreach scopes, or failed counterfactual compilation/runtime
reject explicitly rather than becoming evidence.
Compilation remains mandatory: the current selected compiler rejects
`§EACHKV` over a fully-qualified `System.Collections.Generic.Dictionary` type
with `Calor0202`; the verifier does not bypass that product limitation.

```bash
python3 tests/E2E/agent-tasks/test-remaining-control-flow-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --remaining-control-flow
```

The second command builds actual released v0.29.0 using the same unchanged
archive/manifest/locked-restore procedure and runs 111 real old/current entries.
The original 49 logic and 67 `cf-001..004` compatibility controls remain separate
and unchanged. No task definitions, original fixtures, active corpus subjects,
compiler product code, or release state are changed.
