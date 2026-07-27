# Task: grant minimum support

The `RateLimit` module computes capped request-budget grants.
`GrantRequests` returns the requested budget, and the returned grant
**never exceeds `maxAllowed`** — callers debit the request budget by it
directly.

Add minimum support:

1. `GrantWithMinimum(requested, maxAllowed, minGrant)` — the capped grant
   (exactly as `GrantRequests` computes it), but never below `minGrant`.
   When `minGrant > maxAllowed`, `minGrant` wins.
2. `FormatGrant(amount)` — returns `"granted: <amount>"`. Pure formatting.

Rules:

- Public surface: keep every existing public function unchanged in name,
  signature, and behavior; add the two new public functions.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
