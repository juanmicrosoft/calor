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

### Finding F1 — three committed transcripts diverge, none regenerated

`EffectRowExperimentHarnessTests.ExperimentTranscripts_MatchARerun` (**P29**) goes **red**
with the prototype in the build. Per the spike's own discipline the transcripts are
**not** regenerated — a divergence is a finding, and the frozen evidence base belongs to
E2, not to a throwaway prototype. The three, with their verdicts:

| Script / case | Committed | With the prototype | Verdict |
|---|---|---|---|
| `run.py` **X6a** (`§F{…}<T, U, eff e>`) | `Calor0100: Expected Greater but found Identifier` | parses; the case's own `§E{}` then under-declares `alloc` → `Calor0410` | **Intended.** X6a exists to show `eff e` is new syntax today; the spike is what makes it parse. E2 must re-author X6a, not regenerate it |
| `run3.py` **Z1** (`§FLD{i32:x:pri}` ⏎ `§E{cw}`) | four `Calor0100` cascade lines | one `Calor0405: A §E{…} effect row must be on the same line as the type it annotates…` | **Intended** — this is §3.1's row-aware recovery, and the 4→1 collapse is exactly the claim P2(b) makes |
| `facts.py` line 6 | `Ast/FunctionNode.cs:252` | `Ast/FunctionNode.cs:283` | **Unavoidable noise.** `facts.py` pins `file:line` structural facts; adding `ParameterNode.Row` moved the line. Any E2 implementation moves it too |

One divergence was **not** legitimate and was fixed rather than recorded: an early draft
trimmed the unknown-effect-code string, which changed **X5a**'s message from `' ^ e'` to
`'^ e'`. That is a gratuitous message change on a path the spike does not own; the
untrimmed code is reported again and X5a matches its transcript.


## Final structure — two branches

The spike produced a throwaway prototype and a set of artifacts. They ship apart, on
purpose:

| Branch | Carries | Why |
|---|---|---|
| `spike/effect-rows-emitter` (pushed, **not** for merge) | the prototype: 6 files under `src/`, 0 new files | It is a spike, not E2 — its parser-level effect-variable scope alone is a shortcut E2 must replace. And with it in the build **P29 is red by design** on three cases (F1 above), so a branch carrying it cannot be the PR that "must merge before E2" |
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
