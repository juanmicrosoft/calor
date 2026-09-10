# Confirmatory input reservation (#1260)

**Reserved, unregistered, unfunded and unrun.** Directory existence is not
evidence that an epoch ran. No pilot outcomes, fabricated observations, run
directories, stage ledger or benefit verdict are present.

This directory reserves `w-rows-001` separately from the prospective pilot
`w-rows-pilot-001`. Its `tasks/` tree contains the same **85 frozen files**
as the reviewed task-only supersession and the pilot input tree. Copying
inputs does not pool observations: neither tree contains collection data.

## Deliberately not active pins

`pins.json` is schema 1, kind `pp-w-rows-confirmatory-reservation`, with
`lifecycle: reserved-unregistered`. It is **not** a schema-2 runnable epoch.
`registration.json` is likewise a typed reservation, not a frozen stage
registration. The existing runner and analyzer reject these descriptors.
Renaming the lifecycle or passing a confirmation flag does not register a
stage or supply missing approval.

Compiler, arm, model and client values are prospective identity references
copied from the pilot's already chosen pins. `admission/pilot-pins.json`
preserves those exact bytes; `admission/task-supersession.json` preserves
the reviewed source/native-inspection registration. Their hashes are bound
by the reservation descriptor. These references are not an active stage-2
registration or permission to make result-dependent configuration edits.

Stage-2 `runsPerArm`, effect-size Δ and noninferiority margin are explicitly
null. No positive stand-in N, pilot allocation, old A-1.12 size or invented
effect size is substituted. Activation requires:

1. Genuine explicit spending and separate null-result acceptance under #1259.
2. Authorized pilot collection and its registered off-ramp decision (#1267).
3. Reviewed stage-2 size/effect/decision rules from actual pilot evidence
   (#1262), if the existing protocol permits proceeding.
4. A reviewed replacement of this reservation with complete stage-specific
   schema-2 pins/registration. Any identity change requires the stated
   amendment, not an unnoticed config edit.

The historical ledger and M0 administrative stop remain unchanged.

## Input templates and separate output roots

Both committed epoch directories are **input templates**. Future authorized
collection must read the selected template and write to a different, new
output root. The runner's existing never-overwrite protection remains intact.
Do not delete or mutate the committed input template to make room for runs.

The following pilot admission check deliberately exits 2 without invoking a
product or agent: approval is absent. It demonstrates explicit input resolution,
not a command to approve or launch collection.

```bash
bash bench/phase0-agent-native/run-ppw-epoch.sh \
  --epoch-id w-rows-pilot-001 --stage pilot \
  --registration bench/phase0-agent-native/epochs/w-rows-pilot-001/registration.json \
  --tasks-root bench/phase0-agent-native/epochs/w-rows-pilot-001/tasks \
  --compiler-root not-used-before-authorization \
  --epochs-root .ppw-collection-output
```

Selecting this confirmatory reservation instead is rejected as an unsupported
active registration, before any product or agent command.

Analysis accepts exactly one epoch ID and explicit stage. It never enumerates
sibling epoch directories:

```bash
python3 bench/phase0-agent-native/ppw-analyze.py \
  --epoch-id w-rows-pilot-001 --stage pilot \
  --epochs-root bench/phase0-agent-native/epochs
```

That command validates the complete pilot inputs, then refuses because the
pilot has not completed collection. Selecting `w-rows-001` is also refused,
because it is unregistered. Multiple/combined IDs and wrong stages are errors;
no dry-run estimate or empty ledger is emitted for either template.

## Historical compatibility and validation

The #1271 C# lifecycle trigger already uses the historical ledger's `epochRun`
flag rather than directory existence. Creating this reservation exposed a
second, Python-side existence trigger: the historical `--ledger` path treated
any `pins.json` as a completed A-1.12 collection. Five actual C# regression
tests failed.

The narrow correction recognizes this explicitly typed, hash-bound, unrun
reservation only with its exact field sets, null stage-2 sizes, consistent
state, and no run/analysis artifacts or symlinks. Active-stage/approval fields,
unknown fields, malformed state and added result artifacts fail closed.
Historical ledger recomputation remains byte-identical. Direct legacy
analysis of the reservation is rejected, including `--dry-run`; no redesigned
data is interpreted using A-1.12 arithmetic.

The exhaustive PP-E1/W5 directory census also lists the new reservation as
a skipped, different-kind input with zero shape-eligible runs. Its 120 analyzed
runs and all historical observations/statistics are unchanged. The census
rule is not weakened to hide new input directories.

Author validation: 29 targeted Python tests passed, including real CLI
admission checks with product/agent tripwires, complete 85-file/certificate
agreement, one-ID analysis, and malformed-reservation negatives. Of 24
targeted C# cases, 23 passed and the existing `epochRun: true` case was
correctly skipped. Separate independent review and final-head full CI records
are linked in the #1260 PR before merge. No model inference or collection was
performed.
