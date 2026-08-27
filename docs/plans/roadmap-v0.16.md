# Roadmap — v0.16 "Measured Effects"

**Date:** 2026-08-27
**Status:** Draft v2 — Draft v1 (2026-08-27, §10 round 1: three adversarial lenses, all
NEEDS-FIXES) revised with every finding applied or declined in §10. Written against the source at
`7d621c0d` (main after PR #1110), before the 0.15.0 release PR merges (`Directory.Build.props:3`
reads `0.14.3`). Every number is re-measured from the tree or the archived epoch, with its source;
the S1/S2 measurement tables live in `2026-08-27-v0.16-s1-s2-measurement-notes.md` (*N:*).
**Governing inputs:** `roadmap-v0.13-v0.15.md` (*R:*), `docs/design/effect-rows-in-the-type-system.md`
(*D:*), the seven 0.15 slice notes (*e2a…ppe1:*), `docs/plans/agent-native-gates.md` (*A:*,
read-only), `v0.13-freeze-registrations.md` (*F:*), the open issue list on 2026-08-27,
`CHANGELOG.md` `[Unreleased]`, and the 2026-08-18 test-suite audit.

---

## 0. Where 0.15 left us (measured at `7d621c0d`)

### 0.1 Shipped, and two discipline breaches to record beside the #944 precedent

- **E1–E5, M1, PP-E1 landed** (R:522-727): one `EffectResolver.Resolve(EffectResolverKey)`
  (R:524-529); eight row positions and `FunctionBoundType.Row` (R:593-608); Calor0424/0425 at the
  six D:903-910 sites plus the rank-1 solve (R:621-647); Calor0418 replaced by row charging
  (R:672-696); `ProjectIndex.EffectRows`, `calor query effects` / `impact --effects` (R:697-720).
  Elision default-on (R:976); `§SEMVER{1.x}` refused with Calor0701 (`CHANGELOG.md:303-325`).
- **Breach 1 — ES-08 never registered.** F:364-366 registers the effect-row edit script "before
  roadmap §4.2 E2 merges"; `tests/TestData/EditScripts/` holds ES-01…ES-07 only and E2 merged
  (PRs #1101/#1102). Gate 3 (R:889-905) was therefore not met as written. §6 carries it.
- **Breach 2 — R:987 said PRs #982/#981/#976 would be "merged or closed in the 0.15 kickoff
  sweep"; all three are open.** #982 is gate 3's CLI-process leg. §6 carries each.
- **Never built:** gate 5 leg (b)'s `compile-all-committed-calr` job (R:938-940; no workflow
  mentions it) and gate 3's `Calor.Sdk` leg (R:891-893).

### 0.2 PP-E1 — HIT, and what the archive can and cannot say

- Leg A 10/10, control clean, ramp not fired; leg B point **1.1835**, lower bound **0.9012**,
  median CV **0.2746**, 40/40 valid; per-pair 1.1762 / 1.5118 / 1.1907 / 0.8984
  (`effect-rows-probe-ledger.json`; `epochs/e1-rows-parity-001/ppe1-analysis.json`). Registered
  reading: "no large tax detected", power 0.22 / 0.48 / 0.77 (ppe1:262-274; A:317).
- **The N1 pairs contain no callbacks** (ppe1:294-298); leg B measured "a stricter compiler's cost
  on ordinary code, not rows' cost on rows-using code" (ppe1:271-274). **Rows' benefit is
  unmeasured.**
- **The harness's strict CLI compile of every build-time source state emitted zero effect-family
  diagnostics on either arm** (N:S1.1 — 49 builds, 26 treatment / 23 control, 4 vs 2 of them
  unedited observation builds; only Calor0100 ×2, Calor0101 ×2, Calor0830 ×1 vs Calor0830 ×1). The
  agent's own `dotnet build` stdout is not archived (W1). The brief's "Calor0425 noise / BCL
  Unknown charge" hypothesis is refuted on this data.
- **The gap is agent turns, and it is real:** per-pair median Δturns +3 / +6 / +4 / −1;
  within-pair permutation p = **0.004** (turns), 0.025 (tokens), 0.449 (wall-clock) (N:S1.2).
  Sensitivity: all-naive 1.3490, one-run-corrected 1.3390, registered 1.1835 (N:S1.2).
- **S1 steps 1–2 are DONE and null** (N:S1.3): byte-identical C# and CLI text across the arms on
  all 40 archived programs; the arm diff is enumerated. `run-pair.sh:853/867` run
  `claude --print --output-format json`, so no per-turn tool calls exist. **"Why more turns" is
  open and cannot be closed from the archive.**

### 0.3 The static benchmark cannot see rows

`CHANGELOG.md:391-398`: 1.32× over 30 runs, 217 benchmarks, byte-identical at 0.14.0–0.14.3
(`:392/:412/:450/:515`); `[Unreleased]` has no block yet. D:1768: 0 of 359 `tests/TestData` goldens
had a function-typed shape at design freeze; **1 of 359** now (`QueryCorpus/project/app.calr`,
E5's). `Calor.Conversion.Tests` never runs the effect pass (D:1770). Regression indicator only (R:37-38).

### 0.4 Corpus, ledgers, and the denominator (exact)

| Instrument | Value | Source |
|---|---|---|
| Committed `.calr` in ledger scope | 941 = 926 + 15 spike artifacts (excluded, D:2722-2729) | `find`; ppe1:288-292 |
| D-A (Calor-native higher-order demand) | **2**; D-B (Roslyn, three subjects) **3121** over 364 files; floor 25 inert | `higher-order-demand-ledger.json` |
| Calor0425 corpus ledger (P32) | **8** over **99 enforced** of 364; 265 excluded (59 parse-failed, 206 bind-failed); causes `InvocationRowless` 4, `ExternalBase` 4, **`UnknownSource` 0, `InvocationUndetermined` 0** | `calor0425-corpus-ledger.json` |
| Calor0270 ledger | 193 across 38 of **305** bound modules | `calor0270-corpus-ledger.json` |
| Resolver-key ledger | 259 bound / 812 string (E1 2c baseline 202/751 + the 40 archives; E2 moved nothing on the 886) | `effect-resolver-key-ledger.json`; R:532-537 |
| Metadata gate 6 | **817/1248 = 65.46 %** (129/226, 104/113, 584/909) | `metadata-binding-corpus-ledger.json` |

**The denominator is bind-first by design, and stays so (Draft v1 round 1, DECIDED).** The
shipped compiler stops at binder errors (`Program.cs:830-834`), the ledger test refuses unbound
modules (`Calor0425CorpusLedgerTests.cs:347-354`) and asserts `AggregateModulesExcluded > 0`
(`:181-183`); e3a:266-268 calls bind-first the correct denominator. **S2's histogram** (N:S2):
of the 206 bind-failed modules, **200 carry Calor0200** (member access on converted/external
receivers; a C# `null` emitted as an identifier is the *first* error in 104), 88 Calor0273, 35
Calor0208, 26 the #1097 ICE (Calor0932) — and **1** module is ICE-only. The 59 parse failures are
Calor0099 36 / Calor0100 21 / Calor0117 2 (#903's three clusters). **#1104 unlocks zero modules.**
Converter-side fixes provably reach **100** enforced; the expected reach is ≈ 119 (N:S2).

Two readings drive §1: rows meet real code only through conversion (D-A = 2; the converter emits
no rows, D:1780-1783), **and even after W3 the enlarged denominator holds zero rows** — so no row
meets real code in 0.16. The "BCL-returned delegate" cause is 0 in the bind-clean quartile, which
is the least likely place to find it: FluentValidation's 236 delegate-typed declarations
(`higher-order-demand-ledger.json` `dB.perSubject`) sit largely in its 142 bind-failed modules.

### 0.5 Registered 0.15.x debts that shape the theme (full table §6)

`FunctionBoundType.Row` has no end-to-end reader (R:710-720; D:2700-2708; e5:150-164); lambda
parameters invoked in-lambda → Calor0411 (e4:255-259); index/`calor build` parity and interface
members (e5:265-275); `PropagateInstantiatedCharges` stops silently at `maxIterations = 10_000`
(`EffectEnforcementPass.cs:1129`) while `ProcessScc`'s cap of 100 already reports **Calor0600**
(`:455`, `:484-487`); #1104 recursion in the nested `EffectInferrer`
(`EffectEnforcementPass.cs:2547`; cycle `:4022 → :3942 → :3827`); DEFERRED at design merge
(R:738-742): async rows (D:§11, 1922-1945), `PreconditionSuggester`, reflection/`dynamic`,
`+=`, BCL-returned delegates; SHOULD never started: E6–E9, M2 (R:729-736).

---

## 1. Theme

### 1.1 What the ladder says comes after 0.15

R:40-46 ends at 0.15; R:1068-1082 claims compositional effect safety "instrumented at fixture
scale" plus "a standing, quantitative path back to real-scale measurement"; R:993-994 keeps the
real-scale venue retired until ≥ 70 evaluable tasks and a real Calor arm exist. 0.16 starts with
two measured holes — rows' benefit never observed; every corpus-scale effect claim resting on 99
of 364 modules — and one product hole: nothing an agent uses reads the index but `calor query`.

### 1.2 Three candidates against §0

**A — "Effects that pay for themselves"** (measure the benefit; remove the tax; IL-derived rows).
*Benefit leg:* supported — unmeasured by construction, fixtures exist. *Tax leg:* the named cause
is refuted (§0.2); the open question needs an instrument, not product. *IL leg:* demand reads 0
where it can be measured, and the place it would live is unmeasured (§0.4) — a demand trigger
tied to the W3 denominator, not a slot. Building it now would be the designer-judgment gate that
failed TIER1A (R:441).

**B — "Index consumers"** (E6/E7/E8, R:729-734). *Supported:* E7 is fully specified (callers /
callees / impact / effects over the index), already has gates (gate 3's MCP leg, gate 7's E7 leg,
R:861-864), needs no annex registration, and is the only candidate that puts something new in an
agent's hands — MCP `calor_navigate` neither reads the index nor exposes callers/impact
(R:410-411). *Draft v1 dismissed B for "no measurement"; that was the draft's omission — the
E7 gates exist and are stated in §5.* What B lacks is a proof point; what it does not need is a
theme. **E7 is MUST (§3.1).**

**C — "Trustworthy migration"** (#903, #1097, #901, #929, #943, #847). *Supported:* the §0.4
denominator. *Against as the whole:* R:995 / R:294-306 decline a broad campaign; its measured
subset is the converter-side reach S2 priced (100 provable, ≈ 119 expected) — MUST W3, not a theme.

### 1.3 Recommendation — **v0.16 "Measured Effects"**

**User-visible deliverable, one sentence:** an agent can ask the MCP server who calls a function,
what it calls, what breaks if its effects change, and what its effect row is — read from the same
index `calor build` writes — and the converter's output parses and binds for more of the real
code it is pointed at.

Beneath that, the measurement spine: (1) **rows' benefit is observed** by a pre-registered proof
point on callback-heavy tasks (PP-W-rows, §4.1) whose verdict decides whether 0.17 is a rows
release; (2) **the turn gap is attributed or bounded** — per-turn capture in the harness before
any further paid epoch, S1 published; (3) **the conversion denominator moves by a pre-registered,
provable amount** (gate 9). The title claims what §0.4 allows: nothing in 0.16 puts a row on real
code; 0.16 measures effects and makes the next measurement possible.

---

## 2. Entry gate — three spikes and one registration before any row-family `src/` change merges

0.15's pattern (R:437-513; D:1988-1996; R:762-786). No new type-system decision exists, so the
gate is spikes with numeric exits plus annex entry **A-1.12**.

**What A-1.12 blocks, exactly:** merges that change the row family under `src/` — W6, W7, and any
Calor0424/0425/0404/0405 emission change — until the entry exists (the M1 rule, R:721-727,
scoped). It does **not** block W1 (harness), W3 (converter / effect-pass robustness), E7, W5, or
the S1/S2 artifacts.

### 2.1 Spike S1 — the zero-spend replay (steps 1–2 DONE and null, N:S1)

Remaining: (3) commit `bench/phase0-agent-native/ppe1-turn-attribution.py` reproducing N:S1.1–S1.2
with an exact-equality test; (4) a **2-run pilot** (one pair, one run per arm) under
`claude --print --verbose --output-format stream-json` (Claude Code 2.1.243 requires `--verbose`
with `stream-json`), streamed as `tee transcript.jsonl | jq -c 'select(.type=="result")' >
agent.json` so `detect_invalid_run` (`run-pair.sh:42-72`, which scans the whole of `agent.json`
for "rate limit" / "api error" / "overloaded") keeps its input. The pilot **defines the turn
count** (distinct assistant `message.id`; `stream-json` emits one event per content block, and
subagent turns appear only with `--forward-subagent-text`) — not "equals `num_turns`".
*Pass:* a per-turn transcript per run with a documented turn definition. *Fail:* the stream cannot
be separated from `agent.json` without changing `detect_invalid_run` — then that change is W1's.

### 2.2 Spike S2 — DONE: the first-error histogram (N:S2)

Result: 100 provable / ≈ 119 expected / 0 from #1104. It sets gate 9's floor (§5) **now**, before
any W3 fix merges. The residual question S2 leaves is binder-side (Calor0200 on Lossy receivers,
200 modules) and is SHOULD by §9.

### 2.3 Spike S3 — PP-W-rows fixtures, arms and margin, blind as to agent behaviour

Author five pair specs (`bench/phase0-agent-native/pairs/W-*/spec.md`, the N1 shape) with per-arm
starters and held-out tests that observe the laundered effect; compile every starter and every
seeded variant on **both** arms and record the exact diagnostic multisets; run the margin
derivation on the chosen population (§4.1, "Margin") with the grid extended to 1.15/1.20; compute
leg A's minimum detectable difference under the two-level cluster bootstrap and set the cell
count from A:81 (≥ 80 % power using the **upper confidence bound of the estimated variance**,
dry run ≥ 3 runs/arm on ≥ 5 pairs). *Pass:* A-1.12 registers all of it with the arm-A derogation
(§4.1) and the API-spend ceiling; verified at registration by `grep -rn "PP-W" src/` empty and no
row-family `src/` diff since `7d621c0d`. *Fail:* the cell count for 80 % power exceeds the spend
ceiling — the PP registers with its achievable power stated and the UNDERPOWERED branch armed.

---

## 3. Ship — tiered

### 3.1 MUST

- **E7 — MCP query surface over the index:** `calor_query` (or `calor_navigate` extended) exposing
  `callers | callees | impact [--effects --row] | effects`, reading `ProjectIndex` (format 4.0)
  and answering byte-for-byte what `calor query` answers. *Touches:* `Mcp/Tools/NavigateTool.cs`
  or a sibling, `Commands/QueryCommand.cs` (shared reader), `docs/`. *Gates:* gate 3's MCP leg
  (the edit-script corpus through the MCP surface), gate 7's E7 leg (the ten effects goldens
  answered identically via MCP). *Discriminating:* answer from the in-memory graph instead of the
  index and the gate-7 MCP golden for the cross-module fold (`Whisper`) fails.
- **W1 — Per-turn capture.** `run-pair.sh` / `run-bundle.sh` archive `transcript.jsonl` per run
  (S1's mechanics), the agent's `dotnet build` stdout, and #1094's `.calor-build-state.json`;
  `pair.json` gains the arm-A derogation field §4.1 names. *Touches:* the two runners,
  `templates/calor-arm/CalorArm.csproj.template:17`, `tests/test_token_usage.py` sibling.
  *Pin:* a run directory without `transcript.jsonl` is `invalid`; the transcript's turn count (S1's
  definition) is recorded in `result.json`. *Discriminating:* delete the archive step → the first
  0.16 epoch run is invalid → PP-W-rows route (b).
- **W2 — PP-W-rows** run and adjudicated at the 0.16.0 release commit (§4.1). *Touches:*
  `pairs/W-*`, `ppw-analyze.py`, `effect-rows-benefit-ledger.json`, its exact-equality test.
- **W3 — Converter reach + effect-pass robustness.** (a) #903 clusters 1–2 (Calor0099 dedent
  emission in `Migration/CalorEmitter.cs`; empty-`§IFACE` Calor0100); (b) #1097:
  `TryInferLambdaParameterType` (`RoslynSyntaxVisitor.cs:11774-11794`) returns `null` for
  `TypeKind.Error` so no bare `?` is emitted; (c) #1104: a depth bound in the nested
  `EffectInferrer` (`EffectEnforcementPass.cs:2547`, cycle `:4022 → :3942 → :3827`) with a
  crash-repro pin — **in `tests/Calor.Enforcement.Tests/EffectInferrerRecursionTests.cs`, over a
  committed fixture reduced from the Serilog module and enforced *without* binding**, because the
  ledger test refuses unbound modules and must keep doing so. *Pin:* gate 9. *Discriminating:*
  revert (a) → `ExcludedParseFailed` rises above 2 → red; revert (b) → MediatR `ModulesEnforced`
  drops below 27 → red; revert (c) → the recursion test crashes the host.
- **W4 — Turn-gap attribution published.** S1's script and note; after W1, the per-turn
  tool-class table (Read / Grep / Bash-build / Edit / other) over PP-W-rows' runs. *Touches:*
  `ppe1-turn-attribution.py`, `tests/…/EpochTurnAttributionTests.cs`, the 0.16.0 release notes.
  *Gate:* 12. *Discriminating:* delete one archived run → the exact-equality test is red.
- **W5 — Silent stop made loud.** `PropagateInstantiatedCharges` reports **Calor0600** at its cap
  (the `ProcessScc` text, `:484-487`; no new code), with the cap injectable so the pin runs at
  cap = 2 on a three-hop fixture. *Touches:* `EffectEnforcementPass.cs:1123-1140`,
  `tests/Calor.Enforcement.Tests/`. *Discriminating:* revert the report → `_IsReported` fails.

**Cut line 1.** If W3(a) overruns, W2 still runs (fixture-scale, corpus-independent); gate 9
reads its **(b)+(c) floor** (MediatR ≥ 27; aggregate ≥ 100) and the release notes name the
cluster that did not land. If E7 overruns it defers to 0.16.x **with** its two gate legs — a MUST
that slips is renamed in the release notes, not silently re-tiered.

### 3.2 SHOULD

- **W6 — Binder on Lossy receivers:** Calor0200 (200 modules) / Calor0273 (88) on
  converted/external receivers; the C# `null`-as-identifier emission (104 first errors) may be
  converter-side and is triaged first. Binder-adjacent → SHOULD by §9; if it ships, gate 9's ledger
  regenerates in-PR with the delta disclosed.
- **W7 — `FunctionBoundType.Row` end-to-end** (D:2700-2708; e5:150-164) and lambda-parameter rows
  (e4:255-259; e4:252-254). Row-family; blocked by A-1.12.
- **W8 — Index parity with `calor build`** (e5:265-272), interface members indexed (e5:273-275),
  the index-build effect pass measured against gate 8's envelope (e5:259-261).
- **E6** `review-packet` over the index; **E8** contract outcomes facet; **M2** the real Calor
  arm (R:735-736) — a SHOULD for the third release; W2 is not M2.

### 3.3 DEFERRED (frozen as the residual list at A-1.12)

IL-derived rows for BCL-returned delegates — *trigger:* `UnknownSource + InvocationUndetermined`
> 10 over gate 9's enforced set at the 0.16 branch cut (today 0 / 99). Async rows — the
D:1936-1942 three-clause test, **adjudicated by the maintainer in writing at the 0.16 branch cut**
(the 0.15.0 retro has no date). `PreconditionSuggester` on the typed CFG; reflection /
`DynamicInvoke` / `dynamic`; `+=`; `§DEL` type parameters (D:1143); rank-2 (e3b:150-155); E9
(no design); converter-emitted rows (D:1780-1783); ρ_body under-approximation of an escaping
lambda (e4:230-234 — **not silent**: 0.15.0 emits Calor0425 "return of 'Wrap' is function-typed
with no effect row" at the `§R` span; the under-approximation is observable only through the
private `_lambdaBodyRows`, so it is a §6 row with a trigger, not a MUST).

---

## 4. Honest measurement

### 4.1 PP-W-rows — "with rows, fail-closed, agents launder fewer effects on callback-heavy code than under the pre-rows language as it was usable, at no large loop tax"

**Pairs — five (A:81's floor), per-arm starters.** The spec is arm-neutral; **the starter is
not**: the `after/*.calr` fixtures do not parse on v0.14.3 (Calor0100 at `<eff`), so arm A starts
from the row-less `before/` programs and arm B from `after/`. Both are frozen by blob SHA
(`git ls-tree` at `7d621c0d`):

| Pair | Shape | Arm A starter (`before/`) | Arm B starter (`after/`) | Laundering opportunity in the EXTENSION |
|---|---|---|---|---|
| W-001 | middleware `RunTwice<eff e>` + `Handle` | `A3-middleware.calr` `2d351d10` | `A3-middleware.calr` `e5ee81e2` | add a timing/logging stage; the pure caller's `§E{}` must stay honest |
| W-002 | `Map<eff e>` over a list | `A3-map.calr` `9f108655` | `A3-map.calr` `0885b3dd` | add a `MapAndReport` that passes a **lambda** whose body prints |
| W-003 | `Match` combinator | `A3-match.calr` `1f36ea6e` | `A3-match.calr` `c1ce7517` | add a fallback branch that logs, invoked through a **lambda** |
| W-004 | `§FLD{Action<i32>:onChange}` | `A3-callback.calr` `f2dca4a6` | `A3-callback.calr` `05ddc23d` | add a subscriber that writes; `Bump` is declared `§E{}` |
| W-005 | MediatR-shaped pipeline `Handle(request, next)` | `A2.calr` `d49d0017` | `A2.calr` `93ecdf16` | add a pre-processor step that prints |

`after/A2.calr` does **not** compile clean on 0.15.0 (exit 1: Calor0410 'unknown' + 2× Calor0411
— the PP-E1 post-E4 multiset, A:338-450); its control is therefore that multiset, and route (a)
below is worded as PP-E1's "matches the frozen multiset", not "compiles clean".

**Design change from Draft v1 (round 1, C.6): the starter builds on both arms.** No defect is
seeded in the starter. The task asks for an extension whose *natural shortcut* launders an effect
(a printing lambda handed to a pure-declared combinator; a writing subscriber under a pure
`Bump`); the held-out test asserts the pure path stays silent (captured stdout / a recording
sink). The compiler on arm B catches what the agent writes; arm A's compiler cannot. Leg B then
charges arm B for *reacting to a diagnostic*, which is the cost being measured — **pre-registered
bias direction: leg B's ratio is expected > 1 on arm B; a ratio < 1 would be surprising and is
disclosed as such.** The prompt's "the starter already builds" (`run-pair.sh:792`) stays true.

**What v0.14.3 already catches — disclosed per pair, measured at S3, not assumed.** Under
`--permissive-effects` v0.14.3 emits `warning Calor0410 … uses effect 'cw'` on a *method-group*
printing callback (Draft v1 review, C.4); W-002/W-003 therefore seed through **lambda bodies**,
and W-004 through a field callback, which v0.14.3 cannot charge. S3 compiles every seeded
extension on arm A and records the multiset; any pair where arm A draws a Calor0410 *naming the
effect* is published as "warning-vs-error" and its leg-A contribution reported separately. Arm A
also sees Calor0418/0419 **warnings** at every invocation under the waiver — an arm-visible
signal, disclosed.

**Arms — contrast (iii), with the derogation it needs.** v0.14.3 + `--permissive-effects` vs
v0.15.0 strict. Rejected: (i) 0.15.0 vs 0.16 (rows in both arms — a tax re-run, not a benefit
test); (ii) 0.15.0 strict vs permissive (Calor0424 fires in both — measures the waiver, not rows).
Chosen (iii) is the only contrast that isolates *having rows*; its confounds are named: on v0.14.3
the waiver also silences Calor0410/0411, so **leg A measures "effect checking with rows,
fail-closed" against "the pre-rows language as an agent could actually use it" — not rows in
isolation** — hence the title above, and arm A additionally lacks E1 re-keying and the elision
default, both measured inert on contract-free code (N:S1.3; R:530-531).
**Derogation (DECIDED, round 1 C.2):** a Calor arm under `--permissive-effects` is an invalid run
under the frozen pin — `run-pair.sh:289` reads `arms.calor.config.permissiveEffects` against the
annex §1 pin table, and `CalorArm.csproj.template:17` hardcodes `CalorEnforceEffects=true`.
**A-1.12 registers, under §7's supersession rule, a "pre-rows control arm" derogation**: for
PP-W-rows only, arm A's `pair.json` sets `permissiveEffects: true`, the pin named as relaxed is
the §1 table's `permissiveEffects = false` row, the template gains a per-arm
`<CalorPermissiveEffects>` property (W1), and every other pin (model, `raw` edits, distinct
`Calor.Tasks` hashes, censoring caps) stands. Arm B runs no flags.

**Metric — two legs, nested design (A:§6.1), one verdict.**

- *Leg A — escapes.* Per pair, the escape rate on each arm (`result.json` `escapedBugs` /
  `heldoutPassed` from the effect-observing test); statistic = **median over pairs of the
  per-pair escape-rate delta (A − B)**, with the same two-level cluster bootstrap as leg B; bar =
  the one-sided 95 % lower bound of the delta exceeds **0** and the point exceeds the MDD S3
  derives at 80 % power. Not Fisher over pooled runs — runs are nested in pairs.
- *Leg B — loop tax*, PP-E1's rule verbatim on `tokens.output` (A-1.9.1): fails iff the lower
  bound exceeds 1.0 **and** the point exceeds the margin; iterations observational.

**Margin — derived, disclosed, one knife edge.** Re-running the committed
`ppe1-margin-derivation.py` (seed 4537, 300 × 400) on `e1-rows-parity-001` with the grid extended
to 1.15/1.20: within-cell CV median 0.2746, null point p95 **1.1800 → margin 1.20**; conjunction
false-fail 0.7 % (point-only 2.7 %); power **0.53 / 0.81 / 0.97** at 1.25× / 1.4× / 1.6×. *Knife
edge:* the population's own realized point is 1.1835 — the 0.15 tax sits *at* its null p95 —
and pooling both arms of an epoch whose arms differ inflates the null spread, so an `e1`-only
calibration biases the margin **toward leniency**. S3 pre-registers the population — `e1` alone,
or `w5-parity-002 + e1` pooled — before any W run, and states the margin the other choice would
have given. The CV cap = 1.5 × the chosen population's median CV.

**Cells and spend.** Five pairs; runs per cell from A:81 (≥ 80 % power at the registered
leg-A MDD, upper-bound variance); at the archived mean of **$1.005 per run** (N:S1.2) a 5 × 8 × 2
design is ≈ $80 — the pre-registered **API-spend ceiling is $150** (≈ 150 runs); if 80 % power
needs more, the PP registers its achievable power and arms UNDERPOWERED.

**Four-valued outcome, precedence NOT-ADJUDICATED > MISS > UNDERPOWERED > HIT** (A:317):
NOT-ADJUDICATED — (a) any unmutated starter fails to reproduce its frozen multiset on its arm;
(b) any run lacks W1's transcript; (c) the PP-W5 validity floor / distinct-hash / censoring
routes; (d) W2 does not ship in 0.16.0 **and only where §9 cut line 2 was invoked in writing**,
cited in the ledger. **Own-goal clause** (A:317): a route caused by this workstream is MISS, with
the artifact. MISS — leg A below its bar on a valid harness, or leg B fails, or an own-goal.
UNDERPOWERED — leg A at bar with leg B's point over the margin and the bound not firing, or CV
over the cap, or registered achievable power < 80 %. HIT — leg A at bar and leg B not failing:
"rows, fail-closed, caught the registered classes at no large loop tax", never "rows are free".

**Blindness, stated correctly (round 1, C.8):** PP-E1 executed all ten mutation cells (10/10) and
the S3 compiles execute every seeded extension on both arms; the registration is results-blind
**only as to agent behaviour** — no agent has run any W task. **Freeze event:** A-1.12, guarded by
`check-annex-freeze.py`. **Who runs it:** the 0.16.0 release PR's author before the tag.

### 4.2 The tax question — instrument first

S1 steps 1–2 are done and null; the per-diagnostic attribution attributes nothing to
diagnostics; the per-turn table over PP-W-rows' own runs (the first captured epoch) is W4's
deliverable. **No product change is justified by the 18 % until that table exists.**

### 4.3 Unchanged from 0.15

No real-scale epoch until both re-entry conditions hold (R:856-857); M2 SHOULD; 1.32× a regression
indicator; register-then-merge enforced by A-1.10.

---

## 5. Release gates — instrument, denominator, freeze point, discriminating pin

**Carried from 0.15, restated as what exists:**

1. **Laundering, six closed classes** — unchanged (R:866-881).
2. **Higher-order demand ledger** re-executed at the release commit; floor 25 (R:882-888).
3. **Surface agreement — as it exists:** clean-vs-incremental in-process only
   (`EditScriptIdentityTests`); the CLI-process leg is PR #982 (open), the `Calor.Sdk` leg is
   unbuilt, ES-08 is unregistered (§0.1). **E7's MCP leg is built in 0.16** and joins the
   instrument; the CLI/SDK legs and ES-08 are §6 rows with triggers, not gates.
4. **PP-E1** — a regression pin: leg A stays 10/10 with a clean control on every 0.16 commit.
5. **Corpus compatibility — leg (a) only, as it exists** (`tests/TestData/Benchmarks`,
   `samples/`, test-compiled `.calr`); leg (b) is unbuilt (§6). W3-attributable new diagnostics are
   separated and published (R:941-944).
6. **Resolution floor** 817/1248 exact per subject, two-sided (R:947-958).
7. **Index/query goldens** — the ten E5 goldens; **E7 leg unconditional** (E7 is MUST).

**New:**

8. **Harness capture.** *Instrument:* W1's validity test. *Denominator:* every run of every 0.16
   epoch. *Freeze:* A-1.12 names it a validity condition. *Pin:* remove the archive step → route (b).
9. **Conversion denominator floor.** *Instrument:* the Calor0425 and Calor0270 ledger tests,
   schema 3 with `floorRule`, exact per subject. *Denominator:* 364 subject modules at the pinned
   SHAs, bind-first. *Floor, pre-committed here from S2:* aggregate `ExcludedParseFailed ≤ 2`
   (cluster 3 remains) **and** `ModulesEnforced ≥ 100` (MediatR ≥ 27, serilog ≥ 47,
   FluentValidation ≥ 26); expectation ≈ 119, published, not gated. *Freeze:* the S2 PR writes
   the floor before any W3 fix merges. *NOT-ADJUDICATED route:* the one ICE-only module draws a
   second error after the #1097 fix — then the floor is unreachable by construction and is
   re-registered at 99 with the artifact. *Pin:* revert the converter fix → per-subject
   `ModulesEnforced` / `ExcludedParseFailed` move → red. Two-sided as gate 6.
10. **PP-W-rows.** *Instrument:* `effect-rows-benefit-ledger.json` + exact-equality test +
    `ppw-analyze.py`. *Denominator:* five pairs × the registered cells × two arms. *Freeze:*
    A-1.12. *Pin:* the annex guard; dropping a pair fails the test.
11. **Silent-stop coverage.** *Instrument:* W5's `_IsReported` pin at an injected cap.
    *Denominator:* the one named path (`:1129`). *Freeze:* §3.1 W5. *Pin:* revert → red.
12. **Turn attribution.** *Instrument:* `ppe1-turn-attribution.py` + exact-equality test.
    *Denominator:* every archived epoch under `bench/phase0-agent-native/epochs/`. *Freeze:*
    A-1.12 (the fields it reads). *Pin:* delete one run → red.

---

## 6. Carried debt — trigger and venue for every registered residual

| Item | Source | Trigger | Venue |
|---|---|---|---|
| ES-08 never registered while E2 merged (breach) | F:364-366; §0.1 | — | **0.16 kickoff sweep:** register ES-08 under F-3's supersession rule with the breach disclosed |
| Gate 3 CLI-process leg (PR #982 open); `Calor.Sdk` leg unbuilt | R:891-893; §0.1 | E7 lands (the MCP leg needs the same driver) | #982 merged or closed in the kickoff sweep; SDK leg with E7 |
| PRs #981 (unreleased-changes doc), #976 (perf-gate strategy, #965) open | R:987 | — | kickoff sweep: merge, close, or re-open as issues — never silent |
| Gate 5 leg (b) `compile-all-committed-calr` never built | R:938-940 | — | 0.16.x; until then gate 5 claims leg (a) only (§5) |
| PP-E1 negative-control pin skips the effect pass on `A3-map`/`A3-match` | e3b:272-278 | — | **kickoff sweep with #949** (cheap; gate 4 leans on it) |
| `FunctionBoundType.Row` no end-to-end reader; AST span-matching | R:710-720; D:2700-2708 | A-1.12 registered | 0.16 SHOULD W7 |
| Lambda params invoked in-lambda → Calor0411; untyped alias hop | e4:255-259; e4:252-254 | A-1.12 registered | 0.16 SHOULD W7 |
| Calor0410 demoted to a warning under `--permissive-effects` | e4:246-248 | **PP-W-rows arm A depends on it** (the pre-rows waiver) — any change before the epoch is an own-goal | frozen through the 0.16.0 release commit; re-adjudicated after |
| ρ_body under-approximation on an escaping lambda (reported as Calor0425, not silent) | e4:230-234 | a fixture measured silent | DEFERRED (§3.3) |
| `PropagateInstantiatedCharges` `10_000` cap silent | `EffectEnforcementPass.cs:1129` | — | **0.16 MUST W5** (Calor0600) |
| #1104 recursion | `EffectEnforcementPass.cs:2547`; e3a:252-270 | — | **0.16 MUST W3(c)** |
| Calor0200/0273 on Lossy receivers (200 / 88 modules); `null` as identifier (104) | N:S2 | — | 0.16 SHOULD W6 |
| Index folds cross-module charges for bindable files; `calor build` for compiled | e5:265-272 | — | 0.16 SHOULD W8 |
| Interface methods not indexed; index-build cost unmeasured | e5:273-275; :259-261 | — | 0.16 SHOULD W8 |
| `§FLD`/`§B` rows not index positions; hover declared-only; `--json` on `effects` only | e5:168-175 | E7 | with E7 |
| Solution-level manifests not consulted by the index | e5:256-258 | a corpus solution with manifests | 0.16.x |
| Calor0419 at BCL argument sites (D-A = 2) | e4:249-251 | D-A `calor0419FunctionTyped` > 10 | 0.17 with IL rows |
| BCL-returned delegates → Unknown | D:1660-1664 | `UnknownSource + InvocationUndetermined` > 10 over gate 9's set | DEFERRED, demand-triggered |
| Key parameter component = inferred argument types; IL keys `FromStrings` | D:1360-1366; :1677-1687 | gate 6 must move, or IL rows trigger | 0.17 |
| Lambda `ParameterTypes` are surface spellings | D:1614-1620 | W7 | with W7 |
| Async rows | D:1922-1945 | three-clause test, maintainer, **0.16 branch cut** | DEFERRED |
| `PreconditionSuggester` on typed CFG; #909 double-report | R:739; #909 | — | 0.16.x together |
| Q1 C#-declared interface rows; Q4 Calor0425 span placement | D:2810-2819; :2834-2847 | IL rows / gate 9's never-invoked fraction | 0.17 / 0.16 branch cut |
| Q3 `Subtypes` widening has no doc-drift guard | D:2827-2833 | — | 0.16.x: a Calor13xx drift code in `self-check docs` |
| D2 (§6.2 row 6 code), Q2 (`eff` lookahead, pinned) | e3b:68-88; D:2820-2826 | — | closed by annotation / pin; no action |
| 0.14 §3.3 decisions 2–3 (migrator, golden regen) | R:977; #1084 | a user-reported 1.x file | demand-driven (0 of 941 declare 1.x) |
| 3.5.1 null-state slice; #845; #859/#884 Z3 flake; #970 tri-state; TIER1A not-run | R:978-984 | **0.16 branch cut**, maintainer, with the 0.15-cycle flake rate | 0.16.x instrument debt / release-notes rows |

---

## 7. Backlog disposition — all 30 open issues on 2026-08-27 (none P0/P1; #1082 `p2`)

| Issue | Disposition |
|---|---|
| #1104 | 0.16 MUST W3(c) — robustness; unlocks no modules (N:S2) |
| #1097 | 0.16 MUST W3(b) — converter-side `null` on `TypeKind.Error` |
| #903 | 0.16 MUST W3(a) clusters 1–2; cluster 3 (2 files) with it if trivial, else 0.16.x |
| #1094 | 0.16 MUST W1 |
| #901 | 0.16.x; regenerates the demand ledger with disclosure (45 files not reaching the pass) |
| #929 | 0.16.x parser fix with a named fixture |
| #943 | 0.16.x; re-read against gate 9's enforced set for `ref`/`out` sites |
| #847 | demand-driven (R:1020) |
| #1084 | item 1 done; 2–3 demand-driven (§6) |
| #1082 (`p2`), #875 | sequenced after W3; gate 6 makes mis-sequencing visible (R:980-982) |
| #845 | 0.16.x (§6) |
| #859, #884, #959 | 0.16.x instrument debt; flake rate attached at the 0.16 branch cut |
| #965 | release-blocking for 0.16.0 if it recurs on the 0.15.0 release; #976's strategy PR is its venue |
| #949 | kickoff sweep (with the PP-E1 pin rewrite, §6) |
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
   and what its row is — answered from the index, byte-identical to `calor query` (gate 7 E7 leg).
2. **On five callback-heavy tasks, rows fail-closed catch laundered effects that the pre-rows
   language let through, at no large loop tax** — PP-W-rows HIT at the release commit, or the
   verdict published as it reads.
3. **The converter's output parses for all but two of 364 real modules and binds for at least
   100** (gate 9), and every effect claim on real code names that denominator.
4. **The compiler reports when it stops checking** (gate 11).
5. **Every paid epoch is attributable per turn** (gates 8, 12); the 0.15 gap is explained or bounded.
6. Unchanged from R:1068-1082: null safety for 0.14's closed classes; honest contracts with
   default-on elision; no real-scale claim until the re-entry conditions hold.

---

## 9. Cut lines and schedule abort

- **Binder-adjacent rule (R:745-748).** W6 and W7 are SHOULD and defer to 0.16.x without
  renegotiation. W3 touches the binder nowhere: (a) and (b) are converter-side, (c) is the effect
  pass. If W3(c) cannot be bounded without changing a resolution answer (gate 6 moves), (c)
  becomes SHOULD and gate 9 keeps its (a)+(b) floor.
- **Cut line 1** (§3.1): W3(a) overrun does not stop W2; E7 overrun re-tiers in writing.
- **Cut line 2:** if A-1.12 has not registered by the 0.16 branch cut, no row-family `src/`
  change merges (W6/W7 stay out), and 0.16.0 ships **E7 + W1 + W3 + W4 (over the archived epochs
  only) + W5** under the same title; PP-W-rows moves to 0.17 with its fixtures frozen where they
  are and route (d) cites this line.
- **Schedule abort:** if W1 is not merged by the branch cut, no paid epoch runs in 0.16;
  PP-W-rows reads NOT-ADJUDICATED by route (b) — MISS under the own-goal clause if the cause is
  this workstream's — and the release notes say so.
- **Not cut lines:** the 1.32× benchmark; PP-E1's regression pin (a red there is a 0.15
  regression, fixed before release).

---

## 10. Review record

### Round 1 (2026-08-27, on Draft v1 @ `1af259a9`) — plan/process 85 %, measurement 88 %, engineering 85 %; all NEEDS-FIXES

§0's numbers reproduced under every lens; one reviewer re-executed S1 steps 1–2 (byte-identical C#
and CLI text) and measured the bind-failed histogram this draft then re-ran (N:S2 — identical
counts). Dispositions:

| # | Finding | Disposition |
|---|---|---|
| A.1 | Title claims rows on real code; none meet it in 0.16 | **Done** — "Measured Effects" (orchestrator DECIDED) |
| A.2 | No user-visible deliverable sentence | **Done** — §1.3 |
| A.3 | E7 dismissed as "no measurement"; it is specified, instrumented, needs no registration | **Done** — E7 MUST with gate 3 MCP leg + gate 7 E7 leg; §1.2 argues B fairly and records the draft's omission |
| B.1 | Deleting the bind-first guard inflates the ledger; #1104 unlocks zero | **Done** — bind-first kept (DECIDED); §0.4, N:S2 |
| B.2 | Run the first-error histogram now | **Done** — N:S2 (364 files; Calor0200 200, Calor0273 88, Calor0208 35, ICE 26, ICE-only 1, `null`-first 104) |
| B.3 | W3 scope: converter-side #903 c1–2 + #1097 (`null` on `TypeKind.Error`); #1104 as robustness with a crash-repro pin whose home is named; drop the "UnresolvedBoundType route" claim | **Done** — §3.1 W3(a)(b)(c); pin in `Calor.Enforcement.Tests/EffectInferrerRecursionTests.cs` over a committed reduced fixture, unbound |
| B.4 | Calor0200/0273 binder work is binder-adjacent → SHOULD | **Done** — W6 |
| B.5 | Gate 9: pre-commit a numeric floor from the histogram; schema 3 `floorRule`; converter-fix pin; NOT-ADJUDICATED route; drop "doubled" and the vacuous "#1104 alone" line | **Done** — floor `ExcludedParseFailed ≤ 2 ∧ ModulesEnforced ≥ 100` (27/47/26); expectation ≈ 119 published; "doubled" removed from §1.3/§5/§8/cut line 1 |
| C.1 | `after/` fixtures do not parse on v0.14.3; register per-arm starters | **Done** — five pairs with `before/`/`after/` blobs verified by `git ls-tree` |
| C.2 | `--permissive-effects` on a Calor arm is an invalid run; derogation or contrast (ii) | **Done** — derogation text in §4.1 (DECIDED); contrast (ii) argued and rejected because Calor0424 fires in both arms; the waiver's 0410/0411 silencing is named and leg A retitled |
| C.3 | `after/A2.calr` fails route (a) before the first run; use five pairs incl. A3-middleware | **Done** — A3-middleware is W-001, A2 is W-005 with its frozen multiset; route (a) reworded to "matches the frozen multiset" |
| C.4 | Method-group seeds are already Calor0410 warnings on v0.14.3 permissive | **Done** — W-002/W-003 seed through lambda bodies, W-004 through a field; S3 measures every seed on arm A and publishes warning-vs-error pairs; e4:246-248 row given a trigger |
| C.5 | Leg A metric must respect nesting; MDD/cells from A:81; spend ceiling; drop the unfirable UNDERPOWERED-by-design | **Done** — per-pair delta with two-level bootstrap; MDD from S3; ≥ 80 % power at upper-bound variance; $150 ceiling at $1.005/run |
| C.6 | Seeded starter fails to build on arm B — legs in tension, prompt broken | **Done** — redesigned: starter builds on both arms; laundering arises in the extension; bias direction pre-registered |
| C.7 | Margin on `e1`: p95 1.18 → 1.20; grid starts at 1.25; knife edge; pooled population | **Done** — re-derived (1.1800 → 1.20; false-fail 0.7 %; power 0.53/0.81/0.97); grid extension noted; population choice pre-registered at S3; bias direction (toward leniency) disclosed |
| C.8 | "7 of 10 cells … tasks blind" is stale | **Done** — "results-blind only as to agent behaviour" |
| D.1 | W4 MUST with no gate; missing Touches/Discriminating; cut line 2 must dispose of it | **Done** — gate 12; W4 lines; cut line 2 lists W4 |
| D.2 | W5 half 1: reuse Calor0600, injectable cap | **Done** — W5 rewritten; the "reserve a code" question is moot |
| D.3 | W5 half 2 is not silent on 0.15.0 | **Done** — dropped from MUST; §3.3/§6 row with a "measured silent" trigger |
| D.4 | W1: `--verbose` required; stream must not feed `agent.json`; define turn count from the pilot | **Done** — §2.1 / §3.1 W1 |
| D.5 | "M1 rule" over-generalised | **Done** — §2 states what A-1.12 blocks (W6/W7, row-family `src/`) and what it does not |
| E.1–E.5 | Builds 26 vs 23 with 4 vs 2 unedited; permutation p; both naive sensitivities; "harness's strict CLI compile" wording with `run-pair.sh:677-701`; `ExecutionWorkspace.cs`; S1 1–2 done; table moved to a note | **Done** — §0.2 and N:S1 (my permutation 0.004/0.025/0.449 beside the reviewers' 0.012/0.08/0.44, statistic stated) |
| F.1 | ES-08 breach; gate 3 CLI/SDK legs; gate 5 leg (b); PRs #982/#981/#976 open | **Done** — §0.1, §5 gates 3/5 restated as what exists, §6 rows |
| F.2 | 0410-demoted row trigger; PP-E1 pin rewrite to kickoff sweep with #949 | **Done** — §6 |
| F.3 | IL-rows trigger tied to the real floor; bind-failed quartile note | **Done** — §0.4, §3.3, §6 |
| F.4 | 30 open issues; retro-venued items need owner + date | **Done** — §7; retro items moved to the 0.16 branch cut, maintainer |
| F.5 | Cites: R:441; delete the "no D:§5.1" parenthetical; `EffectInferrer` nested at `:2547`; A:81; route (d) cites §9 cut line 2 | **Done** — all applied; `EffectInferrer` path corrected in §0.5/§3.1/§6 |
| F.6 | Trim narration; §6 "—/not scheduled" rows are a list | **Done** — Draft v2 is 6 610 words by `wc -w` (v1 7 253 as reviewed) with the S1/S2 tables moved to N:; every §6 row has a trigger or a closing pin |

**Declined:** none. **Narrowed:** C.2 — the reviewers allowed contrast (ii) as an alternative;
this draft keeps (iii) with the derogation because (ii) cannot separate rows from the waiver
(Calor0424 fires on both arms), and states leg A's claim at the width (iii) actually supports.

**Open questions for round 2:** (1) whether the 0.15.0 release PR's benchmark block moved;
(2) the S3 population choice (`e1` alone vs pooled) — pre-registered, not decided here; (3) whether
E7 extends `calor_navigate` or adds `calor_query` (a product decision with no gate consequence);
(4) whether the `null`-as-identifier emission (104 first errors) is converter-side — if so it
moves from W6 to W3 and gate 9's expectation rises, with the floor unchanged.
