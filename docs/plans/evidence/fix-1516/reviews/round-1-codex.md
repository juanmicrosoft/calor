# fix-1516 — Codex review round 1

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), soundness
review prompt, with the `origin/main...HEAD` diff (src and tests) appended. Reviewed commit: af83fb53b.

A first attempt, with a more adversarially worded prompt, was stopped by the Codex content filter
before it produced any review. The prompt was reworded with the same scope and rerun.

## Review output (verbatim; absolute worktree paths shortened)

1. **Verdict: REQUEST CHANGES.** Outcomes below are derived by static inspection; no builds or tests were run.

2. **Blocking finding: emitted identifier collisions invalidate local equations and counterexamples.**

```calor
§M{m1:Test}
  §F{f1:Check:priv}
    §O{void}
    §B{a-b:i32} INT:1
    §B{~ab:i32} INT:2
    §PROOF{p1} (== `a-b` INT:1)
```

The solver reports **Discharged**: it resolves `a-b` to `1`, sees no rebinding of that exact name, and asserts `a-b == 1`.

The emitter removes the hyphen, then treats the mutable `ab` binding as an assignment to the existing variable. The effective C# is:

```csharp
int ab = 1;
ab = 2;
// proof check omitted
```

At the proof, the emitted reference to `a-b` reads `2`; the claimed equality is false. This satisfies **(a)**.

For **(b)**, replace the proof with:

```calor
    §PROOF{p1} (== `a-b` INT:2)
```

The solver reports **Failed**, with modeled `a-b = 1`. Every execution reaches that proof with emitted `ab = 2`, so the check actually passes; the counterexample is unreachable.

Responsible locations:

- `src/Calor.Compiler/Verification/Obligations/FactCollector.cs:395`: binding and mutation checks use original names, ignoring emitted identifier collisions.
- `src/Calor.Compiler/Verification/Obligations/FactCollector.cs:82`: resolved-local exemption permits the fabricated counterexample.
- `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:3836`: mutable bindings use sanitized names to detect rebinding.

The emitter collision predates this change, but both proofs were previously **Unsupported (Unknown variable)** with their guards retained. This change makes it worse. Refuse resolution when distinct names share an emitted identifier, or make emitted naming preserve variable identity.

3. **Non-blocking finding.**

The consistency fallback in `src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:273` rebuilds the solver without reasserting local equations. This loses precision, but weakens assumptions and records an inexactness reason, preventing an unreachable model from becoming **Failed**.

## Resolution

- **Blocking (emitted-name collision): fixed.** `ResolveProofLocals` now maps every name in the
  function (references, bindings, assigned names, parameters, solver-declared names) through the
  emitter's own `CSharpEmitter.SanitizeIdentifier` (made `internal`). A local, or a parameter its
  definition reads, that shares its C# identifier with another name is not resolved. The proof is
  then Unsupported with "shares its C# name with another variable". This also closes the Failed
  variant: an unresolved local keeps the `IsStaleBefore` stale check. Test:
  `LocalSharingItsCSharpName_IsRefused` (both claims, `INT:1` and `INT:2`).
- **Non-blocking (the consistency fallback drops the local equations): no change.** Dropping
  assumptions only weakens them, and the fallback records an inexactness reason, so it cannot
  turn the local equations into a Discharged or Failed verdict.
