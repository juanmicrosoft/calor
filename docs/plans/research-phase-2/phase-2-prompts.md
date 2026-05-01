# T3 Prompts — Phase 2 (compositional correctness)

Same harness rules as Phase 0/1. Same scaffolds (csharp-baseline annotated, csharp-bare). T3.B is the primary decision cell.

T3 prompts target the **T2.C difficulty band** — features where the natural implementation has a ~50% chance of introducing a documented invariant violation. Sweet spot for surfacing annotation effects.

## T3.A — Inventory transfer between locations (multi-INV)

```
Add multi-location inventory support.

Each inventory item now belongs to a Location (a string identifier — e.g. "warehouse-east", "warehouse-west"). Add support for transferring stock from one location to another:

  - TransferAsync(sku, fromLocation, toLocation, quantity)
  - Decreases OnHand at fromLocation by quantity
  - Increases OnHand at toLocation by quantity (creating the destination item if needed)
  - Rejected if fromLocation doesn't have enough Available (not just OnHand)
  - Reservations stay bound to their original location

Maintain backwards compatibility: existing inventory items use a default location.

When you're done, run the test suite and confirm everything passes.
```

**Risk class:** INV-2 violation across two items. Natural buggy implementation transfers OnHand without checking Available, or modifies one side without persisting the other consistently.

## T3.B — Refund processing (primary, INV-3 + INV-4)

```
Add a refund feature for paid orders.

An admin can request a refund on a Paid order:
  - The order's status transitions to "Refunded"
  - All Captured payments for that order have their status set to "Refunded"
  - All non-terminal reservations (Created or Confirmed) are Released
  - The customer is notified via NotificationService

Constraints:
  - Refund is only allowed on Paid orders. Reject Draft, Submitted, Shipped, Delivered, Cancelled, Returned with HTTP 400.
  - If the order has any Fulfilled reservations, the refund is rejected — those goods are gone, and a separate (out-of-scope) return process would be needed.

Add Refunded to OrderStatus and PaymentStatus.

When you're done, run the test suite and confirm everything passes.
```

**Risk class:** INV-3 (release Fulfilled reservations) and INV-4 (Paid order must have Captured payment). Natural buggy implementation might:
- Refund Fulfilled reservations, breaking INV-3 (terminal-state absorbing)
- Skip the Captured-payment check, leading to inconsistent state
- Forget to release reservations, leaving inventory falsely reserved

## T3.C — Order split (INV-1 + INV-3)

```
Add order-split support.

An admin can split a Submitted order into two orders:
  - The original order keeps a subset of its line items
  - A new order is created with the remaining line items, inheriting the same customer, currency, and Submitted status
  - Both orders' TotalAmount must reflect their respective line-item sums
  - Existing reservations should be re-bound to whichever order now contains the corresponding SKU

Add a method SplitOrderAsync(orderId, lineItemIdsToMove) that returns the new order.

When you're done, run the test suite and confirm everything passes.
```

**Risk class:** INV-1 violation on either order, INV-3 violation if reservations get duplicated/lost during re-binding. Natural buggy implementation might:
- Create new order without recomputing TotalAmount from line items
- Leave reservations bound to the original order's ID
- Duplicate reservations across both orders

## Order of execution

T3.B (primary) → T3.A → T3.C, in parallel batches of 10.
