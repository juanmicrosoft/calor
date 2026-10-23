# fix-1516 — Codex review round 2

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), the round-1
soundness prompt with a round-2 preamble (the round-1 fix, and a request to cover new paths), and the
`origin/main...HEAD` diff (src and tests) appended. Reviewed commit: 9469dc122.

## Review output (verbatim; absolute worktree paths shortened)

1. **Verdict: REQUEST CHANGES.** Static review only; no builds or tests run.

2. **Blocking finding: a local’s refinement guard can change its value after initialization — (a).**

```calor
§M{m1:Test}
  §RTYPE{r1:Bump:i32} (== (inc #) INT:2)
  §F{f1:Check:pub}
    §O{void}
    §E{mut}
    §B{k:Bump} INT:1
    §PROOF{p1} (== k INT:1)
```

The solver reports **Discharged** for `p1`: it erases `Bump` to `i32` and asserts `k == 1`. The separate subtype obligation cannot translate `inc`, so its runtime guard remains.

The effective C# is:

```csharp
int k = 1;
if (!(checked(++k) == 2))
    throw new ArgumentOutOfRangeException(...);
// p1 check omitted
```

The refinement guard passes and leaves `k == 2`. Every execution reaches a false proof whose check has been removed. On `origin/main`, this proof was **Unsupported (Unknown variable)**.

Responsible locations:

- `src/Calor.Compiler/Verification/Obligations/FactCollector.cs:450`: resolution checks the initializer and body mutations, overlooking the named refinement predicate.
- `src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:153`: refinement erasure permits the local equation.
- `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:1372`: the guard executes after initialization.

Resolution must account for executed refinement predicates, including mutations and opaque code that could affect other locals or parameter dependencies.

3. **Non-blocking findings.**

The round-1 identifier-collision fix is confirmed, including its parameter-dependency check. No additional non-blocking findings or new concrete (b) witness established.

## Resolution

- **Blocking (state-changing refinement guard): fixed.** `CollectFromCallable` now records whether
  any refinement predicate the function can run changes state: every named refinement type in the
  module and every inline parameter refinement. It checks for raw C#, lambdas, and other opaque
  code, increments, calls, `new`, and collection or heap writes. When one does, no local in that
  function is resolved, and the proof is Unsupported with "refinement guards may change state".
  The check is module-wide (it does not ask which types the function uses), so it covers guards
  run for other locals, for assignments, and on entry. Tests:
  `LocalWithStateChangingRefinementGuard_IsRefused` (the reviewer's program) and the control
  `LocalWithPureRefinementType_Discharges`.
