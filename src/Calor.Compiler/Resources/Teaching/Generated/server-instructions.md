Calor is an indentation-only DSL that compiles to C# on .NET 10.

### MANDATORY Rules for AI Agents

Rule 0: Read `calor://primer` once before writing Calor. Use `calor_help` with `query` or `feature` for details; `action=features` reports conversion capabilities.
Rule 1: Never create new `.cs` files. All new code MUST be written in Calor; new files must be `.calr` files.
Rule 2: Convert C# to Calor before modifying: use `calor_convert`, then edit the `.calr` source. Use `calor analyze` for migration assessment.
Rule 3: Never edit generated files (`.g.cs`).
Rule 4: Compile after writing with `calor_compile` (autoFix is on by default); apply diagnostics and verify contracts with `calor_verify`.

Indent bodies two spaces; structural blocks end at dedent.
Use documented lowercase headers, func and →, without source IDs. Types precede names.
Use let for immutable bindings, var for mutable bindings and set for updates.
Effects (!) and postconditions (=>) precede the first body statement.
Expressions use C# precedence, postfix calls f(x) and indexing items[i].
Use C# type names, T[] arrays, T? nullability and exceptions for absence and failure.
Plain strings interpolate ${expression}; \${0} preserves literal placeholder text.
Bind local helpers to lambda values rather than nested function declarations.
Do not handwrite raw C# passthrough; converters own exact fallback output.

Read `calor://effects` for effect codes, `calor://tags` for syntax, `calor://id-prefixes` for tooling-owned identity, and `calor://workflows` for editing workflows. Use `calor_file_write` for atomic checked edits; for multi-file edits use `calor_session_open` and `calor_session_close`.

These are best-effort agent instructions; compiler diagnostics remain authoritative.

Convert before modifying existing C#. Agent hooks are best-effort; repository CI is the backstop for changes against a protected base.
