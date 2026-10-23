# Amendment 1.1.0 — Codex review, round 1

**Reviewer:** Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family.
**Input:** the full amendment diff against `origin/main` (`585b45d6`), first commit of the branch.
**Prompt:** hostile review — does the amendment weaken a guarantee beyond the five approved decisions,
open a release bypass, launder anything, or retroactively change a frozen rule after inspection?
**Result:** 4 BLOCKING, 3 MAJOR, 2 MINOR. Codex confirmed the three packet hashes match.

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `buildInputs: ["website/"]` leaves out the deploying workflow, the actions it calls, and the build environment, so a workflow change can alter output while the diff reports only docs. | **Fixed.** The workflow and every script, action, and configuration file it invokes are now `machinery`, never documentation. A machinery change since the base is allowed only through a merged, maintainer-reviewed PR listed in the audit record (`machineryChanges`); the verifier refuses an unlisted change and any build input outside `website/` and the enumerated machinery. Requiring zero machinery change would make the path unusable (R2's own PR changes the workflow after the base), so this rests on reviewed merges to protected `main`, which the contract states. `D003` checks the list's shape; `C013` keeps the workflow in machinery. |
| 2 | BLOCKING | The audit record was committed after the run, so a deploy could be published with no durable record ever. | **Fixed.** Two reviewed phases: an `authorized` record committed on `main` before dispatch (the run refuses unless it matches the run's own recomputation), then a `deployed` completion adding the run id. An uncompleted record cannot be a base. `D001` enforces the status/run-id pairing. |
| 3 | BLOCKING | The `not`/`no`/`without` negation allowance lets "not independently verified to be incorrect" through. | **Fixed.** New `wordingRule`: a changed path may not contain the phrase family at all, negated or not; the built site still passes the #1410 scan. |
| 4 | BLOCKING | `reviewedInPr: 0` and `#PRNUM` leave the amendment without its reviewed PR; `C010` accepts 0. | **Deferred to the maintainer, by instruction.** The placeholders are deliberate: the PR number does not exist until the PR is opened, and the maintainer replaces both, re-runs the hash step, and re-runs the tests before merge. The PR body lists this as a merge precondition. Tightening `C010` to `> 0` would make this commit fail its own positive control, so it is not done here. |
| 5 | MAJOR | Path matching admits an allowlisted `.mdx` symlink to denied content, and MDX is executable (`compileMDX`). | **Fixed.** `fileRule`: every added or modified path is git mode `100644` (no symlink, submodule, or executable). `mdxRule`: no `import`/`export`, no `{…}` expression, and no JSX element other than the registered MDX components and plain HTML (minus `script`, `iframe`, `object`, `embed`, `style`, `link`) in changed lines outside code. The audit record carries `fileModeCheck` and `mdxCheck` (`D004`). |
| 6 | MAJOR | `C013` accepted `website/content/**/*.mdx`, removal of every denial but data, and any attestation text. | **Fixed.** `C013` now binds to the rule 1.1.0 registered: the allowlist must be a subset of it, the denylist a superset, `website/` diffed, the workflow machinery, and the pre-contract base and attestation unchanged. Widening the rule needs a validator change. Mutation controls for each. |
| 7 | MAJOR | `D001` accepted any version at or above 1.1.0, including an invented one; the proposed-state fixture kept the 1.1.0 rules. | **Fixed.** The record's version must be in the amendment log and at or after the introducing amendment, which must itself be logged (control: `9.9.9`). `ProposedContract` now also removes every rule 1.1.0 added. |
| 8 | MINOR | The inventory-path control would pass without the inventory check, since every inventoried website path is also denied. | **Fixed.** `InventoriedPathIsRefusedEvenWhenTheAllowlistMatches` adds an otherwise allowed page to an inventory artifact and requires `D003`. |
| 9 | MINOR | Wrong-type `pendingUpdates` or `defectResolutions` became empty lists (false pass). | **Fixed.** Both must be arrays when present (`I013`); controls for each. |

Also in this round, at the maintainer's direction (same session): **decision 6**, the per-PR ceiling
exception for B1 PR #1473 (1,520 lines, `capacity.exceptions`), with `C011` shape checks and controls.

Test count after round 1: EvidenceContractTests 166 → 256 (+90). Round 2 replaced the two-phase
record of #2 with a single record committed before publication (see round 2).
