# RFC v5 — Compact Stable Identifiers: Drop Structural IDs, Compact Symbol IDs

**Status:** Approved for implementation
**Supersedes:** [path-2-drop-ids-v4.md](path-2-drop-ids-v4.md)
**Reviewed against:** [path-2-drop-ids-v4-critique.md](path-2-drop-ids-v4-critique.md), [path-2-drop-ids-v4-devils-advocate.md](path-2-drop-ids-v4-devils-advocate.md)
**Type:** Calibration revision of v4 (not a structural rewrite)

> **Reader's note.** v5 preserves v4's structure and arguments unchanged. It applies 13 targeted calibrations identified by the v4 critique round, plus one new institutional rule (§0.1). For any section not listed in §0 below, **the v4 text stands as written**. This RFC documents only the deltas; refer to v4 for full context on unchanged sections.

---

## §0 — Changes from v4

Both v4 reviewers (designer-voice critique + devil's advocate) explicitly concluded "approve and ship; v5 not necessary." v5 exists to capture their calibrations before implementation so they are not lost in commit messages or follow-up issues.

| # | Section | Source | Change |
|---|---------|--------|--------|
| 1 | §5.7.3 | DA §2 (HARD) + DC §1 | **REPLACED** with byte-preservation form. v4's "trivia anchor verification" assumed AST infrastructure that does not exist in the compiler (verified: `Parsing/Lexer.cs:531` discards comments via `return NextToken()`; AST has no `LeadingTrivia`/`TrailingTrivia` fields; `Token.IsTrivia` covers only whitespace+newline). The verifier now asserts byte-equality over non-migrated regions, which depends only on the text-edit migrator already specified in §5.6. |
| 2 | §16.F | DA §3 | **REPLACED** with N=100 RNG-generated production-format ID measurement. Fixes the `u` violation in the v4 sample `m_q9r8s7t6u5v4` (Crockford lowercase excludes `i,l,o,u` per §6.1) and reduces hand-pick selection bias. New numbers: ULID mean=19.22 tokens, compact mean=9.55 tokens, **savings = 9.67 tokens/occurrence at 95% CI (9.30, 10.04)**. Per-ID reduction: **50.3%** (v4 claimed 44%). Project-wide headline ~20–25% holds; the gate confirms. |
| 3 | §10.6 | DA §4 | **ADDED named statistical test**: Wilcoxon signed-rank on paired per-task turn-count medians, α=0.05. Revert trigger: Wilcoxon p<0.05 AND Cliff's δ magnitude ≥0.2. Sign-off authority: repo maintainer within 14 days of gate-data collection. |
| 4 | §9.3 | DC §3 | **ADDED row** for `IdGenerator.Generate()` caller audit (~0.5 day). Grep-verified scope: `Ids/IdGenerator.cs` is `public static class IdGenerator`, with only 2 call sites in production source — `Ids/IdAssigner.cs:175` and `Ids/IdAssigner.cs:180`. v4's "0.5–2 days" was conservative; the upper bound is unfounded. |
| 5 | §6.3.1 | DA §5 | **ANNOTATION**: "No current caller is multi-threaded; the `ConcurrentDictionary` is defense-in-depth for future multi-threaded migrators." Keeps the design but flags it as forward-looking rather than load-bearing. |
| 6 | §11 | DA §6 | **ADDED week-2.5 Phase 1 dependency checkpoint**: "Phase 1 parser-change PR (the structural-ID drop) must merge by end of week 2 for Phase 2 to start on schedule. If it slips beyond week 2.5, Phase 2 shifts by the slippage." Removes the implicit assumption that Phase 1 is risk-free. |
| 7 | §1 (third bullet) | DC §2 | **TIGHTENED** wording to distinguish verified per-ID savings from projected project-wide savings. v4's bullet conflated the two. |
| 8 | §5.7.2 | DC §4 | **ANNOTATION**: effect-declaration verification rule is "Documented for future migrators; not exercised by the Phase 1 migrator." Avoids implying it ships in v1. |
| 9 | §6.3.1 | DC §5 | **ADDED non-goal**: "Incremental compilation is out of scope for this RFC. If incremental compilation is later added, the `IdRegistry` will need an incremental-rebuild story; that's a future RFC." Closes a question raised in v4 review. |
| 10 | §14 | DC §6 | **WORDING**: "deferred to" instead of "transferred to"; "absorb" instead of "do." Cost transfer to humans/CI was an accidental reading of v4 prose; v5 clarifies. |
| 11 | §9.4 | DA §7 (item 1) | **TASK AUTHORING**: 1.5 days → 3 days (0.5 day × 6 new tasks). v4's 1.5 days assumed batch authoring efficiency that is not realistic for the deliberate, version-pinned templating §10.4 requires. |
| 12 | §0.1 (NEW) | DC §7 + DA §9 | **NEW SECTION**: codify §13.3 "grep-verify every architectural claim" rule as a follow-up institutional action (RFC checklist in `docs/process/rfc-review-checklist.md`). This is the lesson of the v3→v4→v5 trajectory: each round caught one assertion not actually in source (v3 caught the 9-char collision math; v4 caught the cache-result fiction; v5 caught the trivia-anchor fiction). Codifying the rule catches the next instance at PR review rather than at RFC vN+1. |
| 13 | §13 | DA §7 (item 3) | **FLAGGED**: `migration.log.json` location for multi-repo / git-submodule projects is out of scope and deferred to a future "migrator productionization" RFC. |

All other v4 text stands.

---

## §0.1 — Institutional follow-up: grep-verification rule

**Source:** Designer-voice critique §7 recommendation 7; devil's advocate §9 final paragraph.

The v2→v3→v4→v5 trajectory of this RFC exhibits a pattern: each adversarial round caught **exactly one architectural assertion that was not actually present in the source code**.

| Round | Fictional assertion | Caught by | Outcome |
|-------|---------------------|-----------|---------|
| v2→v3 | "9-character compact IDs are collision-safe given current ID density" | v2 critique | Recomputed birthday-collision math; bumped to 12 chars |
| v3→v4 | "Migrator caches resolution results, so re-validation cost is amortized" | v3 critique | No cache existed; argument restructured around fresh re-validation |
| v4→v5 | "Verifier asserts trivia-anchor positions match across migration" | v4 devil's advocate | Lexer discards comments at line 531; no trivia anchors exist. Replaced with byte-preservation form. |

This is not a coincidence; it is a process gap. RFC review currently has no mechanism that **requires** an architectural claim ("X is verified by Y," "Z is cached," "the verifier compares W") to be grep-anchored to a file:line citation in the source tree.

**Action (to land as a separate PR before Phase 1 implementation begins, not blocking on RFC approval):**

Add `docs/process/rfc-review-checklist.md` containing at minimum:

1. **Architectural claim rule.** Every RFC sentence of the form "the compiler does X" or "the verifier asserts Y" must cite a file:line in the source tree, OR be flagged as "proposed; not yet implemented" in-line.
2. **Reviewer responsibility.** RFC reviewers grep-verify every architectural claim. Reviewers who cannot verify a claim with `rg` or equivalent in <30 seconds add a `[VERIFY]` comment.
3. **Author responsibility.** RFC authors who make a claim about future implementation explicitly mark it `[PROPOSED]` so reviewers do not need to verify against current code.

CLAUDE.md should be updated to reference this checklist for all RFC-class documents under `docs/plans/`.

**Owner:** Repo maintainer (RFC checklist), then any contributor (CLAUDE.md cross-reference).
**Effort:** ~2 hours.
**Not blocking:** Phase 1 implementation can begin before the checklist lands; the checklist exists to harden the *next* RFC, not this one.

---

## §1 — Summary (revised)

This RFC proposes a two-phase change to how Calor source files use stable identifiers, replacing v4's bullet list with the following revised version. v4's three bullets become:

- **Phase 1 — Drop structural IDs.** Remove ULIDs from all structural constructs (modules, functions, classes, loops, conditionals, try/catch, etc.). Structural identity is recovered at parse time from source position and tag kind, not from an authored ID.
- **Phase 2 — Compact symbol IDs.** For symbols where stable identity matters across edits (currently: targets of diagnostics with `fix` payloads, and cross-file symbol references), keep an ID but shorten it from 26-char Crockford-uppercase ULID to 12-char Crockford-lowercase compact ID. Collision-safe for ID densities up to 10^7 per repo (recomputed in v3).
- **Token economics.** *Verified per-ID:* a structural ID drop saves ~9.67 tokens at the cl100k_base tokenizer (§16.F, N=100, 95% CI 9.30–10.04); a symbol ID compaction saves ~9.67 tokens per occurrence. *Projected project-wide:* aggregate savings against current Calor sample programs are approximately 20–25% of total token count, gated by the §10 measurement-and-revert protocol before Phase 2 ships.

Headline numbers updated per §16.F re-measurement. All other v4 §1 prose stands.

---

## §5.7.2 — Effect declaration verification rule (annotation)

The effect-declaration verification rule (full text in v4 §5.7.2) **stands as written for future migrators**, including third-party migrators that rewrite effect signatures. **It is not exercised by the Phase 1 migrator** specified in §5.6, which only removes `{id…}` blocks and does not touch effect declarations. Phase 1 verifier implementations may skip the effect-comparison code path. The rule is documented now so a future RFC adding an effect-rewriting migrator does not need to re-derive it.

---

## §5.7.3 — Source byte preservation outside migrated regions (REPLACED)

> **v5 replaces v4's "trivia anchor verification" rule entirely.** v4 asserted the verifier compared AST-level trivia anchors before and after migration. The Calor parser has no such anchors: `Parsing/Lexer.cs:531` returns `NextToken()` immediately after a comment token, discarding the comment with no AST attachment; `Parsing/Token.cs:375` defines `IsTrivia => Kind is TokenKind.Whitespace or TokenKind.Newline` — comments are not trivia. Building the anchor infrastructure would add 2–4 weeks of compiler work that v4 did not account for. v5 reframes the property in terms the migrator can actually verify.

### Property

The migrator's behavior, as specified in §5.6 step 3, is a text-edit pass: it identifies byte ranges that match the `{id…}` pattern adjacent to a structural-tag opener (`§M{…}`, `§F{…}`, `§L{…}`, etc.) and deletes only those byte ranges. **All source bytes outside the deleted ranges remain byte-equal between the original and the migrated source.**

### Verifier rule

For each migrated file:

1. Reconstruct the "would-be-original" by re-inserting the deleted `{id…}` blocks at their recorded positions (positions logged by the migrator into `migration.log.json` per §5.6 step 4).
2. Assert byte-equality between the reconstructed source and the original source file on disk before migration.

This is a **byte-equality assertion over non-migrated regions**, not an AST comparison.

### What this guarantees

- **Comments preserved:** comment bytes are outside the deleted `{id…}` ranges, therefore preserved byte-for-byte.
- **Whitespace preserved:** whitespace bytes outside the deleted ranges are preserved byte-for-byte.
- **String literals preserved:** any `{id…}`-shaped substring inside a string literal is not adjacent to a structural-tag opener, therefore not matched by the migrator, therefore preserved.
- **No AST infrastructure required:** the verifier needs only the file-on-disk and the `migration.log.json` produced in the same migrator run.

### What this does not guarantee (and why that's fine)

- Does **not** assert that the parser produces identical ASTs before and after migration. That is a separate property covered by §5.7.1 (the round-trip parse-and-emit test). v4's §5.7.3 redundantly tried to assert it via trivia anchors.
- Does **not** require comments to retain semantic association with a specific syntactic construct. Calor source does not currently use such associations, and adding them is out of scope.

### Implementation cost

This verifier is ~30 lines: read original, read migrated, read log, apply log in reverse, `Span<byte>.SequenceEqual`. Compared to v4's anchor-comparison plan (which would have required building leading/trailing trivia fields on every AST node), this is a **multi-week-to-multi-day reduction**.

---

## §5.7.6 — Test plan adjustment

v4 §5.7.6 enumerated test cases for trivia-anchor verification. v5 replaces those cases with byte-preservation tests:

- **T-5.7-a:** Migrate a file with no `{id…}` blocks. Assert migrated bytes equal original bytes (log empty, no edits).
- **T-5.7-b:** Migrate a file with one `{id…}` block. Reconstruct using log, assert byte-equality.
- **T-5.7-c:** Migrate a file with a `{id…}`-shaped substring inside a string literal. Assert the substring is not removed and bytes outside any matched range are preserved.
- **T-5.7-d:** Migrate a file with comments adjacent to structural-tag openers. Assert comment bytes are preserved.
- **T-5.7-e:** Migrate a file, mutate the migrated file, then attempt reconstruction. Assert the verifier reports the mismatch and fails.

Test count is preserved (5 cases). Each case is unit-testable without parser changes.

---

## §6.3.1 — IdRegistry concurrency and incremental-compilation scope

### Concurrency (annotation)

v4 §6.3.1 specified the `IdRegistry` as backed by `ConcurrentDictionary`. v5 annotates this as **defense-in-depth**: no current caller of `IdRegistry` is multi-threaded. Grep-verified: `Ids/IdAssigner.cs` is the only production consumer, and it runs in a single-threaded pass after parse. The `ConcurrentDictionary` choice anticipates future multi-threaded migrators or build-system integrations and costs nothing in single-threaded use. If a future RFC removes the multi-thread possibility, swapping to `Dictionary<TKey, TValue>` is a 1-line change.

### Incremental compilation (non-goal)

Incremental compilation — partial recompiles where only changed files are reparsed — is **out of scope for this RFC**. The current build is whole-program; the `IdRegistry` is rebuilt from scratch on every run. If incremental compilation is later added (no current proposal does so), the registry will need an incremental-rebuild story (e.g., persistent serialization, invalidation on dependency edges). That story is a future RFC, not a Phase 2 prerequisite.

---

## §9.3 — Audit rows (added)

v4 §9.3 listed implementation tasks. v5 adds:

| Task | Estimate | Owner | Notes |
|------|----------|-------|-------|
| Audit `IdGenerator.Generate()` callers; update each to compact-ID path | **0.5 day** | Compiler | Grep-verified scope: `Ids/IdGenerator.cs:60–64` is the only `Generate()` method; only 2 call sites in production — `Ids/IdAssigner.cs:175` and `Ids/IdAssigner.cs:180`. Test source may add more; budget covers both. |

v4's "0.5–2 days" upper bound was conservative absent the grep verification. v5 narrows it to 0.5 day.

---

## §9.4 — Task authoring (revised estimate)

v4 §9.4 estimated 1.5 days for authoring the 6 new measurement-gate task templates listed in §10.4. v5 revises this to **3 days** (0.5 day × 6 templates).

**Why the revision:** §10.4 requires version-pinned templates with both pre- and post-migration variants, baseline runs, and reviewer instructions. v4's 1.5 days assumed authoring efficiency from batched template work; the devil's advocate correctly noted that deliberate, paired template authoring does not benefit much from batching. 0.5 day per template — including review, dry-run, and template-of-templates harmonization — is the realistic budget.

Total §9 budget impact: +1.5 days, absorbed within the Phase 2 buffer in §11.

---

## §10.6 — Statistical test for the measurement gate

v4 §10.6 specified the gate threshold (24% project-wide token reduction or revert) but did not name a statistical test for the *turn-count regression* failure mode. v5 specifies:

### Test

**Wilcoxon signed-rank test** on paired per-task turn-count medians (pre- vs post-migration), α = 0.05.

- **Pair unit:** one §10.4 task template; pre-migration median over N=10 runs vs post-migration median over N=10 runs.
- **Sample size:** all 6 §10.4 task templates produce paired samples; n=6 pairs is small but Wilcoxon is well-defined for n ≥ 6 (asymptotic approximation begins around n=20; exact test for smaller n).
- **Direction:** one-sided test that post-migration turn count is **not greater than** pre-migration turn count.

### Revert trigger

Both conditions must hold to trigger revert:

1. **Wilcoxon p < 0.05** (statistically significant turn-count increase).
2. **Cliff's δ magnitude ≥ 0.2** (effect size at least "small").

Either condition alone is insufficient: significance without effect-size protects against false positives from small n; effect size without significance protects against noise-driven swings.

### Sign-off authority and deadline

- **Authority:** Repo maintainer (per `CODEOWNERS`).
- **Deadline:** Decision (ship vs revert) within **14 calendar days** of gate-data collection completion.
- **Default if deadline missed:** revert. This is intentionally biased toward reverting silently-stalled rollouts.

### What this protects against

A scenario where projected token savings (§16.F) materialize but agent-turn behavior degrades — e.g., agents waste turns disambiguating positionally-referenced constructs that previously had stable IDs. v4 noted this risk in §13 item 4 but did not specify the gate measurement. v5 closes the gap.

---

## §11 — Release sequencing (added checkpoint)

v4 §11 specified a 6-week release plan with Phase 1 and Phase 2 in sequence. v5 adds a **week-2.5 Phase 1 dependency checkpoint**:

> **Week 2.5 checkpoint:** Phase 1 parser-change PR (the structural-ID drop in `Parsing/Parser.cs`) must be merged to main by end of week 2 for Phase 2 to start on schedule at week 3. If Phase 1 PR has not merged by end of week 2.5, Phase 2 start date shifts by the slippage. The repo maintainer makes this call at the week-2.5 sync.

**Why:** v4's plan implicitly assumed Phase 1 is risk-free, but Phase 1 touches the parser, which historically has the highest test-failure rate of any compiler subsystem. A 2.5-day buffer between "Phase 1 done" and "Phase 2 start" absorbs typical parser-change rework without invalidating the 6-week headline.

---

## §13 — Honest residual concerns (additions)

v4 §13 enumerated 11 residual concerns. v5 adds two:

### §13.12 — Multi-repo / submodule projects

The `migration.log.json` location (currently spec'd as repo-root per §5.6 step 4) is unclear for projects spanning multiple git submodules or repos. A pragmatic default would be one log per repo; a "true cross-repo" migration would need an aggregator. **Out of scope for this RFC; flagged for a future "migrator productionization" RFC** that addresses log location, retention, log schema versioning, and cross-repo coordination.

### §13.13 — Lesson: the trivia-anchor fiction

v4 §13.10 captured the "cache-result fiction" lesson from the v3→v4 critique round. v5 adds the analogous v4→v5 lesson: **the trivia-anchor fiction**. v4 §5.7.3 specified a verifier that compared AST trivia anchors before and after migration, on the implicit assumption that the AST had such anchors. It does not. The fiction survived three RFC iterations because no reviewer grep-verified `Lexer.cs` or the AST node definitions for trivia fields. §0.1 codifies the institutional rule that catches the next analogous fiction at review time.

---

## §14 — Pivot plan reconciliation (wording)

v4 §14 used the phrases "cost transferred to humans/CI" and "humans do the work." v5 replaces with:

- "cost transferred to" → **"cost deferred to"**: emphasizes the cost is paid later (at PR review and CI execution), not redirected to a different party.
- "humans do the work" → **"humans absorb the validation work"**: clarifies that the work is bounded validation (PR review for the `{id…}` removal pattern), not unbounded labor.

The substantive position is unchanged; v4's wording read as if responsibility was being delegated, which the devil's advocate flagged as misleading.

---

## §16.F — Token measurement (REPLACED)

> **v5 replaces v4 §16.F entirely.** v4 used 5 hand-picked sample IDs, included a Crockford-invalid sample (`m_q9r8s7t6u5v4` contains `u`), and reported ULID mean=23.4 and compact mean=13. v5 uses 100 RNG-generated production-format IDs with the correct Crockford alphabet. Token counts came down; the savings claim survived.

### Methodology

- **Tokenizer:** `tiktoken.get_encoding('cl100k_base')` — Python tiktoken, the encoding used by GPT-4 family models including those typically backing agentic coding flows.
- **ULID alphabet (Crockford uppercase, 32 chars):** `0123456789ABCDEFGHJKMNPQRSTVWXYZ`. Excludes `I, L, O, U` per Crockford Base32.
- **Compact alphabet (Crockford lowercase, 32 chars):** `0123456789abcdefghjkmnpqrstvwxyz`. Excludes `i, l, o, u` per §6.1.
- **Sample size:** N = 100 IDs of each form, generated via `secrets.choice` from the appropriate alphabet.
- **Format:** `m_` prefix (module ID) + 26 chars (ULID) or 12 chars (compact). Other prefixes (`f_`, `l_`, etc.) have analogous behavior; module form was chosen as representative.

### Reproduction script

```python
import tiktoken, secrets, statistics, math

enc = tiktoken.get_encoding('cl100k_base')

ulid_alpha = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'        # Crockford upper, 32 chars
compact_alpha = '0123456789abcdefghjkmnpqrstvwxyz'     # Crockford lower (excl i,l,o,u)

ulids = ['m_' + ''.join(secrets.choice(ulid_alpha) for _ in range(26)) for _ in range(100)]
compacts = ['m_' + ''.join(secrets.choice(compact_alpha) for _ in range(12)) for _ in range(100)]

ulid_tokens = [len(enc.encode(x)) for x in ulids]
compact_tokens = [len(enc.encode(x)) for x in compacts]

diffs = [u - c for u, c in zip(ulid_tokens, compact_tokens)]

mean_u = statistics.mean(ulid_tokens)
mean_c = statistics.mean(compact_tokens)
mean_diff = statistics.mean(diffs)
sd_diff = statistics.stdev(diffs)
ci_half = 1.96 * sd_diff / math.sqrt(len(diffs))

print(f'ULID mean={mean_u:.2f} sd={statistics.stdev(ulid_tokens):.2f}')
print(f'Compact mean={mean_c:.2f} sd={statistics.stdev(compact_tokens):.2f}')
print(f'Savings per ID: mean={mean_diff:.2f}, 95% CI ({mean_diff-ci_half:.2f}, {mean_diff+ci_half:.2f})')
print(f'Per-ID reduction: {100*mean_diff/mean_u:.1f}%')
```

### Results (N = 100)

| Metric | ULID (26-char Crockford upper) | Compact (12-char Crockford lower) |
|--------|-------------------------------:|----------------------------------:|
| Mean tokens | **19.22** | **9.55** |
| Std dev tokens | 1.56 | 1.10 |

| Savings | Value |
|---------|------:|
| Mean savings per ID | **9.67 tokens** |
| 95% CI on savings | **(9.30, 10.04)** |
| Per-ID reduction | **50.3%** |

### Interpretation

- **The savings claim survives.** A 9.67-token-per-ID reduction at a tight 95% CI (width 0.74 tokens) defeats the "v4's numbers could halve under independent measurement" concern from the devil's advocate.
- **The absolute numbers were inflated in v4.** v4's ULID mean (23.4) and compact mean (13) appear to have been hand-picked from samples that tokenized worse than random samples. v5's RNG-generated samples regress toward what cl100k_base produces on typical Crockford output.
- **Per-ID reduction is higher than v4 claimed.** v4 reported ~44% per-ID reduction; v5 measures 50.3%. The headline project-wide savings projection (~20–25%, gated by §10) is unchanged because project-wide savings depend on ID density and surrounding token mass, not on per-ID savings alone.
- **The `u` violation is fixed.** v4's sample `m_q9r8s7t6u5v4` contained `u`, which §6.1 excludes from the compact alphabet. v5's RNG generator draws from the correct alphabet.

### Why N = 100 is sufficient

The standard error of the mean savings is 1.56/√100 ≈ 0.156 tokens, giving a 95% CI half-width of ~0.31 tokens. The reported CI half-width of 0.37 reflects the slightly wider standard deviation of the paired differences; either way, the CI is much narrower than any plausible decision threshold. Increasing N would tighten the CI but not change the conclusion: savings of 9.67 tokens per ID is firmly inside the CI.

---

## §17 — Decision

v4's recommendation stands, refined by v5's calibrations:

1. **Approve Phase 1.** Remove structural IDs from the parser, rewriter, and migrator per §5. The §5.7.3 byte-preservation verifier replaces the v4 trivia-anchor verifier and removes the 2–4 week of AST work that v4 inadvertently required.
2. **Approve Phase 2 contingent on the §10 measurement gate.** Compact symbol IDs per §6, but ship only if the gate passes both the 24% project-wide token-reduction threshold and the §10.6 Wilcoxon-based turn-count regression check.
3. **Begin §0.1 institutional rollout in parallel.** Land `docs/process/rfc-review-checklist.md` before Phase 1 implementation begins, to harden the next RFC.
4. **Track residual risks per §13.** The 13 items in §13 (11 from v4 + §13.12 multi-repo flag + §13.13 trivia-fiction lesson) are not blocking but should be triaged into follow-up issues at Phase 1 kickoff.

This RFC is the v2→v3→v4→v5 chain's terminal version barring discovery of another architectural fiction. The convergence of both v4 critiques on "approve and ship" suggests further iteration is diminishing returns; v5 captures their calibrations so they are not lost.

---

## Appendix: v4→v5 review summary

| Reviewer | Verdict | Calibrations addressed in v5 |
|----------|---------|------------------------------|
| Designer-voice critique | "Approve as-is. Begin implementation." | All 7 recommendations folded in (§5.7.3 replaced, §1 verified-vs-projected, §9.3 audit row, §5.7.2 annotated, §6.3.1 incremental non-goal, §14 wording, §0.1 institutional rule). |
| Devil's advocate | "Approve and ship. A v5 is not necessary." | All 3 HARD/calibration fixes folded in (§5.7.3 replaced, §16.F re-measured with N=100 RNG, §10.6 named statistical test). Smaller items §5, §6, §7 (multi-repo, week-2.5 checkpoint, task-authoring revision, multi-thread annotation) also folded in. |

**Both reviewers said v5 is not necessary.** v5 exists to capture calibrations *before* implementation so they survive into PR review, not because v4 was unsafe to ship.
