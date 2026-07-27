# M4b Kickoff — WS5 Wedge Probe

**Kickoff record (2026-07-27):**
**Parent:** `loop-plan-v0.9.md` §2 WS5, §3 M4b, §5 PP-W1, §6.2 Call 2 ·
**Depends on:** M1 (merged); D4.4 registration (Annex **A-1.2**, this PR) ·
**Prior milestones:** M3 complete (`loop-m3-ws2.md`), M4 complete
(`loop-m4-ws3.md`)

## 1. What the probe measures, restated operationally

The program's differentiated-value claim — *enforcement catches what
agent-generated C# evidence misses* — is 0-for-165 observed because frontier
agents do not ship organic bugs at authorable fixture scale (strategy §9).
WS5 sidesteps that with **injected defects**: each probe pair is a working
fixture carrying one seeded regression that violates intent the fixture
declares in both arms' idiomatic evidence forms (Calor: `§Q`/`§S`/`§E`;
C#: doc comments + NRT under the strategy-§0 full toolkit). The agent gets
an adjacent modification task whose spec never mentions the defect.

- **M-W1 (defect catch rate)**, per arm: fraction of injected defects
  **absent at declared-done**, measured by one per-defect held-out probe
  test (fails iff the defect is present), majority across runs per defect.
- **PP-W1** adjudicates the Calor−C# catch-rate delta (Annex A-1.2 for the
  frozen threshold).
- Telemetry additionally records *how* a catch surfaced (defect-linked
  diagnostic in agent-visible output: `Calor0410` chain,
  `ContractViolationException`, test failure) — reported, not adjudicated.

**Honest limit (plan text, restated):** injected defects measure detection
capability, not organic incidence. A hit proves the mechanism works where
it claims to; zero-vs-zero is decisive in the other direction and triggers
Call 2's pre-committed pivot-or-stop.

## 2. Priced decisions (recorded at kickoff, per §3 kickoff discipline)

**Defect classes bind to the claim as stated; the known holes are excluded
and listed.** Three classes, three defects each (N = 9):

| Class | Seeded regression | Calor claimed catch channel |
|---|---|---|
| **W5-A** undeclared side effect | a `§E{}`-declared (pure) function gains an effectful *named static* call | build-blocking `Calor0410` with call chain (`EffectEnforcementPass`) |
| **W5-B** violated scalar contract | off-by-one/boundary slip making a declared `§S` false on inputs the arm-shared smoke test exercises | **Debug-mode runtime guard** (`ContractViolationException`) thrown during any build+test the agent runs |
| **W5-C** laundered effect via covered chain | a public caller declared `§E{fs:r}` reaches a write through a covered call chain whose intermediate declares its own effect *honestly* — the violated declaration is the caller's (distinguishing W5-C from W5-A, where the defective function's own declaration is wrong) | build-blocking `Calor0410` with the full chain (`EffectEnforcementPass` interprocedural SCC propagation) |

**Excluded paths (the documented holes — the probe measures the claim as
stated, not the gaps):** delegate-typed invocations (assumed pure by
default; warning-only even under `--strict-effects` —
`EffectEnforcementPass.cs:657` and the pinning tests
`DelegateInvocation_*` in `Calor.Enforcement.Tests`), and override/
interface dispatch effect-laundering (no effect-variance rule; strategy
§1.1 r3, fix scheduled 2a item 4). Any defect whose catch would require
these paths is out of scope for PP-W1 and disqualifies the fixture.

**W5-B's catch channel is the runtime guard, not static verification —
because of #807/#755.** Static verify currently refutes *every*
result-referencing postcondition with an unconstrained `result` (#807), so
a verify-based catch cannot distinguish a seeded violation from a spurious
refutation of correct code — the channel is contaminated and **excluded
from the probe until #807 is fixed** (revisit trigger: #807 closed →
probe may re-run with the verify channel and tighter classes). Runtime
guards are unaffected by #807 (guards are emitted for non-`Proven`
contracts) but #755 means a vacuously-`Proven` precondition elides its
guard — so W5-B fixtures use **postconditions only**, chosen to be
non-`Proven` (guard emitted) and violated on smoke-test inputs. Fixture
acceptance (PR 2) verifies the guard actually throws pre-agent.

**Both arms get identical task surface and identical agent-visible smoke
tests.** Each pair ships a small arm-shared smoke test project the agent
can run (same tests, both arms — the C# arm is not handicapped by hidden
coverage). The defect-probe tests live in the *held-out* suite (never
agent-visible), one per defect, alongside the pair's normal held-out
behavioral tests. "Caught" is adjudicated solely on the probe test at
declared-done; agent-visible surfacing telemetry is attribution color.

**The defect is pre-existing in the starter fixture, not injected
mid-run.** The agent's task is an adjacent feature change that requires
building the project (so build-blocking catches surface on iteration 1 in
the Calor arm if the claim holds) and touches the defective module's
neighborhood without the spec naming the defective behavior. The C# arm's
fixture carries the same defect and the same declared intent (doc
comments + NRT); its full toolkit (NRT, `EnforceCodeStyleInBuild`,
whatever tests the agent writes) is free to catch it.

**Mixed-project shape: the existing calor-arm template is the mixed
project.** The plan's "Calor module inside a C# solution" is satisfied by
the established pattern — a `Microsoft.NET.Sdk` C# project consuming
`.calr` sources via `Calor.Tasks`/SDK targets, with the held-out project
referencing the emitted assembly. No new project-system work.

**W5-C is intra-module laundering, not cross-module — a constraint
discovered at fixture-authoring time and priced here.** Cross-module
laundering is architecturally identical for enforcement
(`CrossModuleEffectEnforcementPass` catches it, verified), but a
pre-existing emitter gap (#809: cross-module calls emit unqualified C#, so
a multi-module project's defect-FREE reference can never link under
MSBuild/csc — and the qualified spelling is `Unknown:*` under the strict
policy) means no cross-module fixture can ship a working reference
solution. The intra-module chain exercises the same claim — the violated
declaration is the caller's, the intermediate's declaration is honest, and
`EffectEnforcementPass` reports the full chain — on a path that emits
working C#. Revisit trigger: #809 fixed → a cross-module W5-C variant may
be added.

**No paid feasibility dry-run — a variance argument replaces it, and that
is a priced decision.** D4.5's rule is that a [P] threshold must be shown
decidable before it freezes. W5-A/C catches are *deterministic build
failures* (the compiler either blocks or it doesn't — run-to-run agent
variance affects only whether the agent then fixes the defect, which is
why "absent at declared-done" is the measure and majority-across-runs the
aggregation). W5-B depends on the agent running the smoke tests at least
once — bounded nondeterminism, absorbed by 3 runs/arm/pair with
per-defect majority. With N = 9 defects the threshold below is decidable
at trivial spend; a dedicated variance epoch would cost more than the
probe itself. Registered in Annex A-1.2 with this argument.

**Probe scale and spend.** 9 pairs (one defect each) × 2 arms × 3 runs =
54 agent runs, iteration budget 10, pinned model per gates-doc
conventions. Estimated well under the gates-doc $1,500/epoch ceiling
(WS2-exit-scale runs suggest low hundreds). The concrete figure is
entered via the `phase-2-spend-authorisation.md` process and the epoch
**does not run until the user authorizes the spend** — same discipline as
`ws2-exit-e2e-001`.

**Box confirmation:** parent's 2–3 wk stands. The probe epoch itself is
days; the box is dominated by fixture authorship + acceptance checks.

## 3. Slicing (each PR merges green on its own)

1. **PR 1 (this PR):** kickoff record + gates-doc **Annex A-1.2** —
   additive registration of M-W1 and PP-W1's frozen threshold, the
   excluded-holes list, the #807 channel exclusion, and the
   feasibility-by-determinism argument. No allowlist entries (fixtures
   are `.calr`/`.cs` under `bench/`; harness work is bash/python).
2. **PR 2 — D5.1:** the 9 probe pairs under
   `bench/phase0-agent-native/pairs/W5-*` (pristine-plus-defect starter
   fixtures both arms, arm-shared smoke tests, per-defect held-out probe
   tests, `defect.json` manifest naming the class/covered path/probe
   test), plus harness support: per-test held-out outcome extraction (the
   current stream only counts failures) surfaced as
   `defects{injected,caught}` in `result.json`, and fixture acceptance
   checks (pre-agent: Calor arm blocks W5-A/C at build, W5-B smoke test
   throws the contract guard; C# arm builds green and smoke tests pass —
   i.e. the C# toolchain does not catch the defect *statically*, which is
   the asymmetry under test).
3. **D5.2 — probe epoch** (not a PR until results): run from a committed
   branch after PR 2 merges and the user authorizes spend; epoch record +
   VERDICT under `bench/phase0-agent-native/epochs/ws5-probe-001/`;
   PP-W1 adjudication happens at M5 as part of Call 2 (§6.2), not here —
   the epoch produces the measurement, the call consumes it.

## 4. Non-goals

- Organic-incidence claims (`real-scale-benchmark-design.md` territory).
- The delegate/dispatch holes — excluded and listed, not probed.
- Static-verify-based catching (excluded until #807; revisit trigger).
- Human-review detection (machine-zone §7's red-team gate measures
  spec-diff review as a human-review replacement on the dogfood module —
  different subject, corpus, and comparator; no registration overlap).
- PP-W1 adjudication inside M4b — measurement here, Call 2 at M5.
