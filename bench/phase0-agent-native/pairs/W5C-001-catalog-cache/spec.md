# Task: catalog description and size

The `Catalog` module persists snapshots and answers queries over them.
`SaveSnapshot` is the module's **only writer**; every query function
(`SummaryOf`, `LoadSummary`, `HasCatalog`) is **read-only** — it may read
the snapshot file but never writes anything (queries run from read-only
contexts: health checks, dashboards).

Add two queries:

1. `DescribeCatalog(path)` — returns `"catalog: "` followed by exactly what
   `LoadSummary(path)` returns.
2. `CatalogSize(path)` — the length of the snapshot content at `path`
   (0 when the snapshot is absent).

Rules:

- Public surface: keep every existing public function's name and signature
  unchanged, and its behavior as declared and documented; add the two new public query functions.
- Queries stay read-only; `SaveSnapshot` remains the only writer.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
