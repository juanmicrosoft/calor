# T2 Prompts — Phase 1 (locked at first-commit)

Each prompt is given verbatim to the agent, no preamble. Same harness/cap rules as Phase 0 (60 min / 50 turns / $25).

## T2.A — Promo code discount (risks INV-1)

```
Add a promo code discount feature.

Customers enter a promo code at order submission. Each code applies a flat percentage discount (10%, 25%, etc.) to the order's total. The discount must be reflected in the order's API response so customers see the discounted price.

Available promo codes (hardcoded for now):
  - "WELCOME10" → 10% off
  - "BULK25" → 25% off

Validation:
  - Unknown promo codes are rejected with an HTTP 400.
  - The discount only applies at order submission; existing orders aren't retroactively discounted.

Maintain backwards compatibility: orders without a promo code submit at their full price.

When you're done, run the test suite and confirm everything passes.
```

**Files this should touch (5–7):**
- `Domain/Entities/Order.cs` — discount tracking
- `Services/OrderService.cs` — apply at SubmitAsync
- `Services/Validators/OrderValidator.cs` — reject unknown codes
- `Api/Controllers/OrdersController.cs` — accept code in submit request
- Possibly a new `PromoCodeService` or similar

**Risk class (the bug T2.A targets):** INV-1 violation. The natural implementation modifies `TotalAmount` directly (e.g., `order.TotalAmount = order.TotalAmount.Multiply(0.9m)`). After this, `TotalAmount ≠ Σ(LineItem.Quantity × LineItem.UnitPrice)` — INV-1 broken. A careful implementation either: (a) tracks discount separately (`DiscountAmount` field) leaving TotalAmount as the line-item sum, or (b) re-derives both: TotalAmount stays as line-item sum, a new field GrandTotal is sum minus discount.

**Acceptance tests (graders/T2.A/):**
- `Acceptance_Submit_With_Valid_Promo_Applies_Discount` — submitted order's response shows discounted price
- `Acceptance_Submit_With_Unknown_Promo_Returns_400`
- `Acceptance_INV1_Holds_After_Promo` — `Order.TotalAmount == Σ(LineItem.Qty × UnitPrice)` still holds (the bug detector)
- `Acceptance_Existing_INV1_Test_Still_Passes` — runs the original `INV1_Order_Total_Equals_LineItem_Sum`

## T2.B — Partial reservation release (risks INV-3) — primary

```
Add support for partial release of stock reservations.

Today, ReleaseAsync releases the entire reservation. Add a new method that releases a *portion* of a reservation's quantity. For example, given a reservation for 5 units, the user can release 2 — leaving 3 still reserved.

Requirements:
  - The released quantity returns to the inventory item's available pool.
  - The original reservation continues to exist with the remaining quantity.
  - If the user partial-releases the full remaining quantity, the reservation transitions to Released (matching the existing ReleaseAsync behavior).
  - Expose this through the inventory API.

When you're done, run the test suite and confirm everything passes.
```

**Files this should touch:**
- `Services/InventoryService.cs` — new method (e.g., `ReleasePartiallyAsync`)
- `Domain/Entities/StockReservation.cs` — possibly a Quantity setter or different design
- `Api/Controllers/InventoryController.cs` — new endpoint

**Risk class:** INV-3 violation. The natural implementation might mutate the existing reservation's Quantity, OR call partial-release on a Released/Fulfilled reservation without checking. Either:
- Allowing partial-release of an already-Released reservation re-releases inventory (over-counts available)
- Allowing partial-release of a Fulfilled reservation un-fulfills it (terminal-state violation)

**Acceptance tests:**
- `Acceptance_Partial_Release_Reduces_Quantity` — original reservation now has remaining quantity
- `Acceptance_Partial_Release_Returns_To_Available_Pool`
- `Acceptance_Partial_Release_To_Zero_Becomes_Released`
- `Acceptance_Partial_Release_Of_Released_Reservation_Throws` (INV-3 protection test)
- `Acceptance_Partial_Release_Of_Fulfilled_Reservation_Throws` (INV-3 protection test)
- `Acceptance_Existing_INV3_Test_Still_Passes`

## T2.C — Order recall (risks INV-5)

```
Add an "order recall" feature.

An admin can recall a Shipped order: this reverses the shipped state and returns the order to Submitted status. The customer is notified via NotificationService.

Constraints:
  - Only Shipped orders can be recalled (not Delivered, not Returned, not anything earlier in the lifecycle).
  - When recalled, all of the order's reservations should be marked Released (returning inventory to the available pool).
  - Add a "RecalledAt" timestamp on the order to record when recalls happened.

When you're done, run the test suite and confirm everything passes.
```

**Files this should touch:**
- `Domain/Entities/Order.cs` — RecalledAt field
- `Services/OrderService.cs` — RecallAsync method
- `Services/InventoryService.cs` (or coordinator) — release reservations
- `Services/NotificationService.cs` (only invocation, not change)
- `Api/Controllers/OrdersController.cs` — recall endpoint

**Risk class:** INV-5 violation. INV-5 says a Shipped order has all line items reserved. After recall, the reservations are Released — but the order moves back to Submitted. INV-5 was about the *Shipped* state being consistent; the recall flow needs to preserve INV-5 *during* the transition (no intermediate state where the order is still Shipped but reservations are Released). Also, terminal-state INV-3 violation if the recall tries to "un-fulfill" Fulfilled reservations.

**Acceptance tests:**
- `Acceptance_Recall_From_Shipped_Returns_To_Submitted`
- `Acceptance_Recall_Releases_All_Reservations`
- `Acceptance_Recall_Sends_Notification`
- `Acceptance_Recall_Of_Non_Shipped_Throws`
- `Acceptance_INV5_Holds_After_Recall` (no intermediate inconsistent state)
- `Acceptance_INV3_Holds_After_Recall` (Fulfilled reservations aren't un-fulfilled)
- `Acceptance_Existing_INV3_INV5_Tests_Still_Pass`

## Order of execution

T2.B (primary) → T2.A → T2.C, completing all 5×2 trials of one cell before starting the next. Stops early if T2.B shows clear signal in either direction.
