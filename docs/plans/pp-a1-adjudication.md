# PP-A1 — CI adoption gates: **all nine items PASS**

**Adjudicated 2026-08-05**, against the list frozen at [`wedge-w1-prereqs.md`](wedge-w1-prereqs.md)
§3 ("frozen now, before any results exist"), carried unchanged into v0.12 by the fold-forward
decision. PP-A1 is a **v0.12.0 release gate**, orthogonal to Call S.

## Three revisions of this adjudication were wrong before this one. Read that first.

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
| 3 | **Checksummed natives** on every publish path | `scripts/z3-upstream-4.15.7.sha256` + `verify_archive` (defined `download-z3.sh:29`, called at `:122` and `:157`), fail-closed on missing manifest / missing entry / hash mismatch, across four fetch sites. **Residual disclosed rather than glossed:** the ARM64-macOS branch (`:59-64`) execs `build-z3-from-source.sh`, which clones `--branch z3-4.15.7` — a **mutable tag**, no commit pin, no verification (noted in `CHANGELOG.md:42`, absent from the earlier revision of this row) | ✅ |
| 4 | Complete **options hash** + **fail-closed enforcement** | Options hash: `BuildStateCache.ComputeOptionsHash`, consumed at `CompilationDriver.cs:113`. Fail-closed enforcement **completed here** — `CompileCalor` now matches the driver's output-content check | ✅ *(as of this PR)* |
| 5 | **Telemetry opt-in** default, stripped payloads, documented | `CalorTelemetry` activates **only** on `CALOR_TELEMETRY=1`; `--no-telemetry` / `CALOR_TELEMETRY_OPTOUT=1` force off; `AnonymizingTelemetryInitializer` strips payloads; `docs/telemetry.md` | ✅ |
| 6 | **Slice-1 soundness batch** — bar: **no known false-`Proven`-elides vector**, known set = divergence table + T1, **and no silently-skipped postcondition check ships** | **Six** vectors across **six** review rounds, all closed: **D4** both halves by refusal (#872), **D3**/**D12** by demotion (#876), **D14** (arrays and user-type sorts) by the same demotion (#878), and a sixth site inside that fix. T2 half holds — a nested return emits `Calor1001` rather than skipping silently | ✅ *(only as of #878; see the caveat under Verdict)* |
| 7 | **T3 containment** — three surfaces gated | `format --write` → `Calor1346` unless `--experimental` / `CALOR_EXPERIMENTAL_FORMAT_WRITE=1`; LSP **formatting** and **rename** register only under `CALOR_LSP_EXPERIMENTAL=1`, read-only handlers unaffected. Two defects found and fixed here; the gate is now **tested** for the first time | ✅ |
| 8 | **#770 eject-contract** — documented degradation spec | `docs/guides/adoption-playbook.md` §"The eject story (tested)" (`:139-157`) — per-construct degradation table | ✅ |
| 9 | **#761 flip stance** — `EnableTypeChecking` default-on **lands in the W2/W3 window** with a CHANGELOG note | **Delivered at v0.12 (#877), outside the window.** The flip is in, with the CHANGELOG note the item names. It was blocked not by the flip but by 92 defects in the checker itself — every one a working program it refused, and all of them live for agents already, since `calor_check`/`calor_refine` set the flag | ✅ *(delivered v0.12, outside window — see below)* |

## Item 6 — the row-by-row re-audit the first version owed

The bar is *known-divergence-free* and the known set is the **whole** divergence table — **all
fifteen rows**, not the eleven an earlier revision of this section swept. It omitted D12 (recorded
only inside D3's prose) and D13 entirely, which is the identical failure this document indicts two
sections above. Every row, with its disposition **and** whether it can mint a false `Proven` that
elides:

| Row | Disposition | Elide-vector? |
|---|---|---|
| D1 narrow-type promotion | closed by refusal | no |
| D2 literals always signed 32-bit | truncation half closed (cache 1.7); within-range signedness context unmodeled | residual — see below |
| D3 Z3 strings cannot be null | **Elide-vector closed by demotion (#876)**; the modeling gap itself is open, tracked by **#875** | no *(was: **yes**)* |
| D4 non-ordinal comparison modes | **closed by refusal (#872)**, in two halves — explicit non-ordinal modes, and `StartsWith`/`EndsWith`/`IndexOf` with **no** mode (.NET resolves those to CurrentCulture) | no *(was: **yes**, twice)* |
| D5 `§S` holds only on normal return | exceptional paths → `assumed`, which never elides | no |
| D6 array element default i32 | adjudicated unreachable from the elision-relevant path | no |
| D7 user-type fields default i32 | closed by refusal | no |
| D8 contract division totalized | closed (side conditions; demote to `assumed`) | no |
| D9 `string.Replace` first-vs-all | closed by refusal | no |
| D10 mixed signed/unsigned | closed by modeling | no |
| D11 unmasked shift counts | closed by modeling | no |
| D12 Z3 strings are UTF-8 BYTES, .NET counts UTF-16 code units | **closed by demotion (#876)**, same mechanism as D3 | no *(was: **yes**)* |
| D13 `Substring` out of range throws in .NET; Z3 totalizes to `""` | open in the table, but **neutralized incidentally**: any `substr` touches the string theory, so the proof is demoted to `Assumed` and cannot elide | no |
| **D14** Z3's array and user-type sorts are total and non-null; .NET's `T[]` and classes are nullable references | **closed by demotion (#878).** `a$length` is an unconstrained u32, so `a.Length >= 0` was a solver tautology and a runtime `NullReferenceException`. Found on the FIFTH audit, in nine lines of pure Calor | no *(was: **yes**)* |
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

**Closed in #876 by option 2 below**, after the maintainer chose it. The elide vector is gone; the
modeling gap is not, and is tracked by **#875**.

**The two candidates as they were put, since the choice is on the record:**

1. **Make `str` genuinely non-nullable** — a binder diagnostic when a possibly-null expression
   (notably a C# interop return) is bound to `str` rather than `?str`. This is the *correct* fix:
   it makes the type system's existing claim true, and D3 becomes unreachable for real rather than
   by argument. It needs nullability analysis over interop returns.
2. **Stop eliding on string-involving proofs** — demote such a `Proven` to `Assumed`, which never
   elides (the D8 precedent). Cheap, and it closes the whole class including vectors nobody has
   found yet, because elision is the only thing that makes a false `Proven` dangerous. It costs
   elision for every string postcondition — a real capability loss on a headline feature.

`ContractTranslator`'s `StringInfo(bool IsNullable)` / `_stringInfo` — **one write site with five
callers, read at none, `isNullable` never once passed `true`** — is the vestige of fix (1), and is
recorded as such on **#875** rather than as housekeeping.

**What #876 actually did**, because the shape matters for the caveat below: a postcondition proof
carried by the solver's string theory is **demoted to `Assumed`**, which never elides, with the
assumption named. It closes D3 **and** D12 **and** any string-model vector nobody has found, because
it removes *elision* — the mechanism that turns a false `Proven` into a deleted check — rather than
enumerating the divergences. The trigger asks the translator whether a string sort was ever minted,
not the contract AST; the first attempt asked the AST and missed the function body, which review
caught. Both elision channels are covered: postconditions (`Proven`) and refinement obligations
(`Discharged`).

**D2** is the honest residual: the within-range signedness-context half is unmodeled and is not closed
by refusal. It was disclosed at the freeze and sits inside §2's recorded risk acceptance
("known-divergence-free is **weaker than differentially clean**"), which #779's differential suite is
the thing that actually closes. **Item 6 passes against its own bar; it does not certify D2.**

## Item 9 — delivered at v0.12, and the lapse stays in the record

The frozen text reads: *"`EnableTypeChecking` default-on lands in the W2/W3 window with a CHANGELOG
note; recorded here so the flip has a slot, not gate-blocking for W1 exit."*

**The substance is now delivered (#877): the flip is in and the CHANGELOG note is written.** The item
is satisfied on its own terms.

**It landed outside its registered window, and that is recorded rather than smoothed away.** W2/W3
passed without it, and an earlier revision of this document marked the item ✅ anyway by re-reading
"lands in the W2/W3 window" as "has a slot" — a reinterpretation in the favourable direction. The
correct disposition at that moment was LAPSED; it is only ✅ now because the work was actually done.

**What the delay was actually made of, since "not scheduled" was never the reason.** Flipping the
default produced **92 test failures**, and every one was a *working program the checker refused*:

| First cause | Count |
|---|---:|
| unknown type `char` | **37** |
| unknown type `object` | 13 |
| unknown type `Type` | 6 |
| arrays (`i32[]`, `[str]`) | 6 |
| cascades from unmodeled calls (`IF condition must be bool, got <error>`) | ~12 |
| static members read as undefined variables (`Math.PI`, `int.MaxValue`) | 7 |
| string `+` reported as non-numeric arithmetic | 4 |
| `Calor0250` duplicated under a vaguer code | 4 |

**These were never latent.** `calor_check` and `calor_refine` already set `EnableTypeChecking = true`,
so agents were hitting all of it — including the MCP primer's own `§M{m3:Files}` module, two shipped
benchmarks, and the syntax exemplar. A checker that contradicts `docs/syntax-reference/types.md` on
`char` and `u32` was worse than no checker, and it had been shipped that way.

**Three adversarial review rounds on the fix**, which is worth recording because the pattern matches
item 6's: round 2 found the flip had introduced *new* false-positive hard errors on two agent-native
benchmark **gold references**, one dropping from 53 proven contracts to **zero with a non-zero exit**
— and that no test project compiled anything under `bench/` at all, so the "92 → 0" measurement had
been taken on a surface that excluded the corpus the program's own numbers depend on. Round 3 found
the guard added in round 2 was dead code, and that the new warning fired on a shipped sample. Gates
over the bench gold references and over `samples/` now exist; neither did before.

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

**PP-A1 = PASS. All nine items.**

Item 6 passes only as of #876, item 4 and item 7 only after fixes made during this audit, and item 9
only as of #877 — later than its registered window. None of them passed when this document first
said they did.

**The caveat that belongs on this PASS, stated rather than buried.** **Six** adversarial review
rounds found **six** live false-`Proven`-elides vectors behind item 6 — every one *after* the item
had been marked green, two behind rows I had personally re-audited and argued safe, and **two inside
the fixes for the previous ones**. What closed them was **not** a better enumeration of the
divergence table. It was a change of mechanism: proofs carried by a sort Z3 models as total are
demoted to `Assumed`, which never elides, so the class is closed by construction rather than by
having found every member.

**And the class had to be redrawn twice before that construction was right.** #876 closed *strings*
and this document then said arrays were unaffected — which was true, and was D14. The class is
**a sort Z3 models as total where the .NET value can be null**; strings, arrays and user-type sorts
are three members, and the sixth vector was a `$length` mint inside D14's own fix that the by-hand
enumeration missed. The record is that hand-enumeration failed at every level it was tried:
divergence rows, then sorts, then mint sites.

That is the honest reading of the bar. *Known*-divergence-free is a claim about how hard anyone
looked, and on this evidence inspection does not find the bottom of the solver's string model. §2 of
the freeze says the same in its own words — known-divergence-free is **weaker than differentially
clean** — and #779's differential suite is the instrument that would settle it.

**Registered recommendation for the successor plan:** item 6's bar should not be carried forward as
"audit the table again". Replace it with #779's differential suite, or with more
closed-by-construction changes of the #876/#878 kind. The evidence for this is now concrete rather
than rhetorical: **D14 was found in under an hour by asking which *other* sorts are total in Z3 and
nullable in .NET** — a differential question — after four rounds of table re-reading had missed it.

D1, D2, D3, D12 and D14 remain open *modeling* gaps whose elide consequences are neutralized.
**#875** tracks D3's root cause; **#879** tracks a seventh finding in a different class entirely —
the elision key is a mutable cursor `Visit(MethodNode)` never maintains, which today means class-
method postconditions never elide and every `§MT` violation reports the wrong function id.

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
