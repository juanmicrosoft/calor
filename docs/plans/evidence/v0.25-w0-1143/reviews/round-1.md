# Codex review — round 1

Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral`, reasoning effort high), on
`git diff origin/main...HEAD` at `5399be82`. Verdict: **BLOCKING findings remain** (5 blocking,
3 should-fix).

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `~~~calor` and blockquoted fences bypass the check (MDX renders them as code) | Fixed. `ParseFences` follows CommonMark openers: backtick or tilde, 3+ characters, indented (lists, JSX) or blockquoted; closers must match. 5 negative-control tests |
| 2 | BLOCKING | A negative group member (lexer failure) suppresses generated-C# validation for the whole group, hiding a positive member's `Calor1002` | Fixed. Groups that mix negative and positive members also compile the positive subset alone; it must have no errors. Test `NegativeGroupMemberCannotHideAnotherMembersErrors` |
| 3 | BLOCKING | `drift:ignore` exempted complete programs and output fences | Fixed. On website pages the marker affects prose scans only. Test asserts a suppressed broken program and output still fail |
| 4 | BLOCKING | `illustrative` is invisible on the published site | Fixed. `website/src/lib/code-meta.ts` forwards the annotation; `CodeBlock` labels the block "Example output (not checked)". Verified in the static build (`out/docs/cli/query/index.html`: 7 labels) |
| 5 | BLOCKING | JSON MCP responses unchecked; tutorial `used_as_index` response impossible for its source | Fixed. JSON fences with envelope/MCP-response keys must be labelled (8 labelled). Tutorial Part 3 rewritten around `Divide` only, with responses captured from `calor mcp --stdio` (`calor_refine` types/obligations/guards/diagnose) |
| 6 | SHOULD-FIX | Proof claims assume obligation solving that the default compile does not run | Fixed. Verified: `VerifyRefinements` is set only by `calor_refine`; a `§PROOF` on a `§B` name is `Unsupported` ("Unknown variable"). Examples now state the proof on the expression (solver: `Discharged`; unsafe variant: `Failed` with counterexample) and the pages say the default compile keeps every guard |
| 7 | SHOULD-FIX | Location claims after the fence were ignored | Fixed; checked on both sides. Test added |
| 8 | SHOULD-FIX | `§IF` quick-reference row still showed `§IF (> x 0)`; "always needs an ID" overstated (expressions) | Fixed in website and `docs/syntax-reference/index.md`: statement rows use `§IF{if1}`; wording says "an `§IF` statement" |
