# Redesigned PP-W instrument (#1264)

This is unpaid instrument engineering, **not collection authorization, a task
freeze, or a new scientific registration**. The frozen design is
[`2026-09-05-ppw-rows-fixture-redesign.md`](../../docs/plans/2026-09-05-ppw-rows-fixture-redesign.md).
No real epoch or registration pins are created by this change.

## Historical compatibility and explicit supersession

The A-1.12 analyzer, its arithmetic, twelve starter slots, pair counts, old
two-compiler identities, archived epochs and benefit ledger are unchanged.
The old runner is retained as `run-ppw-legacy-epoch.sh` for historical tests.
Its `--confirm-paid-epoch` option is not authorization to collect new data.
`run-ppw-epoch.sh` now selects the redesigned instrument.

The new format is **schemaVersion 2**, kind `pp-w-rows-redesign`. It cannot
enter the old analyzer, even with `--dry-run`, or replace the old benefit
ledger. This is a versioned instrument supersession, not a rewrite of the
claims in A-1.12. The original registration tests remain intact.

**#1271 is not closed by this change.** Actual new starter git-blob hashes,
task counts and identities must be recorded in a separately reviewed
supersession after #1256/#1266/#1257/#1258 freeze the task set. The synthetic
test fixture's strings and counts are not those pins.

The #1271 mechanism checks an explicit `supersededPins` table against the
preserved historical ledger: registration reference, starter freeze commit,
both old arm objects, old pair counts and all twelve starter git-blob slots.
It never edits that ledger or its original tests. `replacementPins` separately
records the shared `compilerCommit`, `policies` (`A: ["--permissive-effects"]`,
`B: []`), `pairCounts` and `starterBlobs`. Counts come from frozen task manifests,
not inherited A-1.12 defaults. Each manifest explicitly states `class` (`blind`
or `warning-vs-error`) and Boolean `legB`. Those metadata do not add an estimand
or decide any new stage's sample size.

Each replacement starter slot records `task`, `arm` (`A`/`B`), task-root-relative
`path`, and Git `blobSha`. The required order is registered task order, then A/B,
then sorted `.calr`/`.calr.inc` paths. SHA-256 still covers the entire task inventory;
git-blob pins provide the separate starter-registration identity. Missing,
edited, duplicated or mismatched slots fail admission and analysis.
**No actual replacement table is supplied here.** The only new tables are
deterministic synthetic test inputs, clearly marked not-a-registration.

## Single epoch and stage

The stage-record entry point takes exactly one epoch id:

```bash
python3 bench/phase0-agent-native/ppw-analyze.py \
  --epoch-id <registered-pilot-id> --stage pilot --epochs-root <archive-root>
```

This writes `<archive-root>/<id>/ppw-stage-ledger.json`, without `--dry-run`.
It is append-only: an existing record is never overwritten. A confirmatory
invocation must name `--stage confirmatory` and that stage's distinct,
registered id. The analyzer does not enumerate sibling epochs, accept a
second epoch, or read caller-selected task/scoring files.
Repeated `--epoch-id` or `--stage` arguments are rejected rather than applying
argparse's usual last-value-wins behavior.

The stage ledger records `epoch`, `stage`, `dataKind`, `epochRun`, the shared
compiler, both policies, every cell's counts and descriptive rates, corrected
output tokens, and `verdict: null`. **Neither stage currently adjudicates a
confirmatory verdict.** Pilot estimand aggregation/precision and stage-2
Δ/N/rules still require their scientific registrations (#1261/#1262).
No A-1.12 Δ, $150 ceiling, N, power model or off-ramp calculation is inherited.
The frozen redesign's off-ramps remain obligations, not new decisions made
by this engineering report.

For each cell, invalid runs are disclosed separately; valid runs form the
escape and shape-realization denominators. Non-building declared-done runs
remain in that denominator and contribute no escape. A named held-out
failure counts as an escape only when its failure detail contains the
task's frozen `tests.effectFailureSignature` (starting `HELDOUT_EFFECT:`).
Value-only failures are reported separately. If a held-out project cannot
execute its named tests (including missing members or skipped observers),
the slot remains visible in `unscorableHeldoutRuns`; that cell's escape rate
is null rather than treating an unknown outcome as zero or dropping it.
The legacy silence-signature
scorer is unchanged. Pilot rates are not silently aggregated into a new
estimand or compared against a newly invented threshold.

## Collection format

```text
<epoch-id>/
  pins.json
  registration.json
  tasks/<task-id>/
    pair.json
    spec.md
    starter-a/                 identical source bytes to starter-b/
    starter-b/
    seeded/clean-a/            shape indicator positive control
    seeded/clean-b/
    smoke/                    visible suite; SmokeShim.calor.cs under shims/
    tests/                    held-out suite; TestShim.calor.cs under shims/
  runs/<task-id>/
    calor-permissive/run-1/    result.json, transcript.jsonl, final-src/, etc.
    calor-strict/run-1/
  ppw-stage-ledger.json        only after completed collection and validation
```

`pins.json` has one **compiler** object: release, commit, repository root,
DLL path, DLL SHA-256, Tasks SHA-256, and the compiler hash observed through
both product canaries. No `armA`/`armB` compiler overrides are admitted.
`arms.A` is `calor-permissive`, policy `permissive`, flags
`["--permissive-effects"]`; `arms.B` is `calor-strict`, policy `strict`, flags
`[]`. Both use raw editing and the same template/product/task bytes.
The neutral labels describe policy, not a retired compiler version.

Each task's `arms` uses those same two keys, with `fixture: starter-a` or
`starter-b`. Both configs retain effect enforcement, debug contracts and Z3.
Only A has `permissiveEffects: true, controlArmKind: "permissive"`; B has
`permissiveEffects: false` and no control kind. `pre-rows` is historical only.

### Separate dependency and editable fragments

Tasks that need a dependency separate from the edit surface can declare:

```json
{
  "sourceAssembly": {
    "parts": ["dependency.calr.inc", "task.calr.inc"],
    "editableParts": ["task.calr.inc"]
  }
}
```

Both starters and both clean seeds contain those named fragments. Parts are
unique flat `.calr.inc` filenames, each ending with a newline. Order is
explicit; `editableParts` must be a nonempty proper subset. Unlisted compiled
sources are rejected. Clean seeds must preserve the immutable dependency bytes.
This declaration describes source assembly, not a stage registration.

The generated project composes exact fragment bytes into
`src/obj/ppw-source/Program.calr` immediately before `CompileCalorFiles`. It
does not paste dependency rows into the editable fragment. Every compilation
rechecks immutable hashes and replaces a stale or edited generated input.
Only the generated file enters `CalorCompile`; default source globs are not
left active. Dependencies are integrity-protected, **not hidden or sandboxed**:
they and generated build files remain readable. Whether that exposure meets
R1 is a separate methods decision, not an engineering approval.

The agent prompt names the editable fragments. Changing an immutable part,
adding another compiled source, or changing assembly/build configuration
invalidates the attempted run without replacement. Its original fragments,
including an altered dependency, are still archived before the workspace
is removed. Source hashes track fragment edits, excluding generated outputs.
Shape indicators inspect **only editable fragments**, never dependency rows.
Final fragments are checked against the frozen immutable inputs during
analysis. This interface currently supports raw editing, not MCP fragment
operations. It introduces no task count, shape-diversity threshold or sizing
decision.

`registration.json` must include:

- `schemaVersion: 2`, `status: frozen`, `supersedes: A-1.12`, a written
  `cause`, and independent `reviews` references;
- the exact `compilerCommit` (the frozen v0.18.0 release);
- an explicit unique `tasks` denominator and SHA-256 `artifacts` map covering
  **every** task-relative file, including each `pair.json`;
- a `stages` map. Each registered stage names its own `epochId` and
  `runsPerArm`. Pilot and confirmatory ids cannot coincide. No count defaults;
- for collection, each stage also supplies `modelPin`, `agentVersion`, and
  `spendAuthorization`, `stageRegistration`, `modelRegistration` evidence
  objects (`path` relative to the registration, plus `sha256`).

The pins hash the registration; the registration hashes the complete task
inventory. Source changes, missing/extra runs, unregistered tasks, wrong-stage
run identities, mixed compiler hashes, altered workspace policy/configuration,
symlinks and hard links fail closed. A failed run stays recorded rather than
being replaced by a new attempt in the same epoch.

The product's `optionsHash` includes workspace paths. Disjoint hashes are
therefore **not** a policy witness. They are retained observationally, while
`policy-before.json` and `policy-after.json` record the generated project's
actual Boolean policy properties and hashes of workspace build configuration.
Changed project/configuration files invalidate the slot. Redesigned arms
have zero replacement retries, including API failures; original transcripts,
usage envelopes and invalid reasons survive. Historical arms retain their
registered behavior.

Generated workspaces **and the separate held-out output directories** include
local build/package import boundaries, so an
ordinary restore does not inherit root central-package/lock-file settings
and get misclassified as policy tampering. A real .NET restore/build test
checks that the integrity snapshot stays unchanged; another restores and
executes the actual shell-generated held-out project without rewriting it.

Every attempted run also records its product-canary compiler hash, independent
of compilation-cache creation. Generated-C# validation failures can leave no
cache: a nonbuilding slot remains in the denominator using that independent
product witness. Any cache hash that does exist must still match the pin.
Successful builds require their normal compiler-cache evidence.

After those real prerequisites exist, the collection interface is:

```bash
CLAUDE_MODEL=<registered-model> bash bench/phase0-agent-native/run-ppw-epoch.sh \
  --epoch-id <registered-id> --stage pilot \
  --registration <reviewed-registration.json> --tasks-root <frozen-tasks-root> \
  --compiler-root <clean-v0.18.0-product-checkout> --confirm-paid-epoch
```

**Do not execute this now.** No monetary ceiling or null-result acceptance
was approved by #1264. The command requires supplied authorization evidence,
but the tool cannot judge whether prose constitutes valid maintainer approval.
Build the one shared Release compiler/Tasks/runtime product first. The runner
verifies its release commit, checkout cleanliness, hashes, both canaries,
model and agent version before invoking any agent. Product drift is checked
before each run and after collection. Runs are interleaved A/B per task.
Nonempty `CALOR_P0_*` and `CALOR_LOOP_*` environment overrides are rejected
at collection admission, including the historical timeout test hook. They
cannot silently change frozen task timeouts or disable capture.

Lifecycle is explicit: `scaffolded` → `collecting` → `collected` →
`archived`. Only the last two can produce stage records. Directory existence
never means `epochRun: true`. The historical C# conditional-test pair now
reads its ledger's `epochRun` Boolean; exactly one member is skipped whether
or not a scaffold directory exists.

## Verification and limits

`tests/test_ppw_instrument.py` materializes deterministic **synthetic** inputs
inside the repository. Its capture-helper → CLI analyzer → stage-ledger
integration executes without a paid agent or `--dry-run`. A second integration
executes the actual shell runner using deterministic, local `claude` and
`dotnet` stand-ins, checks both arms, and feeds its captured results into the
stage ledger. Negative controls mutate strict to permissive and simulate an
API failure; both retain a single invalid attempted run. The fixtures carry
`dataKind: synthetic`, and their reports have `empirical: false`.
They are not a pilot, a task registration, or scientific evidence.

The separate policy canary was also compiled with the actual v0.18.0
release-commit binary: permissive succeeds, strict reports `Calor0410 unknown`.
That is an instrument check, not an agent-effect measurement. Full paid
collection and eventual confirmatory adjudication are not validated by
synthetic inputs.

`tests/test_ppw_registration.py` adds independent negative controls for the
supersession pin tables, nested/cross-linked epoch data, stage promotion and
CLI option overrides. It also protects the lifecycle test pair's complementary
`epochRun` checks and expected skip counts. These tests cannot close #1271's
remaining obligation to re-pin the actual task set after its freeze and review.

`tests/test_ppw_source_assembly.py` exercises exact composition, immutable
inputs, source-inventory violations, generated-file tampering, fragment
capture and editable-only shape scoring. A synthetic shell→capture→pilot-ledger
test verifies that fragment edits are journaled. Real MSBuild exercises the
generated target; an available local Calor.Tasks product additionally builds
the preserved #1255 quota-adapter starter and honest completion through the
actual generated project. That last check validates compilation plumbing,
not a newly registered v0.18 experiment.

The assembler was also checked locally with the exact v0.18.0 release-commit
Debug DLL (`514f538024df990af86054af25975b756ba42ab1`). For the preserved #1255
quota-adapter input, assembled starter and honest completion compiled under
both policies. The assembled laundering completion compiled without diagnostics
under permissive policy and failed under strict policy (`Calor0410`, `Calor0411`).
All six invocations used the same unchanged DLL and left the editable fragment
untouched. This reproduces an existing compiler control, not an agent-effect
measurement, task qualification, or task-count decision.
