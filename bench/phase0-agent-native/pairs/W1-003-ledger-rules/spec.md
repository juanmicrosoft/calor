# W1-003 — Ledger Fee Rules (behavioral specification)

This is the only task statement either arm sees. It is arm-neutral: it specifies
behavior and a public surface, never implementation language idioms.

All arithmetic is 32-bit integer arithmetic. Callers keep every amount in
`1 .. 999999`, so no 32-bit overflow occurs; overflow behavior is outside the
contract.

## Data model

The ledger is a double-entry journal held in three parallel arrays. Entry `i`
is the triple `(debits[i], credits[i], amounts[i])` and moves `amounts[i]`
units **from** the debit account **to** the credit account. `count` is the
number of entries; entries `0 .. count-1` are valid and array capacity beyond
`count` is scratch space. Accounts are small non-negative integers; **account
0 is the house fee account**. Because every entry debits exactly what it
credits, the sum of all account balances is always zero (conservation).

## Existing behavior (already implemented in the starting fixture — must be preserved except where the task below changes it)

- `EntryFee(amount)` — the fee charged on a transfer of `amount`.
- `IsValidAccount(account, accountCount)` / `IsValidAmount(amount)` —
  validation predicates (`0 <= account < accountCount`; `0 < amount < 1000000`).
- `PostEntry(debits, credits, amounts, count, debitAcct, creditAcct, amount)`
  — appends one entry at index `count`, returns `count + 1`. Requires
  capacity for one more entry.
- `AccountBalance(debits, credits, amounts, count, account)` — credits minus
  debits for `account` over entries `0 .. count-1`.
- `TotalDebited` / `TotalCredited` — per-account one-sided totals.
- `TotalVolume(amounts, count)` — sum of all entry amounts.
- `FeesCollected(credits, amounts, count)` — total credited to account 0.
- `TransferCost(amount)` — the total a payer gives up when transferring
  `amount`: `amount + fee(amount)` under the current fee schedule.
- `CanAfford(balance, amount)` — `balance >= TransferCost(amount)`.
- `Transfer(debits, credits, amounts, count, fromAcct, toAcct, amount)` —
  posts the main entry (`fromAcct -> toAcct, amount`), then, **only when the
  fee is positive**, posts a fee entry (`fromAcct -> account 0, fee`).
  Returns the new entry count. Requires capacity for two entries.
- `BatchPost(...)` — appends `n` plain entries taken from three input arrays;
  equivalent to `n` calls to `PostEntry`. No fees.
- `BatchTransfer(...)` — performs `n` transfers taken from three input
  arrays; must remain observably equivalent to `n` sequential calls to
  `Transfer` (same entries in the same order, same final count).

## Task

### 1. Replace the fee schedule

The current schedule charges `amount / 100` (integer division) on every
transfer. The new schedule is:

- `amount >= 2000` → fee is `0` (large transfers are fee-free),
- `amount < 100` → fee is `1` (minimum fee),
- otherwise → fee is `amount / 100` (unchanged mid-band).

Every fee the module charges, prices, or predicts must follow the new
schedule consistently: `EntryFee` returns it, `Transfer` and `BatchTransfer`
post a fee entry exactly when it is positive, `TransferCost(amount)` equals
`amount + fee(amount)`, and `CanAfford` agrees with `TransferCost`.

### 2. Add a reversal operation

`ReverseEntry(debits, credits, amounts, count, index)` → integer — appends a
compensating entry for existing entry `index` (`0 <= index < count`): the
debit and credit accounts are swapped, the amount is identical. Returns
`count + 1`. Requires capacity for one more entry. After reversing an entry,
every account balance is as if the original entry had never been posted
(conservation is preserved). Reversal never charges a fee.

### Constraints

- Do **not** change any other observable behavior: plain posting, balances,
  totals, batch posting, and the entry layout (order and contents of posted
  entries) must remain exactly as before, except where the new fee schedule
  changes which fee entries exist and their amounts.
- Pure integer computation only: no I/O, no global state, no floating point.

## Public surface (pinned; held-out tests bind to it via a fixed per-arm shim)

Static functions `EntryFee(int) → int`, `IsValidAccount(int, int) → bool`,
`IsValidAmount(int) → bool`,
`PostEntry(int[], int[], int[], int, int, int, int) → int`,
`AccountBalance(int[], int[], int[], int, int) → int`,
`TotalDebited(int[], int[], int, int) → int`,
`TotalCredited(int[], int[], int, int) → int`,
`TotalVolume(int[], int) → int`,
`FeesCollected(int[], int[], int) → int`,
`TransferCost(int) → int`, `CanAfford(int, int) → bool`,
`Transfer(int[], int[], int[], int, int, int, int) → int`,
`BatchPost(int[], int[], int[], int, int[], int[], int[], int) → int`,
`BatchTransfer(int[], int[], int[], int, int[], int[], int[], int) → int`,
`ReverseEntry(int[], int[], int[], int, int) → int`, reachable through the
arm's `TestShim.cs` (provided by the harness; not editable by the agent).
