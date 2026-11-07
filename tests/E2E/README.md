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

### Opt-in measured agent-task evidence (POSIX)

The normal `agent-tasks/run-agent-tests.sh` invocation is unchanged: it uses the
existing prompt, teaching, unpinned CLI invocation, and cleanup behavior. It is
not a measured baseline. To opt in, supply all three new options:

```bash
# Only after separately approving the model, arm, task selection, and spending.
# MODEL must be the full versioned Claude model ID, not "sonnet"/"opus"/"default".
CALOR_COMPILER_ROOT="$EXACT_WORKTREE_ROOT" \
CALOR_COMPILER_CONFIGURATION=Debug \
  ./tests/E2E/agent-tasks/run-agent-tests.sh \
  --measured-output "$NEW_ABSOLUTE_OUTPUT_DIRECTORY" \
  --model "$MODEL" --arm "$ARM_LABEL" --task logic-001 --single-run

# Guarded local controls; never calls a provider:
python3 tests/E2E/agent-tasks/test-measured-run.py
python3 tests/E2E/agent-tasks/test-compiler-selection.py
```

This supplies capture plumbing, **not** an approved experiment, paired scheduler,
baseline, budget, or release result. The caller must interleave old/new invocations
on one approved exact model and select identical task/verifier inputs. No historical
inputs, task prompts, primers, frozen ledgers, or compiler semantics are rewritten.
`--arm` is attribution only. The default compiler root is this integration worktree;
the configuration defaults to Debug. The selected root/configuration is built by
the existing `build_compiler` helper, without retries or another-root fallback.
Measured stages disable persistent MSBuild servers/shared compilation to keep
their child lifetimes bounded.

Measured mode supports `--task`, `--category`, `--filter` (case-insensitive regular
expression), `--single-run`, and `--verbose`. It rejects unknown/duplicate options,
conflicting selections, empty selection, invalid task JSON, missing local fixtures,
GitHub clone inputs, symlinked input/output paths, and an existing output directory.
Use canonical absolute paths (on macOS resolve `/var` or `/tmp` to `/private/...`).
Output must be outside both compiler and integration roots; its parent must exist.
There is no `--clean`, output reuse, automatic cleanup, or overwrite in this mode.
The existing setup still preserves a fixture's CLAUDE.md, otherwise checks/copies
the integration-generated teaching reference with the **selected** compiler.
Unsupported old teaching APIs fail setup; they never fall back to another compiler.
No old/new primer policy or syntax translation is inferred.

Only `run.json` with `status: "accepted"` represents valid measured evidence.
An accepted behavioral failure exits 1; invalid evidence exits 2 and is retained
with an explicit error, without aggregate pass/token claims. Preflight failures
before output reservation create no evidence directory and admit no agent.
Do not consume an `attempt.json` alone: a later mutation/cancellation can invalidate
the whole run. `skips` is explicit; v1 rejects invalid selected tasks instead of
silently skipping them, so accepted runs have zero skips.

Each attempt retains initial, pre-verifier agent-output, and final workspaces, raw `agent.stdout` JSONL and
byte-exact `agent.stderr`, setup/verification logs, actual process argv/cwd/timing/
exit/cleanup status, and a unique attempt identifier. The run retains selected
task IDs/arm/model, CLI executable path/hash, compiler version and executable
version check, compiler-image hashes/bytes, Git commit and complete worktree status.
Before any agent admission, the existing selected-compiler helper loads native
Z3 and solves a satisfiable control. Its selected-image identity, exact result,
raw output, and process status are retained as `compiler-native` artifacts.
Missing or corrupt native assets invalidate the run before a provider attempt;
they are not counted as agent behavioral failures.
`test-logic-old-release.py --native-prerequisite` runs the same control against
unchanged archived v0.29 source and the current selected compiler.
Source manifests/copies cover compiler `src/`, build `scripts/`, root-level build
configuration, and the integration agent-task/teaching/supervision sources.
Task, fixture, actual initial input, and teaching hashes are recorded separately.
Compiler images, source manifests, inputs, and retained copies are checked before
agent admission and after each stage; persistent mutation invalidates the run.
Raw capture is checked against the bytes actually read from the pipes.

Token schema v1 follows the documented
[Claude SDK result schema](https://code.claude.com/docs/en/agent-sdk/typescript#sdkresultmessage)
and [cost tracking](https://code.claude.com/docs/en/agent-sdk/cost-tracking).
It accepts one init, one successful final result, one fresh session identity,
and exactly the supplied model in init/assistant messages and `result.modelUsage`.
Full versioned IDs may have a date suffix or omit one. Bare aliases and
`latest`/`default` selectors are rejected; a selector resolving to another
reported ID invalidates evidence. This records the observed model identifier,
not an independently identifiable model-weight checkpoint.
Subagent message streams, extra result envelopes, model fallback, errors,
aborted messages, unsupported event types/system subtypes, missing/empty or
malformed usage are invalid evidence, even when generated code passes.

Tokens come **once** from final `result.modelUsage[MODEL]`: `inputTokens`,
`outputTokens`, `cacheCreationInputTokens`, and `cacheReadInputTokens` map to the
four snake-case fields in the report. Input excludes cache; `total_input_tokens`
adds input, cache creation, and cache read without output tokens. `total_tokens`
adds all four disjoint counts. These are query-pipeline totals (including internal
calls and same-model subagent activity); helper calls outside that pipeline are
not included. Main-loop `result.usage` is validated and retained separately, never
added. Repeated assistant blocks/cumulative envelopes cannot inflate totals.
No token estimation, raw-text-length proxy, billing estimate, or zero-fill is used.
Missing usage cannot supply an old/new comparison.

Attempt pass counts and **task majority votes** are separate: by default all three
attempts run and two must pass; `--single-run` requires one of one. Reports retain
both denominators. `tokens_per_attempt` divides task tokens by its attempt count;
`tokens_per_task_all_attempts` averages task totals including every voted attempt.
The separate `input_tokens_total`, `input_tokens_per_attempt`, and
`input_tokens_per_task_all_attempts` fields use input plus both cache counts only.
Neither divides by only successful attempts. Unique assistant message IDs and
tool-use IDs give observed message/tool counts; CLI `num_turns` is labeled separately.
None is called iterations-to-green. Agent compiler/build iteration counts and
iterations-to-green remain explicitly null/unobserved; real verification stage
status/logs are retained, not substituted for agent iteration metrics.

Build/setup/verification each have a 300-second deadline, compiler version has
30 seconds, and each agent uses the unchanged task timeout (v1 admits 1–3600
seconds). Pipe EOF and final capture validation share that deadline; each raw
stream is capped at 64 MiB. A slow identity scan cannot admit late completion.
Cancellation/timeout cleanup reuses the repository's birth-identity-checked
process tracker, signals only recorded groups/PIDs, and closes inherited pipes
without an unbounded drain. Cleanup has a three-second signaling budget plus at
most two bounded two-second process-table queries and a one-second root reap;
an exhausted cleanup budget invalidates evidence rather than retrying forever.
This is supervision, **not a sandbox**: a descendant
that escapes and reparents before it is observed cannot safely be identified or
killed. Such retained-pipe hangs fail the evidence deadline. Point-in-time hashes
cannot prove absence of transient edit-and-restore attacks. CLI user/global
settings, hooks, network helpers, and external toolchain dependencies are not
hermetically frozen; no settings/teaching changes are silently imposed to hide
that limitation. Keep raw artifacts private and audit these external influences
before interpreting real paid results.

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

## Basic-syntax task behavioral verification

The four `basic-001` through `basic-004` `verify.sh` entry points use the same
disposable helper, selected compiler assemblies, immutable source read, and
bounded 120-second process/EOF cleanup as the logic tasks. They read
`Calculator.calr`; their original task definitions and fixtures are unchanged.
Run their controls with:

```bash
python3 tests/E2E/agent-tasks/test-basic-syntax-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --basic-syntax
```

The distinct `basic001`..`basic004` internal selectors prevent a name collision:
basic `IsPositive` needs no contracts, while logic `IsPositive` still requires
its original full contract proofs. Exact parsed public, non-async, non-generic
signatures and generated public/static CLR signatures are checked. `GetZero`
has zero parameters and an `Int32` result, not the loop profiles' inferred
`void`. `int`, `i32`, and mixed aliases use selected-compiler type normalization.
No new purity, body-shape, formatting, or mandatory contract requirements are
imposed.

Runtime contracts remain enabled when compiling and executing selected-generated
C#. Optional preconditions (including inline `#` refinements) must admit every
prompt input, proved with the selected Z3 translator and five-second solver
limit. Entry proofs accept total comparison/boolean/conditional/implication
expressions and fail explicitly on unsupported expressions or unknown results;
they never discard runtime inputs to fit a guard. Optional postconditions are
executed, not treated as a required basic proof. Logic's proof restrictions
and control-flow's raw-effect and executable-coupling checks are unchanged.

`GetZero` executes its one zero-argument case. `IsPositive` executes 11 signed
boundary, negative, zero, and positive cases. `Multiply` executes 205 independently
computed products from 19 varying factors, including zero, one, negatives,
ties, commuted pairs, signed boundaries, and large signed products. Its oracle
computes in `Int64` and admits only products representable by CLR `Int32`.
Both checked and unchecked implementations pass; the prompt specifies no
overflow policy. These finite body checks are not a universal arithmetic proof.

`PrintNumber` must explicitly declare console-write on the original parsed
function before effect synthesis. It executes 11 signed inputs against an
independent exact numeric-line stdout oracle. CRLF/LF is normalized; extra
text, wrong numbers, comment decoys, and output without the numeric line fail.
Captured output retains the shared 4096-character cap.

The separate old-release mode archives and builds the actual unchanged
v0.29.0 tree using the same verified-asset/locked-restore mechanism as the
existing modes. It executes 49 actual old/current basic entrypoint controls;
missing prerequisites or errors fail, never skip or substitute a current
compiler. Current controls additionally exercise basic/logic selector collisions,
optional unrelated/weakened contracts, narrowed guards, signatures, wrong
behavior, raw effects, runtime errors, and bounded deadlines. This closes only
four verifier prerequisites, not all agent tasks or release validation.

## Enum task behavioral verification

The `enum-001` through `enum-003` entry points read `Enums.calr` through the
same selected-compiler helper, single immutable source read, debug-contract
runtime, and bounded 120-second process/EOF supervision. Every profile checks
the selected root, configuration, and native assets and unconditionally loads
Z3, even though the enum prompts require no contract proof.

```bash
python3 tests/E2E/agent-tasks/test-enum-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --enums
```

The selected AST must contain the named native public module enum and every
required member. The selected-generated C# is independently compiled, and
reflection checks that each member belongs to the actual public CLR enum
in that assembly. `Color` requires a native explicit assignment on each of
`Red`, `Green`, and `Blue`, and their reflected values must be exactly `1`,
`2`, and `4`. The prompt does not require a `Flags` attribute. `Status` and
`Priority` impose no numeric assignments, member ordering, underlying type,
or exclusion of additional members.

`Priority.IsUrgent` must belong to the native `Priority` enum extension.
Reflection requires a public, nongeneric static boolean extension method
with one value parameter of the actual CLR `Priority` type. Parameter names
and body constructs are not prescribed. All four required named inputs execute
against independent expected results: false for `Low` and `Medium`, true for
`High` and `Critical`. Optional contracts and inline parameter refinements
must admit those inputs; guards never filter the runtime oracle. Contract and
execution errors fail explicitly.

The actual-old mode archives unchanged v0.29.0 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its native prerequisites,
and uses locked restore/build through the existing 600-second runner. It runs
127 real old/current entrypoint controls, including current-parser rejection
of historical IDs. Old IDs and native old refinement syntax appear only in
temporary inputs. Missing assets, history, APIs, builds, or controls fail rather
than skip or substitute a current compiler.

Current controls also cover interop/comment/unrelated decoys, duplicate and
missing declarations, explicit assignment omissions and wrong values,
private/wrong signatures, wrong urgency for each named input, narrowed guards,
runtime errors, infinite loops, deadlines, and selected native-asset failures.
These checks cover the required named enum values, not undefined underlying
values or additional members' urgency. Finite body execution is not a universal
proof of arbitrary implementations or agent nonregression. Original task
definitions, fixtures, and corpus membership remain unchanged.

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
failures. Z3 proves that the combined preconditions and inline parameter
refinements describe exactly `0..100` over signed 32-bit inputs. Inline `#`
bindings use the selected translator's parameter scope; either native runtime
guard form must reject out-of-domain inputs. Native nested conditionals must execute on valid
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
archive/manifest/locked-restore procedure and runs 117 real old/current entries.
Temporary old controls use the released `§I` parameter-refinement form because
v0.29 does not parse predicates inside compact signatures. Predicate bytes and
domain expectations are unchanged; current controls retain compact signatures.
The original 49 logic and 67 `cf-001..004` compatibility controls remain separate
and unchanged. No task definitions, original fixtures, active corpus subjects,
compiler product code, or release state are changed.

### Contract-writing prompts (`contract-001` through `contract-005`)

These five entries add separate `contract001..005` profiles to the existing
selected-compiler helper. They load the selected native Z3, inspect the actual
target function AST, compile emitted debug-contract C#, and invoke the reflected
public/static Int32 signatures. `contract-003` selects `BuggyContracts.calr`;
the other four select `Calculator.calr`. Required predicates must belong to the
target, not another function, comments, or strings. Original explicit
preconditions and inline parameter refinements use the selected public
translator, including its push/pop scope for `#`; there is no textual predicate
replacement. Existing logic/basic/control-flow profiles retain their separate
requirements and oracles.

`SquareRoot(x)` returns **x**, as its actual prompt requests, not a mathematical
square root. Its preconditions must describe `x >= 0`; ten signed/boundary runtime
cases check identity, and three negative inputs must fail a native contract.
Both `Abs(x)` tasks require `result >= 0`, including zero, and run 18 independent
absolute-value cases. The original buggy fixture is a negative control; changing
only `result > 0` to `result >= 0` is a positive control.

`SafeDivide(a,b)` requires both `b != 0` and `a % b == 0`, not merely a nonzero
divisor. Its postconditions must imply `result * b == a` with representable
mathematical products, and admit the independently determined exact quotient.
109 signed/zero/boundary cases exercise exact divisions, including negative
divisors and Int32.MinValue divided by representable divisors. Six zero-divisor
or inexact-division inputs must fail a contract, not accidentally divide by
zero. The native emitters evaluate explicit preconditions before an inline
refinement guard: an inline `b != 0` plus an unguarded explicit `a % b == 0`
throws before the inline guard on zero. Controls reject this ordering and
accept a short-circuiting explicit predicate with the same inline refinement.

`SumToN(n)` requires the Gauss formula, `n * (n + 1) / 2`, **not a loop or loop
invariant**. 107 independent widened-arithmetic runtime cases cover `0..100`
and larger boundaries through 46340; three negative inputs must fail a contract.
Native return/binding formulas support commuted factors and multiplication/
division rearrangements that retain the correct sampled results. Each formula
maps to exactly one emitted statement; replacing its result with zero must
change a sampled outcome or violate a contract. A loop-only implementation or
an unused formula cannot provide that algorithm evidence. Unmapped witnesses,
counterfactual compilation failures, or unexpected runtime failures reject
explicitly. This is finite dependency evidence, not universal body correctness.

Z3 checks entry-domain equivalence, required postcondition implication, and
optional-contract admissibility for independently computed correct outputs.
Supported contract expressions are signed Int32 literals/references, native
scoped `#`, comparisons, boolean/conditional/implication expressions, and
addition/subtraction/multiplication/division/remainder with explicit
representability and definedness checks. Unknown references, calls, unsupported
nodes or widths, translator warnings, and solver unknown/timeouts fail rather
than becoming proof evidence. Optional predicates cannot narrow required
representable inputs or reject their correct outputs.

The prompts do not specify an overflow policy. Abs(Int32.MinValue) has no Int32
absolute value, and Int32.MinValue / -1 has no Int32 quotient; those ambiguous
cases are outside output/runtime proofs. Hypothetical overflowing products in
the division postcondition are also outside its implication scope. Sum output
proofs and runtime cases use `0..46340`, where the original Int32 Gauss
intermediates fit. `46341..65535` have representable mathematical sums but
overflow the original Int32 product; above 65535, the sum itself cannot fit.
No behavior there is claimed and no checked/wrapping/widening policy is chosen.
Sum's explicit precondition still must describe **all** `n >= 0`; adding a
46340 cap is rejected, not silently accepted as the task's contract.
Successful entries print these limits. Predicate proofs and finite executable
oracles do not establish agent nonregression.

```bash
python3 tests/E2E/agent-tasks/test-contract-writing-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --contract-writing
```

The strict compatibility mode archives unchanged released v0.29.0 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its own native size/hash
pins, locked-restores and builds its source, then runs 81 actual old/current
entry-point controls. Only disposable old-parser strings receive historical IDs
and native `§I{i32:x} | (predicate)` / `§O{i32}` parameter spellings; predicates
and current inputs are unchanged. Missing history, prerequisites, APIs, builds,
or controls fail without skips. No current compiler assemblies substitute for
the released source build. The shared selected-root/configuration/native guards
and 120-second process/EOF/cancellation cleanup remain unchanged.
All task definitions, original fixtures, TestData, active corpus and historical
inputs remain byte-unchanged. No provider/model/count API, paid run, push,
publication, version, ledger, floor, or release/workflow change is involved.

The current suite executes 146 real contract entry points plus eight preserved
mandatory logic entry points. It includes native alternatives, aliases,
equivalent/inline predicates, optional-contract restrictions, foreign/lexical
decoys, missing/private/wrong-signature/stub targets, signed/zero/boundary
results, checked and unchecked representable arithmetic, missing selected
dependencies, compilation/runtime failures, and bounded nontermination.
