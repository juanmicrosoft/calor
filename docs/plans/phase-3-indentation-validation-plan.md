# Phase 3 Validation Plan — Indentation-Delimited Blocks

**Status:** Draft pre-registration document, **revision 4.1** (post-cycle-4 critique, in-place edits). Becomes immutable once merged under the amendment discipline codified in §11. **RFC:** [`phase-3-indentation-rfc.md`](phase-3-indentation-rfc.md).

**Revision history:**

- **v1** drafted alongside the RFC. Procedurally weak: pilot below noise floor, Cliff's δ silently weakened, no engineering kills, no sub-feature controls, no prior-art baseline, agents-vs-humans cost-asymmetry inverted, no model/seed pinning.
- **v2** integrated cycle-1 critique. Added §0 prior art, H1+H2+H3+H4, §3a pre-pilot correctness gates, §5.3 engineering kills, §4 parity controls, sub-feature freeze, restored δ to 0.33, §6 honest cost-shift, §7 salvage, §8 telemetry-gated removal, §9 pinning.
- **v3** integrated cycle-2 critique. Pilot deleted (DA C1: rejected true H1 at 95.5%); freeze narrowed (DA C4); RFC pre-merge prerequisite added (DA C3); H3 operationalised (DA C2); hybrid-grammar tracking added (PL G3); manifest hash CI (DA SR5); stress corpus (PL G6); commingled-mode test (PL M6); §5.3.7 sequencing kill (DA SR6); §8a downgrade SLA (DA SR7).
- **v4** integrated cycle-3 critique. Five structural changes: unified §4.2.3 construct table (was §4.2.3 + §4.2.4 contradictory enumerations); H3 paired-within-seed; FoldingRangeHandler budgeted; Calor0101 codebase-bug code-fix made a plan-merge prereq; §5.3.7 rewritten with DAG and PR-merge-timestamp verification. Plus: H6 per-construct integration gate, §1.5 absolute count floor, §1.7 cognitive-load secondary, hybrid-grammar exposition policy, denominator-zero handling, amendment discipline, samples/ tree-hash pinning, scripts-committed-before-merge prereq.
- **v4.1** integrates cycle-4 critique (final per directive: ship-with-edits, not v5). All v3-cycle fixes verified ADDRESSED by both reviewers; six in-place edits applied:
  1. **H6 threshold made Arm-0-relative** (DA C1 = PL M1). `Arm I per-trial pass rate ≥ max(50%, Arm 0 per-trial pass rate − 10pp)` replaces absolute 50%. Prevents H6 from spuriously archiving on hard constructs (Arm 0=35%, Arm I=45% has +10pp arm effect and should not archive) while keeping the absolute floor for catastrophic regressions. Applied in §1 H6 and §5.2 rule list.
  2. **Prereq 1 disambiguation algorithm specified** (PL G1, DA m1). Three-part criterion (Keywords-or-special-case + matching /X + ParseStatement dispatch) replaces "mechanical enumeration from Lexer.cs" hand-wave; explicitly handles §PP special-case scanner branches; explicitly excludes expression-position closers and mid-block markers. New script `compute_phase3_opener_set.py` pinned in §9. New Parser.cs SHA pin in §9.
  3. **§11 self-protection** (PL S1). Added bullet: "any change to §11 itself is a v5-trigger change." Standard fixed-point for amendment-discipline clauses; prevents in-place weakening of the discipline that protects all other v5 triggers.
  4. **§5.3.7 DAG annotation reconciled with §7 salvage table** (DA M1). FoldingRangeHandler labeled "partly-salvage" in DAG to match §7's "partly" classification (was "NO-salvage to LSP-future-RFCs").
  5. **§5.3.7 prose vs DAG reconciled** (DA SR1). The strict-before language was inconsistent with the DAG (which shows folder/AST-diff/parser-dedent as siblings under lexer-root). Prose rewritten to match diagram: lexer first, then any order, then LSP-snapshot-harness after folder.
  6. **§4 hybrid-grammar policy forced to (B)** (PL G2). The v3/v4 (A)-or-(B) choice at pre-pilot was a researcher degree of freedom that undermined pre-registration. v4.1 fixes (B) — examples-only inference — at plan merge with honest acknowledgment that policy (B) penalizes irregular hybrids (which is correct: such hybrids should archive under §5.3.5).
  - Plus three smaller integrations: §2 substrate-sizing prerequisite (PL G3) covering H6 if Prereq 1 produces > 10 dedent-closed openers; §5.2.5 denominator-math footnote (DA SR2) justifying the `<5` threshold; §5.3.7 dual-mode LSP timing note (PL G4) — FoldingRangeHandler validated against Arm 0 closer-form fixtures at handler merge, Arm-I-fixture validation runs as post-merge gate.
  - Both cycle-4 reviewers verified all v4 (cycle-3) fixes ADDRESSED with no regressions and recommended "ship as v4.1 with named in-place edits, NOT defer to v5."

> **Plan merge prerequisites (v4, blocking).** This plan does NOT merge until all four conditions hold:
>
> **Prereq 1 — RFC §4.2.3 unified construct table.** RFC must contain a *single* table in §4.2.3 with one row per structural opener defined in `src/Calor.Compiler/Parsing/Lexer.cs` at the SHA frozen in §9. Columns: `[opener]` `[has closer]` `[Phase-3 treatment: dedent-closed | closer-retained | not-applicable]` `[rationale ≤ 1 line]` `[example input/expected token stream/expected AST — for `dedent-closed` rows only]`.
>
> **Disambiguation algorithm (v4.1 per cycle-4 PL G1, DA m1).** A keyword X is a "structural opener" subject to this table iff ALL THREE hold:
> 1. X is defined in the `Keywords` dictionary of `Lexer.cs` OR is handled by a named special-case scanner branch (currently `§PP`, `§/PP`, `§PPE` at `Lexer.cs:640-641, 718-731, 905-906`).
> 2. X has a matching `/X` closer entry in the same dictionary (i.e., `Keywords["/X"]` exists) OR is in the special-case set.
> 3. X's TokenKind is referenced from a `ParseStatement` dispatch branch (or its equivalent for class members, expression-position closers, etc.) in `src/Calor.Compiler/Parsing/Parser.cs` at the SHA frozen in §9 — i.e., X opens a *statement-context* block, not just an argument list or expression context. This rule explicitly EXCLUDES `/C`, `/THIS`, `/BASE`, `/NEW`, `/ANON`, `/INTERP` (expression-context closers, not statement-block openers) and mid-block markers `§EI`, `§EL`, `§CA`, `§FI`, `§K`, `§WHEN` (chain continuations, not openers).
>
> Aliases (`/SW` for `/W`, `/ENUM` for `/EN`) are merged with their canonical form. The opener `§IF` is a known special case: it has no matching `§/IF` closer (the chain closer is `§/I` which closes the entire IF/EI/EL chain); `§IF` IS a structural opener and IS included in the table.
>
> A `compute_phase3_opener_set.py` script (committed in same PR per Prereq 3) implements this algorithm by lexing/parsing the pinned SHAs and emitting a canonical opener list; §3a rule 6 compares the implementation's actual rewrite set against this script's output.
>
> The mechanical-enumeration rule is intentional: it eliminates the hand-curated "19 candidates" undercount that v3 inherited (DA C1 cycle 3 found ≥15 missing openers including `§T`, `§D`, `§W`, `§K`, `§DO`, `§WH`, `§PP`, `§UNSAFE`, `§SYNC`, `§OP`).
>
> **Prereq 2 — Calor0101 code-reuse fix in `main`.** `src/Calor.Compiler/Parsing/Parser.cs:3901` currently reports `Calor0101` for "IF expression requires an else clause (§EL)", colliding with `MismatchedId` at `Diagnostic.cs:31` (PL M1, verified). A separate PR must split the IF-requires-§EL diagnostic into a distinct code (suggested: next free `Calor01XX` slot) and land in `main` BEFORE this plan merges. Without this, §1.4 / §5.2 rule 7 measures the union of two unrelated conditions and the sanity check fires spuriously.
>
> **Prereq 3 — Instrument scripts written and committed.** `scripts/ast_equivalence.py` and `scripts/h3_block_boundary_diff.py` must be written, reviewed per §3a rules 8 and 9, and committed in the same PR as this plan. Writing them surfaces edge cases (comment attachment, scoped-symbol renaming, auto-generated IDs) that cannot be hand-waved through specification text. SHAs pinned in §9 are computed at the merge commit.
>
> **Prereq 4 — RFC §10.5 amendment discipline applied to RFC's §4.2.3 expansion.** The RFC update produced by Prereq 1 lands under the RFC's own amendment discipline; this validation plan does not bypass it. If the RFC author cannot complete Prereq 1 within ~1 week of v4 merge intent, this plan is archived as `phase-3-indentation-validation-plan-blocked.md` with the evidence of why expansion was unachievable. (Concretely: if even *defining* the per-construct table proves intractable, that is itself a strong signal the language is too irregular for the indent rewrite.)

> **Standing rule (unchanged across revisions).** No compiler surgery before §3a's nine pre-pilot correctness gates clear (less §3a rule 5, which now runs after Tier 1.5). No Tier 1.5 smoke-test run before §4 parity controls clear. No Tier 2 spend before §3a, §4, §5.3, and Tier 1.5 all clear. No ship before §5.2 (H1) AND H2 AND H3 AND H4 AND H6 clear. Failing any of these archives this plan and the RFC; no further code is written.

---

## §0 — Prior-art baseline

Honest framing (continued from v3 per DA M7 / PL G7; further refined v4 per PL P1): not every prior-art lesson becomes a pre-registered criterion. Lessons that *do* flow into gates are marked **[gated]**; those that inform without instrumenting are marked **[consulted]**.

- **Python (PEP 8 / `IndentationError` / `TabError`).** Even with decades of investment, mixed-tab/space remained a top-cited beginner bug until tabs were effectively banned. **[gated]** RFC §4.1 rejects tabs hard; §3a rule 5 fires kill if agents emit tabs in ≥10% of files.
- **Python (PEP 8, `\` continuation usage at scale).** Python kept `\` line-continuation indefinitely despite recommending parenthesised continuation; ~3% of CPython itself uses `\`. **[gated]** This is the *new* prior-art justification for §1.5 hybrid-grammar tracking (replacing v3's Haskell `{;}` analogy, which did not transfer to Calor — PL P1). Hybrid-grammar tolerance is calibrated against Python's empirical `\` usage rate.
- **F# light vs verbose syntax (17 years, never deleted).** Tooling and code-generation use cases keep the explicit form alive past intended deprecation. **[gated]** §8 step 3 retains the compat flag for **≥ 2 minor versions AND ≥ 6 months calendar** AND requires a named telemetry condition before removal (calendar floor added v4 per DA m5/SR13). The plan does not commit to ever removing the flag; the floor is "≥ 2 versions AND ≥ 6 months", the ceiling is "indefinitely until telemetry warrants".
- **Scala 3 (optional significant indentation).** Five years on, the community still argues; the conservative ship was *keep both*. **[consulted, not instrumented]** The §8 telemetry gate is informed by this prior art but does not produce a Scala-specific criterion.
- **OCaml / Reason (significant whitespace experiment, walked back).** Reason's "ship both, deprecate verbose" plan never executed because the indent form failed to attract enough adopters even with both available. **[consulted, not instrumented; new v4 per PL P3]** Informs §8 step 3's "telemetry-conditional removal" framing — the comparator suggests removal is *less* likely than the plan optimistically suggests.
- **YAML (Norway / silent semantic shifts under whitespace).** Indent + permissive parsing produces silent bugs. **[gated]** H3 (silent-bug cap) is an explicit Tier-2 hypothesis with concrete thresholds, a defined denominator (§5.2.5), a severity classification, and a named instrument script (`scripts/h3_block_boundary_diff.py`).

A passing gate against this prior art means: agents wrote Calor faster (H1), reversibility was designable (H2), silent block-rebinds stayed under the rate the YAML lesson warned of (H3), read-only LSP held up (H4), AND per-construct integration didn't regress on any specific construct (H6). It does **not** mean indent is the right long-term call — that is answered by post-ship telemetry over ≥2 minor versions AND ≥6 months calendar (§8).

---

## §1 — Hypotheses (primary + cost-caps + integration gate + descriptive secondaries)

> **H1 (primary, ship rule).** Replacing explicit structural closers with indentation increases the **median of per-trial paired differences** (Arm I per-trial pass-rate minus Arm 0 per-trial pass-rate) by ≥ **3 percentage points** across the 28 trials of §2, AND the per-trial paired difference at the 25th percentile is no worse than −5 percentage points. The median condition is **binding** (cross-ref §5.2 rule 1, v4 resolution of PL S5).

> **H2 (reversibility cost-cap).** `calor fix --from-indent` exists as a *stable CLI command* with `--help`, snapshot tests on `samples/` *and* the synthetic stress corpus from §3a rule 1b, *and* documentation at `docs/migration/indent-to-closer.md`. The AST round-trip harness on `samples/` ∪ stress-corpus ∪ Tier-2-fixtures passes byte-for-byte under `parse(closer) → emit(indent) → parse(indent) → emit(closer)` AST-equal.

> **H3 (silent-bug cost-cap).** *Operational definition in §5.2.5 (rewritten v4).* In Arm I outputs paired against matched-seed Arm 0 outputs (both qualifying per the denominator), the **statement-execution-affecting** block-scope-boundary AST difference rate is **0** (zero tolerance) AND the **cosmetic-only** block-scope-boundary AST difference rate is < 1% (per-trial median) AND ≤ 5% (per-trial p90).

> **H4 (read-only tooling cost-cap, pre-Tier-2).** `Calor.LanguageServer` produces correct folding ranges, document outline, and goto-definition on the pre-migrated Arm I fixtures, validated by snapshot test before Tier 2 spend. **NOTE (v4 per DA C4):** folding-range capability is not currently implemented (`src/Calor.LanguageServer/Handlers/` has no `FoldingRangeHandler.cs`, verified). H4 thus requires building the handler; budget added to §7 (~½ engineer-week) and sequencing reconciled in §5.3.7.

> **H5 (write-time tooling DOCUMENTATION milestone, pre-§8-step-3).** A written design document `docs/design/indent-aware-editing.md` covers auto-format-on-save, paste-and-reindent, smart-newline, and on-indent-change re-fold. A non-RFC-author AND non-LSP-implementer reviewer signs off. **H5 is reframed v4 (PL M3/SR9) as a documentation milestone, not a cost-cap.** A design doc cannot bound real engineering cost; the naming was misleading. H5 gates §8 step 3 (compat flag removal), not Tier 2 ship.

> **H6 (per-construct integration cost-cap, ship rule; threshold revised v4.1 per cycle-4 DA C1 / PL M1).** For each opener in the RFC §4.2.3 unified table classified as `dedent-closed`, at least one trial in §2's substrate exercises that opener as the dominant construct in the trial's program. **Arm I per-trial pass rate ≥ max(50%, Arm 0 per-trial pass rate − 10pp).** The Arm-0-relative floor prevents H6 from spuriously archiving on construct-intrinsic difficulty (a trial where Arm 0=35% and Arm I=45% has arm-effect +10pp and should not archive); the absolute 50% floor prevents H6 from passing trivially when both arms catastrophically regress. Coverage gaps (constructs not exercised by any trial) trigger archive subject to §2's substrate-sizing rule. **NEW v4 per PL G1/SR1; v4.1 calibration per cycle-4 DA C1, PL M1.** Resolves substrate-vs-surface tension by adding a binary per-construct check that does not need statistical power.

**Ship requires H1 AND H2 AND H3 AND H4 AND H6.** (H5 gates §8 step 3, not Tier 2.)

**Single statistical primary metric:** paired Wilcoxon signed-rank on per-trial pass-rate differences. Pass-rate per trial = three-runs majority-pass aggregation per seed × seeds-per-trial. Driver pinned in §9.

**Power note (v4):** the paired Wilcoxon at n=28 with α=0.025 one-sided and an assumed per-trial paired-difference SD of ~5pp (consistent with Phase 2 measurement-results dispersion) has approximately 70–85% power against a true median paired difference of +3pp. The Cliff's δ ≥ 0.33 constraint is independent of n and provides a magnitude floor. The plan does not claim 90%+ power; the trade-off is single-primary-metric simplicity and pre-registration discipline against marginal statistical efficiency.

### §1.1–1.4 Descriptive secondaries (NOT in the ship rule, with one exception called out)

- **§1.1.** Median character count per file per arm.
- **§1.2.** Median diff line count for the `wrap-existing-block-in-try-finally` trial (refactoring tax).
- **§1.3.** `Calor0099*` diagnostic rate per arm (denominator: per-trial fraction of `calor_compile` tool invocations that emit any `Calor0099*`; report median and p90 across 28 trials).
- **§1.4.** `Calor0101` diagnostic rate in Arm 0. *Pointer:* the binding archive condition formerly in §1.4 has been moved to **§5.2 rule 7** for consistency with v4's "every ship-blocking rule lives in §5.2" convention (DA M2/SR6). The Calor0101 measurement here is descriptive only; the binding interpretation requires the Prereq 2 code-fix to land first.

### §1.5 — Hybrid-grammar failure-mode tracking (v4 strengthened)

If the resolved RFC §4.2.3 table is hybrid (any construct retains its closer in Phase 3), classify each Arm I failure by failure mode:

- **Closer-omission-on-retained-closer** (agent dropped a closer the grammar requires)
- **Indent-mismatch** (whitespace bug)
- **Dedent-ambiguity** (parser picked a different block boundary than the agent intended)
- **Other** (semantic error unrelated to grammar)

**Pre-registered archive condition (v4 strengthened per DA M5/SR9):** if `closer-omission-on-retained-closer` count is BOTH > 25% of Arm I failures AND absolute count ≥ 4 across the 28 trials, the hybrid grammar imposes unacceptable cognitive load; archive the RFC regardless of H1 result. The absolute-count floor prevents binary 2-vs-3 archives at low failure counts.

### §1.7 — Cognitive-load secondary (NEW v4 per PL SR2)

For Arm I trials whose final per-seed outcome is `pass`, record the number of intermediate `compile-fail` attempts per three-runs-majority aggregate. If the per-trial median of intermediate-failure-count is > 2× the Arm 0 median, classify the trial as `cognitive-load-flagged` and report alongside §1.5 buckets. This catches the "passed after multiple corrections" case where cognitive cost was paid but H1 doesn't see it.

§1.7 is **descriptive, not binding**. The threshold (2×) has no calibration source and is reported for post-hoc analysis only. A future revision may promote it to binding once Phase 3 ship telemetry calibrates the multiplier.

---

## §2 — Substrate

**Total: 28 trials.** 24 carried-forward `task:` rows from Phase 2 + 4 purpose-sampled `template:path-3-gate/*` templates.

Sampling rule for the 4 new templates (one per category from RFC §6.2 indent-sensitive operations):

| Category | Template id (manifest path) | Why this category |
|----------|------------------------------|-------------------|
| Insert-deeper | `template:path-3-gate/insert-nested-loop` | Tests indent depth growth |
| Wrap-existing | `template:path-3-gate/wrap-existing-block-in-try-finally` | Worst case for refactoring tax |
| Extract-shallower | `template:path-3-gate/extract-method-from-deep-nest` | Tests dedent on extraction |
| Cross-grammar paste | `template:path-3-gate/paste-cs-snippet-and-convert` | Tests paste-destroys-indent failure mode |

**Substrate-vs-surface gap acknowledgment (v4, PL G1).** The 4 indent-direct templates cannot statistically detect a construct-specific regression in any individual opener. v4 introduces **H6** (§1) as a *binary* per-construct integration gate that bypasses the statistical-power problem. The 28-trial Wilcoxon remains the H1 primary metric and is dominated by indent-insensitive trials measuring "Arm I doesn't regress on indent-insensitive surface"; H6 fills the missing per-construct coverage with binary fixture checks.

**Authorship constraint:** templates authored by the implementer, reviewed for arm-bias by a non-author; reviewer signoff (commit SHA) recorded in `docs/plans/phase-3-pre-pilot-report.md`.

**Manifest path:** `tests/E2E/agent-tasks/phase-3-gate-tasks.txt`.

**Manifest integrity:** the manifest has SHA-256 `<computed at merge>`. CI test `tests/Phase3GateManifest_HashFrozen` asserts file hash equals pinned value; any change requires v5 of this plan.

**Trial substrate viability prerequisite.** Each of the 28 trials must, in an independent single-arm dry-run of ≥ 20 seeds against Arm 0, produce pass rate **between 30% and 90% inclusive**. Trials outside this band are replaced from a reserve pool; replacements documented in `phase-3-pre-pilot-report.md`.

**Substrate-sizing prerequisite (v4.1 per cycle-4 PL G3).** H6 (§1) requires per-construct coverage. If Prereq 1's resolved opener table contains > 10 `dedent-closed` constructs, the substrate must be expanded with one additional `template:path-3-gate/per-construct/<X>` trial per uncovered construct before Tier 2 spend. The "uncovered" determination is mechanical: cross-reference each of the 28 trials' programs against the table, mark each construct exercised. Any unmarked dedent-closed construct triggers substrate expansion. The expansion is part of §3a; it does not require v5 unless Prereq 1's output exceeds a calibrated upper bound (currently set at 25 — beyond that, the substrate expansion is large enough that statistical and budget assumptions shift and v5 is required). This rule prevents H6 from either (a) silently failing on a large table by under-coverage or (b) being defined-away to "only the constructs the substrate already covered" (which would render H6 vacuous).

---

## §3 — Protocol (Tier 1.5 smoke + Tier 2 full gate)

### Tier 1.5 — Operational smoke test

The v2 pilot was deleted in v3 because DA C1 (cycle 2) showed it rejects a true +3pp H1 effect 95.5% of the time. The Tier 1.5 smoke test has **no statistical role**; its sole purpose is to verify the harness operates correctly before Tier 2's 12-hour run.

- **2 trials × 5 seeds × 2 arms = 20 model invocations.** Under the §9 driver's 3-runs-majority convention, that's **60 model calls** (the v3 revision history transcription error of "60-run operational smoke" referenced this multiplied count; v4 standardises on stating both: 20 model-invocation aggregates / 60 raw model calls). DA m1/SR11.
- Wall-clock: < 15 minutes.
- Dollar cost: ~$1.
- **Smoke-test pass rule (pure operational):** all 60 raw runs complete without harness error (HTTP/timeout/crash); both arms produce ≥ 1 non-zero pass rate in at least 1 trial. If smoke-test fails, fix harness; do not interpret pass-rate magnitudes statistically.

### Tier 2 — Full gate (only if §3a + §4 + Tier 1.5 all pass)

- **28 trials × 30 seeds × 2 arms × 3 majority-runs = 5,040 model calls.**
- Wall-clock: ~12 hours at 8 workers.
- Dollar cost: ~$80–$100 (Phase 2 actual was $120 with similar arithmetic).
- **Ship-or-shelve rule:** §5.2 (H1) AND H2 AND H3 AND H4 AND H6.

### Why no statistical pilot

Per cycle-2 DA C1: at H1's nominal effect (+3pp, baseline p ≈ 0.84), the per-trial diff at 10 seeds is concentrated near zero; pooled across 6 trials, the v2 sign-test rule rejected ~96% of true H1. The honest design is: run §3a's correctness gates (which DO screen for instrument failure), then a cheap operational smoke test, then Tier 2 directly.

---

## §3a — Pre-pilot correctness gates (v4: nine gates, rule 5 reordered)

**Rule count clarification (DA m2):** gates are numbered 1, 1b, 2, 3, 4, 5, 6, 7, 8, 9 → **nine gates total**. Rule 1b is independent of rule 1 (different corpora); rule 9 is new in v4.

**Sequencing (v4 per DA M1/SR5):** all gates EXCEPT rule 5 must clear before Tier 1.5. Rule 5 (50-run agent tab-rate sanity) runs *after* Tier 1.5 confirms the harness is operationally sound, before §3a is declared closed. This avoids spending 50 expensive agent invocations through an unvalidated harness.

1. **Skeletal migrator AST-equivalence on `samples/`.** `calor fix --to-indent` runs on every file under `samples/` and produces output where `parse(closer) → AST_a` and `parse(indent_output) → AST_b` are structurally equal modulo ID auto-generation. **AST-equivalence relation defined in `scripts/ast_equivalence.py` at SHA pinned in §9** (committed in same PR as this plan per Prereq 3). Zero failures.
1b. **Migrator AST-equivalence on stress corpus.** Same test on a synthetic stress corpus of ≥ 5 files generated by mechanical deep-nesting (each `samples/*.calr` body wrapped in §L → §TR → repeat to depth 5). Committed at `tests/stress-corpus/phase-3/*.calr`. Zero failures.
2. **Round-trip equivalence on samples ∪ stress-corpus.** A throwaway `--from-indent` reverse migrator brings indent files back to closer form, byte-equivalent modulo whitespace renormalisation. This is the H2 prototype instrument (the final shipped tool is the stable CLI in H2 proper).
3. **Multi-line-construct coverage.** Arm I parses at least one fixture per opener classified `dedent-closed` in the RFC §4.2.3 unified table (Prereq 1). The construct list is mechanically derived from `Lexer.cs`; the §4.2.3 table is the authoritative source.
4. **LSP snapshot.** `Calor.LanguageServer` folding-range, outline, and goto-definition snapshot tests pass on Arm I fixtures. **NOTE (v4):** this requires `FoldingRangeHandler.cs` to exist; budget added to §7. Until the handler exists, gate 4 cannot fire; sequencing in §5.3.7 accounts for this.
5. **Agent tab-rate sanity (runs AFTER Tier 1.5 per v4).** 50 single-seed runs against 5 stratified trials in Arm I. If agent emits tab indentation in ≥ 10% of files, kill plan: indent is off-distribution for the model.
6. **Opener-rewrite reviewer signoff.** Arm I implementation's list of opener-rewrites compared against RFC §4.2.3 unified table. Discrepancy fires §5.3.5 multi-line-construct kill.
7. **Commingled-mode parse correctness (v4: property-based fuzz per PL G3/SR3).** Generate ≥ **100 commingled fixtures** by randomly choosing closer-or-dedent for each opener-instance in a corpus drawn from `samples/` ∪ stress-corpus, with the fixed seed list pinned in §9. AST-equivalence rate against pure-closer reference must be ≥ **99.9%** (i.e., ≤ 1 divergent case across the 100). Any divergent case is triaged and either committed as a permanent regression fixture (if parser bug, blocks merge) or documented as expected ambiguity (if grammar ambiguity, requires RFC §4.2.3 amendment). This replaces v3's "5 hand-written fixtures" which sampled ~5×10⁻⁵% of the combinatorial space.
8. **Arm I implementation review.** A non-RFC-author reviews the Arm I implementation diff and signs off in `phase-3-pre-pilot-report.md`. Specifically:
   - List of openers receiving dedent rewrites (matches RFC §4.2.3 table).
   - `--to-indent` migrator's handling of empty blocks, multi-arm `§W`, nested `§TR`.
   - Parser diagnostic policy for ambiguous cases (silent accept vs `Calor0099*` emit).
   - The Arm I system prompt's hybrid-grammar exposition policy choice per §4 (added v4).
   Reviewer name and signoff SHA recorded.
9. **H3 instrument reviewer signoff (NEW v4 per PL S2/SR7).** A non-author reviewer reviews `scripts/h3_block_boundary_diff.py` and `scripts/ast_equivalence.py` before their SHAs are pinned in §9. Signoff in `phase-3-pre-pilot-report.md`. Specifically reviews:
   - ID-renaming modulo correctness in AST equivalence.
   - Comment-attachment classification in H3 cosmetic-vs-statement-execution split.
   - Paired-within-seed iteration logic in H3 numerator.
   - Zero-tolerance trigger logic in §5.2.5.
   - Denominator-zero handling (§5.2.5 final paragraph).
   Without signoff, H3 has no instrument and §5.2.5 cannot fire.

A summary report (`docs/plans/phase-3-pre-pilot-report.md`) documents all nine gate results and is merged before Tier 1.5 spend (for rules 1-4, 6-9) and before Tier 2 spend (for rule 5).

---

## §4 — Arms

Exactly two. Single-knob.

### Arm 0 — `phase-1-baseline`

`main` after PR #624 lands. Fixtures pass through `calor fix --drop-structural-ids samples/` first, so closers are bare-form `§/F` (no ID) and openers use compact form `§F{Main:pub}`. This is the form a post-Phase-1 codebase will actually use, and the fair comparator for Arm I.

### Arm I — `phase-3-indent`

A throwaway implementation branch with the RFC §4.1–§4.3 applied per the resolved §4.2.3 unified table. Sub-feature freeze (cycle-2 narrowed):

| Sub-feature | Status in Arm I | Why |
|-------------|------------------|-----|
| `§PASS` empty-block marker | **UNFROZEN** (introduced) | Pure addition; freezing it would make empty-bodied functions unrepresentable |
| `Calor0099a SuspiciousIndentJump` warning | NOT emitted | New diagnostic surface; defer to post-gate |
| `\` line-continuation marker | NOT introduced | RFC §4.6 has genuine open question; freeze is honest |
| `§/I` (if/elif/else chain close) | **UNFROZEN** (dedent ends chain per RFC §3) | The RFC's most distinctive rule |
| Tabs hard-rejected | YES | RFC §4.1, validated by §3a rule 5 |

If H1 passes, the still-frozen `\` continuation ships as its own post-gate RFC.

### Fixture parity (mandatory)

- Arm I fixtures produced by `calor fix --to-indent` over post-Phase-1 Arm 0 fixtures; required byte-equivalent except for closer deletion + 2-space indent normalisation.
- Verification: round-trip ≥ 5 files in §3a rule 2; assert byte equality. Output pasted into pre-pilot report.
- Arm 0 fixtures also receive a no-op `calor format` pass to control for whitespace renormalisation noise.
- Same parity controls apply to the stress corpus.

### Prompt parity (mandatory; v4 adds hybrid-grammar exposition policy per PL G5/SR5)

- Both arms' system prompts committed verbatim in Appendix A.
- Diff between prompts constrained to minimum text necessary.
- Token counts within ±10%, example counts identical.
- Non-RFC-author reviewer signs off; name + SHA recorded.

**Hybrid-grammar exposition policy (v4.1 per cycle-4 PL G2).** The Arm I system prompt's treatment of the hybrid grammar uses **policy (B) — examples-only inference**: the prompt provides 3-5 Arm I example programs without explanatory text about which constructs use dedent and which retain closers. The agent must infer the per-construct rule from examples. The ±10% token-count rule holds.

Policy (B) is fixed at plan merge (not chosen at pre-pilot). Rationale: (B) is the more honest comparator because real users will not be issued an explanatory grammar table on every interaction; (B) measures grammar-with-inference usability, which is what shipping the grammar actually means.

**Honest acknowledgment (v4.1):** policy (B) makes the gate harder for hybrid grammars (the agent must infer the partition from examples). If the RFC §4.2.3 table is purely dedent (no construct retains its closer), policy (B) measures grammar usability without the inference penalty. If §4.2.3 is hybrid, the inference cost is real and the gate is correctly measuring it. The v3/v4 (A)-or-(B) conditional choice was a researcher degree of freedom that cycle-4 PL G2 correctly identified as undermining pre-registration; v4.1 closes it by forcing (B). Cycle-4 PL noted that if RFC §4.2.3 turns out so irregular that (A) becomes the only fair comparator, that is itself evidence the grammar is too irregular and the RFC should be archived under §5.3.5.

### 4.4 — Arm I implementation review

The implementer of Arm I commits to weekly status reports (§5.3.0) and the reviewer commits to a written approval at each weekly checkpoint. This is the operating discipline that turns "non-author review" from a one-time gate into a real check throughout the 3-week build.

### Sham-arm omission

Deliberately omitted (Phase 2 already covered "opt-in grammar tweaks move agent behaviour"). Imported by reference.

---

## §5 — Decision rules (pre-registered)

### 5.2 Full gate → ship

There is no separate §5.1 in v3+: the v2 pilot was deleted; the Tier 1.5 smoke test's rule is in §3 above and has no statistical content.

**Adopt indent grammar iff all seven:**

1. **Median paired-difference (binding):** median of per-trial paired differences (Arm I − Arm 0) ≥ 3 pp across 28 trials. *Resolved v4 per PL S5: this is binding, matching §1 H1 wording. The v3 "descriptive, not binding" tag is dropped.*
2. **Bottom-quartile floor:** per-trial paired difference at p25 ≥ −5 pp.
3. **Primary statistical test (sole p-value):** paired one-sided Wilcoxon signed-rank on per-trial pass-rate differences (Arm I − Arm 0), n=28, **p < 0.025** with **Cliff's δ ≥ 0.33**. `scipy.stats.wilcoxon(zero_method='wilcox', mode='exact')` per scipy version pinned in §9.
4. **`Calor0099*` quality floor:** per-trial fraction of `calor_compile` invocations emitting any `Calor0099*` has **median ≤ 5% and p90 ≤ 15%** across the 28 trials.
5. **Silent-bug cap (H3):** see §5.2.5 below.
6. **Hybrid-grammar cognitive-load cap (§1.5):** if the resolved RFC §4.2.3 table is hybrid, then the fraction of Arm I failures attributed to `closer-omission-on-retained-closer` is < 25% **OR** the absolute count is < 4.
7. **Substrate validity (Calor0101 sanity, moved from §1.4 per v4 DA M2/SR6):** after Prereq 2's code-fix lands, the post-fix `Calor0101` (MismatchedId-only) diagnostic rate measured on the substrate during the Phase 2 re-analysis is ≥ 5%. If < 5%, the hypothesis is ill-posed (indent solves a non-problem) and the plan archives without proceeding to ship even on a passing H1.

**AND** H2 (reversibility tool shipped + AST round-trip passes against final implementation).

**AND** H4 (LSP snapshot tests pass against final implementation; FoldingRangeHandler exists).

**AND** H6 (per-construct integration gate — at least one trial per dedent-closed construct passes `Arm I ≥ max(50%, Arm 0 − 10pp)`, v4.1 calibration).

**Note: H5 (write-time tooling documentation milestone) gates §8 step 3 removal of the compat flag, not Tier 2 ship.**

### 5.2.5 — H3 silent-bug instrument (rewritten v4 per DA C3, PL M2)

**Denominator (per trial):** the set of (trial, seed) pairs where **both** the matched Arm I and Arm 0 outputs satisfy:
- `agent.reported_success == True`, AND
- `calor parse <output>.calr` exits 0

This is **paired-within-seed**. v3's "all Arm I outputs compared against canonical fixture" conflated agent stochasticity with arm effect (DA C3); v4 pairs Arm I output to the matched-seed Arm 0 output (same agent, same seed, different arm).

**Numerator (severity-classified, per trial):**

For each qualifying (trial, seed) pair, compute `ast_I = parse(f_I_{trial,seed})`, `ast_0 = parse(f_0_{trial,seed})`. A **block-scope-boundary difference** is any case where some statement node `N` has a different enclosing block in `ast_I` vs `ast_0` (modulo renaming of locally-scoped symbols).

Classify each boundary difference:

- **Statement-execution-affecting:** the difference changes which statements execute on at least one input. Concretely: the difference affects a loop's body membership, an if-arm's body membership, a try-block's body membership, or a function's body membership. **Zero tolerance.** Any single instance archives the RFC.
- **Cosmetic-only:** the difference is in comment attachment, blank-line positioning, or trailing whitespace. **Per-trial median ≤ 1%** of the trial's denominator AND **per-trial p90 ≤ 5%**. (v4 per PL M2/SR10: per-trial median+p90, mirroring §5.2 rule 4's structure, replaces v3's pooled <1% which obscured per-trial concentration.)

**Denominator-zero handling (v4 per DA M3/SR12; math footnote added v4.1 per cycle-4 DA SR2):** trials with denominator < 5 are reported as `insufficient-evidence` and do not contribute to the 0-tolerance / cosmetic-rate aggregates. **Ship is blocked if > 20% of trials are insufficient-evidence** (i.e., > 5 of 28 trials lack the paired denominator). This prevents a regression that drives Arm I to near-zero pass-rate from "vacuously passing" H3.

*Denominator math justification (v4.1 footnote).* At H1's nominal effect (+3pp, Arm 0 baseline ≈ 0.84), Arm I ≈ 0.87. Under positive seed-pairing correlation (same agent, same seed), both-pass rate ≈ 0.80, yielding ~24 paired qualifying outcomes per trial of 30 seeds — well above the `<5` insufficient-evidence threshold. Only pathological regimes (e.g., Arm 0 at the lower viability bound of 30% with a strong negative arm effect) approach the threshold, and the `>20%` insufficient-evidence ship-block correctly catches those.

**Instrument:** `scripts/h3_block_boundary_diff.py` at SHA pinned in §9. The script is committed in the same PR as this plan (Prereq 3); the SHA is back-filled into §9 at the merge commit. Reviewer signoff per §3a rule 9.

**Reference oracle (descriptive secondary, not gated):** as a *secondary* metric, the v3 unpaired comparison against the canonical `tests/E2E/agent-tasks/phase-3-gate-fixtures/<trial-id>.calr` reference is still computed and reported, but does not contribute to ship-or-shelve. It informs debugging when boundary differences appear.

### 5.3 Engineering kill criteria

**Apply during Arm I build, before Tier 1.5.** Each fires immediate archive.

5.3.0. **Weekly milestone enforcement.** An RFC-non-author records status in `docs/plans/phase-3-eng-status.md` at end of each week. Required milestones:

- End of week 1: `calor fix --to-indent` skeleton runs on `samples/` and passes §3a rule 1.
- End of week 2: lexer indent-tracking passes the existing `Calor.Compiler.Tests` suite (modulo expected regressions in `Calor.Conversion.Tests` snapshots).
- End of week 3: parser dedent-acceptance gives green §3a rule 7 (commingled mode).
- End of week 4 (NEW v4): `FoldingRangeHandler` implementation passes §3a rule 4 LSP snapshot tests.

Missing any milestone fires §5.3.1 without further review.

5.3.1. **Budget overrun:** > 4 weeks without runnable Arm I (v4: extended from 3 to 4 weeks to accommodate FoldingRangeHandler line item per DA C4).

5.3.2. **Existing-suite regression:** ≥ 10 tests regress in `Calor.Compiler.Tests` on throwaway branch vs `main`.

5.3.3. **`samples/` byte-round-trip regression:** ≥ 20% of `samples/` files fail `--to-indent` ↔ `--from-indent` round-trip.

5.3.4. **Continuation-line ambiguity:** RFC §4.6 unresolvable without breaking the recursive-descent contract in `Parser.cs`.

5.3.5. **Multi-line-construct break:** §3a rule 3 OR rule 6 fails for any construct in the RFC §4.2.3 table.

5.3.6. **Agent tab-rate sanity:** §3a rule 5 fires (runs after Tier 1.5 per v4 sequencing).

5.3.7. **Salvageable-first sequencing (rewritten v4 per DA M4/SR8, PL G4/SR4).** The sequencing constraint operates on a DAG, not a strict linear order:

```
lexer indent pass (partly-salvage, prerequisite)
    ├─→ parser dedent acceptance (yes-throwaway, gated by §3a rule 7)
    ├─→ FoldingRangeHandler implementation (partly-salvage; v4.1 per cycle-4 DA M1: consistent with §7)
    │       └─→ LSP snapshot harness (NO-salvage; cannot land before handler)
    └─→ AST-diff harness (NO-salvage; cannot land before AST is parseable)
```

The kill criterion: **the lexer indent pass must merge to the throwaway branch first** (no dependencies); the AST-diff harness, FoldingRangeHandler, and LSP snapshot harness are *siblings* of parser dedent acceptance under the lexer-rooted DAG (they do not strictly precede parser dedent — they share the lexer-precondition only). The previous wording "must each merge before parser dedent acceptance lands" was inconsistent with the DAG and is corrected v4.1 (cycle-4 DA SR1). The actual sequencing rule is:

1. Lexer indent pass merges first.
2. Parser dedent acceptance, FoldingRangeHandler, and AST-diff harness may merge in any order after the lexer lands; the LSP snapshot harness must follow FoldingRangeHandler.
3. **Dual-mode validation of §3a rule 4 (v4.1 per cycle-4 PL G4):** FoldingRangeHandler is validated against Arm 0 closer-form fixtures at handler-merge time. §3a rule 4's Arm-I-fixture validation runs as a separate post-merge gate after parser-dedent has merged AND folder, AST-diff harness, and parser-dedent are all in the throwaway branch's HEAD.

Verification is **mechanical via PR merge timestamps**: the throwaway branch's PR queue is configured to NOT use squash-merge (`gh pr merge` invoked with `--rebase` or `--merge`, not `--squash`), so `git log --first-parent --format='%h %ai %s' rfc/phase-3-indent` is the audit trail. A reviewer (non-RFC-author) verifies the sequence in `phase-3-pre-pilot-report.md`.

This resolves both (a) v3's circular-dependency problem (LSP harness needed parser dedent) by making lexer-then-handler the path, and (b) v3's squash-merge unverifiability (DA M4) by requiring rebase or merge commits on the throwaway branch only.

Each kill fire is archived as `phase-3-indentation-rfc-eng-rejected.md` with the diagnostic evidence.

---

## §6 — Out of scope (cost-shift acknowledged, not denied)

The gate measures benefit on the population that bears the lowest share of the cost. The cost is real and is borne elsewhere.

- **Human-developer ergonomics:** known unmeasured liability; mitigated by §8's ≥2-minor-version-AND-≥6-month compat-flag retention + telemetry gate.
- **Greppability:** partly measured (§1.1, char count); not gated.
- **Compile-time performance:** lexer indent-tracking is O(n) for full re-lex; incremental re-lex on edits may cascade indent-stack changes to EOF (interaction with `docs/plans/incremental-compilation.md`). The Python and rust-analyzer parser communities have published-on this; v4 acknowledges this is a real consideration without gating it (PL P2).
- **Editor adoption cost beyond LSP read-only:** H4 covers folding/outline/goto (with explicit FoldingRangeHandler line item per v4). Auto-format / paste / smart-newline gated by H5's design doc, which must exist before Tier 2 but implementation gates §8 step 3 only.
- **Diff-size in PR reviews, human-survey ergonomics:** subjective.

---

## §7 — Budget & salvage classification

**Dollar cost:** ~$1 smoke + ~$80–100 full gate = ~$85–100 of model spend.

**Engineer cost (v4 expanded):** ~4 weeks compiler work for Arm I throwaway build (RFC §4.1 lexer + §4.2 parser + §4.3 writer + unfrozen `§PASS` support and `§/I` dedent handling) + ~½ week `FoldingRangeHandler` implementation (NEW v4 per DA C4/SR3) + ~3 days §3a infrastructure + ~1 day driver wiring + ~1 day H2 stable CLI promotion + ~3 days H3 + AST-equivalence script authoring and reviewer turnaround + ~1 week H5 design doc authoring.

**Total: ~5–6 weeks engineer-time before Tier 2.**

**Salvage classification (v4 updated):**

| Component | Throwaway? | Salvage target |
|-----------|-----------|----------------|
| Lexer indent-tracking pass (§4.1) | partly | `calor format` indent normalisation |
| Parser dedent acceptance (§4.2) | yes | sunk cost if Phase 3 archives |
| Writer indent emission (§4.3) | partly | `calor format`, future `calor refactor` |
| `--to-indent` skeleton (§3a rule 1) | partly | reusable as `calor format` mode |
| `--from-indent` skeleton (§3a rule 2) | yes | sunk cost — contingent on Phase 3 ship |
| `--from-indent` stable CLI (H2) | yes | sunk cost — contingent on Phase 3 ship |
| LSP snapshot harness (§3a rule 4) | NO | reusable for any future grammar RFC |
| **FoldingRangeHandler implementation (NEW v4)** | **partly** | **reusable for any future grammar; needs indent-aware rewrite if Phase 3 archives but the LSP capability stays** |
| AST-diff harness (§5.2.5, §3a rule 1) | NO | reusable for any future migrator |
| **H3 instrument script (NEW v4)** | **NO** | **reusable as silent-bug detector for any future grammar RFC** |
| H5 design doc | NO | applies to any future indent-aware editing |

The §5.3.7 DAG sequencing enforces front-loading via the lexer-first rule; "yes throwaway" rows must follow the lexer in PR merge order.

---

## §8 — Ship plan

Three-step shipping. Step boundaries are PR boundaries.

1. **Land §4.1–§4.6 implementation in `main`.** Documentation updated to show indent as canonical. Closer form retained behind a `--compat-explicit-closers` parser flag. Tier 2 substrate added as permanent regression suite.
2. **Migration.** Run `calor fix --to-indent` over `samples/`, `docs/`, `tests/E2E/agent-tasks/`. AST-equivalent rewrites committed.
3. **Compat flag removal — gated, not scheduled.** Retained **≥ 2 minor versions AND ≥ 6 months calendar** (v4 per DA m5/SR13: calendar floor calibrated to recent release cadence so the "2 minor versions" rule isn't trivially satisfied in ~2 weeks). Removal triggers (ALL must hold):
   - **Issue-tracker rule with disposition policy (v4 per DA SR14, PL S3/SR8).** The repo carries a `phase-3-indent-bug` label since §8 step 1. Removal requires:
     - Zero issues with this label open AND meeting ALL: (a) reproducer ≤ 50 LOC of `.calr`, (b) open ≤ 60 days OR has a reproduction-confirmed reply from the named owner.
     - Issues older than 60 days without a confirmed reproducer are auto-relabelled `phase-3-indent-stale` and do not block removal.
     - An issue closed as `wontfix` or `duplicate` counts as **open** for the purposes of this gate UNLESS the closure is co-signed by the triage owner AND one non-author reviewer.
   - **Owner.** The RFC author triages weekly; if unavailable, a named deputy in `docs/plans/phase-3-triage-owner.md`.
   - **Tooling rule.** H5 (write-time editing design doc) has at least a *proof-of-concept* implementation in `Calor.LanguageServer`, verified by a non-author code-review.
   - **Sample corpus rule.** Zero `--compat-explicit-closers` usage in `samples/`, `docs/`, `tests/`.

### §8a — Downgrade SLA

Before §8 step 3 fires, `calor fix --from-indent` MUST be a stable shipped CLI subcommand (this is H2). Stability means:
- `--help` text
- snapshot tests on `samples/` AND `tests/E2E/agent-tasks/phase-3-gate-fixtures/`
- documentation at `docs/migration/indent-to-closer.md`
- same support SLA as other `calor fix` subcommands

§8 step 3 telemetry conditions are the trigger; H2 stable CLI is the prerequisite. Both must be met to remove the flag.

---

## §9 — Pinning (model, analyser, seeds, scripts, corpora)

- **Model.** Same snapshot as Phase 2 gate (model name + version inserted at merge time). Verdict is conditional on this model.
- **Seeds (Tier 1.5 smoke):** `[0, 1, 2, 3, 4]`.
- **Seeds (Tier 2 full gate):** `[0, 1, ..., 29]`. Paired across arms.
- **Seeds (§3a rule 7 commingled-mode fuzz, v4):** deterministic generator seeded with `[0, 1, ..., 99]`; the generator script is pinned alongside the fixtures.
- **Analyser.** `scripts/analyze_gate_results.py` at SHA `<inserted at merge>`. Wilcoxon: `scipy.stats.wilcoxon(zero_method='wilcox', mode='exact')`, scipy `<version pinned>`. Cliff's δ: `cliffs_delta` package `<version pinned>`.
- **H3 instrument.** `scripts/h3_block_boundary_diff.py` at SHA `<inserted at merge>`. Defines paired-within-seed boundary equivalence and severity classification per §5.2.5. **Committed in same PR as this plan per Prereq 3.** Reviewer signoff per §3a rule 9.
- **AST equivalence relation.** `scripts/ast_equivalence.py` at SHA `<inserted at merge>`. Defines modulo-ID-renaming structural equality used in §3a rules 1, 1b, 7. **Committed in same PR as this plan per Prereq 3.** Reviewer signoff per §3a rule 9.
- **Driver.** `tests/E2E/agent-tasks/run-agent-tests.sh` at SHA `<inserted at merge>`. Three-runs majority-pass per seed.
- **Trial manifest.** `tests/E2E/agent-tasks/phase-3-gate-tasks.txt`, SHA-256 `<computed at merge>`. CI test `tests/Phase3GateManifest_HashFrozen` enforces.
- **`samples/` corpus tree-hash (NEW v4 per DA m4/SR7).** Pinned tree-hash `<computed at merge>` via `git rev-parse HEAD:samples/`. CI test `Phase3SamplesCorpus_HashFrozen` enforces. Prevents `samples/` growth during the 3-4 week build window from hiding regressions in §3a rule 1 or §5.3.0 week-1 milestone.
- **Prompts.** Appendix A: `system-prompt-arm-0.md` and `system-prompt-arm-I.md`, committed in same PR. Hybrid-grammar exposition policy choice per §4 recorded in `phase-3-pre-pilot-report.md`.
- **Stress corpus.** `tests/stress-corpus/phase-3/*.calr`, committed in same PR; ≥ 5 files generated by mechanical deep-nesting per §3a rule 1b.
- **`Lexer.cs` opener-source SHA (NEW v4).** The "structural opener set" referenced by RFC §4.2.3's unified table is `src/Calor.Compiler/Parsing/Lexer.cs` at SHA `<inserted at merge>`. If `Lexer.cs` introduces a new opener between merge and Tier 2 spend, the RFC §4.2.3 table must be amended (which triggers §11 v5 cascade) before Tier 2 proceeds.
- **`Parser.cs` dispatch-source SHA (NEW v4.1 per cycle-4 PL G1).** The "statement-context opener" criterion in Prereq 1's disambiguation algorithm is `src/Calor.Compiler/Parsing/Parser.cs` at SHA `<inserted at merge>`. If `Parser.cs` introduces a new `ParseStatement` dispatch branch between merge and Tier 2 spend, Prereq 1's enumeration must be re-run; any new statement-context opener triggers §11 cascade.
- **`compute_phase3_opener_set.py` (NEW v4.1 per cycle-4 PL G1).** Script that implements Prereq 1's disambiguation algorithm against the pinned Lexer.cs and Parser.cs SHAs, emitting a canonical opener list. SHA `<inserted at merge>`. Committed in same PR per Prereq 3. Reviewer signoff per §3a rule 9. §3a rule 6 compares the parser dedent implementation's actual rewrite set against this script's output.

If any pinned artifact changes between merge and Tier 2 spend, the gate is invalidated; v5 protocol required.

---

## §10 — Open items deferred to the implementation PR (not v5)

These do not affect pre-registration:

- How `calor format` interacts with indent grammar during compat window — covered by H5 design doc.
- (v4 removed: "Exact behaviour of `--to-indent` on multi-line `§C`/`§A` chains" — this now belongs to the RFC §4.2.3 unified table per Prereq 1; DA M7/SR10.)

These are technical decisions, not measurement-design decisions; they do not weaken any pre-registered hypothesis or rule.

---

## §11 — Amendment discipline (NEW v4 per DA SR16, PL S4/SR13)

This plan's post-merge change discipline:

1. **v5-trigger changes (require full revision):**
   - Any change to §9 pinned artifacts (manifest hash, script SHAs, model, seeds, samples/ tree-hash, Lexer.cs SHA, Parser.cs SHA).
   - Any change to §1 hypothesis text (H1-H6) including thresholds, denominators, archive conditions.
   - Any change to §5.2 ship rules (rules 1-7).
   - Any change to §5.3 engineering kill criteria (5.3.0-5.3.7).
   - Any change to §3a's nine gates' pass conditions.
   - Any change to §4 arms (Arm 0 definition, Arm I freeze table, prompt-parity rule, hybrid-grammar exposition policy).
   - Any change to the Plan-merge prerequisites in the header (Prereqs 1-4), including Prereq 1's disambiguation algorithm.
   - Any change to §8 ship plan (steps 1-3 conditions).
   - **Any change to §11 itself** (NEW v4.1 per cycle-4 PL S1: standard fixed-point for amendment-discipline clauses, prevents in-place weakening of the discipline that protects all other v5 triggers).

2. **In-place amendment (clarifying prose, with CHANGELOG entry in commit message):**
   - Wording clarifications that do not change any binding rule.
   - Citation fixes (file:line corrections).
   - Cross-reference fixes between sections.
   - Reformatting that does not move content between sections.

3. **Triage rule for ambiguous cases.** If a contributor cannot tell whether a change is v5-class or in-place, **default to v5**. False positives (over-revisioning) cost little; false negatives (missed v5 trigger) compromise pre-registration discipline.

4. **Meta-process for v5 itself.** A v5 follows the same four-cycle critique discipline that produced v3 and v4 (devil's advocate + PL designer in parallel, integrate, self-assess, iterate). If a cycle-N critique surfaces a finding that should have triggered v5 but didn't, the missed-trigger is itself a critique finding for the next cycle and must be reviewed.

5. **Archive condition.** If two consecutive critique cycles fail to converge on ≥ 95% confidence (robust + complete + comprehensive axes), the plan is archived as `phase-3-indentation-validation-plan-blocked.md` with the unresolved findings. No Tier 2 spend before convergence.

---

## §12 — Critique discipline (optional reusable process, NEW v4 per PL SR14)

The cycle-1/2/3/4 critique discipline that produced this plan revision is offered as a reusable pattern for future RFCs (specifically those proposing structural language changes). It is *not* binding on this plan; this section codifies institutional knowledge from this RFC's history so it is not lost.

The pattern:

1. **Pre-registration draft authored by RFC champion** (v1).
2. **Parallel critique cycles.** For each cycle: two independent reviewer roles run in parallel — devil's advocate (find statistical/protocol weaknesses) + programming-language designer (find design-level weaknesses). Reviewers should NOT see each other's outputs until both complete.
3. **Author integrates findings into next revision** (v2, v3, v4, ...). For each finding, author documents whether it is `addressed`, `partially addressed with scope rationale`, or `out-of-scope with reason`.
4. **Self-assessment on three axes** after each revision: robust (holds up under hostile reading), complete (covers all raised findings), comprehensive (anticipates next-cycle findings). Aggregate score in [0,100].
5. **Convergence rule.** Iterate until aggregate ≥ 95%, OR until cycle count reaches 4, whichever first. If cycle 4 still < 95%, escalate to project lead with a triage decision (ship-with-known-gaps, defer, archive).
6. **Cycle-N+1 scope constraint.** Each subsequent cycle is told what previous cycles already addressed; reviewers are instructed not to re-raise resolved findings unless they have a NEW angle.

This discipline added ~2-3 weeks to RFC turnaround time but is responsible for catching: pilot-undersized statistical design, sub-feature freeze testing a grammar nobody would ship, RFC underspecification of multi-line constructs, opener-enumeration undercount, H3 instrument paired-vs-unpaired confound, FoldingRangeHandler covertly required, and a real codebase bug (Calor0101 reuse in `Parser.cs:3901`).

Adoption is per-RFC; future RFC authors may borrow, modify, or skip this discipline as appropriate to their proposal's risk profile.

---

## Appendix A — System prompts

*(Verbatim prompts committed in the same PR as this plan at `docs/plans/phase-3-system-prompts/system-prompt-arm-0.md` and `docs/plans/phase-3-system-prompts/system-prompt-arm-I.md`. Token-count parity, hybrid-grammar exposition policy choice (§4), and reviewer signoff recorded in `docs/plans/phase-3-pre-pilot-report.md`.)*
