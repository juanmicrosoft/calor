# Roadmap — v0.16 "Measured Effects"

**Date:** 2026-08-27
**Status:** Draft v4 (intended final) — three adversarial rounds (§10: round 1 on v1, round 2 on
v2, round 3 on v3 — engineering APPROVE-with-fixes, measurement and plan/process NEEDS-FIXES with
one Major) with every finding applied or declined in §10. Written against the source at `7d621c0d` (main after PR #1110), before the 0.15.0 release PR
merges (`Directory.Build.props:3` reads `0.14.3`). Every number is re-measured from the tree or
the archived epoch with its source; the S1/S2 measurement tables live in
`2026-08-27-v0.16-s1-s2-measurement-notes.md` (*N:*).
**Governing inputs:** `roadmap-v0.13-v0.15.md` (*R:*), `docs/design/effect-rows-in-the-type-system.md`
(*D:*), the seven 0.15 slice notes (*e2a…ppe1:*), `docs/plans/agent-native-gates.md` (*A:*,
read-only), `v0.13-freeze-registrations.md` (*F:*), the open issue list on 2026-08-27,
`CHANGELOG.md` `[Unreleased]`, and the 2026-08-18 test-suite audit.

---

## 0. Where 0.15 left us (measured at `7d621c0d`)

### 0.1 Shipped — with two discipline breaches and one published-number correction, recorded beside the #944 precedent

- **E1–E5, M1, PP-E1 landed** (R:522-727): one `EffectResolver.Resolve(EffectResolverKey)`
  (R:524-529); eight row positions and `FunctionBoundType.Row` (R:593-608); Calor0424/0425 at the
  six D:903-910 sites plus the rank-1 solve (R:621-647); Calor0418 replaced by row charging
  (R:672-696); `ProjectIndex.EffectRows`, `calor query effects` / `impact --effects` (R:697-720).
  Elision default-on (R:976); `§SEMVER{1.x}` refused with Calor0701 (`CHANGELOG.md:303-325`).
- **Breach 1 — ES-08 never registered.** F:364-366 registers the effect-row edit script "before
  roadmap §4.2 E2 merges"; `tests/TestData/EditScripts/` holds ES-01…ES-07 and E2 merged (PRs
  #1101/#1102). Gate 3 (R:889-905) was not met as written. §6 carries it.
- **Breach 2 — R:987 said PRs #982/#981/#976 would be "merged or closed in the 0.15 kickoff
  sweep"; all three are open.** #982 is gate 3's CLI-process leg. §6 carries each.
- **Correction to a published 0.15 number.** The P32 ledger's "8 Calor0425 sites over 99 of 364
  modules" (`calor0425-corpus-ledger.json`; cited at R:654-656) counts modules that pass the
  **raw** binder bag, but the shipping compiler applies `BindingDiagnosticPolicy.
  PropagateCompilationErrors` (`Program.cs:820`; allowlist `Binding/Scope.cs:53-78`) before it
  stops (`:829-833`). Under the production rule **256** of 305 parsed modules reach the effect
  pass and Calor0425 appears in **30** of them (67 sites) (N:S2). The number was a ledger
  artifact, not a compiler fact. The committed ledger and R:654-656 stand as written (P32 is not
  an annex row); the corrected 30/256 figure is published in the next release's notes beside the
  8/99 one, and **K1** (§3.1) regenerates the ledger under the shipping rule. Found by the round-2
  engineering review; re-measured for this draft (N:S2.1, S2.2) and reproduced cell-for-cell by
  all three round-3 lenses.
- **Never built:** gate 5 leg (b)'s `compile-all-committed-calr` job (R:938-940) and gate 3's
  `Calor.Sdk` leg (R:891-893).

### 0.2 PP-E1 — HIT, and what the archive can and cannot say

- Leg A 10/10, control clean, ramp not fired; leg B point **1.1835**, lower bound **0.9012**,
  median CV **0.2746**, 40/40 valid; per-pair 1.1762 / 1.5118 / 1.1907 / 0.8984
  (`effect-rows-probe-ledger.json`; `epochs/e1-rows-parity-001/ppe1-analysis.json`). Registered
  reading: "no large tax detected", power 0.22 / 0.48 / 0.77 (ppe1:262-274; A:317).
- **The N1 pairs contain no callbacks** (ppe1:294-298); leg B measured "a stricter compiler's cost
  on ordinary code" (ppe1:271-274). **Rows' benefit is unmeasured.**
- **The harness's strict CLI compile of every build-time source state emitted zero effect-family
  diagnostics on either arm** (N:S1.1 — 49 builds, 26 treatment / 23 control, 4 vs 2 of them
  unedited observation builds; Calor0100 ×2, Calor0101 ×2, Calor0830 ×1 vs Calor0830 ×1). The
  agent's own `dotnet build` stdout is not archived (W1). The "Calor0425 noise / BCL Unknown
  charge" hypothesis is refuted on this data.
- **The gap is agent turns, and it is real:** per-pair median Δturns +3 / +6 / +4 / −1. Two
  statistics, both stated: the reviewers' *pooled mean difference* permutation p = 0.012 (turns) /
  0.08 (tokens) / 0.44 (wall-clock); this draft's *median-over-pairs of the paired mean delta*
  (with four pairs the median is the mean of the two middle deltas) p = 0.0037 / 0.0249 / 0.4375
  (N:S1.2). Sensitivity: all-naive 1.3490, one-run-corrected 1.3390, registered 1.1835.
- **S1 steps 1–2 are DONE and null** (N:S1.3): byte-identical C# and CLI text across the arms on
  all 40 archived programs; the arm diff is enumerated; the harness hook is identical across the
  two Calor arms (`run-pair.sh:502-516`, including the `${calr_block}${smoke_block}` clauses at
  `:510`). `run-pair.sh:853/867` run `claude --print --output-format json`, so no per-turn tool
  calls exist. **"Why more turns" is open and cannot be closed from the archive.**

### 0.3 The static benchmark cannot see rows

`CHANGELOG.md:391-398`: 1.32× over 30 runs, 217 benchmarks, byte-identical at 0.14.0–0.14.3;
`[Unreleased]` has no block yet. D:1768: 0 of 359 `tests/TestData` goldens had a function-typed
shape at design freeze; **1 of 359** now (E5's `QueryCorpus/project/app.calr`).
`Calor.Conversion.Tests` never runs the effect pass (D:1770). Regression indicator only (R:37-38).

### 0.4 Corpus, ledgers, and the denominator — raw bag vs production rule (exact)

| Instrument | Value | Source |
|---|---|---|
| Committed `.calr` in ledger scope | 941 = 926 + 15 spike artifacts (excluded, D:2722-2729) — *#1120 (W3(c)) adds one crash-repro fixture: 942* | `find`; ppe1:288-292 |
| D-A (Calor-native higher-order demand) | **2**; D-B (Roslyn, three subjects) **3121** over 364 files; floor 25 inert | `higher-order-demand-ledger.json` |
| Calor0425 ledger (P32), **raw-bag rule** | 8 over 99 enforced; 265 excluded (59 parse, 206 raw-bind) | `calor0425-corpus-ledger.json` |
| Calor0270 ledger, raw-bag rule | 193 across 38 of 305 bound | `calor0270-corpus-ledger.json` |
| Resolver-key ledger | 259 bound / 812 string — *#1120 (W3(c)) regenerates for its fixture: 265 / 814* | `effect-resolver-key-ledger.json`; R:532-537 |
| Metadata gate 6 | **817/1248 = 65.46 %** (129/226, 104/113, 584/909) | `metadata-binding-corpus-ledger.json` |

**The production denominator (N:S2, measured twice — in-process with the propagation policy, and
through the pinned CLI per module, agreeing per subject on every row):** of 364 subject files,
**59** fail to parse (Calor0099 36 / 0100 21 / 0117 2); **49** stop at binding under the shipped
rule (Calor0208 29 / 0250 13 / 0201 7); **256** reach the effect pass — 115 stop at Calor0410,
7 at Calor0422/0423 ("effect contract unavailable", carried by 61 / 4 modules), 52 pass the
effect pass and stop at Calor1002 (codegen), **82 compile clean end-to-end**; zero crashes.
Calor0200 / Calor0273 / Calor0932 (#1097's ICE) / a C# `null` emitted as an identifier are **not
user-visible** — never propagated (`Scope.cs:53-78`; `RoslynSyntaxVisitor.cs:8850`,
`Parser.cs:2691`, `CSharpEmitter.cs:3403`). The raw-bag counts of Draft v2 (Calor0200 in 200,
Calor0273 88, Calor0208 35, ICE 26, ICE-only 1, `null`-first 104; Calor0272 18, Calor0250 13)
describe that bag and nothing a user sees.

Two readings drive §1: rows meet real code only through conversion (D-A = 2; the converter
emits no rows, D:1780-1783), **and even after W3 the enlarged denominator holds zero rows** — no
row meets real code in 0.16. The "BCL-returned delegate" cause (`UnknownSource` +
`InvocationUndetermined`) is 0 in the 99-module ledger; its count over the 256 is what K1
produces, and the IL-rows trigger (§3.3) reads that.

### 0.5 Registered 0.15.x debts that shape the theme (full table §6)

`FunctionBoundType.Row` has no end-to-end reader (R:710-720; D:2700-2708; e5:150-164); lambda
parameters invoked in-lambda → Calor0411 (e4:255-259); index/`calor build` parity and interface
members (e5:265-275); `PropagateInstantiatedCharges` stops silently at `maxIterations = 10_000`
(`EffectEnforcementPass.cs:1129`) while `ProcessScc`'s cap of 100 reports **`Calor0600`**
(`:455`, `:484-487`) — a code from the API-strictness band (`Diagnostic.cs:522-523`,
`BreakingChangeWithoutMarker`), a pre-existing mis-banding; #1104 recursion in the nested
`EffectInferrer` (`EffectEnforcementPass.cs:2547`; cycle `:4022 → :3942 → :3827`); DEFERRED at
design merge (R:738-742): async rows (D:§11, 1922-1945), `PreconditionSuggester`,
reflection/`dynamic`, `+=`, BCL-returned delegates; SHOULD never started: E6–E9, M2 (R:729-736).

---

## 1. Theme

### 1.1 What the ladder says comes after 0.15

R:40-46 ends at 0.15; R:1068-1082 claims compositional effect safety "instrumented at fixture
scale" plus "a standing, quantitative path back to real-scale measurement"; R:993-994 keeps the
real-scale venue retired until ≥ 70 evaluable tasks and a real Calor arm exist. 0.16 starts with
two measured holes — rows' benefit never observed; the corpus-scale effect ledgers measured over a
denominator the compiler does not use — and one product hole: nothing an agent uses reads the
index but `calor query`.

### 1.2 Three candidates against §0

**A — "Effects that pay for themselves"** (measure the benefit; remove the tax; IL-derived rows).
*Benefit leg:* supported — unmeasured by construction, fixtures exist. *Tax leg:* the named cause
is refuted (§0.2); the open question needs an instrument, not product. *IL leg:* demand reads 0
over the 99-module ledger and is unmeasured over the 256 — a demand trigger tied to K1's
regeneration, not a slot. Building it now would be the designer-judgment gate that failed TIER1A
(R:441).

**B — "Index consumers"** (E6/E7/E8, R:729-734). *Supported:* E7 is fully specified, already has
gates (gate 3's MCP leg, gate 7's E7 leg, R:861-864), needs no annex registration, and is the only
candidate that puts something new in an agent's hands — **no MCP tool references `ProjectIndex`
today**; `NavigateTool.cs` is text-based (`CalorSourceHelper.Parse`, a textual reference scan, no
project-directory notion). *Draft v1 dismissed B for "no measurement"; that was the draft's
omission.* **E7 is MUST (§3.1).**

**C — "Trustworthy migration"** (#903, #1097, #901, #929, #943, #847). *Supported:* the §0.4
parse-failure leg — 59 modules a user cannot compile at all. *Against as the whole:* R:995 /
R:294-306 decline a broad campaign; the measured subset is MUST W3, not a theme.

### 1.3 Recommendation — **v0.16 "Measured Effects"**

**User-visible deliverable, one sentence:** an agent can ask the MCP server who calls a function,
what it calls, what breaks if its effects change, and what its effect row is — read from the same
index `calor build` writes — and the converter's output parses for all but two of 364 real
modules instead of all but 59.

Beneath that, the measurement spine: (1) **rows' benefit is observed** by a pre-registered proof
point on callback-heavy tasks (PP-W-rows, §4.1) whose verdict decides whether 0.17 is a rows
release; (2) **the turn gap is attributed or bounded** — per-turn capture before any further paid
epoch, S1 published; (3) **the effect ledgers mirror the shipping compiler** (K1) and the
conversion denominator moves by a pre-registered, provable amount (gate 9). Nothing in 0.16 puts
a row on real code; 0.16 measures effects honestly and makes the next measurement possible.

---

## 2. Entry gate — three spikes and one registration before any row-family `src/` change merges

0.15's pattern (R:437-513; D:1988-1996; R:762-786). No new type-system decision exists, so the
gate is spikes with numeric exits plus annex entry **A-1.12**.

**What A-1.12 blocks, exactly:** merges that change the row family under `src/` — W6-row work,
W7, and any Calor0424/0425/0404/0405 emission change — until the entry exists (the M1 rule,
R:721-727, scoped). It does **not** block W1, W3, K1, E7, W5, or the S1/S2 artifacts.

### 2.1 Spike S1 — the zero-spend replay (steps 1–2 DONE and null, N:S1)

Remaining: (3) commit `bench/phase0-agent-native/ppe1-turn-attribution.py` reproducing N:S1.1–S1.2
with an exact-equality test; (4) a **2-run pilot** (one pair, one run per arm) under
`claude --print --verbose --output-format stream-json` (Claude Code 2.1.243 requires `--verbose`
with `stream-json`), streamed as `tee transcript.jsonl | jq -c 'select(.type=="result")' >
agent.json` so `detect_invalid_run` (`run-pair.sh:42-72`, which scans all of `agent.json` for
"rate limit" / "api error" / "overloaded") keeps its input. The pilot defines the per-turn field
A-1.12 registers — **`turns.assistantMessages` = distinct assistant `message.id`** (`stream-json`
emits one event per content block; subagent turns only with `--forward-subagent-text`) — not
"equals `num_turns`". The pilot counts against §4.1's spend ceiling.
*Pass:* a per-turn transcript per run with the field defined. *Fail:* the stream cannot be
separated from `agent.json` without changing `detect_invalid_run` — then that change is W1's.

### 2.2 Spike S2 — DONE: the denominator under both rules (N:S2)

Result: the production rule enforces 256 of 305 parsed modules; converter-side fixes recover 57 of
59 parse failures; #1104 unlocks 0; #1097 is invisible to users. It sets gate 9's floor (§5)
**now**, before any W3 fix or K1 merges. **Registration-time re-run:** K1's regeneration is the
plan's own S2 record; the in-process and CLI rows must agree per subject as they do today.

### 2.3 Spike S3 — PP-W-rows fixtures, arms, margin — and the dry run

In order: (a) **A-1.12 fixes the margin population and the CV cap first** — `e1-rows-parity-001`
alone or pooled with `w5-parity-002`, decided in writing before any seeded-extension compile
(compiler outputs do not unblind agent behaviour, but the population choice must not follow the
seeds); (b) the margin is derived by the **committed** `ppe1-margin-derivation.py` with the
population added as a flag, `SIMS` raised to **≥ 3 000**, and the grid extended to 1.15/1.20 (a
bench-script change, S3 *Touches*), under the committed script's own null-redraw convention
**named in A-1.12: resample-with-replacement** (`simulate()`, `:111-140`, `random.choice(pooled[pair])`
— not a permutation); seed and `SIMS` are recorded in A-1.12; the registered rule is §4.1's
"Margin"; (c) author the six pair specs (`bench/phase0-agent-native/pairs/W-*/spec.md`) with
per-arm starters and held-out tests that observe the laundered effect; compile every seeded
extension on **both** arms and record the multisets (§4.1 table — five of six already measured
for this draft); (d) **after W1 merges, a paid dry run per A:81 — ≥ 3 runs/arm on the six pairs,
36 runs ≈ $36** — which sets **N** (runs per cell) to reach ≥ 80 % power at the **pre-registered
effect size Δ = 0.5** (§4.1 leg A), using the upper confidence bound of the dry-run variance;
the dry run sizes N and never moves the bar; (e) A-1.12 registers all of it, verified at
registration by `grep -rn "PP-W" src/` empty and no row-family `src/` diff since `7d621c0d`.
*Fail:* N for 80 % power at Δ = 0.5 exceeds the spend ceiling — the PP registers its achievable
power and arms UNDERPOWERED.

---

## 3. Ship — tiered

### 3.1 MUST

- **E7 — MCP query surface over the index (DECIDED, round 2):** a **new `calor_query` tool**
  registered at `McpMessageHandler.cs:61` beside `NavigateTool`, calling a reader **extracted
  from `QueryCommand`** (its `Execute*` are `private static` in a 598-line file), exposing
  `callers | callees | impact [--effects --row] | effects` from `ProjectIndex` (format 4.0). Not
  an extension of `NavigateTool`, which is text-based and has no project-directory notion; no MCP
  tool references `ProjectIndex` today. *Touches:* `Mcp/Tools/QueryTool.cs` (new),
  `Commands/QueryCommand.cs` (reader extraction), `Mcp/McpMessageHandler.cs`, `docs/`. *Gates:*
  gate 3's MCP leg (compile through MCP → same diagnostics and index bytes as the CLI, R:889-905)
  and gate 7's E7 leg (the effects goldens answered byte-for-byte via `calor_query` — eleven as
  the corpus stands: eight `effects` rows and three `impact-effects` rows; earlier drafts said
  "ten", which miscounted the corpus) — two
  legs, stated separately in §5. *Discriminating:* answer from an in-memory graph instead of the
  index and the gate-7 golden for the cross-module fold (`Whisper`) fails.
- **K1 — the P32 ledger mirrors the shipping rule (kickoff sweep, DECIDED; scoped to P32).**
  `Calor0425CorpusLedgerTests.cs:347-354`'s guard becomes `PropagateCompilationErrors` into a
  fresh bag, then `HasErrors`; the ledger regenerates with the cause named; schema 3 carries
  `bindRule: "propagated"` and gate 9's `floorRule`. The Calor0270 ledger is **not** touched:
  `Calor0270CorpusVolumeTests.cs:188-208` has no bind guard (it counts Infos from the raw bag over
  every parsed module — 305 — and asserts `AggregateModulesBound > 250` at `:118`); its schema
  gains `bindRule: "parsed"` so the two rules are named side by side. K1 also pins the
  **scratch-cwd rule** — the in-process measurement, like N:S2.2's CLI pass, runs with no
  `.calor-effects.json` beside the input. *Touches:* `Calor0425CorpusLedgerTests.cs`,
  `Calor0270CorpusVolumeTests.cs` (schema field only), both ledger JSONs, `CHANGELOG.md`.
  *Sequencing pin:* K1's PR asserts that `tests/Calor.Enforcement.Tests/
  EffectInferrerRecursionTests.cs` exists — W3(c) first — because K1 runs the **in-process**
  effect pass over ~157 modules the ledger has never enforced; the CLI shows **zero crashes over
  the 256 today** (N:S2.2), so the risk is confined to a test host that cannot catch a stack
  overflow. *Discriminating:* restore the raw-bag guard → `ModulesEnforced` 256 → 99 → red.
- **W1 — Per-turn capture.** `run-pair.sh` / `run-bundle.sh` archive `transcript.jsonl` per run
  (S1's mechanics), the agent's `dotnet build` stdout, and #1094's `.calor-build-state.json`;
  `pair.json` gains the additive "pre-rows control arm" definition (§4.1) and the template gains
  `<CalorPermissiveEffects>`. *Touches:* the two runners — including `run-pair.sh:289-290`,
  which today rejects any `arms.calor.config` other than `true false debug true` (exit 3) and must
  admit the registered pre-rows control arm; a `run-ppw-epoch.sh` (or a `W)` arm in
  `run-m5-epoch.sh:152-162`'s group `case`, whose `*)` fallback already takes explicit ids) that
  drives the six `W-00x` ids, since `run-ppe1-epoch.sh:56/:169` hardcodes `PAIRS=(N1-…)` /
  `--pairs "N1"` — the existing `W1-/W2-/W3-/W5A-…` pair directories do not collide (exact-id
  matching); `templates/calor-arm/CalorArm.csproj.template:17`; `tests/test_token_usage.py`
  sibling; `ppe1-margin-derivation.py` (population flag). *Pin:* a run without `transcript.jsonl` is `invalid`;
  `turns.assistantMessages` is recorded in `result.json`. *Discriminating:* delete the archive
  step → the first 0.16 epoch run is invalid → PP-W-rows route (b).
- **W2 — PP-W-rows** run and adjudicated at the 0.16.0 release commit (§4.1). *Touches:*
  `pairs/W-*`, `ppw-analyze.py` — which, unlike `ppe1-analyze.py:66` (a hardcoded pair list with
  no per-pair exclusion; `:189` is harness-invalid disclosure only), reads a **`legBPairs`** field
  from the epoch's `pins.json` so W-005's exclusion from leg B is frozen at A-1.12 rather than a
  script default — `effect-rows-benefit-ledger.json`, its exact-equality test.
  *Discriminating:* drop one pair from the ledger, or edit one frozen per-arm multiset, and
  `EffectRowsBenefitLedgerTests` fails; the annex guard rejects an edit to the A-1.12 row.
- **W3 — Converter reach + effect-pass robustness.** (a) #903 clusters 1–2 (Calor0099 dedent in
  `Migration/CalorEmitter.cs`; empty-`§IFACE` Calor0100) — the user-visible bar; (b) #1097:
  `TryInferLambdaParameterType` (`RoslynSyntaxVisitor.cs:11774-11794`) returns `null` for
  `TypeKind.Error` so no bare `?` is emitted — an ICE the CLI never surfaces, fixed for
  robustness; (c) #1104: a depth bound in the nested `EffectInferrer` (`EffectEnforcementPass.cs:
  2547`, cycle `:4022 → :3942 → :3827`) with a crash-repro pin in
  `tests/Calor.Enforcement.Tests/EffectInferrerRecursionTests.cs` over a committed fixture reduced
  from the Serilog module and enforced *without* binding. **(c) lands before K1**, pinned by K1's
  existence assertion on this test: the CLI pass shows zero crashes over the 256 (N:S2.2) and
  the two known crashers stop at a propagated Calor0250, so the residual risk is in-process only. *Discriminating:* revert (a) →
  `ExcludedParseFailed` rises above 2 → red; revert (b) → the ICE fixture test is red; revert
  (c) → the recursion test crashes the host.
- **W4 — Turn-gap attribution published.** S1's script and note; after W1, the per-turn
  tool-class table (Read / Grep / Bash-build / Edit / other) over PP-W-rows' runs. *Touches:*
  `ppe1-turn-attribution.py`, `tests/…/EpochTurnAttributionTests.cs`, the release notes. *Gate:*
  12. *Discriminating:* delete one archived run → the exact-equality test is red.
- **W5 — Silent stop made loud, in the effects band.** Reserve **Calor0406
  `EffectInferenceDidNotConverge`** (0406–0409 free at `7d621c0d`) and emit it at **both** caps —
  `PropagateInstantiatedCharges` (`:1129`, today silent) and `ProcessScc` (`:455`, today the
  mis-banded `Calor0600` string, retired at that site) — with the caps injectable so the pins
  run at cap = 2 on three-hop fixtures. Retiring `Calor0600` at `ProcessScc` changes an emitted
  code and gets a `CHANGELOG.md` line; **no test asserts the `Calor0600` string at that site**
  (`grep -rn Calor0600 tests/` hits only the code-prefix list in `TelemetryPrivacyTests.cs:95`).
  *Touches:* `Diagnostics/Diagnostic.cs`, `EffectEnforcementPass.cs:455-487, 1123-1140`,
  `tests/Calor.Enforcement.Tests/`, `CHANGELOG.md`.
  *Discriminating:* revert either emission → its `_IsReported` pin fails.

**Cut line 1.** If W3(a) overruns, W2 still runs (fixture-scale, corpus-independent); **gate 9
becomes regression-only** (`ModulesEnforced` under K1's rule must not fall; the parse-failure
leg reads 59 and the release notes name the cluster that did not land). If E7 overruns it defers
to 0.16.x **with** both gate legs — a MUST that slips is renamed in the release notes, not
silently re-tiered.

### 3.2 SHOULD

- **W6 — The real binding and effect losses on converted code:** Calor0422/0423 "effect contract
  unavailable" (61 / 4 modules) and Calor0208 (29 modules stop at binding) — binder- and
  resolver-adjacent → SHOULD by §9; if it ships, K1's ledgers regenerate in-PR with the delta.
  (Draft v2's W6 on Calor0200/0273 is deleted: those codes are never propagated.)
- **W7 — `FunctionBoundType.Row` end-to-end** (D:2700-2708; e5:150-164) and lambda-parameter rows
  (e4:255-259; e4:252-254). Row-family; blocked by A-1.12.
- **W8 — Index parity with `calor build`** (e5:265-272), interface members indexed (e5:273-275),
  the index-build effect pass measured against gate 8's envelope (e5:259-261).
- **E6** `review-packet` over the index; **E8** contract outcomes facet; **M2** the real Calor
  arm (R:735-736) — a SHOULD for the third release; W2 is not M2.

### 3.3 DEFERRED (frozen as the residual list at A-1.12)

IL-derived rows for BCL-returned delegates — *trigger:* `UnknownSource + InvocationUndetermined`
> 10 over K1's enforced set at the 0.16 branch cut (0 / 99 today; the 256-module count is K1's to
produce). Async rows — the D:1936-1942 three-clause test, **adjudicated by the maintainer in
writing at the 0.16 branch cut**. `PreconditionSuggester` on the typed CFG; reflection /
`DynamicInvoke` / `dynamic`; `+=`; `§DEL` type parameters (D:1143); rank-2 (e3b:150-155); E9 (no
design); converter-emitted rows (D:1780-1783); the ρ_body under-approximation of an escaping
lambda (e4:230-234 — reported as Calor0425 at the `§R` span on 0.15.0, so not silent; a §6 row
with a "measured silent" trigger).

---

## 4. Honest measurement

### 4.1 PP-W-rows — "with rows, fail-closed, agents launder fewer effects on callback-heavy code than under the pre-rows language as it was usable (warnings included), at no large loop tax"

**Pairs — six, three of them blind (floor two), per-arm starters, per-arm emissions measured per
cell.** The spec is arm-neutral; **the starter is not** (`after/*.calr` does not parse on v0.14.3
— Calor0100 at `<eff`), so arm A starts from the row-less `before/` programs and arm B from
`after/`, both frozen by blob SHA (`git ls-tree` at `7d621c0d`). No defect is seeded in the
starter; the task asks for an extension whose natural shortcut launders an effect, and the
held-out test asserts the pure path stays silent (captured stdout / a recording sink). Emissions
below were measured for this draft on both arms (v0.14.3 `--permissive-effects` at `63316987`;
0.15 strict at `7d621c0d`) unless marked as the round-3 measurement reviewer's.

| Pair | Starter A (`before/`) / B (`after/`) | Extension the task asks for | Arm A emission on the shortcut | Arm B emission | Class |
|---|---|---|---|---|---|
| **W-001** A3-middleware | `2d351d10` / `e5ee81e2` | a **field-stored** stage `§FLD{Func<i32>:stage:pri}` (row `§E{cw}` in B) passed to `RunTwice` from a new pure `§MT{mt003:Twice} §E{}` | only the pre-existing Calor0418 warnings on `g` — **no Calor0410** | `error Calor0410: 'Twice' uses 'cw'` (rank-1 charge) | **blind** |
| W-001s (published sibling, not adjudicated) | same | a printing `§LAM` bound by `§B` and passed to `RunTwice` | `warning Calor0410` (reviewer-measured) | error Calor0410 | warning-vs-error |
| W-002 A3-map | `9f108655` / `0885b3dd` | `MapAndReport`: a printing `§LAM` bound by `§B` and passed to `Map` | `warning Calor0410` (an inline `§LAM` as `§A` draws Calor0208 on both arms; the `§B`-bound form compiles) | error Calor0410 | warning-vs-error |
| W-003 A3-match | `1f36ea6e` / `c1ce7517` | a logging fallback via a `§B`-bound `§LAM` | `warning Calor0410` | error Calor0410 | warning-vs-error |
| **W-004** A3-callback | `f2dca4a6` / `05ddc23d` | a new pure `§MT{mt002:Peek} §E{}` invoking `onChange` (`Bump` is `§E{cw}` in both blobs; the field row `§E{cw}` exists in B only — A's field has no row) | two Calor0418 warnings on `onChange`, **no Calor0410** | `error Calor0410: 'Peek' uses 'cw'` | **blind** |
| W-005 A2 | `d49d0017` / `93ecdf16` | a `§P` pre-processor step inside `Handle` | `warning Calor0410` (reviewer-measured) | error Calor0410; **arm B's starter does not build** (exit 1, PP-E1's frozen post-E4 multiset, A:338-450) | warning-vs-error, **leg A only** |
| **W-006** A3-map | `9f108655` / `0885b3dd` | a **field-stored** stage `§FLD{Func<i32,i32>:stage:pri}` (row `§E{cw}` in B) passed to `Map` from a new `§MT{mt001:Twice} §E{alloc, mut}` | only the pre-existing Calor0418 warning on `f` — **no Calor0410** | `error Calor0410: 'Twice' uses 'cw'` | **blind** |

The same field-stored shape on A3-match is **not registrable with a module-level method group**:
passing `Zero` for `onNone` from inside a class draws `error Calor1002` on arm A (generated C#
cannot resolve it — a codegen confound unrelated to effects). With a second field
`§FLD{Func<i32>:none:pri}` (row `§E{}` in B) for `onNone` the shape compiles on both arms and is
**blind** (arm A: 2× Calor0418 only; arm B: `error Calor0410 'Twice'`) — measured in review round
4; it is available as a fourth blind cell if S3 wants one, and is recorded here so the
method-group variant is not re-proposed. **W-005 stays as a warning-vs-error, leg-A-only cell** rather than being dropped,
because it is the only MediatR-shaped pipeline in the set and its arm-A signal is the one PP-E1's
own control names; the agent must repair `Handle` on arm B before extending, so the pair's runs
are **excluded from leg B** (its `tokens.output` is archived, never entered into the ratio) and
the repair is disclosed in the spec. A2 is not re-frozen.

**Cell classes and the leg-A verdict (DECIDED, rounds 2–3):** *blind* cells — {**W-001, W-004,
W-006**}, three registered, floor **two** (any blind cell that fails route (a) drops, and below two
the PP reads NOT-ADJUDICATED); *warning-vs-error* cells — {W-002, W-003, W-005, W-001s} — where
v0.14.3 permissive already names the effect as a warning and leg A measures "agents act on an
error they would not act on as a warning". The verdict is read on the blind cells; the other
class is published beside it. **Median convention:** the leg-A statistic is the median over blind
pairs of the per-pair escape-rate delta; with an odd count it is the middle value, with an even
count the mean of the two middle values — so k = 3 is no more powerful than k = 2 (the middle
value of three), and the floor is what carries the power. Arm A's disclosed signals: Calor0418/
0419 warnings at every invocation under the waiver, and Calor0410 warnings on lambda-body
launderings. Route (a) below is worded as PP-E1's "matches the frozen multiset", not "compiles
clean".

**Bias direction, pre-registered:** leg B's ratio is expected > 1 on arm B — the agent reacts to
an error — which is the cost being measured; < 1 is disclosed as surprising. The prompt's "the
starter already builds" (`run-pair.sh:792`) is true for every leg-B pair.

**Arms — contrast (iii), with an additive arm definition, not a supersession.** v0.14.3 +
`--permissive-effects` vs v0.15.0 strict. Rejected: (i) 0.15.0 vs 0.16 (rows in both arms);
(ii) 0.15.0 strict vs permissive (Calor0424 fires in both — measures the waiver, not rows).
Confounds named: on v0.14.3 the waiver also silences Calor0410/0411 (hence "warnings included" in
the title); arm A lacks E1 re-keying and the elision default, both inert on contract-free code
(N:S1.3; R:530-531). A Calor arm under `--permissive-effects` is invalid under A:13 rule 2
(`run-pair.sh:289` reads `arms.calor.config.permissiveEffects`; `CalorArm.csproj.template:17`
hardcodes `CalorEnforceEffects=true`; `run-pair.sh:289-290` exits 3 on any other config — W1
changes that check). A:90-92 permits supersession only for a documented
empirical defect in the protocol — this is not one. **A-1.12 therefore registers an ADDITIVE arm
definition** (A-1.9.1's additive-amendment precedent): a **"pre-rows control arm"** pin row that
scopes A:13's invalidity rule to Calor *treatment* arms; for PP-W-rows only, arm A's `pair.json`
sets `permissiveEffects: true` and `controlArmKind: "pre-rows"`, the template's
`<CalorPermissiveEffects>` follows it (W1), and every other pin — model, `raw` edits, distinct
`Calor.Tasks` hashes, censoring caps — stands. Arm B runs no flags. A-1.12 also records
`pins.json` **`legBPairs`** = {W-001, W-002, W-003, W-004, W-006} — the leg-B denominator, with
W-005 named as excluded.

**Metric — two legs, nested design (A:§6.1), one verdict.**

- *Leg A — escapes.* Per pair, the escape rate on each arm (`result.json` `escapedBugs` /
  `heldoutPassed` from the effect-observing test); statistic = **median over blind pairs of the
  per-pair escape-rate delta (A − B)** (convention above), two-level cluster bootstrap. **Bar:
  the one-sided 95 % lower bound of that delta exceeds 0.** The bar is **not** a function of the
  dry-run variance: the **pre-registered effect size is Δ = 0.5** (the round-3 simulation of this
  rule at k = 2 blind pairs and 8 runs/cell gives MDD ≈ 0.45 with power 0.87 at Δ = 0.5; k = 1
  gives MDD ≈ 0.6 and a single-fixture test), and the A:81 dry run sets **N** to reach ≥ 80 %
  power at Δ = 0.5 under the upper confidence bound of its variance. Warning-vs-error cells are
  published beside the verdict with the same statistic.
- *Leg B — loop tax*, PP-E1's rule verbatim on `tokens.output` (A-1.9.1) over the leg-B pairs
  (W-001–W-004, W-006): fails iff the lower bound exceeds 1.0 **and** the point exceeds the
  margin; iterations observational.

**Margin — one derivation, one number, population named; registered as a rule.** Population
`e1-rows-parity-001` (corrected tokens, 40 valid runs, within-cell CV median 0.2746); the
committed `ppe1-margin-derivation.py`'s null redraw, **resample-with-replacement** from each
pair's pooled runs (`simulate()`, `:111-140`); `SIMS = 3 000`, `BOOT = 400`, seed **4537**: null
point median 0.9986, **p95 1.1766**; across seeds {4537, 1, 2} at 3 000 sims p95 1.1766–1.1864,
Monte-Carlo half-width **0.005**. **Registered rule: the margin is the 0.05 grid line above
(p95 + its Monte-Carlo half-width)** — 1.1766 + 0.005 = 1.182 → **1.20**. At that margin:
conjunction false-fail 1.2 % (point-only 3.5 %); power **0.51 / 0.84 / 0.97** at 1.25× / 1.4× /
1.6×. *Knife edge, disclosed with the across-seed range:* at 300 sims the same convention ranges
1.171–1.213 across seeds (round-3 measurement review), which under the rule would round to 1.25;
a shuffle-and-split permutation (not the script's convention) gives 1.200 at 3 000 sims, also
1.25 under the rule. Draft v3 had the two conventions' labels swapped; A-1.12 records the
convention by name, the seed, `SIMS`, and the number the committed script prints. The
population's own realized point (1.1835) sits at its null p95, and pooling an epoch whose arms
differ inflates the null spread — an `e1`-only calibration biases **toward leniency**; §2.3(a)
fixes the population and the CV cap (1.5 × the chosen population's median CV) before S3's
compiles, with the margin the other population would have given stated.

**Cells and spend — with the mid-epoch rule (DECIDED).** Six pairs; N per cell from the dry run.
At the archived **$1.005 per run** (N:S1.2): S1 pilot 2 runs + dry run 36 runs + a 6 × 8 × 2
main epoch 96 runs ≈ $135; **ceiling $150**, and the pilot, every retry, the dry run and the
epoch all count against it. **When the ceiling is reached the epoch stops**; the PP-W5 validity
floor then decides route (c) on what was run; the overrun is disclosed in the ledger. If N > 9
is needed, the six-pair epoch does not fit the ceiling (N = 9 → 146 runs ≈ $147 with zero
retries; N = 10 → $159) and the PP registers its achievable power.

**Four-valued outcome, precedence NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT** (A:317):
NOT-ADJUDICATED — (a) any unmutated starter fails to reproduce its frozen multiset on its arm;
(a′) fewer than two blind cells survive (a); (b) any run lacks W1's transcript; (c) the PP-W5
validity floor / distinct-hash / censoring routes (including a ceiling stop that leaves a cell
below 2 valid runs); (d) W2 does not ship in 0.16.0 **and only where §9 cut line 2 was invoked in
writing**, cited in the ledger. **Own-goal clause** (A:317): a route caused by this workstream is
MISS, with the artifact. MISS — leg A below its bar on a valid harness, or leg B fails, or an
own-goal. UNDERPOWERED — leg A at bar with leg B's point over the margin and the bound not
firing, or CV over the cap, or registered achievable power < 80 % at Δ = 0.5. HIT — leg A at bar
and leg B not failing: "rows, fail-closed, caught the registered classes at no large loop tax",
never "rows are free".

**Blindness, stated correctly:** PP-E1 executed all ten mutation cells (10/10) and S3 compiles
every seeded extension on both arms (five of six are compiled in this draft); the registration is
results-blind **only as to agent behaviour**, and the dry run's 36 runs are the first agent runs,
used to size N, never to adjudicate. **Freeze event:** A-1.12, guarded by
`check-annex-freeze.py`. **Who runs it:** the 0.16.0 release PR's author before the tag.

### 4.2 The tax question — instrument first

S1 steps 1–2 done and null; nothing attributes to diagnostics; the per-turn table over
PP-W-rows' runs is W4's deliverable. **No product change is justified by the 18 % until it exists.**

### 4.3 Unchanged from 0.15

No real-scale epoch until both re-entry conditions hold (R:856-857); M2 SHOULD; 1.32× a regression
indicator; register-then-merge enforced by A-1.10.

---

## 5. Release gates — instrument, denominator, freeze point, discriminating pin

**Carried from 0.15, restated as what exists:**

1. **Laundering, six closed classes** — unchanged (R:866-881).
2. **Higher-order demand ledger** at the release commit; floor 25 (R:882-888).
3. **Surface agreement — as it exists:** clean-vs-incremental in-process only
   (`EditScriptIdentityTests`); the CLI-process leg is PR #982 (open), the `Calor.Sdk` leg is
   unbuilt, ES-08 unregistered (§0.1). **New in 0.16 — the MCP leg (E7):** the edit-script corpus
   compiled through MCP `calor_compile` yields the same canonical diagnostics and index bytes as
   the CLI path (R:889-905). The CLI/SDK legs and ES-08 are §6 rows with triggers.
4. **PP-E1** — a regression pin: leg A stays 10/10 with a clean control on every 0.16 commit.
5. **Corpus compatibility — leg (a) only** (`tests/TestData/Benchmarks`, `samples/`,
   test-compiled `.calr`); leg (b) unbuilt (§6). W3-attributable new diagnostics are separated
   and published (R:941-944).
6. **Resolution floor** 817/1248 exact per subject, two-sided (R:947-958).
7. **Index/query goldens** — the E5 goldens; **E7 leg unconditional:** the same ones answered
   byte-for-byte through `calor_query`. Counted at E7: **eleven** effects goldens (eight
   `effects`, three `impact-effects`) inside a corpus of 28, of which 21 are tool-answerable —
   the whole tool-answerable set is on the leg, not just the effects rows. ("Ten" in earlier
   drafts was a miscount; the denominator is pinned by
   `QueryToolGateTests.TheLegCoversEveryEffectsGolden`.)

**New:**

8. **Harness capture.** *Instrument:* W1's validity test. *Denominator:* every run of every 0.16
   epoch. *Freeze:* A-1.12 names it a validity condition. *Pin:* remove the archive step → route (b).
9. **Conversion denominator — re-set on the production rule.** *Instrument:* the Calor0425
   ledger test after K1 (schema 3, `bindRule: "propagated"`, `floorRule`, scratch cwd), exact per
   subject; the Calor0270 ledger unchanged (`bindRule: "parsed"`). *Denominator:* 364 subject
   modules at the pinned SHAs. *Floor, pre-committed here from N:S2:* **`ExcludedParseFailed ≤ 2`**
   (the user-visible bar; 57 recovered) **and `ModulesEnforced ≥ 250`** under the propagated rule
   — a **regression floor, six below the observed aggregate of 256**; per subject MediatR ≥ 29 and
   serilog ≥ 84 (both **equal** to today's count — exact floors) and FluentValidation ≥ 137 (six
   below 143, where the slack sits). Expectation after W3(a): ≈ 256 + 48, published, not gated.
   *Freeze:* this document; K1's PR writes `floorRule` before any W3 fix merges.
   *NOT-ADJUDICATED route:* K1's in-process regeneration lands below 250 for a cause that is one of
   the **documented CLI-only passes** — `TypeChecker`, `PatternChecker`, `BindValidationPass`
   (Calor0250), `ReturnValidationPass` (Calor0205) at `Program.cs:760-808`, or the
   `--enforce-effects` default (`:1230`); the effect-pass construction is otherwise identical
   (`Program.cs:856-866` vs `new EffectEnforcementPass(bag)`, `EffectEnforcementPass.cs:161-162`)
   — then the floor is re-registered from K1's number with the artifact. *Pin:* revert the
   converter fix → `ExcludedParseFailed` rises → red; restore the raw-bag guard →
   `ModulesEnforced` drops to 99 → red. Two-sided as gate 6.
10. **PP-W-rows.** *Instrument:* `effect-rows-benefit-ledger.json` + exact-equality test +
    `ppw-analyze.py`. *Denominator:* six pairs × N cells × two arms, two cell classes; blind floor two. *Freeze:* A-1.12. *Pin:* the annex guard; dropping a pair fails the test.
11. **Non-convergence coverage.** *Instrument:* W5's two `_IsReported` pins at injected caps,
    both emitting Calor0406. *Denominator:* the two caps (`:455`, `:1129`). *Freeze:* §3.1 W5.
    *Pin:* revert either → red.
12. **Turn attribution.** *Instrument:* `ppe1-turn-attribution.py` + exact-equality test.
    *Denominator:* every archived epoch under `bench/phase0-agent-native/epochs/`. *Freeze:*
    A-1.12 (`turns.assistantMessages`). *Pin:* delete one run → red.

---

## 6. Carried debt — a trigger, or an unconditional venue, for every registered residual

| Item | Source | Trigger | Venue |
|---|---|---|---|
| P32 ledger gates on the raw binder bag; the published "8 over 99" | §0.1; N:S2 | — | **kickoff K1** (P32 only), after W3(c) |
| Calor0270 ledger counts Infos over every parsed module (no bind guard) | `Calor0270CorpusVolumeTests.cs:118,188-208` | — | K1 records `bindRule: "parsed"`; no regeneration |
| ES-08 never registered while E2 merged | F:364-366; §0.1 | — | kickoff sweep: register under F-3's supersession rule with the breach disclosed |
| Gate 3 CLI-process leg (PR #982 open); `Calor.Sdk` leg unbuilt | R:891-893 | E7 lands | #982 merged or closed in the kickoff sweep; SDK leg with E7 |
| PRs #981, #976 open | R:987 | — | kickoff sweep: merge, close, or re-open as issues |
| Gate 5 leg (b) never built | R:938-940 | — | 0.16.x; gate 5 claims leg (a) only until then |
| PP-E1 negative-control pin skips the effect pass on `A3-map`/`A3-match` | e3b:272-278 | — | kickoff sweep with #949 |
| `ProcessScc` emits `Calor0600` (API-strictness band) for non-convergence | `EffectEnforcementPass.cs:486`; `Diagnostic.cs:522-523` | — | **0.16 MUST W5** (Calor0406) |
| `PropagateInstantiatedCharges` cap silent | `EffectEnforcementPass.cs:1129` | — | 0.16 MUST W5 |
| #1104 recursion | `EffectEnforcementPass.cs:2547`; e3a:252-270 | — | 0.16 MUST W3(c), before K1 |
| #1097 ICE (Calor0932) — invisible to users, never propagated | N:S2; `Scope.cs:53-78` | — | 0.16 MUST W3(b), robustness |
| Calor0422/0423 "effect contract unavailable" (61 / 4 modules); Calor0208 binding stops (29) | N:S2.2 | — | 0.16 SHOULD W6 |
| `FunctionBoundType.Row` no end-to-end reader | R:710-720; D:2700-2708 | A-1.12 registered | 0.16 SHOULD W7 |
| Lambda params invoked in-lambda → Calor0411; untyped alias hop | e4:255-259; :252-254 | A-1.12 registered | 0.16 SHOULD W7 |
| Calor0410 demoted to a warning under `--permissive-effects` | e4:246-248 | PP-W-rows arm A depends on it — a change before the epoch is an own-goal | frozen through the 0.16.0 release commit; re-adjudicated after |
| ρ_body under-approximation on an escaping lambda (reported as Calor0425) | e4:230-234 | a fixture measured silent | DEFERRED |
| Index folds cross-module charges for bindable files | e5:265-272 | — | 0.16 SHOULD W8 |
| Interface methods not indexed; index-build cost unmeasured | e5:273-275; :259-261 | — | 0.16 SHOULD W8 |
| `§FLD`/`§B` rows not index positions; hover declared-only; `--json` **closed for the four E7 facets** (`callers`, `callees`, `impact`, `effects`) — `symbol`, `contracts`, `assumptions` stay text-only | e5:168-175 | E7 | `--json` clause CLOSED (E7 PR); the `§FLD`/`§B` and hover clauses carry to 0.16.x |
| Solution-level manifests not consulted by the index | e5:256-258 | a corpus solution with manifests | 0.16.x |
| Calor0419 at BCL argument sites (D-A = 2) | e4:249-251 | D-A `calor0419FunctionTyped` > 10 | 0.17 with IL rows |
| BCL-returned delegates → Unknown | D:1660-1664 | `UnknownSource + InvocationUndetermined` > 10 over K1's set | DEFERRED, demand-triggered |
| Key parameter component = inferred argument types; IL keys `FromStrings` | D:1360-1366; :1677-1687 | gate 6 must move, or IL rows trigger | 0.17 |
| Lambda `ParameterTypes` are surface spellings | D:1614-1620 | W7 | with W7 |
| Async rows | D:1922-1945 | three-clause test, maintainer, 0.16 branch cut | DEFERRED |
| `PreconditionSuggester` on typed CFG; #909 | R:739; #909 | — | 0.16.x together |
| Q1 C#-declared interface rows; Q4 Calor0425 span | D:2810-2819; :2834-2847 | IL rows / K1's never-invoked fraction | 0.17 / 0.16 branch cut |
| Q3 `Subtypes` doc-drift guard | D:2827-2833 | — | 0.16.x: a Calor13xx drift code |
| D2 (§6.2 row 6), Q2 (`eff` lookahead) | e3b:68-88; D:2820-2826 | — | closed by annotation / pin |
| 0.14 §3.3 decisions 2–3 | R:977; #1084 | a user-reported 1.x file | demand-driven (0 of 941 declare 1.x) |
| 3.5.1 null-state slice; #845; #859/#884 Z3 flake; #970 tri-state; TIER1A not-run | R:978-984 | 0.16 branch cut, maintainer, flake rate attached | 0.16.x instrument debt / release-notes rows |

---

## 7. Backlog disposition — all 30 open issues on 2026-08-27 (none P0/P1; #1082 `p2`)

| Issue | Disposition |
|---|---|
| #1104 | 0.16 MUST W3(c) — robustness, before K1; unlocks no modules |
| #1097 | 0.16 MUST W3(b) — invisible to users; ICE robustness |
| #903 | 0.16 MUST W3(a) clusters 1–2 (57 modules); cluster 3 with it if trivial, else 0.16.x |
| #1094 | 0.16 MUST W1 |
| #901 | 0.16.x; regenerates the demand ledger with disclosure |
| #929 | 0.16.x parser fix with a named fixture |
| #943 | 0.16.x; re-read against K1's enforced set for `ref`/`out` sites |
| #847 | demand-driven (R:1020) |
| #1084 | item 1 done; 2–3 demand-driven (§6) |
| #1082 (`p2`), #875 | sequenced after W3; gate 6 makes mis-sequencing visible (R:980-982) |
| #845 | 0.16.x (§6) |
| #859, #884, #959 | 0.16.x instrument debt; flake rate attached at the 0.16 branch cut |
| #965 | release-blocking for 0.16.0 if it recurs on the 0.15.0 release; #976 is its venue |
| #949 | kickoff sweep (with the PP-E1 pin rewrite) |
| #948 | 0.16.x; close if the `CHANGELOG.md:383-387` fix holds through the cycle |
| #922 | not 0.16; re-read with E7's identity needs |
| #906 | 0.16.x; widens the F-2 denominator and says so |
| #909 | 0.16.x with the `PreconditionSuggester` residual |
| #1011 (R1–R14) | continuous; R1/R2/R9 are the ones 0.16's gates lean on |
| #1030, #1031, #1032, #1042 | audit follow-ups, continuous |
| #851 | retired with the venue (R:1043) |
| #673, #709, #711 | adoption work, not release-gated (R:1006-1010) |

---

## 8. What "better than C#" means at the end of 0.16 — testable

1. **An agent can ask the MCP server** who calls a function, what breaks if its effects change,
   and what its row is — from the index, byte-identical to `calor query` (gate 7 E7 leg; gate 3
   MCP leg).
2. **On six callback-heavy tasks (three blind), rows fail-closed catch laundered effects the pre-rows language
   (warnings included) let through, at no large loop tax** — PP-W-rows HIT at the release commit,
   or the verdict published as it reads, blind and warning-vs-error cells separately.
3. **The converter's output parses for all but two of 364 real modules (57 recovered) and does
   not regress the enforced set** (gate 9), and every effect claim on real code names the
   production denominator (K1).
4. **The compiler reports when effect inference does not converge** (gate 11, Calor0406).
5. **Every paid epoch is attributable per turn** (gates 8, 12); the 0.15 gap is explained or bounded.
6. Unchanged from R:1068-1082: null safety for 0.14's closed classes; honest contracts with
   default-on elision; no real-scale claim until the re-entry conditions hold.

---

## 9. Cut lines and schedule abort

- **Binder-adjacent rule (R:745-748).** W6 and W7 are SHOULD and defer to 0.16.x without
  renegotiation. W3 touches the binder nowhere: (a) and (b) are converter-side, (c) is the effect
  pass; K1 changes a test guard. If W3(c) cannot be bounded without changing a resolution answer
  (gate 6 moves), (c) becomes SHOULD, K1 waits for it, and gate 9 keeps only its parse-failure leg.
- **Cut line 1** (§3.1): W3(a) overrun does not stop W2; gate 9 becomes regression-only; E7
  overrun re-tiers in writing.
- **Cut line 2:** if A-1.12 has not registered by the 0.16 branch cut, no row-family `src/`
  change merges (W6-row work / W7 stay out), and 0.16.0 ships **E7 + K1 + W1 + W3 + W4 (over the
  archived epochs only) + W5** under the same title; PP-W-rows moves to 0.17 with its fixtures
  frozen where they are, and route (d) cites this line.
- **Schedule abort:** if W1 is not merged by the branch cut, no paid epoch runs in 0.16;
  PP-W-rows reads NOT-ADJUDICATED by route (b) — MISS under the own-goal clause if the cause is
  this workstream's — and the release notes say so.
- **Not cut lines:** the 1.32× benchmark; PP-E1's regression pin.

---

## 10. Review record

### Round 1 (2026-08-27, on Draft v1 @ `1af259a9`) — plan/process 85 %, measurement 88 %, engineering 85 %; all NEEDS-FIXES

§0's numbers reproduced under every lens; the **round-1 engineering lens** re-executed S1 steps
1–2 (byte-identical C# and CLI text) and measured the raw-bag bind-failed histogram this draft
then re-ran with identical counts (N:S2.1, raw column). Dispositions:

| # | Finding | Disposition |
|---|---|---|
| A.1 | Title claims rows on real code | **Done** — "Measured Effects" (DECIDED) |
| A.2 | No user-visible deliverable sentence | **Done** — §1.3 |
| A.3 | E7 dismissed as "no measurement" | **Done** — E7 MUST with both gate legs; §1.2 records the omission |
| B.1 | Deleting the bind-first guard inflates the ledger; #1104 unlocks zero | **Done** — bind-first kept (DECIDED); superseded in round 2 by the propagated rule (K1) |
| B.2 | Run the first-error histogram | **Done** — N:S2.1 |
| B.3 | W3 scope; #1104 pin home; drop the "UnresolvedBoundType route" | **Done** — §3.1 W3(a)(b)(c) |
| B.4 | Calor0200/0273 binder work → SHOULD | **Done in v2; deleted in v3** (never propagated) — W6 redefined |
| B.5 | Gate 9 numeric floor; drop "doubled" | **Done** — re-set in round 2 |
| C.1 | Per-arm starters | **Done** — `before/`/`after/` blobs verified |
| C.2 | Permissive on a Calor arm is invalid; derogation or (ii) | **Done** — (iii) kept; made an additive arm definition in round 2 |
| C.3 | `after/A2.calr` fails route (a); five pairs | **Done** — A3-middleware W-001, A2 W-005; route (a) reworded |
| C.4 | Method-group seeds already warn on v0.14.3 | **Done** — cell classes; see round 2 N1 |
| C.5 | Nested leg-A statistic; MDD; ceiling | **Done** — §4.1 |
| C.6 | Starter fails on arm B | **Done** — builds on both arms; laundering in the extension |
| C.7 | Margin on `e1`; grid; knife edge | **Done** — re-derived 1.1800 → 1.20 |
| C.8 | Blindness wording | **Done** |
| D.1 | W4 gate/lines/cut line 2 | **Done** — gate 12 |
| D.2 | W5 half 1: reuse Calor0600, injectable cap | **Done in v2 as "reuse Calor0600"; corrected in round 2** — Calor0600 is mis-banded; Calor0406 reserved. Draft v2's "the reserve-a-code question is moot" was wrong. |
| D.3 | W5 half 2 not silent | **Done** — dropped; §6 row |
| D.4 | W1 stream-json mechanics | **Done** — §2.1 |
| D.5 | "M1 rule" scoped | **Done** — §2 |
| E.1–E.5 | §0 corrections; table moved to a note | **Done** — §0.2, N:S1 |
| F.1 | ES-08 breach; gate 3/5 legs; open PRs | **Done** — §0.1, §5, §6 |
| F.2 | 0410-demoted trigger; PP-E1 pin rewrite to the sweep | **Done** — §6 |
| F.3 | IL-rows trigger tied to the real floor | **Done** — §3.3, §6 |
| F.4 | 30 open issues; retro items owner + date | **Done** — §7; branch cut, maintainer |
| F.5 | Cites (R:441; `EffectInferrer` nested; A:81; route (d)) | **Done** |
| F.6 | Trim; §6 rows need a trigger or an unconditional venue | **Done** — 6 610 words by `wc -w` in v2; every §6 row carries a trigger or an unconditional venue |

**Declined:** none. **Narrowed:** C.2 — (iii) kept over (ii) because (ii) cannot separate rows
from the waiver (Calor0424 fires on both arms).

### Round 2 (2026-08-27, on Draft v2 @ `fa45eaf1`) — plan/process 88 %, measurement 90 %, engineering 80 %; all NEEDS-FIXES; every round-1 item verified applied

The **round-2 engineering lens** found the denominator artifact (raw bag vs
`PropagateCompilationErrors`) and measured the production outcome through the CLI over all 364
modules; this draft re-measured both rules (N:S2.1 in-process with the policy, N:S2.2 pinned CLI)
and reproduced its counts (49 binding stops = 29 / 13 / 7; Calor0422 in 61, 0423 in 4).
Dispositions:

| # | Finding | Disposition |
|---|---|---|
| ENG NEW-1/2 | The ledger's bind-first guard is not the shipping rule; 256 of 305 reach the effect pass in production; Calor0200/0273/`null`/0932 are not user-visible | **Done** — §0.1 correction to a published number; §0.4 and N:S2 rewritten as raw vs propagated, both re-measured; K1 kickoff item (DECIDED); gate 9 re-set (`ExcludedParseFailed ≤ 2 ∧ ModulesEnforced ≥ 250`, per-subject floors, NOT-ADJUDICATED route); "doubled"/"99" removed; #1104 sequenced before K1 with the reason in W3(c) |
| ENG NEW-3 | W6 must be about Calor0422/0423 and Calor0208; #1097 is invisible to users | **Done** — W6 redefined; #1097 row and W3(b) say so |
| ENG minor | N:S2 counts 0272 = 18, 0250 = 13 | **Done** — N:S2.1 |
| MEAS N1 | "v0.14.3 cannot charge a lambda body" is false; `§B`-bound `§LAM` warns Calor0410; inline `§LAM` as `§A` draws Calor0208 on both arms | **Done** — W-002/W-003 are explicit warning-vs-error cells reported separately (DECIDED); the table records per-arm emissions |
| MEAS N2 | W-004's `Bump` is `§E{cw}` in both blobs; the blind shape is a new pure `Peek` invoking `onChange` | **Done** — W-004 rewritten; held-out test observes `Peek` |
| MEAS N3 | Dry run per A:81 (option a); the derogation is not a §7 supersession | **Done** — §2.3(d): 30-run dry run after W1, inside the ceiling; additive "pre-rows control arm" pin row scoping A:13 to treatment arms (A-1.9.1 precedent) |
| MEAS N4 | Population and CV cap fixed before S3's compiles | **Done** — §2.3(a), §4.1 Margin |
| MEAS N5 | Name both permutation statistics | **Done** — §0.2 (pooled mean difference 0.012/0.08/0.44; median-over-pairs 0.0037/0.0249/0.4375) |
| MEAS N6 | Arm A's signals include Calor0410 warnings on lambda launderings | **Done** — §4.1 |
| PLAN N1 | Calor0600 is API-strictness band; reserve Calor0406 for both caps; §6 row for the mis-banding | **Done** — W5, gate 11, §6 (DECIDED) |
| PLAN N2 | Mid-epoch ceiling rule | **Done** — §4.1 "Cells and spend" (DECIDED) |
| PLAN N3 | Redraw convention flips the grid line; derive from the committed script with a population flag | **Done** — §2.3(b), §4.1 Margin; script change in W1 *Touches* |
| PLAN N4 | Gate 3 MCP leg stated separately from gate 7 E7 leg | **Done** — E7 item, gates 3 and 7 |
| PLAN N5 | W2 *Discriminating* line | **Done** |
| PLAN N6 | F.6 wording | **Done** — §6 header and round-1 F.6 row |
| PLAN N7 | §8.3 restated; cut line 1 gate 9 regression-only | **Done** |
| PLAN N8 | Name the per-turn field in A-1.12 | **Done** — `turns.assistantMessages` (§2.1, gate 12) |
| PLAN N9 | Correct D.2; attribute the S2 reproductions | **Done** — round-1 D.2 row; N:S2 attribution (round-1 engineering: raw histogram; round-2 engineering: propagated/CLI, with #1097 applied MediatR 27 → 100 total under the raw rule) |
| E7 open question | New `calor_query` tool at `McpMessageHandler.cs:61`; reader extracted from `QueryCommand` | **Done** — §3.1 E7 (DECIDED) |
| minor | Harness hook carries `${calr_block}${smoke_block}`, identical across arms | **Done** — §0.2 |

**Declined:** none. **Narrowed:** none.

**Open questions for round 3** were answered by round 3 (below).

### Round 3 (2026-08-27, on Draft v3 @ `8c838430`) — engineering APPROVE-with-fixes 85 %, measurement NEEDS-FIXES 90 %, plan/process NEEDS-FIXES 90 % (one Major); all three reproduced the two-rule denominator cell-for-cell (256 / 59 / 49; Calor0425 in 30 modules / 67 sites; zero crashes)

| # | Finding | Disposition |
|---|---|---|
| 1 (Major, all lenses) | Only one of five cells is blind (W-001's lambda extension and W-005's `§P` both warn Calor0410 on arm A); k = 1 → MDD ≈ 0.6; k = 2 → MDD ≈ 0.45 (power 0.87 at Δ = 0.5); k = 3 no better (median convention) | **Done** — W-001 re-registered as the field-stored-callback-to-`RunTwice` shape (measured blind for this draft on both arms; the lambda extension kept as the published sibling W-001s); the field shape measured on A3-map (**blind** → W-006) and A3-match (not registrable: arm A draws `error Calor1002` on the module-level method group `Zero` from inside a class); blind = {W-001, W-004, W-006}, floor two; median convention (odd/even) stated; Δ = 0.5 pre-registered, the dry run sizes N |
| 2 | W-005's arm-B starter does not build; the repair confounds leg B | **Done** — W-005 kept as a warning-vs-error, leg-A-only cell (the only MediatR-shaped pipeline, and its arm-A signal is PP-E1's own control); its runs are excluded from leg B and the repair is disclosed; A2 not re-frozen |
| 3 | Margin labels inverted (the script is with-replacement); seed-fragile; 1.1800 stale | **Done** — convention named (with-replacement, `simulate()` `:111-140`); re-run at `SIMS = 3 000` for three seeds (p95 1.1766–1.1864, half-width 0.005); rule registered as "the 0.05 grid line above p95 + Monte-Carlo half-width" → 1.20; knife edge disclosed with the 300-sim across-seed range and the permutation alternative; S3 raises `SIMS` in the committed script |
| 4 | K1 scope: the 0270 test has no bind guard; Touches/Discriminating; sequencing pin; scratch-cwd rule; NOT-ADJUDICATED route names the CLI-only passes | **Done** — K1 scoped to P32; 0270 gets `bindRule: "parsed"`; Touches/Discriminating lines; K1's PR asserts `EffectInferrerRecursionTests` exists; scratch-cwd pin; gate 9's route names `TypeChecker` / `PatternChecker` / `BindValidationPass` / `ReturnValidationPass` (`Program.cs:760-808`) and the `--enforce-effects` default (`:1230`), construction otherwise identical (`:856-866`; `EffectEnforcementPass.cs:161-162`) |
| 5a | Gate 9 per-subject sentence wrong (serilog 84 equals today; FV six below) | **Done** — "regression floor, six below the observed aggregate"; per-subject exact/slack stated |
| 5b | P32 is not an annex row; the 0.15.0 notes never printed 99 | **Done** — §0.1 reworded: ledger and R:654-656 stand; 30/256 published beside 8/99 in the next release's notes |
| 5c | W5's Calor0600 → 0406 is user-visible; no test asserts the string | **Done** — CHANGELOG line; `grep` result stated (`TelemetryPrivacyTests.cs:95` only) |
| 5d | `run-pair.sh:510` not `:511` | **Done** |
| 5e | W-004 row: A's field has no row | **Done** |
| NEW-4 | Leg A's bar must not depend on dry-run variance | **Done** — bar = lower bound > 0; Δ = 0.5 fixed; N from the dry run |

**Declined:** none. **Narrowed:** item 2 — kept W-005 (leg A only) rather than dropping it, with
the one-sentence reason in §4.1.

### Round 4 (2026-08-27, on Draft v4 @ `417b4d55`) — plan/process APPROVE 92 %, engineering APPROVE 90 %, measurement APPROVE 92 %

One engineering minor, applied on the branch without re-review: W1/W2 *Touches* now name the
harness mechanics the sixth pair and W-005's leg-B exclusion need — a `run-ppw-epoch.sh` (or `W)`
arm) for the six `W-00x` ids (`run-ppe1-epoch.sh:56/:169` hardcodes N1; `run-m5-epoch.sh:152-162`
resolves groups by `case` with an explicit-id fallback; no collision with the `W1-…W5C-` pair
directories), `ppw-analyze.py` reading `legBPairs` from `pins.json` (named in A-1.12; `ppe1-analyze.py:66`
hardcodes its list), and `run-pair.sh:289-290`'s config rejection cited by the derogation. The
measurement lens returned **APPROVE 92 %** with every registered number re-derived (W-006 blind on
both arms; margin at `SIMS = 3 000` across three seeds: p95 1.1766–1.1864, half-width 0.005 →
1.20; false-fail 0.012; power 0.514 / 0.837 / 0.973; leg-A conventions; `legBPairs` as a registered
leg-B denominator of five) and two wording residuals, both applied: the A3-match field shape is
registrable with a field for `onNone` (a fourth blind cell, §4.1), and the ceiling cut-over is
N > 9, not N > 8 (§4.1).

**Remaining open questions (none block registration):** (1) whether the 0.15.0 release PR's
benchmark block moved; (2) whether the A3-match `Calor1002` confound is a converter/emitter
defect worth an issue (a module-level method group referenced from a class body) — filed at S3
if it reproduces on `after/` with the row declared.
