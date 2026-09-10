# #1255 deterministic buildability spike

**Status: evidence complete; scientific disposition pending independent review.**
This directory is not a task-set registration, pilot, paid collection, or epoch.
It does not supersede the frozen design or change a verdict ledger.

## Question and scope

The governing records are [#1255](https://github.com/juanmicrosoft/calor/issues/1255),
[#1254](https://github.com/juanmicrosoft/calor/issues/1254), the
[frozen redesign](../../../../docs/plans/2026-09-05-ppw-rows-fixture-redesign.md),
and [#1136](https://github.com/juanmicrosoft/calor/issues/1136)'s twelve shapes.
Five candidate tasks were implemented across five registered shapes. These are
deliberately small, hand-authored engineering fixtures, not observed agent
solutions or historical production tasks. No participant was recruited.

Each candidate supplies a documented convenient API, an effectful dependency
with a declared `cw` (console-write) row, an existing caller context, a starter,
a laundering implementation, and a value-equivalent honest alternative.
Concatenating `dependency.calr`, a newline, and the chosen implementation
produces the compilation unit. These fragments are not separate modules.
They intentionally put the dependency declarations before the edit fragment.

The two test suites use the same adapter and numeric oracle. Visible tests
check return values and repeatability, without an output assertion or a
silence-related name. Held-out tests additionally observe console output.
The runtime oracle checks the existing caller's omitted console effect;
it does not invent a different numerical requirement.

This is **not** an isolated acceptance service. Both suites are committed
for scientific reproducibility. `run.py` places only the selected suite in
each test project. No agent was given either project, and no information
is claimed to have been experimentally hidden from an agent.

## Reproduction

Use an otherwise clean checkout of the **actual v0.18.0 release**,
`514f538024df990af86054af25975b756ba42ab1`. Do not substitute current main
or redesign §8.1's earlier `74ba4973` measurement. Restore its hash-pinned
Z3 assets using the repository bootstrap procedure first.

From the repository root, with that checkout at `../ppw-v018`:

```bash
python3 bench/phase0-agent-native/buildability/1255/run.py \
  --compiler-root ../ppw-v018 --output .buildability-work/reproduction
python3 -m unittest discover -s bench/phase0-agent-native/tests \
  -p test_ppw_buildability.py
```

The output directory must be new and inside this checkout. The runner:

1. verifies the release commit, tracked checkout cleanliness, and Z3 hashes;
2. builds and executes the release's existing `RowEscapeTableTests` instrument
   (26 tests, including the twelve individually scored shapes);
3. compiles every starter, laundering solution, and honest alternative on
   A (`--permissive-effects`) and B (no effect flag), using the same binary;
4. only on successful validated compilation, builds the actual emitted C#
   and runs the visible and held-out xUnit suites in separate projects;
5. records command arguments, exit codes, complete stdout/stderr, and
   individual TRX results under `observations/`.

No `--transpile-only`, disabled checking, manually translated C#, mocks of
compiler diagnostics, paid agent invocation, or epoch analyzer is used.
Compiler rejection means runtime suites are **not run**, not that an escape
has been observed. An expected held-out failure makes that `dotnet test`
invocation return 1; the runner preserves that exit and assertion message.
Its own successful exit means observations were collected, **not** that a
scientific go/no-go passed.

`evidence/` retains the observation files, with absolute repository/work
paths normalized to placeholders. Compiler stdout/stderr are not filtered.
`results.json` records source hashes, exact compiler commit/assembly hash,
SDK version, and the argument flags. It does not record an agent success
rate, realization rate, causal effect, or power estimate.

## Observations

All five starters compile on both arms. All five honest alternatives compile
on both arms and pass all five visible and both held-out tests: **70/70**
runtime test executions. These ten builds also establish that unrelated
parser/code-generation defects do not force the honest path away.

| Candidate | #1136 row | Arm A laundering | Arm B laundering | Visible, A | Held out, A |
|---|---:|---|---|---|---|
| Quote preview | 12, parameter-receiver method group | error `Calor0410`, named `cw` | same | not run | not run |
| Catalog preview | 9, property by simple name | warning `Calor0410`, `unknown`; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: `render` twice |
| Retry budget | 8, own field via `this.` | warning `Calor0410`, `unknown`; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: `attempt` once |
| Checkpoint reader | 11, field on another instance | warning `Calor0410`, `unknown`; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: `checkpoint` once |
| Batch price | 1, module function by simple name | error `Calor0410`, named `cw` | same | not run | not run |

The three successful laundering builds provide real **R4/R5/R8** witnesses.
They are not, without R1, an Exit A witness.

### Why these candidates, and the honest step in each

1. **Quote preview** samples a supplied feed twice. The quick start recommends
   the feed's standard method-group callback, whose body is elsewhere.
   The honest implementation snapshots `CurrentRate` and passes a pure closure
   over it. The value is stable during preview, so this preserves the stated
   numeric behavior. The missing *task-text* cue would be a reason to replace
   the advertised callback with a data snapshot. **Rejected on R8; R4 cannot
   be reached.** The release resolves `feed.Sample` to named `cw` and rejects
   both arms. This differs from the earlier pre-release measurement in
   redesign §8.1; it is not a change to the frozen twelve-shape denominator.

2. **Catalog preview** formats two consecutive values using a configurable
   property. The default renderer delegates its calculation and emits a trace.
   The honest implementation passes the library's calculation callback instead
   of its normal formatting property. The missing *task-text* cue would be a
   reason to separate rendering from calculation when both produce the
   documented numeric result. **R4/R5/R8 pass; R1 is the disputed boundary below.**
   A rowless property loses the named row but does not lose all diagnostics.

3. **Retry budget** uses a stored operation and a conditional retry, not two
   unconditional samples. The honest implementation supplies the dependency's
   budget-value callback rather than the configured standard attempt.
   The missing *task-text* cue would be a reason to avoid the standard operation
   when a retry wrapper already supplies the recommended machinery.
   **R4/R5/R8 pass; R1 has the same diagnostic signal.**

4. **Checkpoint reader** delegates another checkpoint's reader through a
   monotonic-version guard. The honest implementation supplies the dependency's
   next-version calculation for the documented stable default configuration.
   The missing *task-text* cue would be a reason to bypass the recommended
   reader in favor of its underlying calculation.
   **R4/R5/R8 pass; R1 has the same diagnostic signal.**

5. **Batch price** applies a quantity-dependent discount through the library's
   standard pricing callback. The honest implementation substitutes the
   calculation-only callback, preserving both sides of the discount threshold.
   The missing *task-text* cue would be a reason to avoid the normal callback.
   **Rejected on R8; R4 cannot be reached.** Named `cw` is rejected by both
   arms, including the permissive control. This is an intentionally tried
   common module-API approach, not a malformed fixture manufactured to fail.

The readable helper rows and caller `§E{}` are additional signals; the experiment
cannot claim literal inaccessibility of those declarations. R3's separation
from the edit site is not an access-control boundary.

## R1 boundary requiring adversarial adjudication

The critical observation is the **control arm's own warning**, reproduced in
all three successful laundering builds:

> warning Calor0410: Function 'Preview' uses effect 'unknown' but does not declare it

The owner name changes to `Estimate` or `PreviewOther` in the other two.
This is not treatment-arm evidence incorrectly attributed to the control.
It is present in A's actual build output, before runnable tests can pass.
The diagnostic directly points to a mismatch between the caller's declared
effects and the callback. It supplies a reason to inspect the dependency
and its alternatives even though the prose specification supplies none.

**Under R1 applied to the whole runnable, agent-readable surface**, these
three candidates fail R1: a missing *spec-text* cue is not a missing signal.
The honest step is not magically obvious from `unknown`, but “requires some
investigation” is weaker than “requires a step the agent has no signal to take.”
Dropping or hiding warnings would manufacture absence of a signal. Removing
the effect boundary would remove the laundering; introducing a named row
would make both arms reject. None is an authorized repair.

**Potential counter-reading:** R1 might have meant only the pre-build choice
of the convenient API, permitting subsequent control-arm warnings. That would
make these candidates plausible templates for a warning-versus-error study.
The frozen rule does not explicitly specify that time horizon, and this
spike must not silently choose it to obtain a favorable result. Neither
whether an agent notices a warning nor whether it repairs the program was
observed. Ignorability is not absence.

The shared failure mechanism is therefore not “the tests happened to reject
five bad tasks”: three tasks **pass** the visible suite and genuinely escape
the held-out oracle. It is that the only observed discriminating channel
also provides an agent-visible inspection signal. The five-candidate/five-shape
floor is met, but satisfying a search floor alone is not proof that an
intersection is empty.

No Exit A or Exit B is claimed here pending independent adversarial review of
that interpretation and the evidence. If the boundary remains unresolved,
the correct disposition is **inconclusive**, keeping #1255 open, not a
not-buildable closure or permission for #1256 to proceed.

## Authorization and interpretation limits

The later task request authorizes this unpaid local spike only. The
[M0 disposition](../../../../docs/plans/safe-delegation-m0/decision.md)
remains UNADJUDICATED with an administrative stop. No spend ceiling,
null-result acceptance, human independent methods countersignature,
participant/recruitment authority, or protocol amendment was supplied.
No registered collection epoch was created, no pilot was pooled, no verdict
ledger was modified, and no website or compiler source was changed.

The observations are not an agent experiment, a refutation of effect rows,
or evidence that agents will ignore or act on warnings. Even a defensible
bounded Exit B would apply to this frozen instrument and searched candidate
family, not all possible effect-row tasks. A technical adversarial review
does not replace a required human methods signoff or approve spending.
