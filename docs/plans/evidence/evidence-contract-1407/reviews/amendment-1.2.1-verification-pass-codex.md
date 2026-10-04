# Amendment 1.2.1, S1 run-2 harness ceiling constant — verification pass (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...135db8fc` (PR #1488). This is a verification-only pass, not a review round. It checked five things:

1. The round-1 NIT is fixed, and the test manifest is main's total plus 7.
2. Only condition 4 of the #1311 exception changed in substance, with no budget increase.
3. The registered framed text hash and the `sha256.json` hashes match the packet.
4. No placeholder remains; the amendment names PR #1488; both versions are 1.2.1; the markdown log row matches the JSON.
5. The inspection statement is honest.

## Codex output (verbatim)

No BLOCKING, NON-BLOCKING, or NIT defects found at `135db8fc`.

1. Round-1 wording fixed in both files. Manifest is **12,511 + 7 = 12,518**; only the compiler project changed.
2. Only condition 4 changed in substance. Scope, exception justification, **3008**, **1508**, other conditions, ceilings, and rules are unchanged. No budget increase.
3. Recomputed framed hash matches `d9a57518…f896bac`. All three LF-normalized packet hashes match.
4. No placeholders remain. Both versions are **1.2.1**; JSON and markdown agree on **PR 1488 / 2026-10-03T23:29:07Z**.
5. Inspection disclosure is consistent with 1.2.0 and the cited harness/run-1 evidence. Personal inspection cannot be independently proven from repository files.

Read-only verification; xUnit was not run.

VERIFIED

## Disposition

VERIFIED, with no findings. This commit adds only this record. The scoped test command passes 311 of 311 with zero warnings: `dotnet test tests/Calor.Compiler.Tests/ --filter "FullyQualifiedName~EvidenceContract|FullyQualifiedName~SoundnessRegistration"`.
