# 0.25 F4 (#1139) evidence: iterator accessors

**Status:** IN_PROGRESS (branch `milestone-0.25/f4-1139-iterator-accessors`). Not a READY record:
READY needs a candidate commit, per-case evidence carrying `requirementSha256`, the #1423/#1426
blocker references and the maintainer's approval (`docs/plans/v0.25-interop-scope-and-baseline.md` §10).

## Change

`RoslynSyntaxVisitor.ConvertProperty`/`ConvertIndexer` escalate any property or indexer whose
accessor body contains a `yield` statement (excluding nested local functions and lambdas) to
`§CSHARP` member interop, feature `iterator-accessor` (`FeatureSupport`: NotSupported). This is
converter-level, so no surface depends on the post-conversion rescue. `Calor0209` for native
`§YIELD` in an accessor is unchanged (F4-ITER-03 control). Native conversion was not attempted:
Calor has no iterator accessor, and a synthesized helper method would add a member and name
collisions for a capacity of 400 non-test lines.

## Files

| Path | Content |
|---|---|
| `reproduce/reproduce.py` | Driver. Imports the R0 driver and `ProbeRunner` unchanged |
| `results.json` | Per-surface results for F4-ITER-01/02 and the F4-ITER-03 control |
| `generated/` | Every `.calr` produced, per surface (`.calr.txt`, kept out of the corpus ledgers) |
| `reviews/` | Codex adversarial review records |

## Results (from `results.json`)

| Case | Surface | Output | Default compile | Run vs original |
|---|---|---|---|---|
| F4-ITER-01, -02 | `cli-default`, `cli-passthrough` | 2 `§CSHARP`, 0 `§YIELD`/`§YBRK`, loss `iterator-accessor` | pass | match |
| F4-ITER-01, -02 | `cli-migrate` (`calor migrate`, one-file project) | same | pass | match |
| F4-ITER-01, -02 | `mcp-default`, `mcp-passthroughOnError`, `mcp-passthroughOnError-moduleName` | same, `isError: false` | pass | match |
| F4-ITER-01, -02 | `cli-no-fallback` | refused, exit 1, no file | — | — |
| F4-ITER-03 | native Calor control | — | `Calor0209` at line 12 | — |

No surface reports `post-validation-fallback`. The R0 baseline for the same fixtures was
`compile-error:Calor0209` on MCP default and rescue-dependent `preserved-match` on the CLI.

Library and project-migration (`ProjectMigrator`, with and without `PassthroughOnError`) surfaces,
plus an interleaving fixture (producer/consumer order, `finally` on early `break`, deferred
argument validation), are covered by
`tests/Calor.Compiler.Tests/Migration/IteratorAccessorConversionTests.cs`.

## Not measured

- **F4-ITER-04** (the three historical FluentValidation modules with project references): not run.
- MCP `calor_migrate` and `calor_batch`, MSBuild and LSP surfaces: not run.
- macOS arm64 only.
