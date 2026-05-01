# Scaffold Spec — Wholesale Order Processing Service

A synthetic .NET 10 codebase representing a realistic SaaS service: a wholesale order processor that takes B2B orders, reserves inventory, processes payments, and ships.

## Why this domain

- **Real invariants** spanning multiple files (Order.Total = Σ(LineItem.Qty × UnitPrice); Inventory.Available = OnHand − Reserved)
- **State machines** that are easy to break with naive edits (Order: Draft → Submitted → Paid → Shipped → Delivered, with conditional Cancel transitions; Reservation: Created → Confirmed → (Released | Fulfilled))
- **Cross-file coordination** — adding a field exercises 5–7 files
- **Familiar patterns** — controller/service/repo/entity, async, validation chains — so model success is not gated on exotic API knowledge
- **Bounded** — 35–45 files, achievable in 2–3 days of authoring

## Repository layout

```
WholesaleOrders/
├── src/
│   ├── WholesaleOrders.Api/                 (~10 files)
│   │   ├── Controllers/
│   │   │   ├── OrdersController.cs
│   │   │   ├── InventoryController.cs
│   │   │   ├── PaymentsController.cs
│   │   │   └── ShipmentsController.cs
│   │   ├── Middleware/
│   │   │   ├── IdempotencyMiddleware.cs
│   │   │   └── ErrorHandlingMiddleware.cs
│   │   ├── Program.cs
│   │   └── DependencyRegistration.cs
│   ├── WholesaleOrders.Domain/              (~12 files)
│   │   ├── Entities/
│   │   │   ├── Order.cs
│   │   │   ├── OrderLineItem.cs
│   │   │   ├── Customer.cs
│   │   │   ├── InventoryItem.cs
│   │   │   ├── StockReservation.cs
│   │   │   ├── Payment.cs
│   │   │   └── Shipment.cs
│   │   ├── ValueObjects/
│   │   │   ├── Money.cs
│   │   │   └── Sku.cs
│   │   └── Enums/
│   │       ├── OrderStatus.cs
│   │       └── ReservationStatus.cs
│   ├── WholesaleOrders.Services/            (~10 files)
│   │   ├── OrderService.cs
│   │   ├── InventoryService.cs
│   │   ├── PaymentService.cs
│   │   ├── ShipmentService.cs
│   │   ├── NotificationService.cs
│   │   └── Validators/
│   │       ├── OrderValidator.cs
│   │       ├── InventoryValidator.cs
│   │       └── PaymentValidator.cs
│   └── WholesaleOrders.Infra/               (~6 files)
│       ├── Persistence/
│       │   ├── AppDbContext.cs
│       │   ├── OrderRepository.cs
│       │   ├── InventoryRepository.cs
│       │   └── CustomerRepository.cs
│       └── Logging/
│           └── StructuredLogger.cs
└── tests/
    └── WholesaleOrders.Tests/               (~8 files)
        ├── OrderServiceTests.cs
        ├── InventoryServiceTests.cs
        ├── PaymentServiceTests.cs
        ├── OrderValidatorTests.cs
        ├── InvariantTests.cs            (CRITICAL — checks invariants below)
        ├── StateTransitionTests.cs      (CRITICAL — checks state machines)
        ├── IdempotencyTests.cs
        └── IntegrationTests.cs
```

**Total: ~46 files.** Slight overshoot of 30–50 target.

## Invariants (load-bearing for scoring)

The following invariants are encoded as automated tests in `InvariantTests.cs`. Maintenance edits that break any of these count as a regression.

| ID | Invariant | Test |
|----|-----------|------|
| INV-1 | `Order.TotalAmount == Σ(LineItem.Quantity × LineItem.UnitPrice)` for all non-Cancelled orders | `Order_Total_Equals_LineItem_Sum` |
| INV-2 | `InventoryItem.Available == OnHand - Reserved`, both ≥ 0 | `Inventory_Available_Equals_OnHand_Minus_Reserved` |
| INV-3 | `StockReservation` cannot transition Released → Fulfilled or vice versa | `Reservation_Terminal_States_Are_Absorbing` |
| INV-4 | An order in `Paid` status must have at least one `Payment` with status `Captured` | `Paid_Order_Has_Captured_Payment` |
| INV-5 | An order in `Shipped` status must have all line items reserved (no oversell) | `Shipped_Order_All_Items_Reserved` |
| INV-6 | Idempotency: same `Idempotency-Key` → same response within 24h | `Idempotency_Returns_Cached_Response` |
| INV-7 | Order state transitions follow the documented graph (no Draft → Shipped, etc.) | `OrderStatus_Transitions_Match_Spec` |

## State machines (encoded in `StateTransitionTests.cs`)

```
Order:
  Draft ──Submit──▶ Submitted ──Pay──▶ Paid ──Ship──▶ Shipped ──Deliver──▶ Delivered
    │                  │                  │              │
    └────Cancel────────┴──Cancel──────────┘              └──Return──▶ Returned
                                                              (only within 30d)

Reservation:
  Created ──Confirm──▶ Confirmed ──Fulfill──▶ Fulfilled (terminal)
    │           │
    └─Release──┴──Release──▶ Released (terminal)
```

## Seeded realistic mess (deliberate)

The scaffold includes the kind of issues real codebases have. These are NOT bugs to fix — they're the noise that maintenance edits must navigate around without amplifying. Documented here so they can be audited.

| ID | Where | What |
|----|-------|------|
| MESS-1 | `OrderValidator.cs` and `OrderService.cs` | Status-validation logic duplicated; OrderValidator allows `Submitted → Cancelled` but OrderService also re-checks and is one transition stricter. Subtle drift. |
| MESS-2 | `Order.CalculateTotal()` and `OrderService.RecalculateTotal()` | Two implementations. Order entity rounds at 2 decimals; OrderService rounds at 4. Discrepancy < 1 cent on most carts but flags on bulk orders. |
| MESS-3 | `PaymentService.cs` | A `// TODO: refactor — too many params` comment that's load-bearing — the param order matches what `Stripe.Charges.Create` expects elsewhere. |
| MESS-4 | `StructuredLogger.cs` | Used inconsistently — `OrderService` logs every method entry; `InventoryService` only logs errors. |
| MESS-5 | `InventoryRepository.cs` | One async method (`GetByIdAsync`) doesn't actually await anything but is marked `async Task<>` — minor, but a model that "fixes" it changes the signature and breaks callers. |
| MESS-6 | `OrderLineItem.cs` | `Quantity` is `int`, but `UnitPrice` is `decimal`. Multiplication done in `decimal` everywhere — except in one place (`OrderService.EstimateTotal`) where it's cast to `double` for legacy reasons. |
| MESS-7 | `Customer.cs` | Has a `LegacyCustomerCode` field with a `[Obsolete]` attribute — but it's still serialized in the API response for a partner integration. |

## Test target on initial scaffold

- All `dotnet build` succeeds with `TreatWarningsAsErrors`
- All ~40 tests pass on the unmodified scaffold
- All 7 invariant tests pass
- All state-transition tests pass

If any of these fail on the unmodified scaffold, the scaffold is incomplete and must be fixed before T1 begins.

## Non-goals

- This scaffold is **not** trying to be production-quality code. It's trying to be representative of code coding agents encounter.
- It is **not** trying to be exotic or unusual. Patterns should feel familiar to anyone who has seen a .NET service.
- Performance, security hardening, observability beyond basic logging — out of scope.

## Calor variant

The Calor variant is a literal port of the C# scaffold using the same architecture and the same invariants. Differences:

- `.calr` files instead of `.cs` (except generated `.g.cs`)
- Effects declared on every method (`§E{}`, `§E{db:r}`, `§E{db:w}`, `§E{net:r}`, `§E{log}`, `§E{mem:w}`, `§E{throw}`)
- Preconditions/postconditions on services and validators (`§Q`, `§S`)
- Same MESS-1 through MESS-7 issues — translated faithfully (e.g., MESS-1 keeps the duplication, just expressed in Calor)
- Same invariant tests (xUnit, written in C#, against the compiled Calor)

The port goal is fidelity, not optimization. If Calor's structural advantages help, they should help on the same problems with the same noise.
