# Amendment 1.2.0, S1 conforming re-run and C2 determinism budget — review round 2 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...502e09fa` (PR #1484). Brief: verify the round-1 fixes, including an independent recomputation of the registered text hashes, then repeat the full hostile check: laundering, scope creep, ceiling changes beyond decisions A and B, honesty of the inspection statement, `C011` correctness, and packet consistency.

## Codex output (verbatim)

1. **BLOCKING — C011’s hash does not bind the conditions array’s structure.** [EvidenceContractValidator.cs:158](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:158) flattens scope, conditions, and justification through an unframed LF join at [line 266](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractValidator.cs:266). This mutation passes the C011 predicates:

   ```python
   old = exception["conditions"]
   exception["conditions"] = old[:5]
   exception["justification"] = "\n".join(
       old[5:] + [exception["justification"]]
   )
   ```

   The hash remains `1a1264e7865d72da924a1871462705ce66c133aa3bec255bb67d7a6e8fd85f98`. Five registered conditions—including run-1 retention and disagreement routing—have disappeared from the conditions array and moved into justification. The text remains present, but the validator does not enforce the promised exact scope/conditions/justification match. Existing mutation controls miss this boundary-moving mutation. Hash an unambiguous structured representation, or bind fields and ordered conditions separately, and add this control.

2. **NON-BLOCKING — B’s fallback charging language also encompasses ordinary PR controls.** [contract.json:337](docs/plans/evidence/evidence-contract-1407/contract.json:337) and [v0.24-evidence-contract.md:568](docs/plans/v0.24-evidence-contract.md:568) put “every other protocol run,” including controls, on `determinism-compute`. The specified G2 branch’s `protocol.json:404` instead charges pull-request control-mode runs to `ordinary-ci`; dispatched controls use determinism capacity. Narrow the fallback wording to preserve that distinction. This ambiguity adds no capacity, but could move another allocation beyond decision B.

3. **NIT — The CRLF normalization control is nondiscriminating.** [EvidenceContractTests.cs:1176](tests/Calor.Compiler.Tests/EvidenceContract/EvidenceContractTests.cs:1176) replaces LF characters inside the registered strings, but those strings contain zero LF characters. Both input sequences are identical; removing normalization would still pass. Include an explicit multiline input.

The remaining checks passed:

- Independently recomputed both registered text hashes: S1 `1a1264e7…85f98`; charge rule `c12f5d86…36269`. The listed text mutations change the hashes and fail the reproduced predicates.
- Round 1’s charge-rule binding and duplicate-identity findings are fixed. S1 matching correctly distinguishes issue-keyed exceptions from PR-keyed exceptions and rejects different gates, values, ceilings, or amendments.
- The submitted authorization preserves run-1 findings, statuses, attempts, and provenance limitations; limits restarts to the registration policy and fixed allowance; requires another amendment for other harness changes; and routes disagreements to G2/G3.
- Existing ceiling values and classifications are unchanged. B binds qualifying execution to the manifest candidate and regeneration ledger, shares the existing 1,500 minutes, and limits qualification to one execution per regeneration.
- The inspection flag is honestly `true`; branch evidence supports both S1 triggers. The amendment does not resolve O2 completeness or claim to confirm run-1 provenance.
- All three packet hashes match. Contract, inventory, and latest amendment versions agree at `1.2.0`; C010 fields are complete; no placeholders were found. `ProposedContract` removes the amendment additions and restores the affected closure sentences.

Validation used source inspection and read-only hash/predicate reproductions; I did not execute the .NET tests.

REQUEST CHANGES

## Disposition

REQUEST CHANGES. One BLOCKING finding, one NON-BLOCKING finding, and one NIT; all three are fixed in the next commit.

1. **BLOCKING, fixed.** `TextSha256` now hashes a framed encoding. It writes the part count, then for each part its UTF-8 byte length, `:`, the text, and LF, after CRLF-to-LF normalization. Text can no longer move between conditions and the justification without changing the hash. The registered hashes are recomputed: S1 exception `166a99f1…24df77`, charge rule `603d579b…ae03d0`. The round-1 values in the round-1 record are superseded. New control `conditions-moved-into-justification` applies the reviewer's exact mutation: the first five conditions stay, and the rest are joined into the justification.
2. **NON-BLOCKING, fixed.** The charge rule's fallback no longer moves any other allocation. It now says the rule "changes the charge of no other protocol run". Runs on other commits, #1135 executions, control runs, and a second candidate-time execution "are charged exactly as they would be without it (a dispatched run to determinism-compute)". Pull-request control runs therefore stay on `ordinary-ci`, as in the G2 draft. The §9 prose matches. Because the rule text changed, the charge-rule hash is recomputed.
3. **NIT, fixed.** The non-discriminating CRLF assertion is removed. New test `TextHashNormalizesLineEndingsAndFramesParts` checks that CRLF and LF multiline inputs hash alike, and that moving a boundary, merging two parts, or adding an empty part each changes the hash.

`eng/test-manifest.json` is +2. The scoped test command passes 275 of 275 with zero warnings.
