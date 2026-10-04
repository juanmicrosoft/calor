# Amendment 1.3.1 extension (#1502: three registered changes, D-NUM-WHILE-BOUND) — round 1 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `origin/main...cc22d543` (PR #1504). The delta since the approved state is `759bf1e4..cc22d543`.

The prompt gave the two new facts and the maintainer decision of 2026-10-04 verbatim. The facts were #1502's linux-arm64 CI failure and the false proof that the revert reopens. The decision was "Decide by operand widths" plus a whole-loop `Unsupported` result. The prompt asked the reviewer to check five things:

- fidelity of #1502's text to exactly the three changes (a)–(c);
- soundness of the width rule's "stronger only where proven" claim, including shifts, division, negation, unsigned subtraction, and mixed signedness;
- how `D-NUM-WHILE-BOUND` is recorded under the discovery and slot rules;
- honesty of `weakens`, the justification, and the inspection statement;
- validator changes and packet hygiene.

## Codex output (verbatim)

VERDICT: APPROVE

No BLOCKING, MAJOR, MINOR, or NIT findings.

- [contract.json:337](docs/plans/evidence/evidence-contract-1407/contract.json:337) binds #1502 to exactly (a)–(c), the specified base, required tests, unknown → Unsupported, determinism, 600 non-test lines, an APPROVE-only verification pass, and freeze.
- The width rule requires the actual C# promotion and an operation-specific range proof. `i32 × u32` fits `long` over both full operand ranges. It does not authorize overlooking unsigned subtraction underflow, signed negation overflow, shift semantics, or separate division guards.
- `D-NUM-WHILE-BOUND` is recorded and owned by #1502. Demotion uses the existing R-NUM PR slot; the seventh slot remains restricted to the two obligation findings. #1503 and all pre-1.3.1 exception texts are unchanged.
- Validator registrations correctly distinguish `revertOnly`, scope prefixes, bases, and findings. Negative controls check a clean pristine contract and their own subjects.
- Packet and registered text hashes match; no duplicate JSON keys were found. Timestamps and document text agree. Manifest accounting is exactly **12713 → 12724 (+11)**.

Execution limitation: the scoped `EvidenceContractTests` command failed before running tests because the read-only sandbox blocked MSBuild’s temporary-directory creation. Validator review was by source inspection; hashes and accounting were checked directly.

## Resolution

No change was requested. A verification-only pass on the extension follows. Locally, `dotnet test tests/Calor.Compiler.Tests/ --filter "FullyQualifiedName~EvidenceContract|FullyQualifiedName~SoundnessRegistration"` gives 392 passed, 0 failed, and 0 warnings.
