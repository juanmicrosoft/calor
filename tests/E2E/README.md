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
invariant**. All 65,536 inputs `0..65535` execute against an independent cumulative
sum oracle, including `SumToN(46341) = 1073767311` and
`SumToN(65535) = 2147450880`; three negative inputs must fail a contract.
Gauss witness recognition normalizes native integer arithmetic algebraically,
including signed Int32/Int64 casts, widened immutable bindings, expanded
polynomials, commuted factors, and equivalent parity-based factoring.
This recognition does not model cast truncation or division rounding as real
arithmetic correctness: the actual selected-emitter body must separately
produce the exact integer result for every representable input.

The debug mapped emission must match the original compiled code. A separate
contract-disabled selected-emitter probe must first produce the same correct
outputs for all 65,536 inputs. Each recognized native return/binding witness
maps to exactly one probe statement; its zero counterfactual must execute
successfully on the entire domain. At least one successfully observed returned
value must change. A contract violation, other runtime error, or compilation
failure never supplies output-dependence evidence. Original Debug compilation,
predicate proofs and runtime/domain contracts remain mandatory. This preserves
correct strong postconditions such as `result >= n`: invalidating those guards
in a mutant is neither success evidence nor a reason to reject the correct
original body. A loop-only implementation, an unused formula, or a formula
feeding only a helper precondition cannot provide output-dependence evidence.
Unmapped/ambiguous witnesses and unsuccessful probes reject explicitly.
This is finite dependency evidence, not a universal proof of arbitrary bodies.

Z3 checks entry-domain equivalence, required postcondition implication, and
optional-contract admissibility for independently computed correct outputs.
Sum's admissibility and arithmetic-safety checks exhaust all 65,536 correct
input/output pairs by substituting concrete bit vectors into the selected
translator's original expressions and requiring each native Z3 simplification
to be true. No textual predicate replacement, candidate-body output, skipped
input, nonlinear approximation or solver-unknown fallback supplies that evidence.
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
checks cover **all** `0..65535` representable mathematical sums. The naive
Int32 product/division body is rejected in both checked and unchecked modes:
throwing or returning a wrapped, incorrect result for a representable sum is
not an overflow-policy ambiguity. Legitimate widened intermediates and native
factoring are positive controls on both compiler arms. Above 65535, the sum
itself cannot fit, so no behavior is claimed and no overflow policy is chosen.
Sum's explicit precondition still must describe **all** `n >= 0`; adding a
46340 or 65535 cap is rejected, not silently accepted as the task's contract.
Successful entries print these limits. Predicate proofs and finite executable
oracles do not establish agent nonregression.

```bash
python3 tests/E2E/agent-tasks/test-contract-writing-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --contract-writing
```

The strict compatibility mode archives unchanged released v0.29.0 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its own native size/hash
pins, locked-restores and builds its source, then runs 97 actual old/current
entry-point controls. Only disposable old-parser strings receive historical IDs
and native `§I{i32:x} | (predicate)` / `§O{i32}` parameter spellings; predicates
and current inputs are unchanged. Missing history, prerequisites, APIs, builds,
or controls fail without skips. No current compiler assemblies substitute for
the released source build. The shared selected-root/configuration/native guards
and 120-second process/EOF/cancellation cleanup remain unchanged.
All task definitions, original fixtures, TestData, active corpus and historical
inputs remain byte-unchanged. No provider/model/count API, paid run, push,
publication, version, ledger, floor, or release/workflow change is involved.

The current suite executes 170 real contract entry points plus eight preserved
mandatory logic entry points. It includes native alternatives, aliases,
equivalent/inline predicates, optional-contract restrictions, foreign/lexical
decoys, missing/private/wrong-signature/stub targets, signed/zero/boundary
results, checked and unchecked representable arithmetic, missing selected
dependencies, compilation/runtime failures, and bounded nontermination. Both
arms include the formerly accepted `n == 46341` return-zero decoy and the native
unused-formula/helper-precondition/ordinary-loop decoy. A separate mutant-only
division-by-zero decoy also rejects; failed execution is never coupling success.

### Current idiom prompts (`idiom-001` and `idiom-002`)

The historical directory names `01_option_return` and `02_match_option` do not
describe these tasks. Their unchanged `task.json` prompts require
`Clamp(min, max, value) -> i32` and `IsEven(n) -> bool`. Neither asks for Option
syntax or a particular body construct. Both actual `verify.sh` entries select
new `idiom001` / `idiom002` profiles in the existing compiler-backed helper.
The original logic `Clamp(value, min, max)` selector, its 726 runtime cases,
and all other existing profiles remain separate.

The selected public AST and reflected public/static CLR signatures must match
the prompt's parameter names, order, integer types and return type. Native
aliases such as `int` are accepted through the existing `ExpandType` helper.
Clamp requires entry-domain equivalence to `min <= max`; its postconditions
must imply both inclusive result bounds and admit the mathematically correct
clamp result. IsEven requires a mandatory postcondition equivalent to
`result == (n % 2 == 0)`, using signed remainder, across the entire signed
Int32 domain, including Int32.MinValue. One-way implications do not suffice.

These profiles reuse the selected public contract translator and scoped
inline-refinement entry mechanism. Explicit and inline predicates may describe
the required domain equivalently; optional predicates cannot narrow it or
exclude correct outputs. Arithmetic must be defined and representable in the
evidence scope. Short-circuiting predicates are modeled with their evaluation
guards. Unsupported nodes, foreign/scoped references, widths, calls, translator
warnings, solver unknown and timeouts reject rather than supply proof evidence.
No predicate from another function, comment, string or candidate body substitutes
for the selected declaration's own contracts.

Independent runtime oracles exercise 3,610 Clamp triples with `(min,max,value)`
positional arguments, all combinations of 19 signed/boundary values with valid
bounds. Three reversed-bound inputs must fail a native domain contract. IsEven
runs 521 distinct inputs: the union of `-256..256` and 19 signed/boundary inputs
against a widened signed-remainder oracle. These are executable samples, not universal body proofs;
contract proofs do not establish body correctness. Constant/boundary/unused-
expression decoys, wrong parameter order, weakened/vacuous contracts and
optional-domain laundering are negative controls. Successful entries explicitly
print the proof domain and runtime limitations.

```bash
python3 tests/E2E/agent-tasks/test-idiom-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --idioms
```

The additive strict `--idioms` mode archives unchanged released Git commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its own pinned native
manifest, locked-restores and builds that source, and links the same helper
against each actual old/current arm. It executes 81 actual entry-point controls,
including current-parser rejection of an old-ID input. Only temporary old
controls receive historical IDs and native `§I` refinements through the existing
adapter. Missing assets/history/APIs, build failures, skipped/empty controls and
unknown proof results fail; no copied-current old arm or product API patch exists.
The runner retains all old modes, its 600-second total bound and the shared
120-second entry/process/EOF cleanup bound. Current controls also retain the
logic/basic selector collisions, native-only prerequisite, selected-root/
configuration rejection and bounded nontermination checks. The current suite
executes 97 idiom entries, 14 preserved logic/basic/enum/flow entries and one
native-only entry; successful output counts are checked, not merely printed.

This unit replaces verification for only these two existing tasks. It does not
complete all 129 task prerequisites or establish milestone/release acceptance.
Task definitions, fixtures, TestData, active `.calr` corpus, historical ledgers,
floors and product source remain unchanged. No provider/model/count/spending,
publication/version/release or grammar change is involved.

## Generic behavioral prerequisites (gen-001..003)

The three existing generic tasks use the shared selected-compiler verifier, not
text searches. They require native public `Identity<T>`, `Container<T>`, or
`Repository<T>` declarations. The verifier independently compiles the emitted
C# and reflects its actual CLR generic parameter, signatures, private storage
field, and (for Repository) reference-type constraint. Raw C# interop anywhere
in the input, comments, and unrelated declarations cannot satisfy these checks.
This closes only three verifier prerequisites for #1605, not all 129 tasks or
the 0.30 milestone.

Identity reports return identity, not private storage evidence. It runs 21
input/return identity cases across Int32, Int64, Guid, string,
object, and List<Int32>. Container runs 42 state steps across the same six closed
types, using two real instances per type. Each step independently writes the
actual private T field, checks Get, changes that field to a different value,
executes Set, and checks both the stored value and Get. Repository runs 24 append
steps across string, object, and List<Int32>. It snapshots the actual private
List<T>, then checks every appended element's reference identity, order, count,
prior contents, and isolation from a second instance. Inputs include null,
empty strings, equal-content distinct references, repeated values, and value-type
extremes. These are finite behavioral oracles, not universal verification.

Parameter and field names are unrestricted. Container Get/Set need not be
public; Add must be public. Set/Add return types are not prescribed. Extra
generic constraints are allowed; both `class` and `class?` establish the required
CLR reference-type constraint. The independently selected closed-type pool
must contain at least two valid instantiations, or the verifier reports
`Unsupported finite closed-type setup` rather than guessing suitable types.
Class setup uses real public or nonpublic parameterless constructors, or an
unambiguous one-argument T/List<T> constructor. List constructor inputs contain
independent seed values. Other constructor shapes report `Unsupported finite
constructor setup`; no uninitialized-object bypass is used. Initial stored
values/list contents need not be default or empty. Repository may initialize
its list lazily inside Add. Additional private generic storage fields are
allowed. Every candidate field is tested using fresh real instances; at least
one field must satisfy all state, identity, and isolation checks for each closed
type. Storage is established by behavior, not declaration order or a guessed
field name. Runtime errors still reject; they are not treated as unsuccessful
field matches.

```bash
python3 tests/E2E/agent-tasks/test-generic-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --generics
```

The current suite executes exactly 103 real verify.sh controls: 77 native
acceptance/rejection cases (25 positive, 52 negative), 15 missing/invalid/
interop/ambiguity controls, three runtime/deadline controls, and eight selected
root/configuration/native-asset controls. It requires 26 successful controls
and 77 rejections overall. The historical mode archives unchanged release
commit `d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its own native-asset
manifest, restores locked dependencies, and builds that release's own compiler.
Only disposable inputs acquire historical declaration IDs. The mode executes
the same 77 cases in each actual old/current arm, plus rejection of historical
syntax by the current compiler: exactly 155 controls (50 positive, 105 negative).
There is no copied-current baseline, fallback, skip, or empty successful run.
The existing 120-second per-profile and 600-second historical total bounds,
unconditional native prerequisite, other profiles, and measured no-admission
gate remain unchanged. Original task definitions, fixtures, active .calr
subjects, product code, corpus membership, ledgers, and floors are unchanged.

### Original string prompts (`str-001` through `str-005`)

The five unchanged `task.json` prompts are authoritative, not their broader
directory titles. They require `StringLength(str s) -> i32`,
`StringContains(str s, str sub) -> bool`, `GetFirstChar(str s) -> char` with
`length(s) > 0`, `IsValidEmail(str email) -> bool` using regex pattern `"@"`,
and `BuildGreeting(str name) -> str` using a newly constructed StringBuilder.
There are no upper/lower, starts/ends, or RFC email-validation requirements.
Each existing `verify.sh` now selects its own `str001`..`str005` profile in the
shared selected-compiler helper. Native aliases and supported equivalent BCL
forms are accepted, including `s.Length`, `s.Contains(sub)`, `Regex.IsMatch`,
and builder method calls, not just one lexical spelling.

The selected native/public AST and reflected public/static CLR method must
have the exact names, parameter order, string parameters and int/bool/char/string
return types. Debug public emission is independently compiled and compared
against selected-public `#line` emission before mapping native witnesses.
Length, contains, index-zero char access and pattern-`"@"` regex must contribute
to returned outputs, through data or control dependence. Statement decisions,
conditional expressions and immutable local decision bindings are supported.
Merely placing a builtin in an unused binding, output-independent branch
condition, comment or unrelated helper cannot supply the witness. Independently
compiled contract-disabled baselines must preserve behavior.
Neutralizing mapped operations must change
outputs; mapping ambiguity, compilation failures and counterfactual exceptions
reject, rather than count as dependence evidence.

Builder checks instrument native construction, appends and ToString in the
selected public emission. Every sampled returned string must be the actual
ToString result of the same newly created instance whose append arguments are
exactly `"Hello, "`, the input name, and `"!"`, in order. Nested/chained appends,
local aliases and BCL method calls are supported. Append neutralization must
change the greeting. A dead builder followed by independent concatenation,
different instances, constructor-seeded prefixes, wrong ordering and
noncontributing appends cannot pass.

Independent host CLR oracles use 23 strings: empty/nonempty values, case-sensitive
matches, signed-like punctuation, `@` in otherwise invalid email-like strings,
quotes, whitespace/newlines, composed/decomposed Unicode, non-BMP surrogate
pairs, isolated surrogates and embedded NUL code units. Length runs 23 cases,
contains runs all 529 ordered pairs against an explicit ordinal code-unit search,
first-char runs 22 nonempty cases, regex runs 23 cases against literal-`@` search,
and greetings run 23 cases against independent concatenation: 620 behavior
cases per complete positive five-function set. GetFirstChar additionally must
reject empty input via a native explicit or inline contract, not an index error.

Contract equivalence is proved only in a test-only integer-length abstraction:
each non-null CLR string has a length in `0..Int32.MaxValue`. Supported native
length predicates use integer literal comparisons and Boolean conjunction,
disjunction and negation, including native inline `#` refinements. GetFirstChar
must admit exactly positive lengths; other profiles cannot narrow the non-null
prompt domain. Optional supported predicates must be tautological on that
domain. Arithmetic, calls, result predicates and nullability predicates outside
this fragment fail closed. Solver unknown/timeouts do not count as proofs.
No Z3 Unicode-string length is equated with CLR UTF-16 length. These finite
runtime checks are not universal body proofs, and neither null behavior nor
arbitrary string/char postconditions are proved.

```bash
python3 tests/E2E/agent-tasks/test-string-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --strings
```

The current suite executes six tests and 135 real `verify.sh` controls. It
checks success counts, equivalent native/BCL forms, exact native/CLR types,
missing and narrowed contracts, builtin-dependent decisions, unused and
output-independent builtins, dead/different builders, ambiguous mappings,
counterfactual runtime errors, corrupt native
images, root/configuration rejection and bounded nontermination.

The additive strict `--strings` mode executes 137 actual old/current entrypoint
controls: 68 against each arm and current rejection of an old-ID input. It uses
the existing archive of `d82f3ae95175ecd09581c6922701706be72b0ca9`, its own
native manifest verification, locked restore and unchanged compiler build.
Only disposable old inputs receive IDs and old native `§I` refinements.
Both arms exercise genuine positives (including statement, conditional-expression
and immutable-binding decisions), signature/contract failures and behaviorally
correct unused-builtin/dead-builder decoys. The tautological `length(s) >= 0`
branch followed by independent Enumerable.Count is rejected because neutralizing
the builtin leaves every output unchanged. Missing native assets,
history/APIs, build errors, skipped/empty controls or unknown proofs fail; there
is no current-binary stand-in or fallback. Existing old modes, the 600-second
total bound, and the shared 120-second entry/process/EOF bound are unchanged.

This unit changes only these five behavioral prerequisites. Task definitions,
fixtures, TestData, active `.calr` subjects, corpus membership, historical
ledgers/floors and product source remain byte-unchanged. It does not run an
agent/provider or establish milestone, experiment or release acceptance.

### Collection construction and behavior (`coll-001` through `coll-006`)

The six existing collection entries use the shared compiler-backed helper,
with separate `coll001` through `coll006` selectors. Their unchanged prompts
require `SumList() -> int`, `CreateScores() -> int`, `UniqueCount() -> int`,
`BuildCollection() -> int`, `HasValue(value: int) -> bool`, and
`GetListLength(n: int) -> int`. Titles do not add requirements.
The selected native public AST and reflected public/static CLR signatures
must agree on names, parameter order, scalar widths and return types.
`int`/`i32` aliases use the existing public `ExpandType` helper.

The original Debug compilation must first produce independently correct
results. Debug emission with the selected emitter's `#line` mapping must be
syntax-equivalent to that compiled C#. A contract-disabled emission is then
instrumented using resolved BCL symbols and native statement mappings, not
identifier spelling or source-text matches. Its unchanged baseline must also
produce the correct results before any counterfactual executes.
Construction, Add, Count, Contains and iteration records retain the actual
collection object identity and snapshots of its actual elements or key/value
pairs. Native constructors/operations, BCL Add/Count, concrete aliases, native
helpers, and supported BCL factories are exercised without changing product APIs.

`SumList` requires an initially empty `List<int>`, actual Adds of 10, 20, 30,
and native iteration/indexed-loop reads of those actual values. Its result is
60. A counterfactual changes each observed iteration value by 7 and must
successfully return the independently expected sum, 81. A loop adding constant
20, an unrelated range loop, unused actual iteration, or a conditional constant
that merely changes under mutation cannot supply this evidence.

`CreateScores` requires an initially empty `Dictionary<string,int>` and the
actual Alice/95, Bob/87, Carol/92 Add sequence and snapshots, then returns its
count, 3. `UniqueCount` requires an initially empty `HashSet<int>` and all six
actual Adds, 1, 2, 2, 3, 3, 3. Every duplicate Add executes and is observed even
though it does not change the set. Actual snapshots grow to `{1,2,3}`; only the
returned Count must affect output, not each duplicate Add.
`BuildCollection` independently observes its empty integer list, Adds 1 and 2,
and empty integer/string dictionary, Adds 1/one, 2/two, 3/three. It returns
their actual combined count, 5. Each collection must independently supply its
contribution. Identity-specific Count counterfactuals must return the expected
original result plus 1, not simply any different result.

`HasValue` observes initialization completed before membership on the same
actual list, with the exact state `[10,20,30]`, and a real Contains call with
the supplied value. Constructor initialization, an empty construction followed
by Adds of 10, 20, 30, and constructor prefixes `[10]` or `[10,20]` followed by
the remaining Adds are accepted. Every Add must have the exact expected
sequence and per-Add snapshot; restoring a wrong intermediate snapshot before
Contains does not supply that evidence. All 21 signed/boundary inputs
have independent membership oracles, including Int32.MinValue, Int32.MaxValue,
the three members and their adjacent nonmembers. Inverting the observed Contains
result must successfully invert the returned bool.

`GetListLength` proves exact entry-domain equivalence to **all** `n >= 0`
through the existing selected public/scoped contract translator, including
inline refinements and arithmetic definedness/admissibility. Optional guards
cannot exclude required inputs; finite runtime limits never become accepted
precondition caps. Native runtime contracts must reject Int32.MinValue, -17
and -1. Executable inputs are exactly
`0,1,2,3,5,7,16,31,64,127,257`. Each execution observes an initially empty list,
actual loop-body Adds of precisely `1..n` to that same list, its exact snapshots,
and its returned Count. Adding `n` repeatedly, adding 1 repeatedly, an unrelated
loop, or returning `n` beside unused Count observations rejects.
Large allocations, large-input bodies and arbitrary unsampled behavior remain
unproven. These checks are not a universal body proof.

Unsupported native mappings, compilation failures, runtime failures, solver
unknown/timeouts and unsuccessful mutants reject explicitly. The current native
compiler rejects implicit `List<i32>` to `IList<i32>` assignments/native PUSH on
that interface (`Calor0202`). A nested
`Enumerable.ToList(Enumerable.Select(..., native lambda))` spelling also reports
unknown effects (`Calor0411`/`Calor0410`); it is a failing control, not an accepted
factory or a skipped proof. The valid List constructor taking the same Select
sequence is exercised instead. No native-grammar/API or effect-enforcement patch
works around these limitations. The unchanged collection fixture's initialized
SumList is rejected for lacking the prompt's empty construction/Add sequence;
its existing native initialized-list block is separately a HasValue positive.

```bash
python3 tests/E2E/agent-tasks/test-collection-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --collections
python3 tests/E2E/agent-tasks/test-compiler-selection.py
python3 tests/E2E/agent-tasks/test-logic-verifiers.py TimeoutWrapperTests LogicVerifierTests.test_selected_arm_has_no_parent_fallback LogicVerifierTests.test_shared_compilation_selects_same_arm
```

The current suite requires exactly **122 real collection entrypoints**, plus
26 preserved logic/basic/control-flow/enum/contract/idiom profile controls and
one selected native-only control. It covers exact states, wrong values/keys,
missing/reordered duplicate Adds, dead/unrelated collections, initialized versus
empty construction, private/missing/wrong signatures, optional-contract
restrictions, helpers/aliases, successful counterfactual oracles, mutant-only
exceptions and bounded nontermination. The last two commands add nine selection,
process/EOF and cancellation tests, including three actual preserved logic
entrypoints. The 120-second entry/process/EOF cleanup mechanism is unchanged.

The additive strict `--collections` mode archives unchanged released Git commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its own native asset manifest,
locked-restores and builds that source, and links the same helper to each actual
old/current compiler arm. It requires exactly **109 actual entrypoints**, with
old IDs/native `§I` refinements only in disposable historical inputs, and checks
current-parser rejection of an old-ID input. No copied-current arm, skip,
empty/unknown run, fallback evidence or historical fixture mutation is accepted.
All previous strict modes and the 600-second total historical deadline remain.

This unit completes only these six behavioral-verifier prerequisites. Task
definitions, original fixtures, TestData, active corpus membership, historical
inputs, ledgers, floors and product code remain byte-unchanged. It does not
establish full task-set, agent, milestone or release acceptance.

### Scalar pattern verifier prerequisites

The original `pm-001`, `pm-002`, `pm-003`, and `pm-006` wrappers use the shared
selected-compiler helper rather than text matching:

```bash
python3 tests/E2E/agent-tasks/test-scalar-pattern-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --scalar-patterns
```

The controls exercise 79 current entrypoints (34 acceptance, 45 rejection).
The strict historical mode requires 97 actual unchanged released/current
entrypoints, including current rejection of an old declaration-ID input.
Both compiler arms execute the same explicit bounded selection, covering every
task's native behavior and coupling, aliases, contracts, and decisive decoys.
It archives and builds release `d82f3ae95175ecd09581c6922701706be72b0ca9`
using that tree's locked dependencies and verified native asset manifest.
The 600-second total and 120-second entry/process/EOF bounds are unchanged.

Finite negative admission requires an exact outer exit-1 wrapper and the same
exact status/format for every nested wrapper, plus the existing failure
diagnostics. UNKNOWN, solver timeouts, OS/launch errors, EOF, cleanup, inherited
pipes, process-group failures and global deadlines propagate before real
counters change. Positive output must contain exactly one complete selected
compiler identity for the requested resolved root, configuration and
`net10.0/calor.dll`, with no infrastructure or failure-shaped contamination.
`ScalarAdmissionTests` mocks the actual `execute()` path; its synthetic calls
do not contribute to the 79/97 real controls.

The 97-entry strict replay uses two isolated workers per arm, with locked
accounting and a fresh disposable helper for each actual entry. Both arms remain
sequential; all 48 representatives per arm and the final old-syntax rejection
are retained. The shared 600-second deadline is unchanged, and each outer entry
is additionally capped at 120 seconds. Worker exceptions propagate. Synthetic
worker tests contribute no real controls. Before this scheduling change, the
parent current 79-entry suite passed on `8e970b45f` in 476.297 seconds, but the
sequential strict replay exhausted its total deadline after the archived arm.
That failed replay is not acceptance evidence.

Exact public native and reflected CLR scalar signatures are required. DayName
and IsSingleDigit require their literal cases and wildcard default in a native
switch expression. ScoreCategory requires supplying relational cases; reversed
less-than threshold ordering is also supported. DescribeNumber supports native
expression or statement switches with two executed variable-pattern guards and
a wildcard default. Guards must reference their actual captured variables.
Parameter names, integer aliases, immutable input/result aliases, harmless
additional switches, and optional valid contracts are supported.

Baseline signatures each execute 1,031 signed, boundary and nearby-value cases.
Other native integer literals add their representable neighbors to the pool.
The helper checks original Debug runtime contracts and proves entry predicates
admit all signed Int32 inputs. A separately compiled contract-disabled baseline
must agree. Controlled traces then record each actual native switch arm and its
returned value; both must supply the independent expected output. An exact
selected-public-emitter/source-mapped intervention replaces only the supplying
switch input with its bitwise complement. Every counterfactual must return the
independently expected complemented-input output; compile/runtime errors reject.
Unused switches, constant-input switches, dead required arms/defaults and
tautological captured-variable decoys cannot supply this evidence.

These are four bounded behavioral prerequisites, not universal arbitrary-body,
output-predicate, agent-nonregression or milestone acceptance. Carrier pattern
tasks `pm-004` and `pm-005` remain separate. Original tasks, fixtures, TestData,
active corpus membership, historical ledgers/floors and product code are unchanged.

## OOP prerequisites (oop-001..006)

Only the six existing `oop-features` entry points change. They read
`Domain.calr` through the shared selected-root/configuration verifier, load
that compiler's native Z3 prerequisite, inspect native AST declarations,
and independently compile and reflect the emitted C#. Recursive raw-interoperability
rejection and syntax-equivalent source-mapped emission prevent comments,
interop, lexical decoys, and unrelated members from supplying the evidence.
These are six verifier prerequisites for #1605, not agent acceptance, all
129 tasks, or milestone/release validation.

| Task | Required evidence | Canonical runtime cases |
|------|-------------------|-------------------------|
| oop-001 | Public Person class with no source members; compiler-synthesized default constructor allowed | 0 |
| oop-002 | Public IRepository interface, abstract body-free GetById(integer id), actual closed CLR Option&lt;Entity&gt; return type | 0 |
| oop-003 | Public Calculator and public Add(integer a, integer b), correct representable signed sums | 133 |
| oop-004 | Public Product, public integer Price native auto-property, emitted body-free accessors, actual compiler-generated CLR backing field and accessor state | 13 |
| oop-005 | Public Counter, actual private integer field named _count | 0 |
| oop-006 | Public Point, actual private integer _x/_y fields, public constructor(integer x, integer y), correct field state on two real instances per argument pair | 338 |

The original prompts, not the task titles, define the checks. Price's prompt
does not prescribe getter/setter visibility or require a setter. Getter-only,
get/set, get/init, private-setter, and static auto-properties are supported.
Existing accessors must operate on their actual backing state; a manual getter
with identical behavior is not an auto-property. For a readonly static backing
field, the finite check observes the getter's actual stored value instead of
attempting an unsupported write. Initial values are not prescribed.
Static members, readonly/constant fields, and unrelated overloads are not
prohibited when the selected compiler accepts them. Point checks isolation
only for instance fields; the prompt does not prohibit static fields.

The exact `Option<Entity>` spelling is retained. Disposable positive controls
declare ordinary native `Option<T>` and `Entity` classes; no built-in option
grammar, runtime feature, nullable substitution, or absence semantics is added.
Native interfaces have no visibility slot and emit as public interfaces.
Integer aliases are resolved by the selected public `AttributeHelper.ExpandType`
API, then checked against actual CLR Int32 signatures. Parameter names
id/a/b/x/y and field names _count/_x/_y are checked where the prompts specify them.

Add uses a long oracle and 13 independently chosen signed boundary values,
excluding only sums outside Int32's representable range. Both checked and
unchecked compiler modes must pass. Point varies all 169 ordered boundary
pairs and reads the actual private fields after each construction. Optional
entry contracts and scoped inline refinements must admit the entire prompt
domain, not just the finite probes; unmodeled predicates and unknown proof
results fail. Field and empty-class checks are finite structural evidence,
not universal behavior proofs.

Calculator/Product instance setup uses a real public or nonpublic zero-argument
constructor, otherwise an unambiguous one/two-integer constructor with fixed
independent seeds (-7, 11). Other setup shapes fail explicitly with
`Unsupported finite constructor setup`; constructor/runtime exceptions fail,
and no uninitialized-object bypass or guessed defaults are used.

```bash
python3 tests/E2E/agent-tasks/test-oop-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --oop
```

The current suite requires exactly 160 actual verify.sh controls: 105 native
identity/state/signature cases, 45 missing/invalid/comment/interop/named-module/ambiguity/
unrelated-member cases, and 10 selected-root/configuration/native-asset/runtime/
deadline cases. Overall, 39 controls succeed and 121 are rejected.
The strict separate `--oop` lane executes the same named representative matrix
in each actual old/current arm: 43 native cases, six recursive-interop controls,
and four missing/comment/named-module/unrelated-owner controls, plus current
rejection of historical syntax: exactly 107 controls (32 positive, 75 negative).
It archives unchanged Git commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies that release's own
native-asset manifest, restores locked dependencies, and builds its own compiler.
Only disposable old input strings acquire historical IDs and section-form
`§I` refinements. There are no copied-current baselines, skips, empty passes,
timeout-shaped negative successes, or fallback compiler roots. The existing
120-second entry/process/EOF limit and 600-second historical total remain intact.
No task.json, fixture, TestData, active .calr subject, corpus membership,
historical ledger, floor, or product source is changed.

`OopAdmissionTests` mocks the actual `verify()` path without adding real
controls. It enforces exact outer and nested exit-1 wrappers, the existing
behavioral diagnostics, and one complete requested selected-compiler identity.
Infrastructure, UNKNOWN, solver timeouts and failure-contaminated success
propagate before counters change. Only the existing explicitly negative
numeric verifier-timeout control opts into an ordinary timeout; EOF, cleanup,
OS errors and global deadlines still propagate. The existing corrupt-native
control is a separately validated prerequisite refusal, not a behavioral
negative: both exit-1 wrappers, the native-loader diagnostic, exact selected
identity and owned corrupt bytes are checked without other contamination.
Invalid root/configuration and missing owned dependency controls likewise
validate their exact intended prerequisite refusal without behavioral credit.
Only complete native-loader alternative-probe lines may contain the loader's
ordinary missing-library text; appended OS/EOF/cleanup diagnostics still fail.
These guards preserve the 160/107 real controls and unchanged limits; they do
not establish milestone acceptance.

The strict replay uses two isolated workers within each arm, as the async replay
does. Every entry still builds its own disposable helper and selects the actual
compiler; there is no helper cache or binary substitution. The old arm finishes
before the current arm starts. The 107 entries, categories, 120-second entry bound
and shared 600-second total deadline are unchanged. Counter updates are locked,
and worker failures propagate without a success summary. The dedicated worker
mock checks both arm counts, the two-worker limit and direct infrastructure
exceptions; it contributes no real entries.

Before this scheduling change, the parent current suite passed all 160 entries
on `4fcd72d60` in 983.922 seconds. Its sequential 107-entry replay exhausted the
600-second total budget during the current Counter controls; that failed run is
not acceptance evidence. The bounded-worker replay must pass in full separately.

### Original arithmetic binding prerequisites: type-005 and type-006

Only the original variable-binding and variable-assignment wrappers dispatch
internal `type005`/`type006` profiles to the existing selected-root engine.
The unchanged prompts require public `ComputeWithTemp(int a, int b): int`
and `Accumulate(int n): int`, respectively. Intermediate/accumulator names
are not prescribed. The four Option/Result tasks are not changed or
reinterpreted as nullable/exception tasks.

Both profiles create the selected native Z3 Context, compile native source
with debug contracts, and independently compile and inspect its actual CLR
signature. Selected-emitter debug syntax must match the original compiled
emission before its exact selected-public, file/line-mapped, contract-disabled
emission can be instrumented. Original, contract-disabled and instrumented
baselines must all produce independent expected outputs.

`ComputeWithTemp` observes two executed native bindings whose values are
`a+b` and `a-b`. Polynomial recognition accepts renamed variables, casts,
widened intermediates and algebraically equivalent scalar bindings. Native
return recognition keeps those binding identities as **independent symbols**:
the return must normalize to their product, including through aliases.
Substituting the original input formulas during this recognition would wrongly
certify an unrelated `a*a-b*b` return. Actual CLR local-symbol/version reads
must connect both binding writes to the executed return and each binding to
both parameters. Each binding is independently replaced with zero, across
every core case; every mutation must finish successfully and return exactly
zero, with at least one changed output per binding. Unused bindings, zero-weight
decoys, input-recomputed returns and conditional decoys that only react to
mutations are not product provenance. A helper contract using unused bindings
cannot establish dependence, and a mutant-only runtime error rejects explicitly.
Alias expressions are resolved through their exact executed write sites and
read versions, not their initializers: a mutable alias containing the product
is accepted, but overwriting it with an input-recomputed/gated result rejects.

The **133 core cases** comprise the Cartesian product of
`[-46340,-1024,-31,-2,-1,0,1,2,31,1024,46340]`, plus twelve signed boundaries:
`(1073741824,1073741823)`, `(1073741823,1073741824)`,
`(-1073741824,-1073741823)`, `(-1073741823,-1073741824)`,
`(1073741824,-1073741823)`, `(-1073741824,1073741823)`,
`(-1073741823,1073741824)`, `(1073741823,-1073741824)`,
`(46341,46340)`, `(46340,46341)`, `(-46341,46340)`, `(46340,-46341)`.
Mathematical sums, differences and products fit signed Int32; the independent
oracle uses signed Int64 arithmetic, not candidate outputs. There are exactly
**266 successful complete-case binding counterfactuals**.

Six supplementary zero-product pairs have representable final results but
wider mathematical intermediates:
`(Int32.MaxValue,Int32.MaxValue)`, `(Int32.MinValue,Int32.MinValue)`,
`(Int32.MaxValue,-Int32.MaxValue)`, `(-Int32.MaxValue,Int32.MaxValue)`,
`(1073741824,1073741824)`, `(-1073741824,-1073741824)`.
All three baselines execute these only for both selected Int64 binding types
with widened initializer evaluation (including aliases), or for unchecked
selected-public emitted arithmetic. Merely declaring a long binding around
an Int32 addition does not widen that addition. They do not add
counterfactual/dependence claims. Checked narrow intermediate arithmetic
excludes these six cases rather than accepting an overflow exception as
evidence. Checked narrow, checked widened and unchecked narrow controls are
tested separately. No universal overflow policy is inferred.

`Accumulate` requires an actual native mutable zero initialization followed by
exactly three executed writes to that same CLR local identity. Each native
update normalizes to previous accumulator plus the supplied `n`; each actual
write reads the previous version and the parameter, including through aliases.
The observed states must be precisely `0,n,2n,3n`; the selected native return
must normalize to that accumulator and read its final version. Three unrolled
assignments and a three-iteration loop are accepted. Each update is independently
perturbed by +1; every complete-case mutation must return precisely `3n+1`.
Unused accumulators beside `3*n`, another nonzero-initialized variable,
constant increments, unreachable bindings and wrong update counts reject.

The **17 cases** are
`[-715827882,-715827881,-1000000,-46341,-31,-3,-2,-1,0,1,2,3,31,46341,1000000,715827881,715827882]`.
All mathematical intermediate states and the final result fit Int32.
There are exactly **51 successful complete-case update counterfactuals**.
Mutation states/results also fit Int32, including the positive boundary
`3*715827882+1 = Int32.MaxValue`.

The existing scoped selected contract translator and arithmetic-definedness
guards prove optional entry/output contract admissibility over the corresponding
representable core domain, not just sampled inputs. Contracts cannot cap that
domain, exclude signed inputs, exclude correct outputs, or introduce foreign
references. Unsupported fragments and solver unknown/timeouts fail, with no
unknown-proof fallback. Body/provenance checks remain finite, **not a universal
body proof**. Native polynomial provenance supports signed scalar
arithmetic/casts/aliases and mapped executed loop/branch statements; helper-call
results, arbitrary conditional formulas, raw interop, local functions and
ambiguous mappings cannot supply these polynomial witnesses. There are at most
64 witness candidates and 10,000 trace records per invocation.

```bash
python3 tests/E2E/agent-tasks/test-type-arithmetic-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --type-arithmetic
```

The current suite requires exactly **59 real arithmetic entrypoints**
(19 accepted, 40 rejected), including the unchanged basic fixture plus added
disposable functions, missing inputs and root/configuration selection controls.
Python independently checks case uniqueness, representability and exact counts.
All controls are Python strings or outside-repository temporary inputs.

The additive strict historical mode archives unchanged actual v0.29 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its own Z3 manifest,
locked-restores and source-builds it, then links this same test-only helper to
each selected old/current arm. It requires exactly **71 real entrypoints**
(22 accepted, 49 rejected), including current rejection of an old-ID input.
Historical IDs appear only in disposable adapted inputs. The existing
120-second per-entry/process/EOF bounds, 600-second historical total bound,
process cleanup, native-only measured admission and all prior dispatch/proofs
remain unchanged, including all 65,536 SumToN inputs/output-only mutants,
generic behavioral storage selection, string control dependence and collection
state tracing.

The strict matrix is representative, not the full 59-control current suite.
It executes the same **35 categories per arm**: 23 ComputeWithTemp cases
(8 accepted, 15 rejected) and 12 Accumulate cases (3 accepted, 9 rejected).
Every entrypoint still recompiles the test helper against that selected arm,
compiles the actual candidate source, and executes its complete original,
contract-disabled, instrumented and applicable counterfactual cases. No
compiler/body evidence is cached, substituted or shared across entrypoints.

ComputeWithTemp retains `int` aliases with checked unrolled bindings, genuinely
widened algebra, long declarations with narrow initializer evaluation, unchecked
narrow arithmetic, mutable and assigned-product aliases, overwritten/gated
write-version decoys, admissible entry/output contracts, unused bindings beside
input-recomputed returns, zero-weight decoys, wrong binding values, unreachable
bindings, helper-contract decoys, baseline and mutant-only runtime errors,
admissible/narrowed scoped refinements, narrowed preconditions, excluded outputs,
foreign contract references, and private/wide-parameter/wide-return signatures.
Accumulate retains checked unrolled updates, three-iteration loops, widened
increment aliases, unused accumulators beside independent returns, two/four
updates, nonzero initialization, constant increments, another variable's
updates, unreachable accumulators, and baseline/mutant-only runtime errors.
All accepted entries retain exact native/CLR signatures, independent signed
oracles and complete 266 zero-binding or 51 +1-update counterfactual checks.

Only redundant permutations are omitted from strict replay, and all remain
in the unchanged full current suite: ComputeWithTemp's canonical `i32` positive
(`int` positive retained), immutable alias (mutable/assigned aliases retained),
direct conditional decoy (overwritten/gated alias retained), and binding-free
return (unused/unreachable binding decoys retained). Accumulate omits `int`,
unchecked and tautological-contract permutations (covered by ComputeWithTemp);
its zero-weight and binding-free returns (other output/state decoys retained);
and its admissible/narrowed refinements, narrowed precondition, excluded output,
foreign reference, private/wide-parameter/wide-return permutations (the same
selected guards and signature rejection categories run in ComputeWithTemp).
The full current suite still tests every omitted task-specific permutation.
This avoids repeating cross-profile categories inside the historical budget:
the **600-second total and 120-second entry/process/EOF deadlines are unchanged**.
Exhausting either deadline remains a mandatory failure, not accepted evidence.

Arithmetic controls accept only finite candidate failures. Direct or nested
timeout, deadline, EOF, cleanup, launch/OS and abnormal process-exit failures
are re-raised before recording a verdict, even when enclosed in
`Verifier command exited 1` output. No arithmetic control intentionally expects
a timeout. Every positive requires exactly one complete `Selected compiler:`
line matching the resolved requested root, configuration and `net10.0/calor.dll`;
missing, wrong, duplicate and suffix-only identities reject.

Three lightweight mocked guard tests exercise the actual arithmetic `verify`
method without launching a compiler or contributing to either real-entrypoint
count. They check positive/negative infrastructure propagation, direct/nested
process errors, success-shaped timeout output, invalid/multiple compiler
identities, a valid requested Release identity and ordinary nested finite
candidate rejection. The full suite still requires 59 real current controls,
and strict replay still requires 71 actual old/current controls.
These guards also make solver `UNKNOWN; timeout` failures fatal rather than
finite expected negatives. Validation of this guard follow-up exposed those
failures in ComputeWithTemp's narrowed precondition and narrowed inline
refinement controls; the archived replay stopped at the same inline guard.
Those runs are not passing full-suite evidence. The engine and solver deadlines
were not changed to hide or work around the failures.

The subsequent arithmetic entry-proof fix substitutes the existing independent
representable `ArithmeticCases` inputs into the **same translated entry
counterexample**, including its exact domain constraint, before invoking Z3.
An exactly true substituted predicate supplies a concrete rejected input.
False or unresolved substitutions never admit a candidate: the original
universal proof still runs for every positive and every unsolved entry
counterexample, with the unchanged 5,000 ms solver limit. Other profiles are
unaffected. The existing narrowed-precondition/refinement controls now also
require explicit concrete representable witness evidence; timeout diagnostics
cannot satisfy that assertion or the infrastructure guard.
Serial validation of the combined guard/entry-proof revision passed all 59
current controls and all 71 genuine archived-old/current controls. The two
ComputeWithTemp narrowing forms reject concretely at `a=-46340,b=-46340`;
the two Accumulate forms reject at `n=-715827882`. Positive runtime, state and
complete-case counterfactual requirements remain 133/266 and 17/51.

This is only the two arithmetic prerequisites, not acceptance of all 129 tasks,
an agent, the milestone or a release. Product code, other task wrappers,
task definitions, original fixtures, TestData, active Calor subjects/corpus,
historical inputs/ledgers/floors, versions and workflows remain byte-unchanged.

### OOP strict compatibility matrix

The historical matrix is deliberately smaller than the unchanged full current
suite. Each entry still launches its real individual verify.sh, builds the same
helper against that arm, and performs all native/CLR/runtime checks for its
profile. Native Z3 loading, immutable old bootstrap, entry/process/EOF supervision,
and the 600-second total are not cached, relaxed, or bypassed.

| Profile | Strict representatives retained |
|---------|---------------------------------|
| Person | Empty sealed class, private class rejection, source field and explicit constructor rejection |
| IRepository | Integer alias, exact parameter name/width, nullable substitution rejection, wrong closed generic argument, implementation rejection, excluding and admitting scoped refinements |
| Calculator | Checked/unchecked signed sums, private method, parameter name/width, constant stub, excluding and unmodeled preconditions, failing output contract, real nonpublic constructor |
| Product | Getter-only, init, private setter, static get/set, private property and wrong width, behaviorally equivalent manual implementation, unsupported real-constructor setup |
| Counter | Integer alias, readonly and constant fields, wrong visibility and name |
| Point | Integer alias with real instance isolation, readonly/static field state, wrong field/constructor visibility, constructor parameter width, swapped values and missing assignment |
| Shared adversaries | Recursive raw interop in every profile; missing input, comment-only declarations, named-module substitution and unrelated member owner; current rejection of historical IDs |

The omitted permutations remain covered by all 160 current controls: canonical
spellings duplicated by accepted aliases/modifiers, repeated wrong type/name/
visibility mutations, alternate source-member kinds, alternate non-Option return
types, duplicate equivalent admitting/excluding guards on other profiles, extra
generic arity and overload permutations, additional initial-value/static/setup
variants, subtraction/zero/decoy state variants, and repeated missing/comment/
interop/ambiguity/selection/deadline cases. The strict matrix retains the distinct
native signature, closed CLR identity, auto-property provenance/accessor state,
constructor setup/state/isolation, guard proof, runtime contract-failure, and
adversarial ownership categories rather than every permutation of them.

### Original Option/Result carrier tasks (type-001 through type-004)

These four entries now observe actual returned carriers instead of grepping
retired constructor markers. Their original prompts and fixture are unchanged:
`TryDouble(int x)` returns `Option<int>` with Some(`x * 2`) exactly when `x > 0`;
`GetNothing()` always returns None; `SafeDivide(int a, int b)` returns
`Result<int, string>` with Ok(`a / b`) when `b != 0`, otherwise
Err(`"Division by zero"`); `AlwaysFail()` returns
Err(`"Operation not supported"`). They are not nullable-return or exception-based
substitutes.

The helper requires exactly one public, synchronous, nongeneric native module
function with the original parameter names and types. It rejects raw C#,
interop, comment decoys, duplicate declarations and wrong signatures. The
selected compiler's public AST and emitter remain authoritative. Its generated
C# is compiled independently with nullable analysis and **warnings as errors**.
Reflection checks the actual closed CLR return identity: `Option<int>` or
`Result<int, string>`, from a matching native auxiliary generic class in the
module namespace or the selected historical `Calor.Runtime` assembly. A matching
name, scalar return or fixed-type field masquerading as a generic payload is not
enough.

The bounded observable model is anchored in the released public APIs:
`IsSome`/`IsNone`, `IsOk`/`IsErr`, `Unwrap()` and `UnwrapErr()`. A case tag must
be a named public instance boolean field or readable, non-indexed property.
At least one of each carrier's complementary case tags must exist; every tag
that exists must agree with the independently expected case. Active payloads
are read through a public instance zero-argument accessor or an explicitly named
`Value`/`Error` field or readable property. Their generic-definition types must
be the corresponding generic parameters, and the closed types must be exactly
`int`/`string`. When both an accessor and named payload member exist, **both**
must agree. Malformed, throwing or contradictory authoritative APIs reject; a
decoy member cannot override them. Unrelated boolean or payload fields are not
guessed as witnesses.

Native auxiliary declarations are permitted in disposable candidate/control
source, never injected secretly or written into original fixtures. The portable
old/current positives explicitly declare tagged `Option<T>`/`Result<T,E>` classes
with nullable generic payload annotations. Those annotations preserve the
actual closed CLR generic-parameter types and avoid `CS8618`. They qualify the
function's return with its module namespace and infer the local allocation's
type. This avoids the old binder's `Calor0275` distinction between reference
values and its built-in runtime Option, without modifying either compiler.
The model does **not** promise full old API restoration, arbitrary third-party
carrier compatibility, inactive accessor behavior or a universal body proof.
In particular, an inactive payload is not examined merely because a member
exists.

Each correct TryDouble positive executes **137** distinct signed/boundary
inputs, including zero, negative extremes and `1073741823 -> 2147483646`.
Only positive inputs whose doubled mathematical value exceeds `Int32.MaxValue`
are excluded; negatives remain required even though doubling them would overflow.
SafeDivide executes **399** Cartesian signed/boundary pairs: 20 zero-denominator
errors and 379 successes, including zero quotients, negative operands, truncation
toward zero and large representable results. Only `Int32.MinValue / -1` is
excluded. GetNothing and AlwaysFail each execute **8** repeated invocations.
There is no invented overflow policy or positive-only/nonzero-only precondition.
Optional contracts execute on the required probes and cannot conceal those
sampled cases. Compilation, runtime, observation and timeout failures reject
explicitly.

```bash
CALOR_COMPILER_CONFIGURATION=Release python3 tests/E2E/agent-tasks/test-carrier-verifiers.py
CALOR_COMPILER_CONFIGURATION=Release python3 tests/E2E/agent-tasks/test-logic-old-release.py --carriers
```

The current suite requires exactly **137 real verify.sh entrypoints**, plus
**2 actual helper-build selected-image substitution controls**. It includes
native fields/properties, accessor-only payloads, complementary-only tags,
conflicting/decoy members, wrong cases and payloads, exact errors, signed
division, boundary corruption, restrictive pre/postconditions, wrong signatures,
missing/invalid/raw/duplicate source, retired constructors, missing/corrupt/
foreign compiler/native assets and bounded nontermination/output. The four
canonical current constructor controls reject with `Calor0006`; no retired
`SM`/`NN`/`OK`/`ERR` lexer, parser or runtime support is restored.

The additive strict `--carriers` mode requires exactly **116 representative
actual old/current entrypoints**. It archives unchanged released Git commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies that tree's own native asset
manifest, locked-restores and builds its own source. Both real arms execute
portable native auxiliary positives and negative controls; four historical
native constructor positives remain positive on the old arm and reject on the
current arm. No current-assembly substitution, skip, empty run or fixture
mutation counts as compatibility.

Carrier selection also checks the selected checkout's managed/native Z3 output
against its own pinned manifest. It snapshots the top-level managed/native
images and `runtimes/` images copied by the helper project, requires byte-identical
helper copies, and rechecks before execution and
before reporting success. Existing profiles keep their original selection and
native Context behavior. Every successful control checks the exact resolved
selected compiler root and configuration. Finite controls accept rejection only
from an exited verifier command; supervision, EOF and deadline failures propagate.
Only the two explicitly declared bounded-timeout controls may accept a verifier
timeout, never a cleanup/supervision failure. Entry/process/EOF cleanup is bounded to **120 seconds**;
each carrier invocation and its observations have the shared **4096-character**
stdout cap. Historical compatibility, including archive/restore/build, has the
existing **600-second total** deadline and 120-second subprocess bounds.
These are bounded verifier prerequisites for four original tasks, not full
milestone, task-set or agent acceptance. All 40 prior profiles, all 65,536
SumToN probes and strict successful-output counterfactual requirements remain.

### Original async-functions behavioral prerequisites

The four `async-001..004` entrypoints use the same selected-root/configuration,
native-only, bounded test engine as the prior profiles. They check the original
task prompts, not additional behavior inferred from task titles. Parsed public
native async declarations and independently compiled CLR `Task<int>` signatures
with async state machines are both required. Native async `i32`/`int` outputs
lower to `Task<int>`; explicit task outputs are also checked. DataService must
be a public native class with a public instance async LoadAsync. Network-read
rows on ProcessAsync/BackgroundProcessAsync and the network row on LoadAsync
are read before compilation can synthesize effects.

Actual native calls and awaits are mapped by expression and source-line identity
to that same public emission. Immutable task aliases are supported. The mapped
FetchDataAsync/SlowOperationAsync call must resolve to an ordinary native
auxiliary function. Its body is not run: a test-only task probe supplies
independent values and tracks exact call/task identity. ProcessAsync must double
those results; BackgroundProcessAsync must return them unchanged and apply
ConfigureAwait(false) to the actual awaited task. Unused calls/awaits/configuration,
unrelated helpers, blocking Result, constants and wrong arithmetic cannot pass.
Await neutralization must compile and execute successfully and change results;
mutation errors are rejection, not dependence evidence.

LoadAsync's mapped GetAsync must resolve semantically to the real BCL HttpClient
in the System.Net.Http assembly, not a same-named or namespace-spoofing class.
The admitted HTTP fragment uses native local construction or a native private
readonly HttpClient field initialized with parameterless `§NEW`. Immutable
client/address aliases and a constant absolute HTTP(S) GetAsync(string) address
are supported. The mapped BCL GetAsync call executes with its actual receiver
and argument evaluation intact. Only validated client construction is changed:
it creates a real HttpClient backed by a controlled no-network HttpMessageHandler.
The handler observes the actual receiver, address and GET request; instrumentation
records the actual returned GetAsync task, not just the handler's response task.
Null clients, invalid addresses and wrong receiver/task provenance reject.

Real public zero-argument DataService construction executes every admitted field
initializer. Implicit default constructors and explicit empty public constructors
are supported; construction is never skipped. The actual GetAsync task must
complete before LoadAsync can return 100. Await-only neutralization preserves
GetAsync and the request, and must successfully return 100 while that actual task
is still pending. Constructor, runtime and mutation errors reject rather than
count as evidence. Clients and handlers are disposed after each control.
Unmodeled calls, properties, custom operators/conversions, static/module
initialization, inheritance, lambdas, nonempty/nonpublic/parameterized constructors,
HTTP overloads, mutable fields and arbitrary injected clients fail closed.
This is a bounded admitted fragment, not universal HTTP or arbitrary async support.

GetDataAsync runs three times and returns 42; it need not suspend or delay.
ProcessAsync runs nine independent representable helper values, including both
endpoints of the safe doubling interval, with completed and pending tasks.
BackgroundProcessAsync additionally runs Int32.MinValue/MaxValue unchanged.
LoadAsync runs two response statuses with completed/pending tasks. Each
operation-bearing profile also checks completed/pending faults on the exact task.
Each positive LoadAsync control executes seven real controlled GETs: those six
baseline states plus the successful await-only counterfactual. The additional
error control passes every real-call baseline but must reject its faulting
await-neutralized runtime; the exception is not completion-dependence evidence.
The admitted uncaught-await fragment must propagate those controlled faults.
There are 3, 20, 24 and 6 runtime cases respectively (53 per complete positive
set). No scheduling, elapsed delay, synchronization-context behavior or universal
body proof is asserted. Every entry/process/EOF remains bounded by the shared
120-second deadline; the old runner retains its total 600-second bound.
The outer control reserves two seconds inside these bounds so the nested
supervisor can terminate its own process groups before the outer deadline.

```bash
python3 tests/E2E/agent-tasks/test-async-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --async-functions
```

The current suite has six tests and 100 real entrypoint controls, including
equivalents, fake/dead operations, receiver/input validation, corrupt native
assets, compiler selection and bounded nontermination. The additive strict old
mode requires exactly 139 selected actual controls: 69 per arm plus current
rejection of an old-ID input. This bounded matrix retains every required behavior
and adversarial category, plus all eight real-instance/readonly-field controls;
redundant alias/signature permutations and supervision
checks remain in the full current suite. It archives unchanged v0.29 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies that release's own native
manifest, performs locked restore and builds its own compiler source. Only
disposable old inputs receive old IDs and explicit native input/output syntax.
Missing APIs/assets/history, failed positive controls and skipped/empty runs
fail; no reflection fallback or current-binary stand-in is allowed.
Finite negative controls admit only an explicit `Verifier command exited`
status 1 failure, including nested exits. Abnormal statuses, success-shaped
failure output, solver UNKNOWN/timeout, OS/launch errors, unexpected process/EOF,
global/entry deadline and cleanup failures
re-raise, including nested supervisor timeouts inside command-exit output.
Only the two explicit bounded-timeout controls opt into timeout admission;
cleanup errors still fail. Every positive control asserts exactly one selected
`calor.dll` identity with the resolved requested root and configuration, rejecting
indented extra identities as well as ordinary duplicates. A
separate in-process admission/identity test adds no real entrypoint controls.
Strict compatibility keeps the genuine old/current arms sequential and executes
two isolated entrypoint workers within each arm under the same absolute
600-second deadline. Each entry retains its own disposable workspace, helper
source build and 120-second process/EOF bound; counters are synchronized. There
is no helper/compiler caching, arm substitution, dropped control or relaxed bound.
The deliberate corrupt-native control expects the classifier to re-raise its
loader failure. It checks the selected disposable asset bytes, native loader
exception and corruption diagnostic, rejects timeout/EOF/cleanup contamination,
and counts that actual invocation separately as a prerequisite refusal, never a
finite behavioral rejection.

Known native/API limits are explicit, not repaired by this test-only unit.
The current binder rejects an explicitly typed Task<int> local initialized
directly from a scalar-output native async function with Calor0202; inferred
locals and native task-returning auxiliary functions are covered alternatives.
The archived compiler rejects the inferred task.Result negative before CLR
execution because its getter has unknown effect (Calor0410). The old mode
asserts that exact blocker and does not label it CLR behavioral evidence.

Only the four verifier scripts, shared test-only dispatch/engine, disposable
Python controls and this dedicated documentation are changed. task.json files,
fixtures (including the header-only AsyncOps/Domain inputs), TestData, active
`.calr` subjects, corpus, historical ledgers/floors, product source, version and
workflows remain unchanged. No agent/provider, paid call, network service,
publication, release or milestone-wide acceptance is involved.

### Original effects-system prompts (`eff-001` through `eff-006`)

Only the six original effects-task `verify.sh` entries use the new `eff001`..
`eff006` profiles in the existing selected-compiler engine. The unchanged
`task.json` prompts require public native `SayHello(str name) -> void`,
`ReadInput() -> str`, `ReadFile(str path) -> str`,
`WriteFile(str path, str content) -> void`, `FetchUrl(str url) -> str`, and
`LogToFileAndConsole(str path, str message) -> void`.
SayHello prints the supplied name with Console.WriteLine; no greeting prefix
is prescribed. ReadInput returns the supplied console line. ReadFile returns
the actual File.ReadAllText content. WriteFile uses File.WriteAllText.
FetchUrl is only `"Response from: " + url`, with a conceptual network-read
declaration: it never performs HTTP. Logging prints the message with
Console.WriteLine and appends it with File.AppendAllText, preserving prior
file content. No cross-output ordering beyond these operations is imposed.

The verifier reads source once, parses its original declarations before
compiler inference, and checks required declared console/file/network effects.
Additional declared effects and read/write supersets are allowed. Native
public names, parameter order and types, and return types must match the
independently compiled and reflected CLR method. Raw interop cannot establish
native behavior. Roslyn resolves actual BCL call symbols across the native
module, including helpers. A same-named user helper, comment, unused helper,
or effect annotation cannot replace an executed required BCL operation.

An independently compiled interposed baseline checks actual BCL arguments,
results, stdin/stdout/stderr and file state before the unmodified generated
baseline runs. Both baselines must pass. Instrumentation then neutralizes
each required operation family: reads return a distinct non-null string and
writes become no-ops. Each family must change successful outputs or state
in at least one case. Counterfactual compilation/runtime errors are failures,
not dependence evidence. File byte-read/write alternatives are guarded too,
so correct-baseline decoys that ignore ReadAllText or overwrite the result
of WriteAllText/AppendAllText independently fail coupling. Disposable native
UTF-8 codec helpers make those decoys compilable without changing effect
manifests or weakening compilation. There is no universal codec claim.

The console streams are deterministic and restored in `finally` after each
invocation, including failures. Eight independently varied strings include
empty, distinct values, whitespace, LF/CRLF, composed Unicode and a non-BMP
pair. ReadInput instead uses six actual line values (no embedded terminator),
followed by a different next line; EOF/null is outside the tested domain.
Files live exclusively in one freshly owned temporary directory. Every file-profile case
initializes two independent existing files (including empty content), supplies
one of their actual paths, varies content/message and initial state separately,
and observes both files plus an unchanged unrelated sentinel.
SayHello and FetchUrl run 16 cases each, ReadInput 12, and each file profile
128: 428 cases for a complete six-function positive set, repeated for both
baselines and applicable counterfactuals.

Both current and genuine v0.29 native nullable annotations reject a direct
Console.ReadLine return from non-null `ReadInput` with `Calor0273`
(a typed non-null binding also fails).
The positive controls bind its nullable result and explicitly guard the
non-null branch; the fallback is never exercised or adjudicated. This is a
compiler/API accommodation, not a new EOF, exception or null policy.
Compatibility controls require that rejection in both actual compiler arms
rather than change the product, signature or oracle.

```bash
python3 tests/E2E/agent-tasks/test-effects-verifiers.py
python3 tests/E2E/agent-tasks/test-logic-old-release.py --effects-system
```

The current suite executes 131 real effects entries (34 positive, 97 rejected)
and 35 preserved entries: one positive for every prior profile plus the
selected native-only prerequisite. It checks aliases, extra effects,
helper/Encoding equivalents, original declarations, private/wrong signatures,
constant and wrong-argument outputs, non-owned/other-file writes, append versus
overwrite, correct-baseline independent-output decoys, counterfactual runtime
errors, selected-root/configuration/native failures and bounded nontermination.

The additive historical mode runs a bounded 60-case original-prompt subset against
both selected arms, plus current rejection of an old-ID input and the two
direct-ReadLine compatibility cases and eight local-helper controls per arm:
exactly 139 real entries (39 positive, 100 rejected).
The subset retains all 18 positive/equivalent/alias inputs,
original-effect and private-signature failures, metadata/comment/constant
decoys, combined-effect omissions, wrong log arguments/overwrite, simulated
network ordering/IO violations, and all four successful-baseline coupling/error
controls. Redundant wrong-type/duplicate cases remain in the full current suite;
they are not all replayed historically within the 600-second total bound.
Both arms reject correct-baseline byte-codec decoys only on
coupling and reject counterfactual exceptions as non-evidence.
It archives unchanged v0.29 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies that archive's own native
manifest, restores locked dependencies and builds its original source.
Only disposable old inputs acquire declaration IDs. Missing assets/history,
build/API errors, skips or empty runs fail. Existing old modes, native-only
admission, root/configuration/native guards, scoped translators, every prior
profile and all 65,536 Sum/counterfactual checks are unchanged. The shared
120-second entry/process/EOF and 600-second historical total deadlines remain;
the new control supervisor reserves one second for nested process cleanup.
Two bounded workers run independent entries in disposable workspaces in each
arm, preserving the entire previous strict matrix within that same total
deadline. Compiler/helper builds are still selected-arm builds, not cached or
substituted. Counters are synchronized and every positive effects entry checks
the exact resolved selected `calor.dll` root and configuration, not a substring.

Whole-module native rejection explicitly crosses `LocalFunctionStatementNode`
boundaries with reference-identity visited-node protection. The general walker
intentionally treats these nodes as separate-callable leaves; omitting this
edge could hide raw statements or expressions in an archived native local
helper. Actual v0.29 entry controls reject `§RAW` statements and `§CS` expressions
inside both one-level and two-level local helpers, including unused helpers.
Current nested `§F` inputs instead hit its explicit parser retirement error
(`Calor0100`); those are syntax blockers, not behavioral/raw-scan evidence.
A native nested helper passes with the genuine archived compiler, while a
native lambda helper passes in both arms. The verifier admits only a resolved
delegate invocation bound directly to a local lambda initializer. Raw
statements/expressions inside lambda helpers are rejected in both arms.

Finite negatives require exact outer and nested `Verifier command exited 1:`
wrappers. Abnormal statuses, malformed wrappers, solver UNKNOWN/timeouts,
OS launch errors, process/EOF/cleanup/global-deadline failures and success
markers in failure output propagate rather than satisfy a finite negative.
Only the two explicitly opted-in bounded-timeout probes accept an ordinary
`Verifier timeout after ...s:` refusal; never solver, OS, cleanup, EOF or
global-deadline contamination. Positive output must be infrastructure-free
and contain exactly one complete resolved-root/configuration compiler identity
line. Indented, mixed-prefix, missing, duplicate and alternate identity lines
fail. Synthetic controls exercise the actual `verify()` admission path and
are excluded from all real-entrypoint totals. The current suite has seven tests.
Only the unchanged engine's verbatim successful limits disclaimer is exempt
from diagnostic scanning because it names EOF/null as untested. Arbitrary
Limits-prefixed errors or contamination appended to that disclaimer still fail.

The six deliberately corrupted-native invocations are explicit validated
prerequisite refusals, not generic finite behavioral negatives. Each must
raise before admission counters change, carry exactly two exit-1 wrappers,
identify Microsoft.Z3's DllNotFoundException and a platform-specific invalid
image error, and leave the owned corrupted file byte-unchanged. Timeout, UNKNOWN,
cleanup, EOF and success contamination cannot establish that refusal.
The current 131-entry total remains 34 positives and 97 rejections: 89 finite
negatives, two explicit bounded-timeout probes and six native-image refusals.

The earlier local-helper follow-up passed the full strict 139-entry old/current run and the
focused eight-entry current local-helper run (one positive, seven rejections),
plus the lightweight synthetic supervisor/identity test. The expanded full
131-effects-plus-35-preserved current suite was not rerun during shared-host
saturation. Its predecessor's 123-effects-plus-35-preserved run passed before
the follow-up; that is not a result for the expanded current suite.
After parent integration, the complete 131-effects-plus-35-preserved suite
passed seven tests in 891.107 seconds and strict genuine-old/current replay
passed all 139 entries (39 positive, 100 rejected) within 600 seconds.
Those results precede the final finite-admission classifier hardening.
The hardening itself was validated with lightweight actual-verify synthetic
controls, all six real corrupted-native refusals, and one real FetchUrl positive
confirming that the unchanged limits disclaimer and exact compiler identity
are admitted. No new full heavy run was started in the child.
The body/IO matrix, engine, task inputs, strict
categories and 120/600-second limits are unchanged.

**Limits:** this is finite process supervision, not a sandbox or universal
proof. The interposer refuses non-owned supplied paths and unmodeled external
call/constructor surfaces before original IO. Supported console/string/math,
StringBuilder, byte-list and modeled file BCL calls can be observed precisely;
unsupported overloads or other services fail explicitly. This does not prove
that arbitrary hostile code is confined, deterministic, or incapable of
distinguishing instrumentation. Tests access no non-owned host files or
external service. No missing-file, EOF/null, encoding, greeting-prefix or
exception policy is added, and no model/provider is invoked.

This local unit closes only these six verifier prerequisites. Product source,
task definitions, original fixtures, TestData, active `.calr` subjects, corpus
membership, historical inputs/ledgers/floors, versions and workflows remain
byte-unchanged. It does not claim all 129 prerequisites, agent or milestone
acceptance, or release readiness.

### Original carrier matching tasks (pm-004 and pm-005)

`UnwrapOrDefault(Option<int> opt, int defaultVal)` and
`HandleResult(Result<int,string> res)` now use behavioral prerequisites, not
grep checks for retired constructor tokens. Both require the original public
integer-returning signatures and a native **carrier-input switch expression**.
A switch over a boolean tag alone cannot satisfy these prompts.

The selected public AST must declare ordinary public generic `Option<T>` or
`Result<T,E>` classes. The selected emission is independently compiled with
warnings as errors, and reflection checks exact closed CLR input identities,
parameter order, public/static method identity and integer output. Inputs are
real instances constructed through public parameterless constructors and public
writable named case/payload fields or properties. Unknown construction APIs fail
explicitly; this bounded model does not promise arbitrary third-party carriers.

At least one complementary named tag (`IsSome`/`IsNone`, `IsOk`/`IsErr`) must
exist. Every present tag must agree. Active typed `Value`/`Unwrap` observations
must agree for positive cases; `Error`/`UnwrapErr` observations must agree for
error cases. Payload members must use the actual generic type parameters, not
fixed-width stand-ins. Unrelated flags such as `IsSuccess` are not guessed as
case evidence. Observations run before and after calls, including interventions.

Native type-capture/guard patterns and combined type/property patterns work on
both real compiler arms. Positive controls include scalar aliases, complementary
tags, all active observations, immutable input/result bindings, an unused match
beside a genuinely supplying match, and namespace-qualified factory return
annotations with inferred allocations. Bare nested property payload capture
(`§PPROP §PMATCH{Value} §VAR{v}` with arm result `v`) currently fails with
`Calor0200 Undefined variable 'v'`. These prerequisites use the supported native
forms; no parser, binder or retired carrier runtime is restored.

Each Option positive checks **210** independent runtime cases and **1,050**
successful counterfactuals. Each Result positive checks **90** runtime cases and
**360** counterfactuals. Payloads cover 15 signed/boundary integers; defaults
cover seven integers and errors cover three distinct strings. Debug contracts
execute on every original input; contract-disabled emission must preserve those
outputs before intervention. This is finite evidence, not a universal body or
carrier-contract proof; null carriers and unsampled bodies are not modeled.

Source-mapped selected emission identifies the actual native switch and its
arms. Traces must show distinct positive/negative arm selections and the
independently expected output. Complementing each supplying arm's result must
complement the function output. Replacing only that switch's governing carrier
must produce independently predicted outputs when its case, payload/error or
both change. Option defaults are also complemented independently across the
complete case set. Dead witnesses, captures replaced by reads from the original
caller argument, ignored switches and guessed outputs cannot pass.

```bash
CALOR_COMPILER_CONFIGURATION=Release python3 tests/E2E/agent-tasks/test-carrier-pattern-verifiers.py
CALOR_COMPILER_CONFIGURATION=Release python3 tests/E2E/agent-tasks/test-logic-old-release.py --carrier-patterns
```

The current suite requires **71 actual behavioral entrypoints** (**24 accepted,
47 rejected**), six actual selected-root/configuration/native infrastructure
guards, one explicitly declared timeout, and two actual helper-build image
substitution controls. Finite negatives require exited verifier failures with
declared behavioral diagnostics and exact outer and nested exit-1 wrappers.
Direct or wrapped timeout, EOF, cleanup, build, native-loading, OS/launch,
UNKNOWN and abnormal-status failures propagate even when an expected behavioral
diagnostic is also present. Positive evidence rejects infrastructure and failure
contamination and requires one complete canonical compiler identity, including
rejection of indented or mixed-prefix extra identities. Explicit prerequisite
and timeout guards require their exact single declared refusal, not a matching
substring amid other failures.

The child's complete 71-current/97-strict results precede this final admission
hardening. Its guard-only follow-up passed five targeted tests with isolated
synthetic counters and one real positive per profile. Parent integration passed
the same five guard tests with zero behavioral entrypoints; complete integrated
current/strict matrices and preservation remain pending. No acceptance credit
comes from the earlier weaker guard or these synthetic tests.

The additive `--carrier-patterns` mode executes **97 actual entrypoints**
(**48 accepted, 49 rejected**). All positive forms run for both signatures;
shared adversarial categories alternate signatures in the identical
representative matrix on each arm. It archives unchanged v0.29 commit
`d82f3ae95175ecd09581c6922701706be72b0ca9`, verifies its own native manifest,
locked-restores only from the local package cache and builds its own source.
Historical IDs occur only in disposable old inputs; current rejection of those
IDs is mandatory. Each positive checks the exact selected root/configuration/
`calor.dll` path. Missing cached dependencies fail without a network fallback.

The shared selected-image checks retain their top-level/`runtimes/` copy scope,
excluding unrelated publish artifacts. Native `Context` loading, per-process/
entry/EOF cleanup deadlines of **120 seconds**, the old-run **600-second total**
deadline and **4,096-character** per-invocation/observation output bounds remain.
All 56 previously integrated profiles, all 65,536 SumToN probes and their output-only/
successful-counterfactual checks are unchanged. These two prerequisites are not
full milestone, agent-task-set, publication or release acceptance.
