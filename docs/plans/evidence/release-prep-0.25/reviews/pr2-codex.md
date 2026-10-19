# Release prep 0.25, PR 2 (#1548): Codex reviews

**Budget:** at most 3 rounds plus 1 verification pass. **Used:** 2 invocations (round 1, round 2).
Each invocation: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`, prompt on
stdin. Raw final messages: `pr2-round-1-codex.raw.txt`, `pr2-round-2-codex.raw.txt`.

## Round 1 (reviewing f07d27df5): REQUEST-CHANGES, 1 MAJOR

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MAJOR | On Windows `dotnet --version` ends in CRLF, and `$(...)` keeps the CR. The `verify-release.yml` `install-and-verify (win-x64)` job would fail with the correct SDK. | **Fixed** (9bb247ca2). Every require step compares `sdk="$(dotnet --version \| tr -d '\r')"`. `ReleaseSdkPinTests` requires that form and `shell: bash`. Checked locally with LF and CRLF input. |

Codex also reported as passing: YAML parsing, the font source and export checks, font bytes and
CSS parity, and the release-credential check.

## Round 2 (reviewing 9bb247ca2): APPROVE

No findings. Codex verified the CRLF fix with LF and CRLF input, YAML validity, the SDK guards,
the font hashes and fallback metrics, base-path handling, and the font check's negative controls.

No verification pass was needed: round 2 approved with no findings to verify.
