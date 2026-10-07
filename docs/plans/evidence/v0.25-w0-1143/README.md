# 0.25 W0 (#1143): website example checks — evidence

Base: `origin/main` at `fd34f41f`. Baseline: 0.25 R0 §5
(`docs/plans/v0.25-interop-scope-and-baseline.md`, `docs/plans/evidence/v0.25-r0-1426/baseline-results.json`).
Independence: reduced (maintainer + Codex), per R0 §9.

## Mechanism

`calor self-check docs` (already the "spec drift" step of the `test` job in
`.github/workflows/test.yml`, which runs on every PR including website-only ones) now loads
`website/content/**/*.mdx` minus `WebsiteExampleChecker.HistoricalExclusions` (`changelog.mdx`).
No workflow job was added. Convention and findings (`Calor1332`–`Calor1334`) are documented in
`docs/cli/self-check.md` § "Website examples".

Coverage pinned by `WebsiteExampleCheckerTests.PublishedWebsite_IsClean_AndCoverageIsPinned`:
96 pages, 49 complete programs (5 negative, 3 grouped), 2 checked `output` fences,
24 `illustrative` fences.

## Disposition of the R0 §5 list (13 rejected fences)

| R0 fence | Cause | Disposition |
|---|---|---|
| nullability-and-dotnet-interop #1, #3, #5 | intended negatives (`Calor0272/0273/0274`) | `expect=` annotation; prose already cites code and line/column, now checked; `NullabilityDocumentationTests` asserts annotation agrees with its table |
| token-economics #4 | C# `namespace` line inside a `calor` fence | split into a `calor` fence and a `csharp` fence |
| cross-module-effect-propagation #1 | undefined `Order`/`DbContext` (Calor1002) | callee made self-contained (`SaveOrder` declares `db:w`, no undefined types) |
| cross-module-effect-propagation #2 | only meaningful compiled with #1; caller also passed no argument for the old `Order` parameter | `group=orders expect=Calor0410,Calor0417`; quoted output fence now `output` and lists the real `Calor0417` warning too |
| cross-module-effect-propagation #5 | `←` annotations inside code (Calor0100) | `//` comments; `group=orders-using` with the callee |
| syntax-reference/index #1, structure-tags #49 | `§IF` without an ID (Calor0102) | `§IF{if1}`; index table row no longer claims `§IF` has an auto-assigned ID; structure-tags sentence claiming closers are "dropped silently" corrected to `Calor0830` (verified) |
| dependent-types-tutorial #3, refinement-types #11 | arithmetic on `§RTYPE`-typed parameters rejected (`Calor0202`, `i32{#i32}`) — a type-checker defect, not a doc typo | rewritten with inline refinements; one-sentence note states the current limitation; `UnsafeTransfer` fragment updated to match. **Defect to file:** named refinement types are not numeric for `TypeChecker.IsNumericType` |
| inheritance #8, #12 | `§MT` method fragments; R0's scan matched the `§M` prefix of `§MT` | not complete programs (`^§M(\s|\{|$)` rule, same as docs/); unchanged |

## Other findings fixed by the widened scan

- Keywords: `§FOREACH` ×2 → `§EACH`, `§MATCH` → `§W`, `§CAST{INT}` → `(cast i32 f)` (mirrors
  `docs/semantics/core.md`), Do-While row `§WHILE` → `§/DO{id} cond` (mirrors docs/).
  `methodology.mdx` legacy detector names `§LOOP`/`§INV` suppressed with `{/* drift:ignore */}`
  (benchmark text unchanged).
- `cli/compile.mdx` "Error Reporting": invented format replaced by a checked negative example
  (`expect=Calor0200`) and its real `output`.
- Output-shaped fences labelled `illustrative` (no source program on the page, or not compiler
  output): effect-soundness ×5, safety, analyze ×2, compile (verify summary), init, migrate ×2,
  query ×6, verify, claude-integration, adding-calor, effects-contracts-enforcement,
  installation help, cross-module `Calor0417` (hypothetical callee). The `calor query` outputs
  do not match the `tests/TestData/QueryCorpus` fixture (e.g. `Relay` vs `Fan`/`Main`); checking
  them needs the page to carry its `app.calr` — left for #1429-style CLI-output fixtures.

## Negative control

`negative-control.txt`: a throwaway page with an uncompilable program, a wrong `expect=`, an
unlabelled output fence and an unknown code; `self-check docs` exits 1 with all five findings.
