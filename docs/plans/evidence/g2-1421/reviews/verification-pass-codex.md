# #1421 G2 determinism protocol — verification-only pass on the round-3 fixes (Codex)

**Reviewer:** Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, with
only the round-3 fix diff (`d064d35c..a02ba60b`) on stdin. **Reviewed commit:** `a02ba60b`.
**Verdict: NOT CLEAN** (3 of 5 resolved, no regression reported).

| Round-3 finding | Result |
|---|---|
| 1 Trusted main-validator step | **NOT RESOLVED.** Wrapping the exact trusted block in a shell `if false; then … fi` skips both validator calls, and D014 still passes |
| 2 Repurposed cell ids | RESOLVED |
| 3 Masked gate jobs | **NOT RESOLVED.** `if: ${{ 1 == 2 }}` on publish-nuget's "Run project tests" step skips it, and D014 still passes |
| 4 Values of invalid profiles | RESOLVED |
| 5 Unknown mode | RESOLVED |

**Status: open, not fixed in this PR.** The review cap (§9: 3 rounds, then this pass) is reached,
so no further change was made. Both open items are the same limitation: D014 reads workflow
text, and text matching cannot prove that a step executes (a shell or expression can always
disable it). D014 is defense in depth. The trust root for "the gate runs and the trusted
validator runs" is branch protection, CI results, and maintainer review of any workflow change.
Options for the maintainer: accept this as a recorded residual, or have the second G2 PR replace
the text checks with a structural check (parse the YAML and require each registered gate step to
have no `if:` other than none, and the trusted step's `run` body to equal a frozen list of
lines).
