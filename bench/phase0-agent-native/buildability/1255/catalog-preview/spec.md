# Catalog preview

Implement `Catalog.Preview(int value)`: return the sum of formatted `value`
and `value + 1` using the existing `SumFormatted` accumulator. The catalog
uses its default configuration. Inputs can be zero, positive, or negative.

Keep the existing public surface and the dependency implementation.

## Dependency quick start

`Catalog.Formatter` is the standard formatting callback. Pass `Formatter`
to `SumFormatted` along with the input value. The default formatting
transformation doubles its input. `dependency.calr` contains the library.
