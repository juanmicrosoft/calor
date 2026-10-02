# Round 2: Codex adversarial review

- Reviewer: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (cross-family), diff vs `origin/main` on stdin.
- Reviewed SHA: `8944765764fa950fb42fba1ab5bab90ed34bae30`.
- Prompt: hostile review against #1417 and contract §6; numbered BLOCKING/MAJOR/MINOR or "No objections".

## Verbatim output

1. **BLOCKING — [DurableProvenance.cs:342](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ac3bb362723d09f44/tests/Calor.Compiler.Tests/Provenance/DurableProvenance.cs:342): Phase-1 commitments can be substituted after squash.** Once the measured commit disappears, `ReadPhase1` skips checking its hashes. `VerifyWriteBack` never retrieves the reviewed pending record from git. In `WriteBackFailsClosedWhenTheMergeChangedAMeasuredInput("manifest.json")`, replacing **both** the completed manifest hash and `writeBack.phase1InputContentHashes` with the landed hash makes the forged completion pass: the comparisons become tautological and the artifact landing remains valid. Round-1 finding 3 is inadequately fixed; its new control preserves the original phase-1 hash, missing this substitution.

2. **BLOCKING — [DurableProvenance.cs:332](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ac3bb362723d09f44/tests/Calor.Compiler.Tests/Provenance/DurableProvenance.cs:332): Write-back can omit the measured compiler entirely.** Phase 1 requires only nonempty hash maps and forbids `"/"`; it requires neither `src` nor complete measured-input coverage. In the `src/b.cs` changed-input control, use only `manifest.json` in `phase1.treeHashes`, retaining its unchanged content hash. Completion succeeds despite the changed compiler, adds the landing’s root hash, and verifies as authoritative. Round-1 finding 2’s whole-tree fix covers the completed commit but does not establish equality with the measured inputs.

3. **BLOCKING — [DurableProvenance.cs:292](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ac3bb362723d09f44/tests/Calor.Compiler.Tests/Provenance/DurableProvenance.cs:292): Completed provenance does not bind the current artifact.** `VerifyWriteBack` checks `phase1ArtifactSha256` against the historical landing commit but never calls `CheckCommittedArtifact`. After completion, changing artifact numbers while retaining its stamp leaves every write-back check satisfied; the repository artifact check compares only that stamp. The identity consequently authenticates old bytes while reporting the changed artifact as authoritative. Round-1 finding 4’s fix rejects a wrong landing commit but misses artifact drift after landing.

4. **MAJOR — [LedgerCommitStampTests.cs:206](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-ac3bb362723d09f44/tests/Calor.Compiler.Tests/LedgerCommitStampTests.cs:206): The write-back command can produce an identity that fails its own verifier.** For a pending ledger retaining `resolvableOnMain`, completion changes `durableIdentity.commit` to the squash commit, but `WriteBack` leaves the legacy pointer unchanged. The documented filtered command reports success and writes the index; subsequent complete validation fails `P015`. All five production ledger entries carry this field, while none of the write-back controls includes it.

## Dispositions

| # | Severity | Disposition | Change |
|---|---|---|---|
| 1 | BLOCKING | Accepted, fixed | The measuring PR commits the pending entry into `commit-stamp-index.json` together with the artifact, so the reviewed phase-1 record is part of the landing commit. `VerifyWriteBack` reads the index *at the landing commit* and requires the `writeBack` copy to equal that pending record (JSON deep equality on `headCommit`, `pr`, `treeHashes`, `inputContentHashes`, `artifactSha256`). Rewriting both the completed hashes and the phase-1 copy no longer passes. Control: the reviewer's exact shape (phase-1 copy rewritten to the landed values) in `WriteBackFailsClosedWhenTheMergeChangedAMeasuredInput`, for both a changed `src` and a changed manifest. The test fixtures now commit the index as a real PR would. |
| 2 | BLOCKING | Accepted, fixed | Phase-1 `treeHashes` (pending and in `writeBack`) must claim `src`, the measured compiler (`P013`). Complete coverage of every measured input cannot be decided generically for an arbitrary producer; the rule makes the compiler mandatory and leaves other inputs to the producer's record, which review checks. Control: in `PendingIdentityIsNeverAuthoritativeAndMustDescribeTheCommittedArtifact`, a phase 1 claiming only `manifest.json`. |
| 3 | BLOCKING | Accepted, fixed | A completed write-back now also requires the committed artifact to still hash to `phase1ArtifactSha256`, so a changed artifact needs a new identity. Control: `MergeCommitWriteBackNamesTheMergeCommit` changes the ledger's numbers while keeping its stamp, and the completed identity fails in a fresh clone. (`measured-on-top-of` already binds the current artifact through `landing.artifactSha256`.) |
| 4 | MAJOR | Accepted, fixed | A pending entry may not carry the #1199 `resolvableOnMain` field: it described an earlier measurement, and leaving it would make the write-back output fail `P015`. The verifier rejects it at phase 1 (`P013`), before any write-back runs. Control: the same pending test. |

Mutation check after the fixes: four defects were reintroduced together (no comparison with the
committed pending record, no `src` requirement in phase 1, no current-artifact check, and
`resolvableOnMain` allowed on a pending entry). 4 of 22 tests failed.

BLOCKING remaining after round 2: 0 by disposition. Round 3 re-reviews.
