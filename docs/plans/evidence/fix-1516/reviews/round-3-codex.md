# fix-1516 — Codex review round 3

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), the
soundness prompt with a round-3 preamble (both earlier fixes, and a request to look for any hidden
code between a binding and the proof), and the `origin/main...HEAD` diff (src and tests) appended.
Reviewed commit: 490f9286d.

## Review output (verbatim; absolute worktree paths shortened)

1. **Verdict: REQUEST CHANGES.**

The sanitized-name collision fix covers both locals and parameters their definitions read. The refinement fix covers `(inc #)` and the listed explicit mutations, but misses hidden effects.

2. **Blocking findings**

**A. A refinement getter can change a parameter after the local captures it.**

```calor
§M{m1:Test}
  §RTYPE{r1:Guard:i32} (== Heap.Next INT:1)
  §CL{c1:Heap:pub}
    §FLD{i32:State:pub:stat} INT:1
    §PROP{pr1:Next:i32:pub:stat}
      §GET
        §ASSIGN State INT:2
        §R INT:1
  §F{f1:Check:pub}
    §I{i32:x:ref}
    §O{void}
    §B{saved:Guard} x
    §PROOF{p1} (== saved x)
  §F{f2:Run:pub}
    §O{void}
    §C{Check} §A{ref} Heap.State §/C
```

- **Solver:** `Discharged`, from `saved == x`.
- **Runtime:** `saved` becomes `1`; its inserted refinement guard invokes `Heap.Next`, changing the aliased `x` to `2`. The proof is false, but its check disappears.
- **Cause:** `src/Calor.Compiler/Verification/Obligations/FactCollector.cs:163` checks explicit mutations but excludes getter/indexer reads. These predicates also do not contribute to the body’s heap-mutation scan. The guard executes after binding at `src/Calor.Compiler/CodeGen/CSharpEmitter.cs:1372`.

**B. Passing a local as `in` exposes its storage to an opaque callee.**

```calor
§M{m1:Test}
  §F{f1:Mutate:priv}
    §I{i32:value:in}
    §O{void}
    §B{sink:i32} §CS{(System.Runtime.CompilerServices.Unsafe.AsRef(in value) = 2)}
  §F{f2:Check:pub}
    §O{void}
    §B{k:i32} INT:1
    §C{Mutate} §A{in} k §/C
    §PROOF{p1} (== k INT:1)
```

- **Solver:** `Discharged`, from `k == 1`.
- **Runtime:** `Mutate` writes `2` into `k` through its readonly reference. The false proof’s check disappears.
- **Cause:** `src/Calor.Compiler/Verification/Obligations/FactCollector.cs:833` only invalidates `ref`/`out` arguments. The callee’s raw C# does not make the caller opaque.

Both witnesses previously remained unsupported because their proofs referenced undeclared solver locals.

3. **Non-blocking findings**

The consistency fallback rebuilds the solver without `localEquations` at `src/Calor.Compiler/Verification/Obligations/ObligationSolver.cs:273`. This can lose proofs; its inexactness flag prevents fabricated `Failed` results.

No additional **(b)** blocker found. Outcomes above are derived by static tracing; no builds or tests were run.

## Resolution

- **A (getter in a refinement guard): fixed by tightening the round-2 rule.** A refinement predicate
  now counts as state-changing unless every node in it is plain: literals, operators, conditionals,
  `#`, and undotted names. Any member read, property, call, increment, or opaque code refuses local
  resolution in that module's functions. Test: `LocalWithGetterReadingRefinementGuard_IsRefused`
  (the reviewer's program, without the caller).
- **B (`in` argument): fixed.** Any argument passed with a modifier (`in`, `ref`, `out`) on any node
  that carries argument modifiers marks its root variable as rebound. That variable is then
  refused both as a local and as a parameter a local's definition reads. Test:
  `LocalPassedAsInArgument_IsRefused`.
- **Added while fixing A (same class of hidden code):** resolution is refused in a module that
  declares operator overloads or raw C# type members (`OperatorsMayBeOverloaded`). In such a module,
  an operator in a definition or refinement predicate may run user code. Test:
  `LocalInModuleWithOperatorOverloads_IsRefused`.
- **Non-blocking (consistency fallback): no change**, as in round 1.
