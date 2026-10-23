# S2 #1413 fix-obligation-state — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only. I did not run `dotnet`. The new named-refinement facts introduce a false discharge, and several mutation paths still permit false proofs.

1. **BLOCKING — A parameter named `result` can discharge a false return refinement.**

   ```calor
   §M{m1:M}
     §RTYPE{r1:Pos:i32} (> # INT:0)
     §F{f1:Probe:pub} (Pos:result) -> Pos
       §E{}
       §R INT:-5
   ```

   `FactCollector.cs:172–177` adds `result > 0` as an entry fact. [ObligationSolver.cs:93](src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:93) then declares the return value using that same name. `ContractTranslator.cs:394–401` overwrites the variable registration; both declarations produce the same Z3 constant.

   The return predicate and entry assumption become identical, so the query is UNSAT and the return obligation is **Discharged**, although the function returns `-5`. The RefinementReturn demotion at `ObligationSolver.cs:281–282` applies only to SAT. `CSharpEmitter.cs:6175–6178` consequently removes the return guard.

   This is newly exposed by the named-refinement facts. `Z3Verifier.cs:212–222` already refuses this collision in contract verification; the obligation solver needs equivalent protection or a fresh return symbol.

2. **BLOCKING — Inline raw C# remains invisible to mutation killing.**

   [FactCollector.cs:200](src/Calor.Compiler/Verification/Obligations/FactCollector.cs:200) recognizes only `RawCSharpNode` as opaque. The parser also supports `RawCSharpExpressionNode` through `§CS{...}` (`Parser.cs:11808–11812`), and the emitter returns its contents verbatim (`CSharpEmitter.cs:8900–8903`).

   For an `i32:x` parameter, this body retains the stale precondition and discharges the false proof:

   ```text
   §Q (> x INT:0)
   §B{sink:i32} §CS{(x = -5)}
   §PROOF{p1:claim} (> x INT:0)
   §R x
   ```

   The collector records `sink`, never `x`. The same omission makes the **new** named-refinement entry fact stale for a `Pos:x` parameter mutated through `§CS`.

3. **BLOCKING — Ref alias expansion does not protect scoped guard facts, `in` parameters, or constructors.**

   Alias expansion occurs in `CollectFromCallable` at `FactCollector.cs:144–149`, but [AddGuardFact at line 407](src/Calor.Compiler/Verification/Obligations/FactCollector.cs:407) recomputes assigned names without that expansion.

   With parameters `(i32:x:ref, i32:y:ref)`:

   ```text
   §IF{if1} (< x INT:0)
     §R INT:0
   §EL
     §ASSIGN y INT:-5
     §PROOF{p1:claim} (>= x INT:0)
   §R x
   ```

   Calling with the same positive variable twice reaches the else branch, then makes `x == -5`. Nevertheless, the new else fact survives because its local assigned-name set contains only `y`. That fact discharges the false proof. The solver’s stale-condition check cannot rescue this: it runs only for SAT.

   Two additional entry-fact holes exist:

   - `in x` can alias `ref y`, but the expansion excludes `ParameterModifier.In`, despite its supported emission at `CSharpEmitter.cs:2965`.
   - Constructors use `CollectFromStatements`, bypassing alias expansion entirely (`ObligationSolver.cs:435`).

4. **BLOCKING — Pointer stores can mutate scalar parameters without killing their facts.**

   [FactCollector.cs:523](src/Calor.Compiler/Verification/Obligations/FactCollector.cs:523) has no `PointerDereferenceNode` case in `RootName`. An ordinary `i32:x` parameter can be changed through its address:

   ```text
   §Q (> x INT:0)
   §UNSAFE{u1}
     §B{ptr:i32*} §ADDR x
     §ASSIGN §DEREF ptr INT:-5
   §PROOF{p1:claim} (> x INT:0)
   §R x
   ```

   Only `ptr` is recorded as assigned. The store sets `MutatesHeap`, but the precondition reads scalar `x`, so `ReadsHeap` is false. The stale precondition still discharges the proof.

   These are executable AST forms: `CSharpEmitter.cs:9398–9407` emits address-taking and dereferencing. Without pointer alias analysis, address escape/indirect writes must conservatively invalidate relevant scalar facts.

5. **BLOCKING — Calls to previously defined closures do not kill captured scalar guard facts.**

   Define an `Action` lambda that assigns captured parameter `x = -5` before an if. In the else branch of `x < 0`, invoke that action, then prove `x >= 0`.

   The whole-function scan sees the lambda’s assignment, but [FactCollector.cs:407](src/Calor.Compiler/Verification/Obligations/FactCollector.cs:407) scans only the branch when deciding whether its guard survives. The invocation contains no assignment or ref/out argument. The call-based heap kill at lines 411–416 does not apply to scalar `x`.

   Consequently, the new else fact discharges a claim that becomes false after invocation. `CollectAssignedNames` needs callable effects, or conservative invalidation of captured storage at invocation.

   Lexical scoping also lets outer branch facts apply to proofs inside deferred lambda bodies (`ObligationGenerator.cs:178–187`). Such bodies can execute after their enclosing branch facts cease to hold; they need separate callable analysis.

6. **MAJOR — Exactness bookkeeping accidentally prevents valid fact collection.**

   [FactCollector.cs:281](src/Calor.Compiler/Verification/Obligations/FactCollector.cs:281) puts `AddGuardFact` behind `exact &&`. The same problem occurs for elseif, while, and for at lines 288, 236, and 229.

   Example:

   ```text
   §IF{exit} (< x INT:0)
     §R INT:0
   §IF{positive} (> x INT:5)
     §PROOF{p1:claim} (> x INT:0)
   §R x
   ```

   The first if marks subsequent statements inexact. The second if therefore never collects its own valid guard. A proof previously discharged from `x > 5` becomes Unsupported. This also affects nested guards inside foreach and try bodies, which start inexact.

   Collect facts independently, then combine their availability with exactness. The current short-circuiting breaks valid proofs and can turn them into errors under Strict policy.

7. **MAJOR — Loop exits still produce unreachable Failed models.**

   [FactCollector.cs:220](src/Calor.Compiler/Verification/Obligations/FactCollector.cs:220) leaves following statements exact after a loop that contains no syntactic diversion. It adds no loop-exit condition.

   An empty `while (x < 0)` followed by `§PROOF (>= x 0)` is correct on every execution reaching the proof: negative inputs never leave the loop. Nevertheless, `x = -1` is accepted as a counterexample and produces Failed.

   The documented residual for preceding throwing expressions is another reachability gap (`FactCollector.cs:108–109`). Thus “exact” still does not establish that a SAT model reaches the obligation.

8. **MAJOR — Named-refinement assumptions are omitted for constructors and operators.**

   [ObligationSolver.cs:435](src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:435) analyzes constructor bodies without their parameters or refinement predicates; operators take the same route at line 490.

   A public constructor receiving `Pos:x` and immediately proving `x > 0` has an emitted entry guard, but no corresponding solver fact. The proof is considered exact and can be Failed with `x = 0`, which the entry guard excludes. The parameter-aware collection path needs to cover these callables too.

9. **MINOR — The changelog overstates the reachability guarantee.**

   [CHANGELOG.md:54](CHANGELOG.md:54) says Failed occurs “only when its counterexample can actually reach the obligation.” Findings 7–8 contradict that, as does the explicitly documented throwing-expression residual.

   Also, “ignored after any reassignment” suggests ordering precision. The implementation kills entry facts based on writes **anywhere in the body**, including writes after an obligation.

The tests contain **14 Facts plus 11 theory rows = 25**, matching `12587 → 12612`. The claimed 20 pre-fix failures is consistent with static inspection, but I did not execution-verify it. The array-store test at `S2ObligationStateTests.cs:108–121` is non-discriminating for this repair: the existing nullable-reference-model demotion already prevents Discharged/Failed. The five named-refinement tests at lines 315–337 also accept blanket Unsupported outcomes. The new suite does not cover the blocking witnesses above.

The AST mutation inventory is incomplete beyond the concrete witnesses: increments of non-reference targets, expression-target calls, event updates/custom accessors, and implicit disposal effects are omitted. Its declaration inventory also misses foreach index variables, dictionary key/value bindings, using/fixed bindings, catch variables, and pattern bindings. Those omissions should not all be treated as executable false-proof witnesses, but the collector is not exhaustive.

The **Failed → Unsupported demotion itself preserves protection**: `ShouldEmitObligationGuard` retains every non-Discharged status (`CSharpEmitter.cs:578–584`), `Visit(ProofObligationNode)` explicitly guards Unsupported (`9945–9950`), and the default policy produces a visible warning (`Program.cs:980–1005`). The unsafe guard removals identified above come from remaining or newly introduced **Discharged** outcomes.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING parameter named `result` + named-refinement fact discharges the refined return | Fixed: a `RefinementReturn` obligation in a callable that has a parameter named `result` is `Unsupported` (the same refusal the contract verifier already applies). Regression: `ParameterNamedResult_DoesNotDischargeTheRefinedReturn`. |
| 2 BLOCKING `§CS{...}` writes invisible | Fixed: `RawCSharpExpressionNode` makes the body opaque (no entry or guard facts, no exact state). Regression: `IndirectWrite_KillsFacts` (§CS row). |
| 3 BLOCKING alias expansion misses guard facts, `in`, constructors | Fixed: the alias group includes `in`. Once any member is written, every member counts as written, and guard facts reading a member are killed in every body. Constructors and operators now go through the parameter-aware collection, which also fixes 8. Regression: `IndirectWrite_KillsFacts` (aliased else-branch row). |
| 4 BLOCKING pointer stores | Fixed conservatively: `§UNSAFE`, `§FIXED`, `§ADDR`, and `§DEREF` make the body opaque. Regression: `IndirectWrite_KillsFacts` (pointer row). |
| 5 BLOCKING closures | Fixed conservatively: any lambda makes the body opaque. This covers a captured write invoked inside a guarded branch and deferred bodies that run after their guards stop holding. Regression: `IndirectWrite_KillsFacts` (closure row). Cost: no obligation facts in functions that contain a lambda (completeness only). |
| 6 MAJOR exactness gating blocked fact collection | Fixed: guard facts are collected first, and exactness is combined afterwards (if/elseif/while/for). Regression: `GuardAfterEarlyExit_StillUsedAsAFact` (Discharged again). |
| 7 MAJOR loop exits | Fixed: a statement after any loop (while/for/do/foreach) is not exact; the exit condition is not asserted. Regression: `ObligationAfterLoop_IsNotRefutedWithANonExitingModel`. The throwing-predecessor residual stays documented. |
| 8 MAJOR constructors/operators lacked parameter facts | Fixed (see 3). Regression: `ConstructorNamedRefinementParameter_IsAnEntryFact` (Discharged). |
| 9 MINOR CHANGELOG overstatement | Rewritten. It now says facts are dropped for writes anywhere in the body, lists the opaque constructs, says which reachability is checked, and names the throwing-predecessor residual. |
| Inventory notes (non-ref increments, binders) | Increments of element/field targets record their root. Every declared name that could shadow a parameter counts as written: patterns, lambda parameters, array/collection declarations, and any node with a `VariableName`, `KeyName`, `ValueName`, or `BindingName` (catch, foreach key/value, using). The array-store test stays as a labeled control. |
