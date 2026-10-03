# Amendment 1.1.0, withdrawal of decision 1 — review round 1 (Codex)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed diff: `6f030f6d7902c2e6541538df97c7b02ba7797a19..273747e8` (withdrawal of decision 1 before 1.1.0 merges; maintainer decision 2026-10-03). Scope: anything of decision 1 left behind, faithful restoration of the 1.0.1 text, no change to decisions 2–6, record honesty, `sha256.json` and `eng/test-manifest.json` consistency.

## Codex checks (verbatim excerpt)

```
terminal condition 7 restored: True
other terminal conditions unchanged: True
R2 matches expected source: True
G2 matches expected source: True
G3 matches expected source: True
benchmarkEquivalence unchanged from pre-withdrawal: True
authorityCapacity unchanged from pre-withdrawal: True
determinismRows unchanged from pre-withdrawal: True
platformDeterminism unchanged: True
inventory unchanged: True
```

## Codex output (verbatim)

No findings.

Decision 1’s operative text, validator rules, helpers, and tests are removed. Restored passages match frozen 1.0.1; decisions 2–6 and capacity remain unchanged. The withdrawal and inspection records are consistent. Packet hashes match, and the manifest correctly records 72 removed test cases.

Tests were inspected, not executed, in the read-only workspace.

## Disposition

No findings; no change made. Locally: the filtered run (`EvidenceContract|SoundnessRegistration|ReleaseGate`) passed 210 of 210, and the full `tests/Calor.Compiler.Tests/` run passed with Total 12336, equal to the manifest's `expectedTotal`; build 0 warnings.
