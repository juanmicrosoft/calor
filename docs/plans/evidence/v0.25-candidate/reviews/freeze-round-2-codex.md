# 0.25 candidate freeze PR (#1579): Codex round 2

- **Invocation 2 of at most 4.**
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** `d49cb3a47` (the round-1 fixes), whole diff from `origin/main`.
- **Verdict:** REQUEST-CHANGES: 3 MAJOR, 1 MINOR.

Codex confirmed: offline regeneration is byte-identical; all 32 recorded PR merges and 19 issue states
match GitHub; the merge-aware target selection is corrected; 14,606 + 33 = 14,639.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | MAJOR | The trust anchor left out compiled inputs outside the test project (`tests/Shared/Z3TestSeeding.cs` is linked into the assembly; MSBuild inputs). | Fixed: with a target set, the running checkout must be the landing commit itself (`HEAD` equals the landing) and `git status --porcelain --untracked-files=all` must be empty, which covers every compiled and build input. Control: `TheTrustAnchorRefusesAnotherCheckout`. |
| 2 | MAJOR | A duplicated gate could replace a missing one (count and per-entry membership only). | Fixed: gate ids must be unique and equal the reviewed table's key set. Control: F5 replaced by a copy of W0. |
| 3 | MAJOR | Fingerprint evidence of only `packages.md` passed. | Fixed: both reproducible-builds hash files (`packages-1.json`, `packages-2.json`) must be retained and both must name the fingerprint, SDK and OS. Control: evidence reduced to `packages.md`. |
| 4 | MINOR | T1 summary checked only `registerVersion`. | Fixed: readiness, `approvalSeal` (recomputed) and `baselineApprovalSeal` are compared too. Three controls. |

Test manifest 14,639 → 14,645.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:70
   The trust anchor excludes compiled inputs outside the project directory. tests/Shared/Z3TestSeeding.cs is linked into this assembly but escapes both guards. Its module initializer could replace the requested classification target with the landing SHA, allowing a later checkout to pass. Require an actual clean landing checkout, including shared sources and MSBuild inputs, and add a drift control.

2. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:261
   Count plus per-entry membership does not establish gate completeness. Replacing merged[6] (F5) with a duplicate of merged[8] (W0) satisfies this predicate and every merge check. Require unique gate IDs and exact key-set equality; add a duplicate-replaces-missing control.

3. MAJOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:239
   Fingerprint verification accepts nonempty evidence containing only packages.md, then skips its contents. Keeping that entry and setting the fingerprint to sha256: followed by 64 zeroes passes both this predicate and generator validation. Require both JSON witnesses and compare their toolchains; add a text-only-evidence rejection control.

4. MINOR — tests/Calor.Compiler.Tests/EvidenceContract/V025CandidateManifestTests.cs:197
   T1 summary verification checks only registerVersion. Changing recorded readiness.F1 to READY, or either summary approval seal, escapes the independent verifier. Compare readiness, approvalSeal, and baselineApprovalSeal against the candidate packet and recomputed seal; add isolated controls.

Checks: offline regeneration is byte-identical; all 32 recorded PR merges and 19 issue states match GitHub. Merge-aware target selection is corrected. 14,606 + 33 = 14,639 is correct. Unmodified --check cannot query remote tags here; .NET tests are blocked by sandbox temp-file permissions. No files modified.
```
