# PP-W-rows — the A:81 dry run (`w-rows-dry-001` + `w-rows-dry-002`), read

**Date:** 2026-09-01 (the epoch collected 2026-08-28)
**Status:** the dry run's own report. **It adjudicates nothing.** A-1.12 gives the dry run two
jobs — SIZE N and report the per-run cost — and forbids it a verdict; `ppw-analyze.py --dry-run`
enforces that in code (no verdict, no ledger write).
**Artifacts:** `bench/phase0-agent-native/epochs/w-rows-dry-001/` (36 run directories) and
`w-rows-dry-002/` (18, completing the three pairs the first collection never reached);
regenerate either with
`python3 bench/phase0-agent-native/ppw-analyze.py bench/phase0-agent-native/epochs/<epoch> --dry-run`.

**Answer, in one line: N = 9 for 80 % power, and N = 9 does not fit the ceiling — the PP arms
UNDERPOWERED.** §4 below.

**What the dollars are.** Every figure in this note and in the analyzer's output is
`total_cost_usd` as Claude Code computes it: token counts priced at **list API rates**, which the
archive states in its own field (`modelUsage.<model>.costBasis: "list"`). These epochs ran through
the operator's Claude Code **subscription**, so no per-run charge occurred; the resource actually
consumed is the subscription's usage allowance — which is precisely what returned HTTP 429 and
truncated `w-rows-dry-001`. A-1.12's $150 ceiling is therefore a **pre-registered stopping rule
denominated in dollars**, not a bill. Its methodological job — stopping the experiment from being
enlarged until it yields the answer the workstream wants — is unaffected, and nothing here re-tunes
it. (`phase-2-spend-authorisation.md:17-25` carries both framings at once and should be corrected
when that document is next touched; it is signed pre-registration, so this note records the
correction rather than editing it.)

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

## 3. `w-rows-dry-002` — the three pairs the limit never reached

Collected 2026-09-01 on the same arms, same model pin, 3 runs per cell over **W-004, W-005,
W-006** — 18 runs, all valid, all built, both arms' canaries correct. `--pairs` narrows a dry
epoch only; it is refused outright for `--epoch w-rows-001`, whose denominator A-1.12 froze at six.

| Pair | class | arm A escapes | arm B escapes | shape realized (A / B) |
|---|---|---|---|---|
| W-004 counter-peek | **blind** | 0 / 3 | 0 / 3 | 2 / 3, 2 / 3 |
| W-005 pipeline-trace | warning-vs-error | 0 / 3 | 0 / 3 | 3 / 3, 3 / 3 |
| W-006 map-doubler | **blind** | 0 / 3 | 0 / 3 | 1 / 3, 0 / 3 |

**Leg A is zero on both arms, in every cell of both collections.** Across the two epochs that is
**28 valid runs and not one escape** — no effect-observing test carrying the silence signature has
failed on either compiler. Shape realization is better here than in the first collection (11 of 18
cells' runs against 2 of 10), so the zeros are not simply "the mechanism was never exercised":
W-004 and W-005 realized the registered shape in 5 of 6 and 6 of 6 runs and still produced nothing
to catch.

**Leg B: point 1.1175, one-sided 95 % lower bound 0.8634, median within-cell CV 0.2520** (cap
0.41). The point is **below** the registered 1.20 margin and the bound does not fire — on these
pairs there is no large loop tax either. Both legs read the same way: no measurable difference
between the arms.

## 4. The number the dry run exists to produce: **N = 9, and N = 9 does not fit**

Two blind pairs carried a delta (W-004, W-006), which meets the registered floor of two, so sizing
ran for the first time. Both deltas are 0.0, so the observed between-pair variance is 0.0 and its
upper 95 % bound is 0.0 — **the power curve below is computed under the most favourable variance
assumption available**, no between-pair spread at all, and it still lands where it lands.

Per-run cost, measured over `w-rows-dry-002`'s 18 paid runs: **$2.0278** — double A-1.12's
pre-registered $1.0048, and 43 % above `w-rows-dry-001`'s own $1.418. Prior spend counts **every**
sibling dry epoch (`$26.95` + `$36.50` + the pilot); pricing against only the epoch in hand would
have forgotten the first collection and over-stated the affordable N by a whole step.

| N | power | runs | ~USD (list-rate) | fits $150 |
|---|---|---|---|---|
| 2 | 0.432 | 62 | 116.17 | yes |
| **3** | **0.480** | **74** | **140.50** | **yes — the largest that fits** |
| 4 | 0.557 | 86 | 164.84 | no |
| 6 | 0.733 | 110 | 213.51 | no |
| **9** | **0.868** | **146** | **286.51** | **no — the smallest that reaches 0.80** |

**The registered consequence.** A-1.12: "if the dry run demands N > 9 for 80 % power at Δ = 0.5,
the PP registers its achievable power and arms UNDERPOWERED rather than overrunning." The demand
is N = 9 exactly, at nearly twice the ceiling; the largest affordable N is **3**, whose achievable
power is **0.48**. Either reading — N = 9 unaffordable, or N = 3 at 0.48 — arms **UNDERPOWERED**.
The analyzer sets `sizing.armsUnderpowered` and says so in its own words.

## 5. What this leaves open

The dry run has now done both of its jobs, and both answers are unfavourable to running the main
epoch as registered:

- **N = 9 for 80 % power; the ceiling affords 3.** Arming UNDERPOWERED is the pre-registered
  response, and it is available without spending anything further.
- **Zero escapes on both arms across 28 valid runs, with the shape realized in most of them.**
  The quantity leg A measures has not appeared at all, on either compiler — which is a fact about
  these six fixtures, not yet a fact about effect rows.
- **Leg B's point estimate is 1.1175, below the registered margin.** No large loop tax on this
  evidence.

None of this is a verdict, and none of it changes anything A-1.12 froze. What it changes is the
question in front of the maintainer: whether to spend the remaining allowance on a collection the
instrument has already priced at 0.48 power, or to adjudicate PP-W-rows on the registered
UNDERPOWERED route and name W2 in the release notes as roadmap cut line 1 requires.
