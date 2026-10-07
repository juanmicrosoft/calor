# C1 #1423 tag-rule fix verification pass (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given the full
`git diff origin/main...HEAD` and read-only repository access. Under the contract §9 independence deviation
this is an adversarial tool review, not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **MAJOR — `docs/plans/evidence/c1-1423/generate_candidate_manifest.py:72`**: Malformed remote output passes: `garbage<TAB><ref>` followed by `<candidate><TAB><ref>^{}` returns no problems; a peeled-only entry also passes. Validate every OID and require the base ref alongside its peel. Add rejection controls; replace the self-test’s nonhexadecimal `'o' * 40` fixture with a valid OID.

2. **MINOR — `docs/plans/v0.24-c1-candidate.md:55`**: “`contract.json` children plus R0” is false. The verifier excludes C1, C2, and A1. Restore those exclusions.

The required controls discriminate, and `main()` runs them. Lookup errors, extra refs, and local non-commit refs reject.

`--check` at `696ab82470626164979a07792ed74932ab88d9e6` found no manifest difference but exited 1: remote lookup failed because `github.com` could not resolve. A successful check remains unverified here. No files changed.

## Dispositions

Scope: the tag-rule fix only (`git diff 8dcd541b HEAD`). The pass confirmed that the required
controls discriminate, that `main()` runs them, and that lookup errors, extra refs, and local
non-commit refs are rejected. Its sandbox could not resolve github.com, so it could not complete
`--check` itself. The manifest showed no difference before the remote lookup failed. `--check`
passes locally with network access.

1. **Fixed.** Every remote OID must be 40 lowercase hex characters. A peeled entry (`^{}`) without
   its base ref is rejected. Two controls were added: a non-hex base OID with a valid peel, and a
   peel-only entry. Both fail. The other-commit fixture is now a valid OID (`'a' * 40`). The
   self-test now has 13 controls. Mutation check repeated: re-introducing the local-masks-remote
   bug, or preferring the unpeeled entry, makes the self-test fail.
2. **Fixed.** The record again says the child set equals `contract.json` children other than C1,
   C2, and A1, plus R0.

This was the one verification pass requested for this fix. Both findings were fixed afterwards and
were not re-reviewed by Codex.
