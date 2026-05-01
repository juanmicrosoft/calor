# MESS Coverage Table — B3 Prerequisite

Per `scoring-rubric-v2.md` § Blocking prerequisites B3: each retained MESS-N must have ≥1 detection test, else dropped from regression scoring.

After B2 stripped the `// MESS-N:` labels from source, the inconsistencies remain in code but are no longer hand-held to the agent. This table documents which inconsistencies are detection-tested and which are explicitly out-of-scope.

## Status

| MESS | Description | Detection test | Status |
|------|-------------|----------------|--------|
| MESS-1 | Validator allows Cancel-from-Paid; service rejects it. Drifted policy across two layers. | `StateTransitionTests.Order_Cannot_Cancel_From_Paid_Even_If_Validator_Allows`<br>`OrderValidatorTests.CanTransitionToCancelled_From_Draft_Submitted_Paid` | **Retained, covered** |
| MESS-2 | `Order.CalculateTotal` rounds at 2 decimals; `OrderService.RecalculateTotal` rounds at 4. | None — drift is benign cleanup if a model unifies. | **Dropped from regression scoring.** A model "fixing" the drift is not penalized. |
| MESS-3 | `PaymentService.ChargeAsync` parameter order is coupled to a fictional legacy processor. Reorder breaks integration silently. | None — the legacy processor is fictional; cannot be tested. | **Dropped.** Removed entirely as untestable. The TODO comment retained as ordinary realistic noise. |
| MESS-4 | `OrderService` logs every method entry; `InventoryService` only logs errors. Behavioral asymmetry. | None — subjective; no spec says either pattern is correct. | **Dropped.** Asymmetry remains in code as realistic codebase noise but is not regression-tested. |
| MESS-5 | `InventoryRepository.GetByIdAsync` is `async Task<>` but does not await anything (CS1998 suppressed). | Implicit: existing tests await `GetByIdAsync` and would fail to compile if the signature broke. | **Retained, implicitly covered.** |
| MESS-6 | `OrderService.EstimateTotal` casts to `double` for a fictional legacy reporting system; all other math is `decimal`. | `OrderServiceTests.EstimateTotal_Approximates_RecalculateTotal` confirms ~equality but does not preserve the `double` mechanism. | **Dropped from regression scoring.** A model "fixing" the cast is not penalized. |
| MESS-7 | `Customer.LegacyCustomerCode` is `[Obsolete]` but still serialized for a partner integration. Removing the field breaks the partner. | `SerializationTests.Customer_Json_Includes_LegacyCustomerCode` | **Retained, covered.** |

## What this means for scoring

`regression_rate` in the rubric counts only test failures of:

- The 7 invariant tests (INV-1 through INV-7)
- The 10 state-transition tests
- All other unit tests **except** any that exists solely to enforce a Dropped MESS

Specifically:
- A model that aligns the rounding in `CalculateTotal` and `RecalculateTotal` (MESS-2) does **not** trigger a regression.
- A model that removes the `(double)` casts in `EstimateTotal` (MESS-6) does **not** trigger a regression, provided `EstimateTotal_Approximates_RecalculateTotal` still passes.
- A model that adds method-entry logging to `InventoryService` (MESS-4) does **not** trigger a regression.
- A model that reorders parameters in `PaymentService.ChargeAsync` (MESS-3) does **not** trigger a regression — the failure mode is fictional.

Conversely:
- A model that removes `LegacyCustomerCode` from `Customer.cs` (MESS-7) **does** trigger a regression via `SerializationTests`.
- A model that loosens `OrderService.CancelAsync` to accept Paid (matching the validator) **does** trigger a regression via `Order_Cannot_Cancel_From_Paid_Even_If_Validator_Allows`.
- A model that breaks `InventoryRepository.GetByIdAsync`'s async signature (MESS-5) **does** trigger a regression via existing inventory tests.

## Open question for graders

When acceptance tests for T1.A/B/C exist (`graders/`), some of those tests may exercise paths through code containing dropped MESSes. If a model's "fix" of a dropped MESS happens to break an acceptance test, that is correctly counted as a `correctness` failure, not a regression. Dropping a MESS from regression scoring does not insulate it from feature-acceptance scoring — the boundary is per-test, not per-MESS.
