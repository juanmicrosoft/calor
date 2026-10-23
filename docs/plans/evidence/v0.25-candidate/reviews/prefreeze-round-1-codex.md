# 0.25 pre-freeze PR (#1576): Codex round 1

- **Invocation 1 of at most 4** (fresh budget: 3 rounds plus 1 verification pass).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" -` (prompt on stdin).
- **Reviewed:** the PR's first commit on `origin/main` `a6d5c2f0d`.
- **Verdict:** REQUEST-CHANGES: 1 BLOCKING, 2 MAJOR, 1 MINOR.

Codex also verified: earlier amendment entries and the 0.24 map entry are unchanged, and
`expectedTotal` 14,565 was correct.

## Findings and dispositions

| # | Severity | Finding (summary) | Disposition |
|---|---|---|---|
| 1 | BLOCKING | A READY record only had to name the candidate: family, scope version, cases, cells, tests, reviews, approval, independence and resolved blockers were unchecked (S007 bypass). | Fixed: `ready_problems` ports `InteropScopeValidator.ValidateReady` (S007) to the gate. The record must name its family, a registered scope version and the candidate; every case the T1 register lists for the family needs a passed row and a retained result record (candidate, case, `requirementSha256`, every cell passed); tests, existing website pages, retained reviews, approver URL, reduced independence, every family blocker resolved, and each mapped 0.24 repair's merge commit plus a retained passing witness run on the candidate. 12 controls, one per field, each asserting only its own message. |
| 2 | MAJOR | `acceptancePreconditions` checks were weaker than S014 (requirements, kind, gate and source could change). | Fixed: `preconditions_problems` ports S014: equality with the latest amendment copy, decision linkage, the replaced #1408 record and outcome, A1's blockers, the five `(id, kind, gate, source)` tuples with requirements and retained sources, and the pinned 1.3.0 digest (`PreconditionsDigest130`, reproduced with a System.Text.Json-compatible serializer). Controls: a weakened requirement in both copies, and a packet copy that differs. |
| 3 | MAJOR | A dropped producer or a hollow W1 file passed. | Fixed: exactly the two registered producers; T1 and W1 schemas, six-family coverage with issues, reduced independence, consumed scope path and version. Controls for both. |
| 4 | MINOR | Packet controls did not isolate their guards (post-candidate tampering also tripped the candidate/adjudication equality; invalid JSON masked the immutability guard). | Fixed: seal, producer, W1, precondition and T1 controls mutate before the candidate; the immutability controls use valid JSON at the adjudication; the record's contract hashes are computed from the fixture's own files; every packet and READY control asserts that each violation line names its target. |

`ReleaseGateScopePacketTests`: 22 → 37 cases; test manifest 14,565 → 14,580.

## Codex output (final message, verbatim)

```text
VERDICT: REQUEST-CHANGES

1. BLOCKING — scripts/verify_release_adjudication.py:625: READY bypasses S007. The gate accepts {"candidate":"<candidate SHA>"} as a READY record. It checks neither family identity nor scope version, cases/cells, tests, reviews, approval, independence, or resolved blockers. The passing fixture at ReleaseGateScopePacketTests.cs:160 already exercises this incomplete record. Fix: validate downstream READY records against S007, including requirement digests and mapped witness runs; add independent negative controls for every required field.

2. MAJOR — scripts/verify_release_adjudication.py:534: acceptancePreconditions validation is weaker than S014. Keeping the five IDs while replacing every requirement with "No rerun needed" and changing kind, gate, and source produces no violation. Amendment-copy equality and the pinned 1.3.0 decision are unchecked. Fix: enforce S014's amendment linkage, immutable 1.3.0 copy, required check tuples, retained sources, and release-gap floor.

3. MAJOR — scripts/verify_release_adjudication.py:527: malformed packets pass the new checks. Read-only controls produced zero violations after deleting one required producer from evidenceSeal, or replacing W1 with only scopeVersion and independence. Thus "both producers" and the W1 register's actual contents are unenforced. Fix: require the registered producer paths and validate T1/W1 schemas, family coverage, and consistency with scope. Add controls that mutate packets before freezing the candidate.

4. MINOR — tests/Calor.Compiler.Tests/ReleaseGate/ReleaseGateScopePacketTests.cs:110: packet controls do not isolate their guards. Tampering with generated output after the candidate also triggers packet-directory inequality, so removing seal verification leaves that test green. Appending non-JSON text to the register similarly masks removal of the register immutability guard. Fix: test seal defects before the candidate commit; test immutability with valid JSON and otherwise consistent hashes.

Earlier amendment entries and the 0.24 map entry are unchanged. expectedTotal correctly equals 14,565. Seven selected Python controls and the generator self-test passed; broader execution was limited by the read-only sandbox.
```
