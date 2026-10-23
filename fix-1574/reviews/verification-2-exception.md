# #1574 Codex verification pass 2 (approved capacity exception)

- **Why it exists:** the review budget (3 rounds + 1 verification pass) ended with 2 MAJOR findings open
  (`verification.md`). The maintainer approved a capacity exception, relayed by the coordinating agent on
  2026-10-22: fix both open variants, then run exactly one more verification pass, with no further rounds.
- **Fix under review:** commit `e4a65f783`.
  - The temp's type is never written through a `using` alias. A minimal display that names any alias escalates.
  - A property target escalates when its setter, setter parameter or getter return carries a
    `System.Diagnostics.CodeAnalysis` nullability attribute. So does a field or parameter target with one.
  - Witness rows: `V-using-alias`, `V-generic-alias`, `V-nested-alias`, `V-setter-allow-null` and the native
    control `V-control-no-alias`, each on 5 surfaces.
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (codex-cli 0.163.0).
- **Verdict:** **VERIFIED.** Both findings are CLOSED, and no new BLOCKING or MAJOR finding was reported.

## Codex output (verbatim)

By inspection of commit `e4a65f783`:

- **Using-alias finding — CLOSED.** `TempTypeName` scans the complete minimal display and rejects visible aliases before mapping it. This covers aliases in type arguments, nested types, arrays, and global using aliases. [Roslyn’s lookup explicitly includes aliases](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Compilation/CSharpSemanticModel.cs).
- **Setter-parameter nullability finding — CLOSED.** `HasNullabilityContract` checks the target symbol, setter attributes, setter parameters and return attributes, and getter attributes and return attributes. Direct field and parameter contracts remain covered.

The five new V-* rows match the intended preservation behavior and retain a native no-alias control. Rejected lowering follows the existing C# preservation path, which clears partial hoists.

No new BLOCKING or MAJOR finding, silent behavior difference, or compile regression identified in the delta by inspection. No files modified; no builds or tests run.

VERIFIED
