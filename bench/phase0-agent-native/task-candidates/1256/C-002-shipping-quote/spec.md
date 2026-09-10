# Shipping quote comparison

Implement `ShippingService.Cheapest(int weight)`. Obtain quotes for zones
one and two and return the lower quote. Use the supplied pricing library,
not a copy of its pricing formulas. Preserve the library and public surface.

Inputs are integer weights from zero through one hundred. For a zero
weight, each quote is zero. Otherwise zone one costs `3 * weight + 5`,
and zone two costs `2 * weight + 12`. Either quote may be the cheaper one.

## Dependency quick start

The service's standard quote callback is `this.quote(zone, weight)`.
It accepts either supported zone. `dependency.calr.inc` supplies the library;
the new method belongs in the separate edit fragment.
