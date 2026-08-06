# w5-parity-001 — **VOID.** The contrast the epoch existed to measure was never applied.

**This epoch does not adjudicate PP-W5.** An earlier revision of this file reported
**PASS** at a median paired ratio of 1.109. That verdict is **withdrawn**. The numbers are
arithmetically correct and reproduce exactly; they are a measurement of nothing.

Kept in the record rather than deleted, because the defect is the finding.

## What went wrong

`--calor-dll` pins **only** the CLI/MCP/envelope build. The agent never invokes the `calor`
CLI — it is instructed to run `dotnet build`, which binds Calor.Tasks / Calor.Sdk /
Calor.Runtime **and the emitter** through the arm template's `__REPO_ROOT__`. That comes
from `run-pair.sh --arm-repo-root`, and **`run-m5-epoch.sh` never passed it.**

So both arms compiled `.calr` with the same main-era compiler. The v0.10.0 build was used
only for harness-internal telemetry the agent never sees.

**The proof is filesystem, not inference.** Neither arm worktree contains
`src/Calor.Tasks/bin` at all. Had `__REPO_ROOT__` pointed at the control worktree,
`UsingTask AssemblyFile=…/Calor.Tasks.dll` would have failed and **every control run would
have failed to build**. All 20 control runs were green with full held-out passes on
iteration 1. There is no reading of that other than: both arms ran the harness checkout's
compiler.

`run-pair.sh` documents this in its own comments — that `--calor-dll` alone is sufficient
*only* for M5, and that "a workstream that changed codegen/Calor.Tasks would instead
require a per-arm checkout". `run-guarantees-epoch.sh` does pass `--arm-repo-root`. This
epoch reused the M5 runner and inherited a pin documented as insufficient for exactly this
question. The delta suppressed is not small: `CSharpEmitter.cs` alone is +399 lines between
the two commits.

## Two claims in the withdrawn verdict that were also wrong on their own terms

1. **"Arms are keyed on the build each run used."** `result.json.calorDll` is the
   `--calor-dll` argument echoed back — what the harness was *told*, not what compiled the
   agent's code. It split a clean 20/20 and certified nothing. It was reported here as
   provenance; that was the assurance that let the real defect through.
2. **The attribution paragraph.** It claimed the D3/D12/D14 elision withdrawal predicted a
   mild positive ratio. **All four N1 fixtures contain zero contracts** — there is no
   elision to withdraw. Independently, compiling all 40 archived `final-src/*.calr` with
   both arm CLIs yields **byte-identical** C#, 40/40. A mechanism was invoked that could
   not operate on this population, and it happened to flatter the number.

## What the epoch did buy

The apparatus. Four defects, three found before the run and one after:

1. The two arms wrote to the same directory and the treatment silently destroyed the
   control — caught by the zero-spend pre-flight (4 result files where 8 were expected).
2. The M5 swap guard rejected a correct parity configuration.
3. The `raw` arm never invoked the agent at all on bash 3.2 (empty array under `set -u`) —
   a live attempt that collected 40 invalid cells for **$0.00**.
4. **This one** — the product was never bound per arm, and the CLI-hash guard certified a
   contrast that did not exist.

Only the fourth cost money: **$38.75**.

## What the record must not say

Not "the toolchain imposes no large tax". Not "~11%". Nothing about v0.10.0 versus main.
The instrument did not have the contrast in it.

## Correctness that survives, so the re-run inherits it rather than re-deriving it

Independently verified during review: the analyzer faithfully transcribes the frozen row
(5th-percentile **lower** bound, ratio treatment/control, failure a conjunction, floors
2/3/12, two-level cluster bootstrap matching `m5-analyze.py`); the reported numbers
reproduce bit-identically from the raw data; the bound is **not** seed-dependent (200
seeds → 0.911–0.935, none above 1.0); the spend accounting is exact; and `w5-analyze.py`
was committed 18 minutes **before** the epoch started, so results-blind timing holds.

Superseded by **`w5-parity-002`**, run with the product bound per arm and with a guard that
refuses to start unless the two arms' `Calor.Tasks.dll` differ.
