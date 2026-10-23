# T1 (#1428) Codex review, round 2 (invocation 2 of at most 4)

**Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt: round 1's
prompt plus "verify each round 1 fix, then look for remaining or new problems", on PR #1562 at
`cfec3c0cc` (rebased onto `13195e887`). **Verdict:** CHANGES-REQUESTED (4 MAJOR, 2 MINOR).
Codex judged R1-1, R1-4 and R1-6 closed, R1-3 closed on content checks, and R1-2, R1-5 and R1-7
incomplete. Reduced independence (R0 §9).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R2-1 | MAJOR | Deleting both precondition copies skips S014 entirely | Fixed: from scope 1.3.0 on, S014 requires the preconditions; control "preconditions removed from the packet and the amendment" |
| R2-2 | MAJOR | `aggregate.py` reads evidence only at the candidate, so evidence committed after the freeze is invisible | Fixed: `--evidence-rev REV` reads scope, register, evidence and records at REV (the candidate must be its ancestor); records still count only when they name the candidate |
| R2-3 | MAJOR | A failed library or MCP conversion that still returns Calor is discarded, so broken output becomes `rejected` | Fixed: returned output is always compiled; broken output is `invalid-output` (never allowed) even when the surface reported failure; a refusal whose output compiles stays `rejected` |
| R2-4 | MAJOR | T005 accepts counts from any JSON file, and the counts are outside the approval seal | Fixed: the source must be F2's `rows.json` and one of F2's approved evidence files; the source and counts are in `approvalSeal`; control "corpus source redirected" |
| R2-5 | MINOR | In-process conversion still loads `~/.calor/manifests` (the converter's synthesis resolver) | Partly fixed: no test-only switch exists (fixing it needs a `src/` change, out of scope for T1), so every control cell fails closed when the user manifests directory holds a manifest. CI and local runs use an isolated `HOME` |
| R2-6 | MINOR | T1-ARRAY-CONDITIONAL's selected-branch initializers log the same tag | Fixed: `L1`, `L2` |
