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
