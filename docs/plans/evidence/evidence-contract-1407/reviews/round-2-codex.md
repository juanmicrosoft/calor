# #1407 evidence contract — cross-family adversarial review, round 2 (Codex)

| Field | Value |
|---|---|
| Artifact | PR #1466, branch `milestone-0.24/r0-1407-evidence-contract` |
| Commit reviewed | `24053f87fcdd883aad5852e614865b2927bd015d` (round-1 fixes) |
| Reviewer | OpenAI Codex CLI 0.159.2, model `gpt-6.1-sol`, `model_reasoning_effort=high` |
| Invocation | `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -o <out> -` from the worktree root |
| Prompt | Round-2 hostile review: verify each round-1 disposition (`reviews/round-1-codex.md`, `git diff HEAD~1 HEAD`) and find remaining or new defects against checks (a)–(f); full #1407 and #1409 text appended; same fixed maintainer decisions named |
| Proposer | Claude Code agent (Claude Opus 5.5), author of the packet |
| Result | 2 BLOCKING / 3 MAJOR / 1 MINOR |

## Codex output (verbatim)

## Round-1 dispositions

1. **Partially resolved** — Missing reports and all 13 suites are now inventoried, but the new SDK-consumer entry records the wrong Z3 input provenance.
2. **Resolved** — Benchmark registration must merge before repairs, differential checks, or recomputation; later changes require amendments.
3. **Partially resolved** — The independence wording now matches the maintainer decision, but the terminal predicate still permits release-critical blockers outside the sweep.
4. **Resolved** — B1 and N1 require separate registrations and attempts; S1 capacity explicitly covers their combined work.
5. **Partially resolved** — Acceptance write-back is specified, but its permitted edits conflict with inventory-version validation, and mixed ceiling states still pass.
6. **Resolved** — Establishment prerequisites and identified non-vacuity checks are required. The documented boundary between schema validation and review of evidence truth is explicit.
7. **Partially resolved** — Missing fields, inclusion flags, empty denominators, and comparability metadata are checked; benchmark freshness remains bypassable, and the positive fixture contains invalid hashes.
8. **Resolved** — Versions must increase, the inspection flag must be Boolean, and amendment controls are separated.
9. **Resolved** — The uniqueness claim was withdrawn; the revised text accurately describes lossy legacy mappings.
10. **Resolved** — The optional production K-induction diagnostic path is recorded accurately.
11. **Resolved** — Named packet coverage is enforced; all three committed hashes match.

## Objections

1. **[BLOCKING] Terminal success still permits release-critical `BLOCKED` artifacts** — `contract.json:262` excludes blockers only among **#1419 sweep rows**. Conditions at `contract.json:265–267` require regeneration, matching identity consumption, and reduced independence, but never require zero blockers across other adjudication rows. #1408’s closure evidence (`contract.json:333`) accepts one outcome per artifact, including `BLOCKED`. A regenerated quality report could therefore receive `BLOCKED` while satisfying the enumerated success predicate. This remains contrary to the epic’s explicit zero-release-critical-blockers requirement. — **Fix:** require zero `BLOCKED` adjudications across every release-critical artifact, gate, and claim; require missing or inconclusive required evidence to block success. Add a negative control using a non-sweep blocker.

2. **[BLOCKING] Regeneration requires publication before the adjudication that must authorize publication** — `contract.json:332` requires #1424 to regenerate **every authoritative and derived inventory artifact**, and #1408 depends on #1424. However, `artifact-inventory.json:529–543` classifies the actual **nuget.org packages** as authoritative and gives a publishing workflow as their regeneration path. That workflow pushes packages at `.github/workflows/publish-nuget.yml:441–442`. Conversely, `contract.json:330` requires package publication to wait for successful adjudication, and `contract.json:20` authorizes no package publication. The contract supplies no distinction between candidate package generation and registry publication that breaks this cycle. — **Fix:** inventory unpublished candidate packages and their inspection/provenance outputs separately. Regenerate and adjudicate those before publication; treat registry publication as a subsequent, separately authorized consumer of their exact hashes.

3. **[MAJOR] The acceptance write-back does not produce a valid packet as specified** — `contract.json:13` requires amendment `1.0.1` and says the write-back “changes no other field” beyond the listed acceptance changes. The inventory remains `contractVersion: "1.0.0"` (`artifact-inventory.json:4`), which fails I012 when the contract becomes `1.0.1` (`EvidenceContractValidator.cs:277–278`). The positive write-back control validates only the contract (`EvidenceContractTests.cs:705–708`), so it misses this failure. Additionally, C012’s `PROPOSED` branch returns without checking ceiling states (`EvidenceContractValidator.cs:209–213`): changing one ceiling to `ACCEPTED` passes despite the claim that every mixed state is rejected. — **Fix:** explicitly permit synchronized inventory/document version and lifecycle updates, then validate the complete acceptance packet. Require every ceiling to remain `PROPOSED` in the proposed state and add that mixed-state control.

4. **[MAJOR] Benchmark evidence bypasses candidate and freshness checks** — `ValidateEvidenceRows` returns to the next row whenever `counted != "established"` (`EvidenceContractValidator.cs:416–417`), before checking artifact classification, open defects, candidate identity, or recording time. Its positive benchmark fixture deliberately uses `counted: "not-established"` (`EvidenceContractTests.cs:862`). Keeping that fixture’s benchmark metadata intact while changing its artifact to stale `benchmark-results`, its source to another full SHA, and its timestamp to before the cutoff produces no rejection through those checks. Static benchmark results need not establish a proof, but decision-bearing results still must satisfy the frozen-candidate and freshness rules. — **Fix:** distinguish decision-bearing evidence from proof establishment. Apply artifact, candidate, provenance, and freshness checks to decision-bearing benchmark rows regardless of `counted`; retain explicit historical-row handling. Add controls for these mutations.

5. **[MAJOR] The SDK-consumer inventory attributes evidence to assets it never consumes** — `artifact-inventory.json:270` declares `sdk-consumer-check` derived from `z3-release-binaries`. Actually, the consumer job bootstraps through `download-z3.sh` or `.ps1` (`publish-nuget.yml:210–217`) and locally packs its own SDK (`.github/scripts/test-sdk-package.sh:34–35`). The publishing job separately downloads the repository’s `z3-binaries` release assets (`publish-nuget.yml:355–395`). The new inventory therefore misrecords the release-critical consumer check’s input chain and omits this specific consumer-versus-published-package identity gap from its open defects. — **Fix:** record the upstream-bootstrap inputs accurately and assign the identity gap to #1420/#1410. Require eventual consumer evidence to identify the exact candidate package and native-asset hashes it tested.

6. **[MINOR] The “well-formed” benchmark control contains invalid SHA-256 identities** — The positive fixture uses `"11"`, `"22"`, and `"33"` as source/task SHA-256 values (`EvidenceContractTests.cs:795–798`), and `"aa"`, `"bb"`, and `"cc"` as comparability hashes (`EvidenceContractTests.cs:849–856`). These pass because `IsBlank` checks only null or blank strings (`EvidenceContractValidator.cs:562–563`); it also accepts empty objects or arrays in required identity fields. This is separate from the explicitly deferred task of rehashing real manifests. — **Fix:** use valid 64-hex synthetic hashes, validate identity-field types and hash syntax, and add short-hash and wrong-type negative controls.

## Verdict

2 BLOCKING / 3 MAJOR / 1 MINOR.
## Dispositions

| # | Severity | Disposition | Change |
|---|---|---|---|
| 1 | BLOCKING | **Accepted, fixed.** The predicate excluded blockers only among #1419 sweep rows. | New terminal condition 3: no #1408 adjudication row for any release-critical artifact, gate, or claim is `BLOCKED`; missing, inconclusive, stale, or incomparable required evidence makes a row `BLOCKED`, never `HISTORICAL-ONLY` or `BOUNDED`. New `ValidateTerminalRecord` (`T001`–`T003`) checks the machine-checkable part (no release-critical `BLOCKED`, no empty success record, reduced independence, no `SUPPORTED`, known outcomes, boolean `releaseCritical`). Controls include a non-sweep blocker (`release-quality-reports`) and a failure record that may carry a blocker. |
| 2 | BLOCKING | **Accepted, fixed.** Verified: `nuget-packages` is authoritative, its regeneration path is the publish workflow (`publish-nuget.yml:442` pushes), and #1424 had to regenerate every authoritative artifact before #1408. | New `releasePath.publicationSeparation` and §7 "Regeneration never publishes": #1424 regenerates publication surfaces only as unpublished, content-hashed candidates; push, release creation, asset upload, and Pages deployment happen only after `MILESTONE-SUCCEEDED`, through #1410, consuming the exact adjudicated hashes. New inventory artifact `candidate-packages` (authoritative, `content-hash`, owners #1424/#1410/#1423). `nuget-packages` regeneration now says it is not regenerated by #1424. #1424 closure evidence and terminal condition 6 updated. |
| 3 | MAJOR | **Accepted, fixed.** | The write-back now explicitly sets the inventory `contractVersion` and the document's version/status lines and amendment log. The positive control validates contract and inventory together; a new control shows a write-back that leaves the inventory at `1.0.0` fails `I012`. `C012` now requires capacity and every ceiling `PROPOSED` in the proposed state, with a mixed-state control. |
| 4 | MAJOR | **Accepted, fixed.** | Any benchmark row not recorded `historical` is decision-bearing; artifact (`E004`/`E005`/`E014`), candidate (`E006`), and freshness (`E007`) checks now run for it whatever `counted` says. The positive benchmark fixture cites the authoritative `benchmark-corpus` with its #1276 defect resolved. Controls: stale artifact, other commit, pre-cutoff time; a historical row is exempt. New `reclassificationRule`: a repaired stale artifact becomes authoritative only by a versioned amendment naming the repairing PR. |
| 5 | MAJOR | **Accepted, fixed.** Verified: the `sdk-consumer` job bootstraps Z3 with `download-z3.sh`/`.ps1` (`publish-nuget.yml:210-217`) and packs its own SDK (`.github/scripts/test-sdk-package.sh:34-35`). | `sdk-consumer-check` now derives from `z3-upstream-pins`, records the upstream-bootstrap environment, adds #1420 as owner, and records the open defect that no hash ties the tested package and natives to the published one (#1420, #1410). |
| 6 | MINOR | **Accepted, fixed.** | Fixtures use 64-hex synthetic hashes. Every `*Sha256` field in a pair row or comparability key must be 64 hex digits (`E009`/`E008`); empty objects and arrays count as missing. Controls for short pair hashes, a wrong-type identity, and a short comparability hash. |

No objection in this round was rejected; none concerned a maintainer decision.

**Tests after fixes:** `dotnet test tests/Calor.Compiler.Tests/ --filter "FullyQualifiedName~EvidenceContract"`
— 149 passed, 0 failed. `eng/test-manifest.json` Calor.Compiler.Tests `expectedTotal`
12200 → 12217. Zero build warnings. `sha256.json` re-hashed.
