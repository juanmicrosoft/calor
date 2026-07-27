# Task: ledger description and size

The `Ledger` module persists entries and answers balance queries over
them. `AppendEntry` is the module's **only writer**; every query function
(`ReportOf`, `BalanceReport`, `HasEntries`) is **read-only** — it may
read the ledger file but never writes anything (queries run from
read-only contexts: health checks, dashboards).

Add two queries:

1. `DescribeLedger(path)` — returns `"ledger: "` followed by exactly what
   `BalanceReport(path)` returns.
2. `LedgerSize(path)` — the length of the ledger content at `path`
   (0 when the ledger is absent).

Rules:

- Public surface: keep every existing public function unchanged in name,
  signature, and behavior; add the two new public query functions.
- Queries stay read-only; `AppendEntry` remains the only writer.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
