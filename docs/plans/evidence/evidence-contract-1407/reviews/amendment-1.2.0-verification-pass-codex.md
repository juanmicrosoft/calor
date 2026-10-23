# Amendment 1.2.0, S1 conforming re-run and C2 determinism budget — verification pass (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...413c8b85` (PR #1484). This is a verification-only pass, not a review round. It checked six things:

1. Every finding from rounds 1 and 2 is fixed.
2. No placeholder remains, the amendment names PR #1484, and both versions are 1.2.0.
3. The `sha256.json` hashes match the packet files.
4. The registered framed text hashes match `contract.json`.
5. Nothing outside decisions A and B changed.
6. `R006` still accepts the R1 registration's contract version 1.0.1.

## Codex output (verbatim)

No BLOCKING, NON-BLOCKING, or NIT defects found.

All six checks pass:

1. Every rounds 1–2 finding is fixed, including boundary framing, duplicate identities, fallback charging, and the discriminating CRLF control.
2. No specified placeholders remain. The amendment names PR #1484; both contract versions are `1.2.0`.
3. All three normalized packet hashes match `sha256.json`.
4. Both framed text hashes match the validator’s registered constants.
5. Changes stay within decisions A/B; other ceilings, rules, and classifications are unchanged.
6. R006 still accepts R1’s recorded contract version `1.0.1`.

Only the round-3 record changed after approval. Verification used source inspection and independent read-only reproductions; .NET tests were not executed.

VERIFIED

## Disposition

VERIFIED, with no findings. This commit adds only this record. The scoped test command passes 275 of 275 with zero warnings: `dotnet test tests/Calor.Compiler.Tests/ --filter "FullyQualifiedName~EvidenceContract|FullyQualifiedName~SoundnessRegistration|FullyQualifiedName~BenchmarkRegistration|FullyQualifiedName~BenchmarkResults"`.
