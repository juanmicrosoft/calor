# S2 #1413 fix-obligation-state — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), adversarial prompt, diff `origin/main...HEAD` appended to the prompt.

## Review output (verbatim)

VERDICT: REQUEST-CHANGES

Static review only; I did not run `dotnet`. The earlier concrete witnesses are repaired, but two additional mutation paths can still produce false Discharged outcomes and remove runtime guards.

| Earlier finding | Verification in current code |
|---|---|
| R1.1 — Parameter named `result` | Resolved: `ObligationSolver.cs:63–67` refuses the colliding return obligation. |
| R1.2 — Inline `§CS` writes | Original body witness resolved: `FactCollector.cs:209–211` makes raw expressions opaque. Expressions outside the body remain unsafe, as described below. |
| R1.3 — Scoped aliases, `in`, constructors | Original witnesses resolved: alias grouping includes `in` and populates `_aliasWritten` at `FactCollector.cs:152–160`; scoped guards consult it at `441`. Constructors use parameter-aware collection at `ObligationSolver.cs:445`. |
| R1.4 — Pointer stores | Resolved conservatively: unsafe/fixed/address/dereference constructs make the body opaque at `FactCollector.cs:209–211`. |
| R1.5 — Closures and deferred bodies | Resolved conservatively: lambdas make the callable opaque at `FactCollector.cs:211`; entry and scoped facts are refused at `86–89` and `434–435`. |
| R1.6 — Exactness blocked valid fact collection | Resolved: facts are collected before combining exactness at `FactCollector.cs:242–250` and `295–311`. |
| R1.7 — Models that never exit preceding loops | Original witnesses resolved: loops divert subsequent exactness at `FactCollector.cs:352–364`. Throwing predecessors remain an explicit residual at `116–117`. |
| R1.8 — Constructor/operator parameter facts | Resolved: both use parameter-aware collection at `ObligationSolver.cs:445` and `500`. |
| R1.9 — Changelog overstatement | Body-wide killing and the throwing residual are explicit at `CHANGELOG.md:50–58`. The assertion that Failed requires matching program state remains too strong; finding 3 supplies another counterexample. |
| R2.1 — `§CDIR` payload | Resolved for the reported witness: `CompilerDirectiveNode` makes the body opaque at `FactCollector.cs:209`. |
| R2.2 — By-reference parameter aliases a written field | Original explicit-store/call witnesses resolved: `MutatesHeap` expands writes to every by-reference parameter at `FactCollector.cs:156–160`. Implicit getter calls still escape, as described below. |
| R2.3 — Dropped parameter refinements | Original witness resolved: dropped names are recorded at `FactCollector.cs:179–181` and checked at `ObligationSolver.cs:296–298`. The direct-name test misses dependencies through retained assumptions. |
| R2.4 — Mutation/binder inventory | Requested additions exist at `FactCollector.cs:215–218`, `549`, `564`, and `48–51`. The scoped heap-mutation inventory remains inconsistent with the updated whole-body inventory. |

1. **BLOCKING — Entry refinement expressions execute outside the mutation/opacity scan and can invalidate earlier entry facts.**

   ```calor
   §M{m1:M}
     §RTYPE{r1:Pos:i32} (> # INT:0)
     §F{f1:Probe:pub}
       §I{Pos:x}
       §I{i32:y} | §CS{((x = -5) == -5)}
       §O{i32}
       §E{mut}
       §PROOF{p1:claim} (> x INT:0)
       §R x
   ```

   `Probe(1, 0)` passes the first parameter guard. The second guard assigns `x = -5` and evaluates to true. The proof condition is then false.

   [FactCollector.cs:148](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:148) initializes the scan from **body statements only**; `198–211` consequently never sees this raw expression. The collector retains the named-refinement fact `x > 0` at `175–183`.

   This expression is accepted by the parameter parser (`Parser.cs:2207–2215`, `11808–11812`). The emitter executes parameter guards sequentially (`CSharpEmitter.cs:6144–6167`) and emits the raw assignment verbatim (`8900–8903`).

   The solver asserts `x > 0` at `ObligationSolver.cs:189–192`. The raw refinement cannot be translated, but that only adds an inexactness reason (`194–196`); those reasons prevent **SAT refutations only** (`300–309`). The contradictory proof query is UNSAT, so the proof is Discharged and its guard disappears at `CSharpEmitter.cs:9928–9931`.

   Entry predicates and precondition evaluation need to participate in mutation/opacity analysis. Scanning only the body does not establish that all entry facts hold simultaneously when body execution begins.

2. **BLOCKING — Property getter calls still permit the by-reference/heap-alias false proof.**

   ```calor
   §M{m1:M}
     §CL{c1:Box:pub}
       §FLD{i32:Value:pub}
       §PROP{pr1:Trigger:i32:pub}
         §GET
           §ASSIGN this.Value INT:-5
           §R INT:0
       §MT{mt1:Probe:pub}
         §I{i32:x:ref}
         §O{i32}
         §E{mut}
         §Q (> x INT:0)
         §B{sink:i32} this.Trigger
         §PROOF{p1:claim} (> x INT:0)
         §R x
   ```

   A C# caller sets `box.Value = 1` and invokes `box.Probe(ref box.Value)`. Reading `Trigger` executes the getter and makes `x == -5`.

   [FactCollector.cs:212](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:212) classifies explicit calls and stores as heap mutations, but excludes property reads. The getter body belongs to a separate member and is absent from this callable’s scan. Thus `MutatesHeap` remains false, and the alias expansion at `156–160` does not invalidate `x`.

   The dotted reference is emitted as an actual property access (`CSharpEmitter.cs:3557–3597`); getter bodies execute normally (`6588–6636`). Nevertheless, the scalar precondition survives `ObligationSolver.cs:160–169`, discharges the identical proof, and removes its guard.

   This also affects named-refinement entry facts. Potential getter/indexer calls—and implicit enumeration calls—need conservative mutation handling unless their effects are established.

3. **MAJOR — The new dropped-refinement rule misses dependencies through retained assumptions and still produces unreachable Failed models.**

   ```calor
   §M{m1:M}
     §F{f1:Probe:pub}
       §I{i32:x} | (&& (> # INT:0) (> y INT:0))
       §I{i32:y}
       §I{i32:z}
       §O{void}
       §E{}
       §Q (== y z)
       §ASSIGN x INT:1
       §PROOF{p1:claim} (> z INT:0)
   ```

   Every execution reaching the body has `y > 0` from the entry refinement and `y == z` from the precondition. Assigning `x = 1` preserves the refinement. Therefore the proof holds.

   [FactCollector.cs:179](/private/tmp/claude-501/-Users-juanrivera-repos-juanmicrosoft-calor/98372634-e96e-40c2-8b28-3e5d5ca7761f/scratchpad/rc-fix-obligation-state/src/Calor.Compiler/Verification/Obligations/FactCollector.cs:179) drops the refinement and records only `{x, y}`. The solver retains `y == z` (`ObligationSolver.cs:160–169`), but the new overlap check examines only the obligation’s direct references—`{z}`—at `296–298`.

   The proof statement remains exact, and `z` is unassigned. Consequently, a SAT model such as `y = z = 0` becomes **Failed** at `312–313`, although the entry guard excludes that model. Default policy turns Failed into a compile error (`ObligationPolicy.cs:33`).

   Direct name overlap is insufficient to establish reachability. Dropped refinements must affect exactness through dependencies in retained assumptions, or the candidate counterexample must be checked against the omitted entry constraints.

4. **MINOR — The scoped mutation inventory still omits the operations added in Round 2.**

   Whole-body classification now includes `ExpressionCallNode`, event updates, and `UsingStatementNode` (`FactCollector.cs:215–218`), and uses `IsHeapStore` for dotted stores and increments (`212`, `323–332`).

   `AddGuardFact` still uses a separate older list at `FactCollector.cs:444–449`. It omits those operations, increments, and dotted-reference stores. Also, `ReadsHeap` omits `MultiDimArrayAccessNode` (`92–95`), despite its addition to store-root handling at `564`.

   These are remaining inventory gaps; I am not claiming an additional default-policy false-discharge witness from them. The nullable-reference-model demotion protects some such cases. Reusing one mutation classification would prevent the entry and scoped rules from continuing to diverge.

The suite contains **19 Facts + 17 theory rows = 36**, matching `12587 → 12623` in `eng/test-manifest.json:8`. I did not execution-verify the pre-fix failure count. The array-store case remains a non-discriminating control (`S2ObligationStateTests.cs:107–121`), and the five named-refinement rows still accept blanket Unsupported results (`489–491`). Add regressions for the three concrete witnesses above.

**Failed → Unsupported still preserves guards and remains visible:** `ShouldEmitObligationGuard` retains non-Discharged outcomes (`CSharpEmitter.cs:578–584`), explicit proofs guard Unsupported (`9945–9950`), and default policy reports a warning (`ObligationPolicy.cs:42`; `Program.cs:980–1005`). The blocking defects above instead produce false **Discharged** outcomes.

## Response

| Finding | Disposition |
|---|---|
| 1 BLOCKING raw C# in an entry predicate is not scanned | Fixed. The collector records `HasOpaqueEntry` when a parameter refinement (inline or named) contains opaque code: raw C#, unsafe/pointer code, or a lambda. The solver returns `Unsupported` for every obligation of a callable whose parameter refinement or precondition is opaque, before it asserts any fact. Regression: `RawCSharpInAParameterRefinement_KillsEntryFacts` (the reviewer's `§CS{((x = -5) == -5)}` witness). |
| 2 BLOCKING property getter calls permit the by-reference alias false proof | Fixed. A member or element read (`FieldAccessNode`, a dotted reference, element access) and `foreach` now count as possible heap mutations, because a getter, indexer, or enumerator may run. The exception is a read inside a `§PROOF` condition, which only evaluates the check. This feeds `MutatesHeap`, and so the alias expansion and entry-fact staleness. Regression: `PropertyGetterRead_KillsAliasedRefParameterFact` (the reviewer's `this.Trigger` witness). |
| 3 MAJOR dropped-refinement dependencies through retained assumptions | Fixed. A SAT result is inexact (`Unsupported`, no compile error) when a dropped entry refinement shares a name with the obligation or with any asserted precondition or applicable fact. The previous rule checked only the obligation's direct references. Regression: `DroppedRefinement_ReachedThroughARetainedPrecondition_IsNotFailed` (the reviewer's `y == z` witness). The existing control `NestedSelfSubtypeObligation_IsTranslatedRatherThanUnsupported` still gets a determinate verdict. |
| 4 MINOR two mutation inventories | Fixed. `AddGuardFact` and the whole-body classification now share one predicate (`MayChangeHeap`: `IsHeapMutation` plus heap reads outside proofs). `ReadsHeap` includes `MultiDimArrayAccessNode`. |
| Test notes | All 3 new regressions failed before this round and pass after it. `S2ObligationStateTests` now has 39 cases. Calor.Compiler.Tests 12626 and Calor.Verification.Tests 411 pass, including the G3 differential oracle, whose obligation rows read array elements and fields only inside `§PROOF`. |
| Capacity | The PR is at 575 of 600 non-test lines. Doc comments were condensed to make room. |
