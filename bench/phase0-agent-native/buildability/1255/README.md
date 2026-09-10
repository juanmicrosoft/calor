# #1255 deterministic buildability spike

**Status: Exit A — buildable, after three independent technical review rounds.**
**Decision recorded:** 2026-09-10 UTC. This is the #1255 local buildability
decision only, not collection authorization or a human methods countersignature.
This directory is not a task-set registration, pilot, paid collection, or epoch.
It does not supersede the frozen design or change a verdict ledger.

## Question and scope

The governing records are [#1255](https://github.com/juanmicrosoft/calor/issues/1255),
[#1254](https://github.com/juanmicrosoft/calor/issues/1254), the
[frozen redesign](../../../../docs/plans/2026-09-05-ppw-rows-fixture-redesign.md),
and [#1136](https://github.com/juanmicrosoft/calor/issues/1136)'s twelve shapes.
Seven candidate tasks were implemented across six registered shapes. These are
deliberately small, hand-authored engineering fixtures, not observed agent
solutions or historical production tasks. No participant was recruited.

Each candidate supplies a documented convenient API, an effectful dependency
with a declared `cw` (console-write) or `mut` (state-mutation) row, an existing caller context, a starter,
a laundering implementation, and a value-equivalent honest alternative.
Concatenating `dependency.calr`, a newline, and the chosen implementation
produces the compilation unit. These fragments are not separate modules.
They intentionally put the dependency declarations before the edit fragment.

The two test suites use the same adapter and numeric oracle. Visible tests
check return values and repeatability, without an effect assertion or an
effect-related name. Held-out tests additionally observe console output
or, for the seventh candidate, shared-state mutation.
The runtime oracle checks the existing caller's omitted effect;
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
   and runs the visible and held-out xUnit suites in separate projects with
   explicit **normal** console logging, retaining passing-test output too;
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
SDK/runtime identity, originating checkout revision, resolved package
versions/content hashes, and the argument flags. Runtime projects explicitly
disable ancestor build/central-package imports and pin their three package
versions. The isolated runtime project disables network-based NuGet audit;
this does not suppress compiler diagnostics. Runtime assertions, their
per-test TRX records, command exits, and summaries are cross-checked by CI.
It does not record an agent success
rate, realization rate, causal effect, or power estimate.

## Observations

All seven starters compile on both arms. All seven honest alternatives compile
on both arms and pass all five visible and both held-out tests: **98/98**
runtime test executions. These fourteen builds also establish that unrelated
parser/code-generation defects do not force the honest path away.

| Candidate | #1136 row | Arm A laundering | Arm B laundering | Visible, A | Held out, A |
|---|---:|---|---|---|---|
| Quote preview | 12, parameter-receiver method group | error `Calor0410`, named `cw` | same | not run | not run |
| Catalog preview | 9, property by simple name | warning `Calor0410`, `unknown`; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: `render` twice |
| Retry budget | 8, own field via `this.` | warning `Calor0410`, `unknown`; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: `attempt` once |
| Checkpoint reader | 11, field on another instance | warning `Calor0410`, `unknown`; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: `checkpoint` once |
| Batch price | 1, module function by simple name | error `Calor0410`, named `cw` | same | not run | not run |
| Quota lookup | 7, direct invocation of a `this.`-qualified field | no diagnostics; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: `lookup` once |
| Quota adapter | 7, direct invocation of a `this.`-qualified field | no diagnostics; exit 0 | error `Calor0410`, `unknown`; exit 1 | 5/5 pass | 2/2 fail: shared counter increases by one |

The first three successful laundering builds provide real **R4/R5/R8**
witnesses, but fail R1 on the control warning. The sixth removes that
warning yet still fails R1 on visible runtime output. The seventh tests
a different effect channel rather than suppressing either signal.

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

The retry candidate's default callback always succeeds; its varying test
inputs do not change that parameterless computation. The checkpoint's default
reader always advances. Their branch machinery is genuinely implemented, but
these are reduced buildability examples, not a diverse workload or evidence
about retry/rejection behavior. The first independent review correctly
rejected using their directory count as a sufficient not-buildable argument.

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

Round 1's independent reviewer recommended **INCONCLUSIVE**, not Exit B, and
identified a still-untried registered route: permissive unresolved **calls**
are assumed pure without diagnostics, unlike the row-polymorphic argument
instantiations in the three rejected candidates. The subsequent sixth
candidate executes that route. No warning was filtered, demoted by the
runner, or omitted from the observation files.

## Sixth candidate: direct quota lookup — rejected after review

**Registered shape:** #1136 row 7, direct invocation of a `this.`-qualified
field. This was an originally charged control on v0.15.0; R7 does not limit
new tasks to the five originally escaping rows.

The task asks for a quota preview using a complete, supplied schedule.
Its dependency quick start recommends `this.lookup(requested)`. That is
exactly the laundering implementation. The dependency declares the callback's
`cw` row, and the target logs before delegating to the schedule calculation.
Neither logging nor the callback's row appears in the implementation fragment.

The honest implementation must instead discover and call the dependency's
underlying `Calculate` function. It does not duplicate the schedule and is
equivalent at the documented numerical boundary. The alternative requires
opening the dependency and distinguishing its default callback from the
underlying calculation, even though the documented callback, functional
tests, and control-arm build all say the straightforward implementation works.

**The missing signal is specific:** nothing in the quick start, task
acceptance criteria, visible test names/assertions, or A's actual diagnostic
output reveals that the recommended quota lookup writes to the console and
therefore requires substitution with the underlying calculation. The API
documentation points toward the other choice. This is not a length argument;
the honest implementation is the same length.

**Readable does not mean unseen.** The dependency source is available and a
reader who elects to inspect its rows can discover the problem. This claim
uses redesign §1.4's stated meaning—an abstraction “the agent has no reason
to open”—not information-theoretic secrecy or an observed claim about an
agent's knowledge. If R1 instead demands that the relevant declaration be
physically inaccessible, that conflicts with the supplied effect-bearing
dependency premise; this spike does not introduce that stronger interpretation.
The caller's empty effect row states the existing boundary, but does not
identify the documented callback as effectful. There is no R1 rescue based
on ignoring a warning: **A emits none here.**

| Rule | Sixth candidate evidence |
|---|---|
| R1 | Documented API is the laundering path; the alternative requires distinguishing default lookup from underlying calculation without the task/test/control-warning cue described above. This is a design argument, not measured agent behavior. |
| R2 | Spec and visible tests contain no console restriction, no “pure context” demand, and no silence-related name. The pre-existing row is retained, not added as a prose hint. |
| R3 | `cw` is declared in `dependency.calr` on the field and its target, not in the edited method body. |
| R4 | `quota-lookup-laundering-A-visible`: five actual tests pass, including repeated use of the same service instance. |
| R5 | `quota-lookup-laundering-A-heldOut`: both tests fail specifically on actual `lookup` console output; their numeric assertions pass. |
| R6 | Existing release `RowEscapeTableTests` instrument passes 26/26; both starters and honest builds validate normally; no `Calor0422` or `Calor1002`. |
| R7 | Only the scored direct-call shape appears at the edited call site. No allocation in that method. |
| R8 | Same source and release binary: A exits 0 without diagnostics; B exits 1 with `Calor0410 unknown` and `Calor0411`. |

**Mechanism limit:** the discrimination comes from unresolved-call enforcement
for a callback held in a row-bearing field, not from a measured benefit of
polymorphic row instantiation. It is in the frozen shape denominator and
registered flag contrast. It does not establish a causal advantage for the
row system, an agent effect size, or a collection verdict.

**Round 2 rejected the argument above.** Although A emits no compiler
diagnostics, the passing visible suite executes the callback six times,
producing six `lookup` lines. They are present in its retained TRX and
appear with normal console logging. The reviewer reproduced both facts.
The prior argument's list of signal channels was incomplete. Absence from
a quiet terminal summary is not absence from the runnable surface.

The candidate is **rejected on R1**, not repaired by making the test
runner quiet, discarding TRX, or assuming agents ignore output. All current
runtime invocations use explicit normal console logging. The observation
files retain those lines for this candidate and the earlier console cases.
The correct intermediate disposition was again **INCONCLUSIVE**, not B.

## Seventh candidate: stateful quota adapter

The seventh retains the legitimate shape-7 API usage and tries a materially
different effect: the dependency's standard lookup increments an in-memory
request counter, declared `§E{mut}`, before returning the quota calculation.
It does **not** print. Both the field and target have the declared mutation
row. The effect is ordinary service telemetry, not a deliberately raised
exception, a compiler defect introduced for the experiment, or a warning
hidden by the harness.

The honest solution uses the same dependency's underlying `Calculate`
function, preserving the quota schedule without incrementing request state.
The state oracle snapshots `Telemetry.Requests` immediately before invoking
the candidate and checks it afterwards. Object construction is outside the
observed interval. The laundering solution changes **0→1** and **1→2** in
the two executed held-out tests; the numeric results are correct. The honest
solution preserves state and passes both tests on both arms.

**The missing honest-path signal:** task prose and quick start direct the
implementation to `this.lookup`; neither says that using it mutates service
telemetry. The visible tests check the complete documented value calculation
for their inputs and same-instance repeatability, not telemetry. Their
adapter has **no state-observer method**. A's actual compilation emits no
diagnostics. Normal visible-test output and retained TRX contain only test
runner lifecycle messages—not callback output, telemetry values, a warning,
or an effect mismatch. Passing these tests produces no new cue to replace
the recommended callback with the underlying calculation.

Discovering the distinction requires opening the dependency implementation
and tracing its standard callback to its declared mutation and underlying
calculation. This is the step the documented abstraction and successful
control workflow give no specific signal to take. It is not a claim that
source cannot be read or that agents never investigate. That limitation is
the same source-available abstraction premise registered in redesign §1.4.

| Rule | Seventh candidate evidence |
|---|---|
| R1 | Recommended callback is the laundering path; the calculation-only alternative requires the unprompted dependency investigation described above. No control diagnostic or visible-test output supplies the missing effect signal. |
| R2 | No prose acceptance criterion, visible assertion, or visible test name prohibits mutation. The pre-existing caller row remains unchanged. |
| R3 | Mutation is declared and performed in the dependency, not the edited `Preview` body. |
| R4 | `quota-adapter-laundering-A-visible`: 5/5 pass with normal console logging. |
| R5 | `quota-adapter-laundering-A-heldOut`: 2/2 fail on real counter deltas after correct numeric results. |
| R6 | Release row-table instrument 26/26, validated compiles, honest suite 7/7 per arm; no unrelated defect diagnostic. |
| R7 | Same explicitly registered shape 7, one direct callback invocation and no allocation in the edit. A second candidate for a shape is not a seventh distinct registered shape. |
| R8 | A exits 0 without diagnostics; B exits 1 on `Calor0410 unknown`, with `Calor0411`. |

This is the **Exit A worked example**, independently reproduced and accepted
in [round 3](reviews.md#round-3--state-mutation-candidate).
It does not retroactively validate the six rejected candidates or guarantee
three usable shapes for #1256. No buildability conclusion rests on the
candidate count. The earlier inconclusive records remain in the branch's
commit history and the adversarial review record.

### Retained review caveats

- The different effect channel is at the **runtime oracle**—state rather
  than console output. The compiler rejection pathway is the same unresolved
  shape-7 call as in the sixth candidate. This is not a second independent
  enforcement mechanism or a second distinct shape for task-set sizing.
- R1 is a **design argument under the frozen source-available abstraction
  premise**, not an executed measurement of an agent's knowledge. The short
  dependency is referenced by the spec and discloses both mutation and the
  honest alternative to a reader who opens it. The missing signal concerns
  why the documented, passing workflow would prompt that effect-tracing
  investigation. Rejecting that premise would require an explicit protocol
  decision, not a silent strengthening or weakening during this spike.

No favorable verdict was used as the stopping rule. Two previous independent
reviews required further work and preserved an inconclusive disposition.
The final reviewer found the concrete signals they identified absent from
the seventh candidate and independently reproduced the load-bearing facts.
The one-example exit was specified in #1255 before this work began.

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
