# #1407 evidence contract — cross-family adversarial review, round 4 (Codex)

| Field | Value |
|---|---|
| Artifact | PR #1466, branch `milestone-0.24/r0-1407-evidence-contract` |
| Commit reviewed | `90c179dbc0b5e53f13d65ec9895842ce8ac3c3cf` (round-3 fixes) |
| Reviewer | OpenAI Codex CLI 0.159.2, model `gpt-6.1-sol`, `model_reasoning_effort=high` |
| Invocation | `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -o <out> -` from the worktree root |
| Prompt | Round-4 hostile review: verify each round-3 disposition (`reviews/round-3-codex.md`, `git diff HEAD~1 HEAD`) and find remaining or new defects against checks (a)–(f); full #1407 and #1409 text appended; same fixed maintainer decisions named |
| Proposer | Claude Code agent (Claude Opus 5.5), author of the packet |
| Result | **0 BLOCKING** / 2 MAJOR / 2 MINOR — the protocol's stopping condition (a round with zero BLOCKING) is met; no round 5 was run |

## Codex output (verbatim)

## Round-3 dispositions

1. **Resolved** — Proposed-state fixtures now undo the acceptance write-back in memory; `FrozenContract()` no longer appends a duplicate acceptance amendment.
2. **Partially resolved** — Artifact/gate completeness, fixed criticality, historical-only eligibility, and the independence statement are enforced. Stale artifacts still pass as `BOUNDED`, and claim completeness remains caller-controlled.
3. **Resolved** — Z3 regeneration now downloads and verifies pinned assets. Package and website regeneration paths avoid publication; the new blanket upload prohibition creates a separate inconsistency below.
4. **Resolved** — The comparability sampling unit must equal the frozen `program-pair`; the new control exercises mutually consistent but incorrect keys.
5. **Resolved** — Required pair fields have explicit JSON types and controls using booleans, numbers, and nonempty objects.

## Objections

1. **[MAJOR] Terminal success still accepts explicitly stale evidence as `BOUNDED`** — `contract.json:268` requires stale required evidence to produce `BLOCKED`, never `BOUNDED`. However, `EvidenceContractValidator.cs:524–538` checks historical-only eligibility and explicit blockers without rejecting bounded adjudication of stale inventory entries. The new `SuccessRecord()` assigns `BOUNDED` to every nonhistorical artifact (`EvidenceContractTests.cs:1155–1158`), including all nine artifacts explicitly classified stale, such as `benchmark-results` (`artifact-inventory.json:361–365`). `WellFormedSuccessRecordPasses` then requires acceptance of that record (`EvidenceContractTests.cs:539–542`). This contradicts information already available to the validator; checking it requires no oracle execution. — **Fix:** reject successful terminal records containing a stale artifact adjudicated `BOUNDED` or `SUPPORTED`. Build the positive fixture against a synthetic, explicitly amended repaired inventory, and add a control restoring one artifact’s stale classification.

2. **[MAJOR] Claim completeness is still defined by the submitted terminal record** — Required subjects contain inventory artifacts and child gates only (`EvidenceContractValidator.cs:502–507`). Any nonempty `claim:` suffix is accepted as a subject (`lines 519–522,558–559`), without a frozen claim manifest. Consequently, adding a `BLOCKED` claim to `SuccessRecord()` fails, but removing that claim restores acceptance: the validator cannot distinguish an omitted registered claim from a claim that never existed. The “every subject exactly once” requirement (`contract.json:276`) therefore fixes missingness for artifacts and gates while leaving claims susceptible to omission after adjudication. — **Fix:** freeze the expected claim IDs before adjudication, alongside the raw-artifact freeze, and pass that manifest to terminal validation. Reject missing and unregistered claims; add a control removing a required blocked claim.

3. **[MINOR] Hash-comparability controls can pass for the wrong reason** — `ChangedComparabilityFieldIsIncomparable` replaces every string field with `"changed"` (`EvidenceContractTests.cs:340–354`). For `pairManifestSha256`, `metricSetSha256`, and `exclusionsSha256`, this introduces malformed hashes. Hash syntax independently produces the same E008 code (`EvidenceContractValidator.cs:627–628`) asserted for unequal identities. Those three controls would still pass if comparison equality enforcement at lines 633–640 were removed. This contradicts §12’s assurance that unrelated rejection cannot satisfy a control (`v0.24-evidence-contract.md:503–506`). — **Fix:** mutate hash identities to different valid 64-character hexadecimal values. Keep malformed-hash rejection in separate controls.

4. **[MINOR] The new upload prohibition conflicts with an inventoried regeneration command** — `contract.json:231` and `v0.24-evidence-contract.md:282–283` prohibit regeneration through any workflow that “uploads.” Yet `ci-test-reports` still prescribes rerunning `test.yml` (`artifact-inventory.json:85`), which uploads Actions artifacts at `.github/workflows/test.yml:212,408,572`. These uploads retain evidence rather than publish a release, but the prohibition contains no such distinction. — **Fix:** restrict the prohibition to public release-asset uploads and publication/deployment operations, explicitly permitting evidence-retention uploads.

## Verdict

0 BLOCKING / 2 MAJOR / 2 MINOR.
## Dispositions

| # | Severity | Disposition | Change |
|---|---|---|---|
| 1 | MAJOR | **Accepted, fixed.** | An artifact still classified stale can only be adjudicated `BLOCKED` (`T003` otherwise), so success needs it repaired and reclassified by amendment first. The positive fixture now runs against a synthetic repaired inventory (every stale artifact reclassified); a control restores `benchmark-results` to stale and is rejected. |
| 2 | MAJOR | **Accepted, fixed.** | New `rules.terminalSuccess.claimRegistry`: #1424 commits the claim ids at the raw-artifact freeze; #1408 adjudicates exactly that list. `ValidateTerminalRecord` takes the registry; registered claims are required subjects, and unregistered claim rows are rejected. Controls: unregistered claim (`T003`), omitted registered claim (`T001`), blocked registered claim (`T001`). |
| 3 | MINOR | **Accepted, fixed.** | `ChangedComparabilityFieldIsIncomparable` changes hash fields to a different valid 64-hex value, so only the equality rule can reject them; malformed hashes stay in their own controls. |
| 4 | MINOR | **Accepted, fixed.** | The prohibition now names publication operations (push packages, create or edit a release, upload release assets, open a publication PR, commit to `main`, deploy) and states that uploading Actions artifacts to retain evidence is not publication. |

No objection in this round was rejected; none concerned a maintainer decision. These round-4
fixes were made after the stopping condition was met and have not been re-reviewed by Codex.

**Tests after fixes:** `dotnet test tests/Calor.Compiler.Tests/ --filter "FullyQualifiedName~EvidenceContract"`
— 166 passed, 0 failed. `eng/test-manifest.json` Calor.Compiler.Tests `expectedTotal`
12231 → 12234. Zero build warnings. `sha256.json` re-hashed.
