# Fix #1524/#1528 — Codex adversarial review, round 2

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt plus `git diff origin/main...HEAD` on stdin.
- Reviewed commit: `06a4b30cd` (round-1 fixes `cd802cdeb` plus a CRLF restore of `CalorEmitter.cs`).
- Verdict: **REQUEST CHANGES** (3 major-silent). Codex traced the code; it did not run probes.
- Codex confirmed that the round-1 fixes for collection-specialization gating, direct interpolation, `default(float)`/`default(decimal)` and attribute formatting address their witnesses, found no regression in numeric formatting or raw-string scanning, and did not reopen round-1 finding 6.

## Findings and dispositions

| # | Finding (repro) | Severity | Disposition |
|---|---|---|---|
| 1 | The rollback used a queue count. Converting a block lambda (`F = () => { ... }`) calls `ConvertBlock`, which clears `_pendingStatements`, so after `Use(ignored = 0, ignored = 0, new P { F = () => {...}, A = i++, B = j++ })` the rollback kept one `i++` hoist and the preserved C# ran it again (110 → 211). New in this PR. | major (silent) | Fixed. The rollback takes a snapshot of the queue (`SnapshotPendingStatements`), detects any change by reference (`PendingStatementsChanged`), and restores the snapshot. Test row: `UseF(Ignored = 0, Ignored = 0, new PF { F = () => { return 0; }, A = Ci++, B = Cj++ })` → `110`. |
| 2 | Hoists made by the Calor emitter, not the visitor: in `new P { A = new Q().A }` the emitter hoists the `§NEW{Q}` receiver of `.A` to a `§B{~_hoistN}` line, so `Q()` ran before `P()` (`"pq"` → `"qp"`). Pre-existing on main. | major (silent) | Fixed. Converted `NewExpressionNode`s with initializers carry their original C# (`CSharpSource`). `CalorEmitter.Visit(NewExpressionNode)` counts `HoistToTempVar` calls while it emits the initializer values; if any happened, it removes the lines hoisted for this creation (arguments included), records an `EmitterFallback` loss for `object-initializer`, and emits `§CS{original}`. Hoisted block lambdas do not count: creating a delegate has no observable effect. Test row: `new P { A = new Q2().A }` → `kq5`. |
| 3 | Target-typed constructor arguments: `new(new List<int> { 5 }) { A = 1 }` passed the argument through `ConvertBlockLevelCollectionToNew`, giving `new List<int>(5)` (capacity, no element). Pre-existing on main. | major (silent) | Fixed in both creation paths. A constructor argument that converts to a block-level collection now preserves the enclosing creation (feature `object-initializer` when the creation has an initializer, otherwise the new registry entry `collection-initializer`). Tests: `q = new(new List<int> { 5 }) { A = 1 }` → `k7kl11`; `new P(new List<int> { 5 })` → `kl1` with a `collection-initializer` loss. |

## Negative control

The four new rows were run against the round-1 sources (`cd802cdeb`): all four fail and the other 44 pass.
