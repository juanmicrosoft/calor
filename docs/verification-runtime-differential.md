# Verifier ↔ generated-runtime differential gate

Issue #779's residual soundness gate is implemented by the
`VerifierRuntimeDifferentialTests` xUnit fixture under `tests/Calor.Verification.Tests`. It
deterministically compares typed solver outcomes with execution of the C# emitted from the same
AST while runtime guards are forced on.

## Frozen denominator

The tool verifies the F-4 SHA-256 pin for `docs/verification-modeled-forms.md` before generating
anything. It derives 65 named forms from the machine whitelist:

| Category | Forms |
|---|---:|
| Scalar types | 10 |
| Integer array element types | 8 |
| Expression kinds | 15 |
| Binary operators | 18 |
| Unary operators | 2 |
| String operations | 10 |
| Ordinal comparison rule | 1 |
| Declarable quantifier-type policy representative | 1 |

Every form is generated in all three positions (`§Q`, `§S`, explicit `§PROOF`), at identity-wrapper
depths 1, 2, and 3, and with both a true/provable and false/refutable polarity: **65 × 3 × 3 × 2 =
1,170 cases**. The identity wrappers preserve the base predicate while exercising nested lowering.
Every row asserts that its registered form actually occurs in the base AST.

`SelfRefNode` needs a refinement binding. For that row only, the harness binds `#` to an
`i32 __self__` parameter before solver translation; the generated C# uses the emitter's existing
`__self__` lowering and receives the same runtime argument. This tests the modeled refinement
meaning without presenting unbound `#` as ordinary source-valid `§Q`/`§S`/`§PROOF`.

`FieldAccessNode` runs through the production module-derived type registry. The contract pass and
obligation solver derive `Probe.Value: u8` from the generated class declaration and translate it as
an 8-bit unsigned uninterpreted accessor. The generated predicate checks the `255` upper bound and
executes with the runtime witness at that boundary. The registered `u8` sort makes the bound
provable (or explicitly assumed under the nullable-reference model); the historical guessed-`i32`
fallback leaves a counterexample and therefore fails the gate rather than accidentally matching.

## Oracle

The module is verified once through the contract pass and obligation solver, then emitted twice:

1. **Guard-forced:** `ElideProvenGuards = false`; Roslyn compiles this C# in memory and every
   generated method is invoked with a deterministic typed witness.
2. **Elision-enabled:** `ElideProvenGuards = true`; method bodies are inspected to measure the
   actual postcondition and `ObligationStatus.Discharged` elision routes. Preconditions must
   always retain their guards.

Provable cells must be `Proven` and refutable cells must be `Refuted`. A provable cell may instead
be `Assumed` only when the form registers the exact production assumption set; the report publishes
every such allowance. Unsupported, timeout, unknown, or unavailable matrix outcomes are not
coverage. Runtime execution independently requires provable cases to complete and refutable cases
to fire the generated guard.

Fail-safe controls use the same deterministic status/reason/exception classifier as production
`ProofOutcome.Assign`: timeout and solver-error controls no longer rehydrate injected statuses.
Unsupported, unavailable, and assumed controls use their production evidence constructors. Every
non-decisive status must retain and fire its false guard at both emitter choke points. Vacuous
proofs, missing guards, unexpected runtime exceptions, stale reports, and whitelist drift fail.

Bounded `forall` and `exists` rows execute the emitter's LINQ lowering, including D15's full
implication predicate. Explicit proof rows execute the separate obligation solver and emitter path.

## Metrics and CI

Machine-readable and human-readable reports are committed at:

- `bench/phase0-agent-native/verifier-runtime-differential.json`
- `bench/phase0-agent-native/verifier-runtime-differential.md`

The report publishes solver-handled forms and cells, forms eliding, typed outcome counts, explicit
`Assumed` allowances, fail-safe controls, and mismatches. The current result is **65/65 forms
solver-handled, 1,170/1,170 cells solver-handled, 40/65 forms eliding (61.54%), and zero
mismatches**. Outcomes are 435 `Proven`, 585 `Refuted`, and 150 explicitly allowed `Assumed`.

CI runs:

```bash
dotnet test tests/Calor.Verification.Tests/Calor.Verification.Tests.csproj \
  -c Release --no-build \
  --filter "FullyQualifiedName~VerifierRuntimeDifferentialTests.CommittedReportsMatchGeneratedOracle"
```

The test reruns the full oracle and byte-compares both committed reports. The step is blocking and
uploads the reports as workflow artifacts; unavailable Z3 is a failure rather than a skip. To
regenerate metrics intentionally, set
`CALOR_UPDATE_VERIFIER_RUNTIME_DIFFERENTIAL_REPORTS=1` while running the same filtered test.
Repository discovery accepts both a `.git` directory and a worktree `.git` file. The report paths
are pinned to LF in `.gitattributes`, and generated assemblies run in collectible load contexts
that are disposed and unloaded after each main or fail-safe execution.
