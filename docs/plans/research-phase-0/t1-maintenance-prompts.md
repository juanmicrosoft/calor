# T1 — Maintenance / Second-Edit Prompts

**Locked once committed. The exact text below is what the model sees, verbatim, with no preamble beyond the system prompt and the file tree.**

## Theory under test (T1)

> Coding agents are more successful at maintaining (extending, modifying) existing code in Calor than in C#, because Calor's effect declarations and contracts make cross-file coordination explicit and machine-verifiable.

## Common harness

Every T1 run starts from the same scaffold commit (tagged `phase-0-baseline`). The model receives:

1. The system prompt (Claude Code default, no Calor-specific instructions beyond what's in the project's CLAUDE.md)
2. The user prompt below (one of T1.A / T1.B / T1.C)

The model has full access to the file system via standard tools.

A run completes when:
- The model says it's done (any phrasing), OR
- 60 minutes elapse, OR
- 50 turns reached, OR
- $25 spent (hard cap per run)

Whichever comes first.

---

## T1.A — Add order priority

```
Add a "priority" concept to orders. Customers can mark orders as Standard, Expedited, or Critical when submitting them. The priority should be:

- Persisted with the order
- Returned in the order's API response
- Validated (only the three allowed values)
- Used by the shipment service so that Critical orders ship before Expedited, which ship before Standard

Maintain backwards compatibility: existing orders without an explicit priority should be treated as Standard.

When you're done, run the test suite and confirm everything passes.
```

**Files this should touch (5–7):**
- `Domain/Entities/Order.cs` (or `.calr`) — new field
- `Domain/Enums/` — new `OrderPriority` enum
- `Services/OrderService.cs` — accept priority on submit
- `Services/Validators/OrderValidator.cs` — validate the value
- `Services/ShipmentService.cs` — use priority to order
- `Api/Controllers/OrdersController.cs` — accept in request DTO
- `Infra/Persistence/AppDbContext.cs` — schema (column or config)

**Feature-acceptance tests (pre-defined):**
- `Order_Submit_With_Priority_Persists`
- `Order_Submit_Without_Priority_Defaults_To_Standard`
- `Order_Submit_With_Invalid_Priority_Returns_400`
- `Shipment_Schedule_Critical_Before_Expedited_Before_Standard`

**Adversarial post-hoc tests (model doesn't see):**
- Submitting `priority: "URGENT"` (invalid) doesn't accidentally accept and downcast
- Old orders queried after the migration return `Standard` rather than `null`/throwing
- Shipment ordering is stable for ties (multiple Critical orders preserve insertion order)

---

## T1.B — Inventory reservation expiry

```
Inventory reservations should expire if not confirmed within 30 minutes. When a reservation expires:

- Its status becomes "Released"
- The reserved quantity returns to the inventory item's available pool
- A notification is sent (use the existing NotificationService)

Add a background mechanism that processes expirations. You can use a hosted service, a timer, or anything reasonable — the test suite checks behavior, not implementation choice.

When you're done, run the test suite and confirm everything passes.
```

**Files this should touch (4–6):**
- `Domain/Entities/StockReservation.cs` — `ExpiresAt` field
- `Services/InventoryService.cs` — expiry logic
- New `Services/ReservationExpiryWorker.cs` (or equivalent in .calr)
- `Api/Program.cs` (or `DependencyRegistration.cs`) — wire up the worker
- `Infra/Persistence/InventoryRepository.cs` — query for expired

**Feature-acceptance tests (pre-defined):**
- `Reservation_Expires_After_30_Minutes_If_Not_Confirmed`
- `Expired_Reservation_Releases_Inventory`
- `Expired_Reservation_Sends_Notification`
- `Confirmed_Reservation_Does_Not_Expire`

**Adversarial post-hoc tests (model doesn't see):**
- A reservation released by the user manually doesn't get "released again" by the expiry worker (idempotent)
- A Fulfilled reservation never gets re-released by expiry (terminal-state respect — INV-3)
- Inventory.Available stays consistent (INV-2) while expiry runs concurrently with new reservations

---

## T1.C — Partial order fulfillment

```
Today, an order ships in one shipment. Add support for partial fulfillment: an order can ship in multiple shipments, each containing a subset of the line items.

Requirements:
- An order is "Shipped" only when ALL line items have been shipped
- Until then, it remains "Paid" (the prior status)
- Each shipment records which line items (and quantities) it contains
- Shipping more than the line item's quantity must be rejected
- The Shipments API exposes a list of shipments per order

Update the existing tests to reflect partial-fulfillment behavior where appropriate. Add new tests for the new behavior.

When you're done, run the test suite and confirm everything passes.
```

**Files this should touch (6–9) — most cross-file of the three:**
- `Domain/Entities/Shipment.cs` — line item subset (+ qty)
- `Domain/Entities/OrderLineItem.cs` — track quantity shipped
- `Domain/Entities/Order.cs` — `IsFullyShipped` derivation
- `Services/ShipmentService.cs` — partial-shipment logic
- `Services/OrderService.cs` — status transition only when fully shipped
- `Api/Controllers/ShipmentsController.cs` — list endpoint
- `Api/Controllers/OrdersController.cs` — order response includes shipments
- `Infra/Persistence/...` — schema for shipment-line-items
- Tests — updates

**Feature-acceptance tests (pre-defined):**
- `Order_Status_Stays_Paid_After_Partial_Shipment`
- `Order_Status_Becomes_Shipped_After_All_Items_Shipped`
- `Cannot_Ship_More_Than_Line_Item_Quantity`
- `Shipments_List_Returns_All_Shipments_For_Order`

**Adversarial post-hoc tests (model doesn't see):**
- Sum of shipped quantities across shipments equals line item quantity for fully-shipped order (INV-5 generalization)
- Shipment cannot reference a line item from a different order
- Cancelling an order with partial shipments doesn't quietly mark already-shipped items as Cancelled

---

## Why these three

| Prompt | Cross-file coordination | State machine impact | New invariant introduced | Why included |
|--------|-------------------------|-----------------------|--------------------------|--------------|
| T1.A | Medium (5–7 files) | None | None (just an attribute) | Baseline — does Calor help on a "boring" change? |
| T1.B | Medium-High (4–6 files + new background pattern) | Reservation states | Yes (idempotent expiry) | Tests Calor's effect system — explicit `§E{db:w, log}` declaration on the worker |
| T1.C | High (6–9 files, cross-aggregate) | Order + Shipment | Yes (sum constraint INV-5 generalization) | Hardest — most invariant churn, most place for things to silently break |

If Calor wins on T1.B or T1.C and not T1.A, that's evidence the effect/contract machinery is the active ingredient.
If Calor wins on T1.A but not T1.B/C, that's noise.

## Order of execution

T1.A → T1.B → T1.C, completing all 5×2 trials of one before starting the next. This lets us stop early if T1.A produces strong negative signal (saves $).
