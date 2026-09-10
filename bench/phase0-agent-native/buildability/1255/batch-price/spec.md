# Batch pricing

Implement `Preview(int quantity)` for a batch order. Use `PriceBatch`,
which applies a five-unit discount above ten items. Standard pricing is
three units per item. Accept nonnegative quantities, including zero.
Preserve the public API and library behavior.

## Dependency quick start

`StandardPrice` is the library's normal pricing callback. Supply it to
`PriceBatch` along with the batch quantity.
