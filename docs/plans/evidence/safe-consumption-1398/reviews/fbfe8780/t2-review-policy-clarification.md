# Original review record: t2-review-policy-clarification

Actual context: `7340894d-8cfb-4ced-be96-96cc277239f5`. Requested model: `claude-opus-4.8`; caller-exposed model: `claude-opus-4.8`.

Extracted from original session events, not reconstructed. Only caller invocation, received prompts, visible assistant responses and tool arguments/status metadata are exported. System/developer messages, hidden reasoning and raw tool outputs are intentionally excluded. Tool success is distinct from shell process exit status; null means no recorded shell exit marker. These historical reviews are not parent acceptance.

## Exact received prompts

Perform an independent narrowly scoped review-record clarification, NOT an implementation task. Exact fbfe878092584700e74651d0b0af0f2b6abb33b0 at /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398, base approved8f9891a1. Source is unchanged after full integration/adversarial reviews. A prior sync reviewer (cannot receive follow-up) ACCEPTed the full diff but incorrectly wrote 'All claims are AnalysisOnly (0272–0275)' and called the guarded-wildcard statement emission defect 'pre-existing' without establishing baseline execution. Resolve these evidence errors by actual source inspection; do not assume their claim or verdict. Contract is0275 ACTIVE with initializer/return transitional guards,0272/3/4 AnalysisOnly, no activation/guard/demotion lift. Inspect actual BindingDiagnosticPolicy, diagnostic catalog, tests and base..head changes. Return exact policy classification, whether fbfe preserves it, any high-confidence blocking contradiction, and source/evidence. Targeted no-build checks are okay; DLL is exact-head and shared, so no builds/edits/Git writes/PR posts. For wildcard case, current fail-closed observation is proven, but don't assert before/after attribution without comparing actual source/emission; explicitly mark unproven if you do not establish it. Your review is supplemental policy/evidence clarification, NOT a substitute for the other two full final-head reviews and NOT a new human/statistical independence claim. Research fully paused: no paid experiments, frozen-study fixtures/private ledger/drafts1438/1078, release operations or new scope. Parent alone adjudicates merge after actual final CI.

## Full visible responses

I have completed the source inspection. No builds, edits, or git writes were performed — only read-only `git diff`/`grep`/`view` against the exact head `fbfe8780`.

## Review-record clarification — consumption-1398 @ fbfe8780 (base 8f9891a1)

### 1. Exact policy classification (authoritative source: `src/Calor.Compiler/Binding/Scope.cs`, `BindingDiagnosticPolicy.Catalog`)

Code→name mapping (`Diagnostics/Diagnostic.cs:326/342/357/360`):

| Code | Name | Catalog disposition | Issue |
|------|------|--------------------|-------|
| Calor0272 | NullableToNonNullableBinding | `Analysis(...)` → **AnalysisOnly** | 1385 |
| Calor0273 | NullableReturnFromNonNullable | **AnalysisOnly** | 1385 |
| Calor0274 | NullableArgumentToNonNullableParameter | **AnalysisOnly** | 1385 |
| Calor0275 | ReferenceOptionMismatch | `BindingDiagnosticDisposition.CompilationError` → **ACTIVE** | 1397 |

This matches the stated contract exactly: **0275 ACTIVE, 0272/0273/0274 AnalysisOnly, no activation/guard/demotion lift.** The "initializer/return transitional guards" are the additive `RequiresTransitionalAssignmentCheck` flag on `NullableReferenceType` (`CalorType.cs:217`, consumed at `TypeChecker.cs:981,1222,2119`); they add checks, they do not change any catalog disposition. 0275 is emitted as a hard error at `Binder.cs:1705` (`ReportError`); 0272 routes through `SemanticsVersion.NullabilitySeverityFor()` + `BindingDiagnosticContext` and stays AnalysisOnly.

**The prior reviewer's "All claims are AnalysisOnly (0272–0275)" is FALSE.** 0275 is a CompilationError/ACTIVE gate, not AnalysisOnly. This is a review-record evidence error, corrected here.

### 2. Does fbfe preserve the policy? — YES (proven)
`git diff 8f9891a1..fbfe8780 -- Binding/Scope.cs Diagnostics/Diagnostic.cs SemanticsVersion.cs` returns **0 lines**. The catalog, code constants, and severity gating are byte-identical to the approved base.

### 3. Guarded-wildcard `case _ when` emission — attribution
- Responsible code: `CSharpEmitter.Visit(MatchStatementNode)` (`CSharpEmitter.cs:4850`) — wildcard + **null** guard → `default:`; wildcard + **non-null** guard falls to the `else` → `case _ when <guard>:`.
- `git diff` for `CodeGen/`, `Parsing/`, and `Ast/` is **empty** — the emitter branch, the match parser, and the pattern nodes are byte-identical to base. The Binder change (`DeclareSuccessfulConditionPatterns`, `allowBindings`) only affects guard-binding *scope declaration*; it does not create a guarded-wildcard case node nor alter emission, and adds no new reachability.
- **Source-established conclusion:** the defect lives in unchanged, not-newly-reachable emitter code → **pre-existing/inherited, not introduced by fbfe.**
- **Explicitly marked unproven:** I did not execute the base head, so the strict *runtime* before/after byte-for-byte attribution is NOT proven by execution. The head's present fail-closed `case _ when` emission is readable in the unchanged emitter; the evidence README (§70) explicitly scopes the transfer repair to not fix it.

### 4. High-confidence blocking contradiction?
**None in the source under review.** fbfe conforms to the stated contract. The contradictions are in the *prior review record* (0275 mislabeled AnalysisOnly; "pre-existing" asserted without baseline). Neither invalidates the source; both are corrected above. The reviewer's ACCEPT of the diff is not contradicted by source, but its rationale required this correction.

This is supplemental policy/evidence clarification only — not a substitute for the two full final-head reviews, and not an independence/statistical claim. Parent adjudicates merge after actual final CI.

## Exact recorded shell commands

Event `277759fc-33ed-435f-8b10-84f7f74daed5`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git rev-parse HEAD && git log --oneline -5 && git status --short | head
````

Event `dc28fb83-6d93-40e1-8573-c397ae45accc`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git diff --stat 8f9891a1..fbfe878092584700e74651d0b0af0f2b6abb33b0
````

Event `9b943460-5fbe-465a-95d4-c35fd056f16b`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rn "BindingDiagnosticPolicy" src/ --include=*.cs -l
````

Event `255cc8a5-f3ab-450b-b128-fe6fa30d5e1b`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rln "class BindingDiagnosticPolicy\|BindingDiagnosticPolicy" src/Calor.Compiler --include=*.cs | head; echo "---"; grep -rn "0272\|0273\|0274\|0275\|AnalysisOnly\|Active" src/Calor.Compiler/SemanticsVersion.cs | head -50
````

Event `75c18d6e-e6ee-4a2d-b12a-6297660d0db5`; process exit: 1.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rln "BindingDiagnosticPolicy" src/Calor.Compiler --include=*.cs; echo "=== search enum/class def ==="; grep -rn "enum BindingDiagnosticPolicy\|BindingDiagnosticPolicy {" src/Calor.Compiler --include=*.cs
````

Event `084d747c-0de8-4239-a1a0-090f03dc064d`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rn "BindingDiagnosticPolicy" src/Calor.Compiler/SemanticsVersion.cs
````

Event `84d6b5d8-a983-476b-90f3-582daff331a7`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rn "class BindingDiagnosticPolicy\|static.*BindingDiagnosticPolicy\|BindingDiagnosticPolicy\b" src/ --include=*.cs | grep -iv "comment\|///" | grep -i "class\|static\|namespace\|public\|internal" | head
````

Event `39c5aaf9-6053-4e86-ba74-4f9a23837b9e`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rn "0272\|0273\|0274\|0275\|NullableToNonNullableBinding\|NullableReturnFromNonNullable\|NullableArgumentToNonNullableParameter\|ReferenceOptionMismatch" src/Calor.Compiler/Diagnostics/*.cs | head -40
````

Event `215322b9-9193-409b-9946-fdab2ea19a9f`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git diff 8f9891a1..fbfe878092584700e74651d0b0af0f2b6abb33b0 -- src/Calor.Compiler/Binding/Scope.cs src/Calor.Compiler/Diagnostics/Diagnostic.cs src/Calor.Compiler/SemanticsVersion.cs | wc -l
````

Event `fff28b8f-bc16-48bc-b83e-deb85856efdb`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rn "ReferenceOptionMismatch\|Calor0275" src/Calor.Compiler --include=*.cs
````

Event `38ccba6a-f9bd-4f4b-a258-7e1b057f2129`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rni "wildcard\|guarded" src/Calor.Compiler --include=*.cs | head -30
````

Event `fa7dd3ac-3020-4437-bdc5-d8427f5c8990`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rni "wildcard\|guard" docs/plans/evidence/safe-consumption-1398/README.md | head -40
````

Event `7614e6d0-018e-46dc-94a0-d2e166126d34`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git diff --stat 8f9891a1..fbfe878092584700e74651d0b0af0f2b6abb33b0 -- src/Calor.Compiler/CodeGen/ src/Calor.Compiler/Migration/ && echo "=== emitter changed? (empty=no) ==="
````

Event `1f529aa5-74fe-40a0-a68d-4de0721320b1`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rn "case _\|\"case \"\|_ when\|when \|Wildcard\|\"_\"" src/Calor.Compiler/CodeGen/CSharpEmitter.cs | head -30
````

Event `da11ef4f-f459-4c18-b01f-cfda07233b75`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git diff 8f9891a1..fbfe878092584700e74651d0b0af0f2b6abb33b0 -- src/Calor.Compiler/Binding/Binder.cs | grep -ni "wildcard\|guard\|match" | head -30
````

Event `e38f285a-e6d7-4859-9e86-c15ba87e847f`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git diff 8f9891a1..fbfe878092584700e74651d0b0af0f2b6abb33b0 -- src/Calor.Compiler/Binding/Binder.cs | sed -n '80,180p'
````

Event `ec275fcd-ec6b-48a5-9f53-957d9d58f565`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git diff --stat 8f9891a1..fbfe878092584700e74651d0b0af0f2b6abb33b0 -- src/Calor.Compiler/Parsing/ src/Calor.Compiler/Ast/ && echo "=== (empty = parser+ast unchanged) ==="
````

Event `dfae1c61-5ee1-431b-862d-9aaf34006117`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && git diff 8f9891a1..fbfe878092584700e74651d0b0af0f2b6abb33b0 -- src/Calor.Compiler/TypeChecking/TypeChecker.cs | grep -ni "0272\|0273\|0274\|0275\|ReferenceOption\|Nullable\|initializer\|return\|guard\|transition" | head -40
````

Event `ee335cb6-d248-4378-a8f7-4d2f3dc423ac`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -rn "RequiresTransitionalAssignmentCheck\|Transitional\|ReferenceOptionMismatch\|0272\|0273\|0275" src/Calor.Compiler/TypeChecking/*.cs
````

Event `f61c4adc-1b5b-4d83-91c9-da55d3651949`; process exit: 0.

````bash
cd /Users/juanrivera/.copilot/session-state/cea41f9d-a634-40dd-ad65-279680749d85/files/worktrees/consumption-1398 && grep -n "enum BindingDiagnosticDisposition" -A6 src/Calor.Compiler/Binding/Scope.cs
````
