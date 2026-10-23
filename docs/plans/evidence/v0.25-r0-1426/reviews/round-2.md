# Codex adversarial review, round 2 (#1426 R0)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), cross-family adversarial review. Not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `03f6f1c2`, with round 1 and its dispositions in the tree.
- **Verdict:** REQUEST-CHANGES (round-1 items 1, 6, 8 verified FIXED; 1 BLOCKING and 6 MAJOR remaining or new).

## Findings (verbatim)

Round-1 verification:

1. **FIXED — MCP defaults.** `docs/plans/evidence/v0.25-r0-1426/reproduce/reproduce.py:121` now supplies only source and the selected mode. The custom-module-name witness is separate.
2. **NOT FIXED completely — READY evidence.** Typed counts and frozen vocabularies are fixed, but arbitrary existing files still qualify as examples and review records (`tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:309`). See finding 4.
3. **NOT FIXED completely — amendments.** Added cases and exact field changes are checked, but historical removals remain permanent exemptions, and family moves miss readiness invalidation (`InteropScopeValidator.cs:163`, `:200`). See finding 3.
4. **NOT FIXED completely — evidence integrity.** The committed result/output hashes match, and behavioral equality is recomputed. Producers remain unsealed (`InteropScopeValidator.cs:251`). See finding 6.
5. **NOT FIXED — mandatory matrix.** Additional witnesses help, but mandatory dimensions still live in broad, mutable placeholders (`docs/plans/evidence/v0.25-r0-1426/scope.json:945`, `:1070`). See finding 5.
6. **FIXED — collision witness.** The retained output has the user `_hoist000` at line 25 and a generated binding with that name at line 28; Grid’s parameters also match the generated array identifiers (`docs/plans/evidence/v0.25-r0-1426/generated/F5-ARRAY-04.mcp-default.calr.txt:16`).
7. **NOT FIXED completely — MET.** An amendment is now required, but condition-specific closure evidence is still unchecked (`InteropScopeValidator.cs:232`). See finding 1.
8. **FIXED — website arithmetic.** `docs/plans/v0.25-interop-scope-and-baseline.md:268` correctly identifies four remaining non-parse rejections.

Remaining and newly found defects:

1. **BLOCKING — S009 still accepts fabricated gate satisfaction.**  
   `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:236` accepts any existing file for every condition; `:238` accepts any closure-time string ending in `Z`. A correctly registered amendment can set `gateDecision: "R0-MET"`, point every condition at the planning document—which says #1413 remains open—and register two empty `not-measured` placeholders with roles `interaction` and `doc-example`. S009 accepts this without actual closure, capacity acceptance, deviation acceptance, or blocker mapping. The test helper already supplies the planning document as every condition’s evidence (`InteropScopeTests.cs:302`).

   **Minimal fix:** validate small, condition-specific retained records, including #1413’s closed state and valid timestamp. Require concrete interaction/example registrations. Add a negative control with the gate amendment present but irrelevant evidence.

2. **MAJOR — Acceptance obligations can shrink without changing the frozen denominator.**  
   `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:47` tracks only family, role, expected outcome, baseline status, and fixture hash. Observation lists and the prose defining placeholder requirements are neither frozen nor amendment-tracked. For example, delete three observations from F6-REPORT-03, leaving only CLI success (`docs/plans/evidence/v0.25-r0-1426/scope.json:904`). The CLI/MCP comparison disappears, yet the remaining observation matches sealed results, S002/S013 accept it, and the pinned denominator stays unchanged.

   **Minimal fix:** freeze a digest of each case’s acceptance requirements and observation specification; amend exact old/new digests and invalidate affected READY records. Include fixture identity in the tracked contract.

3. **MAJOR — Amendment history still grants permanent removal exemptions and misses the source family of moves.**  
   `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:163` unions removals across all amendments. Remove F6-REPORT-10 in one amendment, restore it later, then remove it again: the first removal still authorizes the final deletion. Re-registration of an existing ID is silently ignored by `TryAdd` at `:145`.

   Separately, move F4-ITER-04 to F5 through an exact family amendment. The old F4 READY record survives: `:200` examines current F4 cases and removed cases, so the moved case disappears from its stale check.

   **Minimal fix:** apply amendments as ordered active/removed state transitions. Validate restoration explicitly, and record both old and new families as affected by a move. Add controls for both sequences.

4. **MAJOR — READY can contain no website examples or review evidence, and unmeasured rows need only an assertion.**  
   `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:309` accepts `websiteExamples: ["README.md"]`; `:313` also accepts README as a review record. `:316` accepts the bare `https://github.com/` as an approval URL. These satisfy existence checks without supplying the required artifacts.

   Furthermore, the positive READY helper marks every F4 case passed (`InteropScopeTests.cs:322`), including unfixtured F4-ITER-04, without any retained candidate result. This contradicts the claim that unmeasured rows block readiness (`docs/plans/v0.25-interop-scope-and-baseline.md:395`).

   **Minimal fix:** restrict example/review paths to their intended retained artifact locations, validate a repository PR-review URL, and require a candidate-bound result reference for each case. Keep the historical R0 baseline unchanged.

5. **MAJOR — Mandatory matrices remain under-registered.**  
   `docs/plans/evidence/v0.25-r0-1426/scope.json:945` registers MCP migration with “outcome, provenance and partial failure,” but does not separately require defaults, precedence, aggregation, and schema compatibility. F6-REPORT-12 (`:1070`) compresses enabled/disabled behavior across all surfaces into one unchecked prose obligation. A single `passed` assertion can satisfy either placeholder despite missing matrix cells required by [#1144](https://github.com/juanmicrosoft/calor/issues/1144).

   Other remaining omissions include the explicit string-return target row required by [#906](https://github.com/juanmicrosoft/calor/issues/906), async unsupported local functions required by [#847](https://github.com/juanmicrosoft/calor/issues/847), and a declared supported/unsupported array boundary required by [#1132](https://github.com/juanmicrosoft/calor/issues/1132). F5-ARRAY-05 samples several forms but declares all native without defining that boundary.

   **Minimal fix:** add compact required-cell lists beneath existing cases and require one result per cell. They may remain `not-measured` at R0; broad prose must not substitute for frozen acceptance cells.

6. **MAJOR — Producer changes leave the sealed baseline apparently valid.**  
   `docs/plans/evidence/v0.25-r0-1426/scope.json` seals results and generated outputs only. `tests/Calor.Compiler.Tests/InteropScope/InteropScopeTests.cs:242` pins those seals and the denominator; neither reproduction producer participates. Changing `reproduce.py` or `ProbeRunner.cs.txt` can break regeneration or change oracle behavior while every integrity check still accepts the existing packet.

   **Minimal fix:** include both producer hashes in the immutable evidence seal and add one producer-drift negative control.

7. **MAJOR — Tests reject the promised acceptance write-back and later amendments for the wrong reason.**  
   `tests/Calor.Compiler.Tests/InteropScope/InteropScopeTests.cs:52` permanently asserts PROPOSED, while the documented next write-back must set FROZEN (`docs/plans/v0.25-interop-scope-and-baseline.md:42`). Lines 53–55 likewise reject every future legitimate MET/closed-blocker packet. Amendment controls append hard-coded version `1.0.1` to the live packet (`InteropScopeTests.cs:77`), so a valid committed amendment makes those controls fail on version ordering.

   **Minimal fix:** use a fixed initial-state fixture for mutation controls; validate the live packet through lifecycle-aware assertions. Add a positive FROZEN/NOT-MET control and a positive, fully evidenced MET control.

**REQUEST-CHANGES**

## Dispositions (fixed in the commit after this record)

1. **Fixed.** MET now requires, per condition, a record at `gate/<condition>.json` that names the condition and says SATISFIED; the #1413 record must say `CLOSED` with a parseable UTC time. `interaction` and `doc-example` rows must be measured and carry a fixture or observations. New controls: planning document as evidence, #1413 record still open; a positive fully evidenced MET control.
2. **Fixed.** `requirementSha256` (fixture path, note, cells, observations) is a tracked field in `denominatorV1`; changing it needs an exact amendment change. New control: dropping an F6-REPORT-03 observation fails S004.
3. **Fixed.** Amendments apply in order as state transitions. Restoring a removed case is explicit (`addedCases`), re-registering an active case or removing an inactive one fails, and a later deletion needs its own removal. A move marks both the old and new family stale. New controls for both sequences.
4. **Fixed.** Examples must be existing files under `website/content/`; review records must exist under `docs/plans/evidence/**/reviews/`; the approval URL must be a PR review URL of this repository; every case row needs a retained `evidence` file under `docs/plans/evidence/` and a passed result per registered cell. Existence is still all a test can check; the approver's review judges content.
5. **Fixed in part.** Required cells now sit under F2-INTERP-08 and F6-REPORT-04 to 07, 10, 12 and 13, and READY needs a passed result per cell. New rows: F2-INTERP-10 (string return target), F3-LOCAL-05 (async local functions), F5-ARRAY-06 (declared array boundary, a decision row). Cross-feature and documentation rows remain an open gate condition.
6. **Fixed.** `evidenceSeal.producers` holds the SHA-256 of `reproduce.py` and `ProbeRunner.cs.txt`; the pinned test seal covers it. The baseline was regenerated with the current producers. New control: an edited producer fails S013.
7. **Fixed.** The committed-packet test is lifecycle-aware; amendment controls derive the next version from the packet; new positive controls cover FROZEN/NOT-MET and a fully evidenced MET packet.
