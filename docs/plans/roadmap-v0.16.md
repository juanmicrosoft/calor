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

## 2. Entry gate — three spikes, each with a pass/fail, before the MUST tier opens

0.15's pattern (R:437-513): an adversarially reviewed design doc, a throwaway spike with a
G-CODEGEN-style blocking gate (D:1988-1996), and the PP registered in the annex **before** any
implementation merges (R:762-786, A:470). 0.16 has no new type-system decision to design, so the
entry gate is three spikes with numeric exits, and one annex registration.

### 2.1 Spike S1 — the zero-spend replay of `e1-rows-parity-001` (answers "why more turns" as far as the archive allows)

*Doable today, costs no API spend, and must be published before PP-W-rows registers.*

1. Rebuild both arms (`63316987`, `b775acb4`) and, for each of the 40 archived
   `final-src/*.calr`, compile under both and diff the emitted C# and the `dotnet build` text
   byte-for-byte. Expected: identical (N1 has no contracts, so the elision flip is inert) — if not,
   that diff is a candidate cause and is published.
2. Diff `calor --help`, every subcommand's `--help`, and `Calor.Tasks` MSBuild messages between the
   arms (the surfaces `git diff 63316987..b775acb4` names in §0.2).
3. Re-derive the turn/duration/token table of §0.2 with a committed script
   (`bench/phase0-agent-native/ppe1-turn-attribution.py`, exact-equality test in
   `tests/Calor.Compiler.Tests/Effects/`), so §0.2 is reproducible rather than narrated.
4. A **2-run pilot** (one pair, one run per arm) under `claude --print --output-format stream-json`
   to prove per-turn capture round-trips through the harness — the only paid step, bounded to two
   runs, and its purpose is to validate the instrument, not to measure.

*Pass:* steps 1–3 published as a note with the diff artifacts; step 4 archives a per-turn transcript
per run. *Fail:* step 1 finds a byte difference — then the cause is measured, not the harness, and
S1's note says which.

### 2.2 Spike S2 — the conversion denominator (fixes #1104 on a throwaway; measures what it unlocks)

Fix the `EffectInferrer` recursion behind #1104 with a depth bound *on a throwaway branch*, drop
the P32 workaround that skips unbound modules (e3a:252-270, "costs 265 of 364 modules"), and
regenerate the 0425 and 0270 ledgers. Publish the new `ModulesEnforced` / `ModulesBound` and,
per excluded module, the first error code — the `notReachingEffectPass` pattern of the demand
ledger.

*Pass:* the spike shows what floor is **reachable** by the #1104 fix alone versus what needs
#903's clusters 1–2 (Calor0099 dedent, Calor0100 empty-`§IFACE`) and #1097. The floor §5 gate 9
pre-registers is set from this measurement, **before** any of those fixes merge to `main`
(freeze-before-measurement, R:1057-1058). *Fail:* the spike cannot bound the recursion without
changing resolution answers (gate 6's ledger moves) — then #1104 is binder-adjacent work and
falls under §9's abort clause.

### 2.3 Spike S3 — PP-W-rows fixtures and margin, results-blind

Author the four W-rows pair specs (§4.1) as arm-neutral behavioural specs in
`bench/phase0-agent-native/pairs/W-*/spec.md` (the N1 `spec.md` shape, `pairs/N1-002-inventory/spec.md:1-4`),
each with a held-out test that **observes the laundered effect** (stdout capture / a recording
sink), and re-run the margin derivation (`ppe1-margin-derivation.py`, seed 4537) on the
`e1-rows-parity-001` population — the closest archived variance (CV 0.2746) — producing the null
p95, the 0.05-grid margin, the false-fail rate and power at 1.25× / 1.4× / 1.6×.

*Pass:* the annex entry **A-1.12** registers PP-W-rows with fixtures by blob SHA, the two arms,
the metric, the margin, power, the four-valued map and the freeze event, verified at registration
by the absence of any 0.16 row-family change in `src/` (the A-1.11 `grep` discipline, R:767). The
A-1.2 honest-timing disclosure applies: the fixtures are authored by this workstream.
*Fail:* power at the registered effect is below the PP-W5 floor that A:§6.1 requires — then the
design is widened (more runs per cell) or the PP registers as UNDERPOWERED-by-design and says so.

**Ordering:** S1 → S3 (the margin needs S1's attribution script to be trusted); S2 in parallel. No
MUST item merges before A-1.12 exists (the M1 rule, R:721-727).

---

## 3. Ship — tiered, with the cut lines stated

MUST gates the release; SHOULD ships if it fits and defers to 0.16.x without renegotiation;
DEFERRED is named so its absence is a decision (R:517-519).

### 3.1 MUST

- **W1 — Per-turn capture in the harness.** `run-pair.sh` / `run-bundle.sh` archive the
  `stream-json` transcript per run beside `agent.json`; `token-usage.py` (A:619-636) keeps deriving
  `tokens.output` from `modelUsage`, unchanged; #1094's `.calor-build-state.json` archive lands in
  the same change (compiler-attested arm provenance). *Touches:* `bench/phase0-agent-native/run-pair.sh`,
  `run-bundle.sh`, `tests/test_token_usage.py` sibling. *Pin:* a harness test that a run directory
  without a per-turn transcript is `invalid`, and that the transcript's turn count equals
  `agent.json` `num_turns`. *Discriminating:* delete the archive step and the test fails.
- **W2 — PP-W-rows run and adjudicated** (§4.1) at the 0.16.0 release commit, verdict in the
  release notes whatever it says. *Touches:* `bench/phase0-agent-native/pairs/W-*`,
  `ppw-analyze.py`, `effect-rows-benefit-ledger.json`, `tests/…/EffectRowsBenefitLedgerTests.cs`.
  *Pin:* exact-equality ledger test (the `EffectRowsProbeLedgerTests` pattern, R:906-932).
- **W3 — Conversion denominator floor** (the S2 measurement made permanent): #1104 fixed with a
  bounded recursion and a regression pin on the two Serilog modules; #903 clusters 1–2 fixed;
  #1097's bare `?` replaced by the fail-closed `UnresolvedBoundType` route the binder already has
  (`CHANGELOG.md:284-297`). The P32 workaround is deleted. *Touches:*
  `src/Calor.Compiler/Effects/EffectInferrer` (under `Effects/`), `Migration/CalorEmitter.cs`,
  `Migration/RoslynSyntaxVisitor.cs:11774-11794`, the two ledger tests. *Pin:* §5 gate 9 — the
  ledgers' `ModulesEnforced` / `ModulesBound` at or above the pre-registered floor, exact per
  subject. *Discriminating:* restore the workaround and the floor test fails.
- **W4 — The turn-gap attribution published.** S1's note plus, once W1 exists, the per-turn
  breakdown of the first 0.16 epoch (PP-W-rows itself is the first epoch with capture). No product
  change is promised here: the deliverable is a measurement with a cause or a bounded "unknown".
  *Pin:* the attribution script's exact-equality test over the archived epochs.
- **W5 — Silent stops made loud.** The `10_000` cap in `PropagateInstantiatedCharges`
  (`EffectEnforcementPass.cs:1129`) raises a diagnostic (Calor04xx, allocated in the W5 PR) with the
  function that hit it instead of returning quietly; the e4:230-234 escaping-lambda case draws
  Calor0425 at the `§R` (the D:§6.2 site 3 span) instead of nothing. *Pin:* one `_IsReported` test
  each, built from a fixture that reaches the cap / the escape. *Discriminating:* revert and the
  test fails.

**Cut line 1.** If W3 overruns (converter and effect-pass fixes have a history of finding more
behind them — #903 has been open since 2026-08-10), W2 still runs: the PP is fixture-scale and
independent of the corpus floor. W3's floor then reads at its S2-measured "reachable by #1104
alone" value and the release notes say which cluster did not land.

### 3.2 SHOULD

- **E6** `review-packet` reads the index (R:731) — gate 7's E6 leg already exists conditionally.
- **E7** MCP query surface: callers / callees / impact / effects (R:732) — gate 3's MCP leg.
- **W6 — `FunctionBoundType.Row` end-to-end** (the 0.15.x obligation, D:2700-2708): the bound row
  carries the variable part; `ResolveInvokedValueRow`'s AST span-matching goes. Binder-adjacent,
  so SHOULD by §9's rule; the pins that must stay green are named at D:2700-2708 and e5:150-164.
- **W7 — Lambda-parameter rows** (position 2 of D:§3.3) so `§LAM{l1:h:Func<…>} §C{h}` is charged
  rather than Calor0411 (e4:255-259) — the same slice fixes the untyped-alias hop (e4:252-254).
- **W8 — Index parity with `calor build`** (e5:265-272) and interface members indexed
  (e5:273-275); gate 8's 30 s envelope measured on the largest corpus subject (e5:259-261).
- **E8** contract outcomes in the index (R:733).
- **M2** the real Calor arm as product (R:735-736) — a SHOULD for the third release running, and
  §5.1's re-entry condition; W2 is *not* M2: it invokes Calor on fixtures, not on a ≥70-task venue.

### 3.3 DEFERRED (named; frozen as the residual list at A-1.12 registration)

- **IL-derived rows for BCL-returned delegates** — *demand trigger:* the 0425 ledger's
  `UnknownSource + InvocationUndetermined` exceeds **10** over the W3 denominator at the 0.16 branch
  cut; today 0 over 99. Venue if triggered: 0.17 entry spike.
- **Async rows** — the D:1936-1942 three-clause re-entry test is adjudicated at the 0.15.0 retro,
  as D:1926 says; absent that adjudication in writing, deferred again by decision.
- `PreconditionSuggester` on the typed CFG (#786 residual); reflection / `DynamicInvoke` /
  `dynamic`; event-handler `+=`; `§DEL` type parameters (D:1143); rank-2 (e3b:150-155) — all
  unchanged from R:738-742.
- **E9** affected-tests facet — still no design; not deferred *to* anything until one exists.
- **Converter emits rows** — declined again on D:1780-1783's grounds (a Roslyn-side effect
  inference over 3121 sites is the campaign R:995 refuses).

---

## 4. Honest measurement

### 4.1 PP-W-rows — "rows catch laundered effects on callback-heavy code without a large loop tax"

**Pair set (four pairs, the PP-E1 fixture shapes, A:317 leg A):**

| Pair | Shape | Source fixture | Seeded laundering defect (one per task, frozen by diff anchor) |
|---|---|---|---|
| W-001 middleware | pipeline `Handle(request, next)` with `<eff e>` | `docs/design/spikes/effect-rows/after/A2.calr` (blob `93ecdf16`) | an interface member widened `§E{e}` → `§E{e, cw}` with a body that prints — the L5-A2 cell |
| W-002 map | `Map<eff e>` over a list with a pure and an impure callback | `A3-map.calr` (`0885b3dd`) | a printing callback passed where the caller declares `§E{}` — L6-MAP |
| W-003 match | `Match` combinator with two callbacks | `A3-match.calr` (`c1ce7517`) | as L6-MATCH |
| W-004 callback field | `§FLD{Action<i32>:onChange}` invoked from a method | `A3-callback.calr` (`05ddc23d`) | the field's row deleted, the method left `§E{}` — L7-CB |

Each task asks the agent to **extend** the module (a new combinator use, a new subscriber) under an
arm-neutral behavioural spec; the seeded defect is present in the starter and the held-out test
**observes the effect** (captured stdout / a recording sink asserting the pure path printed
nothing). A-1.2 honest-timing: the fixtures pre-exist, and 7 of 10 mutation cells have a
pre-existing execution (A:317 "Honest timing"); the *tasks* are new and blind.

**Arms — which contrast isolates the claim.** Three options, priced:

- *(i) v0.15.0 vs 0.16 build.* Measures 0.16's delta over 0.15, not rows' benefit — rows exist in
  both arms. Rejected for this PP; it is PP-E1's shape and belongs to a 0.16 *tax* re-run if W4
  finds a cause worth removing.
- *(ii) v0.15.0 strict vs v0.15.0 `--permissive-effects`.* Same compiler; the flag waives
  Calor0425 only (R:985; `CHANGELOG.md:88-92`) and, pre-existing, demotes Calor0410 to a warning
  (e4:246-248). The contrast isolates **fail-closed reporting** of the row family — but Calor0424
  fires in both arms, so a laundering the checker *can* prove is caught in both. It measures the
  waiver's cost, not rows' benefit.
- *(iii) v0.14.3 with `--permissive-effects` vs v0.15.0 strict.* Under v0.14.3, invoking a
  function value is Calor0418 rejection (R:358-360), so callback code compiles only under the
  waiver — which is exactly how the spike's own BEFORE artifacts were produced
  (`after/A2.diagnostics.txt` header, R:793-794). Arm A is therefore "the language before rows, as
  an agent would actually have used it"; arm B is rows, fail-closed. This is the contrast that
  isolates *having rows* from *not having them*. **Chosen.** Its confound is stated: arm A also
  lacks 0.15's non-row changes (E1 re-keying, elision default); the fixtures carry no contracts,
  and E1 moved zero corpus diagnostics (R:530-531), so both are inert on these programs — the S1
  replay method (§2.1 step 1) verifies that on the W fixtures before registration.

**Metric.** Two legs, one verdict:

- *Leg A — escapes.* Per run, `escapedBugs` from the held-out effect-observing test
  (`result.json` already carries `escapedBugs`, `heldoutPassed`). Bar: the treatment arm's escape
  rate is below the control's by the registered margin (set at A-1.12 from S3's derivation over the
  4 × 5 design — a Fisher-exact one-sided test at α = 0.05 with the minimum detectable difference
  published; if the minimum detectable difference at power 0.8 exceeds 0.5, the PP registers as
  UNDERPOWERED-by-design and says so).
- *Leg B — loop tax*, the PP-E1 rule verbatim: fails iff the one-sided 95 % two-level
  cluster-bootstrap lower bound of the median paired per-pair `tokens.output` ratio (treatment /
  control) exceeds 1.0 **and** the point exceeds the margin; iterations-to-green observational.
  Margin: S3 re-derives on `e1-rows-parity-001` (null simulation → p95 → round up to the 0.05
  grid, A:317 "Margin derivation"); the number is **not** written here because it must be produced
  by the script, not the plan. The CV cap = 1.5 × the calibration population's median CV, the
  A-1.11 rule.

**Four-valued outcome, precedence NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT** (A:317; R:919-921):

- **NOT-ADJUDICATED**, exhaustive: (a) any fixture fails to compile on either arm unmutated; (b)
  W1's per-turn capture is missing for any run (the harness-invalid route, extended); (c) the
  PP-W5 validity floor (a cell < 2 valid runs drops its pair; < 3 pairs; either arm < 12 valid;
  > 40 % censored); (d) W2 does not ship in 0.16.0 **and only where §3.1's cut line was invoked
  in writing**, cited in the ledger.
- **Own-goal clause** (A:317; R:920-921): a not-adjudicated route caused by this workstream — a
  fixture broken by a 0.16 edit, a ledger regenerated by it, a harness misconfigured by its
  author — is **MISS**, with the artifact published.
- **MISS**: leg A misses its bar on a valid harness, or leg B fails, or an own-goal.
- **UNDERPOWERED**: leg A at its bar but leg B's point exceeds the margin with the bound not
  firing, or the realized CV exceeds the cap.
- **HIT**: leg A at its bar and leg B not failing. A HIT means "rows caught the registered
  laundering classes at no large loop tax", never "rows are free".

**Freeze event:** annex entry **A-1.12**, registered before any W-item merges, guarded by
`scripts/check-annex-freeze.py` (A-1.10, `CHANGELOG.md:223-236`). **Who runs it:** the 0.16.0
release PR's author before the tag, via `run-pair.sh` with W1's capture; `create-release` does not
proceed without the analysis file (the A-1.11 rule).

### 4.2 The tax question — instrument first, diagnosis second

The per-diagnostic attribution the brief asked for has been run for this draft and is in §0.2:
it attributes **nothing** to diagnostics. Two instruments follow:

- *Zero-spend, now (S1):* the arm-surface replay and the committed turn-attribution script. Done
  before A-1.12; its result is an input to the margin derivation (if a cause is found and removed
  in 0.16, PP-W-rows' leg B measures against arm A unchanged — the cause is disclosed, not
  corrected for).
- *With W1:* per-turn tool-call classes (Read / Grep / Bash-build / Edit / other) per arm per run,
  over PP-W-rows' own runs — the first epoch captured. Published as a table in the 0.16.0 release
  notes. **No 0.16 product change is justified by the 18 % until that table exists.**

### 4.3 What stays true from 0.15

No real-scale epoch unless both re-entry conditions hold (R:856-857, R:993-994). M2 stays SHOULD.
The benchmark's 1.32× stays a regression indicator (R:37-38). Register-then-merge is enforced by
the annex guard, not discipline (A-1.10).

---

## 5. Release gates — instrument, denominator, freeze point, discriminating pin

**Carried live from 0.15** (R:859-970), restated only where 0.16 changes the reading:

1. **Effect laundering, closed classes** — unchanged: six classes, one `_IsError`/`_Compiles`
   pair each; W5 adds the escaping-lambda case as a *seventh* pin **without** widening the frozen
   class list (it is a site-3 emission, not a new class).
2. **Higher-order expressiveness** — the demand ledger re-executed at the release commit; floor
   25; **new:** D-A's per-class counts are published beside the W3 denominator so a rise in
   Calor-native higher-order code is visible.
3. **Surface agreement** — as it exists (clean vs incremental); the MCP leg fires with E7.
4. **PP-E1** — adjudicated at 0.15.0; its ledger test stays in CI as a *regression* pin: leg A
   must remain 10/10 with a clean control on every 0.16 commit.
5. **Corpus compatibility** — the committed `.calr` at the 0.16 branch cut, two legs as at
   R:933-946; W3-attributable new diagnostics on the conversion subjects are separated and
   published (the E1 clause, R:941-944).
6. **Resolution floor** — 817/1248 exact, per subject, two-sided (R:947-958). W3 must not move it;
   if #1097's fix changes what binds, the ledger regenerates in the same PR with the delta
   disclosed.
7. **Index/query correctness** — the ten E5 goldens plus W8's interface-member and parity goldens
   when W8 ships.

**New in 0.16:**

8. **Harness capture.** *Instrument:* the W1 harness test (a run without a per-turn transcript is
   invalid; transcript turns = `num_turns`). *Denominator:* every run of every 0.16 epoch.
   *Freeze point:* A-1.12 names the capture as a validity condition. *Discriminating pin:* remove
   the archive step → the first epoch run is invalid → PP-W-rows reads NOT-ADJUDICATED by route (b).
9. **Conversion denominator floor.** *Instrument:* `Calor0425CorpusLedgerMatchesRecomputation`
   and the Calor0270 ledger test, exact per subject, plus a floor assertion. *Denominator:* 364
   subject modules at the pinned submodule SHAs. *Freeze point:* the floor is written into the
   ledgers' `floorRule` by the S2 PR **before** any W3 fix merges; today's values (99 enforced /
   305 bound) are the baseline it must exceed. *Discriminating pin:* re-introduce the P32
   bind-first workaround → `ModulesEnforced` drops below the floor → red. Two-sided as gate 6: a
   rise regenerates in-PR with disclosure.
10. **PP-W-rows.** *Instrument:* `effect-rows-benefit-ledger.json` + its exact-equality test +
    `ppw-analyze.py`. *Denominator:* four pairs × 5 runs/arm, two legs. *Freeze point:* A-1.12.
    *Discriminating pin:* the annex guard rejects an edit to the frozen row; dropping a pair from
    the ledger fails the test.
11. **Silent-stop coverage.** *Instrument:* W5's two `_IsReported` pins. *Denominator:* the two
    named silent paths (`EffectEnforcementPass.cs:1129`; e4:230-234). *Freeze point:* this
    document's §3.1 W5. *Discriminating pin:* revert either emission and its test fails.

A gate missing any of the three is an aspiration (R:1055-1056); every gate above names its pin.

---

