# Independent scaffold review

## Round 1: partially active metadata was not refused

Reviewer: `epoch-scaffolds-review-r1`, independent read-only **gpt-5.5**.
Reviewed staged tree `3d52e2bfef6c51b553b6cf24ad818603e62e3da2`, based on the
separately reviewed #1265 inputs while their CI was pending.

The reviewer executed all 28 targeted Python tests and 24 selected C# cases
(23 passed, one expected skip). It found a **medium** fail-closed defect:
the legacy reservation recognizer still returned true for non-null Δ/margin,
active `stages` or spending-proof fields. In-memory probes reproduced this
even after the modified registration hash was recomputed. The actual
committed-input candidates contained none of those values; the gap concerned
malformed or partially activated descriptors.

Accepted and fixed. The recognizer now requires the exact field set of each
typed reservation descriptor and null N/Δ/margin. Active stage, spending,
stage/model-proof, verdict, and unknown fields are rejected rather than
silently treated as input-only metadata. Added synthetic test-copy controls
exercise both descriptors and recompute registration hashes so rejection
cannot be attributed merely to a stale hash.

Final independent remediation review and exact-final-head full CI must be
linked in the separate #1260 PR before merge. Review acceptance does not
authorize collection, supply funding/null acceptance, or register stage-2 N.
