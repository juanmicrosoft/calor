# Devil's Advocate Review: `path-2-drop-ids-v5-implementation.md`

**Target:** `docs/plans/path-2-drop-ids-v5-implementation.md`
**Voice:** Engineering-plan reviewer (an implementation plan demands different
critique than an RFC — the discipline being audited here is execution, not
architecture).
**Stance:** The plan is well-shaped and earns most of its claims. The
verdict is **revise once, then green-light**, with one blocking defect (the
file is incomplete) and four concrete scope/methodology corrections.
**Companion to:** the four prior RFC audits
(`path-2-drop-ids-{devils-advocate,v2-devils-advocate,v3-devils-advocate,v4-devils-advocate}.md`),
the four designer-voice RFC critiques, and the existing designer-voice
critique of this implementation plan
(`path-2-drop-ids-v5-implementation-critique.md`). This is the auditor-voice
companion to that critique. **Important corroborating signal:** the existing
designer-voice critique praises a "§8.5 'what high confidence means' 5-row
table" as the operational core of the plan. **That section does not exist
in the current file** — which means either (a) the file has been truncated
since the designer-voice critique was written, or (b) the designer-voice
critique was praising sections that were never delivered. Either way, the
file as it stands is incomplete.
**Date:** 2026-05-25

---

## TL;DR

The shape is right. The 3-tier (4-tier) self-verification harness in §3 is
the genuinely novel content and it solves a real problem (a coding agent
shipping 15 PRs cannot block on a 4–5 day statistical gate per PR). The PR
breakdown is concrete with file paths, acceptance criteria, and reviewers.
The pre-registration content in §4 is drafted inline so PR-0b becomes a
copy-paste. §6's rollback decision tree is operationally usable. The
agent-action discipline in §6.2 and §6.3 makes the coding agent's role
explicit in a way most implementation plans skip.

That said, one blocking defect and four substantive corrections need to land
before this plan can drive PRs:

- **Blocking: the document is incomplete.** §7 truncates mid-sentence at
  line 427 ("the verifier asserts Y, or the migrator…"); §8 — "Definition of
  'high confidence' — per-PR, per-phase, per-release" — is promised in §1.1
  but does not exist. The file claims to deliver §8 and does not. The
  companion designer-voice critique cites a `§8.5` 5-row table as the
  plan's operational core; that table is also absent. The plan's most-cited
  innovation is *currently missing from the artifact*.
- **PR-2c scope is narrower than the actual `IdGenerator.*` usage surface.**
  The plan cites "2 production call sites" (verified at `IdAssigner.cs:175`
  and `:180`) but `grep IdGenerator. src/Calor.Compiler/Ids` returns 5 more
  callers across `IdChecker.cs` and `IdValidator.cs`, including 2 calls to
  `ExtractUlid()` — a method whose name and behavior are ULID-format-specific
  and will require rework for 12-char compact IDs. PR-2c's 0.5-day estimate
  is sized for the `Generate()` callers only.
- **§4.1's task paths are fictional.** The pre-registration document lists
  the existing corpus as `tests/E2E/agent-tasks/fixtures/task-001/` through
  `task-024/`. `fixtures/` does contain exactly 24 directories, but none
  follow the `task-NNN` naming pattern (the actual names are descriptive:
  `advanced-calor-project`, `async-project`, `basic-calor-project`,
  `refactor-*`, etc.). A pre-registration document that lists wrong file
  paths is not pre-registration; it's text.
- **All new scripts are `.sh` on a cross-platform repo.** `scripts/`
  currently contains `.sh`, `.ps1`, `.py`, and `.js` files for portability
  reasons (the `test-enforcement.{sh,ps1}` dual-script pattern is the
  in-repo precedent). PR-0d's `scripts/verify-phase1.sh` + nine other `.sh`
  scripts break on Windows developer machines unless WSL/Git Bash is
  mandated. The plan does not address this.
- **Methodology gap in §5.1's task-selection rubric.** Requiring "baseline
  agent achieves ≥80% success" introduces a ceiling effect at PR-0c that
  caps the gate's signal range at week 4. The threshold should be tuned to
  give the gate measurable headroom — closer to 50–70%, or the metric of
  interest should shift to turn count rather than success rate.

None of these are veto-worthy. The fix-list is one document-edit PR
(complete §7, write §8, correct PR-2c scope, fix §4.1 paths, decide on
cross-platform script policy, retune §5.1). The implementation can proceed
once those land.

---

## §1. What the plan got right

Worth itemizing because the plan invests in operational discipline that most
implementation plans skip, and that effort deserves explicit credit. The
existing designer-voice critique covers this ground at length; this section
mirrors it more briefly to keep the auditor focus on the corrections.

**1.1 The three-tier confidence model is the genuine innovation.** §3.1 (Tier
1: <60s per-PR sanity), §3.2 (Tier 2: <30min per-phase corpus check), §3.3
(Tier 3: the 4–5 day §10 statistical gate), and §3.4 (Tier 4: 30-day
post-ship monitoring) decompose the "how confident am I that this PR is
safe?" question into runtimes and owners that match the cadence of work.
The §3.5 calculus table ("what each tier proves") is a clear-eyed framing
that prevents tier-confusion failures (e.g., reading Tier 2 green as "the
gate will pass," which the table explicitly forbids).

**1.2 The PR breakdown is operationally concrete.** §2.1, §2.2, and §2.3
give 15 PRs with file paths, one-sentence acceptance criteria, the
verification tier each PR must clear, and a named reviewer. The dependency
diagram in §2.4 makes the critical path explicit — PR-0a..d block Phase 1;
the week-2.5 checkpoint on PR-1a gates Phase 2 start.

**1.3 §6's rollback playbook is operationally usable.** Most RFCs gesture at
"if the gate fails, we revert"; this plan writes the decision tree with
specific commands (`calor fix --revert-compact-ids`) and explicit
"investigate first vs. revert immediately" branching that matches the
appropriate risk level for each tier. §6.2 mapping "what the agent does in
each scenario" and §6.3 listing what the agent *never* does are the right
discipline for a coding-agent-driven workflow.

**1.4 Pre-registration is drafted inline so PR-0b becomes copy-paste.** §4
covers all 9 items the RFC §16.E checklist demands, with the data schema in
JSON, the analysis-pipeline library-version slots, the retry-policy
cross-reference, and the explicit "reporting commitment regardless of
pass/fail" clause. This is the largest source of friction in
pre-registration protocols (authors put it off because it's tedious), and
the plan pre-pays the cost.

**1.5 The §5 task-authoring rubric is mostly well-shaped.** Six concrete
properties (solvable today, multi-edit, cross-reference touching,
deterministic acceptance, expected turn-count range, prompt-state
separation) cover the failure modes that have bitten prior agent-eval
corpora. The "no LLM-as-judge in acceptance" rule alone is worth shipping.
(The "solvable today ≥80%" threshold has a methodology issue addressed in
§5 below.)

**1.6 §3.6 and §3.7 are defensive-engineering thinking.** "What if Tier 1
is too slow?" and "What if the harness itself has a bug?" are the kinds of
questions implementation plans usually skip because they're embarrassing to
ask. Documenting the remediation order (profile → split → drop a check) and
the harness-self-test discipline preempts the most likely week-2 panic
("the harness is failing, do we ship anyway?").

**1.7 §1.2's hard-prerequisite ordering matches the RFC.** PR-0a through
PR-0d must merge before any Phase 1 implementation PR; PR-0e ships
independently. The "pre-reg doc commits before any Phase 2 code lands"
discipline matches RFC §10.2's "must be committed" rule rather than
weakening it.

---

## §2. Blocking: the document is incomplete

§1.1 promises §8 — "Definition of 'high confidence' — per-PR, per-phase,
per-release" — as a deliverable produced by this plan. The file ends at
line 427, in the middle of §7 (the `docs/process/rfc-review-checklist.md`
draft content), on the sentence "Every sentence of the form 'the compiler
does X', 'the verifier asserts Y', or 'the migrator". §8 does not exist.

**The companion designer-voice critique already corroborates this is a
regression.** That critique praises:

> §8.5 "what 'high confidence' means" is a 5-row table. Action → required
> confidence → how achieved. This operationalizes a concept the v5
> pivot-plan critique demanded ("pre-register what positive looks like") at
> the per-PR level.

That paragraph is praising content the current file does not contain. Either
the file was truncated after the designer-voice critique was written, or
the designer-voice critique was reviewing a draft that was never committed.
Either reading lands the same operational conclusion: **the plan as it
exists on disk right now is missing the section the companion critique
identifies as its operational core.**

This matters operationally because §8 is the section that resolves the
plan's central thesis. §3 introduces the tier model; §3.5 sketches what each
tier *proves*; but §8 was meant to formalize "what does the agent get to
*claim* after each tier passes?" The §3.5 table is the start of that work,
not the end. Without §8:

- The agent reading the plan does not know whether "Tier 1 green + Tier 2
  green + Tier 3 green" lets it claim shipping authority, or whether human
  sign-off is still required (the rollback playbook §6.2 implies the
  latter for Tier 4, but §3.5 doesn't make that explicit for Tiers 1–3).
- The phrase "high confidence" appears in the plan's own subtitle ("what
  high confidence means operationally") and the deferred §8 was meant to
  give it operational teeth. Currently the plan defines "high confidence"
  three times in passing (§3.1, §3.2, §3.3) without integrating them.
- The PR-0a checklist content in §7 is the *content* of `docs/process/rfc-
  review-checklist.md`, and the cut-off is in the middle of an enumerated
  list item that the author of PR-0a will need to know the rest of. The
  PR-0a author will either have to invent the rest, ask the plan author, or
  block on this clarification.

**Recommendation:** Finish §7 (complete the truncated sentence and any
remaining checklist items) and write §8. The companion designer-voice
critique implies §8 used to exist and can probably be recovered from a
prior draft or commit. Estimate: 1–2 hours of writing or 5 minutes of
`git log -p` recovery. No revision of the plan's substance is required —
both sections are straightforward extensions of what's already there, and
the designer-voice critique's praise of §8.5 suggests the content was once
in good shape.

---

## §3. PR-2c scope is narrower than the actual `IdGenerator` surface

§2.3's PR-2c row reads:

> **PR-2c** — `IdGenerator.Generate()` caller audit + migration (RFC §9.3) —
> `src/Calor.Compiler/Ids/IdAssigner.cs:175` and `:180` (the 2 production
> call sites grep-verified in v5) — Both sites switched to
> `CompactIdGenerator`; legacy `IdGenerator.Generate()` kept for migrator's
> reverse-path only; tests green — Tier 1 — 0.5 day estimate.

The `Generate()` call-site count is correct — verified at `IdAssigner.cs:175`
and `:180`. But `grep "IdGenerator\." src/Calor.Compiler/Ids` returns five
additional callers:

```
IdChecker.cs:156      var expectedPrefix = IdGenerator.GetPrefix(entry.Kind);
IdValidator.cs:56     var kind = IdGenerator.GetKindFromId(id);
IdValidator.cs:64     var ulidPortion = IdGenerator.ExtractUlid(id);
IdValidator.cs:163    var kind = IdGenerator.GetKindFromId(id);
IdValidator.cs:167    var ulidPortion = IdGenerator.ExtractUlid(id);
```

`GetPrefix` and `GetKindFromId` are probably format-agnostic — they likely
just inspect the prefix before the `_`. But `ExtractUlid` is named for, and
presumably parses, the ULID portion of an ID specifically. After Phase 2's
12-char Crockford compact format ships, `ExtractUlid` either needs to be
renamed (`ExtractIdPortion`) and rewritten to accept both formats during the
transition, or removed and replaced with a format-aware extraction.

The two `IdValidator.cs:64` and `:167` calls are inside paths that almost
certainly need behavior changes for Phase 2 — validators must accept the
new format. PR-2c's 0.5-day estimate covers the two `Generate()` sites only
and silently inherits "anything else that breaks tests" as PR-2c's
responsibility without budget.

**Recommendation:** Expand PR-2c's file list to `IdAssigner.cs`,
`IdChecker.cs`, `IdValidator.cs`, and `IdGenerator.cs` itself. Revise the
acceptance criteria to "all `IdGenerator.*` callers either switched to the
compact path or explicitly documented as legacy-only with a comment
referencing the migrator's reverse-path." Revise the estimate to 1.0–1.5
days. Add to the PR description a sub-list of the 7 grep-verified call sites
so the reviewer can confirm coverage.

**Why this matters at the plan level:** the plan inherits the v5 RFC's
factual claim ("2 production call sites grep-verified") and propagates it
into a PR scope. The v5 RFC's claim is correct at the `Generate()`-call
granularity but mis-scoped at the *PR* granularity because the PR has to
ship a working compiler, which means every transitive caller of any
`IdGenerator` method needs to work after Phase 2 lands. This is the
implementation-plan analogue of the architectural-fiction pattern the
v2→v3→v4→v5 audit chain has trained for: a precise-sounding claim that
under-scopes the actual change surface. The companion designer-voice
critique praises PR-2c's grep-verification as "exemplary" — it's a half-step
in the right direction, but the grep was too narrow.

---

## §4. §4.1's pre-registration task paths are fictional

The pre-registration document is the load-bearing artifact of the Phase 2
gate. If it lists wrong file paths, the experiment is not pre-registered
against the artifacts that get run.

§4.1 reads:

> Existing tasks (24):
>   tests/E2E/agent-tasks/fixtures/task-001/  @ <commit-sha-to-fill>
>   tests/E2E/agent-tasks/fixtures/task-002/  @ <commit-sha-to-fill>
>   ... (24 entries) ...

`tests/E2E/agent-tasks/fixtures/` does contain exactly 24 subdirectories.
None of them are named `task-NNN`. The actual names are descriptive —
`advanced-calor-project`, `async-project`, `basic-calor-project`,
`refactor-*` variants, etc. A search for any directory matching
`task-\d+` under `fixtures/` returns zero results.

Two ways this could read:

- **Charitable interpretation:** the `task-001`..`task-024` paths are
  placeholders that the PR-0b author will replace with the real names at
  authoring time. The plan does say at line 224 "with hashes and versions
  filled in at PR-0b authoring time." If the same author is also expected
  to substitute the real fixture names, the plan should say so.
- **Less charitable:** the plan author hasn't looked at the fixture
  directory. The 24-count is right (which suggests they did look), but the
  naming pattern is invented. If the pre-reg doc is checked into PR-0b with
  these names verbatim, the analysis pipeline (`scripts/analyze-gate-
  results.py`, mentioned in §4.6) will look up `fixtures/task-001/` and
  find nothing.

**A second related concern:** the plan also implicitly references
`tests/E2E/agent-tasks/tasks/` via the existing harness infrastructure, and
introduces a new `templates/path-2-gate/` subdirectory for the 6 new tasks
in §2.1 PR-0c. But `tests/E2E/agent-tasks/tasks/` already contains 258
files (verified by directory enumeration). The plan nowhere explains how
`tasks/` relates to `fixtures/` or to the new `templates/` subdirectory.
Are tasks files (small) and fixtures directories (workspaces)? The plan
never says. The gate experiment may run against `tasks/`, `fixtures/`, or
`templates/path-2-gate/` — three different populations, none of which the
plan disambiguates.

**Recommendation:** Before PR-0b is filed, enumerate the actual 24 fixture
directory names and substitute them into §4.1. Add a one-paragraph
clarification to §4 distinguishing `fixtures/` (workspace fixtures, 24
dirs), `tasks/` (task files, 258 files), and `templates/path-2-gate/` (new
templates for this gate, 6 dirs). State explicitly which population the §10
gate runs against.

---

## §5. Methodology: §5.1's "≥80% baseline success" creates a ceiling effect

§5.1's task-authoring rubric requires:

> Solvable today: A baseline agent (Arm A) must achieve ≥80% success across
> 10 seeds. If a task is unsolvable today, it cannot distinguish Phase 1/2
> from baseline.

The motivation is correct: a task that's 0% solvable in all arms is signal-
free. But the threshold is set too high to give the gate measurable
headroom.

Walking the math: if Arm A's mean success rate is 80% (the lower bound the
rubric allows), Arm B and Arm C can show at most a 20-percentage-point
improvement before hitting the 100% ceiling. With 6 tasks at 10 runs per
arm = 60 binary observations per arm, the McNemar test's detection power
for a difference of 20 percentage points is reasonable but a difference of
5–10 percentage points (which is the realistic effect range for an
ID-format change) becomes hard to detect.

The asymmetric concern: if the true effect of Phase 2 is to improve success
rate by ~3 percentage points (a real but small effect), the test will
likely come back "no significant difference" and the maintainer reads that
as "Phase 2 doesn't help." But the right interpretation might be "the
corpus was ceilinged."

The gate's RFC §10.3 kill-criterion (1) is "no success-rate regression,
McNemar p > α'" — a non-inferiority test, not an improvement test. So the
ceiling effect mostly hurts the *positive* case Phase 2 needs to make
(criterion 3: turn count OR token reduction with δ ≥ 0.33). Those criteria
use continuous metrics that don't have the same ceiling issue. Partial
mitigation, not full.

**Recommendation:** Lower the rubric to "baseline agent achieves 50–90%
success across 10 seeds." Add explicit language: "tasks above 90% baseline
should be made harder until they fall in range; tasks below 50% should be
made easier or excluded." Tasks at the edges of the range provide the most
distinguishing power for any non-trivial effect size.

Alternative: keep the 80% threshold but explicitly designate the rubric as
selecting for *turn-count-distinguishing* tasks rather than *success-rate-
distinguishing* tasks, and note that success-rate non-inferiority is the
relevant test under this corpus design.

---

## §6. Cross-platform: all proposed scripts are `.sh` on a multi-platform repo

The existing `scripts/` directory contains:

- `.sh`: `bump-version.sh`, `test-enforcement.sh`, `validate-skill-docs.sh`
- `.ps1`: `compile-audit.ps1`, `test-enforcement.ps1`
- `.py`: `compare-benchmarks.py`
- `.js`: `generate-results-md.js`, `merge-llm-results.js`

The pattern is clearly "write in the right language for the job, and where
a script needs to run on both Linux CI and Windows developer machines,
provide both `.sh` and `.ps1` versions" — see `test-enforcement.{sh,ps1}`.

The implementation plan's PR-0d adds five new `.sh` scripts:
`verify-phase1.sh`, `byte-preservation-check.sh`, `ast-roundtrip-check.sh`,
`token-delta-spot.sh`, plus §3.2 introduces `verify-corpus.sh`,
`migrator-corpus-dryrun.sh`, `token-delta-corpus.sh`,
`migrator-revert-roundtrip.sh`; §3.4 adds `phase1-post-ship-monitor.sh`;
§3.3 introduces `run-phase-2-gate.sh`; §5.3 introduces
`validate-task-template.sh`. Ten new bash scripts total across §3 and §5.

A Windows developer running `scripts/verify-phase1.sh` from PowerShell will
either get "command not found" or accidentally invoke a non-bash shell that
mis-parses the script. The repo currently supports Windows developers
(based on existing `.ps1` versions of similar scripts and the user-facing
CLI being a `dotnet` global tool). The plan's §3.1 "runs in <60s locally"
acceptance criterion implicitly assumes the developer is on a Unix-like
system.

The mitigations the plan should pick from:

- **Write in Python or Node.** Both are already in `scripts/` (`.py` and
  `.js` exist); both are cross-platform; both can be invoked the same way
  from CI and dev machines.
- **Provide `.sh` and `.ps1` versions** following the existing
  `test-enforcement.{sh,ps1}` pattern.
- **Mandate WSL/Git Bash for Windows developers** as the official policy
  and document it in `CONTRIBUTING.md`.

Any of the three is fine. Silently adding 10 bash scripts to a
cross-platform repo without choosing is the failure mode.

**Recommendation:** Add a §2.1 row (or an §1.2 prerequisite row) that
declares the language/platform choice for all new scripts before PR-0d is
authored. Update PR-0d's acceptance criteria to require the chosen platform
support.

---

## §7. Smaller items

**7.1 Tier 1 / Tier 2 CI duplication with `test.yml`.** The existing
`.github/workflows/test.yml` already runs `dotnet build` and `dotnet test
-c Release`. The plan's PR-0d adds `.github/workflows/tier1.yml` whose §3.1
table includes "Unit tests: `dotnet test --filter Category=Unit`." If
`test.yml` runs all tests (no filter) and `tier1.yml` runs the unit subset
plus harness checks, both workflows execute on every PR — unit tests run
twice, doubling CI minutes. Either consolidate (extend `test.yml` with the
harness checks) or define `tier1.yml` to run only the new harness checks
(not `dotnet test`) and rely on `test.yml` for unit/integration tests. The
plan doesn't pick.

**7.2 §3.1 "paste full output into PR description" is performative, not
operational.** The CI run that executes `scripts/verify-phase1.sh` is the
canonical record. Requiring the agent to also paste output into the PR
description duplicates the record, drifts when CI re-runs, and pads PR
descriptions. The mechanism that actually enforces "Tier 1 green required"
is the required-check status on the PR. Drop the paste requirement; keep
the required-check rule.

**7.3 PR-1d (byte-preservation verifier) may be redundant with PR-1c (the
migrator itself).** v5 RFC §5.7.3 explicitly notes that byte-preservation
is provided by the migrator's text-edit step (it removes only the byte
ranges identified as `{id…}` blocks, leaving everything else untouched). If
the migrator is correct by construction, a separate "byte-preservation
verifier" is asserting a property the migrator already provides. PR-1d may
collapse into "tests for PR-1c that assert the byte-preservation property,"
which is one day of test-writing rather than a separate component. Worth
re-examining before authoring PR-1d.

**7.4 §4.4 model pinning doesn't have a fallback.** "Model version
(pinned): e.g., `claude-sonnet-4.5` at the version available on
`2026-MM-DD`" assumes the pinned model is available throughout the
experiment. Models get deprecated mid-experiment. The plan should specify
what happens if the pinned model is unavailable during the gate run —
likely: defer the run until access is restored, or invoke §10.5 retry with
the next available pinned model. One-line addition.

**7.5 `docs/process/` directory does not exist today.** PR-0a creates
`docs/process/rfc-review-checklist.md`; the parent directory will be
created implicitly. Not a defect, but worth noting in PR-0a's acceptance
criteria for the reviewer.

**7.6 §1.1 says §7 produces "PR-0a draft" content for `docs/process/rfc-
review-checklist.md`** but the truncated §7 starts the checklist mid-list.
Once §7 is completed, the line in §1.1 ("§7 draft") will accurately
describe the content. Currently §7 is more like "the first 30% of a draft."

---

## §8. What I am not saying

- The tier model is wrong. It's the most novel and useful content in the
  plan, and the existing designer-voice critique is right to praise it.
- The PR breakdown is too granular. 15 PRs over 8 weeks is the right
  granularity for a coding-agent-driven workflow.
- The pre-registration discipline is unnecessary. It's correctly inherited
  from RFC §10 and pre-paid in §4.
- The rollback playbook is overkill. §6 is the right level of detail; most
  RFCs skip this entirely.
- The plan should be rejected. It should ship after the §2 truncation is
  fixed and the §3, §4, §5, §6 corrections land. None require redesign.
- The designer-voice critique is wrong. It correctly identifies the
  strengths; it's reviewing a version of the file that included §8, and
  praising the §8.5 table is a legitimate review of what was meant to be
  delivered. This audit is what's left after the file ended up shorter than
  intended.

---

## §9. Coda

This is the first auditor-voice review of an implementation plan in the
v1→v5 chain. The shape is good: PR breakdown + tier model + pre-reg content
+ rollback + agent-discipline rules. The defects are concentrated in (a)
the document literally cutting off mid-§7 with §8 missing entirely, (b)
two inherited factual errors from the v5 RFC that propagated into PR
scopes, and (c) the cross-platform script choice that wasn't made.

The most informative single signal in this audit is the divergence between
the existing designer-voice critique (which praises §8.5) and the
on-disk artifact (which has no §8 at all). The designer-voice critique
landed before the truncation, or against a working draft that never got
committed. Either way, the file as it stands is missing the section its
companion critique identifies as the operational core of the plan. This
should be the first thing checked: `git log -p` the implementation file and
see whether §8 ever existed; if so, restore it; if not, write it from the
designer-voice critique's description of what was meant to be there.

The v1→v5 RFC arc earned a "ship" verdict; this implementation plan
deserves a "complete the file, then ship" verdict. Estimated edit time:
2–4 hours, or 5 minutes if §8 is recoverable from git history.

One forward-looking observation: the §7 RFC-review-checklist content is
itself a deliverable that the v5 chain has earned. Codifying the "every
architectural claim cites a file + line and is grep-verified before merge"
discipline as a repo-wide rule is the right institutional capture of the
v2→v3→v4→v5 lesson. The unfinished checklist is a sentence short of
delivering on that.

---

*End of v5 implementation plan devil's advocate review. Prior reviews:
`path-2-drop-ids-{devils-advocate,critique}.md` (v1),
`path-2-drop-ids-v2-{devils-advocate,critique}.md` (v2),
`path-2-drop-ids-v3-{devils-advocate,critique}.md` (v3),
`path-2-drop-ids-v4-{devils-advocate,critique}.md` (v4).
Companion: `path-2-drop-ids-v5-implementation-critique.md` (designer-voice).*
