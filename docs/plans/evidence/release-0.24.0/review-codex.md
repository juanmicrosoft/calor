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

## Verification-only pass on the fixes (Codex, run by the session lead)

Reviewer: codex-cli 0.159.2, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`. Reviewed: the second commit (e9774f67).

All eight findings are **RESOLVED**. The current 0.24.0 sections are byte-identical in [CHANGELOG.md](CHANGELOG.md:7) and the [website changelog](website/content/changelog.mdx:13). References below give their respective line numbers.

| # | Status | Evidence in current public text |
|---|---|---|
| 1 | RESOLVED | Lines 56–58 / 62–64 say generated C# “still lacks” removed checks and instruct users to regenerate it. Matches N1’s immutable-artifact limitation. |
| 2 | RESOLVED | Lines 39–47 / 45–53 distinguish two throwing cases from one false-at-null case. All three become `Assumed` with `Calor0819`, matching S1 and both S2 baselines. |
| 3 | RESOLVED | Lines 161–164 / 167–170 describe a possible Windows symbol collision, explicitly “not observed in practice,” matching `D-1493`. |
| 4 | RESOLVED | Lines 184–193 / 190–199 remove the 3×, 30–65 ms, and 1.4 ms figures. The configured 5-second timeout remains. |
| 5 | RESOLVED | All four cache-format mentions identify 0.24.0’s format as **1.22**: lines 149, 168, 200, 271 / 155, 174, 206, 277. Matches `VerificationCacheEntry.CurrentFormatVersion`. |
| 6 | RESOLVED | Lines 244–245 / 250–251 replace the stale limitation with a reference to the throwing-predecessor handling at lines 206–229 / 212–235. Matches R-OBL-RESIDUALS. |
| 7 | RESOLVED | Lines 63–65 / 69–71 state identical verdicts and findings while acknowledging one differing counterexample. Matches S1’s determinism observation. |
| 8 | RESOLVED | Lines 92–102 / 98–108 split the benchmark workflow’s failure conditions into bullets. |

**NEW BLOCKING problems: none found.** The public text preserves §9’s reduced-independence limitation and bounded claim cap, disclaims whole-compiler soundness, and makes no new benchmark-advantage claim. Advisory totals and dispositions match S1/S2. All live version locations checked—including the package lockfile, header version, banner, and public-claims test constant—use **0.24.0**; remaining 0.22.0 benchmark references are explicitly historical.

VERDICT: CLEAN
