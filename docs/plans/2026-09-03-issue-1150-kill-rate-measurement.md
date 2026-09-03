# #1150 — the exit-143 kill rate, measured

**Date:** 2026-09-03
**Deliverable:** `docs/plans/roadmap-v0.18.md` §3.1 M2, the *measure* leg. M2's sequence is
**measure before fixing**, and this is the measurement. No fix to the kill itself is proposed here.
**Method:** every `tests (compiler)` job attempt in the last 60 `test.yml` runs, read **per
attempt** via `actions/runs/{id}/attempts/{n}/jobs`, classified by conclusion; the three
retrievable failure logs grepped for the kill signature.

---

## 1. The rate is 14.7 %, and the issue undercounts it by more than half

| date | compiler-shard attempts | exit-143 kills | rate |
|---|---:|---:|---:|
| 2026-08-28 | 6 | 0 | 0.0 % |
| 2026-09-01 | 18 | 5 | 27.8 % |
| 2026-09-02 | 31 | 5 | 16.1 % |
| 2026-09-03 | 13 | 0 | 0.0 % |
| **total** | **68** | **10** | **14.7 %** |

#1150's body records **four** kills; `test.yml:423`'s comment records **seven**. The measured
number over this window is **ten**.

**The undercount is structural, not sloppiness, and it will recur for anyone who counts this way.**
The GitHub API's default job listing (`/runs/{id}/jobs`) returns only the **latest** attempt. A kill
that passes on retry is therefore *invisible* in the run's final state — and every one of these
kills passes on retry, which is the whole reason they read as a nuisance rather than a defect. Six
of the ten are hidden that way. They surface only by enumerating `run_attempt` and walking
`/attempts/{n}/jobs`.

Every one of the three logs still retrievable carries the same signature: `The runner has received
a shutdown signal`, `exit code 143`, and **zero** `Failed!` lines.

## 2. It is a bounded episode, not a standing condition — but that is not yet a fix

All ten kills fall inside a **27-hour window**: 2026-09-01T18:50 → 2026-09-02T21:51. There are
**6 clean attempts before it** (08-28) and **13 clean attempts after it** (09-03), including the
two `tests (compiler)` runs on PR #1160 at 8m24s and 10m27s.

**Thirteen clean attempts is not evidence that it is fixed.** At the episode rate p = 0.147, the
chance of seeing zero kills in 13 attempts by luck alone is 0.853¹³ ≈ **0.13** — one in eight. That
is not a result.

It is also the number that shows gate 15's floor was chosen well: at 20 consecutive clean runs,
0.853²⁰ ≈ **0.042**. So the registered floor is roughly a 5 %-level check against the measured
episode rate, and 13 runs is not it. **Gate 15 stands unchanged, and this measurement does not
discharge it.**

Nothing in this window identifies what started the episode or what ended it. No candidate change is
proposed, because none is supported: the episode spans commits on `main` and on five different
branches, including a **docs-only** PR, and the same tree passed on retry every time.

## 3. The instrument built for this could not observe it

#1153 added a `Runner resource probe (#1150)` step carrying `if: always()`, with the comment *"a
reading taken at the moment of death. `if: always()` for exactly that."*

**It cannot take that reading.** Exactly one kill happened after that probe landed (#1153 merged
2026-09-02T17:51; the kill is run `33687411104`, 2026-09-02T21:51). Its log contains **no probe
output at all** — not the step header, not `--- memory ---`. When the runner service is stopping it
does not go on to run further steps, so `always()` buys nothing against *this* failure mode. It
remains useful for ordinary failures, and is retained for those.

So the honest state of the measure leg before today: **six weeks of hypotheses, and an instrument
that has never once fired on the event.**

### What the logs do show

The job goes **silent before it dies**. Interval between the last line of output and the shutdown
error, across the three retrievable kills:

| run | gap | passing lines before the kill | last line before the silence |
|---|---:|---:|---|
| `33566680210` | **97.1 s** | 5,032 | a Z3 solver parameter dump (`well_sorted_check (bool) …`) |
| `33687411104` | **29.1 s** | 6,682 | an ordinary `Passed …` line |
| `33566737503` | ~0 s | 6,257 | — |

Three different termination points, three different test counts, no failing test in any log. That
remains consistent with resource exhaustion and is **not** proof of it — a stalled or reclaimed
runner produces the same silence.

## 4. What changed here

`scripts/runner-resource-sampler.sh`, started **inside** the test step and killed with it, printing
one compact line every 10 s:

```
[#1150 probe HH:MM:SS] memAvail=…M memUsed=…M swap=…/…M load=… diskFree=…M top=[proc:…M …]
```

Streamed to the step's own stdout, so the readings are in the captured log **up to the instant of
death** — which is the one property `if: always()` does not have. Wired into both steps the kill has
hit: `tests (compiler)`'s `Run project tests` and the `tests` matrix's `Collect component coverage`.

The 10-second interval is set by §3's table, not by taste: a 29-second silence needs a sub-30-second
sampler to land a reading inside it.

`--self-test` validates the `free -m` column parsing against canned fixture output and runs in CI
beside the other gate self-tests. The sampler executes on Linux but is edited on macOS, where its
Linux branch never runs — without the self-test, a typo in the `awk` ships as a probe that runs,
prints `memAvail=M`, and records nothing. That is the same silent-instrument failure as §3, and it
does not get to happen twice.

## 5. What this does and does not settle

**Settled:** the rate (14.7 %, 10 of 68), that the issue and the workflow comment both undercount
it, why any count taken from final run state will keep undercounting it, that the kills are
episode-bounded, that zero test failures accompany every kill, and that the existing probe cannot
observe the event.

**Not settled:** the cause. Resource exhaustion remains the standing hypothesis and remains
unmeasured, because until this change nothing was measuring it at the moment it mattered.

**Registered in advance, per M2:** if the sampler's readings show the cause is **not** resource
exhaustion, that hypothesis is published as **refuted** and gate 15's floor still holds. The gate is
on the kills stopping, not on the hypothesis being right.

**Kept distinct:** #965 is a *reproducible* kill of `Calor.Performance.Tests`. This is intermittent,
on a different shard, and passes on retry. R17:§9.3 declined to file them together and this
measurement does not overturn that — a shared exit code is not a shared cause.

## 6. Reproducing this

```bash
gh run list --workflow=test.yml --limit 60 --json databaseId,conclusion,headBranch,createdAt
# then, per run, walk EVERY attempt — the default /jobs endpoint hides retried kills:
gh api "repos/juanmicrosoft/calor/actions/runs/<id>"                       # -> run_attempt
gh api "repos/juanmicrosoft/calor/actions/runs/<id>/attempts/<n>/jobs"     # -> per-attempt conclusion
```

---

## 7. First readings from the sampler — the hypothesis is now supported, with a mechanism

The sampler ran on its own PR (run `33781423935`), on both instrumented steps. It works: 48 and 74
readings streamed into the live logs.

| | `tests (compiler)` | `release-quality` coverage |
|---|---|---|
| samples | 48 | 74 |
| `memAvail` start → min | 14,905M → **4,658M** | 13,626M → **4,754M** |
| headroom left at the end | **29 %** of ~15,989M | **30 %** of ~15,989M |
| largest process RSS | 148M → **9,579M** | 1,071M → **8,669M** |
| top-RSS growth monotone | 39 of 47 intervals | 64 of 73 intervals |
| **swap used** | **0M** of 3,071M | **0M** of 3,071M |

Trajectory on the compiler shard, one line per minute:

```
54:59  avail 14905M  used  1084M   top provjobd:148M
56:00  avail 13534M  used  2455M   top VBCSCompiler:1164M
57:00  avail 11228M  used  4760M   top dotnet:2011M
58:00  avail  9067M  used  6922M   top dotnet:4857M
59:00  avail  7054M  used  8935M   top dotnet:6827M
01:00  avail  5738M  used 10250M   top dotnet:8378M
02:51  avail  4658M  used 11331M   top dotnet:9579M   <- still climbing at the end
```

**What this establishes.** A *single* `dotnet` test-host process accumulates **~9.6 GB** over
roughly eight minutes and never plateaus. That is a leak-shaped curve, not a steady-state working
set: memory is retained across the run rather than released between test classes. Available memory
falls by **10.2 GB**, leaving under a third of the runner. The same shape appears independently on
the coverage step, which runs five projects sequentially and reaches 8.7 GB.

**Why this explains the intermittency**, which no previous hypothesis did. The job does not fail
because it needs more memory than exists — it finishes, most of the time, with a couple of gigabytes
to spare. It runs *close to the edge*, and whether it crosses depends on run-to-run variance:
runner model, background load, how far the curve gets before the suite ends. A 27-hour episode at
27.8 % and then nothing is exactly what a near-threshold system looks like when something nudges it.

**Two things this does NOT establish, stated because the temptation is to stop here.**

1. **Both sampled runs survived.** This is the trajectory of a job that *finished*, not of one that
   was killed. It shows the shard runs near the edge; it does not show the far side. The next kill's
   log will, and that is what the sampler was built for.
2. **The mechanism of death is still open.** **Swap was never touched — 0M of 3,071M on both
   steps** — and the message is `The runner has received a shutdown signal`, not a kernel OOM
   notice. So the plausible reading is the *host* reclaiming the runner under memory pressure rather
   than the Linux OOM killer firing inside it. Plausible, not shown.

**Gate 15 is unaffected.** Its floor is 20 consecutive clean post-fix runs, and no fix has been
attempted — this is still the measure leg. What has changed is that the fix, when it comes, has a
target: **the test host's retained memory across the compiler suite**, not a guess about runner
quotas or platform incidents.

## 8. The parallelism hypothesis is refuted

#1150 names two things worth measuring: runner memory, and *"whether xUnit parallelism
(`maxParallelThreads`) is the multiplier."* The second is answered, and the answer is no.

`tests/Calor.Compiler.Tests/AssemblyInfo.cs:3` already carries

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

**Test parallelization on this assembly has been off the whole time.** The suite runs sequentially,
in one test host, and that single host still climbs to 9.6 GB. Concurrency is not the multiplier
because there is no concurrency to multiply.

The load average agrees: over the 48 samples, median **1.49**, max 6.35 (the max during the build
phase, not the test phase). A 4-core runner executing tests in parallel would sit far higher for
far longer.

**Registered consequence.** M2 committed in advance that a refuted hypothesis gets published as
refuted. This one is: **parallelism is not the cause, and turning it down further cannot help,
because it is already off.** `Calor.Performance.Tests` carries the same attribute — relevant to
#965, which is a separate issue and stays separate.

That leaves the finding in §7 as the live one: a single sequential test host retaining ~9.6 GB
across the run. Sequential execution makes the retention *more* interesting, not less — nothing is
holding memory concurrently, so what accumulates is being held across test classes by the one host.

---

## 9. The moment of death, observed — memory exhaustion, measured

The sampler caught a death on its **first encounter with one**: run `33788456072`,
`quality-ratchets`, step `Collect component coverage`, on this measurement's own PR. This reading
has never existed for #1150 before.

```
time      memAvail   memUsed   swapUsed   load
18:13:57     4951M    11037M     0/3071   1.07
18:14:07      535M    15452M     0/3071   1.06   <- 4.4 GB consumed in ONE 10 s interval
18:14:27      507M    15481M    31/3071   1.12   <- swap starts
18:15:08      383M    15605M  1023/3071   1.85
18:15:38      517M    15471M  1485/3071   1.58
18:15:48      387M    15600M  2968/3071   1.79
18:16:27      139M    15849M  3071/3071   6.07   <- swap FULL, load spiking
18:16:33  ##[error]The operation was canceled.
```

**The hypothesis is no longer plausible; it is measured.** Available memory reaches **139 MB** of
~16 GB, **swap fills completely** (3,071 of 3,071 MB), load spikes to 6.07, and the job dies six
seconds later.

### This corrects §7

§7 reported *"swap was never touched — 0M of 3,071M on both steps"* and read that as evidence the
host reclaims the runner rather than the guest OOM killer firing. That inference was drawn from two
runs that **survived**, and it was wrong to lean on. On a run that dies, swap fills completely
first. The escalation is ordinary and in-guest: **memory fills → swap fills → death.**

### It also explains the silence before the kill

§3 recorded that historical kills are preceded by 29.1 s and 97.1 s of no output, and could not say
why. This run answers it. The sampler is a `sleep 10` loop, and its last two readings are **39
seconds apart** — 18:15:48 then 18:16:27. The machine was thrashing so hard that a shell loop could
not be scheduled on time.

So the "silence" in the historical logs is not a hung test and not a stalled runner. **It is the
system thrashing on a full swap.** The gap length is a *measure of the thrashing*, which makes the
97-second case the most severe of the three.

### Scope, stated precisely

This death was on `quality-ratchets` / `Collect component coverage` and presented as
`The operation was canceled` rather than `shutdown signal` / exit 143. That is the step
`test.yml`'s own comment already identified as where the kill lands. Whether the `tests (compiler)`
exit-143 has an identical trajectory is **strongly suggested and not yet caught** — same memory
curve on both steps in §7, same suite, same runner size — but the compiler shard has not itself been
sampled at the moment of death. The sampler is now in place on both, so the next one will say.

### Where this leaves the fix leg

Cause: **the test host retains memory across the run**, reaching ~9.6 GB on a ~16 GB runner in the
healthy case and exhausting the machine when a large allocation lands on top of it. Three earlier
hypotheses are discarded or refuted — quotas, platform incidents, and parallelism (§8).

The fix must reduce retained memory, and the candidates are ordinary: release compilations and Z3
contexts between test classes; split the compiler shard; or run the suite in more than one host so
retention resets. **None is attempted here.** M2's sequence is measure, then fix, and gate 15's
20-consecutive-clean-run floor is unchanged and undischarged.
