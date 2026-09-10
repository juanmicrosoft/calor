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

Generated workspaces include local build/package import boundaries, so an
ordinary restore does not inherit root central-package/lock-file settings
and get misclassified as policy tampering. A real .NET restore/build test
checks that the integrity snapshot stays unchanged.

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
