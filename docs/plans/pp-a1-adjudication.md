# PP-A1 — CI adoption gates: **FAIL** (item 6), item 9 LAPSED

**Adjudicated 2026-08-05**, against the list frozen at [`wedge-w1-prereqs.md`](wedge-w1-prereqs.md)
§3 ("frozen now, before any results exist"), carried unchanged into v0.12 by the fold-forward
decision. PP-A1 is a **v0.12.0 release gate**, orthogonal to Call S.

## Two revisions of this adjudication claimed a PASS. Both were wrong.

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
| 2 | Unmasked gates; publish workflow test-gated; **test-manifest honesty** | All three legs, the third of which an earlier revision restated away: (a) `test.yml:408-430` scans `samples/` unmasked, and `:406` runs `Calor.Ids.Tests`; (b) `publish-nuget.yml`'s `publish` declares `needs: [test, sdk-consumer]`, both keys exist verbatim, and neither the job nor its steps carry an `if:`/`continue-on-error:` that could undermine it; (c) `Calor.Performance.Tests` is wired to nightly `performance.yml:29`. **Residual disclosed:** `Calor.Ids.Tests` is in `test.yml` but **not** in the publish gate | ✅ |
| 3 | **Checksummed natives** on every publish path | `scripts/z3-upstream-4.15.7.sha256` + `verify_archive` (defined `download-z3.sh:29`, called at `:122` and `:157`), fail-closed on missing manifest / missing entry / hash mismatch, across four fetch sites. **Residual disclosed rather than glossed:** the ARM64-macOS branch (`:59-64`) execs `build-z3-from-source.sh`, which clones `--branch z3-4.15.7` — a **mutable tag**, no commit pin, no verification (noted in `CHANGELOG.md:31`, absent from the earlier revision of this row) | ✅ |
| 4 | Complete **options hash** + **fail-closed enforcement** | Options hash: `BuildStateCache.ComputeOptionsHash`, consumed at `CompilationDriver.cs:113`. Fail-closed enforcement **completed here** — `CompileCalor` now matches the driver's output-content check | ✅ *(as of this PR)* |
| 5 | **Telemetry opt-in** default, stripped payloads, documented | `CalorTelemetry` activates **only** on `CALOR_TELEMETRY=1`; `--no-telemetry` / `CALOR_TELEMETRY_OPTOUT=1` force off; `AnonymizingTelemetryInitializer` strips payloads; `docs/telemetry.md` | ✅ |
| 6 | **Slice-1 soundness batch** — bar: **no known false-`Proven`-elides vector**, known set = divergence table + T1, **and no silently-skipped postcondition check ships** | D4 closed by refusal in #872 (both halves). **D3 is a live vector** — reproduced end-to-end, below. T2 half holds (a nested return emits `Calor1001` rather than skipping silently) | ❌ **FAIL** |
| 7 | **T3 containment** — three surfaces gated | `format --write` → `Calor1346` unless `--experimental` / `CALOR_EXPERIMENTAL_FORMAT_WRITE=1`; LSP **formatting** and **rename** register only under `CALOR_LSP_EXPERIMENTAL=1`, read-only handlers unaffected. Two defects found and fixed here; the gate is now **tested** for the first time | ✅ |
| 8 | **#770 eject-contract** — documented degradation spec | `docs/guides/adoption-playbook.md` §"The eject story (tested)" (`:139-157`) — per-construct degradation table | ✅ |
| 9 | **#761 flip stance** — `EnableTypeChecking` default-on **lands in the W2/W3 window** with a CHANGELOG note | Window closed at v0.12 without the flip. Still `init`-default `false`, with **no CLI flag at all** | ❌ **LAPSED** |

## Item 6 — the row-by-row re-audit the first version owed

The bar is *known-divergence-free* and the known set is the **whole** divergence table. Every row,
with its disposition **and** whether it can mint a false `Proven` that elides:

| Row | Disposition | Elide-vector? |
|---|---|---|
| D1 narrow-type promotion | closed by refusal | no |
| D2 literals always signed 32-bit | truncation half closed (cache 1.7); within-range signedness context unmodeled | residual — see below |
| D3 Z3 strings cannot be null | **OPEN — live vector, reproduced** | **YES** — see below |
| D4 non-ordinal comparison modes | **closed by refusal (#872)**, in two halves — explicit non-ordinal modes, and `StartsWith`/`EndsWith`/`IndexOf` with **no** mode (.NET resolves those to CurrentCulture) | no *(was: **yes**, twice)* |
| D5 `§S` holds only on normal return | exceptional paths → `assumed`, which never elides | no |
| D6 array element default i32 | adjudicated unreachable from the elision-relevant path | no |
| D7 user-type fields default i32 | closed by refusal | no |
| D8 contract division totalized | closed (side conditions; demote to `assumed`) | no |
| D9 `string.Replace` first-vs-all | closed by refusal | no |
| D10 mixed signed/unsigned | closed by modeling | no |
| D11 unmasked shift counts | closed by modeling | no |
| **T2** (#764) — the bar's second clause, *no silently-skipped postcondition check ships* | Holds: a nested return emits `warning Calor1001: Postcondition runtime checks for '…' were NOT emitted` | no |

**D3 is live, and my argument that it was not is the single worst thing in this document's history.**

The previous revision argued D3 unreachable: Calor's `str` is non-nullable by construction, so the
solver could never prove a string non-empty where the runtime value is `null`. Adversarial review
executed the argument instead of reading it, and every leg failed:

- **`str` is not non-nullable in practice.** `§B{bad:str}` binds `Environment.GetEnvironmentVariable`'s
  `null` with **zero diagnostics**. The `?T` form exists; nothing enforces it.
- **No precondition is needed.** The divergence bites in the direction the argument never
  considered: Z3 makes `len(s) = 0 ⟺ s = ""` a tautology, while in C# `null` satisfies
  `IsNullOrEmpty` and `null == ""` is **false**.

Reproduced end-to-end on the same shipped path as D4, in pure Calor:

```calor
§F{f001:Echo:pub} (str:s) -> str
  §E{}
  §S (|| (! (isempty result)) (== result STR:""))
  §R s
```

| command | result |
|---|---|
| `calor run` | **throws** — `!string.IsNullOrEmpty(__result__) \|\| __result__ == ""` is genuinely false |
| `calor run --verify` | prints `survived` — **check deleted** |
| `calor verify` | `Proven Rate: 100.0%` |

**And it is not one form.** Independently confirmed a second shape: `§S (>= (len result) INT:0)` —
a Z3 tautology — **crashes with a NullReferenceException** without `--verify` and **survives** with
it. So this is not closable by refusing one operation the way D4 and D9 were: **Z3's string sort has
no null, so every total axiom of its string theory is unsound the moment a `str` holds one.**

**Two candidate fixes, neither a drive-by, and the choice is a product decision:**

1. **Make `str` genuinely non-nullable** — a binder diagnostic when a possibly-null expression
   (notably a C# interop return) is bound to `str` rather than `?str`. This is the *correct* fix:
   it makes the type system's existing claim true, and D3 becomes unreachable for real rather than
   by argument. It needs nullability analysis over interop returns.
2. **Stop eliding on string-involving proofs** — demote such a `Proven` to `Assumed`, which never
   elides (the D8 precedent). Cheap, and it closes the whole class including vectors nobody has
   found yet, because elision is the only thing that makes a false `Proven` dangerous. It costs
   elision for every string postcondition — a real capability loss on a headline feature.

`ContractTranslator`'s `StringInfo(bool IsNullable)` / `_stringInfo` — **one write site with five
callers, read at none, `isNullable` never once passed `true`** — is the vestige of fix (1). The
previous revision filed it as housekeeping ("wire up or delete"). It should be reclassified: it is
the unbuilt half of the fix for a live soundness hole.

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
`Calor.Compiler.Tests`: **92 failures / 6,160**. Tallied by **first cause per failing test** — an
earlier revision of this document counted diagnostic occurrences across the whole log instead, which
double-counted `object`, missed `char` entirely, and produced a table whose rows summed to 48 of 92:

| First cause | Count |
|---|---:|
| `Calor0200` unknown type `char` | **37** |
| `Calor0200` unknown type `object` | 13 |
| `Calor0200` unknown type `Type` | 6 |
| `Calor0200` unknown type `i32[]` | 4 |
| `Calor0200` unknown type `[str]` | 2 |
| `Calor0200` unknown type `u32` | 1 |
| `Calor0200` unknown type, dotted receiver (`price`, `result`) | 2 |
| no `Unknown type` — assertion failures, `Calor0202`/`0250`/`0251`, indentation | 27 |
| **Total** | **92** |

Two corrections to the earlier characterization, both in the direction that made the work look
easier than it is:

- **`char` is the dominant cause, not `object`**, and it drives the whole `StringOperationsE2ETests`
  cluster. The earlier claim that "the dominant cause is a single missing type (`object`, 31 of 36),
  which makes the work look tractable" was the one sentence a maintainer would act on, and it was
  not supported.
- **`u32` and `i32[]`/`[str]` being unknown means the checker's table is missing core documented
  Calor scalar and array types**, not just BCL escapes.
- **The residue is not all "completeness gaps".** It contains checker *defects*: string `+`
  concatenation rejected as non-numeric (`Arithmetic operators require numeric operands, got str`,
  ×4), `Logical operators require bool operands` (×7), dotted static members unresolved
  (`Math.PI`, `int.MaxValue`, `StringComparison.Ordinal`, `System.Environment.NewLine`, ×8), and
  typed literals unrecognized in loop bounds (`Undefined variable 'INT'`, ×2).

Also correcting the diagnostic labels: `Calor0200` is **`UndefinedReference`** and `Calor0202` is
**`TypeMismatch`** (`Diagnostics/Diagnostic.cs:106,108`); the earlier table called them "unknown
type" and "field access on non-record".

So item 9 is blocked on **type-checker completeness and correctness**, not on remembering to
schedule it, and it is a larger job than the first tally implied.

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

**2. The containment had no test at all.** No *test* anywhere referenced `Calor1346` (`docs/cli/structured-output.md:182` did, so the stronger "nothing in the repo" phrasing an earlier revision used was wrong). Item 7 was
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

**PP-A1 = FAIL.** Item 6 does not meet its frozen bar: **D3 is a known false-`Proven`-elides vector
and it is live**, on the shipped `calor run --verify` path, with a reproduction of the same shape as
the D4 finding that caused this document to be rewritten in the first place.

Items 1–5, 7 and 8 pass. Item 9 is **LAPSED** and referred to the maintainer.

**This blocks v0.12.0**, which cannot ship with PP-A1 failing. Two decisions are now the
maintainer's, not this document's:

1. **How to close D3** — the non-nullable-`str` fix or the stop-eliding-on-strings fix above.
2. **Whether item 9's lapse independently blocks the release.**

**On what "known-divergence-free" is worth.** Three adversarial review rounds have now found three
live vectors behind this one item — D4's explicit-mode half, D4's no-mode half, and D3 — each time
after the item had been marked ✅. Two of the three were behind rows I had personally re-audited.
"Known" is a claim about how hard anyone looked, and the honest statement is that this bar cannot be
carried by inspection. §2 of the freeze says the same thing in its own words: known-divergence-free
is **weaker than differentially clean**, and #779's solver-vs-runtime differential suite is the
instrument that would actually settle it. **The recommendation this audit ends on is that item 6's
bar should not be re-asserted by another reading of the table — it should be replaced by the
differential suite.**

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
