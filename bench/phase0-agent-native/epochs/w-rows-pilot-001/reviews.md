# Independent prospective pin review

## Round 1: explicit authorization-state gap

Reviewer: `pilot-pins-review-r1`, independent read-only code-review agent,
model **gpt-5.6-terra**.

The reviewer executed all 27 initial pin/source/precision guards, which
passed, but identified a **medium fail-closed defect**. The collection driver
ignored `collectionAuthorized: false` and `fundingStatus: unfunded`; adding
a syntactically valid opaque evidence reference and the confirmation flag
could proceed despite those explicit declarations.

This finding was accepted. The narrow execution-code fix is separately
committed at `58588813cb8c46446b695539b4fbd530ee6eaf51` so the prospective pins
can name the actual guarded harness, not an inconsistent earlier revision.
That commit must remain in main's history.

The driver now requires explicit authorized/approved state and structured,
nonempty spending evidence bound to the stage and epoch, with a supplied
positive finite ceiling, separate null-result acceptance, and approval
provenance. These are structural checks, not authentication of a claimed
maintainer decision or a per-call billing cap. The real registration remains
unfunded/unauthorized and supplies no spending artifact.

Added tests cover valid opaque references while unauthorized or unfunded,
empty/unstructured evidence, wrong stage/epoch, invalid ceilings, missing
acceptance/provenance, tampering, and confirmation-flag requirements. Approval
values in tests are explicitly synthetic fixtures, not real authorizations.
The author ran all 36 targeted tests successfully. Independent re-review of
the remediation and actual pin set is required before acceptance.

## Round 2: registered model/client binding

Reviewer: `pilot-pins-review-r2`, independent read-only code-review agent,
model **gpt-5.5**, reviewing `8135c73b5f97fd7c8983fbd8980d5f04d6cebac5`.

The reviewer executed all 36 targeted tests and independently checked
registration/proof hashes, central copies, product checkout/hash, and the
actual local client version/executable hash. The unfunded/unauthorized state
was preserved. An additional **medium** defect remained: changing only
`pins.modelPin` or `pins.agentVersion` in memory still passed the central
pin validator, despite disagreement with the frozen stage.

This finding was accepted. Commit
`7fa8df1d630d94e2e3309dffff429de582ff96fc` requires both nonempty string
identities to match the registered stage exactly. Synthetic analysis
fixtures now register their explicit synthetic identities; independent
model/client mutation controls reject both mismatches. All 45 authorization
and instrument checks passed. This commit supersedes the first execution
anchor and must also remain in main's history. The final pin revision
references its exact execution-file hashes.

Final independent re-review and final-head CI records must be posted in the
separate #1265 PR and linked from its body before merge. This finding history
does not substitute for those acceptance records.

## CI: preserve the whole-directory census

The first full final-head CI run found one failure among 404 Python checks:
the PP-E1/W5 archive report's exhaustive directory census still listed 30
entries, while this real pilot input added the 31st. Its registered rule
lists every non-dot entry as analyzed or skipped; hiding an unrun directory
from that denominator would weaken the existing rule.

The existing generator was rerun without changing its algorithm. Only
`entries` and the `skipped` list changed: `w-rows-pilot-001` is explicitly
listed as a different-kind input with **zero shape-eligible runs**. All
previous skipped entries, 120 analyzed runs, observations, statistics and
other fields remain identical. Neither a benefit ledger nor a verdict was
changed. This metadata-only correction receives a separate independent
re-review and a fresh full final-head CI run.
