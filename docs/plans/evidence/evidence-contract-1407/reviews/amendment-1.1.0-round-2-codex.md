# Amendment 1.1.0 — Codex review, round 2

**Reviewer:** Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family.
**Input:** the full amendment diff against `origin/main` after round 1 (`d34369e0`), including
decision 6 and the round 1 record. Finding 4 of round 1 (the `reviewedInPr`/`#PRNUM`
placeholders) was declared deferred to the maintainer.
**Result:** 1 BLOCKING, 2 MAJOR, 1 MINOR.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | Round 1 #2 incomplete: the deploy could publish on an authorization without its run id, and the completion PR might never merge. | **Fixed.** One record, keyed by run id, committed before anything is published. The build job computes it (including its own run id) and uploads a draft; the maintainer commits it through a reviewed PR; the deploy job waits (for example on a required-reviewer environment) and refuses unless the record for its run id is on protected `main` and matches its recomputation and the built artifact. The two-phase status field is removed; `D001` requires the run id. |
| 2 | MAJOR | The MDX rule checked only added or modified lines, so deleting a code fence could activate unchanged executable text. | **Fixed.** `mdxRule` now parses each changed `.mdx` file in full as it stands at the deployed commit. |
| 3 | MAJOR | `C011` accepted any exception attributed to a logged amendment, so #1473's raise could grow or another PR could be added without a new amendment. | **Fixed.** `C011` accepts only exceptions registered in the validator by an amendment (1.1.0: `pr-size`, #1473, 1,520). Controls: other value, other PR, other ceiling, unlogged amendment, no justification, an added exception. |
| 4 | MINOR | The proposed-state fixture kept amended sentences that reference the removed 1.1.0 rules. | **Fixed.** Sentences 1.1.0 appended to frozen text are marked `(amendment 1.1.0)`; the fixture strips them (G3's closure evidence was split so the original sentence is untouched), and `ProposedPacketIsValid` asserts the fixture mentions none of the 1.1.0 rules. |

Test count after round 2: EvidenceContractTests 166 → 254 (+88).
