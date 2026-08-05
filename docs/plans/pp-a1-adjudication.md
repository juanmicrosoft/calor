# PP-A1 — CI adoption gates: **items 1–8 PASS, item 9 LAPSED**

**Adjudicated 2026-08-05**, against the list frozen at [`wedge-w1-prereqs.md`](wedge-w1-prereqs.md)
§3 ("frozen now, before any results exist"), carried unchanged into v0.12 by the fold-forward
decision. PP-A1 is a **v0.12.0 release gate**, orthogonal to Call S.

## The first version of this adjudication claimed all nine ✅. It was wrong.

Adversarial review refuted **item 6**, and the refutation was correct: **divergence D4 was a live
false-`Proven`-elides vector** — the exact thing item 6's frozen bar forbids — sitting unclosed in the
divergence table the item points at. Verified end-to-end before the fix, on a shipped CLI path:

```calor
§F{f001:Echo:pub} (str:s) -> str
  §Q (== s STR:"abc")
  §S (! (Equals result STR:"ABC" :ignore-case))
  §R s
```

`calor run` threw `ContractViolationException` — the postcondition is genuinely **false** at runtime.
`calor run --verify` printed `abc`: the solver translated `:ignore-case` as *ordinal*, returned
`Proven`, and the emitter **deleted the check**. Closed by refusal in #872.

**And then it happened again, to the fix.** Adversarial review of #872 found the *same vector still
open* on the spelling almost everyone actually writes: .NET resolves `String.StartsWith(String)`,
`EndsWith(String)` and `IndexOf(String)` to the **CurrentCulture** overload (unlike `Contains` and
`Equals`, which are ordinal), while the solver models all of them ordinally. `§S (! (starts result
STR:"\u200dabc"))` reproduced the identical throw-vs-print signature. Also closed in #872.

That second finding is the strongest evidence for this document's own thesis. The first fix was
narrow **because it was scoped to the row as written** rather than to the property the row is
supposed to protect. Closing "the divergence the table names" is not the same as closing "the ways a
false `Proven` can elide a check", and only the second is item 6's bar.

Two more rows did not survive contact either:

- **Item 4** was ✅ on evidence from `CompilationDriver`, while the **MSBuild task real projects
  actually build through** had drifted and was missing the output-content check entirely.
- **Item 9** was ✅ "by its own terms", where those terms had been quietly re-read from *"lands in the
  W2/W3 window"* into *"has a slot"*. The window has closed. The item lapsed.

The failure mode common to all three: **the evidence column was assembled by finding something that
supported each row, not by trying to break it.** Item 6 cited four divergence rows that *were* closed
(D6/D7/D8/D9) and never audited the rest of the table. That is confirmation, and it is how a release
gate gets marked green while a soundness hole ships.

## Item-by-item

| # | Requirement | Evidence | |
|---|---|---|---|
| 1 | Functional **published** `Calor.Sdk`; **M-A1 green in CI** | `sdk-package-consumer` runs `.github/scripts/test-sdk-package.sh`: packs `Calor.Sdk`, serves it from a **local feed**, and a template consumer restores/builds/tests against the **packaged artifact** — never a source-tree `ProjectReference` — with `CALOR_SDK_REQUIRE_ALL_RIDS=1` and the in-task Z3 canary | ✅ consumability; "published" ⏳ (see Scope) |
| 2 | Unmasked gates; **publish workflow test-gated** | `publish-nuget.yml`'s `publish` job declares `needs: [test, sdk-consumer]` — publishing cannot run unless both pass | ✅ |
| 3 | **Checksummed natives** on every publish path | `scripts/z3-upstream-4.15.7.sha256` + `verify_archive` at 3 sites in `download-z3.sh`; verification precedes extraction on every branch, fail-closed on missing manifest / missing entry / hash mismatch | ✅ |
| 4 | Complete **options hash** + **fail-closed enforcement** | Options hash: `BuildStateCache.ComputeOptionsHash`, consumed at `CompilationDriver.cs:113`. Fail-closed enforcement **completed here** — `CompileCalor` now matches the driver's output-content check | ✅ *(as of this PR)* |
| 5 | **Telemetry opt-in** default, stripped payloads, documented | `CalorTelemetry` activates **only** on `CALOR_TELEMETRY=1`; `--no-telemetry` / `CALOR_TELEMETRY_OPTOUT=1` force off; `AnonymizingTelemetryInitializer` strips payloads; `docs/telemetry.md` | ✅ |
| 6 | **Slice-1 soundness batch** — bar: **no known false-`Proven`-elides vector**, known set = divergence table + T1 | **Was FAIL.** D4 closed by refusal in #872; full row-by-row re-audit below | ✅ *(only as of #872)* |
| 7 | **T3 containment** — three surfaces gated | `format --write` → `Calor1346` unless `--experimental` / `CALOR_EXPERIMENTAL_FORMAT_WRITE=1`; LSP **formatting** and **rename** register only under `CALOR_LSP_EXPERIMENTAL=1`, read-only handlers unaffected. Two defects found and fixed here; the gate is now **tested** for the first time | ✅ |
| 8 | **#770 eject-contract** — documented degradation spec | `adoption-playbook.md` §"The eject story (tested)" — per-construct degradation table | ✅ |
| 9 | **#761 flip stance** — `EnableTypeChecking` default-on **lands in the W2/W3 window** with a CHANGELOG note | Window closed at v0.12 without the flip. Still `init`-default `false`, with **no CLI flag at all** | ❌ **LAPSED** |

## Item 6 — the row-by-row re-audit the first version owed

The bar is *known-divergence-free* and the known set is the **whole** divergence table. Every row,
with its disposition **and** whether it can mint a false `Proven` that elides:

| Row | Disposition | Elide-vector? |
|---|---|---|
| D1 narrow-type promotion | closed by refusal | no |
| D2 literals always signed 32-bit | truncation half closed (cache 1.7); within-range signedness context unmodeled | residual — see below |
| D3 Z3 strings cannot be null | open, tolerated | argued unreachable — see below |
| D4 non-ordinal comparison modes | **closed by refusal (#872)**, in two halves — explicit non-ordinal modes, and `StartsWith`/`EndsWith`/`IndexOf` with **no** mode (.NET resolves those to CurrentCulture) | no *(was: **yes**, twice)* |
| D5 `§S` holds only on normal return | exceptional paths → `assumed`, which never elides | no |
| D6 array element default i32 | adjudicated unreachable from the elision-relevant path | no |
| D7 user-type fields default i32 | closed by refusal | no |
| D8 contract division totalized | closed (side conditions; demote to `assumed`) | no |
| D9 `string.Replace` first-vs-all | closed by refusal | no |
| D10 mixed signed/unsigned | closed by modeling | no |
| D11 unmasked shift counts | closed by modeling | no |

**D3, argued rather than asserted.** For the null/empty divergence to elide a failing check, the
solver must prove a string non-empty where the runtime value is `null`. It cannot get there: Calor's
`str` is non-nullable by construction (`?T` is the nullable form, and `?str` is **not** in
`ModeledForms.ScalarTypes` — `NormalizeTypeName`'s default arm leaves the `?` intact rather than
folding it to `str`). With no precondition the solver cannot prove non-emptiness of an unconstrained
string at all; with one (`§Q (! (IsNullOrEmpty s))`), a `null` argument **fails that precondition at
runtime**, and precondition guards are never elided (D-G1.2). Same shape as D6's adjudication:
unreachable from the elision-relevant surface. **This is an argument, not a proof, and is recorded as
such.**

**A related finding, reported because it invites the opposite conclusion.**
`ContractTranslator`'s `StringInfo(bool IsNullable)` / `_stringInfo` is **written at five sites and
read at none**, and `isNullable` is never once passed `true`. It is inert. A maintainer looking for
D3's handling will find an apparatus that appears to track string nullability and does not — which is
worse than its absence. Either wire it up or delete it. Filed, not fixed here.

**D2** is the honest residual: the within-range signedness-context half is unmodeled and is not closed
by refusal. It was disclosed at the freeze and sits inside §2's recorded risk acceptance
("known-divergence-free is **weaker than differentially clean**"), which #779's differential suite is
the thing that actually closes. **Item 6 passes against its own bar; it does not certify D2.**

## Item 9 — LAPSED, with the cost measured rather than guessed

The frozen text, verbatim: *"`EnableTypeChecking` default-on lands in the W2/W3 window with a
CHANGELOG note; recorded here so the flip has a slot, not gate-blocking for W1 exit."*

The first version rendered that as "the requirement is that the flip be *scheduled*, not shipped" and
marked it ✅. That is a re-reading in the favourable direction: the text names a **window**, and the
window is what makes a schedule a commitment rather than an intention. W2/W3 are past; the flip did
not land; the item **lapsed**.

**What the flip would cost, measured.** Flipping the `init` default to `true` and running
`Calor.Compiler.Tests`: **92 failures / 6,158**. They are not stale fixtures — they are type-checker
**completeness gaps**, i.e. the compiler would start rejecting programs that are valid today:

| Diagnostic | Count | What it is |
|---|---:|---|
| `Calor0200` Unknown type | 36 | `object` ×31, `Type` ×6 — the checker's type table lacks them |
| `Calor0202` field access on non-record | 8 | trailing member-access chains not modeled |
| `Calor0250`/`0251` bind inference | 4 | |

So item 9 is blocked on **type-checker completeness**, not on remembering to schedule it. That is
useful: the dominant cause is a single missing type (`object`, 31 of 36), which makes the work look
tractable — but it is adopter-facing breakage and it is not v0.12 scope as frozen.

**This is recorded, not overridden.**

- The freeze's "not gate-blocking" clause is scoped to **W1 exit**. It is not evidence about a v0.12.0
  release, and silently extending it there would be the same move that produced the original ✅.
- **Whether the lapse blocks v0.12.0 is a maintainer decision**, not one this document may make by
  reinterpretation.
- If it is deferred again, the successor plan must name a **new window**, so it can lapse visibly a
  second time rather than accrue silently.

## Defects found by this audit and fixed here

**1. `format --write`'s refusal ignored `--format json`.** Every other exit from `format` emits the
envelope (schema v1.1, D1.3); the policy refusal wrote a bare line to stderr with **empty stdout** and
exited 1. An agent that asked for JSON got a parse failure instead of a policy decision — the one
message whose entire purpose is to be understood was the one it could not read. It now emits a proper
envelope carrying `Calor1346`. This is the same class as the early-exit defects already pinned in
`EnvelopeEarlyExitTests`, and the fix lives there.

**2. The containment had no test at all.** Nothing in the repo referenced `Calor1346`. Item 7 was
being adjudicated on code-reading alone. Three tests added: the JSON refusal, the text refusal, and —
the one that makes the other two mean something — a **control** proving the gate is an
*acknowledgement* rather than a broken command, by running the write path through with
`CALOR_EXPERIMENTAL_FORMAT_WRITE=1`.

**3. `docs/cli/format.md` documented `--write` as an ordinary option.** The earlier draft of this
audit fixed three sites; there were **twelve**, including the Quick Start block — the first thing a
reader copies — and the CI recipe, the editor-integration snippet, and the heal-in-place example. All
corrected, with a dedicated section stating the refusal, both escape hatches, the exit code, the JSON
behaviour, and the sibling `CALOR_LSP_EXPERIMENTAL` containment.

**4. `CompileCalor` trusted a `.g.cs`'s existence, not its content** (item 4). `CompilationDriver` has
always required the output's hash to match what the producing compile wrote, so a truncated or
hand-edited generated file is a miss; the MSBuild task — **the path real projects build through** —
only checked that the file existed, and reported stale bytes as "up-to-date". Ported, with a
regression test that corrupts the output and asserts recompilation (plus a control proving an
untouched output still skips). The `EffectSummary != null` skip condition was ported alongside it as
defence in depth: **not reachable through this task today**, since a null AST implies `HasErrors`,
which returns before caching.

## A near-miss worth recording

An intermediate step of this audit read `Program.cs`'s `WithHandler<FormattingHandler>()` /
`WithHandler<RenameHandler>()` lines *without their enclosing `if (experimentalWriteHandlers)` block*
and concluded item 7 was unmet — that a release blocker had failed. It had not. Grep-level evidence
about whether something is *gated* is unreliable by construction, because the gate is the enclosing
context, not the line.

## Verdict

**Items 1–8 PASS. Item 9 LAPSED and referred to the maintainer.**

Item 6 passes **only as of #872** — it did not pass when this document first claimed it did, and it
did not pass after the first half of that PR either.

**Stated plainly, because it bears on how much this verdict is worth:** two rounds of adversarial
review found two live soundness vectors behind an item that had been marked ✅. The bar is
*known-divergence-free*, and "known" is a claim about how hard anyone looked. The residual honest
statement is that no vector is known **to me** after this audit — not that none exists. §2's recorded
risk acceptance says the same thing in the freeze's own words: known-divergence-free is **weaker than
differentially clean**, and #779's differential suite is what would close the gap.

## Scope

- **PASS is against the frozen list only.** Items explicitly excluded there — coverage ratchets,
  mutation testing, LSP E2E revival, SBOM/hermetic builds, #782/#781 full fixes, #764 full lowering,
  D1/D2 numeric-semantics fixes — remain out and are not implied.
- **Item 1's "published" half cannot be evidenced pre-release.** CI proves the *packaged* SDK is
  consumable from a local feed, which is the substantive property; **"published" clears at the v0.12.0
  publish event itself and not before.** Recorded so the row is not read as more than it is.
- **PP-W5 is the other release gate and is NOT adjudicated here.** It requires a spend-authorized
  parity epoch (frozen A-1.4 tranche 1, restated additively at A-1.5.6) and is a maintainer decision.
  It may not be self-cleared.
