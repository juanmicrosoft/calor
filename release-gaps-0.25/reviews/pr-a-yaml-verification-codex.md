# PR A, exception EXC-RELEASE-GAPS-A-YAML: Codex verification-only pass

Granted by @juanmicrosoft on 2026-10-18: one fix (the parser-based rewrite) plus exactly one
verification pass. Reviewing 4e4f6d916. Raw final message: `pr-a-yaml-verification-codex.raw.txt`.

**Verdict: APPROVE.** No BLOCKING or MAJOR findings.

**Codex verified:**

- All 27 Python tests pass with PyYAML 6.0.2, with no skips.
- The multiline flow-mapping finding and every earlier regression are closed.
- A missing parser fails with `W012`.
- CI installs the pinned parser unconditionally and runs both commands. The Linux wheel hash
  matches PyPI.
- The maintainer commands match the code.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | MINOR | `!!set`-tagged collections (`env: !!set {K: a, K: b}`) skip the duplicate-key check, and `strings()` does not walk sets. Codex showed no way to reach the credential: `W004` still blocks a tagged environment. | **Open follow-up.** The exception allowed one fix only, so this is not changed here. Proposed fix: reject every non-default collection tag (`!!set`, `!!omap`, `!!pairs`) with `W012`, and add a regression test. |
| 2 | NIT | The README said names are compared without case, but the allowlist comparison is exact. | Fixed in the README: only the `nuget-release` checks and the key search ignore case. The allowlist match is exact, so `GITHUB-PAGES` fails closed with `W004`. Documentation only. |
| 3 | NIT | A defined merge key produces `W012` during construction, not the documented `W009`. | Fixed in the README: merge keys fail closed with `W012`, while anchors and aliases are reported as `W009`. Documentation only. |
