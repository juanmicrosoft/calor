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
