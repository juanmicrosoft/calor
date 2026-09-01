# PP-W-rows — the A:81 dry run (`w-rows-dry-001`), read

**Date:** 2026-09-01 (the epoch collected 2026-08-28)
**Status:** the dry run's own report. **It adjudicates nothing.** A-1.12 gives the dry run two
jobs — SIZE N and report the per-run cost — and forbids it a verdict; `ppw-analyze.py --dry-run`
enforces that in code (no verdict, no ledger write).
**Artifacts:** `bench/phase0-agent-native/epochs/w-rows-dry-001/` (36 run directories, 36
transcripts, `pins.json`); regenerate this note's numbers with
`python3 bench/phase0-agent-native/ppw-analyze.py bench/phase0-agent-native/epochs/w-rows-dry-001 --dry-run`.

---

## 0. What ran, and what stopped it

| | |
|---|---|
| Epoch | `w-rows-dry-001`, `kind: pp-w-rows`, `mode: live`, 3 runs per cell |
| Arm A (control) | `calor+v0.14.3-pre-rows` @ `283ec9f9` — v0.14.3 + the one `arm/v0.14.3-pre-rows` commit threading `--permissive-effects` through `Calor.Tasks` |
| Arm B (treatment) | `calor+v0.15.0` @ `3bb2601e` (the release tag) |
| Model pin | `claude-opus-4-8`, agent 2.1.248 |
| Planned | 6 pairs x 3 runs x 2 arms = 36 |
| **Billed** | **19** |

The epoch was **truncated by the account's weekly usage limit**, not by anything in the harness or
the fixtures. From `W-004-counter-peek/calor+v0.14.3-pre-rows/run-2` onward every run's
`agent.json` carries `api_error_status: 429`, `terminal_reason: "api_error"` and
`"result": "You've hit your weekly limit · resets Aug 31 at 7pm (America/New_York)"`, with
`total_cost_usd: 0`, `num_turns: 1`. Seventeen runs are archived `invalid` for that reason. The
completed cells are W-001, W-002, W-003 (both arms, 3 runs each) plus W-004 arm A run-1.

**Cost, measured for the first time.** $26.95 over the 19 runs that billed — **$1.418 a run**
against A-1.12's pre-registered **$1.0048** (the mean over e1-rows-parity-001's 40 runs). A-1.12
pinned the spend arithmetic on that estimate while saying in the same breath that "the per-run
cost is unmeasured until the dry run reports it"; it is now measured, it is **41 % higher**, and
`ppw-analyze.py` prices the affordable N on the measured figure when a dry epoch reports one
(`sizing.costBasis`). At $1.418 a run the frozen $150 ceiling, net of the ~$29 already spent,
affords **N <= 7** (6 pairs x 7 x 2 = 84 runs ~ $119).

## 1. Three instrument defects, all of them fatal to an adjudication, all fixed

The dry run's first job is to be read by the instrument, and on the first attempt it could not be.

1. **The pair-id spelling.** `run-ppw-epoch.sh` stamps `ppW.pairs` / `legBPairs` / `blindPairs`
   from `run-m5-epoch.sh`'s pair ids, which are the SHORT ones (`W-001`); `ppw-analyze.py` keyed
   every downstream lookup on the DIRECTORY (`W-001-middleware-stage`). Both spellings are
   registered at A-1.12 "so the two can never drift", and they had drifted: the analyzer read an
   empty denominator, dropped all six pairs, and reported the epoch as an **own goal** on a
   harness that was fine. Fixed by accepting either registered spelling and refusing anything that
   is neither — a wrong denominator is never guessed at.
2. **The canary verdict.** `run-pair.sh` archives `armCanary` as `permissive-ok` (control) or
   `strict-ok` (treatment) — deliberately different strings, because they prove opposite facts
   about the same laundering program. The analyzer required the literal `"ok"` and so rejected
   **every control-arm run** as an arm that does not honour `<CalorPermissiveEffects>`. Fixed, and
   the treatment arm is now checked too: an arm carrying the OTHER arm's verdict is an arm swap,
   which the one-sided check could not see.
3. **No harness commit in `pins.json`.** The `<CalorPermissiveEffects>` template and the pre-spend
   canary live in the harness, so an unrecorded harness checkout is a validity hole.
   `run-ppw-epoch.sh` now stamps `harnessCommit` and `harnessDirtyFiles`.

Also landed with them: the epoch runner records route (a)'s starter compiles
(`ppw-starter-compiles.json`) — an adjudication that never re-checked the unmutated starters
against their frozen multisets is not an adjudication.

None of these moves the bar. All three would have been discovered only after the paid epoch.

## 2. What the 19 valid runs say — evidence, not a verdict

**Leg A: zero escapes, on both arms, in every readable cell.**

| Pair | class | arm A escapes | arm B escapes | shape realized (A / B) |
|---|---|---|---|---|
| W-001 middleware-stage | **blind** | 0 / 3 | 0 / 3 | 1 / 3, 1 / 3 |
| W-002 map-and-report | warning-vs-error | 0 / 3 | 0 / 3 | 0 / 3, 0 / 3 |
| W-003 match-fallback | warning-vs-error | 0 / 3 | 0 / 3 | 0 / 3, 0 / 3 |
| W-004 counter-peek | **blind** | 0 / 1 | — (limit) | 1 / 1 |
| W-005, W-006 | — | — (limit) | — (limit) | — |

No run's failure was excluded by the silence-signature refinement
(`namedTestFailuresWithoutSilence` is empty everywhere), so the zero is a real zero on this
sample, not a scoring artifact.

**The shape is realized about a fifth of the time.** Two of the ten readable runs built the
registered shape. The agents mostly solved the task another way, and a cell that never exercised
the mechanism reads as a null delta rather than as evidence — which is exactly what the
shape-realized indicator exists to say. Worked example, W-002 arm A run-1: the agent routed
through `Map` with a **named** function (`§F{f006:DoubleReport} §E{cw}`), declared its caller
`§F{f007:MapAndReport} §E{alloc, mut, cw}` — honestly — and wrote `Total` as its own pure loop, so
the effect-observing test (`Total_IsSilent`) was silent because nothing was ever hidden. There was
no laundering to catch on either arm.

The consequence for power is arithmetic: at a ~20 % realization rate the effective per-cell sample
is ~0.2 N, so N = 7 (the ceiling's N) yields between one and two shape-realized runs per cell.

**Leg B: point 1.3474, one-sided 95 % lower bound 0.668, median within-cell CV 0.2753** (cap
0.41). The bound does not fire, so leg B does not fail on this sample; the point sits between the
1.30 and 1.35 sensitivity lines and above the registered 1.20 margin. Read as a direction, not a
result: three pairs, and the arm-B cells are the ones the limit did not reach.

**Sizing could not run.** Only one blind pair (W-001) carried a delta; the blind floor is two, and
"the floor of two is what carries the power". `sizing.recommendedN` is `null` with that cause
named.

**W1's per-turn capture works.** 36 of 36 runs archived `transcript.jsonl` — the first archive in
the epochs tree that does — and the W4 tool-class table computes over them:

| arm | runs | assistant messages | Read | Bash-build | Bash-other | Edit |
|---|---|---|---|---|---|---|
| calor+v0.14.3-pre-rows | 18 | 261 | 62 | 17 | 162 | 16 |
| calor+v0.15.0 | 18 | 218 | 59 | 19 | 143 | 17 |

Both columns include the truncated runs' single message each, so the arm totals are not a turn-gap
measurement; what they establish is that the instrument S1 could not build now has its input.

## 3. What this leaves open

The dry run did **not** produce the number it exists to produce (N), and it produced two facts
that bear on whether the main epoch is worth its ceiling: the per-run cost is 41 % above the
registered estimate, and the registered shape is realized in about a fifth of runs. Both are
recorded here; neither is a verdict, and neither changes anything A-1.12 froze.
