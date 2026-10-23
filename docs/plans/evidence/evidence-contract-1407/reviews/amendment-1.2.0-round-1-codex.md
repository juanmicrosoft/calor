# Amendment 1.2.0, S1 conforming re-run and C2 determinism budget — review round 1 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...4da0a53b` (PR #1484). Brief: a hostile check of amendment 1.2.0. It covered six points: (1) laundering, meaning whether run 2 could erase run-1 findings or be used to run until green; (2) scope creep beyond decisions A and B; (3) ceiling changes beyond A and B, and how tightly B binds to the #1423 candidate; (4) whether the inspection statement is honest; (5) whether the `C011` validator is correct; (6) packet consistency.

## Codex output (verbatim)

1. **BLOCKING — C011 does not bind the S1 exception to its authorized scope.** [EvidenceContractValidator.cs:140](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:140) matches ceiling, issue, value, and amendment correctly, but [line 149](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:149) only checks that scope and conditions are nonempty. Replacing them with `"Repeated selective sweeps are permitted"` and `["Choose the better run"]` still satisfies C011. The controls test missing or blank restrictions, never weakened restrictions. Bind the authorization’s substantive restrictions to registered constants or structured fields, and add discriminating mutation controls. The current prose prevents laundering; the validator does not preserve that protection.

2. **BLOCKING — Charge-rule validation does not preserve the candidate restriction.** [EvidenceContractValidator.cs:173](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:173) matches only the rule’s identifier, destination, source, and amendment. [Line 179](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:179) accepts any nonempty work and rule text. Keeping that tuple while changing the work to all determinism runs and removing the candidate, ledger, and execution-count restrictions remains valid. Consequently, C011 cannot distinguish decision B from a broader reallocation. Register and validate those restrictions, with controls for other commits, controls, and additional executions within one regeneration.

3. **NON-BLOCKING — Duplicate detection uses inconsistent identities.** Per-PR matching ignores `issue` at [EvidenceContractValidator.cs:142](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:142), while the duplicate key includes it at [line 158](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:158). A duplicate #1473 exception with a different issue passes both checks. Charge rules also lack validator-level duplicate detection, although the committed-packet `Assert.Single` catches identical duplicates. Use the same effective identity for matching and deduplication, and add both controls. Duplicate absolute ceilings do not themselves authorize extra capacity.

Otherwise, the submitted authorization text stays within A/B: run-1 findings and row statuses remain retained; restarts consume the fixed allowance; harness changes require another amendment; disagreements become findings. B binds its charge to the manifest candidate and regeneration ledger, shares the existing 1,500 minutes, and adds no ceiling capacity.

All three packet hashes match; contract, inventory, and latest amendment versions agree. C010 fields are complete. Base ceilings and existing rules are unchanged. The ProposedContract sentence restoration matches the original child closure text. The inspection flag honestly records decision-bearing inspection, and the branch evidence supports the two stated S1 triggers.

These validator findings follow from source inspection and read-only predicate reproductions; I did not execute the .NET tests.

REQUEST CHANGES

## Disposition

REQUEST CHANGES. Two BLOCKING findings and one NON-BLOCKING finding; all three are fixed in the next commit.

1. **BLOCKING, fixed.** C011 now binds a per-gate exception's text. A new `EvidenceContractValidator.TextSha256` computes the SHA-256 of `scope`, each condition in order, and `justification`, joined by LF with CRLF normalized. `RegisteredCeilingExceptions` records that hash (`1a1264e7…85f98`) and `addedExecutions` 1,508 for `s1-generated-cases` on issue #1311. Any weakened, dropped, added, or reordered condition fails C011, as does a replaced scope, a different justification, or a different or missing `addedExecutions`. New controls: `weakened-condition` (the reviewer's "Choose the better run"), `dropped-condition`, `added-condition` ("Repeated selective sweeps are permitted"), `reordered-conditions`, `replaced-scope`, `other-justification`, `other-added-executions`, `no-added-executions`.
2. **BLOCKING, fixed.** A charge rule's `work`, `rule`, and `justification` hold the #1423-candidate binding, the ledger requirement, and the one-execution-per-regeneration limit. They are now bound by `TextSha256` (`c12f5d86…36269`) in `RegisteredChargeRules`. New controls: `broadened-work` (every protocol execution), `dropped-candidate-binding` ("any commit" instead of "the #1423 manifest"), and `other-justification`.
3. **NON-BLOCKING, fixed.** Matching and duplicate detection now use the same identity: ceiling and PR for a per-PR exception, ceiling and issue for a per-gate exception. New control `duplicate-other-issue` adds a copy of the #1473 exception with a different issue. Charge rules are de-duplicated by id in the validator, with a new `duplicated` control.

§12's `C011` row describes the hash binding. `eng/test-manifest.json` is +14. The scoped test command passes 273 of 273 with zero warnings.
