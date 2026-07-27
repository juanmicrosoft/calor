# Task: config sections and loading

The `Config` module formats config entries. Its `Format*`/`Quote*`
functions are **pure** — no I/O, no shared state (they are called from
contexts where file access is forbidden). `SaveConfig` is the only
function that touches the filesystem.

Add section and load support:

1. `FormatSection(name)` — returns `"[<name>]"`. Pure, like the other
   formatters.
2. `LoadConfig(path)` — returns the raw text of the config file at
   `path`. This one legitimately reads the filesystem — declare it
   accordingly (in Calor that means `§E{fs:r}`; keep the pure functions
   pure).

Rules:

- Public surface: keep every existing public function's name and signature
  unchanged, and its behavior as declared and documented; add the two new public functions.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
