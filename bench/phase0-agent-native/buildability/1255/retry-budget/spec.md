# Retry budget

Implement `RetryPolicy.Estimate()`, the standard attempt's reported budget.
Use `WithRetry`, which retries once on a negative first result, instead of
embedding a number. Keep the configured policy and existing public API.

## Dependency quick start

The policy stores its standard attempt as `this.attempt`. Supply
`this.attempt` to `WithRetry` when calculating an attempt budget.
The implementation lives in `dependency.calr.inc`.
