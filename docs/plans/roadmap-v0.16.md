# Roadmap — v0.16

**Date:** 2026-08-27
**Status:** Draft v1 — written against the source at `7d621c0d` (main after PR #1110, the PP-E1
leg-B record), before the 0.15.0 release PR merges (`Directory.Build.props:3` still reads
`0.14.3`). Every number below was re-measured from the tree or the archived epoch at that commit,
with the file and line it came from; nothing is copied from a prior plan. §10 is empty on purpose —
the adversarial reviews run next.
**Governing inputs:** `roadmap-v0.13-v0.15.md` (§4.0–§4.5 for the 0.15 discipline this draft
mirrors; §8–§10 for the mistakes reviewers made that plan fix), the effect-rows design doc
(`docs/design/effect-rows-in-the-type-system.md`, hereafter *D*), the seven 0.15 slice notes
(`docs/plans/2026-08-2*-v0.15-*.md`), the A-annex (`docs/plans/agent-native-gates.md`, hereafter
*A*, read-only), the open issue list on 2026-08-27, `CHANGELOG.md` `[Unreleased]`, and the
2026-08-18 test-suite audit.

**Conventions.** *R:* = `roadmap-v0.13-v0.15.md`; *D:* = the design doc; *A:* = the annex;
*e2a/e2b/e3a/e3b/e4/e5/ppe1:* = the slice notes by short name; bare paths are repo-relative.
"Ledger" means a committed JSON under `bench/phase0-agent-native/` re-executed by an
exact-equality test.

---

## 0. Where 0.15 left us (measured at `7d621c0d`, 2026-08-27)

### 0.1 What shipped

- **E1–E5, M1 and PP-E1 all landed** (R:522-727). E1 has one `EffectResolver.Resolve(EffectResolverKey)`
  entry point (R:524-529); E2 parses all eight row positions and gives `FunctionBoundType` a `Row`
  (R:593-608); E3 checks the six D:§6.2 sites (D:903-910) with Calor0424/0425 and the rank-1 solve
  (R:621-647); E4 replaces Calor0418 with row charging at invocations (R:672-696); E5 records rows
  in `ProjectIndex.EffectRows` and adds `calor query effects` / `impact --effects` (R:697-720).
  The `[Unreleased]` changelog at `CHANGELOG.md:5-387` is the user-facing record.
- **Elision is default-on** (R:976; `CHANGELOG.md:326-354`); **`§SEMVER{1.x}` is refused with
  Calor0701** (`CHANGELOG.md:303-325`; #1084 item 1 done, items 2–3 open).

### 0.2 PP-E1 — verdict HIT, and what the archive actually shows

- **Leg A** 10/10 detected, control clean, ramp not fired (denominator 10, not 7):
  `bench/phase0-agent-native/effect-rows-probe-ledger.json` (`legA.detected = 10`,
  `legA.rampFired = false`, `negativeControl.clean = true`, `verdict = HIT`,
  `measuredCommit = 7ad1b1e0…`).
- **Leg B** over epoch `e1-rows-parity-001` (`…/epochs/e1-rows-parity-001/ppe1-analysis.json`):
  model `claude-opus-4-8`; arm A = v0.14.3 @ `63316987`, arm B = `b775acb4`; 40/40 valid, 0 %
  censored; per-pair ratios N1-001 **1.1762**, N1-002 **1.5118**, N1-003 **1.1907**, N1-005
  **0.8984**; point **1.1835**; one-sided 95 % lower bound **0.9012**; median within-cell CV
  **0.2746** (max 0.4259); `iterationsToGreen` control `{1:19, 2:1}`, treatment `{1:18, 2:2}`.
  Registered reading: "no large tax detected", never "proven equal"; power 0.22 / 0.48 / 0.77 at
  1.25× / 1.4× / 1.6× (ppe1:262-274; A:317 "Power, so a pass cannot be overread").
- **Sensitivity, on the record** (ppe1:225-230, :274-281): one control run (N1-001 run 3) spawned
  a subagent; read naively (4 522 instead of 12 821 tokens) the point would be **1.3390**, 0.011
  under the 1.35 gate. The corrected figure is the registered one.
- **The N1 pairs contain no callbacks.** Measured over the 40 archived `final-src` files: no
  `<eff …>` binder, no `§LAM` / `§DEL` / function-typed position, 257 `§E{…}` declarations all on
  their own line (ppe1:294-298). Leg B "measures a stricter compiler's cost in the edit-compile
  loop on ordinary code, **not** rows' cost on rows-using code, which no archived epoch measures"
  (ppe1:271-274). **Rows' benefit on callback-heavy code is unmeasured.**

**The 18 % is not the effect checker — re-measured for this draft, and this refutes the brief's
working hypothesis.** Over every `journal.jsonl` in the epoch (49 harness-observed builds: 26
treatment, 23 control — `journal.jsonl` `diagnostics[].code`):

| Arm | Builds | Diagnostics any agent ever saw |
|---|---|---|
| `calor+0.15.0` (treatment) | 26 | Calor0100 ×2, Calor0101 ×2 (parse errors the agent introduced and fixed), Calor0830 ×1 |
| `calor+v0.14.3` (control) | 23 | Calor0830 ×1 |

**Zero Calor0410 / 0411 / 0419 / 0424 / 0425 on either arm.** The candidate causes the brief named
— Calor0425 "cannot tell" noise, fail-closed Unknown charges on BCL-returned delegates — did not
occur once. What differs is agent behaviour, not compiler output (`agent.json` `num_turns`,
`duration_ms`; `result.json` `tokens.output`):

| Pair | Median turns T / C | Median wall-clock T / C (s) | Output tokens T (sorted) | Output tokens C (sorted) |
|---|---|---|---|---|
| N1-001 | 19 / 16 | 136 / 127 | 4864, 8027, 8203, 12426, 13150 | 4221, 5817, 7387, 9431, 12821 |
| N1-002 | 23 / 17 | 135 / 91 | 5642, 7437, 8048, 10298, 10621 | 3361, 4348, 4461, 5548, 10093 |
| N1-003 | 23 / 19 | 255 / 187 | 5004, 11552, 16362, 18034, 23938 | 10597, 11788, 12196, 13585, 14732 |
| N1-005 | 19 / 20 | 109 / 126 | 5736, 6045, 6882, 7284, 7900 | 6128, 7066, 7748, 8366, 8368 |

More turns with the same number of builds and the same first-iteration green rate means the extra
spend is reads, thinking and non-build tool calls — **and the archive cannot say which.**
`run-pair.sh:853` / `:867` invoke `claude --print --output-format json`; `agent.json` holds usage
plus the final message, no per-turn tool calls (keys: `usage`, `modelUsage`, `num_turns`,
`result`, …). The prompt is arm-neutral (`run-pair.sh:792`), the only hook the harness installs
blocks `.g.cs` edits (`run-pair.sh:502-516`), and the surfaces that differ between the two arm
commits and that an agent *could* have read are enumerable from `git diff 63316987..b775acb4`:
`Program.cs` (59 lines, 3 option-description lines), `Commands/QueryCommand.cs` (213),
`Commands/EffectsCommand.cs` (77), `Commands/HookCommand.cs` (14 — `§SEMVER` reminder text; not
installed by the harness), `Calor.Tasks/CompileCalor.cs` (12 — the elision default, inert on
contract-free N1 code), `Sdk.targets` (3). **"Why more turns" is an open question** the entry
spike must answer (§2.1) with a zero-spend replay first and a per-turn capture second — no further
paid epoch runs before the capture exists (§5 gate 8).

### 0.3 The static benchmark cannot see rows

`CHANGELOG.md:391-398` (0.14.3): 1.32× overall over 30 runs, 7 categories to 1, 217 benchmarks
evaluated; the same block is byte-identical at 0.14.0–0.14.3 (`:392`, `:412`, `:450`, `:515`);
`[Unreleased]` carries no benchmark block yet. D:1768: **0 of 359** `.calr` goldens under
`tests/TestData` had a `Func<`/`Action<`/`§DEL`/`§LAM` shape at design-doc freeze; **1 of 359**
now (`QueryCorpus/project/app.calr`, authored with a row on purpose by E5 — re-verified by grep
at `7d621c0d`). D:1770: `Calor.Conversion.Tests` never runs the effect pass. The headline number
remains a regression indicator (R:37-38), and it is structurally blind to 0.15's feature.

### 0.4 Corpus and ledgers (exact, from the committed files)

| Instrument | Value at `7d621c0d` | Source |
|---|---|---|
| Committed `.calr` in ledger scope | **941** = 926 corpus + 15 spike artifacts under `docs/design/spikes/` (excluded by every ledger, D:2722-2729) | `find` at `7d621c0d`; ppe1:288-292 (886 → 926 with the 40 epoch archives) |
| D-A higher-order demand (Calor-native) | **2** (`calor0418` 0, `calor0419FunctionTyped` 2) over 926 files, 45 not reaching the effect pass | `higher-order-demand-ledger.json` `dA.*` |
| D-B backstop (Roslyn count, three subjects) | **3121** over 364 files: 2676 lambdas, 311 delegate-typed declarations, 132 delegate invocations, 2 delegate declarations | same file, `dB.aggregate.*` |
| Route (b) floor | 3123 vs floor 25 — inert | same file, `floor`; probe ledger `routes.b` |
| Calor0425 corpus ledger (P32) | **8** diagnostics over **99** enforced of 364 modules; **265 excluded** (59 parse-failed, 206 bind-failed); causes: `InvocationRowless` 4, `ExternalBase` 4, **`UnknownSource` 0, `InvocationUndetermined` 0, `InvocationAssumed` 0** | `calor0425-corpus-ledger.json` `PerSubject[*]` |
| Calor0270 ledger | **193** diagnostics across 38 of **305** bound modules | `calor0270-corpus-ledger.json` |
| Resolver-key ledger (E1 slice 2c baseline 202/751) | **259 bound / 812 string** — the +57/+61 is the 40 epoch archives, accounted construct by construct (ppe1:303); E2 did **not** move the split on the pre-existing 886 | `effect-resolver-key-ledger.json`; R:532-537 |
| Metadata-binding gate 6 | **817 / 1248 = 65.46 %** (MediatR 129/226, Serilog 104/113, FluentValidation 584/909); MediatR's dominant class "No overload matches the supplied argument types" 86 | `metadata-binding-corpus-ledger.json` `perSubject` |
| Converter → Calor validity | 59 of 364 outputs fail to parse (#903, 3 clusters); 72 `AnalysisICE` from a bare `?` lambda parameter type (#1097) | issue bodies |

Two readings of that table drive §1. First, **the Calor-native corpus has essentially no
higher-order code** (D-A = 2) while the C# corpus has 3121 sites: rows meet real code only through
conversion, and the converter emits no rows (D:1780-1783). Second, **the corpus every 0.15 effect
claim stands on is 99 of 364 modules** — 265 are excluded before the effect pass by conversion
parse failures, binder failures, and the #1104 crash workaround (e3a:252-270) — and the
"BCL-returned delegate" cause the DEFERRED list names is **measured at zero** in the modules that
do reach the pass.

### 0.5 Registered 0.15.x debts (the full table is §6; the ones that shape the theme)

- `FunctionBoundType.Row` still has no end-to-end production reader: `Binder.BindRow` collapses a
  variable-mentioning row to Unknown, so `EffectInferrer.ResolveInvokedValueRow` reads the `§E`
  node by AST span-matching (R:710-720; D:2700-2708; e5:150-164). Binder work that moves every
  E2b/E3/E4 pin.
- A `§LAM` inside a rank-1 function that invokes the polymorphic parameter under-approximates its
  row **silently** if it escapes under a row-less `-> Func<…>` (e4:230-234).
- Lambda parameters invoked inside the lambda → Calor0411, silently pure under
  `--permissive-effects` (e4:255-259); untyped alias `§B{g} f` then `§C{g}` → Calor0411 + the
  fail-closed 0410 (e4:252-254); Calor0410 itself still demoted under `--permissive-effects`
  (e4:246-248).
- Index vs `calor build` parity: the index folds cross-module charges for every file that binds,
  the driver only for files whose own compile succeeded (e5:265-272); interface methods are not
  indexed (e5:273-275); the per-file effect pass in the index build is unmeasured against gate 8's
  30 s envelope (e5:259-261).
- `PropagateInstantiatedCharges` stops silently at `maxIterations = 10_000`
  (`src/Calor.Compiler/Effects/EffectEnforcementPass.cs:1129`; e3b:117-123 says the cap mirrors
  `ProcessScc`'s and names no number). #1104: `EffectEnforcementPass.Enforce` recurses without a
  bound on two Serilog modules (e3a:252-270).
- DEFERRED at design-doc merge (R:738-742): async rows (D:§11, lines 1922-1945 — there is no D:§5.1;
  the 0.16 re-entry test is D:1936-1942), `PreconditionSuggester` on the typed CFG, reflection /
  `DynamicInvoke` / `dynamic`, event-handler `+=`, BCL-returned delegates (D:1660-1662).
- SHOULD tier never started: E6 (`review-packet` reads the index), E7 (MCP query surface), E8
  (contract outcomes facet), E9 (affected-tests facet — no design), M2 (a real Calor arm as
  product) (R:729-736).

---

## 1. Theme

### 1.1 What the ladder says comes after 0.15

R:40-46 ends the staircase at 0.15; R:1068-1082 states what "better than C#" meant at its end:
compositional effect safety **for the registered combinator set, instrumented at fixture scale**,
plus "a standing, quantitative path back to real-scale measurement". R:989-1010 keeps the
real-scale venue retired until "≥70 evaluable tasks" and "an arm that actually invokes Calor"
both exist. 0.16 therefore starts with two measured holes: the benefit of rows on the code they
were built for has never been observed, and every corpus-scale claim about effects rests on
27 % of the conversion subjects.

### 1.2 Three candidates, each tested against §0

**Candidate A — "Effects that pay for themselves"** (the brief's framing): measure the benefit,
remove the tax, extend rows to where Unknown comes from (IL-derived rows for BCL delegates).

- *Benefit leg — supported.* Unmeasured by construction (§0.2); the five spike fixtures already
  carry the callback shapes (A:317 leg A fixture set), so a benefit probe has fixtures.
- *Tax leg — the hypothesis is refuted, the question is open.* Zero effect-family diagnostics on
  either arm (§0.2). There is no diagnostic to remove. The residual question — why 3–6 more turns
  per run — has no instrument in the archive and needs a harness change before any paid run.
- *IL-rows leg — no measured demand.* The 0425 ledger's `UnknownSource` and
  `InvocationUndetermined` buckets are **0** over the 99 enforced modules; the 8 sites present are
  row-less declarations (4) and external bases (4). D-A's `calor0419FunctionTyped` is 2. Building
  IL-derived rows in 0.16 would be building for a cause the instrument cannot find — the TIER1A
  shape (R:1096, "designer-judgment gate"). It earns a **demand trigger**, not a MUST slot (§6).

**Candidate B — "Index consumers"** (E6 / E7 / E8, R:729-734): make the project model the product.

- *Supported by:* the SHOULD tier is fully specified and three of its four items have instruments
  already (gate 3's MCP leg, gate 7's E6/E7 legs, R:861-864). E7 is what `calor_navigate` lacks
  (R:410-411).
- *Against as a theme:* no proof point exists for "agents use the index" and none is proposed;
  R:1005 records zero external adopters; E9 has no design. B is product work whose absence is a
  recorded decision already — it does not need a release's name to ship in 0.16.x. It stays
  SHOULD (§3.2).

**Candidate C — "Trustworthy migration"** (#903, #1097, #901, #929, #943, #847): converter fidelity.

- *Supported by:* the denominator numbers in §0.4. 265 of 364 subject modules never reach the
  effect pass; 59 converter outputs do not parse; 72 bind as `AnalysisICE`; `Calor.Conversion.Tests`
  never runs the effect pass (D:1770); the converter emits no rows (D:1780-1783). The 0.15 gates
  (R:933-970) are green over a denominator that excludes three quarters of the real code, and the
  real-scale re-entry condition (≥70 evaluable tasks) is gated on the same converter (R:28-31).
- *Against as the whole theme:* R:995 and R:294-306 declined "a broad converter campaign", and the
  precedent is that demoted conversion issues got closed one at a time anyway (R:1019). A version
  titled "migration" with no effect-system progress also abandons the ladder's spine after one
  release of rows.

### 1.3 Recommendation — **v0.16 "Rows on real code"**

Take A's benefit leg and its (reframed) tax leg as the measurement spine, take C's *measured
subset* — not a campaign — as the denominator repair those measurements need, and hold A's IL leg
and B behind pre-registered triggers. Stated as three testable outcomes:

1. **Rows' benefit is observed, not asserted.** A pre-registered proof point (PP-W-rows, §4.1)
   runs agents on callback-heavy tasks with seeded laundering defects, rows on vs rows off, and
   publishes a four-valued verdict at the 0.16.0 release commit.
2. **The turn gap is attributed or bounded.** The harness captures per-turn tool calls before any
   paid epoch (§2.1, §5 gate 8); the zero-spend replay over the 0.15 arms is published whatever it
   finds.
3. **The corpus denominator is at least doubled.** The 0425 and 0270 ledgers enforce a
   pre-registered floor of modules reaching the effect pass — the trigger for every "on real
   code" claim, and the instrument that decides whether IL-derived rows have demand.

Why this and not A verbatim: A's tax leg as posed would have built product against a refuted
cause; A's IL leg has a measured demand of zero. Why not C verbatim: the release would carry no
effect-system claim and no proof point. Why not B: it has no measurement and needs no theme.

---

