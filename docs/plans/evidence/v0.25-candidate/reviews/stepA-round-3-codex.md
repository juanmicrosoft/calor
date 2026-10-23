# 0.25 Step A amendment PR (#1610): Codex round 3

- **Invocation 3 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `28f0fc21d` (the round-2 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 1 MAJOR, 1 NIT.

Codex confirmed: the earlier reproducers are addressed; both protocol validators and 36 focused Python controls
pass; the attribution matches all five first-parent merges; the runtime checks discriminate; the changelog sections
are byte-identical; 14,691 + 3 = 14,694.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | Re-registered counts were not type-checked consistently: in Python `0 == False`, so a boolean measurement passed; in C# a matching non-numeric value passed because `Int()` falls back to 0. | Fixed in both: every count (before, after, the measured or recorded value, each delta) must be a JSON integer, never a boolean or string, before any comparison or sum (`IsInteger`, `is_int`). Controls: T1 non-integer count (re-hashed measurement), gate boolean count (re-sealed). |
| 2 | NIT | The plan document's header still said scope 1.4.0. | Fixed: 1.5.0. |

Test manifest 14,694 -> 14,696.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. **MAJOR — `scripts/verify_release_adjudication.py:796`; `tests/Calor.Compiler.Tests/InteropScope/T1RegisterValidator.cs:129`**: Re-registration counts lack consistent integer validation. Reproducer: change the retained measurement's `incompleteDiagnostics` from `0` to `false`, update its hash, recompute the approval seal and contract hash. Python's full `check_scope_packet` returns **zero errors**, because `0 == False`; C# rejects it. Conversely, C#'s `JsonValue` check and `Int()` fallback permit matching nonnumeric measurement/register values when the arithmetic totals zero. Require integer types before comparison or arithmetic in both implementations; add resealed boolean/string parity controls.

2. **NIT — `docs/plans/v0.25-interop-scope-and-baseline.md:3`**: The header still declares scope **1.4.0**, while this amendment and `scope.json` declare **1.5.0**. Update the header.

The prior reproducers are addressed. Both protocol validators and 36 focused Python controls pass; attribution matches all five first-parent merges; runtime checks discriminate; changelog sections are byte-identical; **14,691 + 3 = 14,694**. .NET and tempfile-dependent tests could not run under the read-only sandbox. No files modified.
```
