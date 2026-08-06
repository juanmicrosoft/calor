# PP-W5 parity epoch — spend authorisation

**Authorised:** 2026-08-05 by the maintainer, in session ("go ahead and do PP-W5").
**Status:** recorded **before** the epoch runs. Nothing below re-derives a threshold.

## What is authorised

| | |
|---|---|
| Epoch | `w5-parity-001` |
| Shape | **4 pairs × 2 arms × 5 runs = 40 live agent runs** |
| Pairs | `N1-001-string-utils`, `N1-002-inventory`, `N1-003-csv-row`, `N1-005-order-pipeline` — the four registered N1 neutral pairs, frozen at A-1.4 tranche 1 |
| Control arm | the immutable **`v0.10.0`** release tag build (`e24a6832`) |
| Treatment arm | **`main` at epoch time** |
| Edit mechanism | **`raw` on BOTH arms** (see below — this is not the M5 default) |
| Model | pinned via `CLAUDE_MODEL`, recorded in `pins.json` |

**Projected spend: ~$33**, range **$15–$89**. Derived by summing realised
`total_cost_usd` over the 37 archived N1 runs of `m5-compare-001` — the same four pairs,
same arm type — giving mean $0.83/run (median $0.74, min $0.37, max $2.24) × 40 runs.
Costs are read from the agent's own accounting, never hand-priced from token counts.

## A defect in the apparatus, fixed before running

`run-m5-epoch.sh` hardcoded **arm A = `raw`, arm B = `mcp-file`**. For M5 that is correct —
the mechanism delta *is* what PP-L5 measures. For PP-W5 it would have been wrong: the
frozen row requires **`raw` both arms**, because PP-W5 asks whether the *toolchain* taxed
the loop. Running it on the M5 default would have confounded the toolchain delta with the
tooling delta and answered PP-L5's question with PP-W5's name on it.

The per-arm mechanism, label, role and epoch kind are now parameters, with defaults that
reproduce the frozen M5 shape exactly so `m5-compare-001` stays reproducible.

## What this epoch does and does not adjudicate

**Does:** whether the toolchain shipped between `v0.10.0` and now imposes a large token
tax on the agent loop, on neutral tasks. Both arms are Calor; the only difference is the
compiler build.

**Does not:** anything about Calor versus C#. PP-W5 is a **regression gate**, not a thesis
gate. It is unaffected by Call S retiring the real-scale venue, and unaffected by the
finding that the real-scale bundles' "Calor arm" contains no Calor — neither applies to
the hand-authored N1 pairs or to a same-language parity comparison.

**Attribution, per A-1.5.6 (registered results-blind):** the treatment now carries
**v0.11 + v0.12**, so this adjudicates a two-release delta, not a strictness-batch-only
one. The frozen row's sentence claiming otherwise is recorded stale. On a FAIL, the
isolation ladder is two-step — `v0.10.0 + WS-W2-only`, then `v0.10.0 + all-v0.11` — a §7
supersession filed as such, because a one-release recipe cannot attribute a two-release
delta. The 1.25 margin and its ~1.7% false-fail calibration are **not** re-derived; the
treatment simply carries more change, so a pass is correspondingly stronger.

**Worth stating plainly:** v0.12 added work that could *plausibly* tax this loop — type
checking now runs on every compile, and the D14/D3 demotions withdraw elision from a large
class of contracts, leaving more runtime checks in the emitted C#. If there is a tax, this
epoch should be able to see it, and that is the point of running it rather than assuming.

## The rule, transcribed not re-derived

PP-W5 **fails iff BOTH**: the one-sided 95% cluster-bootstrap **lower** bound of the median
paired per-pair output-tokens-to-green ratio (treatment/control) exceeds **1.0**, AND the
point estimate exceeds **1.25**.

Validity floor: a cell with < 2 valid runs drops its pair, disclosed; if fewer than 3 pairs
survive or either arm has < 12 valid runs, PP-W5 is **reported, not adjudicated**.

Power: 4 clusters × 5 runs bounds only large regressions — measured detection 0.33 / 0.62 /
0.87 at true 1.25× / 1.4× / 1.6×. A pass means *no large tax detected*, never *proven equal*.

The adjudicator (`bench/phase0-agent-native/w5-analyze.py`) was written **results-blind**,
before any run, and transcribes the frozen row. Note the direction differs from PP-L5:
that gate uses the upper bound to detect an improvement; this one uses the **lower** bound
to detect a tax.

## Pre-flight

A `--null-agent` pass (zero spend) runs first to verify plumbing. Epochs in this program
have been invalidated by plumbing defects before — a nondeterministic eligible set, an
arm that carried no Calor — and the cheap check is the one that catches them.


---

## Amendment, 2026-08-06 — realised spend exceeded the authorised range

**Cumulative realised: $92.83.** That is above this authorisation's stated ceiling of $89, and
is recorded here rather than left to be reconstructed from two epoch records.

| epoch | outcome | spend |
|---|---|---:|
| `w5-parity-001` (attempt 1) | **VOID** — both arms compiled through the same `Calor.Tasks` | $38.75 |
| `w5-parity-002` (attempt 2) | PASS | $54.08 |
| | **cumulative** | **$92.83** |

**Why the first attempt bought nothing.** `--calor-dll` pins the CLI, which the agent never
invokes; the compiler that builds the agent's code comes from the arm template's `__REPO_ROOT__`
via `--arm-repo-root`, which `run-m5-epoch.sh` never passed. The pre-flight I ran did not test
for it, and the CLI content-hash guard I had added certified a contrast that did not exist. That
$38.75 is a straightforward loss attributable to an unverified assumption in the apparatus.

**Why attempt 2 cost more than projected** ($54.08 against ~$39). The projection was derived from
the void epoch's own N1 costs — where both arms ran the *same* compiler. A genuine contrast costs
more. Deriving a projection from a run that turned out to be void is itself a lesson: the
projection inherited the defect.

**Guards added so the same loss cannot recur:** a parity epoch now hard-fails unless per-arm
repo roots are supplied, differ, and contain `Calor.Tasks.dll` binaries with different hashes;
every run stamps the root it built against; and the adjudicator refuses to adjudicate unless the
pins record two distinct roots and every run's provenance matches one of them.

No further spend is authorised under this record.
