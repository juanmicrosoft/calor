# Roadmap — v0.18 "The Claim, Tested"

**Date:** 2026-09-03
**Status:** **Draft v4** — one self-conducted adversarial round (§10), five findings, two Major.
**M1 built and adjudicated: PP-S1(rows) and gate 14 = HIT, twelve of twelve** (§3.1 M1).
**M2's measure leg done: the kill rate is 14.7 %, the instrument built for it could never fire, and
the replacement caught a death on its first encounter — 139 MB free, swap full. Memory exhaustion is
measured, not hypothesised** (§3.1 M2). **Spend authorized 2026-09-03; the ceiling is still due with M3's sizing** (§8).
**No independent round has been run**; §10 registers the lenses that still must be applied.
**Written against:** `691b65ec` (main after PR #1155, the 0.17.0 version bump).
`Directory.Build.props:3` reads `0.17.0`; **v0.17.0 has been released and verified** — published
2026-09-03T02:22Z as the project's first non-prerelease, `Verify Release (installed tool)` green
on run `33709552892`.
**Governing inputs:** `roadmap-v0.17.md` (*R17:*), `roadmap-v0.16.md` (*R16:*),
`roadmap-v0.13-v0.15.md` (*R15:*), `2026-09-01-ppw-rows-dry-run.md` (*W:*),
`docs/design/effect-rows-in-the-type-system.md` (*D:*), `docs/plans/agent-native-gates.md` (*A:*),
issue #1136's frozen table, issue #1150's four data points, the four corpus ledgers under
`bench/phase0-agent-native/`, and the open issue list on 2026-09-03.

---

## 0. Where 0.17 left us

### 0.1 Shipped, and it shipped what it said

R1 (the binding-loss breakdown, schema-4 ledger), R2 (the Calor0411 count over the corpus —
8,231 occurrences across 187 of 324 modules, published with its own ceiling caveat), R3 (gate 6's
measurement correction 65.46 % → 92.79 %, *and* the movement leg to 95.91 %), R4(a)–(d) (#1128,
#1137, #1118, #1127) and S1 (#1136's fix set: `§PROP` carries a row, and an argument the pass
cannot name fails closed). Modules reaching the effect checker: **304 → 324**. Modules stopping at
name binding: **60 → 40**.

PP-R1 leg 1 was published as a **MISS** — 15 recovered against a pre-registered floor of 20 — with
the note that the release total of 20 was reached by other work and that a target met by something
other than the thing registered against it is not the target being met. That is the discipline
working, and this document is written to the same standard.

### 0.2 The finding: two registered gates were never evaluated

**R17:§5 gate 14 ("the row escape table") and PP-R1's sibling proof point PP-S1(rows) (R17:§4.2)
have no instrument and no published outcome.** (On the qualified name, see §4.1: `PP-S1` alone is
ambiguous in this repository's record.)

R17:§4.2 registers the instrument precisely: *a table-driven test over exactly the twelve
argument shapes in #1136's table*, five expected to move from escaping to charged-or-refused and
**seven controls that must not change**, with the outcome map HIT / MISS / NOT-ADJUDICATED. Gate 14
names the same test as its instrument, #1136's table as its denominator, and a revert-either-half
discrimination pin.

That test does not exist. What shipped is three hand-written cases in
`tests/Calor.Enforcement.Tests/StrictnessBatchTests.cs:2080, :2108, :2136` — the property row
parsed, the property row *resolved* rather than merely parsed, and an unnameable argument charged
Unknown. They pin the **mechanism**, and `PropertyRow_IsResolved_NotMerelyParsed` is a genuinely
good test. They are not the table. Three shapes verified is not twelve shapes verified, and the
seven controls are not covered at all.

The v0.17.0 release notes' `### Proof points and gates` reports PP-R1 leg 1, gate 6 and gate 9, and
is **silent on PP-S1(rows) and gate 14**.

Under R17's own outcome map this is **NOT-ADJUDICATED**. It leaves half of R17:§1's release claim
— *"every argument shape in #1136's table is either charged or refused"* — carrying no instrument,
after review round M3 promoted S1 from SHOULD to MUST *specifically so that claim would not rest on
a tier that could slip*. The tier held; the evidence did not follow it.

**Registered as this release's first MUST.** Note what it is not: not a suspicion that S1 is
broken. The mechanism tests pass and the fix is real. It is that the release published a claim
whose registered instrument was never built, which is the failure mode the gate discipline exists
to catch, and it was caught by a cleanup sweep rather than by any of five adversarial rounds.

### 0.3 The confound PP-W-rows was blocked on is gone

R17:§2 gave two reasons not to re-run PP-W-rows in 0.17. One was cost. The other was substantive:

> re-running it against a compiler that leaks five of twelve shapes would repeat the confound.

**S1 shipped.** Both halves of #1136's fix set are on main. The treatment arm can now plausibly be
the fail-closed compiler the design assumed — *plausibly*, because §0.2 is exactly the reason we
cannot yet assert it. **M1 is therefore a hard prerequisite of M4, not a parallel workstream:**
the twelve-shape table is the evidence that arm A is fail-closed, and running the epoch without it
would re-run the experiment against an unverified treatment arm, which is the confound again in a
new costume.

### 0.4 The `--permissive-effects` freeze end condition has now occurred, and nobody adjudicated it

R16:§6 froze the Calor0410 demotion under `--permissive-effects` **"through the 0.16.0 release
commit; re-adjudicated after"**. R17:§9 restated it as due *after* 0.16.0 ships and listed it "only
so it is not forgotten then."

**v0.16.0 was released 2026-09-01T22:53Z.** The end condition is met. The adjudication is due now,
and it was forgotten exactly as R17:§9 feared.

It is not housekeeping. `--permissive-effects` **is arm B** of PP-W-rows. An experiment contrasting
a fail-closed compiler with a permissive one cannot have an unadjudicated definition of its
permissive arm. This lands in M3's registration, before any collection.

### 0.5 CI is unreliable on the release-critical path

#1150: the `tests (compiler)` shard killed with `##[error]The runner has received a shutdown
signal` / **exit 143**, **four times in two days**, on four different trees, with **zero test
failures** in every log (7,633 / 7,040 / 6,204 passing result lines). Green on retry every time.
`publish-nuget.yml` gates `publish` on every test job, and the same shard exists there.

The shape rules out a product defect and rules out #965 (which is a *reproducible* kill of
`Calor.Performance.Tests`). The standing hypothesis is resource exhaustion: consistently 8–10½
minutes in, on the **only** shard that checks out `submodules: recursive` — `test.yml:398`, and
note that **#1150 and R17:§9.2 both cite `:375`**, a line reference that has already drifted once
in two days. M2's instrumentation should anchor on the matrix expression, not the line. That shard
adds ~500 MB and carries the memory-heavy corpus tests.

Four in two days on the publish path is not a flake to file under §6. **0.18 cannot be published
reliably until this is understood**, which is why M2 is a MUST and a release gate rather than a
SHOULD.

### 0.6 Housekeeping completed in the 0.17 sweep

Recorded so the next release does not re-derive it. #1128, #1137, #1118, #1127 closed against
the changelog lines that name them (PR #1154 listed them in prose without `Closes` keywords, so
GitHub never closed them). #1094 closed — PR #1119 implemented it and R17:§7 asked for it to close
with 0.16.0. #1136 **left open** with the §0.2 disposition. PR #1148 (auto-generated benchmark
results) **closed**: it replaced a 30-run statistical result over 8 metrics (`overallAdvantage`
1.32) with a **single run** over a different 11-metric set (1.19, `statisticalRunCount: 0`). The
two figures do not answer the same question, and merging it would have put an incomparable number
on the website beside a changelog publishing the other one. Generator defects filed as **#1157**.
The sweep also pruned 38 stale agent worktrees and a nested clone — recorded only because removing
those local refs is what made §10 finding 1's dangling `MeasuredCommit` stamps visible.

---

## 1. Theme — one claim, one blocker, and the honesty to tell them apart

Calor's central bet, stated in `D:` and carried since 0.15, is that **effect rows help agents write
code that does not launder effects**. Three releases have built the mechanism. **No release has
tested the claim.** The one experiment sized for it demanded N = 9 for 0.80 power and could afford
N = 3 at 0.48, so it was recorded UNDERPOWERED on a pre-registered off-ramp and `w-rows-001` was
never run (W:§4, §6). `legA` and `legB` are null in `effect-rows-benefit-ledger.json` and the claim
is **neither supported nor refuted**.

0.18 is the release that tests it, or says in writing why it still cannot.

Two halves, and they are one release because the second gates the first:

1. **Test the claim.** Adjudicate the twelve-shape table so the treatment arm is *demonstrably*
   fail-closed (M1); redesign the fixtures that W:§6 identified as the defect and register the
   redesign before collecting anything (M3); run the epoch if it is authorized and if M1 and M2
   permit it (M4).
2. **Fix the path the result has to travel.** A result nobody can publish is not a result. #1150
   kills the shard that gates publishing (M2).

**What this release will not do is manufacture a verdict.** M4 is conditional on three things that
are outside this document's power to grant: a spend authorization (§8), M1 returning HIT, and M2
green. If any fails, PP-W-rows is re-registered UNDERPOWERED-CARRIED with the reason named, and
0.18 ships M1, M2 and the SHOULD tier. **A release that reports "we did not run it, here is
exactly why" is a success of this plan, not a failure of it.**

---

## 2. What is deliberately *not* in this release

- **Setting Δ, N, or any threshold after seeing arm results.** A:90-92 — *"Threshold changes after
  seeing arm results are never a valid supersession."* M3's redesign registers its own effect size
  and re-derives N **before** collection. This is stated first because it is the rule this release
  is most exposed to breaking: the redesign's whole purpose is to create larger laundering
  pressure, and a larger expected Δ lowers the required N and therefore the cost. That reasoning is
  legitimate **only** in advance and in writing.
- **IL-derived rows.** R17:§3.4's trigger reads 0 over R2's enlarged denominator (304 → 324) and
  the Calor0411 count R2 added. Re-registered unchanged.
- **New language surface.** #943 (ref/out call syntax) is a language addition; it stays 0.18.x.
- **A second reach push.** 0.17 did reach. The remaining binding-loss groups are 17, 12 and 11
  modules — smaller than the 40-module group 0.17 attacked, and `ExternalBase` (S2) is the larger
  fish in that family anyway.

---

## 3. Ship — tiered

### 3.1 MUST

**M1 — PP-S1(rows) and gate 14 adjudicated. The instrument R17 registered and did not build.**

*Deliverable:* a table-driven test over **exactly the twelve shapes in #1136's table**, each named
individually, sourced from the fixtures already committed under
`bench/phase0-agent-native/pairs/W-00*/seeded/` with `ppw-seeded-compiles.json` as the frozen
before-state.

- Five shapes registered as escaping on v0.15.0 must now be **charged (`Calor0410`) or refused**:
  own `§FLD` via `this.`; **`§PROP` by simple name, no receiver**; own `§FLD` via a local alias of
  `this`; `§FLD` on another instance of the same class; instance method group with a parameter
  receiver.
- **Seven controls must not change.** A control that regressed is a **MISS**, not a footnote. This
  is the half most likely to be quietly dropped, because a passing five-of-five reads like success.
- The two **disclosure** shapes (inherited field unqualified; module-qualified module function from
  inside a class body) are reported, **never scored**. #1137 landed in 0.17, so the first may now be
  cleanly measurable end-to-end — check it and record the result, but it does **not** join the
  scored twelve. R17:§4.2 fixed the denominator at twelve precisely because an ambiguous
  denominator is the failure these gates exist to prevent, and "#1137 made a thirteenth measurable"
  is the most plausible route to widening it by accident.
- *Freeze:* #1136's table, which A-1.12 registered as a pre-registered confound. **This release may
  not edit it.** If the table is found wrong, that is a supersession requiring written defect
  analysis (A:90-92) — not an edit.
- *Commit order, because M1's adjudicator has a stake in its verdict* (round 1 finding 4): M1 = MISS
  blocks M4, and this release wants M4 to run. The twelve rows and their expected outcomes are
  transcribed from #1136's table and **committed before the test is first run**, in a commit that
  contains no observed results. The frozen table is the source of truth; the commit order is the
  evidence that it was.
- *Discrimination pin (gate 14):* revert S1(a) and the `§PROP` row goes red while the field shapes
  stay green; revert S1(b) and the alias / other-instance / method-group shapes go red. The two
  halves must fail **different rows**.
- *Outcome:* HIT (twelve of twelve) / MISS (any shape still silent, **or any control regressed**) /
  NOT-ADJUDICATED (a fixture or its frozen multiset is edited during the release). **Published in
  the 0.18 release notes either way**, and if MISS, published as PP-R1 leg 1 was.

*Cost:* a `dotnet test` run. Zero agent runs, zero usage allowance. Any reviewer can reproduce it
on a laptop.

#### Outcome, 2026-09-03: **HIT — twelve of twelve**

*Instrument:* `tests/Calor.Enforcement.Tests/RowEscapeTableTests.cs`.
*Record:* `bench/phase0-agent-native/row-escape-table-ledger.json`.
*Invocation:* `dotnet <calor.dll> -i <src> -o <out>`, no flags — the configuration #1136's table
was measured in, per `ppw-seeded-compiles.json`'s own `invocation` field.

| | pre-S1 (`0defc5dc`) | post-S1 (`1609b695`) |
|---|---|---|
| 7 controls | exit 1, `Calor0410` (shape 7 also `Calor0411`) | **unchanged** |
| 5 escapes | **exit 0, `Calor0425`** — nothing charged | **exit 1, `Calor0410`** |

**Gate 14 wants two different discriminations, and this instrument supplies one of them alone.**

*(1) Pre/post — satisfied here, with a real compiler instead of a synthetic revert.* The pre-S1
build at `0defc5dc` — the parent of S1's merge — **reproduces #1136's frozen table exactly**: the
same seven charged, including shape 7's `Calor0410`+`Calor0411` signature, and the same five at exit
0 with `Calor0425`. That is what establishes the twelve fixtures are not vacuous — they reproduce
the frozen before-state on an independently built compiler from before the fix.

*(2) The two halves of S1 — **not** satisfied by this file alone, and claiming otherwise would be an
overclaim.* Gate 14 also asks that reverting S1(a) turn the `§PROP` row red while the field shapes
stay green, and reverting S1(b) turn the alias / other-instance / method-group shapes red. **Shape 9
is a rowless `§PROP`** — faithful to the committed fixture and to how v0.15 measured it, since a row
on a `§PROP` was a parse error then — so it is closed by S1(b) fail-closed, and reverting S1(a)
would leave it green. S1(a) is discriminated instead by `StrictnessBatchTests.cs:2089` and `:2117`,
which write `§PROP{…} §E{cw}`. **Gate 14's two-halves pin is satisfied jointly by this table and
those two tests.**

Both commit stamps are **reachable on `main`**, applying #1159 rather than repeating it.

**One limitation, disclosed.** The committed test calls the in-process API
(`TestHarness.Compile`); this adjudication's ledger drives the **CLI**, which is what #1136's table
was measured with. They agree on all twelve — but the test in CI would **not** catch a CLI-only
regression. That is the gap #1116 names (gate 3's CLI-process leg, unbuilt), and it is recorded
rather than papered over.

**Three process findings, recorded because two of them nearly produced a false verdict** (full text
in the ledger's `processFindings`):

1. **The instrument was vacuous as first written.** The scored assertion was `!(no errors &&
   Calor0425)` — so a fixture with a typo produces a parse error, `HasErrors` goes true, and the row
   passes **green while measuring nothing**. Closed by also asserting no error below `Calor0300` and
   that `Calor0410` is present. The per-row expected verdicts did not change; the check got
   stricter, not looser.
2. **A stale Release binary produced a false MISS.** The first CLI run used a `bin/Release` artifact
   from Sep 1 18:17; S1 landed Sep 2 19:07. That pre-S1 binary reported five shapes still escaping —
   a MISS that would have blocked M4 for no reason. Caught by checking the artifact's timestamp
   against the fix's merge date.
3. **The first CLI invocation measured the CLI's own help text.** `calor build <file>` is not a
   subcommand; the run printed usage, and grepping that for `Calor` codes matched the option
   descriptions, producing an identical fake diagnostic set for all twelve rows.

**The name collision is worse than round 1 recorded** (§4.1). `agent-native-gates.md:340` registers
**PP-S1** as *"converter fidelity is movable"*, and A-1.7 adjudicated **that** PP-S1 = **MISS** with
the real-scale venue **RETIRED** (lines 1251, 1257). So grepping the repository's registration
record for `PP-S1` answers *MISS, venue retired* — the opposite of this result, for a different
proof point of the same name. PP-S1(rows) is **not** added to the A-annex: that annex governs
agent-native experiments with spend and power and its PP rows are byte-frozen by
`scripts/check-annex-freeze.py`, while this proof point runs in `dotnet test` with zero agent runs.

**M4's first condition is met.** The other two — the spend authorization (§8) and M2's floor —
are untouched by this result.

*A correction this MUST also owes.* v0.17.0's notes reported three gates and omitted these two.
0.18's notes state plainly that gate 14 and PP-S1(rows) went unevaluated in 0.17 and report them now. The
0.17 release is not re-cut; the record is corrected forward, the way R17 printed the old gate 6
figure beside the corrected one rather than replacing it.

**M2 — #1150: measure the exit-143 kill, then fix it. Release gate.**

*Sequence, and it is not optional:* **measure before fixing.** Four kills with zero test failures
support a hypothesis (resource exhaustion), not a diagnosis. #1150 names what to measure: runner
memory during the `tests (compiler)` job, and whether xUnit `maxParallelThreads` is the multiplier.

- *Instrument:* memory and process accounting captured on the `compiler` shard for every run over
  the release cycle, archived like any other harness capture.
- *Floor:* **zero exit-143 kills on `tests (compiler)` across the last 20 consecutive runs on
  main** before 0.18.0 is cut. Retries do not count as passes — a retry is the symptom.
- *Registered in advance:* if the measurement shows the cause is **not** resource exhaustion, the
  hypothesis is published as refuted and the floor still holds. The gate is on the kills stopping,
  not on the hypothesis being right.
- *Distinctness maintained:* #965 stays a separate issue. R17:§9.3 declined to file them together
  and this release does not overturn that on the strength of a shared exit code.

#### Measure leg, 2026-09-03: done. Record: `2026-09-03-issue-1150-kill-rate-measurement.md`

**The rate is 14.7 % — 10 kills in 68 compiler-shard attempts**, not the 4 in #1150's body nor the
7 in `test.yml`'s comment. The undercount is structural: the API's default job listing returns only
the **latest** attempt, and every one of these kills passes on retry, so six of the ten are
invisible unless you walk `run_attempt` and `/attempts/{n}/jobs`. Any future count taken from final
run state will undercount it the same way.

| date | attempts | kills | rate |
|---|---:|---:|---:|
| 08-28 | 6 | 0 | 0.0 % |
| 09-01 | 18 | 5 | 27.8 % |
| 09-02 | 31 | 5 | 16.1 % |
| 09-03 | 13 | 0 | 0.0 % |

**It is a bounded 27-hour episode** (09-01T18:50 → 09-02T21:51) with 6 clean attempts before and 13
after — **and 13 is not a fix.** At p = 0.147 the chance of 13 clean by luck is ≈ **0.13**. Gate
15's 20-run floor lands at ≈ 0.042, so the registered floor is about a 5 %-level check against the
measured rate: **well chosen, unchanged, and not discharged by this.** No candidate cause is named,
because none is supported — the episode spans `main` and five branches including a docs-only PR.

**The instrument built for this could not observe it.** #1153's probe carries `if: always()` and the
claim *"a reading taken at the moment of death"*. Exactly one kill occurred after it landed
(`33687411104`) and its log contains **no probe output at all**: a shutdown stops the runner, so no
later step runs. Six weeks of hypotheses, and the instrument had never once fired on the event —
the same shape as §0.2, one layer down.

*Fixed by* `scripts/runner-resource-sampler.sh`, started **inside** the test step so its readings
stream into the live log and survive the kill. 10-second interval, set by the measurement: the logs
show the job goes **silent** before it dies — 97.1 s, 29.1 s and ~0 s between last output and the
shutdown — so a sub-30-second sampler is needed to land a reading inside that silence. Carries a
`--self-test` for its `free -m` parsing, run in CI, because the sampler executes on Linux and is
edited on macOS.

**First readings, same day: the hypothesis is now supported, and it has a mechanism.** On the
sampler's own PR (run `33781423935`), both instrumented steps show a **single `dotnet` test-host
process growing monotonically to 9.6 GB** (compiler shard) and **8.7 GB** (coverage), with available
memory falling 14.9 GB → **4.7 GB** — under a third of the runner left, and still climbing when the
suite ended. **Swap was never touched: 0 MB of 3,071 MB on both.**

That is a leak-shaped curve, not a working set: memory is retained across the run rather than
released between test classes. And it explains the intermittency, which no earlier hypothesis did —
the job does not need more memory than exists; it runs **close to the edge** and finishes most of
the time, so whether it crosses depends on run-to-run variance. A 27-hour episode at 27.8 % and then
nothing is what a near-threshold system looks like when something nudges it.

*Still open, and deliberately not closed here:* **both sampled runs survived**, so this is the
trajectory of a job that finished, not of one that was killed — the next kill's log is what the
sampler was built for. And the mechanism of death is unshown: swap untouched plus a *shutdown
signal* rather than a kernel OOM notice points at the **host reclaiming the runner** rather than the
in-guest OOM killer, which is plausible and not demonstrated.

**The parallelism hypothesis is refuted, and published as such.** #1150's second measurable was
*"whether xUnit `maxParallelThreads` is the multiplier."*
`tests/Calor.Compiler.Tests/AssemblyInfo.cs:3` already carries
`[assembly: CollectionBehavior(DisableTestParallelization = true)]` — parallelization has been
**off the whole time**, the suite runs sequentially in one host, and that single host still reaches
9.6 GB. Median load over the 48 samples is **1.49**. Concurrency cannot be the multiplier because
there is no concurrency to multiply, and turning it down further cannot help. M2 registered in
advance that a refuted hypothesis is published as refuted; this is that.

Sequential execution makes the retention **more** interesting, not less: nothing holds memory
concurrently, so what accumulates is held across test classes by the one host.

**THE MOMENT OF DEATH, OBSERVED — memory exhaustion, measured.** The sampler caught a death on its
**first encounter with one** (run `33788456072`, `quality-ratchets` / `Collect component coverage`,
on this measurement's own PR). This reading has never existed for #1150:

```
18:13:57   memAvail 4951M   swap    0/3071   load 1.07
18:14:07   memAvail  535M   swap    0/3071   load 1.06   <- 4.4 GB in ONE 10 s interval
18:15:08   memAvail  383M   swap 1023/3071   load 1.85
18:15:48   memAvail  387M   swap 2968/3071   load 1.79
18:16:27   memAvail  139M   swap 3071/3071   load 6.07   <- swap FULL
18:16:33   ##[error]The operation was canceled.
```

**139 MB left of ~16 GB, swap completely full, dead six seconds later.** The hypothesis is no
longer plausible — it is measured.

*This corrects the paragraph above.* The "swap never touched" reading was drawn from two runs that
**survived**, and it was wrong to lean on: on a run that dies, swap fills completely first. The
escalation is ordinary and in-guest — memory fills, swap fills, death — not a host reclaiming a
healthy runner.

*It also explains the silence.* The historical kills are preceded by 29.1 s and 97.1 s of no output
and nothing could say why. The sampler is a `sleep 10` loop, and here its last two readings are
**39 seconds apart**: the machine was thrashing so hard a shell loop could not be scheduled. The
silence is not a hung test — it is thrashing on full swap, and its length measures the severity.

*Scope, precisely:* this death was on the coverage step and presented as `The operation was
canceled` rather than exit 143. Whether `tests (compiler)`'s exit-143 has an identical trajectory is
**strongly suggested and not yet caught** — same curve on both steps, same suite, same runner. The
sampler is now on both, so the next one says.

**FIX LEG, 2026-09-03: the memory is NATIVE, not managed — two decisive experiments.** The obvious
step was to cut the host's memory; it was not taken, because the leak **does not reproduce locally**
(macOS: peak 2,049 MB, 8 reclaim events, oscillating — CI: 9,579 MB, ~0 reclaim, monotonic). That is
a qualitative difference, and it rules out "a test class leaks".

*Which process:* the sampler now prints the largest process's argv. It is the **test host**
(`dotnet exec --runtimeconfig .../Calor.Compiler.Tests…`, 3.96 → 7.91 GB), not MSBuild or
VBCSCompiler.

*Round 1 — GC policy?* **No.** `GCConserveMemory=9` + `GCRetainVM=0` gave peak 11,954M against a
control 11,331M — marginally **worse**, not flatter.

*Round 2 — native, or a managed reference leak?* Round 1 could not tell these apart, since
`GCConserveMemory` cannot collect what is still rooted. `GCHeapHardLimit=0xC0000000` (3 GiB) does,
and the prediction was registered before the run: a reference leak must exceed the cap and throw
`OutOfMemoryException`; native memory never touches it. **The tests passed, zero OOM, and RSS
reached 8.94 GB — three times the cap.** The managed heap fits in 3 GiB. **The ~9 GB is native.**

*What that leaves:* the only large native component here is **Z3** (`Microsoft.Z3` over native
`libz3`), invisible to the GC and counted in RSS. It also explains the local non-repro that nothing
else did — `CLAUDE.md` records a **custom ARM64 Z3 build** on macOS against standard binaries on
Linux. **Not yet shown**, and deliberately not assumed: the contexts at
`ContractVerificationPass.cs:55` and `GuardDiscovery.cs:227` are already `using var`. Next
experiment: a CI run excluding the Z3-dependent classes.

*Status:* five hypotheses now discarded or refuted — quota, platform incident, parallelism, GC
policy, managed reference leak. The fix is **not** written and gate 15's floor is undischarged.

*Earlier framing, superseded:* it has a target — **the test host retains memory across the run**,
~9.6 GB of ~16 GB in the healthy case, exhausting the machine when a large allocation lands on top.
Candidates are ordinary: release compilations and Z3 contexts between test classes, split the shard,
or use more than one host so retention resets. **None attempted here** — measure, then fix, and
gate 15's floor is unchanged and undischarged. Gate 15 is unaffected: no
fix has been attempted, and its 20-run floor stands.

**M3 — The PP-W-rows fixture redesign, registered before any collection.**

W:§6 states the defect: *"six tasks that agents complete honestly cannot measure whether a compiler
stops dishonesty."* The evidence A:90-92 requires as a "documented empirical defect in the
measurement protocol itself" is on record — the ~20 % first-collection shape-realization rate and
**zero escapes on both arms across 28 valid runs**. The quantity leg A measures did not occur on
either compiler.

*Deliverable — a written supersession, merged before a single paid run:*

1. **Defect analysis** (A:90-92's hard requirement): why the six tasks cannot produce laundering
   pressure, argued from the 28-run zero and the realization rate, not from intuition.
2. **A task set that creates the pressure** — tasks where the *convenient* implementation is the
   laundering one, so an honest agent still reaches for it. Every task's registered shape drawn
   from #1136's twelve, so M1's table and the epoch's fixtures share a denominator.
3. **Arm definitions re-registered.** Arm A is the 0.18 compiler, and the §0.2 record is what
   licenses calling it fail-closed. **Arm B depends on §0.4's adjudication** and cannot be written
   until that is given.
4. **Δ, N and cost re-derived in advance**, with the power curve, and an explicit statement that
   the redesign's expected effect size was set from the task design and **not** from any arm data.
5. **A registered off-ramp**, as A-1.12 had: the condition under which 0.18 records
   UNDERPOWERED-CARRIED rather than overrunning the ceiling.

*Freeze:* on merge of the registration PR. After that, thresholds are immovable.

**M4 — Run `w-rows-001`. CONDITIONAL, on all three:**

1. **§8's spend authorization granted** — the maintainer's, in writing, against a stated ceiling.
   W:§4 sized the *old* design at $286.51 for N = 9 against a frozen $150 ceiling. M3 re-derives
   this; the old numbers are the floor of what to expect, not a quote.
2. **M1 returns HIT.** A MISS means the treatment arm still leaks and §0.3's confound is live.
3. **M2's floor met.** An epoch whose analysis cannot survive CI to publication is spend without a
   result.

If any is unmet, PP-W-rows is recorded **UNDERPOWERED-CARRIED** with the unmet condition named, the
ledger's `legA`/`legB` stay null, and 0.18 ships M1–M3 plus the SHOULD tier. **No dry-run numbers
are ever copied into the ledger's outcome fields** — the constraint W:§6 already enforces.

### 3.2 SHOULD

- **S1 — The four overdue maintainer adjudications** (§9). Async rows; the flake rate for
  #859/#884/#959/#948/#1135; #965's release-path observation; and §0.4's `--permissive-effects`
  demotion, whose end condition has now occurred. **§0.4 is a hard input to M3(3)** — if the SHOULD
  tier slips, M3 cannot complete, so this one item is promoted to MUST-by-dependency and named as
  such rather than left to be discovered late.
- **S2 — `ExternalBase`: 61 of 113 Calor0425 diagnostics (54 %), re-measured at the 0.17 ledger.**
  An override or interface implementation reaching an external base. Carried **unassigned** through
  0.15, 0.16 and 0.17 (R17:§6 registered a venue for it in the 0.17 notes; that venue was not
  taken). It remains the single largest identified group in the effect-row diagnostic surface, and
  it is not the IL-rows class, so IL rows' trigger will never cover it.

  **The figure R17 carried is stale, and it moved in the direction that matters.** R17:§0.4 and §6
  both say *53 of 90 (59 %)*. Re-measured against `calor0425-corpus-ledger.json` (schema 4) as
  committed at **`1609b695`** — the squash commit that carries the 0.17 measurement onto `main`,
  cited in place of the ledger's own `MeasuredCommit` stamp, which is **dangling** (round 1
  finding 1, #1159). Cross-checked by its `Calor0411Sites` summing to the 8,231 over 187 modules the
  release notes publish: **61 of 113 = 54 %**, per subject MediatR 0/3, serilog 20/39,
  FluentValidation 41/71.

  So the **absolute count rose, 53 → 61, while the fraction fell, 59 % → 54 %** — because 0.17's
  reach work enlarged the denominator (304 → 324 modules enforced) faster than it touched this
  group. Reporting the fall alone would read as progress against a group nothing was done to.
  *Deliverable:* the per-subject breakdown R17:§6 asked for and never got, and a fix or a
  registered trigger — **not a fourth carry**.
- **S3 — Measurement provenance (#1159) and benchmark integrity (#1157), and one honest re-run.**

  **#1159 first, because it is the cheaper and the more load-bearing.** Every ledger under
  `bench/phase0-agent-native/` stamps a `MeasuredCommit`, every roadmap since 0.15 cites those
  stamps as the provenance of its published numbers, and **none of them resolves on `main`** — the
  repo squash-merges, so a stamp written on a branch names a commit the squash discards. Three of
  five name commits contained by **zero** remote branches: they exist only as unreferenced objects
  in individual clones and will not survive `git gc`, so a fresh clone cannot resolve them at all.
  The numbers are unaffected and reconcile with the release notes; what is gone is a third party's
  ability to check them, which is the entire purpose of the stamp. *Deliverable:* stamp something
  that survives squash-merge (the merge commit written back, as #1030 already registers for
  `roundtrip-baseline.json`; or the release tag), plus **a test that fails when a stamp does not
  resolve on `main`** — cheap, and it would have caught this the first time.

  Then benchmark integrity: Fix the generator (`metricCount`
  carrying the program count; the silent 30-run → single-run methodology swap), then re-run the
  **30-run statistical** suite at the 0.18 commit so the website and the changelog publish the same
  measurement. v0.17.0 disclosed that its numbers were carried forward from `82a7c653` and do not
  exercise effect rows at all; that disclosure is honest but it should not be needed twice.
- **S4 — R17's slipped SHOULD tier**, re-tiered unchanged: `FunctionBoundType.Row` end-to-end plus
  lambda-parameter rows (R15:747; D:2700-2708), index parity with `calor build`, and
  Calor0422/0423.

### 3.3 DEFERRED — residual carried with its trigger

IL-derived rows for BCL-returned delegates (*trigger:* `UnknownSource + InvocationUndetermined` > 10
over the 324-module enforced set — 0 today); Calor0419 at BCL argument sites (*trigger:* D-A
`calor0419FunctionTyped` > 10 — 2 today); IL-keyed resolver keys (*trigger:* gate 6 moves **and**
the string-key fraction stays above half); `PreconditionSuggester` on the typed CFG with #909;
reflection / `DynamicInvoke` / `dynamic`; `+=`; `§DEL` type parameters (D:1143); rank-2; E9 (no
design); converter-emitted rows (D:1780-1783); the `ρ_body` under-approximation of an escaping
lambda; gate 3's CLI-process and `Calor.Sdk` legs (#1116); gate 5 leg (b) (R15:996).

---

## 4. Proof points

### 4.1 PP-S1(rows) (re-run) — "no argument shape launders silently"

**Named `PP-S1(rows)` throughout, because `PP-S1` is ambiguous in this repository's record**
(round 1 finding 2). `substrate-plan-v0.12.md` uses **PP-S1** for a different proof point —
*"converter fidelity is movable"* — and adjudicates it with its own hit/miss table and decision
matrix (§167, §197-200). **The A-annex carries it too**, and that is the sharper problem:
`agent-native-gates.md:340` registers `PP-S1` under that meaning, and A-1.7 adjudicated it
**MISS**, with the real-scale venue **RETIRED** (lines 1251, 1257). The annex is the repository's
registration record — so grepping it for `PP-S1` returns *MISS, venue retired*, which is the
**opposite** of the rows proof point's actual outcome (§3.1 M1: HIT). Given §0.2 is a gap that survived five rounds, a naming
collision that manufactures false evidence of adjudication is not cosmetic. The v0.12 name is
frozen prose in a closed plan and is **not** renamed retrospectively; this release disambiguates on
its own side and carries the qualified name into the release notes.

*Claim:* every one of the twelve argument shapes in #1136's table is charged (`Calor0410`) or
refused, and none produces an uncharged `warning Calor0425` at exit 0.
*Instrument, denominator, freeze, discrimination, outcome map:* §3.1 M1, unchanged from R17:§4.2.
*Why it is re-registered rather than inherited:* R17 registered it and shipped without it. A proof
point carried forward silently is how it was missed the first time.
***Outcome, 2026-09-03: HIT — twelve of twelve.*** Seven controls unchanged, five escapes now
charged, zero still silent. Full record and the discrimination pin in §3.1 M1.

### 4.2 PP-W-rows (redesigned) — CONDITIONAL

*Claim:* with rows, fail-closed, agents launder fewer effects on callback-heavy code, at no large
loop tax.
*Status at the opening of 0.18:* **neither supported nor refuted.** `epochRun: false`,
`verdict: "UNDERPOWERED"`, `legA`/`legB` null.
*Instrument:* M3's registered redesign. *Adjudication:* only if M4's three conditions are met.
*Registered in advance:* a null result from an adequately powered run is a **refutation published
as such**, not a reason to redesign again. The redesign licence is spent once, on W:§6's documented
defect; a second one would need its own defect analysis and would be indistinguishable from
fishing.

---

## 5. Release gates

**Carried from 0.17, restated:** 1 (laundering, six closed classes), 2 (higher-order demand ledger,
floor 25), 3 (surface agreement — CLI-process and `Calor.Sdk` legs still unbuilt, #1116; gate 3
claims only the legs it has), 4 (PP-E1 regression pin), 5 (corpus compatibility, leg (a) only),
6 (resolution floor — regression leg ≥ 95.91 % aggregate with no subject below its own 0.17 figure:
MediatR 98.67, Serilog 97.35, FluentValidation 95.05; the movement leg was SATISFIED in 0.17 and is
not re-armed), 7 (index/query goldens), 8 (harness capture), 9 (conversion denominator:
`ExcludedParseFailed` = 0 and `ModulesEnforced` ≥ **324** aggregate — **re-set on 0.17's result**,
with per-subject floors re-derived at the 0.18 branch cut), 11 (non-convergence coverage, Calor0406
at both caps), 12 (turn attribution over every archived epoch).

**Gate 10 (PP-W-rows)** remains closed as UNDERPOWERED and does **not** gate 0.18. It re-arms only
if M4 runs. The ledger is a regression pin — its bytes must not change without a registered cause.

**Gate 13 (binding-loss breakdown)** carried: the breakdown sums to `ExcludedBindFailed` per
subject. A cause that does not add up is a red gate.

**Gate 14 (the row escape table)** — **carried and armed for the first time.** Registered in 0.17,
never evaluated. Instrument: M1's test. Denominator: twelve shapes. Freeze: #1136's table.

**New:**

15. **Release-path stability.** *Instrument:* the CI run log for `tests (compiler)` on main.
    *Floor:* **zero exit-143 kills across 20 consecutive runs**, counting **only runs after M2's fix
    lands** — a window that straddles the fix measures two different systems.
    *Short window:* if fewer than 20 post-fix runs accumulate before the cut, the gate reads
    **NOT-ADJUDICATED and blocks the cut**. It may not pass on an empty or partial window; a gate
    that passes for lack of data is the failure §0.2 is about.
    *Pin:* retries are not passes; a kill followed by a green retry is a **failed** gate.
16. **Benchmark methodology agreement.** *Instrument:* `website/public/data/benchmark-results.json`
    against `CHANGELOG.md`. *Floor:* the published `overallAdvantage` is computed from the same
    `statisticalRunCount` and metric set in both places, or the difference is stated in both.
    *Pin:* the #1148 diff — a single-run figure replacing a 30-run one under the same key — must
    make this gate red.

---

## 6. Carried debt

| Item | Source | Trigger | Venue |
|---|---|---|---|
| **Gate 14 / PP-S1(rows) unevaluated in 0.17** | §0.2 | — | **0.18 M1, unconditional** |
| **`--permissive-effects` Calor0410 demotion unadjudicated; end condition met 2026-09-01** | R16:§6; §0.4 | **fired** | **0.18 S1, and a hard input to M3(3)** |
| **`ExternalBase` — 61 of 113 Calor0425 (54 %)**, re-measured; R17 carries a stale 53 of 90 (59 %) | R17:§0.4, §6; `calor0425-corpus-ledger.json` | carried unassigned through three releases | **0.18 S2 — fix or registered trigger, not a fourth carry** |
| **exit-143 kills on the publish path** | #1150 (4 data points) | **fired** | **0.18 M2 + gate 15** |
| #965 perf suite kills the runner; runs only on the release path | #965 | recurrence | 0.18 S1 observation; kept distinct from #1150 |
| Flake cluster #948, #959, #884, #859, #1135 | R15:1040 | rate attached — **overdue since the 0.16 branch cut** | 0.18 S1 |
| Async rows | D:§11, 1922-1945 | maintainer, in writing — **overdue since the 0.16 branch cut** | 0.18 S1 |
| **Ledger `MeasuredCommit` stamps unresolvable on main; 3 of 5 on no remote at all** | #1159; round 1 finding 1 | **fired** | **0.18 S3** — a stamp test, and a stamp that survives squash-merge |
| Benchmark generator defects; methodology swap | #1157 | — | 0.18 S3 + gate 16 |
| `FunctionBoundType.Row` end-to-end; lambda params in-lambda → Calor0411 | R15:747; D:2700-2708; e4:255-259 | — | 0.18 S4 |
| Index parity with `calor build`; interface members unindexed; index-build cost | e5:256-275 | — | 0.18 S4 |
| Calor0422/0423 | N:S2.2 | — | 0.18 S4 |
| Gate 3 CLI-process and `Calor.Sdk` legs | #1116 | — | 0.18.x; gate 3 claims built legs only |
| Gate 5 leg (b) | R15:996 | — | 0.18.x |
| `§FLD`/`§B` rows not index positions; hover declared-only | e5:168-175 | — | 0.18.x |
| Solution-level manifests not consulted by the index | e5:256-258 | a corpus solution with manifests | 0.18.x |
| `ρ_body` under-approximation on an escaping lambda | e4:230-234 | a fixture measured silent | DEFERRED |
| #901, #929, #943, #906 | issue list | — | 0.18.x; #943 is a language addition |
| #1139, #1132, #1121, #1131, #1134, #1142, #1143, #1144, #1115 | issue list | — | 0.18.x housekeeping |
| 3.5.1 null-state slice; #845; #970 tri-state; TIER1A not-run | R15:1040 | maintainer | 0.18.x instrument debt |

---

## 7. Backlog disposition — open issues on 2026-09-03

| Issue | Disposition |
|---|---|
| #1136 | **0.18 M1** — open by design; §0.2's comment records what shipped and what closes it |
| #1150 | **0.18 M2**, release-gating (gate 15) |
| #1157 | **0.18 S3** (gate 16) |
| #1159 | **0.18 S3** — measurement provenance; related to #1030, same class |
| #965 | 0.18 S1 observation; kept distinct from #1150 |
| #948, #959, #884, #859, #1135 | 0.18 S1 — flake rate, **overdue since the 0.16 branch cut** |
| #1116 | 0.18.x; gate 3 claims only its built legs |
| #1082, #875 | demand-driven; 0.17 shipped R3's movement leg against them |
| #1084, #847, #922, #845 | demand-driven |
| #909 | 0.18.x with the `PreconditionSuggester` residual |
| #943, #929, #906, #901 | 0.18.x |
| #1139, #1132, #1121, #1131, #1134, #1142, #1143, #1144, #1115 | 0.18.x housekeeping |
| #1011 (R1–R14), #1030, #1031, #1032, #1042 | continuous |
| #673, #709, #711 | adoption work, not release-gated |
| #1128, #1137, #1118, #1127, #1094 | **closed** in the 0.17 sweep (§0.6) |

---

## 8. Spend authorization — required before M4, and only M4

M1, M2, M3 and the entire SHOULD tier cost **zero agent runs**. They are `dotnet test`, CI
instrumentation, a written registration, and compiler work.

**M4 alone needs money.** W:§4's sizing of the *old* design: $2.0278 per run measured over
`w-rows-dry-002`'s 18 paid runs (double A-1.12's pre-registered $1.0048), N = 9 for 0.868 power at
146 runs ≈ **$286.51**, against a frozen **$150** ceiling whose largest affordable N is 3 at **0.48**
power. M3 re-derives all of it for the redesigned task set.

What is needed, in writing, before any paid run:

1. A ceiling. If it stays $150, M3 must reach adequate power **within** it or arm its off-ramp —
   and the honest reading is that the redesign has to raise Δ for that to be possible, which it may
   legitimately aim for **only in advance**.
2. Acceptance that a properly powered null result **is the answer** and gets published as one.

**Not granted by this document.** Recorded here so a later release cannot mistake the plan for the
authorization — the mistake §0.4 shows this project already makes with adjudications it defers.

### Authorization — GRANTED 2026-09-03

The maintainer authorized the spend on 2026-09-03, in session. Recorded here as the durable
record this section exists to hold.

**Still outstanding, and needed before M4 runs, not before M3 is written:**

1. **A ceiling.** The authorization did not name one. W:§4's $150 was frozen against the *old*
   fixture set; M3 re-derives cost and power for the redesigned tasks, so the number to authorize
   against does not exist yet. **The ceiling is due with M3's sizing block**, before any paid run,
   and M3(5)'s off-ramp is written against it.
2. **The null-result acceptance** (condition 2 above) — that an adequately powered null result is
   the answer and gets published as one. Not separately confirmed.

**M4 remains blocked on its other two conditions.** M1 is met (§3.1 M1: HIT). M2's floor is not:
gate 15 requires 20 consecutive clean post-fix runs and §3.1 M2's measurement puts the current
evidence at 13 clean attempts, ≈0.13 under the measured episode rate. Authorizing the spend does
not shorten that.

---

## 9. Maintainer adjudications now due

1. **Async rows** (D:§11, 1922-1945) — the three-clause test, due in writing at the 0.16 branch
   cut. **Overdue by two releases.** DEFERRED is a placeholder, not an adjudication.
2. **The flake rate** for #859/#884/#959/#948/#1135, due at the 0.16 branch cut. #1150 now supplies
   four data points for one member. R17:§9.2's argument stands: nobody was counting until a release
   forced it.
3. **#965** — whether the perf suite killed the runner on a release path. 0.16.0 and 0.17.0 have
   both now shipped, so the observation window R17 was waiting on has closed twice.
4. **`--permissive-effects` Calor0410 demotion** (§0.4) — **newly due**; the end condition fired on
   2026-09-01. It defines arm B and blocks M3(3).

---

## 10. Adversarial review

R17's five rounds did not catch §0.2 — a post-release cleanup sweep did. That is this document's
most important prior: **the rounds reviewed the plan, then the code, then the release notes, and
none of them asked whether the instruments the plan registered actually existed.**

### Round 1 — 2026-09-03, self-conducted, on Draft v1

Five findings. Two are Major and both were found by *checking a citation instead of trusting it*,
which is the cheapest lens available and the one Draft v1 had not applied to itself.

| # | Lens | Finding | Disposition |
|---|---|---|---|
| **1** | provenance | **Major. Every ledger's `MeasuredCommit` is unresolvable, and three of five name commits on no remote at all.** Draft v1 cited `MeasuredCommit: 4767668a` as the corpus ledger's provenance without checking it. It is **not on `main`** and is contained by **zero** remote branches — it survives only as an unreferenced object in individual clones and will not survive `git gc`. Same for `fdff11de` (resolver-key ledger). The repo squash-merges, so a stamp written on a branch names a commit the squash discards. Worse, the field chases its own tail: at `4767668a` the file reads `MeasuredCommit: 0484426f`, and on `main` the same file reads `4767668a` — the **only** byte that differs between the two versions. Every roadmap since 0.15 cites these stamps as the provenance of its published numbers. | **Applied.** Filed as **#1159**; added to §3.2 S3 and §6. §3.2 S2's citation now names the *resolvable* squash commit `1609b695` and states the stamp is dangling rather than repeating it as fact. **The numbers are unaffected** — the ledger's `Calor0411Sites` sums to 8,231 over 187 modules, exactly the release notes' figure, and `ModulesEnforced` to 324. What is broken is third-party verifiability, which is the whole purpose of the stamp. |
| **2** | naming | **Major. `PP-S1` names two different proof points, and the collision points straight at §0.2's failure mode.** `substrate-plan-v0.12.md` uses **PP-S1** for *"converter fidelity is movable"* (§167, §197-200), adjudicated with a hit/miss table and a decision matrix. R17:§4.2 uses **PP-S1** for *"no argument shape launders silently."* A reader — or a future adversarial round — grepping `PP-S1` for evidence of adjudication finds a rich adjudication history belonging to a **different proof point**. That is a live route to concluding §0.2's gap was already closed. | **Applied.** This document uses **PP-S1(rows)** at every occurrence, and §4.1 states the collision. Registered for the release notes too. Renaming the v0.12 proof point retrospectively is *not* proposed: it is frozen prose in a closed plan. |
| **3** | gating | **Gate 15's 20-run floor can be vacuous or unreachable, and M2 contradicts it.** M2 says *measure before fixing*; gate 15 demands 20 consecutive clean runs before the cut. If the fix lands late, 20 runs cannot accumulate; if `main` sees fewer than 20 runs in the cycle, the gate passes on an empty window. Neither is a gate. | **Applied.** Gate 15 restated in §5: the 20-run window counts **only runs after the fix lands**, and if fewer than 20 accumulate the gate reads **NOT-ADJUDICATED and blocks the cut** — it may not pass on a short window. |
| **4** | incentive | **M1's adjudicator has a stake in its verdict.** M1 = MISS blocks M4, and the same release wants M4 to run. Nothing in Draft v1 stopped the twelve rows from being written *after* observing which shapes pass. | **Applied.** §3.1 M1 now requires the twelve rows be transcribed from #1136's table and **committed before the test is first run**, in a separate commit from any expectation values. The frozen table is the source; the commit order is the evidence. |
| **5** | scope | §0.6 records disk reclamation (60 GB → 4.9 GB), which is a local environment fact, not repo state, and does not belong in a roadmap's findings. | **Partially applied.** Kept, trimmed to one clause, because the worktree prune is what surfaced finding 1 — the commits' unreachability became visible only once the local refs were gone. Recorded as the provenance of the finding, not as an accomplishment. |

### What round 1 did not do

It was **self-conducted**, which R16 round 2 established is structurally unable to find half-applied
dispositions of its own findings. It did not review any code (there is none yet). It did not
independently recompute §3.2 S2's 61/113 — that figure was computed once, from the ledger, by the
same pass that wrote it, and its cross-check (`Calor0411Sites` = 8,231) confirms the *ledger* is the
0.17 measurement, **not** that 61/113 was summed correctly. An independent round should re-sum it.

### Rounds required before this draft is acted on

- **Instrument-existence lens (new, and it is the one §0.2 demands).** For every proof point and
  gate in §4 and §5: does the test exist, and does the release-notes template have a line for its
  outcome? Registration is not implementation.
- **Code lens** against `main...HEAD`, not the plan. R17 round 4 found seven defects this way that
  three plan reviews could not.
- **Test-lens.** Does a test observe every claim §3 makes?
- **Conditionality lens.** M4 has three conditions. Trace each: is any of them satisfiable by this
  document's own authors, and would a reader mistake the plan for the authorization? §8 exists
  because §0.4 shows that mistake has already been made once.
- **Denominator lens.** M1 fixes twelve. #1137 landing may make a thirteenth measurable. Confirm
  nothing widens the scored set.

Known unattacked at Draft v1: whether M3's redesign can raise Δ enough to fit any plausible ceiling
(if not, M4 is unreachable and this release's theme rests on M1 and M2 alone — which the plan
should say out loud rather than discover in November); whether gate 15's 20-run floor is
achievable inside one release cycle; and whether S2's `ExternalBase` breakdown is a release's work
or a paragraph.

---

## 11. Cut lines

Registered in advance, in priority order, so scope is shed by rule rather than by whatever is
easiest to drop late:

1. **M4 falls first.** It is already conditional. UNDERPOWERED-CARRIED with the reason named.
2. **S4 falls second** — R17's slipped SHOULD tier, slipping a second time. If it slips, the
   release notes say so; two silent slips is how a residual becomes permanent.
3. **S2 does not fall.** A fourth unassigned carry of 59 % of the Calor0425 surface is the thing
   §6 exists to prevent. If it cannot be fixed, it gets a **registered trigger**, which is the
   minimum §6 accepts.
4. **M1 and M2 never fall.** M1 is a claim already published without evidence; M2 is the path the
   release travels. If either cannot be done, 0.18 does not ship and the reason is published.
