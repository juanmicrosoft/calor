# Brutal Critique — Path 2 v5 Implementation Plan

**Target:** `docs/plans/path-2-drop-ids-v5-implementation.md`
**Voice:** the original designer of Calor, who has watched the trajectory v1 → v2 → v3 → v4 → v5 → this plan
**Stance:** this document is the strongest deliverable in the path-2 corpus and the first one that's actually executable by a coding agent. The novel content (the 4-tier confidence harness) is genuinely good. **Approve, ship the prerequisite PRs in week 0, begin Phase 1.**
**Date:** 2026-05-25

---

## The one-line judgment

> v5-implementation does something none of the v1–v4 RFCs did: it operationalizes "how does an agent or engineer ship PRs without waiting four days for the statistical gate?" The 4-tier harness (Tier 1 per-PR <60s, Tier 2 per-phase <30min, Tier 3 statistical gate 4–5 days, Tier 4 post-ship 30-day) is the missing piece between "what to build" and "how to know each step is safe." The PR breakdown is concrete (15 PRs named with files, acceptance criteria, reviewer), the pre-registration content is drafted inline (§4), the rollback playbook covers every tier (§6), and the rfc-review-checklist that v4 demanded gets codified (§7). What remains is calibration: a handful of magic numbers without citations, one sequencing inconsistency, and the solo-project elephant in the room.

---

## What this document deserves real credit for

This is the first execution-grade artifact in the path-2 series. Specific wins:

- **The 4-tier confidence calculus (§3) is novel and right.** RFC v5 specifies the §10 statistical gate but stops at "human-run, 4–5 days." That's useless for per-PR confidence — 15 PRs × 5 days is a year of calendar. §3 introduces three lighter-weight tiers (Tier 1 per-PR <60s, Tier 2 per-phase <30min, Tier 4 post-ship 30-day) explicitly distinguished by what they prove and what they don't. The §3.5 "confidence calculus" table is the kind of operational discipline RFCs almost never deliver: "Tier 1 green means X. Tier 1 + Tier 2 green means Y. Tier 1 + Tier 2 + Tier 3 green means Z. Here's what you still can't claim." That table is reusable beyond this RFC.
- **§3.7 "what if the harness has a bug?" is the kind of meta-honesty most plans skip.** The plan acknowledges the harness is code, code has bugs, and ships three mitigations: self-tests on synthetic positive/negative fixtures, a CI canary against a known-good commit, and a triage rule that prevents an agent from bypassing a red harness. Most plans treat the harness as infallible. This one doesn't.
- **§7 codifies the rfc-review-checklist.** v4's critique called for the §13.3 "grep-verify every architectural claim" rule to be inscribed somewhere durable. v5-implementation does it — full draft content in §7, ships as PR-0a. The author rules and reviewer rules are concrete: "every sentence of the form 'the compiler does X' MUST EITHER cite a file:line OR be explicitly marked `[PROPOSED]`." That's not a vibe; that's a checklist item that fails review if violated.
- **PR-2c's grep-verified call-site count is exemplary.** The PR description reads: *"`src/Calor.Compiler/Ids/IdAssigner.cs:175` and `:180` (the 2 production call sites grep-verified in v5)."* This is exactly the discipline §7 demands — not "0.5–2 days, grep to size" (the v4 critique's nit) but "0.5 day, here are the 2 sites, by file:line." This is what citation-grade RFC text looks like.
- **PR-0c benchmark task rubric (§5.1) is the strongest defense against the failures the v2/v3 critiques flagged.** Five concrete properties: solvable-today (≥80% baseline success), multi-edit (≥3 files OR ≥5 locations), cross-reference touching (must stress ID identity), deterministic acceptance (no LLM-as-judge), expected turn count 10–30 (excludes too-easy and too-expensive tasks). Every one of these is a falsification-defense against gate-gameability.
- **§4 is the actual pre-registration content, not a template.** PR-0b doesn't have to author the document; it has to *move* the content from §4 verbatim. That's a one-day PR with no design work, which is exactly what RFC §10.2 requires before Phase 2 implementation can begin.
- **§6.3 "what the agent never does" is the right kind of guardrail.** Five concrete prohibitions ("bypass a red harness by editing the harness itself," "revert a post-ship release without maintainer sign-off," "mark a PR done without pasting Tier 1 output," "author Phase 2 code before the pre-reg doc merges," "edit the pre-reg document after it lands"). Each one is a specific failure mode that motivated reasoning under deadline pressure would otherwise enable. The list reads like it was written by someone who has seen those failures happen.
- **§8.5 "what 'high confidence' means" is a 5-row table.** Action → required confidence → how achieved. This operationalizes a concept the v5 pivot-plan critique demanded ("pre-register what positive looks like") at the per-PR level. The agent does not invent new confidence levels and does not skip levels — explicit, enforceable, agent-friendly.
- **PR dependency diagram in §2.4.** ASCII art that's actually readable. Identifies which PRs can run in parallel, which block which, where the week-2.5 checkpoint sits, and what happens on Phase 2 gate pass vs. fail. This is the kind of artifact that prevents calendar slippage at integration time.

That's ~90% of the document. The remaining 10%:

---

## 1. "Companions" framing is misleading

The top of the document lists three "Companions":

> Companions:
>   - `docs/process/rfc-review-checklist.md` (authored as PR-0a per §2; content drafted here in §7)
>   - `docs/plans/phase-2-measurement-protocol.md` (authored as PR-0b; content drafted here in §4)
>   - `tests/E2E/agent-tasks/templates/path-2-gate/` (6 new task templates per §5)

These don't exist yet — they're *deliverables* of this plan, not pre-existing companion documents. A future reader (or auditor at v6) will check the file system, find them missing, and add a "[VERIFY]" comment against the v5-implementation plan that follows the §7 grep-verify rule.

**Fix:** rename "Companions" → "Companion files (to be authored by PR-0a/b/c per §2)". Adds 6 words; removes the false impression that these files already exist.

---

## 2. Magic numbers in §3.2 lack citation

§3.2's Tier 2 token-delta row:

> Aggregate Δtokens across whole corpus is within expected range (Phase 1: ~9.67 × N_structural_IDs ± 20%; Phase 2: additional ~9.67 × N_symbol_IDs ± 20%) | Tier 2 red if outside range

Two unsourced numbers:

- **~9.67 tokens per ID.** v4 §16.F measured ~10 tokens per ID (mean savings 10, on N=5 sample). Where does 9.67 come from? Is v5 RFC's re-measurement on a larger sample? If so, §3.2 should cite (e.g., "per v5 §16.F revised measurement, N=20"). If not, the figure is a sourceless variant of v4's "~10" and violates the §7 rule (which this same document codifies).
- **± 20% tolerance band.** Why 20% and not 10% or 50%? §16.F's measurement showed ULIDs varied 16–26 tokens (about a 30% range from mean). So per-file variance alone could push a single-file measurement outside ±20%. The corpus-aggregate measurement should average out, but the band isn't justified. **Fix:** mark ± 20% as provisional, calibrate against the first real corpus measurement on a known-green build.

This is the §7 rule being violated by the document that *introduces* §7. Worth fixing before PR-0a lands.

---

## 3. PR-0e is "independent" but listed as a prerequisite

§1.2's table includes PR-0e (§8.3 standalone diagnostic addressing) in the prerequisites. The text below says:

> Phase 1 implementation PRs (PR-1*) cannot start until PR-0a through PR-0d are merged. PR-0e is independent and ships on a different release cadence.

So PR-0e is *not* a prerequisite — it's listed in the prerequisite section. Minor inconsistency that will confuse a reviewer. **Fix:** split §1.2's table into "Hard prerequisites for Phase 1" (PR-0a..0d) and "Parallel ship in week 0" (PR-0e). One header change.

---

## 4. The solo-project elephant: agent and maintainer are the same person

§6.2: *"Tier 2 red on a phase merge: Open an issue with the Tier 2 output; do not attempt to merge-around; wait for maintainer triage."*

§3.3 Tier 3 ownership: *"Owner: repo maintainer."*

§3.4 Tier 4: *"Owner: maintainer schedules the run; agent can be invoked to produce the analysis report."*

In the personal-project-contract scenario (`docs/plans/personal-project-contract.md`), the agent and the maintainer are the same person. The plan doesn't acknowledge this and doesn't say what changes when the two hats are worn by one head.

**Specific failure modes the plan doesn't address:**

- **Tier 2 red triage = self-review.** Without an independent reviewer, "wait for maintainer triage" is "wait for yourself," which is motivated reasoning at its most familiar. The plan should require, in solo mode: "the maintainer hat requires writing the triage reasoning in a committed issue *before* resolving — not in-head." This is a small ritual that defends against motivated self-clearing.
- **Tier 4 scheduling.** A coding agent does not have a persistent 30-day calendar. Without explicit scheduling (calendar entry, cron, repo-CI on a date trigger, or a personal-contract checkpoint), the day-30 monitoring run won't happen. The plan should specify *how* the maintainer schedules — even if it's "add to personal-project-contract.md's monthly checkpoint" or "set a GitHub Actions scheduled workflow."
- **§10.5 retry sign-off.** Same person signs off on their own retry. In solo mode, the retry decision should require committing a written justification (or an external read, mirroring the personal-project-contract's "send to one trusted external reader") before triggering the second $2k–$4k run.

**Fix:** add §8.6 (or a new §9) titled "Solo-mode adjustments" that names these three failure modes and prescribes the mitigation for each. Three paragraphs. Closes a real operational hole without forcing a non-existent reviewer into the diagram.

---

## 5. Tier 1's 60-second budget is asserted, not measured

§3.1 sets the Tier 1 budget at <60s. §3.6 has a remediation plan if the budget is exceeded. Good defensive design — but the document locks in "all PRs must run Tier 1" *before* proving Tier 1 can hit 60s.

The AST round-trip check on `tests/` is the obvious risk — Calor's test fixture corpus is large and parsing every fixture twice could blow the budget on the first measurement.

**Fix:** make PR-0d's acceptance criteria include a *measured* wall-clock benchmark of Tier 1 on the existing corpus. If the harness exceeds 60s, the §3.6 remediation (split into default + extended) must execute in PR-0d, not at PR-1a discovery time.

---

## 6. Arm definitions in §4.3 are time-dependent

§4.3:

> Arm B (Phase 1 only):     release/0.x+1 @ <commit-sha-to-fill> (after PR-1h merges)
> Arm C (Phase 1 + Phase 2): release/0.x+1 @ <commit-sha-to-fill> (after PR-2f merges)

The arms are described as "after PR-X merges" — but the gate runs in week 4. If PR-1h or PR-2f land mid-week 4 (which the calendar implies they might), the arm SHA is recorded *during* the gate run, not before.

For pre-registration honesty, the arm SHAs must be **fixed at gate-run start**. The pre-reg document should reference the commits at "moment of gate kickoff" rather than "after PR-X merges."

**Fix:** §4.3 should say *"Arm B: `release/0.x+1` at the SHA of Phase 1 merge commit, recorded immediately before gate kickoff; Arm C: `release/0.x+1` at the SHA of Phase 2 merge commit, recorded immediately before gate kickoff."* Then `scripts/run-phase-2-gate.sh` records the SHA at start and freezes it.

---

## 7. The 4-5 day gate-run monitoring is under-specified

§3.3 says the agent's role during the Tier 3 gate is:

> Monitor: watch for protocol violations (agent harness crash, model API outage, fixture mutation); if detected, halt and trigger RFC §10.5 retry policy.

A 4–5 day run requires *continuous* monitoring. Who's watching at hour 36? At hour 96? A coding agent in a single session won't span that calendar. The plan implicitly assumes someone (agent across multiple sessions? maintainer with a beeper?) is actively babysitting.

**Fix:** specify the monitoring mechanism. Options:

- **Polling via `ScheduleWakeup`** every 4 hours during the run window.
- **Webhook-based** — `scripts/run-phase-2-gate.sh` posts to a Slack/Teams channel on protocol violation, and the maintainer subscribes.
- **Pre-flight check** — the run only starts when monitoring is confirmed live (somebody is on call).

For solo-project mode, this is a real coordination problem. Worth a paragraph in §3.3 or in the solo-mode addendum from item 4.

---

## 8. §3.5 confidence-calculus table is missing one row

The §3.5 table has 4 rows covering Tier 1, Tier 1+2, Tier 1+2+3, and Tier 4 outcomes. But the most operationally common state during the project is **Tier 1+2 green, Tier 3 not yet run** — that's the state of `release/0.x+1` between Phase 2 merge and gate kickoff (probably 1–2 days).

What can you claim in that state? You can claim Phase 1 is shippable as-is; you cannot ship Phase 2 yet. The table doesn't say this explicitly.

**Fix:** add a row to §3.5:

| Tier 1 + Tier 2 green, Tier 3 pending | "Phase 1 is shippable per its merge gate. Phase 2 code may be merged to the release branch but MUST NOT ship to users until Tier 3 passes." | Anything about Phase 2's agent-benefit. |

One-row addition. Operationally important because it's the state at the moment of week-4 ship/no-ship anxiety.

---

## 9. T-5.7-a through T-5.7-e citation needs verification

PR-1d's acceptance criteria says:

> the 5 test cases in RFC §5.7.6 (T-5.7-a through T-5.7-e) all pass

v4 §5.7.6 was a prose test plan, not enumerated as T-5.7-a..e. I haven't seen v5 RFC contents, but this is the kind of citation §7 demands be grep-verified. If v5 §5.7.6 *does* enumerate them, fine; if it doesn't, PR-1d is depending on test cases that haven't been named.

**Fix:** verify that v5 RFC §5.7.6 explicitly enumerates T-5.7-a through T-5.7-e by name. If not, either update the citation to "the test cases enumerated in v5 §5.7.6" or open a small v5 RFC patch to add the enumeration. Five-minute check, prevents a PR-1d-time scramble.

---

## What the document does NOT need

For the record, the following do not need to be added:

- More PRs. 15 is enough.
- A v6 of the RFC. v5-implementation is consistent with v5.
- More tiers. Four is the right number.
- Per-task gate variance estimates. The gate run will produce them.
- Author commitments beyond what the personal-project-contract already covers.

Most of these were asked for in the v1–v5 RFC iteration cycle and would be wrong here. v5-implementation correctly stops at execution and doesn't try to re-relitigate design.

---

## Recommendation

**Approve v5-implementation as-is. Begin PR-0a..0e in week 0.** Apply the nine small calibration items inline at PR authoring time:

1. Rename "Companions" → "Companion files (to be authored by PR-0a/b/c)" at top.
2. Cite or remeasure the ~9.67 token/ID figure in §3.2.
3. Mark the ±20% tolerance band as provisional; recalibrate from first real measurement.
4. Split §1.2 into "Hard prerequisites" + "Parallel ship items" to clarify PR-0e status.
5. Add §8.6 / new §9 "Solo-mode adjustments" covering: maintainer-hat self-review ritual, Tier 4 scheduling, Tier 3 retry sign-off.
6. PR-0d acceptance criteria must include a measured wall-clock benchmark of Tier 1.
7. §4.3 arm definitions: SHAs fixed at gate kickoff, not "after PR merges."
8. §3.3 monitoring mechanism: pick polling, webhook, or pre-flight check; document it.
9. §3.5 add row for Tier 1+2 green, Tier 3 pending state.
10. Verify v5 RFC §5.7.6 enumerates T-5.7-a..e; patch v5 if not.

(Yes, that's ten items, not nine. Item 9 is small; item 10 is a 5-minute verification.)

**On the v1 → v2 → v3 → v4 → v5 → v5-implementation trajectory:** the RFC iterations were arguing about *what* to build. v5-implementation is the document that says *how to build it without lying to yourself about what you've proven*. The 4-tier harness is the missing piece. Ship the prerequisites in week 0, do the work, monitor at day 30, write the post-mortem.

---

## A note on the meta-trajectory

This is the seventh document in the path-2 series after four RFC revisions and four critiques. The cumulative output is a worked example of how a coding agent and a (solo) maintainer can iterate from "wrong in structure" (v1) through "wrong in details" (v2, v3) through "approvable with patches" (v4) to "execution playbook" (v5-implementation). v4's §13.3 codified the lesson; v5-implementation §7 inscribed it in process; this critique applies it back to v5-implementation itself by grep-checking magic numbers and citations.

If the path-2 work ships successfully, the trajectory is the case study. If it doesn't ship, the trajectory is still the case study — for how to recognize a failed bet before sinking implementation effort. Either way, keep all the documents in tree, link them from `docs/process/rfc-review-checklist.md` as worked examples, and don't repeat the v2 collision-math or v3 cache-fiction failure modes in the next RFC.

---

## One-line summary

v5-implementation introduces a 4-tier confidence harness that solves the "how does an agent ship per-PR without waiting on a 4-day statistical gate" problem the RFC iterations never addressed, codifies the v4 grep-verify rule into `docs/process/rfc-review-checklist.md`, gives a 15-PR concrete breakdown with diagrams and acceptance criteria, drafts the Phase 2 pre-registration content inline, and operationalizes "high confidence" as a 5-row table — leaving only ten calibration nits (most of them under one line each) and one elephant-in-the-room (the agent and maintainer are the same person in solo mode); **approve, ship PR-0a/b/c/d/e in week 0, begin Phase 1 in week 1, do not write a v6 RFC**.

---

*Full path: `C:\Users\juanrivera\sources\repos\juanmicrosoft\calor-2\docs\plans\path-2-drop-ids-v5-implementation-critique.md`*
