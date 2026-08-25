# Effect-rows emitter spike — running notes

Roadmap §4.1 term 1; the specification is `docs/design/effect-rows-in-the-type-system.md`
(Draft v4, §12). These notes are written **as the spike runs**, so an interrupted run
loses only the step in progress. Nothing here is a conclusion until it carries an
executed command.

Branch: `spike/effect-rows-emitter`, cut from the design-doc branch
(`docs/effect-rows-design-draft-v1`, tip `5d99f900`).

## Step log

- **S0** branch cut; Z3 assets downloaded; `dotnet build src/Calor.Compiler` green;
  `bench/corpus/MediatR` submodule initialised at the pinned SHA
  `fb309026775ef953a64fb5339d074426c1ad2c37` (A2 lives there and `git clone` does not
  init it — CLAUDE.md).
- **S1** prototype, part 1 — syntax. `Diagnostic.cs` gains Calor0404/0405/0424/0425;
  `Effects/EffectSet.cs` gains `RowFit` + `EffectRow` (the three-point lattice, the
  nine-cell `fits`, the join, `ExtraEffects`, `ToDisplayString`) — **deliberately in an
  existing file** rather than §9's planned `Effects/EffectRow.cs`, so the spike adds
  **zero new files under `src/`** and needs no Calor-first allowlist-precursor PR;
  `ParameterNode.Row` / `ClassFieldNode.Row`; the `eff` branch in
  `ParseOptionalTypeParameterList` with its one-token lookahead (Z4's `<eff>` guard) and
  §7.2(c)'s taxonomy-collision ban; the line-adjacency row at **position 5** (inline
  parameter) and **position 8** (field), each with the Calor0405 parser-stage recovery.
  Effect variables are recorded as **binder indices**, never as names — that is what
  makes the interface/implementation comparison alpha-equivalent (§7.3, W1c).
  `GetDeclaredEffects` skips the reserved key, so a variable never leaks into a concrete
  `EffectSet`. Build green, 0 warnings.
- **S2** prototype, part 2 — checking. `EffectEnforcementPass` gains `GetDeclaredRow` /
  `GetAnnotationRow`, `ResolveLocalValueRow`, `ChargeInvokedRow`, `ReportRowUnknown`,
  and `CheckRowedArguments` (§6.2 sites 2 and 6 in one pass). `CheckEffectVariance`'s
  two `IsSubsetOf` calls become calls to the shared `EffectRow.Fits` — sites 4 and 5
  now share one relation with site 2, which is R2's "no carve-out" bar.
  **The whole prototype is strictly additive**: every new code path is gated on a row
  being present, so a row-less program takes today's path byte-for-byte. Evidence:
  `dotnet test tests/Calor.Enforcement.Tests/` → **344 passed, 0 failed**, and
  `HigherOrderDemandLedgerTests` (the D-A exact-equality ledger) stays green.
- **S3** rebased onto `origin/main` `03ecbca1` (PR #1093 merged) with
  `git rebase --onto origin/main 5d99f900`, so only the three spike commits replay and
  main's copies of the doc and harness are the ones in the tree.

### Finding F1 — SEVEN committed transcripts diverge, none regenerated

`EffectRowExperimentHarnessTests.ExperimentTranscripts_MatchARerun` (**P29**) goes **red**
with the prototype in the build. Per the spike's own discipline the transcripts are
**not** regenerated — a divergence is a finding, and the frozen evidence base belongs to
E2, not to a throwaway prototype.

> **Corrected in review round 1: this said THREE.** The three had been read off P29's
> failure message, which prints the **first** difference per script and hides the rest —
> so three drifting *scripts* looked like three *cases*. Re-diffing all six scripts in
> full gives **seven**, and two of them are in `facts.py`, where the first-difference
> report could never have shown the second. Recorded rather than quietly fixed: it is
> the same failure mode the harness exists to prevent — a number taken from a summary
> instead of from the measurement.

Counted by diffing every script against its committed transcript: `run.py` **3**,
`run3.py` **2**, `facts.py` **2**; `run2.py`, `facts2.py` and `compile53.py` **clean**.

| # | Script / case | Committed | With the prototype | Verdict |
|---|---|---|---|---|
| 1 | `run.py` **X6a** | exit 1; **14-line** `Calor0100`/`Calor0114` cascade | parses; the case's own `§E{}` then under-declares → one `Calor0410 … 'alloc'` | **Intended.** X6a exists to show `eff e` is new syntax today. E2 must **re-author** it, not regenerate it |
| 2 | `run.py` **X9b** (`§FLD … §E{cw}`) | exit 1; **4×** `Calor0100` | **exit 0**, compiles | **Intended** — position 8. X9b is the case §14.1 cites to disprove Draft v1's claim that `§FLD` already parsed a row |
| 3 | `run.py` **X9c** (inline param row) | exit 1; **12×** `Calor0100` | **exit 0**, compiles | **Intended** — position 5 |
| 4 | `run3.py` **Z1** (`§FLD` ⏎ `§E`) | **4×** `Calor0100` | one `Calor0405` | **Intended** — §3.1's recovery; the 4→1 collapse is P2(b)'s claim |
| 5 | `run3.py` **Z3** (wrapped signature) | **8×** `Calor0100` | one `Calor0405` | **Intended**, and the strongest: §3.1 calls Z3 *not hypothetical* because Z5 shows wrapped lists are really written |
| 6 | `facts.py` line 6 | `FunctionNode.cs:252` | `FunctionNode.cs:283` | **Unavoidable noise.** `facts.py` pins `file:line`; `ParameterNode.Row` moved it. Any E2 implementation moves it too |
| 7 | `facts.py` `IsSubsetOf` sweep | `EEP:533` and `:571` listed | both vanish; five `EffectSet.cs` lines appear | **Intended — and it is R2's own evidence moving a pinned fact.** §6.3 says those two calls become calls to the shared `EffectRow.Fits`; this sweep is the instrument that observes it |

One divergence was **not** legitimate and was fixed rather than recorded: an early draft
trimmed the unknown-effect-code string, which changed **X5a**'s message from `' ^ e'` to
`'^ e'`. That is a gratuitous message change on a path the spike does not own; the
untrimmed code is reported again and X5a matches its transcript.


## Final structure — two branches

The spike produced a throwaway prototype and a set of artifacts. They ship apart, on
purpose:

| Branch | Carries | Why |
|---|---|---|
| `spike/effect-rows-emitter` (pushed, **not** for merge) | the prototype: 6 files under `src/`, 0 new files | It is a spike, not E2 — its parser-level effect-variable scope alone is a shortcut E2 must replace. And with it in the build **P29 is red by design** on seven cases (F1 above), so a branch carrying it cannot be the PR that "must merge before E2" |
| `spike/effect-rows-artifacts` (**the PR**) | `docs/design/spikes/effect-rows/**`, `SpikeVerdictTests.cs`, three corpus path-exclusions, the manifest delta, the design-doc update | No `src/` change, so **P29 is green** and the frozen evidence base is untouched |

`spike-verdict.json` records the prototype's branch and commit so the code that produced
the AFTER artifacts stays fetchable and reviewable.

## Finding F2 — spike artifacts collide with three corpus instruments

Committing `.calr` fixtures under `docs/` turned **three** frozen instruments red, each
of which enumerates `.calr` repo-wide and had no reason to expect artifacts there:

| Instrument | How it enumerates | Symptom |
|---|---|---|
| `HigherOrderDemandLedgerTests.DA_CalorNative_MatchesLedgerExactly` | filesystem walk | `D-A corpus size moved: 901 vs 886` |
| `LosslessFormattingTests.CheckedInCalorCorpus_…` | `git ls-files '*.calr'` | `trackedFileCount` 886 vs 901 |
| `experiments/facts.py` (pinned by P29) | `git ls-files '*.calr'` | the "5 function-typed shapes in 5 files" count §1 quotes moved |

All three now exclude `docs/design/spikes/` by path. **No count changed** — those files
were never part of the 886 — so no ledger, baseline or transcript was regenerated. Round 3
had solved the same problem for *scratch* files by writing them outside the repository;
committed artifacts cannot move, so they are excluded instead. Recorded in the design doc
as §13.5(b) so the next spike inherits it.

## What the spike could not make honest

- **R1 is weaker than its wording.** Its bar is the *absence* of Calor0404/0424/0425, and
  the prototype is additive — every new path is gated on a row being present — so §3.5's
  Calor0425-on-omitted-row is unimplemented and a row-less function-typed position still
  reaches Calor0418. The four fixtures do type-check cleanly; not every 0425 route was
  exercised.
- **R1 is recorded, not recomputed, on the PR branch.** §12.3 has P27 recompute it by
  compiling each A3 fixture, which needs a compiler that parses a row. `spike-verdict.json`
  says so in `ramp.R1.recomputedBy`, and P27 asserts that deferral string so it cannot be
  left recorded once E2 lands.
- **§8.2 is untouched.** The prototype reads rows off the AST, because the effect pass is an
  AST walk. `FunctionBoundType.Row` / `ParameterRows` is still owed by E2.
- **R2's residual stands.** Member-level `eff` works when **both** sides are Calor. A
  C#-declared `IPipelineBehavior` has no row for the implementation to be checked against,
  so §14 Q1 is narrowed, not closed.
- **A2 is not the corpus file verbatim.** `calor convert` fails on it alone with four CS0246
  errors, so its three MediatR dependencies were inlined before conversion. The class body is
  the corpus file's, unmodified, and the pinned 29-line subject is still pinned by its own
  test leg.
- **Three A3 fixtures deviate from §7.4's literal spelling** — `§IX` is not a live marker,
  `§ARR{T}` emits `new int[]` for a type-parameter element type, and the `?T`/`is_some`/
  `unwrap` Option surface does not compile. Each deviation is listed in
  `spike-verdict.json` under `artifacts.A3.deviations`, with the reason and what was kept.

## Review round 1 — adversarial review of PR #1096

**NEEDS-FIXES (80%)**, with the verdict itself **EARNED**: the reviewer recomputed all 29
artifacts byte-exact from the prototype commit, and G-CODEGEN (modulo `#line`), R1, R2, R3
and R3's counter-example all reproduced, with the caveats above holding. Twelve findings,
**12 applied, 0 declined** — all corrections of the *record*, none re-opening the verdict.
Full disposition table in the design doc's §15 round 4. The three that changed what the
spike claims rather than how it says it:

1. **Seven divergences, not three** (F1, corrected above). The miscount came from reading
   P29's failure message, which prints the first difference per script.
2. **`gCodegen.A2.diffBytes: 0` was a length delta, not a byte count.** A2 really has **7**
   differing bytes, all digits inside `#line` directives. The field is now split into
   `strictDiffBytes` and `nonLineDirectiveDiffBytes`, and **P27 asserts both for every
   artifact** — the old single field was read by no test at all, so a wrong number sat in
   the record looking like a measurement.
3. **`spike_artifacts.py` still wrote `.g.cs`** after the evidence files were renamed to
   `.g.cs.txt` for the Calor-first guard. It therefore emitted untracked files beside the
   real evidence and never refreshed it — while P31's failure message told the reader to
   run it. Fixed, and re-running it against the prototype now regenerates **all 29
   committed files byte-identically** with nothing left untracked.

Two prototype defects were found that are not record-keeping and are recorded as **E2
obligations** (§12.4 caveats 5 and 6): Calor0404 is reached at only two of §7.3's seven
rejection sites (the rest surface as `Calor0403: Unknown effect code 'e'` — forbidden, but
with a taxonomy message for a scope error), and Calor0421 renders a polymorphic interface
row as `[pure]` because its message is built from `EffectSet.ToDisplayString()`, which
knows only the concrete part. Both have right verdicts and wrong text.
