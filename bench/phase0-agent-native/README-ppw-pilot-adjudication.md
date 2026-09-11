# Registered pilot adjudication (#1267 engineering)

This is the **unpaid analysis integration**, not a collected pilot or issue
completion. #1267 remains open until genuine authorized collection and its
adjudication. #1259 has recorded the real pilot-only USD250 total approval and
stopping-rule/negative-null acceptance; operational collection remains held
for a trustworthy bound and a defensible full-pilot plan. Nothing here invokes a model,
compiler, collector, or payment interface.

## Explicit read-only command

After an authorized pilot has completed, select exactly one output epoch:

```bash
python3 bench/phase0-agent-native/ppw-pilot-adjudicate.py \
  --epoch-id w-rows-pilot-001 --stage pilot \
  --epochs-root path/to/immutable-collected-outputs \
  --out path/outside/epoch-roots/pilot-adjudication.json
```

Without `--out`, the JSON goes to standard output. The command never writes
into an epoch root, replaces an existing output, updates a descriptive stage
ledger or changes a historical benefit ledger. The committed
`epochs/w-rows-pilot-001/` remains an **unrun input template** and is refused.
The differently typed confirmation reservation is also refused. There is no
multi-epoch, confirmatory, dry-run-sizing or arbitrary-cell-file CLI.

The collector still emits descriptive per-cell output. **It does not invoke
this command automatically.** The command deliberately recomputes counts
through `ppw-instrument.analyze` from the selected raw archive; a saved
`ppw-stage-ledger.json` is not accepted as authoritative input.

## Exactly the frozen method

The authoritative method remains
`registrations/ppw-rows-stage1/registration.json` and frozen redesign §9
(PR #1363). No thresholds, allocation, weights or estimands are added:

| Estimand | Fixed mixture | Eligible denominator |
|---|---|---|
| Shape realization | All three tasks × both arms, weight 1/6 each | Actual valid runs in each cell |
| Arm-A escape | Three A task cells, weight 1/3 each | Actual valid runs, including nonbuilds and changed public contracts |

Integer `shapeRealized` and `escapes` counters feed the existing
`precision.py::fixed_mixture_band` helper. Rounded display rates are ignored.
Numeric assertion failures are not effect escapes. Escape is not conditioned
on building, shape realization, or otherwise-correct output. The existing
instrument excludes changed public contracts from the escape numerator, not
from eligible denominators.

Invalid attempts stay separately counted against the 74 scheduled slots per
cell; there are no replacements. Zero-eligible cells and any instrument-marked
unscorable required outcome leave the affected estimand unidentified, with
null point estimate and [0,1] range. Shape unknowns use `unscorableShapeRuns`;
escape unknowns use the **union** of `unscorableHeldoutRuns` and
`unscorablePublicApiRuns`, preventing double counting. This adapter does not
reclassify an instrument-marked unknown as zero. B-arm escape unknowns do not
alter the A-only estimand. Accounting fields disclose counts, not new rates
or additional estimands.

Bands retain the registered allocation of 0.025 error per estimand and at
least 95% joint nominal coverage under the method's independence assumptions.
Actual eligible counts determine precision after attrition; fixed cell
weights remain unchanged. No power or rare-event guarantee is inferred.

Stopping decisions use the exact count-derived rational values:

- Realization **strictly below 1/2**: stop stage 2 as an R6 protocol defect.
- A escape **exactly zero**: stop stage 2; the redesign is unsuccessful under
  the registered R1/R2 rule. A positive upper confidence limit does not cancel
  this point-rule stop.

Exactly 50% is not below 50%. Both known stops are reported if both fire;
there is no invented precedence. An unidentified rule remains null. A known
stop is not erased by an unrelated unidentified estimand; otherwise any
unidentified required estimand leaves the decision unidentified.
`NO_REGISTERED_POINT_STOP` means only that neither point rule fired with
identified estimates. **It never authorizes stage 2**, supplies its N/Δ,
grants funding, re-arms gate 10 or adjudicates a confirmatory benefit verdict.

## Provenance and immutable inputs

The separate prospective `analysis-registration.json` binds the new command,
unchanged precision helper, descriptive analysis dependencies, frozen method,
model decision, task supersession and pilot pins by SHA-256. It is an explicit
reviewed analysis-layer addition, **not a silent change to the #1367/#1368
collection pins or their preserved execution anchor**. Future code or method
drift requires an explicit reviewed analysis registration, not bypassing the
hash checks.

Epoch identity, stage, allocation, model/client, shared product, collection
execution-file hashes and exact frozen task/native-certificate projection must
match the registered scope. The collection commit is recorded and must be a
full commit identity; a later documentation-only collection checkout may have
a different commit ID but its registered execution-file hashes must still
match. Method/model/task evidence hashes must match the committed authority.
The method can be resolved by its verified committed hash even when the
collector copied its registration without duplicating the referenced
`admission/` files into the output archive.

### Explicit inactive guarded projection (#1403)

The original eleven-artifact `PILOT_PINS` remain preserved. They are not
silently treated as the fifteen-artifact inventory emitted by the #1378
spending collector. `guarded-analysis-projection.json` now registers a
separate **non-runnable, synthetic-analysis-only** projection of that exact
reviewed instrument amendment.

An archived selected-stage `instrumentAmendment` must identify the registered
amendment by relative path and SHA. An exact registered path/SHA pair can
resolve against the committed authority. Any different relative path must
name an archive-local file whose bytes match the registered hash. Present
but changed evidence is rejected, including at the canonical path. The execution
inventory must equal the amendment's complete replacement map. Missing or
unregistered references, mixed inventories and downgrades to the old map
are refused. A spending-plan marker without the amendment is also refused.
The chosen projection, selected proof and resolution source are disclosed
in output provenance; no archive or
source identity is rewritten to force a match.

Current guarded selection accepts **only explicitly synthetic input**.
An empirical header fails before raw analysis, even before checking for
contradictory synthetic markers. The original synthetic baseline still works.
This proves read-only compatibility and unchanged mathematics, not empirical
collection readiness. Future actual fifteen-artifact admission requires its
own reviewed operational execution/analysis projection and genuine
liability-bound/full-444 feasibility evidence. No financial limit, ledger
path, receipt projection, active pins or experiment is created here.

`analysis-registration.pre-projection-1403.json` preserves the exact preceding
analysis manifest, including its older supersession. The current manifest
explicitly supersedes that archive and fingerprints the new selector,
projection and amendment. The dated #1394 post-guard assessment remains
byte-identical; its original analysis-manifest hash resolves through this
preserved lineage rather than pretending it describes the new source.

The raw analyzer validates each scheduled run and its source-bound facts;
missing, duplicated, misplaced, cross-epoch, wrong-stage or mixed-policy data
cannot silently change the mixture. The integration additionally checks the
complete unique six-cell inventory and count/unknown/invalid consistency.
Before/after content-inventory hashes detect changes to the epoch during
analysis. Analysis dependencies are rechecked before reporting. The JSON
records the inventory hash/file count, pin/registration hashes, analysis
manifest and execution hashes, model/client and product identity.

The #1434 gateway continuation has one explicit linked-segment exception, not a
general pooling interface. The original gateway-001 C001-A1 launch remains one
invalid attempt under its original profile. Its wrapper carries the original
source and file identities; gateway-002 runs only the other 443 identities under
the reviewed terminal-attempt profile. The unexecuted #1433 proposal is retained
as registration history, not treated as a data segment. The unique population
remains 444 slots and 74 per cell.

Replay distinguishes valid completion events from evidence-backed invalid
terminals. Both consume a slot; only valid runs enter eligible denominators.
Zero-request invalids require the protected attempt, invocation, reason and
sealed-source evidence, not fabricated usage or a missing source-inspection
report. Financial, pricing, isolation and unknown-liability failures still
prevent adjudication. The fixed weights, attrition bands, unidentified-outcome
rules and point stops above are unchanged. Registration history is preserved in
`analysis-registration.pre-terminal-1434.json`; archive relocation does not alter
the recorded original authority paths or source identities.

These checks assume an immutable, honestly archived data source. They do not
cryptographically authenticate model serving, approval provenance, native
inspection producer claims, independence of fresh runs or provider weights.
Those collection/coverage qualifications from §9 still apply.

## Synthetic controls are not a pilot

Tests build a clearly named `synthetic-pilot` only under owned test scratch.
They copy frozen source and cached native control facts without recompiling;
build/test outcomes, usage and transcript records are explicitly prescribed
synthetic fixtures. There are no experimental model calls.

Synthetic input retains `dataKind: synthetic` and `empirical: false`. The
formula/rule evaluation is visible for testing, but its decision status is
`SYNTHETIC_ONLY`, `appliedToEmpiricalData` is false, and no stage-2 authority or
confirmatory verdict exists. An empirical header that conflicts with an
explicit synthetic marker in run/agent records is rejected; absence of a
marker still cannot authenticate genuine observations. No synthetic report is committed as an epoch
outcome. Tests cover exact/rounded boundaries, zero escape, unequal attrition,
nonbuilds/API changes, overlapping unknowns, zero eligibility, malformed
inventories, provenance drift, raw cross-epoch/stage records, stale saved
ledgers, read-only CLI behavior and immutable-output refusal.
