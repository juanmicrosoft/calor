# F1 #943 PR 2 (#1571) — Codex adversarial review, round 2

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9). Invocation 2 of the PR 2 budget.
- Input: `git diff origin/main...HEAD` at `5f3e614ef` (call statements with out declarations
  preserved; no synthesized initializer; round-1 dispositions). Codex ran MCP probes through the
  Release build; it modified no repository file.
- **Verdict: REQUEST CHANGES** (2 BLOCKING, 3 MINOR). Codex confirmed all six round-1 dispositions,
  the T1 seal and hashes, the ledger deltas, and the line ceiling.

## Findings and dispositions

| # | Finding (Codex) | Severity | Disposition |
|---|---|---|---|
| P2R2-1 | `int[] a = new[] { Set(out var n) };`: the bare `§B{~n:i32[]}` takes the following `§ARR{a:i32}` as its initializer, emitting `int[] n = new int[] { Set(out n) };`, which returns `1`, not `7`. Native, no losses. | BLOCKING | Fixed at the root. `Parser.ParseBindStatement` reads an initializer only when it starts on the binding's line; an expression start on a later line is a sibling statement, the rule `§C` inline arguments already follow. This also makes call statements with out declarations read correctly, so they convert natively again (the round-1 preservation is removed). Witnesses: `P2-out-var-array-declaration` (fails on `main` on all 5 surfaces), `P2-OUTVAR-CALL-STATEMENT` (6 evidence surfaces), `BareBinding_DoesNotTakeASiblingAsItsInitializer`. The only committed `.calr` that relied on the old reading, the #1104 cycle fixture, now writes its call on the binding's line (its cycle tests pass unchanged). |
| P2R2-2 | `x = y = Set(out var n);` emits `y = Set(out n); x = Set(out n);` and runs the call twice (`2:2`, not `1:1`). | BLOCKING | Fixed for out declarations: a statement with an out declaration and a nested assignment preserves its member (`out-var`). Witnesses: `P2-out-var-chained-assignment` (fails on `main` on all 5 surfaces), `P2-OUTVAR-CHAINED`. The duplication is a pre-existing converter defect for any chained assignment with a side-effecting right side (`int z = y = G();` calls `G` twice); it is recorded as a residual, outside F1. |
| P2R2-3 | Method-generic `ref T`/`in T` arguments are reported as `out-parameter` by `LowerOutDeclarations`. | MINOR | Fixed: reported under the argument's keyword. Two attribution rows added. |
| P2R2-4 | The `in-parameter` text says an operand that "runs code" keeps the statement, but a property-getter index (`a[I]`) converts natively. | MINOR | Fixed: the text names the syntactic shapes (a call, creation, assignment or `++`/`--`, or an index into a non-array). The getter case keeps evaluation order (nothing is hoisted) and is listed as a residual. |
| P2R2-5 | The CHANGELOG corpus figure (34,361) predates PR 2. | MINOR | Fixed: 34,358 (2.3%), the regenerated ledger. |

## Effect of the round on the PR

Non-test lines 48 of the ~51 left (src 33, CHANGELOG 15). Ledgers regenerated from `main`
(`RoslynSelectedAttempted` 34,358; opaque boundaries 148, expressions 7,746), F1 evidence re-run
(four P2 rows), T1 amendment 1.0.1 re-sealed (`598a5d7c…`).
