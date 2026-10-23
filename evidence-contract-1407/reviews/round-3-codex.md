# #1407 evidence contract — cross-family adversarial review, round 3 (Codex)

| Field | Value |
|---|---|
| Artifact | PR #1466, branch `milestone-0.24/r0-1407-evidence-contract` |
| Commit reviewed | `21a67050f684f32f89a64e148954d0c92db3e1cb` (round-2 fixes) |
| Reviewer | OpenAI Codex CLI 0.159.2, model `gpt-6.1-sol`, `model_reasoning_effort=high` |
| Invocation | `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -o <out> -` from the worktree root |
| Prompt | Round-3 hostile review: verify each round-2 disposition (`reviews/round-2-codex.md`, `git diff HEAD~1 HEAD`) and find remaining or new defects against checks (a)–(f); full #1407 and #1409 text appended; same fixed maintainer decisions named |
| Proposer | Claude Code agent (Claude Opus 5.5), author of the packet |
| Result | 1 BLOCKING / 3 MAJOR / 1 MINOR |

## Codex output (verbatim)

## Round-2 dispositions

1. **Partially resolved** — The zero-blocker rule is explicit, but terminal validation still accepts omitted subjects and caller-supplied criticality.
2. **Resolved** — Unpublished candidate packages break the NuGet regeneration/adjudication cycle. A separate Z3 publication conflict remains below.
3. **Partially resolved** — Version synchronization and proposed-ceiling checks are fixed, but the prescribed acceptance write-back breaks the tests.
4. **Resolved** — Decision-bearing benchmark rows now undergo artifact, candidate, and freshness checks regardless of `counted`.
5. **Resolved** — The SDK consumer’s upstream-bootstrap inputs and tested-versus-published package gap are recorded accurately.
6. **Partially resolved** — SHA-256 syntax and empty containers are rejected; non-hash identity fields still accept invalid JSON types.

## Objections

1. **[BLOCKING] The prescribed acceptance write-back cannot pass the required compiler tests** — `contract.json:13` requires transition to `FROZEN`/`1.0.1` and permits no test changes. However, fixtures load the current committed contract (`tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractTests.cs:1084`). After that transition, `ProposedContractWithAcceptedCeilingFails` merely sets an already accepted ceiling to `ACCEPTED` and incorrectly expects C012 (lines 853–857); `GateMarkedMetWhileCapacityProposedFails` similarly sets an already met gate to `MET` and expects C008 (lines 713–717). Additionally, `FrozenContract()` appends another `1.0.1` amendment to the now-frozen packet (lines 978–981), triggering the strict-version check in `EvidenceContractValidator.cs:246`. The required compiler workflow executes these tests (`.github/workflows/test.yml:508–519`). — **Fix:** construct synthetic proposed/frozen fixtures independently of the committed lifecycle state. Verify the entire suite against both the current packet and an in-memory acceptance packet before merging R0.

2. **[MAJOR] Terminal validation permits missingness and criticality laundering** — `EvidenceContractValidator.cs:501–518` validates only supplied adjudications and trusts their `releaseCritical` flags. Starting from `SuccessRecord()`, removing the required `release-quality-reports` row still returns no violation; changing its outcome to `BLOCKED` and its criticality to `false` also passes. Changing its outcome alone to `HISTORICAL-ONLY` passes despite `contract.json:266` prohibiting that disposition for missing required evidence. The positive success fixture also omits the explicit statement that the epic’s independently adjudicated outcome is unmet (`EvidenceContractTests.cs:1010–1030`, versus `contract.json:274`). These are structural omissions, not questions requiring oracle execution. — **Fix:** validate against a frozen expected-subject manifest with fixed criticality and historical eligibility; reject missing, duplicate, unknown, or improperly demoted subjects. Require the independence limitation explicitly in the success record. Add isolated controls for each bypass.

3. **[MAJOR] An authoritative regeneration command still publishes and mutates release assets** — `artifact-inventory.json:133–142` classifies `z3-release-binaries` as authoritative and prescribes `gh workflow run build-z3.yml` for regeneration, explicitly describing it as republishing. That workflow creates or updates the public Z3 release and uploads assets (`.github/workflows/build-z3.yml:298–320`). Yet `contract.json:269,336` requires authoritative artifacts to be regenerated before adjudication, while line 229 prohibits release creation and asset upload during regeneration. The candidate-package addition does not resolve this environment-artifact conflict. — **Fix:** define Z3 assets as pinned inputs materialized and checksum-verified without publication. If rebuilding is required, inventory unpublished rebuilt candidates separately and remove the publishing workflow from #1424’s regeneration path.

4. **[MAJOR] Contradictory benchmark sampling identities pass validation** — `EvidenceContractValidator.cs:562` checks only `benchmark.samplingUnit`. The sampling unit inside `comparability` is checked for presence and equality with `comparedTo`, not against the frozen unit (lines 585–602). Consequently, changing both comparability keys to `samplingUnit: "run"` while retaining outer `samplingUnit: "program-pair"` returns no violation. The existing negative control changes only the outer field (`EvidenceContractTests.cs:384–388`). This accepts contradictory statistical identities despite the frozen program-pair requirement (`contract.json:200–202`). — **Fix:** use one sampling-unit field, or require every representation to equal the frozen value. Add a control where both comparison keys agree on the wrong unit.

5. **[MINOR] Required non-hash identities accept booleans and nonempty objects** — Required pair fields are checked through `IsBlank` (`EvidenceContractValidator.cs:571–574`), which treats Boolean values and nonempty objects as present (lines 634–640). Thus setting `calorPath: false`, `pairId: {"x":1}`, or `reviewer: true` in the positive benchmark fixture passes. The wrong-type control exercises only an empty SHA-256 object (`EvidenceContractTests.cs:449–453`). — **Fix:** define field-specific JSON types and enforce nonempty strings for IDs, paths, and reviewer identities. Add Boolean and nonempty-container controls, rather than relying on emptiness checks.

## Verdict

1 BLOCKING / 3 MAJOR / 1 MINOR.
## Dispositions

| # | Severity | Disposition | Change |
|---|---|---|---|
| 1 | BLOCKING | **Accepted, fixed.** Reproduced by reasoning: after the write-back, `Contract()` is FROZEN, so the proposed-state controls changed meaning and `FrozenContract()` appended a second `1.0.1`. | New in-memory `ProposedContract()` / `ProposedInventory()` fixtures undo any committed write-back; every proposed-state control and `FrozenContract()` build from them, and `ProposedPacketIsValid` guards the fixture. **Verified:** the write-back was simulated on disk (amendment `1.0.1`, acceptance record, FROZEN/MET/ACCEPTED, inventory `1.0.1`, document header, re-hash) and the full EvidenceContract suite passed 163/163 against it; the proposed packet was then restored and passed 163/163 again. |
| 2 | MAJOR | **Accepted, fixed.** | `ValidateTerminalRecord(contract, inventory, record)` derives the required subjects (every inventory artifact and `gate:#<issue>` for every child except #1408) and fixes criticality from the contract: every subject is release-critical and record flags are ignored. Missing subjects fail success (`T001`); duplicate or unknown subjects and `HISTORICAL-ONLY` on an artifact not classified historical-only are malformed (`T003`). Success under the deviation needs the verbatim `publishedLimitation` (new contract field) and `epicIndependentAdjudicationMet = false` (`T002`). Controls for each bypass Codex named. |
| 3 | MAJOR | **Accepted, fixed.** Verified: `build-z3.yml` creates/updates the public release and uploads assets. | `z3-release-binaries` regeneration now re-materializes the pinned assets with `gh release download` and verifies them against `.github/z3-binaries-4.15.7.sha256`; rebuilding is publication and needs an amendment. The same audit found two more publishing regeneration paths, also fixed: `release-quality-reports` no longer says "re-run publish-nuget.yml" (its publish job pushes), and `website-deployment` now builds without deploying. `publicationSeparation` adds that pinned inputs are re-materialized, never rebuilt, and that no regeneration command may run a workflow that pushes, releases, uploads, or deploys. |
| 4 | MAJOR | **Accepted, fixed.** | `E010` now also requires the comparability key's `samplingUnit` to equal the frozen `program-pair`; control where both keys agree on `run`. |
| 5 | MINOR | **Accepted, fixed.** | New `pairRowStringFields` and `pairRowTypeRule`: IDs, paths, hashes, failure behavior, disposition, and reviewer are non-empty strings; the other required fields are non-empty strings, objects, or arrays (never booleans or numbers). Controls for boolean, number, and non-empty-object values. |

No objection in this round was rejected; none concerned a maintainer decision.

**Tests after fixes:** `dotnet test tests/Calor.Compiler.Tests/ --filter "FullyQualifiedName~EvidenceContract"`
— 163 passed, 0 failed, on the committed (PROPOSED) packet and on a simulated acceptance
write-back. `eng/test-manifest.json` Calor.Compiler.Tests `expectedTotal` 12217 → 12231. Zero
build warnings. `sha256.json` re-hashed.
