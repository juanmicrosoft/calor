# Extra verification pass, Codex (#1132 F5)

- **Exception:** `EXC-F5-EXTRA-VERIFICATION` in the R0 scope packet
  (`docs/plans/evidence/v0.25-r0-1426/scope.json`, `capacity.exceptions`, merged at `ed4bc1909`).
  @juanmicrosoft decided it on 2026-10-11, and the packet records it as `confirmed: true`. It allows
  exactly one verification-only Codex pass on F5's 4 post-verification fixes, and no further
  adversarial round.
- **Reviewer:** OpenAI Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.
  This was the one allowed pass. No other Codex invocation was made on this PR under the exception.
- **Scope:** the fixes made after `verification.md`. Commit `61267112d` (the code) is now `0b8af66fd`
  after the rebase onto `origin/main` at `ed4bc1909`. Its witnesses, commit `0e27e0433`, are now
  `dac223269`. The input was `git diff 7203fa055 dac223269 -- src tests`. Also in scope was any code
  in this PR's files that changed in the rebase. Main did not modify any source file the PR touches.
  Codex was asked to check the fixes against the code at HEAD.
- **Reviewed HEAD:** `525a9a739`. That is the rebased branch plus the key-ledger re-stamp. The
  re-stamp changes no source.

## Result

**VERDICT: REQUEST CHANGES** (2 BLOCKING, 1 MAJOR, 2 MINOR).

| Item from `verification.md` | This pass |
|---|---|
| Round-3 #1, array extension effects | NOT-VERIFIED |
| Round-3 #2, element conversions and method groups | NOT-VERIFIED overall (ordering fixed; multi-line rectangular output broken) |
| Round-3 #5, declared jagged rank coverage | VERIFIED |
| Round-2 #6, `Initialize` | NOT-VERIFIED for every receiver shape |

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `IsArrayInstanceMember` checks argument shapes, not the member C# selects. An extension `GetLength(this int[], long)` is still certified pure. Explicit generic arguments, named arguments, and `out` arguments also bypass the check. A `System.Array` receiver still certifies extensions through the pure default. | **Open.** Not fixed: the exception allows no further review pass. |
| 2 | BLOCKING | `Initialize` is unknown only for a receiver of exactly `System.Array`. Another array type, such as `System.Threading.Tasks.ValueTask[]`, falls through to its namespace's pure default. | **Open.** Not fixed, for the same reason. |
| 3 | MAJOR | A multi-line element in a rectangular statement array, such as `object[,] a = { { new { A = 1 } } };`, is no longer hoisted, and its continuation lines are not re-indented. `§/ANON` consumes the `§ARR2D` dedent. This regression came from removing the hoist. | **Open.** Not fixed, for the same reason. |
| 4 | MINOR | `ArrayExtensionCalls_AndInitialize_AreNotCertifiedPure` does not witness the shape fix: the unknown `Touch()` call masks the result. | **Open.** |
| 5 | MINOR | The shape check rejects real `System.Array` overloads: `GetValue(int[])`, the matching `SetValue`, and `GetValue(uint)`. They now report `Calor0410`, a conservative regression. | **Open.** |

The rules for this pass forbid fixing a BLOCKING or MAJOR finding and then reviewing again. The PR
therefore stays a draft. Findings 1 to 3 need a maintainer decision: re-scope F5, or record a new
exception for another fix-and-review cycle. Any new F5 non-test change also needs a new exception
under `EXC-F5-NONTEST-LINES`. The two MINOR findings were not fixed either, because they touch the
same source files and the same ceiling applies.

## Review output (verbatim)

Reviewed HEAD `525a9a739` without modifying files or accessing `~/.calor`. Tests were inspected, not executed; revert sensitivity below is based on source tracing.

1. **BLOCKING — Extensions can still receive System.Array effects.** [EffectResolver.cs:85](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/src/Calor.Compiler/Effects/EffectResolver.cs:85)

   The predicate recognizes argument shapes, not the selected member. This valid extension is still classified as pure:

   ```csharp
   public static int GetLength(this int[] xs, long dimension)
   {
       Console.WriteLine("side");
       return xs.Length;
   }
   // xs.GetLength(k), where k is long
   ```

   `Array.GetLength` takes `int`, so C# selects the extension. The table accepts `Int64` and returns no effects.

   Same-arity integer extensions also bypass the check through explicit generic arguments (`xs.GetLength<int>(0)`), a different named parameter (`xs.GetLength(index: 0)`), or `out` arguments. The enforcement caller supplies argument types but omits generic arguments, argument names, and modifiers from the resolver key. `GetValue` has the same identity problem, although its table entry charges `alloc`.

   Ordinary positional `GetLength(int)` and `GetValue(int)` select applicable instance methods ahead of extensions. Actual long-index `GetValue` overloads are legitimate. Static `Array.*` calls and receivers typed `System.Array` bypass this table and use manifests; the latter still certify extensions such as `GetLength(string)` through `System.Array`’s pure default. `object` and a key named `T` do not match the array predicate. C# does not permit an array or `System.Array` constraint.

2. **BLOCKING — Initialize remains pure for some array receiver types.** [EffectResolver.cs:127](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/src/Calor.Compiler/Effects/EffectResolver.cs:127)

   The guard protects exactly `System.Array`. Other array types fall through to namespace defaults. This hermetic resolver assertion would fail at HEAD:

   ```csharp
   var resolver = new EffectResolver(
       new ManifestLoader(loadUserLevelManifests: false));
   var result = resolver.Resolve(EffectResolverKey.FromStrings(
       "System.Threading.Tasks.ValueTask[]", "Initialize", []));
   Assert.Equal(EffectResolutionStatus.Unknown, result.Status);
   ```

   The source path returns resolved, empty effects from the built-in `System.Threading.Tasks` namespace default. Likewise, an array of a user struct in `System.Threading` can have a constructor that writes to the console and still receive that namespace’s pure default. Consequently, neither `Initialize` nor unmatched array extensions reliably remain unknown.

3. **MAJOR — Multiline rectangular statement elements lose the array’s block boundary.** [CalorEmitter.cs:2201](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/src/Calor.Compiler/Migration/CalorEmitter.cs:2201)

   Concrete reproduction:

   ```csharp
   public static int Run()
   {
       object[,] a = { { new { A = 1 } } };
       return a.Length;
   }
   ```

   `RenderMultiDimRows` now leaves the anonymous object inline, but the statement emitter does not shift its continuation lines. The anonymous object’s property lands at the row’s column, and `§/ANON` lands at the array opener’s column. `ParseAnonymousObjectCreation` consumes the dedent belonging to `§ARR2D`; the rectangular parser can then absorb the following return.

   The new two-space adjustment fixes the tested one-dimensional anonymous object, but it does not cover rectangular statement arrays. This regression was introduced by removing their hoisting. Default conversion may rescue the member as C#, which still violates native conversion.

4. **MINOR — The extension regression test does not witness the shape fix.** [ArrayExpressionConversionTests.cs:711](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/tests/Calor.Compiler.Tests/Migration/ArrayExpressionConversionTests.cs:711)

   `Go` already calls unknown `Touch()`. Its `Calor0410` remains even if `GetLength(string)` is wrongly certified pure. Reverting only the argument-shape validation would therefore leave this test passing. The two calls need independent assertions.

5. **MINOR — The shape check newly rejects legitimate index-array overloads.** [EffectResolver.cs:85](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1138d934b9ae9c76/src/Calor.Compiler/Effects/EffectResolver.cs:85)

   `g.GetValue(indices)` with `int[] indices` selects System.Array’s `params int[]` overload, but the predicate rejects its array argument and charges an unknown call. A function declaring `alloc`, previously sufficient, now receives `Calor0410`. The analogous `SetValue` overload and `GetValue(uint)` via conversion to `long` are also rejected. Existing scalar-index tests do not cover this new conservative regression.

All named tests below belong to `ArrayExpressionConversionTests`.

| Item | Status | Exact witness and revert sensitivity |
|---|---|---|
| **1. Array extension effects** | **NOT-VERIFIED** | `ArrayExtensionCalls_AndInitialize_AreNotCertifiedPure` contains `GetLength(string)`, but `Touch()` masks its result. **Would not fail** if only the shape fix were reverted. Finding 1 supplies remaining counterexamples. |
| **2. Element conversions / method groups** | **NOT-VERIFIED overall** | `ArrayExpressions_ConvertNatively_AndRunLikeTheOriginal("round3", "3,10,123,1118\|scsc\|sctc", surface)` witnesses the constructor/conversion order and simple multiline element on all eight surfaces. **Would fail** if statement `RenderInPlace` were reverted. `InAddressBeforeAHoistedArgument_IsPreserved_AndRunsLikeTheOriginal` contains the method-group witness. The ordering fix is supported, but finding 3 disproves valid multiline output on every path; no existing test covers that reproduction. |
| **3. Declared jagged rank coverage** | **VERIFIED** | The same `"round3"` theory now includes `int[][,] dj = { new int[,] { { 8 } } };` and reads `dj[0][0, 0]`. It exercises the requested bare initializer. **Would fail** if the declared-element rank fix were reverted. Reverting the test-only commit would simply remove this coverage. |
| **4. Initialize regression** | **NOT-VERIFIED for every receiver shape** | `ArrayExtensionCalls_AndInitialize_AreNotCertifiedPure`, method `Init`, witnesses `System.Array`; **would fail** if its explicit guard were reverted. `ArrayMembersAndRowElements_AreCharged("§C{g.Initialize} §/C", "mut", true)` witnesses `int[,]`. Neither covers the namespace-default bypass in finding 2. |

VERDICT: REQUEST CHANGES
