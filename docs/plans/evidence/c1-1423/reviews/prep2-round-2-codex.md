# C1 #1423 prep for the second freeze (PR #1533), review round 2 (Codex)

Reviewer: Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), given
`git diff origin/main...HEAD` at `de7bae93` and read-only repository access. Scope: the whole PR
after the maintainer decisions of 2026-10-08: contract amendment 1.4.0 approved as proposed, the
six S2 compiler classes registered in protocol 1.4.0, the rename-back accepted, and #1533 treated
as a prep PR. Under the contract §9 independence deviation, this is an adversarial tool review,
not an independent review.

## Reviewer output (verbatim)

VERDICT: REQUEST-CHANGES

1. **MAJOR — New test breaks exact test-count enforcement.** `tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractTests.cs:1715` adds one `[Fact]`, but `eng/test-manifest.json:8` still expects **13,035** compiler tests. The resulting **13,036** total fails `scripts/check_trx.py` in CI and package publication, even if every test passes. **Fix:** raise `expectedTotal` to 13,036 and append the explanatory note before freezing the candidate.

2. **MINOR — Benchmark seed metadata is false.** `docs/plans/evidence/evidence-contract-1407/artifact-inventory.json:466` says `"None; the B1 generator is deterministic."` B1's results generator uses 10,000 bootstrap resamples with **SplitMix64 seed 1276**, registered in `b1-1276/registration/registration.json:150`. **Fix:** record that seed and re-seal the contract packet.

3. **MINOR — Binding title still overstates the repair.** `CHANGELOG.md:320` and `website/content/changelog.mdx:326` say module-wide tables are no longer copied at every loop. `Binder.cs:4715` still copies the accumulating callable-state map; removing empty entries only reduces that map. **Fix:** use "Name binding no longer copies the module-wide symbol table at every loop" identically in both files.

## Dispositions

1. **MAJOR: fixed.** `eng/test-manifest.json` was changed only through `manifest_bump.py`.
   `Calor.Compiler.Tests` now expects 13,036, with a note naming the new test.
2. **MINOR: fixed.** The `seeds` field of `benchmark-workflow-output` now records the B1 seeds:
   per-member input tuples from SplitMix64 seeded by SHA-256 of the pair id and member key, and
   bootstrap intervals from SplitMix64 with state 1276 over 10,000 resamples. The contract packet
   is re-sealed.
3. **MINOR: fixed** with the reviewer's title, identically in both files. The `[0.24.0]` sections
   are still byte-identical, and R2's `G012` scan still finds nothing.
