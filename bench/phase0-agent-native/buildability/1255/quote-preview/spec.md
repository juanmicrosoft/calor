# Quote preview

Implement `QuoteCalculator.Preview(QuoteFeed feed)`. Return the sum of two
samples of the supplied quote feed. Rates may be zero, positive, or negative;
do not hard-code a rate. Reuse the existing `SumSamples` accumulator.

The feed does not change during a preview. Preserve the existing public API.

## Dependency quick start

`QuoteFeed.Sample` is the standard callback for this accumulator. Pass
`feed.Sample` to `SumSamples` to obtain two samples. A feed is constructed
with its current rate. See `dependency.calr.inc` for the implementation.
