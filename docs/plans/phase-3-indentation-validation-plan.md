# Phase 3 Validation Plan — Indentation-Delimited Blocks

**Status:** Draft pre-registration document. Becomes immutable once merged
under the same RFC §10.5 amendment discipline as
[`phase-2-measurement-protocol-v2.md`](phase-2-measurement-protocol-v2.md).
**RFC:** [`phase-3-indentation-rfc.md`](phase-3-indentation-rfc.md).
**Author:** drafted alongside the RFC.

> **No compiler surgery before pilot signal.** The §3 pilot must clear
> its decision rule (§5.2) before any Phase 3 lexer/parser work is
> authored. If the pilot fails, this plan and the RFC are archived; no
> code is written.

---

## §1 — Hypothesis (primary, single, pre-registered)

> **H1.** Replacing explicit structural closers with indentation
> increases the median completion rate of AI-agent-authored Calor tasks
> by at least **3 percentage points** vs the post-Phase-1 baseline,
> while the bottom-quartile completion rate does not decrease by more
> than 2 percentage points.

The asymmetric tolerance (lift ≥ 3 pp median, regression ≤ 2 pp p25)
reflects the asymmetric trade-off in §6 of the RFC: indentation must
clearly help the median case to justify the new whitespace-bug class.

**Single primary metric:** agent task-pass rate, identical to the Phase 2
gate (`tests/E2E/agent-tasks/run-agent-tests.sh` driver, three-runs
majority-pass rule per trial).

Secondary, descriptive only (not part of the decision rule):
- median character count per file (proxy for the proposed win)
- frequency of `Calor0099*` (indent) diagnostics (proxy for the new bug class)
- frequency of `Calor0101` (mismatched id) diagnostics (proxy for the removed bug class)
- median turns to completion (proxy for agent cognitive load)

---

## §2 — Substrate

Reuse the Phase 2 trial substrate, with two changes:

- Drop the 6 `template:path-2-gate/*` trials. They were authored around
  ID-shape questions; they have no informational power for an indent
  question.
- Add 4 new `template:path-3-gate/*` trials, purpose-built to exercise
  indent-sensitive editing: *insert-a-nested-loop*, *wrap-existing-block-in-try-finally*,
  *extract-method-from-deep-nest*, *paste-cs-snippet-and-convert*.

Final substrate: **28 trials** (24 carried-forward `task:` + 4 new
`template:` trials). Manifest path: `tests/E2E/agent-tasks/phase-3-gate-tasks.txt`.

---

## §3 — Two-tier protocol

### Tier 1 — Pilot (cheap, fast, kill-or-proceed)

- **6 trials** stratified across category (selected from §2 to maximise
  coverage variance; manifest committed before run).
- **10 seeds per trial per arm**, 2 arms = **120 runs total**.
- Wall-clock budget: ~1 hour on a 4-worker driver.
- Dollar budget: ~$5 of model spend (matches Phase 2 pilot rates).
- **Goes/no-goes proceed-to-Tier-2 rule:** §5.1.

### Tier 2 — Full gate (only if Tier 1 passes)

- **28 trials × 30 seeds × 2 arms = 1,680 runs.**
- Same driver, same harness as the Phase 2 gate.
- Wall-clock budget: ~12 hours on a 8-worker driver.
- Dollar budget: ~$80 of model spend.
- **Final ship-or-shelve rule:** §5.2.

### Why two tiers

Phase 2 spent ~$120 on a full gate that returned δ ≈ 0. The Tier 1
pilot is a forcing function: it costs <5% of the full gate and either
shows a signal or it doesn't. If it doesn't, we shelve and save $75.

---

## §4 — Arms (exactly two)

**Arm 0 — `phase-1-baseline`**
Current `main` after PR #624 lands. Compact opener form available;
closers required.

**Arm I — `phase-3-indent`**
A throwaway implementation branch with §4.1–§4.3 of the RFC applied.
No `calor fix --to-indent` (out of scope for the gate; we want to
measure the *grammar*, not the migrator). Samples and task fixtures
under `tests/E2E/agent-tasks/` are pre-migrated to indent form *only
for Arm I*; Arm 0 sees the unmodified post-Phase-1 form.

System prompts for the agent reference the indent grammar in Arm I and
the closer-required grammar in Arm 0. Prompts otherwise identical.

> **No Arm B / Arm C.** Phase 2 had three arms to factor compact-id
> from drop-closer-id. Phase 3 has one knob (indent yes/no), so two
> arms is exactly right.

---

## §5 — Decision rules (pre-registered)

### 5.1 Pilot → full gate

Proceed to Tier 2 **iff all of:**

1. **Directional signal:** Arm I median pass rate is at least 1 pp
   above Arm 0 median pass rate across the 6 pilot trials.
2. **No catastrophic regression:** no single trial drops by more than
   10 pp in Arm I.
3. **No instrument failure:** at least 5 of 6 trials produce non-zero
   pass rates in *both* arms (zero-zero rows are evidence the trial
   instrument is broken, not the language).

If any of (1)/(2)/(3) fails, the RFC is shelved as
`phase-3-indentation-rfc-pilot-rejected.md` with the empirical
evidence.

### 5.2 Full gate → ship

Adopt indent grammar (proceed to implementation hardening + ship)
**iff all of:**

1. **Median lift:** Arm I pass-rate median across 28 trials ≥ Arm 0
   median + 3 pp.
2. **No bottom-quartile regression:** Arm I p25 ≥ Arm 0 p25 − 2 pp.
3. **Significance:** paired Wilcoxon signed-rank test on per-trial
   pass rates, two-sided, **p < 0.05** with Cliff's δ ≥ 0.15.
4. **No quality regression in secondaries:** `Calor0099*` diagnostic
   rate per trial in Arm I < 5% of total compiles (i.e. the new
   diagnostic class is not dominating).

Failing 5.2: archive the results doc on the RFC branch as
`phase-3-indentation-results.md` and shelve the RFC. Do not ship.

Passing 5.2: open an implementation PR with full §4.1–§4.6 of the RFC,
including the `calor fix --to-indent` migrator. The `samples/` and
LSP tests must pass on the indent grammar before merge.

---

## §6 — What we explicitly will not measure

- **Human-developer ergonomics.** This compiler is targeted at AI
  agents; the gate measures agent behaviour. A human-developer survey
  is out of scope. (It can be a follow-up if 5.2 passes.)
- **Compile-time performance.** Lexer with indent tracking is O(n);
  the cost is negligible vs the typecheck/verify passes. Not worth
  the measurement budget.
- **Diff-size in PR reviews.** Subjective and tooling-dependent.
- **Editor adoption cost.** VSCode extension changes are scoped
  separately if the gate passes.

---

## §7 — Implementation budget for the gate itself

The Tier 1 pilot requires a working Arm I build. That is:

- A throwaway branch with §4.1 (lexer indent tokens), §4.2 (parser
  dedent acceptance), and §4.3 (writer). Estimated **~1–2 weeks** of
  compiler work for a half-baked but gate-runnable build. Reject paths,
  edge cases, and `calor fix --to-indent` are explicitly post-gate.
- Pre-migrated `tests/E2E/agent-tasks/` fixtures for Arm I. Estimated
  **~1 day** with a one-off migration script.
- Driver wiring (a 2-arm variant of the Phase 2 driver). Estimated
  **~0.5 day**.

Total: ~2 weeks of compiler eng + ~1.5 days of harness eng before
the pilot can run.

> **The gate cost ($5) is dwarfed by the build cost (~2 weeks).** This
> is the *honest* reason we two-tier the gate: the build cost is high
> enough that we want maximum information from the cheapest possible
> spend before committing to a 28-trial full run.

---

## §8 — What "ship" means if 5.2 passes

Three-step shipping plan, in order:

1. Land §4.1–§4.6 implementation in `main`. Documentation updated to
   show indent as canonical. Closer form retained behind a
   `--compat-explicit-closers` parser flag for one minor version.
2. Run `calor fix --to-indent` over `samples/`, `docs/`, and
   `tests/E2E/agent-tasks/`. Commit byte-equivalent rewrites.
3. After one minor version, remove the compat flag.

Each step is its own PR. Step 1 lands behind a feature flag in
`Directory.Build.props`; the flag is removed in step 3.
