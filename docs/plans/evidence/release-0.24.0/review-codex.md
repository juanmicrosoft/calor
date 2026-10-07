# Release 0.24.0 prep — Codex review of the public text

- **Scope:** `git diff origin/main...HEAD` of branch `release/0.24.0-prep` at commit `1854480b`
  (version bump, CHANGELOG.md 0.24.0 section, `website/content/changelog.mdx`, WhatsNewBanner,
  `website/tests/public-claims.spec.ts`).
- **Command:** `codex exec -s read-only --ephemeral -c model_reasoning_effort="high" "<prompt>" < release.diff`
- **Date:** 2026-10-07. One review round, as the release-prep task specifies.
- **Prompt focus:** hostile check for overclaiming (independent adjudication or verification,
  forbidden by the evidence contract §9 independence deviation; soundness proofs; benchmark
  advantage or numbers), inaccurate release history (0.22.0 tagged and GitHub pre-release but not
  on NuGet; 0.23 planning-only; NuGet newest 0.21.0), advisory facts against the S1 results, the S2
  dispositions, and `baselines.N1.artifact` in `docs/plans/evidence/s2-1413/dispositions.json`,
  missed version spots (create-release skill §3), internal contradictions, and CLAUDE.md public
  writing rules.

## Verdict

**REQUEST-CHANGES** (8 findings: 6 MAJOR, 2 MINOR, 0 BLOCKING). Codex confirmed that the release
history, the required totals, the fix-versus-demote assignments, and the cited diagnostics match
the records; that no required advisory mechanism is missing; that no independent-verification or
whole-compiler soundness claim appears; and that no live 0.22.0 version spot remains.

## Findings and resolutions

| # | Severity | Finding | Resolution |
|---|---|---|---|
| 1 | MAJOR | "What to do" said 0.21.0-generated C# "keeps any runtime check it removed", which reverses the risk. | Rewritten: that C# still lacks any check 0.21.0 removed; regenerate it with 0.24.0. |
| 2 | MAJOR | All 3 string interface cases were described as throwing; `IMPL-ASSUMPTION-FORMS-003` (`(|| (! (isempty s)) (== s ""))`) is false at `s = null`, not throwing. | Split: 2 cases throw at `s = null`, 1 case is false at `s = null`; the area intro now says "throw on, or reject". All 3 remain demoted to `Assumed` with `Calor0819`. |
| 3 | MAJOR | The #1493 detail entry said non-ASCII identifiers "used to become one solver variable", contradicting "potential, not observed". | Now "could become one solver variable, which could in principle prove a false contract. This was not observed in practice." |
| 4 | MAJOR | The run-to-run determinism entry kept solver timing figures (3x work, 30–65 ms, 1.4 ms). | Figures removed from the public notes; the 5-second configured timeout stays. |
| 5 | MAJOR | Cache format 1.19/1.20/1.21/1.22 each appeared as the release's format. | Every mention now says the format changes and that 0.24.0 uses format 1.22 (matches `VerificationCacheEntry.CurrentFormatVersion`). |
| 6 | MAJOR | "This does not track statements that throw before the obligation" was stale after R-OBL-RESIDUALS (#1503). | Replaced with a pointer to the "Two more kinds of unreachable counterexample are withheld" entry, which covers those statements. |
| 7 | MINOR | "Twice, with identical results" overstated repeatability: one counterexample differed between runs (S1 results, determinism observation). | Now: same verdicts and findings in both runs; one counterexample differed, and both violated the property. |
| 8 | MINOR | The #1422 benchmark-workflow entry packed five failure conditions into one sentence. | Split into a bulleted list. |

The website changelog section was regenerated from the CHANGELOG section after the fixes; the two
0.24.0 sections are byte-identical. Website build and all 57 Playwright tests (including
`public-claims.spec.ts`) pass after the fixes.
