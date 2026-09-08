# Calor language completeness and test coverage audit

**Date:** 2026-09-08  
**Source baseline:** `d65cb283` (local `main` at the start of the audit)  
**Compiler version:** `0.17.0`, from `Directory.Build.props`  
**Semantics version:** `2.0.0`, from `src/Calor.Compiler/SemanticsVersion.cs`  
**Purpose:** Identify actionable correctness, language-completeness, and
regression-detection gaps, not estimate a percentage of C# that Calor supports.

## Executive assessment

**Calor has broad implemented syntax and substantial testing, but successful
compilation, a "lossless" conversion result, and even a "Proven" contract
verdict do not yet reliably establish the promised semantics.**

This audit identifies **16 actionable findings**: 13 reproduced
implementation/specification defects and three cross-cutting test or
documentation gaps. The most urgent are false proof-driven guard deletion,
always-on corruption of a floating-point contract, and omitted assignment
effects. Five small C# migration examples independently change behavior
while the converter reports validated, lossless, zero-loss output.

| Area | Findings | Main risk | Priority |
|---|---|---|---|
| Proof and runtime contracts | S1-S2 | A failing contract becomes ineffective | P1 |
| Effects and inherited contracts | S3-S4 | Declared guarantees are not enforced | P1 |
| Native expression/control-flow/numeric semantics | N1-N3 | Accepted programs execute differently from their meaning/specification | P1 |
| Migration fidelity | M1-M5 | Valid C# becomes different valid C# without a reported loss | P1 |
| Native switch-arm scope | N4 | A supported composition fails generated-C# compilation | P2 |
| Test oracles, gates, and specification drift | T1-T2, D1 | Existing green signals do not discriminate these failures | P2 |

Here **P1** means correctness/guarantee work to prioritize before expanding
support claims; **P2** means a subsequent completeness or regression-defense
improvement. These are engineering priorities, not security severity ratings.

The baseline runs produced **10,081 passes, 11 skips, and no failures**.
That contrast is the central test-coverage finding: the missing coverage is
often a *behavioral assertion or feature interaction*, not more execution of
the same lines. Local binding and migration branch-coverage floors were also
missed; the corpus/environment qualification is recorded below.

## Scope and method

The review sampled the native parser/AST/code-generation pipeline, C# migration,
binding and semantic analysis, effects and contract verification, and the tests
and CI gates supporting those surfaces. It traced selected constructs through
multiple stages and used small executable counterexamples where possible.
The compiler has 295 tracked C# source files; this is a risk-focused audit,
not a claim to have exhaustively verified every branch or feature combination.

The review distinguishes:

- **Correctness defects:** accepted input loses meaning, produces invalid output,
  or receives a stronger guarantee than the implementation establishes.
- **Intentional limitations:** unsupported features or deliberately bounded
  analysis, particularly when the compiler reports the limitation or preserves
  raw C#.
- **Test gaps:** existing assertions or gates do not establish the behavior
  their users need to rely on.

Source references refer to the baseline above. No compiler implementation or
existing tests were changed. Previously preserved work on
`wip/uncommitted-local-changes-20260908` is outside this audit.

## What is already strong, and what "complete" should mean

The test infrastructure is substantial: a 13-project inventory with expected
test/skip counts, component line/branch floors, deterministic mutation
targets, and specialized formatting, incremental-identity, verifier/runtime,
and project round-trip gates. AST schema tests check node/visitor/child
coverage, not just whether individual visitors happen to compile
([ArchitectureTests.cs:158-213](../../tests/Calor.Compiler.Tests/ArchitectureTests.cs#L158-L213)).
These are valuable protections to preserve.

The production Roslyn backstop also matters: it catches the invalid
double-valued cast and sibling-switch-local examples below. It cannot catch
code that is valid C# with the wrong meaning. Similarly, the project
round-trip harness genuinely builds and executes both baseline and converted
projects
([RoundTripPipeline.cs:43-57](../../tools/Calor.RoundTrip.Harness/RoundTripPipeline.cs#L43-L57),
[101-116](../../tools/Calor.RoundTrip.Harness/RoundTripPipeline.cs#L101-L116)).
The findings are not evidence that all tests are superficial.

Do not equate completeness with unrestricted C# parity. C# record migration
is explicitly non-native and preserves records as counted C# interop;
interface migration with non-representable contracts is explicitly partial
([FeatureSupport.cs:53-69](../../src/Calor.Compiler/Migration/FeatureSupport.cs#L53-L69)).
Those disclosed boundaries are preferable to the unreported semantic
changes in M1-M5.

The most useful next completeness inventory is **per construct and per
stage**: parse, bind/type-check, emit, analyze effects, verify contracts,
migrate, and execute. Each supported cell should link to positive and
negative behavioral tests; unsupported cells should specify the diagnostic,
guard fallback, or interop behavior. An AST visitor count, a Full feature
label, or aggregate line coverage cannot substitute for that inventory.

## Semantic guarantees: highest-priority findings

These probes used the production `Program.Compile` API, not a transpile-only
test helper. Type checking and effect enforcement remained enabled. The
configuration explicitly disabled verification caching, used
`ElideProvenGuards=true`, and toggled `VerifyContracts` as described below;
other options, including `ContractMode.Debug`, retained their defaults.
Generated output was independently compiled with Roslyn and invoked.

### S1. Parameter mutation can falsely prove a postcondition and delete its guard

**Priority: P1. Confidence: high; runtime-confirmed with a negative control.**

```calor
§M{m1:ParameterPoststate}
  §F{f1:Change:pub} (i32:x) -> i32
    §E{}
    §Q (>= x 0)
    §S (>= x 0)
    §ASSIGN x INT:-1
    §R x
```

For `Change(1)`, **without verification the postcondition throws
`ContractViolationException`**. With verification and guard elision enabled,
the compiler reports **`Calor0713`**, removes the guard, and returns **`-1`**.
Both generated assemblies compile.

The verifier only encodes the function body for postconditions referencing
`result`
([Z3Verifier.cs:317-321](../../src/Calor.Compiler/Verification/Z3/Z3Verifier.cs#L317-L321)).
Here it effectively proves `x >= 0` implies `x >= 0` using the entry-state
parameter, ignoring the assignment. The emitter then trusts that proof
([CSharpEmitter.cs:4604-4608](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L4604-L4608)).
The cache key has the same conditional omission of the body
([ContractHasher.cs:62-66](../../src/Calor.Compiler/Verification/Z3/Cache/ContractHasher.cs#L62-L66)).
The cold-cache failure is executed evidence; warm-cache mutation is a
source-backed additional risk that needs a regression test.

**Why it escapes:** the nearby parameter-shadowing and body-hash tests use
result-referencing postconditions, entering the body-aware path
([BindingEncodingTests.cs:277-338](../../tests/Calor.Verification.Tests/BindingEncodingTests.cs#L277-L338)).

**Fix and regression target:** model the exit state for parameter-only
postconditions, or report Unsupported and retain the guard when mutation
cannot be modeled. Include the relevant body state in proof keys and
invalidate previously unsound cached proofs. Test parameter reassignment,
void-returning functions, and warm-cache body edits. Keeping proven guards
can contain the execution consequence, but does not make the proof sound.

### S2. Always-on simplification makes a NaN-rejecting precondition ineffective

**Priority: P1. Confidence: high; runtime-confirmed without verification.**

```calor
§M{m1:NanContract}
  §F{f1:Check:pub} (f64:x) -> i32
    §E{}
    §Q (== x x)
    §R INT:7
```

`Check(double.NaN)` should fail the predicate. Instead it returns **`7`**.
The compiler reports informational `Calor0330`, calling the condition always
true, and emits a guard equivalent to `if (!(true)) throw ...`.

Self-equality folds to true without type/finiteness qualification
([ExpressionSimplifier.cs:370-378](../../src/Calor.Compiler/Verification/ExpressionSimplifier.cs#L370-L378)).
Contract simplification runs unconditionally before optional verification
([Program.cs:917-922](../../src/Calor.Compiler/Program.cs#L917-L922))
and replaces the predicate that reaches runtime
([ContractSimplificationPass.cs:347-357](../../src/Calor.Compiler/Verification/ContractSimplificationPass.cs#L347-L357)).

**Why it escapes:** existing self-equality/inequality tests assert these
folds on untyped references
([SimplificationTests.cs:424-453](../../tests/Calor.Compiler.Tests/SimplificationTests.cs#L424-L453)).
Z3's refusal to model floating-point contracts cannot protect a predicate
that was already rewritten before verification.

**Fix and regression target:** qualify algebraic rewrites by types, IEEE
behavior, and evaluation effects, rather than structural equality alone.
Add production-pipeline contract cases for NaN, infinities, and exceptional
operands. Disabling verification or retaining proven guards does **not**
repair this earlier predicate rewrite.

### S3. Indexed assignment hides mutation and effectful target expressions

**Priority: P1. Confidence: high; runtime-confirmed with a negative control.**

A function declared pure, `§E{}`, can execute:

```text
§ASSIGN §IDX items INT:0 INT:42
```

It compiles without diagnostics and changes a caller-owned array from
`[0]` to **`[42]`**. The equivalent `§SETIDX{items} INT:0 INT:42` is correctly
rejected with **`Calor0410`**, missing `mut`.

An even stronger case places a call to an explicitly console-writing
function inside the index:

```calor
§M{m1:ArrayTargetCall}
  §F{f1:Index:pub} () -> i32
    §E{cw}
    §P "effect escaped"
    §R INT:0
  §F{f2:Write:pub} ([i32]:items) -> void
    §E{}
    §ASSIGN §IDX items §C{Index} §/C INT:42
```

`Write` compiles cleanly, prints **`effect escaped`**, and mutates the array.
No permissive-effects or unsafe-transpile option was used.

Assignment inference examines the RHS and certain field/property targets,
but neither charges indexed writes nor traverses their executable target
children
([EffectEnforcementPass.cs:5221-5238](../../src/Calor.Compiler/Effects/EffectEnforcementPass.cs#L5221-L5238)).
Because assignment is a recognized statement kind, an unknown-node fallback
does not rescue this omission.

**Why it escapes:** nearby closure tests cover dotted getters/setters and
array allocation, rather than indexed writes and index-expression calls
([Issue785ClosureTests.cs:629-675](../../tests/Calor.Enforcement.Tests/Issue785ClosureTests.cs#L629-L675)).

**Fix and regression target:** traverse receiver/index expressions and
classify the write itself. Test equivalent AST spellings against the same
effect expectation, including simple/compound assignments and effectful
receivers. The two syntactic forms must not disagree about the same heap write.

### S4. Nested implementations do not inherit interface contract guards

**Priority: P1. Confidence: high; runtime-confirmed with a top-level control.**

An `IPositive.Get` interface method requires positive input/output. A
contract-free implementation in `Outer.Inner` returning its input accepts
`-1` and returns **`-1`**. Moving the same implementation to module level
causes `Calor0812` inheritance reporting and a runtime
`ContractViolationException`.

The inheritance checker enumerates only module-level classes
([ContractInheritanceChecker.cs:63-74](../../src/Calor.Compiler/Verification/ContractInheritanceChecker.cs#L63-L74)).
The emitter depends on that result to acquire inherited guards
([CSharpEmitter.cs:5632-5664](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L5632-L5664)).
The C# interface remains valid; Roslyn cannot reconstruct Calor's missing
contracts.

**Why it escapes:** the existing inheritance-emission tests cover top-level
implementations
([ContractInheritanceTests.cs:296-359](../../tests/Calor.Compiler.Tests/ContractInheritanceTests.cs#L296-L359)).

**Fix and regression target:** make inheritance traversal cover nested
declarations with correctly qualified identities. Run the same contract
inheritance fixtures at multiple nesting depths and assert actual failing
inputs throw, rather than just checking generated guard text.

## Native language findings

### N1. C# emission loses expression and pattern grouping

**Priority: P1. Confidence: high; runtime-confirmed.**

The parser retains the nested structure, but multiple emitters interpolate
child text without preserving precedence. These programs successfully pass
the default CLI compilation path:

| Native expression or pattern | Input | Expected | Executed result |
|---|---|---|---|
| `(upper (+ a b))` | `"a"`, `"b"` | `"AB"` | **`"aB"`** |
| `(+ (?? x 1) 2)` | nullable integer `x = 5` | `7` | **`5`** |
| `(not (and §PREL{gte} 0 §PREL{lte} 10))` | `20` | Match outside `[0,10]` | **Does not match** |

For example:

```calor
§M{m:ExpressionGrouping}
  §F{f:Probe:pub} (str:a, str:b) -> str
    §R (upper (+ a b))
```

The emitted return is `a + b.ToUpper()`, not `(a + b).ToUpper()`.
Similarly, coalescing becomes `x ?? 1 + 2`, and the compound pattern becomes
`not >= 0 and <= 10`. The coalescing CLI repro also emits a `Calor0200`
warning about the nullable type; it still succeeds and executes incorrectly.

**Cause:** binary precedence handling considers only binary-node children
([CSharpEmitter.cs:3857-3863](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L3857-L3863)).
Null coalescing, string receivers, and compound patterns emit ungrouped
children
([7071-7075](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L7071-L7075),
[8089-8120](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L8089-L8120),
[4497-4499](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L4497-L4499)).

**Why it escapes:** a test explicitly enshrines the wrong output:
`Parse_CastWithBinaryOp_Works` asserts `(int)x + 1` for
`(cast i32 (+ x 1))`
([TypeOperationTests.cs:426-439](../../tests/Calor.Compiler.Tests/TypeOperationTests.cs#L426-L439)).
Existing runtime uppercase tests use simple receivers; binary precedence
tests cover binary-under-binary composition. Another executed cast repro
emitted `(int)x + y` for a cast of a double-valued sum and was rejected
downstream with `CS0266`/`Calor1002`, rather than silently miscompiled.

**Fix and regression target:** centralize precedence-aware expression and
pattern emission. Correct the erroneous test oracle. Execute a child-node
composition matrix across arithmetic, coalescing, casts, receivers, and
nested `not`/`and`/`or` patterns; isolated operator tests are insufficient.

### N2. Expression-match block arms discard executable statements

**Priority: P1. Confidence: high; runtime-confirmed.**

```calor
§M{m:MatchBody}
  §F{f:Probe:pub} (i32:x) -> i32
    §E{cw}
    §R §W{w} x
      §K 1
        §P "must execute"
        §R 7
      §K _ → 0
```

**Expected for `1`:** print `must execute`, then return `7`.  
**Observed:** return `7`, with empty captured stdout. The default CLI succeeds.

The parser and AST retain the complete case body, but the expression emitter
uses only its final return expression. If the last statement is not a
value-return, it substitutes `default`
([CSharpEmitter.cs:4397-4408](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L4397-L4408)).

**Why it escapes:** `Parse_MatchMixedSyntax_AllowsBothStyles` explicitly
accepts a print-plus-return expression arm and verifies both AST statements,
but never executes them
([PatternMatchingParserTests.cs:88-117](../../tests/Calor.Compiler.Tests/PatternMatchingParserTests.cs#L88-L117)).
Other tests named "GeneratesValidCSharp" and "PreservesSemantics" check
substrings, not execution
([273-293](../../tests/Calor.Compiler.Tests/PatternMatchingParserTests.cs#L273-L293),
[441-460](../../tests/Calor.Compiler.Tests/PatternMatchingParserTests.cs#L441-L460)).

**Fix and regression target:** preserve the full block's execution, or reject
expression-position block arms explicitly until supported. Execute stdout,
mutation, arm-local binding, and nested-return cases. Silently selecting the
last return is not an acceptable partial-support behavior.

### N3. Default arithmetic wraps despite the specified trapping behavior

**Priority: P1. Confidence: high; runtime-confirmed specification mismatch.**

```calor
§M{m:OverflowDefault}
  §F{f:Probe:pub} (i32:x) -> i32
    §R (+ x 1)
```

With `x = 2147483647`, the generated code returns **`-2147483648`** without
throwing. Default CLI compilation succeeds and emits `return x + 1;`.
The normative specification requires default TRAP/`OverflowException`
([core.md:190-204](../semantics/core.md#L190-L204)).

Ordinary arithmetic emission does not insert `checked`, and the shipped
execution-project template does not enable `CheckForOverflowUnderflow`
([CSharpEmitter.cs:3825-3863](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L3825-L3863),
[ExecutionWorkspace.cs:310-324](../../src/Calor.Compiler/Commands/ExecutionWorkspace.cs#L310-L324)).
The observed result comes from compiling the generated C# with normal Roslyn
settings, without injecting checked arithmetic.

**Why it escapes:** `S7_IntegerOverflow_Traps` calls `ExecuteChecked`
([NumericTests.cs:15-34](../../tests/Calor.Semantics.Tests/NumericTests.cs#L15-L34)).
The harness itself adds `.WithOverflowChecks(true)`
([SemanticsTestHarness.cs:237](../../tests/Calor.Semantics.Tests/SemanticsTestHarness.cs#L237)).
That test passes, but establishes the behavior of the stronger test
configuration, not the shipped default backend.

**Fix and regression target:** align production emission/backend settings
with the specified overflow policy, and test through those same settings.
Cover dynamic addition, subtraction, multiplication, and narrowing
conversions at boundaries. Do not inject the property being tested only in
the test harness.

### N4. Sibling match arms lack emitted local scopes

**Priority: P2. Confidence: high; rejection reproduced.**

Declaring a local named `v` in each of two statement-match arms produces
`CS0128` and `CS0165` through `Calor1002`, although the emitter internally
treats the arms as separate scopes. It pushes/pops declaration scopes
without emitting corresponding C# braces
([CSharpEmitter.cs:4457-4463](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L4457-L4463)).

This is a completeness defect caught by the Roslyn backstop, **not a silent
successful miscompile**. Emit matching lexical scopes and test same-name
ordinary locals in sibling cases. Existing sibling-rebinding coverage
exercises `if` blocks, which already emit braces
([SiblingRebindCodegenTests.cs:24-34](../../tests/Calor.Compiler.Tests/CodeGen/SiblingRebindCodegenTests.cs#L24-L34)).

## C# migration fidelity findings

For the five cases below, the default **C# to Calor** conversion reported
`success: true`, `fidelity: "lossless"`, `validated: true`, `lossCount: 0`,
and no diagnostics or interop preservation. The original C# and the
C# regenerated from Calor were then independently compiled with Roslyn and
executed with the same driver. Both compiled; their behavior differed.
The compact C# snippets below are method bodies; the runtime fixtures wrap
them in public static methods with the appropriate collection/LINQ imports.

This is the most important completeness distinction in the migration
surface: a feature can be accepted and classified as native without its
semantics actually being preserved.

### M1. Postfix hoisting executes a short-circuited operand

**Priority: P1. Confidence: high; runtime-confirmed.**

```csharp
int i = 0;
bool gate = false;
bool ignored = gate && i++ > 0;
return i;
```

**Expected: `0`. Observed after round trip: `1`.** The converter saves and
increments `i` before evaluating `gate`, rather than within the conditional
right-hand operand.

Postfix expressions append to `_pendingStatements`
([RoslynSyntaxVisitor.cs:10263-10276](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L10263-L10276)).
Binary conversion visits both operands
([9047-9057](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L9047-L9057)),
and pending statements are flushed before the enclosing statement
([6627-6633](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L6627-L6633)).

**Why it escapes:** the existing postfix test uses standalone `int x = i++;`
and checks generated bindings/absence of an error marker, not conditional
execution
([ConversionCampaignFixTests.cs:1369-1388](../../tests/Calor.Compiler.Tests/ConversionCampaignFixTests.cs#L1369-L1388)).

**Fix and regression target:** preserve execution regions during lowering;
otherwise preserve the enclosing member as counted C# interop. Add
original-versus-generated execution tests for side-effecting or throwing
operands under `&&`, `||`, `??`, conditional expressions, and null-conditional
operations. Only the `&&` case above was executed in this audit.

### M2. Tuple assignment becomes sequential writes

**Priority: P1. Confidence: high; runtime-confirmed.**

```csharp
int a = 1;
int b = 2;
(a, b) = (b, a);
return a * 10 + b;
```

**Expected: `21`. Observed: `22`.** The generated code is `a = b; b = a;`.
The tuple conversion loop emits individual writes without capturing the
right-hand values first
([RoslynSyntaxVisitor.cs:6535-6552](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L6535-L6552)).
Tuple deconstruction is advertised as Full support
([FeatureSupport.cs:992-997](../../src/Calor.Compiler/Migration/FeatureSupport.cs#L992-L997)).

**Why it escapes:** the regression fixtures use non-overlapping assignments,
such as `(_a, _b) = (x, y)`, and assert emitted assignment markers rather than
behavior
([ConversionCampaignFixTests.cs:433-470](../../tests/Calor.Compiler.Tests/ConversionCampaignFixTests.cs#L433-L470)).

**Fix and regression target:** preserve C# evaluation order and capture the
necessary RHS values before writes. Execute swap and rotation cases, then
overlapping properties/indexers and RHS side-effect-order cases.

### M3. Dictionary initialization loses operation kind and concrete type

**Priority: P1. Confidence: high; runtime-confirmed.**

```csharp
var d = new Dictionary<int, int> { [1] = 2, [1] = 3 };
return d[1];
```

**Expected: `3`. Observed: `ArgumentException`.** Index initializers call a
setter and permit overwriting; the emitted initializer uses two Add-style
entries, `{ { 1, 2 }, { 1, 3 } }`.

The converter maps setter and Add forms to indistinguishable key/value
entries
([RoslynSyntaxVisitor.cs:11427-11455](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L11427-L11455)),
and the C# emitter always emits Add-style entries
([CSharpEmitter.cs:4824-4843](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L4824-L4843)).
The conversion also discards the concrete dictionary type
([RoslynSyntaxVisitor.cs:11413-11467](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L11413-L11467)):
a separate initialized `SortedDictionary<int,int>` repro became a
`Dictionary<int,int>`, confirmed through runtime type names.

**Why it escapes:** both initializer forms and multiple concrete dictionary
types are advertised as Full
([FeatureSupport.cs:225-229](../../src/Calor.Compiler/Migration/FeatureSupport.cs#L225-L229)).
The catalog fixture uses unique Add-style keys and opts out of round-trip
support
([ConversionCatalog.cs:880-906](../../tests/Calor.Conversion.Tests/ConversionCatalog.cs#L880-L906)).

**Fix and regression target:** retain concrete type, constructor/comparer,
and initializer operation kind. Execute duplicate-key overwrite versus Add
exception cases and assert concrete type identity. Add ordering and comparer
tests rather than infer those guarantees from compilation success.

### M4. C# loop conditions are incorrectly treated as fixed range bounds

**Priority: P1. Confidence: high; runtime-confirmed.**

```csharp
int limit = 3;
int count = 0;
for (int i = 0; i < limit; i++)
{
    count++;
    limit--;
}
return count;
```

**Expected: `2`. Observed: `3`.** C# reevaluates the condition, but the
converted native range captures `limit - 1` before entering the loop.

The converter extracts a binary condition's RHS as a range endpoint without
establishing loop invariance
([RoslynSyntaxVisitor.cs:8072-8124](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L8072-L8124)).
The native range emitter intentionally captures its operands
([CSharpEmitter.cs:3480-3512](../../src/Calor.Compiler/CodeGen/CSharpEmitter.cs#L3480-L3512));
the defect is selecting that representation for a C# loop with different
semantics, not the native range semantics themselves.

**Why it escapes:** catalog coverage uses conventional fixed-bound loop
shapes. The related non-additive-incrementor defect is already documented
in the feature registry and has a skipped regression test
([FeatureSupport.cs:118-131](../../src/Calor.Compiler/Migration/FeatureSupport.cs#L118-L131),
[Issue774ForLoopNonAdditiveIncrementorTests.cs:44-54](../../tests/Calor.Compiler.Tests/Issue774ForLoopNonAdditiveIncrementorTests.cs#L44-L54)).
The mutable-bound repro is additional to that known defect.

**Fix and regression target:** use a native range only when the equivalence
is established; otherwise preserve the C# loop or lower it with exact
condition/increment behavior. A while-loop fallback must still execute the
incrementor on `continue`. Execute mutable and side-effecting bounds,
non-additive increments, and `continue` paths; do not merely assert that a
particular AST node disappeared.

### M5. LINQ grouping discards the element projection

**Priority: P1. Confidence: high; runtime-confirmed.**

```csharp
return from n in values group n * 10 by n % 2;
```

For `values = new[] { 1, 2 }`, enumerating group contents should produce
**`10,20`**; the round trip produced **`1,2`**. It emits
`values.GroupBy(n => n % 2)`, losing the element selector.

The conversion handles `ByExpression` but never `GroupExpression`
([RoslynSyntaxVisitor.cs:11320-11324](../../src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs#L11320-L11324)).
Query syntax is advertised as Full with equivalent method-chain desugaring
([FeatureSupport.cs:207-211](../../src/Calor.Compiler/Migration/FeatureSupport.cs#L207-L211)).

**Why it escapes:** the group test uses identity projection, `group p by
p.Category`, and checks only for `GroupBy`
([LinqSupportTests.cs:411-423](../../tests/Calor.Compiler.Tests/LinqSupportTests.cs#L411-L423)).

**Fix and regression target:** emit the element-selector overload and
execute non-identity grouping cases. Add side-effect/deferred-enumeration
assertions so preserving the projected values does not introduce eager
evaluation.

### Common migration test gap

The ordinary round-trip helper does perform meaningful parsing and Roslyn
compilation checks. However, it returns after compilation and reporting
recorded losses; it does not compare execution
([TestHelpers.cs:88-135](../../tests/Calor.Conversion.Tests/TestHelpers.cs#L88-L135)).
A transformation that silently changes valid C# cannot be detected merely
by asserting that it emitted valid C# and recorded no loss.

Keep the snapshot/compile gates, but add a small C#-origin behavioral
differential suite for M1-M5. The project-level round-trip harness already
builds and runs tests; the missing piece is explicit, discriminating
language-interaction fixtures at this small scale.

## Cross-cutting test and specification gaps

### T1. Expand generated tests from parseable shapes to semantic equivalence

**Priority: P2. Confidence: high. Classification: test-design gap.**

The parse/pretty/parse property generator produces one function containing
one to three print statements, drawn from nine fixed literals
([ParsePrettyRoundTripPropertyTests.cs:32-79](../../tests/Calor.Compiler.Tests/PropertyTests/ParsePrettyRoundTripPropertyTests.cs#L32-L79)).
The properties check successful reparsing, module identity, function count,
and statement count, but not literal values or observable results
([same file:135-194](../../tests/Calor.Compiler.Tests/PropertyTests/ParsePrettyRoundTripPropertyTests.cs#L135-L194)).
For example, replacing every printed integer with zero while preserving
statement count would satisfy these properties.

This is an explicitly documented smoke test, not a misleading implementation.
There are also effect-matching and type-canonicalization properties, ordinary
regression tests, and a verifier/runtime differential gate. The gap is that
this randomized pipeline test does not cover the interactions most likely to
miscompile: nested expressions with side effects, loops, short-circuiting,
exception edges, generics, patterns, and numeric boundaries.

**Recommendation:** extend the existing FsCheck infrastructure rather than
introduce another framework. Generate a bounded, well-typed subset with a
small independent evaluator or known expected traces; compare results,
exceptions, and side-effect order after emission. Normalize incidental IDs
when comparing ASTs, but compare literal values, operators, and child
structure. Include bounded malformed-input cases with explicit termination
and diagnostic assertions. Keep accepted-sample counts visible: currently a
first-parse failure returns `true`, with a separate 30-sample sanity test
guarding the generator's acceptance rate.

### T2. Coverage floors and mutation targets leave important stages unguarded

**Priority: P2. Confidence: high. Classification: regression-gate gap.**

The seven coverage components do not include `Parsing/` or `TypeChecking/`
([coverage-baselines.json:3-11](../../eng/coverage-baselines.json#L3-L11)).
Both have substantial exercised code in this audit: parsing measured
82.27% line coverage and type checking 72.68%. These are measured values,
**not** proposed thresholds. A decline confined to those directories is not
directly constrained by the current per-component coverage gate.

The mutation gate is a useful deterministic set of **seven hand-selected
mutants**, with a configured 100% required kill rate. Its target labelled
`emitter` changes brace escaping in `Migration/CalorEmitter.cs`, not native
C# generation in `CodeGen/CSharpEmitter.cs`
([mutation-baselines.json:3-68](../../eng/mutation-baselines.json#L3-L68)).
There is no mutation target for native lexer/parser dispatch, arithmetic
emission, or evaluation order. A perfect score on these seven mutations is
not an estimate of general mutation coverage.

**Recommendation:** add measured parsing/type-checking floors and a small
set of behavior-oriented mutants for native C# emission and the confirmed
defects in this report. Prefer changing an operator, duplicating an operand,
or dropping a side-effecting expression over adding more structural
visitor-presence checks. Preserve the existing fast, deterministic gate;
there is no need to adopt a large mutation framework.

### D1. The advertised semantics reference is outside the main drift checks

**Priority: P2. Confidence: high. Classification: specification accuracy.**

`calor self-check docs` passes on this baseline, but the keyword and
parse-example scan set consists of the agent instructions, syntax reference,
CLI reference, and exemplar sheet. `docs/semantics/` is not in that set
([DocDriftChecker.cs:212-220](../../src/Calor.Compiler/SelfCheck/DocDriftChecker.cs#L212-L220),
[261-275](../../src/Calor.Compiler/SelfCheck/DocDriftChecker.cs#L261-L275)).
The broader version scan checks the *compiler package version* and does not
establish that the semantics version or feature inventory is current.

Concrete drift remains:

- The semantics landing page ends with **"Semantics Version: 1.0.0"**
  ([README.md:152-156](../semantics/README.md#L152-L156)), while the compiler
  implements `2.0.0` and refuses a different declared major
  ([SemanticsVersion.cs:19-52](../../src/Calor.Compiler/SemanticsVersion.cs#L19-L52)).
- The purported complete inventory says **134** visitor methods
  ([inventory.md:7](../semantics/inventory.md#L7)); the current AST schema
  and non-generic visitor interface contain **184** nodes/methods.
- The landing page describes CNF as the semantics-enforcing production
  pipeline ([README.md:89-105](../semantics/README.md#L89-L105)), but the
  compiler directly calls `CSharpEmitter.Emit(ast)`
  ([Program.cs:1055-1070](../../src/Calor.Compiler/Program.cs#L1055-L1070)).
  The CNF lowering implementation is not connected to that production path.
  Its internal incompleteness should not be confused with a reachable default
  compiler defect.

These are concrete accuracy problems in documents that tell agent authors to
train against the specification, not merely stale historical release notes.

**Recommendation:** bring normative semantics documents into drift checking,
derive the inventory from `eng/ast-schema.json`, and check semantics-version
claims separately from package-version claims. Associate each normative rule
with a positive execution test and, where appropriate, a negative test.
Do not automatically treat dated design/planning documents as normative.

## Recommended remediation sequence

1. **Contain incorrect guarantees first (S1-S4).** Retain guards for
   unmodeled poststate changes, repair proof-cache dependencies, qualify
   simplification by actual types/semantics, and close traversal omissions
   in assignment effects and nested inheritance. Pair every fix with the
   demonstrated passing/failing control.
2. **Repair native semantic preservation (N1-N3).** Centralize grouping,
   preserve complete match-arm execution, and resolve the production
   overflow-policy mismatch. Correct tests that currently assert incorrect
   output or strengthen the backend only inside the harness.
3. **Make migration fidelity conservative (M1-M5).** Fix each lowering or
   preserve unsupported shapes as explicitly counted interop. Downgrade
   broad Full claims until their behavioral cases pass. Do not regard
   successful reparsing/Roslyn compilation as proof of losslessness.
4. **Make these fixes durable (N4, T1-T2, D1).** Add a small behavioral
   differential corpus and per-stage support ledger, then extend the
   existing property/mutation gates and synchronize normative docs.

Acceptance should be demonstrated with the actual user path and ordinary
backend settings: the failing contract must throw, the allegedly pure
write must be rejected, and original/converted programs must agree on
return values, exceptions, state changes, and output. Assertions on strings,
node counts, or diagnostic absence remain useful secondary checks.

This report deliberately does **not** recommend chasing 100% line coverage,
adding every remaining C# feature, or replacing the existing test framework.
The highest-value work is closing the concrete semantic holes and making
support claims conditional on discriminating tests.

## Test evidence and its limits

The Release solution built after restoring missing local dependency assets,
using .NET SDK `10.0.400` on macOS. Seven test projects were run against those
binaries. The compiler suite used the same two complementary filters as
`scripts/compiler-shard-filter.sh`, rather than one long-lived test host.

| Project | Passed | Skipped | Total |
|---|---:|---:|---:|
| Calor.Compiler.Tests | 8,020 | 10 | 8,030 |
| Calor.Conversion.Tests | 466 | 0 | 466 |
| Calor.Semantics.Tests | 51 | 0 | 51 |
| Calor.Verification.Tests | 395 | 0 | 395 |
| Calor.Enforcement.Tests | 669 | 1 | 670 |
| Calor.Evaluation | 206 | 0 | 206 |
| Calor.RoundTrip.Harness.Tests | 274 | 0 | 274 |
| **Total** | **10,081** | **11** | **10,092** |

There were no failed tests in those runs. This is evidence that the *existing
assertions* pass, not that the language is complete. The manifest declares
11,175 test cases across 13 projects; the six remaining project suites were not
run as part of this language-focused audit.
Additional focused test replays overlapped these suites and are not added to
the totals.

Seven skips were corpus-dependent: six compiler tests and one enforcement
test. Two required network access, one represented the known non-additive
for-loop conversion defect (#774), and one awaited an archived experiment
epoch. All 395 verification tests ran; no Z3-unavailable skip occurred.

The external MediatR, Serilog, and FluentValidation submodules were not
initialized. Consequently, the full external round-trip harness and the
corpus-dependent assertions were **not** replayed. The 274 harness unit tests
above are not a substitute for that run.

### Measured coverage versus configured floors

Coverage was collected from the five core projects used by the CI coverage
job: compiler, conversion, semantics, verification, and enforcement. The
table uses the repository's own `scripts/check_coverage.py` aggregation.
Evaluation and harness tests were run without coverage collection.

| Component | Local line % | Floor | Local branch % | Floor |
|---|---:|---:|---:|---:|
| Verification | 74.92 | 74.00 | 67.97 | 65.00 |
| C# code generation | 85.54 | 82.00 | 80.03 | 77.00 |
| Binding | 88.32 | 88.00 | **75.12** | **78.00** |
| Migration | 80.05 | 78.00 | **70.67** | **71.00** |
| Dataflow | 83.81 | 66.00 | 73.99 | 64.00 |
| Taint analysis | 90.89 | 75.00 | 80.52 | 67.00 |
| Effects | 62.88 | 51.00 | 53.40 | 47.00 |

**The local coverage gate failed on binding and migration branch coverage.**
This checkout lacks the corpus used by CI, so these are not asserted to be
CI regressions, nor is the missing corpus asserted to be the only explanation.
A comparable corpus-enabled run is needed before changing thresholds or
attributing the shortfall.

The test-manifest/skip/assertion-quality checker passed, as did the coverage,
TRX, and mutation-checker self-tests. `calor self-check docs` reported no
drift. The actual mutation campaign, LSP flake campaign, performance gates,
and full release workflow were not run.

### Reproduction commands

The audit used these existing entry points; result-directory paths are omitted
for portability:

```bash
dotnet restore
dotnet build -c Release --no-restore

# Use each of the two filters printed by this script in separate compiler runs.
bash scripts/compiler-shard-filter.sh
dotnet test tests/Calor.Compiler.Tests/Calor.Compiler.Tests.csproj \
  -c Release --no-build --no-restore --filter "<one printed filter>" \
  --collect:"XPlat Code Coverage" --logger trx

# Repeat this command for Conversion, Semantics, Verification, and Enforcement.
dotnet test tests/Calor.Conversion.Tests/Calor.Conversion.Tests.csproj \
  -c Release --no-build --no-restore \
  --collect:"XPlat Code Coverage" --logger trx

dotnet test tests/Calor.Evaluation/Calor.Evaluation.csproj \
  -c Release --no-build --no-restore --logger trx
dotnet test tests/Calor.RoundTrip.Harness.Tests/Calor.RoundTrip.Harness.Tests.csproj \
  -c Release --no-build --no-restore --logger trx

python3 scripts/check_test_quality.py
python3 scripts/check_coverage.py --self-test
python3 scripts/check_trx.py --self-test
python3 scripts/run_mutation_gate.py --self-test
dotnet src/Calor.Compiler/bin/Release/net10.0/calor.dll self-check docs
```

The audit's raw TRX, coverage XML, and logs are retained in the session
artifacts under `files/audit-results/`; the findings and essential evidence
are recorded in this report so those local artifacts are not required to
understand the recommendations.
Native and migration fixtures/probes are in `files/audit-language/` and
`files/audit-migration/`; semantic fixtures, negative controls, and the
production-API execution probe are in `files/audit-semantics/`.
