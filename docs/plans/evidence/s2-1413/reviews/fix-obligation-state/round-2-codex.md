# S2 #1413 fix-obligation-state — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only; I did not run `dotnet`. The specific Round 1 witnesses are repaired, but two remaining paths can falsely discharge obligations and remove their guards.

| Round 1 finding | Verification in current code |
|---|---|
| 1 — Parameter named `result` | Resolved: `ObligationSolver.cs:63–67` refuses the colliding return obligation. |
| 2 — Inline `§CS` writes | Resolved: `FactCollector.cs:208–210` makes `RawCSharpExpressionNode` opaque. |
| 3 — Scoped aliases, `in`, constructors | Original witnesses resolved: alias expansion includes `in` at `FactCollector.cs:145–152`; guard killing uses `_aliasWritten` at `425`; constructors use parameter-aware collection at `ObligationSolver.cs:444`. Broader aliasing remains unsafe below. |
| 4 — Pointer stores | Resolved conservatively: `FactCollector.cs:208–210` makes unsafe/fixed/address/dereference constructs opaque. |
| 5 — Closures and deferred lambda bodies | Resolved conservatively: any lambda makes the callable opaque at `FactCollector.cs:210`, preventing parameter and guard facts at `164` and `418`. |
| 6 — Exactness short-circuited fact collection | Resolved: facts are collected before combining exactness at `FactCollector.cs:239–247`, `292–308`, and `375–407`. |
| 7 — Unreachable models after loops | Resolved for the reported loop-exit cases: loops divert subsequent exactness at `FactCollector.cs:273–274`, `347–348`. The throwing-predecessor residual remains documented at `109–110`. |
| 8 — Constructor/operator refinement facts | Resolved: both use parameter-aware collection at `ObligationSolver.cs:444` and `499`. |
| 9 — Changelog overstatement | Revised at `CHANGELOG.md:50–58`: body-wide killing and the throwing-predecessor residual are explicit. The exactness claim still has the gap described below. |

1. **BLOCKING — `§CDIR` is another unchecked raw-C# write channel.**

   ```calor
   §M{m1:M}
     §F{f1:Probe:priv} (i32:x) -> i32
       §E{}
       §Q (> x INT:0)
       §CDIR{compiler-directive:eCA9IC01Ow}
       §PROOF{p1:claim} (> x INT:0)
       §R x
   ```

   The payload decodes to `x = -5;`. `Parser.cs:11778–11789` decodes arbitrary C# without restricting it to directives, and `CSharpEmitter.cs:8894–8897` emits it verbatim.

   [FactCollector.cs:208](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:208) omits `CompilerDirectiveNode` from opacity detection. Its assignment scan also sees no write to `x`. Consequently, `ObligationSolver.cs:160–169` retains `x > 0`, discharges the identical proof, and `CSharpEmitter.cs:9928–9931` removes its guard.

   The binder’s generic unsupported-node handling supplies only an informational diagnostic (`Binder.cs:6698–6706`); it does not prevent emission or discharge. This is the same defect class as Round 1 finding 2.

2. **BLOCKING — A scalar `ref`/`in` parameter can alias heap storage written by the callable.**

   ```calor
   §M{m1:M}
     §CL{c1:Box:pub}
       §FLD{i32:Value:pub}
       §MT{mt1:Probe:pub}
         §I{i32:x:ref}
         §O{i32}
         §E{mut}
         §Q (> x INT:0)
         §ASSIGN this.Value INT:-5
         §PROOF{p1:claim} (> x INT:0)
         §R x
   ```

   A C# caller can set `box.Value = 1` and invoke `box.Probe(ref box.Value)`. The precondition holds; the field assignment then makes `x == -5`.

   [FactCollector.cs:145](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:145) expands aliases only among named by-reference parameters, and only when one appears assigned. Here, the field write records `this.Value`/`this`, never `x` (`479–481`, `544–549`). The predicate `x > 0` is syntactically scalar, so the heap-staleness rule at `79–88` cannot invalidate it.

   The solver therefore retains the precondition and falsely discharges the proof. The emitter produces the field store directly (`CSharpEmitter.cs:6832–6848`) and removes the proof guard.

   The same hole affects a new named-refinement fact for `Pos:x`, and scoped else facts. A call that changes the aliased field also escapes: marking `MutatesHeap` does not kill facts that syntactically read scalar `x`. Without storage alias analysis, potentially mutating operations must conservatively invalidate facts about by-reference parameters.

3. **MAJOR — Dropped parameter refinements are not recorded as inexact, permitting unreachable Failed models.**

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub}
       §I{i32:x} | (&& (> # INT:0) (> y INT:0))
       §I{i32:y}
       §O{void}
       §E{}
       §ASSIGN x INT:1
       §PROOF{p1:claim} (> y INT:0)
   ```

   Every execution entering the body has `y > 0`, enforced by the parameter guard (`CSharpEmitter.cs:6163–6167`). Assigning `x = 1` preserves the refinement, and the proof is correct.

   [FactCollector.cs:164](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:164) drops the entire refinement because `x` is assigned. It records no reason that the entry state is now incompletely modeled.

   `ObligationSolver.cs:160–196` records missing preconditions and untranslatable retained facts, but cannot see this discarded refinement. The proof statement remains exact, and its condition reads only unassigned `y`. Thus the SAT checks at `290–312` permit **Failed** with `y <= 0`, which the entry guard excludes. The assignment’s subtype obligation has the same problem.

   Track discarded parameter refinements as an exactness limitation, or preserve their independently valid conjuncts. This also contradicts `CHANGELOG.md:55–57`.

4. **MINOR — The claimed exhaustive mutation/binder inventory remains incomplete.**

   Concrete omissions include:

   - `ForeachStatementNode.IndexVariableName` (`ArrayNodes.cs:149`), which the emitter declares and increments (`CSharpEmitter.cs:5200–5204`). The collector handles only `VariableName` and its four-property reflection list (`FactCollector.cs:521–535`).
   - `MultiDimArrayAccessNode` store targets (`ArrayNodes.cs:236–245`), absent from `RootName` (`FactCollector.cs:544–549`).
   - Expression-target calls, event accessors, and implicit disposal, absent from the mutation classification (`FactCollector.cs:211–222`, `428–433`), despite executable emission at `CSharpEmitter.cs:8880–8884`, `7437–7448`, and `7025`.
   - Indexed constraints bypass killing and opacity: `AddFunctionWideFact` unconditionally inserts them (`FactCollector.cs:48–49`; `ObligationSolver.cs:375–380`). `MarkAssigned` does not filter those facts.

   These omissions are not all demonstrated default-policy false proofs; existing nullable-reference-model demotion protects some cases. They nevertheless invalidate the author’s exhaustive-inventory claim and the changelog’s “no facts at all” statement.

The suite contains **18 Facts + 15 theory rows = 33**, matching `12587 → 12620` in `eng/test-manifest.json:7`. I did not verify the claimed pre-fix failure count. The array-store control remains non-discriminating, and the five named-refinement rows still accept blanket Unsupported outcomes (`S2ObligationStateTests.cs:443–447`). Add regressions for the three concrete witnesses above.

The **Failed → Unsupported demotion preserves guards**: `ShouldEmitObligationGuard` retains non-Discharged outcomes (`CSharpEmitter.cs:578–584`), explicit proofs guard Unsupported (`9945–9950`), and the default policy reports a warning (`ObligationPolicy.cs:42`; `Program.cs:980–1005`). The blocking failures remain false **Discharged** outcomes.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING `§CDIR` raw payload | Fixed: `CompilerDirectiveNode` makes the body opaque. Regression: `IndirectWrite_KillsFacts` (CDIR row, the reviewer's payload). |
| 2 BLOCKING `ref`/`in` parameter aliasing heap storage | Fixed conservatively: when the body can change heap state (any call, object creation, collection update, event update, `using`, or store into an element, field, or dotted path; `IsHeapStore` also covers compound assignments and increments), every `ref`/`in`/`out` parameter counts as written. Their entry facts and guard facts are then killed. Regression: `IndirectWrite_KillsFacts` (field + `ref x` row, the reviewer's witness). |
| 3 MAJOR dropped refinements leave the state "exact" | Fixed: the names read by a dropped parameter refinement are recorded (`DroppedFactNames`). An obligation reading any of them is not refuted (Unsupported). The narrower rule keeps `ObligationTests.NestedSelfSubtypeObligation_IsTranslatedRatherThanUnsupported` valid, because there the dropped fact is about a different variable. Inline and named refinements of one parameter are now one entry fact. Regression: `DroppedParameterRefinement_MakesTheStateInexact`. |
| 4 MINOR inventory | Added `IndexVariableName` to the binder list, `MultiDimArrayAccessNode` to store roots, and `ExpressionCallNode`, event subscribe/unsubscribe, and `using` to heap mutation. Indexed-type constraint facts go through the same staleness check (`AddFunctionWideFact`). The CHANGELOG was not edited further, to stay inside the 600-line ceiling; its "no facts at all" sentence already lists `§RAW`/`§CS`, unsafe code, and lambdas, and `§CDIR` is now covered too. |

Capacity note: this PR is now at about 595 of 600 changed non-test lines. Any further change beyond this round would exceed the s2-repair-size ceiling.
