# F1 #943 PR 2 (#1571) — Codex adversarial review, round 1

- Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`),
  cross-family. Independence: reduced (see the R0 packet §9). Invocation 1 of the PR 2 budget (3
  rounds plus 1 verification pass).
- Input: `git diff origin/main...HEAD` at `4eb94ba59` (the first draft: a `default` initializer on
  every lowered out var binding, the ref-expression escalation, the attribution change, the
  FeatureSupport text, regenerated ledgers, F1 evidence and T1 amendment 1.0.1), with the D1 contract
  as the specification. The read-only sandbox blocked builds and probes, so findings are traced from
  source; Codex modified no repository file.
- **Verdict: REQUEST CHANGES** (1 BLOCKING, 1 MAJOR, 4 MINOR).

## Findings and dispositions

| # | Finding (Codex) | Severity | Disposition |
|---|---|---|---|
| P2R1-1 | The synthesized `default` initializer is a `ReferenceNode("default")`; with a member named `default` in scope, the C# emitter writes `@default`, so `int n = @default;` runs the member's getter (`reads` 0 -> 1). | BLOCKING | Fixed by design change. The `default` initializer is gone. A call statement with an out declaration (an expression statement that is not an assignment) now preserves its member, which D1 RO-DECL-1 allows ("in every other case the statement or member is preserved"); the other placements lower with no initializer, as in PR 1. Witnesses: `P2-out-var-member-named-default` (native, `0:5:True` on all 5 surfaces), `P2-OUTVAR-PLACEMENTS` (a `@default` member, native on all 6 evidence surfaces), `OutDeclaration_IsLoweredWithoutAnInitializer`. The same `@default` defect exists on `main` for the converter's `int x;` -> `§B{~x} default`; it is outside F1 and recorded as a residual in the README. |
| P2R1-2 | `Set(out string value);` gained `string value = default;` (CS8600) from the initializer. | MAJOR | Resolved by P2R1-1 for the converter. Checked: the C# emitter itself writes `= default` for any initializer-less mutable binding, so `bool ok = Set(out string value);` emits `string value = default;` on `main` and here alike (warning only; unchanged since PR 1). Recorded as a residual; not changed (emitter behavior outside the F1 budget). |
| P2R1-3 | `ref readonly` parameters fall through to `in-parameter`. | MINOR | Fixed: `RefReadOnlyParameter` maps to `ref-parameter` (RO-UNS-1 lists `ref readonly` under ref shapes). Witness row added to `ByReferenceStatementLoss_IsReportedUnderTheParametersFeature` (fails on `main`). |
| P2R1-4 | The `ref-expression` text says "reported as ref-parameter" but the loss is `method`/`property`. | MINOR | Fixed: the text says "with a ref-parameter warning"; `ExpressionBodiedRefReturn_IsPreserved_NotStripped` now asserts that warning. |
| P2R1-5 | README: "Every new test fails on origin/main" is false (the `in-parameter` rows are controls). | MINOR | Fixed: the README states the measured regression sensitivity (19 failures with `src/Calor.Compiler/Migration` reverted) and names the controls. |
| P2R1-6 | README: "propagated error count 0 in every file" is false (MessageTemplateParser has 3). | MINOR | Moot: with the initializer gone, those six files are back to `main`'s numbers; the paragraph was rewritten. |

## Effect of the round on the PR

Ledgers regenerated from `main`'s baseline (`RoslynSelectedAttempted` 34,311; opaque boundaries 150,
expressions 7,830; per file in the README), F1 evidence re-run (three P2 rows), T1 amendment 1.0.1
re-sealed (`97280f1b…`).
