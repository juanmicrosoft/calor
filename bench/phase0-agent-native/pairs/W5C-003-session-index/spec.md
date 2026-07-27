# Task: session description and index size

The `Session` module persists a session index and answers lookups over
it. `WriteIndex` and `MarkActive` are the module's **only writers**;
every lookup function (`LookupSession`, `HasIndex`) is **read-only** —
it may read the index file but never writes anything (lookups run from
read-only contexts: health checks, dashboards).

Add two lookups:

1. `DescribeSession(path, sessionId)` — returns `"session: "` followed by
   exactly what `LookupSession(path, sessionId)` returns.
2. `IndexSize(path)` — the length of the index content at `path`
   (0 when the index is absent).

Rules:

- Public surface: keep every existing public function's name and signature
  unchanged, and its behavior as declared and documented; add the two new public lookup functions.
- Lookups stay read-only; `WriteIndex` and `MarkActive` remain the only
  writers.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
