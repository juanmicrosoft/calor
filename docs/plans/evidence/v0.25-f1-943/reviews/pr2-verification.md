# F1 #943 PR 2 (#1571) — Codex verification pass

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9). Invocation 4 of 4 of the PR 2 budget
  (3 rounds plus 1 verification pass); the budget is now exhausted.
- Input: `git diff origin/main...HEAD` at `4dbc3e3ac` (draft 4: explicit `default` initializer on
  lowered out bindings, preserved when a symbol named `default` is visible or the statement holds a
  chained assignment; parser unchanged). Codex re-ran all 129 committed MCP outputs through the
  Release build (all reproduce exactly); it modified no repository file.
- **Verdict: REQUEST CHANGES** (1 MAJOR). Codex confirmed that the parser is unchanged, that the
  round 1–3 dispositions hold, that the evidence hashes and the T1 1.0.1 seal match, that the ledger
  deltas and CHANGELOG figures agree, and that the diff has 47 counted non-test lines and no new
  `src/**/*.cs` files.

## Finding (open)

| # | Finding (Codex) | Severity | Status |
|---|---|---|---|
| P2V-1 | The `default` collision check uses `SemanticModel.LookupSymbols`, which omits inaccessible members. A **private** base-class field or method named `@default` (`class B { private static int @default = 3; } class Probe : B { … int.TryParse("5", out var n) … }`) is not found, so the binding is lowered. The C# emitter collects base-member names without regard to accessibility and emits `int n = @default;`, which fails with `CS0122` (`Calor1002`). MCP conversion reports `success: false`. | MAJOR | **Open; not fixed.** The PR 2 review budget is spent, so per the F1 rule the work stops here with this finding open for the maintainer. The failure is loud (a compile error and a refused conversion), not a silent change in meaning. The likely fix (not applied, not reviewed) is to also search the containing type's base chain for any member named `default`, regardless of accessibility (for example `type.BaseType … GetMembers("default")`), and add witnesses for a private base field and a private base method. |
