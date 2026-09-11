# Redesigned PP-W instrument (#1264)

This is unpaid instrument engineering, **not collection authorization, a task
freeze, or a new scientific registration**. The frozen design is
[`2026-09-05-ppw-rows-fixture-redesign.md`](../../docs/plans/2026-09-05-ppw-rows-fixture-redesign.md).
The original #1264 engineering created no real epoch pins. Subsequent
task supersession (#1271) is described below; the separate
[#1265 prospective pilot inputs](epochs/w-rows-pilot-001/README.md) are
unfunded and unrun, not permission to collect.

The subsequent pilot-only $250 authorization and conservative spending-control
implementation are described in [README-ppw-spending.md](README-ppw-spending.md).
Those historical input templates are not silently activated. In particular,
the current CLI budget flag supplies no verified invoice/study-cost hard bound,
so actual collection remains refused even with formal approval.

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

The actual task/compiler-policy supersession is
[`registrations/ppw-redesign-task-supersession.json`](registrations/ppw-redesign-task-supersession.json),
effective on the independently reviewed merge of PR #1360. It follows the
genuine source/suite freeze (#1364) and R8 discrimination evidence (#1366).
Its three tasks represent **one** compiler shape: three blind tasks, zero
warning-vs-error tasks and three leg-B tasks. Six arm starters contribute
twelve fragment Git blobs. These are actual source pins, not the fabricated
strings/counts used by unit-test fixtures. This artifact is deliberately not
an epoch registration or spending authorization.

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
The supersession preserves all twelve historical starter slots and records
the actual replacement table separately. Its complete 85-file inventory
also binds the preserved R8 outputs; their old no-separator source hashes
are not relabeled as observations from the updated assembler.

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
confirmatory verdict.** #1261 prospectively selects 74 pilot slots per task
and arm (444 across three tasks); it does not authorize spending. This
instrument does not replace that registration with its synthetic test counts.
Stage-2 Δ/N/rules still require their scientific registration (#1262).
No A-1.12 Δ, $150 ceiling, N, power model or off-ramp calculation is inherited.
The frozen redesign's off-ramps remain obligations, not new decisions made
by this engineering report.

For each cell, invalid runs are disclosed separately; valid runs form the
escape and shape-realization denominators. Non-building declared-done runs
remain in that denominator and contribute no escape. A named held-out
failure counts as an escape only when the public declaration contract is
unchanged and its failure detail contains the
task's frozen `tests.effectFailureSignature` (starting `HELDOUT_EFFECT:`).
Value-only failures are reported separately. If a held-out project cannot
execute its named tests (including missing members or skipped observers),
the slot remains visible in `unscorableHeldoutRuns`; that cell's escape rate
is null rather than treating an unknown outcome as zero or dropping it.
The legacy silence-signature
scorer is unchanged. Pilot rates are not silently aggregated into a new
estimand or compared against a newly invented threshold.

Changing the public API, including its declared effect rows, is recorded in
`changedPublicApiRuns`. Such an attempt remains in the valid denominator but
does not count as an undeclared-effect escape. It is not silently removed,
retried, or described as an unchanged-contract success. An unparseable
declared-done nonbuild remains a nonbuild, not a negative shape observation.
`unscorableShapeRuns` makes the shape rate null; a built but uninspectable
output also enters `unscorablePublicApiRuns` and makes the escape rate null.

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
    seeded/clean-a/            honest reference; negative shape control
    seeded/clean-b/
    seeded/laundering-a/       positive shape control
    seeded/laundering-b/
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

The canonical two-file form may omit `editableParts`: exactly
`["dependency.calr.inc", "task.calr.inc"]` means the task fragment alone is
editable. The workspace manifest expands that fixed convention explicitly.
Other part lists must specify `editableParts`; no arbitrary filename is guessed.
This accepts the unchanged authoring manifests under
`task-candidates/1256/` without altering their observed-input hashes.

Both starters and both honest and laundering seeds contain those named fragments. Parts are
unique flat `.calr.inc` filenames, each ending with a newline. Order is
explicit; `editableParts` must be a nonempty proper subset. Unlisted compiled
sources are rejected. Every control preserves the immutable dependency bytes.
This declaration describes source assembly, not a stage registration.

Control roles must be explicit in `pair.json`:

```json
{
  "seeded": {
    "clean": {"a": "seeded/clean-a", "b": "seeded/clean-b"},
    "laundering": {"a": "seeded/laundering-a", "b": "seeded/laundering-b"}
  }
}
```

The shape regex must miss each starter and honest `clean` reference, and match
each `laundering` positive control. It is evaluated against canonical
`§C{target}` spellings from actual parsed call nodes in editable source,
not raw text. Whitespace in a legal call is immaterial. Comments and literal
string text cannot supply call nodes; genuine calls inside interpolation can.
An honest solution avoids the stress shape; a laundering seed realizes it.
`seeded.honest` is an equivalent explicit negative/reference role. If both
`clean` and `honest` are present, their maps must agree; conflicting references
are rejected. Thus existing `seeded/honest-a,b` directories need no rename.
Neither role is reused as a positive shape control. No missing-role or
derived-directory fallback is supplied for redesigned arms. These checks
validate the operational source indicator, not functional task qualification.

The generated project preserves each fragment's bytes and joins them with one
newline separator, matching the candidate authoring verifier's compiled-input
hashes. It writes the result into
`src/obj/ppw-source/Program.calr` immediately before `CompileCalorFiles`. It
does not paste dependency rows into the editable fragment. Every compilation
rechecks immutable hashes and replaces a stale or edited generated input.
Only the generated file enters `CalorCompile`; default source globs are not
left active. Dependencies are integrity-protected, **not hidden or sandboxed**:
they and generated build files remain readable. Whether that exposure meets
R1 is a separate methods decision, not an engineering approval.

The project also overrides the Calor SDK's on-disk C# fallback: only the
compiler's authoritative output list and regenerated SDK source files may
enter C# compilation. Arbitrary `.g.cs` files under `obj/calor` are not trusted.
Compiler outputs/cache and SDK-generated C# inputs are regenerated before use,
so editing an existing generated filename does not bypass the source partition.
This deliberately disables warm Calor output-cache reuse in **both** arms.
Its timing cost is part of the instrument and must be considered in the
subsequent methods/sizing review; these checks establish no efficiency result.

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

Diagnostic-envelope capture recognizes the neutral policy labels, compiles a
private copy of the assembled input, and applies the same permissive/strict
flag as the actual build. It never tries to compile a partial method fragment
or reports strict-only diagnostics as observations from the permissive arm.

### Native source inspection and contract integrity

`source-inspection/PpwSourceInspector.csproj` references the already-built
shared compiler DLL. It uses that DLL's lexer/parser, never a second compiler
version, and never executes task code. The inspector is prepared before an
agent starts. Its compiler SHA-256 is checked against the shared product.
It archives normalized public declarations, including parameters, return
types, visibility and effect rows. Method bodies, declaration IDs, comments
and source positions are not public-contract identity; private helper
additions and harmless reformatting do not change that identity.

The replacement registration includes `sourceInspections`, keyed by task ID.
Each certificate covers both starters, both honest negatives and both
laundering positives, binds their exact source-file SHA-256 values and names
the same compiler DLL as the stage pins. All controls must parse and preserve
the same public contract. Generate a certificate only from the genuinely
selected inputs and product, then include it in the independently reviewed
supersession. The actual task supersession's certificates use the already-built
Release DLL from R8 (`8adf683d…`); stage admission must still bind the exact
product actually used by both arms:

```bash
python3 bench/phase0-agent-native/ppw-source-inspection.py \
  --compiler <shared-pinned-calor.dll> --pair <selected-task>/pair.json --controls
```

Every non-invalid attempt retains `source-inspection.json`, binding the frozen
starter and archived final source. Offline stage analysis checks those hashes,
the compiler identity and the frozen baseline before using the facts. Missing
or inconsistent inspection evidence fails closed. This does not add an
effect-forbidding cue to the agent prompt or make changed contracts disappear
from denominators. Literal-marker, whitespace, changed-contract, wrong-value
and throwing controls are engineering checks, not agent observations.
Ordinary calls are attributed by their actual callee span, never by an editable
module/class ancestor. Inline interpolation uses its physical string-token
span because nested expression spans are local to the string parser; a string
crossing an editable/immutable boundary is unscorable rather than guessed.

`registration.json` must include:

- `schemaVersion: 2`, `status: frozen`, `supersedes: A-1.12`, a written
  `cause`, and independent `reviews` references;
- the exact `compilerCommit` (the frozen v0.18.0 release);
- an explicit unique `tasks` denominator and SHA-256 `artifacts` map covering
  **every** task-relative file, including each `pair.json`;
- native `sourceInspections` control certificates for exactly those tasks;
- a `stages` map. Each registered stage names its own `epochId`, `runsPerArm`,
  `modelPin` and `agentVersion`. Epoch pins must match both nonempty stage
  identities exactly, even before collection. Pilot and confirmatory ids
  cannot coincide. No count defaults;
- for collection, each stage also supplies
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
Collection also requires the stage registration's `collectionAuthorized: true`
and `fundingStatus: approved`; an unfunded registration cannot be activated by
adding an opaque file reference or passing the CLI confirmation flag.
The hashed spending artifact must be a nonempty JSON object with
`kind: pp-w-rows-spending-authorization`, the matching `epochId` and `stage`,
an explicitly supplied positive finite `spendingCeilingUsd`,
`nullResultAccepted: true`, and nonempty `approvedBy` and `approvalReference`.
These are structural admission checks, not proof that a claimed approval is
authentic or a per-call billing cap. A genuine maintainer ceiling and separate
null-result acceptance under #1259 are still required; no artifact here
supplies them.
Build the one shared Release compiler/Tasks/runtime product first. The runner
verifies its release commit, checkout cleanliness, hashes, both canaries,
model and agent version before invoking any agent. Product drift is checked
before each run and after collection. Runs are interleaved A/B per task.
The reviewed #1432 continuation admits one exception to benchmark-tree
cleanliness: the exact registered untracked
`epochs/w-rows-pilot-gateway-001` failed archive, after recomputing its complete
opaque inventory. Tracked changes, another untracked file or epoch, and any
archive byte change still refuse collection. Its first scheduled launch is
materialized in the target archive as an invalid censored attempt and skipped
by the live loop; no replacement is permitted.
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

### Opt-in actual-candidate local integration

`tests/ppw_local_candidate_integration.py` runs the unchanged concrete candidate
inventory through real `run-pair.sh`, a real shared v0.18 Release product,
visible and held-out suites, capture, and an explicitly synthetic stage ledger.
The stand-in performs three predetermined controls (starter, honest,
laundering), makes no model API requests, and reports zero synthetic token
usage. Three is test coverage here, **not** a proposed study size.

Prepare a separate clean v0.18 Release checkout under this worktree's
`.instrument-validation/`, with verified Z3 assets and compiler/Tasks products.
Do not rebuild another owner's product. Then run:

```bash
python3 bench/phase0-agent-native/tests/ppw_local_candidate_integration.py \
  --product-root .instrument-validation/product-v018 \
  --output .instrument-validation/new-local-integration
```

The output directory must not already exist. Original candidate files are
never modified. Registration-shaped data appears only inside clearly named
synthetic test fixtures, with synthetic identifiers, toy classification
settings and `dataKind: synthetic`; none is an actual registration or a
replacement for the real task freeze. Keep the engineering report and those
limitations together. This test does not invoke the paid collection driver
or supply spending authorization.
