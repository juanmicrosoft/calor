# C1 re-freeze — verification pass 4 (Codex, run by the session lead)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed: generator tag-rule delta 8dcd541b..60af653d at head 60af653d.

## Codex output (verbatim)

Verified head `60af653d`.

1. **BLOCKING — RESOLVED: local tag masked remote.** [Generator line 65](docs/plans/evidence/c1-1423/generate_candidate_manifest.py:65) judges local and remote independently. A local candidate tag with a conflicting remote tag rejects. Reintroducing the masking bug caused a built-in control to fail.

2. **MAJOR — RESOLVED: malformed/peeled-only remote output accepted.** [Generator line 74](docs/plans/evidence/c1-1423/generate_candidate_manifest.py:74) requires every OID to be 40 lowercase hexadecimal characters and every peeled entry to have its base ref. Both rejection controls passed; the other-commit fixture is now valid hexadecimal. Removing either validation caused its control to fail. Preferring the unpeeled entry also failed controls.

3. **MINOR — RESOLVED: child-set sentence.** [Candidate document line 55](docs/plans/v0.24-c1-candidate.md:55) explicitly excludes C1, C2, and A1, then adds R0. This matches the generator and `CandidateManifestTests`.

All **13 built-in controls passed**; `main()` runs them on every invocation, as documented. **No new BLOCKING defect found in the reviewed delta.**

Full `--check` found no manifest difference but exited 1 solely because the remote lookup could not resolve `github.com`; successful live remote verification remains unconfirmed here. No files changed.

VERDICT: CLEAN

## Disposition

Verdict CLEAN. The sandbox could not reach github.com for the live remote lookup; the session lead ran `generate_candidate_manifest.py --check` with network access at 60af653d after `git fetch --tags`: output `OK`, exit 0.
