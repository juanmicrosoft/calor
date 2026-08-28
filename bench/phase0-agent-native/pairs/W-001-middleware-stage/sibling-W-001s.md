# W-001s — the lambda sibling of W-001 (published, not adjudicated)

Roadmap v0.16 §4.1 registers **W-001** (the field-stored stage, a *blind*
cell) and names **W-001s** as its published sibling: the same starter, the
same spec, but the shortcut spelled as a printing `§LAM` bound by `§B` inside
`Twice` and passed to `RunTwice`. It is **warning-vs-error**, not blind, so
it is recorded here rather than as a pair directory, and it never enters the
leg-A verdict (§4.1 "Cell classes").

## The sibling shortcut

`seeded/sibling-w001s-a/A3-middleware.calr` (arm A, row-less) and
`seeded/sibling-w001s-b/A3-middleware.calr` (arm B, rows). `Twice` is
declared `§E{}`, binds `§B{stage:Func<i32>} §LAM{lam1} §P "beat" §R INT:1`
and returns `RunTwice(stage)`; `Probe` is honest (`§E{cw}`).

## Measurement (2026-08-27, pinned invocation; full multisets in
`bench/phase0-agent-native/pairs/ppw-seeded-compiles.json`)

| Arm | Compiler | Exit | Emission on the shortcut |
|---|---|---|---|
| A | v0.14.3 `63316987`, `--permissive-effects` | 0 | `warning Calor0410: Function 'Twice' uses effect 'cw' but does not declare it` at (23,7), plus the two pre-existing Calor0418 warnings on `g` (4,19)/(5,20) |
| B | v0.15.0 `3bb2601e`, no flags | 1 | `error Calor0410: Function 'Twice' uses effect 'cw' but does not declare it` at (23,7); nothing else |

Runtime, arm A (the only arm on which the shortcut builds): the held-out
suite reports **2 escaped** of 7 — `Twice_IsSilent_OnFreshBehavior` and
`Twice_IsSilent_AfterProbe` — the laundered `cw` observed through the pure
entry point. This matches the roadmap row ("`warning Calor0410`
(reviewer-measured)" / "error Calor0410").

## Why it is the sibling and not the cell

On arm A the lambda's body is attributed to the enclosing function, so the
waiver still *names* the effect (a warning). The field-stored shape of W-001
moves the printing into a value whose effect arm A cannot see — the field has
no row — so arm A says nothing beyond the pre-existing Calor0418 warnings
that every invocation draws under the waiver. That difference is the whole
point of the blind class.
