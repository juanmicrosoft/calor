# Verification-outcome fixture corpus (loop plan D1.5)

Committed `.calr` fixtures known to produce each proof status of the closed
envelope vocabulary (`docs/cli/envelope-schema.md`), so the M-E2
(counterexample attach rate) and M-E3 (cliff visibility) metrics have a defined
CI corpus instead of "whatever the build happens to verify". Exercised by
`tests/Calor.Verification.Tests/OutcomeCorpusTests.cs`.

| Fixture | Expected status | Diagnostic | Mechanism |
|:--------|:----------------|:-----------|:----------|
| `proven.calr` | `proven` | `Calor0713` (verbose) | `x > 0 ⇒ x ≥ 1` over i32 — postcondition implied by the precondition |
| `refuted-with-model.calr` | `refuted` + concrete model | `Calor0712` | genuinely refutable with `result` bound to the body (`result = x-1`, refuted at `x=1`); Z3 model attached as structured bindings. (Before guarantees plan D-G1.1 this fixture "worked" for the wrong reason — `result` was unconstrained, #807) |
| `proven-with-result.calr` | `proven` ×4 | `Calor0713` (verbose) | result-referencing postconditions over encodable bodies: single-`§R` (`Identity`), `§IF`/`§EL` (`Max`), `§EI` chain (`Clamp`) — the #807 spurious-refutation class, pinned as proving |
| `refuted-overflow.calr` | `refuted` ×2 + concrete models | `Calor0712` | honest overflow refutations under two's-complement semantics: `x*x` wraps negative, `a+b` wraps below `a` — the class #807 explicitly excludes as correct behavior |
| `unsupported-body.calr` | `unsupported` | `Calor0718` | result-referencing postcondition over a body outside the encodable surface (binding + loop): reported `unsupported`, NEVER refuted against a free `result` (the #807 regression pin) |
| `proven-with-binding.calr` | `proven` ×3 | `Calor0713` (verbose) | the three W5-B probe contract shapes (guard-clause `§B` chain + branch, D-G3.1): result-referencing postconditions over immutable-binding bodies prove via SSA substitution — the depth surface PP-G3 depends on |
| `refuted-with-binding.calr` | `refuted` + concrete model | `Calor0712` | the DEFECTIVE W5-B shape (guard threshold loosened `cap` → `cap+10`): refutes with a genuine model through the §B chain — the CI fact behind A-1.3's feasibility-by-determinism claim for PP-G3's surfacing half (#825 review m1) |
| `assumed-division.calr` | `assumed` + non-empty `assumptions` | `Calor0720` info | division-carrying body: the proof (`a>=0 ⊨ a/b >= -a` for nonzero `b`) is conditional on §S normal-return semantics — Assumed's first producer (guarantees plan D-G2.5); never elides the runtime check |
| `vacuous-precondition.calr` | `proven` (vacuous) | `Calor0719` warning | jointly-unsatisfiable `§Q` set (`x>10 ∧ x<5`): postcondition is Proven with the vacuous flag, the runtime check is kept, and the vacuity is loud (guarantees plan D-G1.3) |
| `unsupported.calr` | `unsupported` | `Calor0718` | `f64` contracts cannot map to bit-vector theory (`ContractTranslator.DiagnoseUnsupportedType`) |
| `timeout.calr` | `timeout` | `Calor0717` | quartic bit-vector equation, verified with a 1 ms solver budget by the test |

## The `unknown` status is not source-fixturable — stated limit

`unknown` (inconclusive, not a timeout) cannot be deterministically produced
from a committed `.calr` file: on quantifier-free bit-vector/nonlinear
problems Z3 keeps searching until the time budget rather than answering
UNKNOWN, and the remaining `unknown` producer (solver error) is
environmental, not expressible in source. The same holds for `unavailable`
(no solver present — split from `unknown` at schema 2.0, guarantees plan
D-G2.2). The corpus therefore covers both at **evidence level**:
`OutcomeCorpusTests` drives `ProofOutcome.Assign` directly with
solver-unavailable/solver-error evidence and asserts the status and its
envelope wire name. If a reliably-UNKNOWN
source form is ever found, add it here and drop this note.

## Governance

- Fixture edits must keep the expected-status table true; the corpus test is
  the enforcement.
- `timeout.calr` depends on a 1 ms budget staying far below the cost of
  bit-blasting a quartic multiply chain — do not simplify its contract.
- These fixtures are deliberately minimal; they measure the reporting
  pipeline, not solver power.
