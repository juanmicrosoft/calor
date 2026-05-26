# task-03: Change a method from `prv` to `pub` and update 5 call sites

## Prompt to the agent

The method `ComputeTax` in `setup/TaxCalc.calr` is declared `prv`
(private). Five call sites across three other files
(`setup/Order.calr`, `setup/Invoice.calr`, `setup/Receipt.calr`)
incorrectly attempt to call `ComputeTax` and currently rely on a
workaround (a public wrapper named `ComputeTaxWrapper`).

Change `ComputeTax` to `pub` and update all 5 call sites to call
`ComputeTax` directly. Delete the now-unused `ComputeTaxWrapper`
function from `TaxCalc.calr`.

## Acceptance

- `ComputeTax` is declared `pub` in `TaxCalc.calr`.
- `ComputeTaxWrapper` no longer exists in `TaxCalc.calr`.
- `Order.calr`, `Invoice.calr`, and `Receipt.calr` each contain at
  least one `§C{ComputeTax}` and no `§C{ComputeTaxWrapper}`.
- Total `§C{ComputeTax}` calls across the three callers == 5.

## Why this stresses the ID system

Tests cross-file identity. In Phase 2, callers reference compact IDs
of `ComputeTax`. The wrapper deletion forces the agent to update all
sites correctly; a missed site would either compile-fail or change
semantics.

## Multi-edit qualifier

3 caller files × variable sites + 1 visibility change + 1 deletion =
≥6 sites across ≥4 files.
