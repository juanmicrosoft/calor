# N1-005 — Order Pipeline (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

## Data model

- All money amounts are integer **cents**; all rates are integer **basis
  points** (1 bps = 0.01%). Every division below is integer division
  (truncation toward zero).
- An order is described by `qty` (units), `unitPrice` (cents per unit), the
  customer's `tier`, and a `taxRateBps`.
- **Valid order:** `1 <= qty <= 1000` and `unitPrice >= 0`.
- **Tier discounts:** tier 1 → 500 bps, tier 2 → 1000 bps, tier 3 →
  1500 bps, any other tier → 0 bps.
- **Order states** (integer codes): 0 Pending, 1 Confirmed, 2 Shipped,
  3 Delivered, 4 Cancelled.
- **Rate and amount domains:** callers guarantee `taxRateBps` and
  `restockFeeBps` are always in `0..10000` (inclusive), and all amounts
  (`qty`, `unitPrice`, subtotals, totals) are non-negative — so every
  computed discount, tax, fee, and refund is non-negative.
- **Per-customer aggregate:** a mutable customer → running-total map
  (`Dictionary<string, int>`) owned by the caller and passed to every
  operation. **Invariant: the map never stores a zero or negative total** —
  a customer with nothing outstanding is simply absent.

## Existing behavior (already implemented in the starting fixture)

- `IsValidOrder(qty, unitPrice)` → boolean — the validity rule above.
- `TierDiscountBps(tier)` → integer — the discount table above.
- `Subtotal(qty, unitPrice)` → integer — `qty * unitPrice`.
- `DiscountAmount(subtotal, tier)` → integer — `subtotal * discountBps / 10000`.
- `TaxAmount(amount, taxRateBps)` → integer — `amount * taxRateBps / 10000`.
- `OrderTotal(qty, unitPrice, tier, taxRateBps)` → integer — `0` for an
  invalid order; otherwise `net + TaxAmount(net, taxRateBps)` where
  `net = subtotal - discount` (tax applies to the discounted amount).
- `CanTransition(fromState, toState)` → boolean — exactly these transitions
  are allowed: 0→1, 0→4, 1→2, 1→4, 2→3.
- `ApplyTransition(state, toState)` → integer — `toState` when the
  transition is allowed, otherwise `state` unchanged.
- `RecordOrder(totals, customer, amount)` — adds `amount` to the customer's
  running total (creating the entry if absent).
- `CustomerTotal(totals, customer)` → integer — the customer's running
  total, `0` if absent.
- `GrandTotal(totals)` → integer — sum of all running totals.
- `ActiveCustomers(totals)` → integer — the number of customers present in
  the map.
- `ProcessOrder(totals, customer, qty, unitPrice, tier, taxRateBps)` →
  integer — `0` for an invalid order (recording nothing); otherwise computes
  the order total, records it for the customer **only when it is strictly
  positive**, and returns it.

## Task: implement the returns/refund flow

1. **Two new order states:** 5 Returned, 6 Refunded. Extend the transition
   matrix with exactly two new transitions — 3→5 and 5→6. All previously
   allowed transitions remain allowed; nothing else is allowed.
2. `RefundAmount(qty, unitPrice, tier, taxRateBps, restockFeeBps)` →
   integer — the refund for returning the given order in full:
   `total - fee`, where `total = OrderTotal(qty, unitPrice, tier, taxRateBps)`
   and `fee = total * restockFeeBps / 10000`. (An invalid order therefore
   refunds `0`.)
3. `RecordRefund(totals, customer, amount)` — subtracts `amount` from the
   customer's running total. If the customer is absent, does nothing. If the
   remaining total would be zero or negative, the customer's entry is
   **removed entirely** (see the map invariant).
4. `ProcessReturn(totals, customer, state, qty, unitPrice, tier, taxRateBps,
   restockFeeBps)` → integer — processes a return of the given order:
   - Allowed only when `state` can transition to Returned (5); otherwise
     returns `0` and changes nothing.
   - Computes the refund via `RefundAmount`; applies it via `RecordRefund`
     **only when it is strictly positive**; returns it.

**Invariant to preserve** (the held-out tests probe it across mixed
operation sequences): after any sequence of `ProcessOrder` / `ProcessReturn`
calls, `GrandTotal` equals the sum of all values returned by `ProcessOrder`
minus the sum of all values returned by `ProcessReturn`, and
`CustomerTotal` satisfies the same per customer. `ActiveCustomers` counts
exactly the customers whose net total is positive. Callers guarantee a
return always corresponds to a previously recorded order for that customer,
so a refund never exceeds the customer's running total; combined with the
domain guarantees above (`taxRateBps` and `restockFeeBps` in `0..10000`,
amounts positive), refunds are always non-negative and never exceed the
order's total.

## Constraints

- Operations mutate the caller's map in place; no filesystem, network, or
  console effects. Declare whatever your language requires to express that.
- Do not change the observable behavior of the existing operations (beyond
  the two new transitions specified above).

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `IsValidOrder(int, int) → bool`, `TierDiscountBps(int) → int`,
`Subtotal(int, int) → int`, `DiscountAmount(int, int) → int`,
`TaxAmount(int, int) → int`, `OrderTotal(int, int, int, int) → int`,
`CanTransition(int, int) → bool`, `ApplyTransition(int, int) → int`,
`RecordOrder(Dictionary<string, int>, string, int)`,
`CustomerTotal(Dictionary<string, int>, string) → int`,
`GrandTotal(Dictionary<string, int>) → int`,
`ActiveCustomers(Dictionary<string, int>) → int`,
`ProcessOrder(Dictionary<string, int>, string, int, int, int, int) → int`,
`RefundAmount(int, int, int, int, int) → int`,
`RecordRefund(Dictionary<string, int>, string, int)`,
`ProcessReturn(Dictionary<string, int>, string, int, int, int, int, int, int) → int`,
reachable through the arm's `TestShim.cs` (provided by the harness; not
editable by the agent).
