# Codex review — round 3 (0.25 F3, #847)

- **Reviewer:** OpenAI Codex (`codex exec -s read-only --ephemeral`, `model_reasoning_effort=high`), cross-family. Reduced independence; not an independent human review.
- **Input:** the combined diff of both stacked PRs (#1519 construct, #1531 converter) from the merge base `fd34f41f` to `3dc56bbe`, with evidence data excluded, plus a correctness prompt covering completeness of the recorded fixes, C#/Calor name agreement, capture classification, effect completeness for escaping values, proof isolation, the global binder change, and the split.
- **Verdict:** REQUEST-CHANGES (3 BLOCKING, 2 NON-BLOCKING). Areas reported clean: capture classification; placement/generic refusal and sibling recursion; contract proof, obligation isolation and runtime-guard retention; the global binder delegate-shadowing change; the PR split.

## Dispositions

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | An expression-call argument (`§C Take §A Help §/C`) bypassed the method-group charge and the argument row check. | **Fixed, fail closed.** A local function may be used as a value only directly as a `§B` initializer, a `§R` value, the value of an `§ASSIGN` to a simple name, or a `?:` branch of those, all outside lambdas, or as a `§C{…}` argument. These are the positions the row checker adjudicates. Any other value use is `Calor0211`, including expression-call arguments (`ReturnValidationPass.CheckValueUses`). The converter (#1531) preserves any member whose method-group uses fall outside these positions. Test `Review3_LocalFunctionValueInAnUncheckedPosition_IsCalor0211`. |
| 2 | BLOCKING | `(?? f Help)`, and array/collection storage, carried an effectful local function past a pure receiving row. | **Fixed by the same rule** (`??` and collection elements are not allowed value positions). The general `??`/collection row gap for ordinary method groups is pre-existing and outside F3. Recommended as a separate issue. |
| 3 | BLOCKING | A qualified store `§ASSIGN Probe.Saved Help` skipped the row check (a bare `Saved` target is checked). | **Fixed by the same rule** (only an `§ASSIGN` to a simple name is allowed). The qualified-store row gap for ordinary method groups is pre-existing; recommended as a separate issue. |
| 4 | NON-BLOCKING | The emitter's bare-name set is the union of every name the enclosing callable declares, so an out-of-scope declaration can keep a needed module call bare. | **Accepted as a known limitation.** The result is always a loud C# error (`Calor1002`/CS0103), never a call to the wrong function. Fixing it needs lexical-visibility tracking in the emitter, which does not fit the F3 source ceiling. Pinned by `Review3_AnOutOfScopeEnclosingName_FailsLoudlyNotSilently`. |
| 5 | NON-BLOCKING | `BindValidationPass` used a shadowed module function's signature (false `Calor0254`). | **Fixed.** Bind validation does not resolve a bare call naming a local function of the body being checked (exact signature, return type and parameter types). Test `Review3_BindValidation_DoesNotUseAShadowedModuleSignature`. |

Round 3 was the last review round under the 0.25 capacity (≤3 rounds); a verification-only pass follows.

## Codex output (final answer, verbatim)

REQUEST-CHANGES

Reproduced with the current Release MCP build, `autoFix:false`. No repository writes.

1. **BLOCKING — Expression-call arguments lose local-function effects.** `EffectEnforcementPass.cs:5877`. Repro: `§R §LAM{l001:x:i32} §C Take §A Help §/C §/LAM{l001}` with `Take (Func<i32,i32>:f §E{})` and a printing local `Help`, returned through `Func<i32,i32> §E{}`. Accepted without diagnostics; `§C{Take}` correctly gives `Calor0424`. Fix: apply the same argument charging and row checks to both call spellings.
2. **BLOCKING — Unrecognized function-value expressions bypass receiving-row checks.** `EffectEnforcementPass.cs:2582`. Repro: `§R (?? f Help)` through a pure return row; also `§B{Func<i32,i32>:g} §E{} (?? f Help)`, and an array initialized with `Help` read back with `§IDX`. Fix: join null-coalescing rows; treat unrecognized sources at function-typed receiving boundaries as Unknown; include collection-element reads.
3. **BLOCKING — Qualified field/property stores bypass escape checks.** `EffectEnforcementPass.cs:2189`. Repro: `§ASSIGN Probe.Saved Help` into `§FLD{Func<i32,i32>:Saved:pub:stat} §E{}`, then a `§E{}` method invoking `Saved` prints. A bare `Saved` target correctly reports `Calor0424`. Fix: resolve assignment destinations by bound member identity.
4. **NON-BLOCKING — Out-of-scope declarations suppress required module qualification.** `CSharpEmitter.cs:1234`. A loop variable `Add` outside `L`'s scope keeps `§C{Add}` in `L` bare, giving `Calor1002/CS0103`. Fix: preserve bare names by lexical visibility or bound identity.
5. **NON-BLOCKING — Bind validation resolves a shadowed module signature.** `BindValidationPass.cs:924`. A local `Add -> List<i32>` shadowing a module `Add -> [i32]` gives a false `Calor0254`. Fix: give bind validation scoped local-function signatures.

Capture classification: no findings. Placement/generic refusal and sibling recursion: no findings. Contract proof, obligation isolation, and runtime-guard retention: no findings. Global binder delegate-shadowing change: no findings in the additional probes. PR split: no findings from tree inspection.
