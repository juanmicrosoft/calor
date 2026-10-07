# Codex adversarial review, round 3 (#1426 R0)

- **Reviewer:** OpenAI Codex CLI (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`), cross-family adversarial review. Not an independent human review.
- **Input:** `git diff origin/main...HEAD` at `49950031`, with rounds 1 and 2 and their dispositions in the tree. This was the last full round.
- **Verdict:** REQUEST-CHANGES (round-2 items 2, 3, 6 verified FIXED; 2 BLOCKING and 3 MAJOR remaining).

## Findings (verbatim)

Round-2 verification:

1. **NOT FIXED completely — MET evidence.** Closure records now require `CLOSED` and a parseable UTC timestamp, but interaction/example registration remains bypassable. See finding 2.
2. **FIXED — requirement shrinkage.** `requirementSha256` covers fixture identity, note, cells and observations; exact amendment changes are required (`InteropScopeValidator.cs:183`, `:379`). All 49 committed digests match.
3. **FIXED — amendment transitions and family moves.** Active registrations are updated in order, and moves touch both families (`InteropScopeValidator.cs:144–172`).
4. **NOT FIXED — READY evidence.** Paths are restricted syntactically, but candidate binding remains absent and path traversal bypasses those restrictions. See findings 1 and 3.
5. **NOT FIXED completely — mandatory matrices.** Return/async rows and required cells were added, but distinct reporting surfaces remain conflated. See finding 4.
6. **FIXED — producer integrity.** Both producers are sealed and checked (`InteropScopeValidator.cs:274`). Their hashes, results and generated-output seals match.
7. **NOT FIXED completely — lifecycle tests.** Lifecycle assertions and version increments improved, but controls still depend on the live packet’s initial case state. See finding 5.

Remaining findings:

1. **BLOCKING — READY still accepts historical evidence containing no candidate result.**  
   `tests/Calor.Compiler.Tests/InteropScope/InteropScopeValidator.cs:328` only checks that the referenced file exists. The positive helper at `InteropScopeTests.cs:354–357` declares candidate `111…111` and marks every F4 row passed using `baseline-results.json`. That file belongs to commit `03f6f1c2…`, records F4 native-path failures, and contains **no F4-ITER-04 result**. Thus the positive control demonstrates the original defect rather than its repair.

   **Minimal fix:** read a compact retained result record and require matching candidate commit, case ID, registered input/requirement identity, result and required cells. Change the positive helper to supply matching records; reject historical, missing-case and off-candidate records.

2. **BLOCKING — MET’s interaction/documentation condition accepts relabeled reporting observations.**  
   `InteropScopeValidator.cs:258–260` requires only a role, measured status and fixture/observation presence. `InteropScopeTests.cs:338–345` satisfies both roles by cloning F6-REPORT-01: one observation of CLI attribution. It establishes neither a cross-feature interaction nor a website example. Registering equivalent clones through `addedCases` also preserves the pinned denominator while satisfying this check.

   **Minimal fix:** require interaction rows to identify at least two participating families, and documentation rows to identify a retained website example. Include these identities in the requirement digest. Replace the copied-observation positive control and add a negative control for role-only relabeling.

3. **MAJOR — Path traversal defeats the READY artifact restrictions.**  
   The prefix checks at `InteropScopeValidator.cs:328`, `:338` and `:341–342` operate on unnormalized paths. All three of these resolve to the existing repository README and pass the corresponding checks:

   - Example: `website/content/../../README.md`
   - Result: `docs/plans/evidence/../../../README.md`
   - Review: `docs/plans/evidence/v0.25-r0-1426/reviews/../../../../../README.md`

   **Minimal fix:** reject traversal segments or normalize paths before checking containment. Add one negative control for each artifact category.

4. **MAJOR — F6 still lacks separately enforceable obligations for distinct public surfaces.**  
   `docs/plans/evidence/v0.25-r0-1426/scope.json:1047–1052` merges converter-library and SDK coverage into single `defaults`, precedence and schema cells. `:1166–1167` similarly uses one migration mode pair despite CLI migration and MCP migration being separate surfaces. A single cell assertion can satisfy these without covering both implementations, contrary to [#1144’s per-surface matrix](https://github.com/juanmicrosoft/calor/issues/1144).

   **Minimal fix:** qualify the existing cells by surface, distinguishing library/SDK and CLI/MCP migration. Register unavailable combinations explicitly as unavailable rather than implying executable coverage. They may remain unmeasured in this PROPOSED packet.

5. **MAJOR — Legitimate amendments still break controls for unrelated reasons.**  
   `InteropScopeTests.cs:354` hard-codes READY assessment to `1.0.0`; any legitimate later F4 amendment makes `WellFormedReadyRecordPasses` stale. The amendment positive control at `:95–100` also assumes F4-ITER-01 still has expected outcome `preserved`. A previously approved change to that field makes its next amendment start from the wrong value. Dynamic version increments do not resolve these dependencies.

   **Minimal fix:** construct mutation controls from a fixed initial-state packet; retain lifecycle-aware validation of the live packet separately. Ensure positive controls pass after a legitimate case amendment.

**REQUEST-CHANGES**

## Dispositions (fixed in the commit after this record; checked by the verification-only pass)

1. **Fixed.** A READY case row's `evidence` must be a JSON record under `docs/plans/evidence/` whose `candidate`, `caseId` and `requirementSha256` match the READY candidate, the case and its current requirement, with `result` and every registered cell `passed`. The positive control now supplies such records; new controls reject a record for another candidate and the historical `baseline-results.json`.
2. **Fixed.** A MET packet needs an `interaction` row naming two or more registered families (`families`) and a `doc-example` row naming an existing page under `website/content/` (`websiteExample`). Both fields are in the requirement digest. New control: a relabeled interaction row without families fails.
3. **Fixed.** Paths with a `..` segment, a leading `/` or a backslash are rejected before any containment check. New controls for examples, review records and result records.
4. **Fixed.** F6-REPORT-07 cells are split into library and SDK; F6-REPORT-12 cells separate CLI and MCP migration and library and SDK (12 cells); F6-REPORT-13 separates CLI and MCP migration provenance. Unavailable combinations are named as such (`cli-migrate-disabled-or-unavailable`).
5. **Fixed in part.** The positive READY control now assesses under the packet's current `scopeVersion`, and the amendment positive control reads the current value of the field it changes, so legitimate later amendments do not break them. A fixed initial-state packet for every mutation control was not added: it would duplicate `scope.json` inside the 1,500-line ceiling. Controls that depend on a specific case value can still need updating when that case is legitimately amended; that is recorded here as a known limit.
