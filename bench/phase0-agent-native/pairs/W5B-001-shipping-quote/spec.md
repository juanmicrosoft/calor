# Task: quote floor support

The `Quote` module computes capped shipping quotes. `QuoteWithSurcharge`
returns base + surcharge, and the returned quote **never exceeds `cap`** —
callers bill against it directly.

Add floor support:

1. `QuoteWithFloor(base, surcharge, cap, floor)` — the capped quote
   (exactly as `QuoteWithSurcharge` computes it), but never below `floor`.
   When `floor > cap`, `floor` wins.
2. `FormatQuote(amount)` — returns `"quote: <amount>"`. Pure formatting.

Rules:

- Public surface: keep every existing public function unchanged in name,
  signature, and behavior; add the two new public functions.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
