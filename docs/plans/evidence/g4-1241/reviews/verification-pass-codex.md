# G4 verification-only pass — Codex

Reviewed: the round-3 fixes only, `git diff 05df1137..f167a910` (excluding the generated report), against `round-3-codex.md`. Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.

## Result (verbatim)

1. VERIFIED — Missing declared linked inputs now reject the build; the committed-deletion control is discriminating. Root-file deletion and mtime freshness remain accurately documented limitations. `scripts/checkout_compiler.py:87`, `scripts/test_tier2_verification.py:278`, `docs/plans/evidence/g4-1241/README.md:35`, `docs/plans/evidence/g4-1241/reviews/round-3-codex.md:17`.
2. VERIFIED — Independently listed mandatory commands must appear; removing either migrator or either self-test fails the assertion. Each invoked mandatory step is then failed individually with exits 1–3. `scripts/test_tier2_verification.py:337–361`.
3. VERIFIED — The description matches the implemented per-error `file|code|declarationId|message` format, sorting and retaining duplicates. `eng/tier2-fixture-expectations.json:4`, `scripts/fixture_compile_check.py:71–86`.

No regressions found: controls remain discriminating, no new laundering path, and linked-directory checks exclude the root where CI logs are written. Static review only; tests mentally traced.

CLEAN
