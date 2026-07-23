# Verification-outcome fixture corpus (loop plan D1.5)

Committed `.calr` fixtures known to produce each proof status of the closed
envelope vocabulary (`docs/cli/envelope-schema.md`), so the M-E2
(counterexample attach rate) and M-E3 (cliff visibility) metrics have a defined
CI corpus instead of "whatever the build happens to verify". Exercised by
`tests/Calor.Verification.Tests/OutcomeCorpusTests.cs`.

| Fixture | Expected status | Diagnostic | Mechanism |
|:--------|:----------------|:-----------|:----------|
| `proven.calr` | `proven` | `Calor0713` (verbose) | `x > 0 ⇒ x ≥ 1` over i32 — postcondition implied by the precondition |
| `refuted-with-model.calr` | `refuted` + concrete model | `Calor0712` | `result` unconstrained by the (unencoded) body, so `¬(result > 10)` is satisfiable; Z3 model attached as structured bindings |
| `unsupported.calr` | `unsupported` | `Calor0718` | `f64` contracts cannot map to bit-vector theory (`ContractTranslator.DiagnoseUnsupportedType`) |
| `timeout.calr` | `timeout` | `Calor0717` | quartic bit-vector equation, verified with a 1 ms solver budget by the test |

## The `unknown` status is not source-fixturable — stated limit

`unknown` (inconclusive, not a timeout) cannot be deterministically produced
from a committed `.calr` file: on quantifier-free bit-vector/nonlinear
problems Z3 keeps searching until the time budget rather than answering
UNKNOWN, and the other `unknown` producers (solver error, solver unavailable)
are environmental, not expressible in source. The corpus therefore covers
`unknown` at **evidence level**: `OutcomeCorpusTests` drives
`ProofOutcome.Assign` directly with solver-unavailable/solver-error evidence
and asserts the status and its envelope wire name. If a reliably-UNKNOWN
source form is ever found, add it here and drop this note.

## Governance

- Fixture edits must keep the expected-status table true; the corpus test is
  the enforcement.
- `timeout.calr` depends on a 1 ms budget staying far below the cost of
  bit-blasting a quartic multiply chain — do not simplify its contract.
- These fixtures are deliberately minimal; they measure the reporting
  pipeline, not solver power.
