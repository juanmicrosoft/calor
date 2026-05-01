# Graders — B5 Prerequisite

Feature-acceptance tests for T1.A / T1.B / T1.C. Applied **post-hoc** by the operator, after the model declares completion. Never present in the model's workspace during the run.

## How grading works

For each completed run at `bench/research-phase-0/runs/<arm>/<prompt-id>/run-<seq>/`:

1. Operator copies the relevant `graders/<prompt-id>/AcceptanceTests.cs` into the run's `tests/WholesaleOrders.Tests/` directory.
2. Operator runs `dotnet test --filter "FullyQualifiedName~Acceptance"` to produce the acceptance test pass/fail.
3. Operator records the result in `test-results.json` with the `acceptance_*` prefix.
4. The full test suite (existing + acceptance) runs again to measure `regression_rate`.

## Design constraints

The graders must:

1. **Not bind to the model's specific type names.** Probe via the API surface (`WebApplicationFactory<Program>`) with string-keyed JSON, or via reflection where API isn't expressive enough.
2. **Be deterministic** — no Thread.Sleep, no real wall-clock dependencies. Where time matters, the graders use a `TestClock` shim if the model wired one in; otherwise they call an explicit "process expired" entry point that the prompt says the implementation must expose.
3. **Be independently reviewable.** Every grader file gets a one-line "what this checks" header. A second reviewer verifies the file matches the corresponding prompt's "feature-acceptance tests" section in `t1-maintenance-prompts.md`.

## Per-prompt grader status

| Prompt | Grader file | Status | Coverage |
|--------|-------------|--------|----------|
| T1.A — order priority | `T1.A/AcceptanceTests.cs` | drafted | 4 acceptance + 3 adversarial |
| **T1.B — reservation expiry** | `T1.B/AcceptanceTests.cs` | **drafted (primary)** | 4 acceptance + 3 adversarial |
| T1.C — partial fulfillment | `T1.C/AcceptanceTests.cs` | drafted | 4 acceptance + 3 adversarial |

## Known limitations

These are the soft spots the operator must judge:

- **T1.B requires a controllable expiry trigger.** The prompt says "anything reasonable — the test suite checks behavior." If the model implements this *only* as a private timer that fires on wall clock with no testable hook (no public method, no DI-injectable clock, no sweep endpoint), grader probes will fail. The operator decides whether this counts as `correctness=0` (the model didn't make it testable) or whether to score by inspection. Pre-committed rule: **untestable implementation = correctness=0**, because the prompt explicitly required test-suite-checkable behavior.

- **The graders may try multiple endpoint conventions** for the expiry trigger (e.g., `/api/inventory/sweep-expired-reservations`, `/api/inventory/reservations/sweep`, etc.). If none are found, the grader looks for a `IReservationExpiry`-shaped service via DI. If neither exists, score = 0.

- **Test additions by the model are NOT counted toward correctness.** Per anti-gaming clause #5 in the rubric. Only the graders' tests count.

## Independent review

Graders pass independent review when **a reviewer who has not seen the scaffold authors a test for the same acceptance criterion** and the two tests would both pass on a correct implementation. For Phase 0, the reviewer can be the user; for confirmatory studies, a separate reviewer is required.

For the pilot, the user is the independent reviewer-of-record. Sign-off is implicit when the rubric+graders+pilot results are read together; explicit if the user countersigns this README.
