# W1 (#1429) Codex review, round 1 (invocation 1 of at most 4)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt: adversarial
review of `git diff origin/main...HEAD` at `1110dafa3`, focused on public claims matching behavior
(defaults, rescue/passthrough, counts, partial failures, envelopes, per-family native/preserved lists,
feature-check claims, version scope, records, test soundness). **Verdict:** REQUEST-CHANGES (4 MAJOR,
5 MINOR). Reduced independence (R0 §9).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R1-1 | MAJOR | `--format json` is not one JSON document on stdout with `--verbose` (converter progress `Console.WriteLine`s) | Fixed (docs): `cli/convert` and `cli/structured-output` state the exception; pinned as `cli-convert-json-verbose` in `W1-F6-rescue` (`stdoutIsOneJsonDocument: false`). The source fix is outside this docs-only PR; listed for the maintainer |
| R1-2 | MAJOR | MCP diagnostic locations: `calor_migrate` uses `perFile[].errors[]`/`warnings[]`, `calor_batch` `files[].issues[]`, not one `issues[]` | Fixed: one row per tool in `cli/structured-output`; `W1-F6-project` now pins each file's issue codes for both tools |
| R1-3 | MAJOR | Local-function slice omits ref returns, attributed parameters and the value-use restriction (`HasOnlyCheckedValueUses`) | Fixed: guide, `cli/convert` and both feature-check pages list them, with the `new Func<int,int>[] { L }` example |
| R1-4 | MAJOR | The F2 interpolation fragment is neither compiled nor compared; the required-marker set omits F4; "every example is compiled" overstated | Fixed: `w1-transcript-lines` marker (each fragment line must be a line of the CLI output); all five markers required; the guide, CHANGELOG and `cli/self-check` now say blocks are compared and complete programs compiled |
| R1-5 | MINOR | By-reference operands that run code and omitted-`in` call sites keep only the statement (`§RAW`, `in-parameter`), not the member | Fixed: guide bullet and table row; verified on the base with an MCP probe (both statements `§RAW`, feature `in-parameter`) |
| R1-6 | MINOR | Native hole list too broad (decimal/long/whole-number double literals, operators with call operands are preserved) | Fixed: guide and feature-check page follow `IsNativeHoleOperand` |
| R1-7 | MINOR | Multi-line array elements are preserved only in rectangular rows | Fixed on both feature-check pages |
| R1-8 | MINOR | `docs/cli/feature-check.md` categories still contradict the registry (out-var, in-parameter, linq, attributes, ...) | Fixed: the stale categorized copy is replaced by a pointer to `calor feature-check --list` plus the interop rows with registry levels and the known gaps |
| R1-9 | MINOR | The wording scan fails on the README, which quoted the forbidden phrase | Fixed: phrase removed from the README and the script docstring; scan reports 0 findings over 24 texts |

Codex also reported: no `src/` change; the default matrix, the marked fences, the project-refusal example
and the family statuses checked out.
