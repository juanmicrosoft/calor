# PR 2 (narrowed) Codex round 3 (#1132 F5)

- **Reviewer:** OpenAI Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.
- **Budget:** invocation 3 of 4 (round 3 of 3, the last adversarial round). Only the
  verification-only pass remains.
- **Reviewed:** `git diff origin/main...HEAD` at `36ca49afc` (after round 2, rebased on
  `c020de748`). Codex confirmed both round-2 code witnesses would fail if their fixes were reverted,
  and that the docs name both manifest exceptions.
- **Verdict:** REQUEST CHANGES (1 BLOCKING, 3 MAJOR).

## Dispositions

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `var xs = new[] { t }` (`t` a `Task`) now binds a `Task[]` (main: `object[]`), so `xs.Initialize()` resolves through the `System.Threading.Tasks` namespace default (pure) instead of unknown | **Not changed; contested.** The converter now gives `xs` its C# type: in C# `var xs = new[] { t }` *is* a `Task[]`, and main's `object[]` was wrong (static type and runtime type; #1521 round 1 fixed `new[] { F(), G() }` becoming `object[]`). For a given receiver type the effect result is main's: a `Task[]` declared in the source resolves exactly the same way on main (parity rows 21-23 pin the namespace-default behavior as main's). The certification the maintainer dropped was the array-member table; nothing here certifies a member that main would not certify for the same type. The witness is also not unsound at run time: `Array.Initialize` on an array of a reference type does nothing. Recorded for the maintainer; the README states the parity claim as "same receiver type, same result" |
| 2 | MAJOR | `object[]? xs = new[] { S() }` bypassed the round-2 declared-type check (the mapped `any[]?` does not end with `[]`), took the block form and declared `string[]`; same for `object[][,]` | Fixed in `6002c19da`: the check compares the whole mapped declared type with the created array type (Calor's `[T]` spelled `T[]`); anything else takes the inline path. Those two shapes then fail the round trip loudly, exactly as `object[]? zs = F();` does on main (checked with main's compiler); they are never silently retyped. Witness: `ImplicitArray_KeepsTheDeclaredStaticType` (`§ARR{zs:` / `§ARR{js:` absent) |
| 3 | MAJOR | A constant earlier argument hoisted to a temp loses its constant conversion: `UseB(C, new int[] { 1 }, new Cell())` picks `UseB(long?)` (2) instead of `UseB(byte?)` (1) | Fixed in `6002c19da`: a constant argument (`GetConstantValue(...).HasValue`) stays in place; it evaluates nothing. Witness: `L.UseB(L.C, …)` in `RetypedEarlierArguments_ArePreserved_AndRunLikeTheOriginal` (runs `3,4,1\|cs` like the original) |
| 4 | MAJOR | `CSharpEmitter` split the element ranks but the empty branch dropped them: `§ARR{a:i32[]} §/ARR{a}` emitted `Array.Empty<int>()` | Fixed in `6002c19da`: `Array.Empty<int[]>()`. Witness: `EmptyJaggedArray_KeepsItsElementRank` |

## Review output (verbatim, findings section)

Read-only review; witnesses below are code-traced, not executed. Both round-2 code witnesses would fail if their fixes were reverted. The documentation now names both manifest exceptions. Those fixes leave the following gaps.

1. **BLOCKING — Implicit-array inference still changes array-member effect results from main.** Location: [RoslynSyntaxVisitor.cs:12720](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1e114316d44b328d/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:12720).

   ```csharp
   public static class P
   {
       public static void Run(System.Threading.Tasks.Task t)
       {
           var xs = new[] { t };
           xs.Initialize();
       }
   }
   ```

   Main’s `InferElementType` falls back to `object` for the reference `t`, producing an `object[]` binding. Its `Initialize` call is unknown. HEAD uses Roslyn’s inferred element type and produces a `System.Threading.Tasks.Task[]` binding. The unchanged namespace default at `src/Calor.Compiler/Manifests/bcl-runtime.calor-effects.json:342` certifies that call as pure.

   The round-2 fix protects explicitly declared `object[]`, but this `var` case still removes main’s unknown-call charge. The parity table constructs already-typed Calor parameters; it cannot detect converter-induced receiver changes. Consequently, it does not prove the claimed complete parity.

2. **MAJOR — Nullable declared arrays bypass the round-2 declared-type check and change overload selection.** Location: [CalorEmitter.cs:1914](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1e114316d44b328d/src/Calor.Compiler/Migration/CalorEmitter.cs:1914).

   ```csharp
   #nullable enable
   public static class P
   {
       static string S() => "a";
       static int Pick(object[]? x) => 1;
       static int Pick(string[]? x) => 2;

       public static int Run()
       {
           object[]? xs = new[] { S() };
           return Pick(xs);
       }
   }
   ```

   Original result: **1**. `MapDeclarationType` produces `any[]?`, which does not end with `[]`. The guard therefore permits the block form without comparing element types. HEAD emits `§ARR{xs:str}`, declaring `string[] xs`; generated C# selects the second overload and returns **2**.

   Main’s fallback inference for the call element produced `object[]`, so this witness also exposes a branch regression. The same suffix check bypasses vector declarations whose elements are rectangular arrays, such as `object[][,]`.

3. **MAJOR — `KeepsMeaningInTemp` accepts nullable conversions that depend on constant-expression status.** Locations: [RoslynSyntaxVisitor.cs:11067](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1e114316d44b328d/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11067), `:11115`.

   ```csharp
   public class Cell { }

   public static class P
   {
       const int C = 1;

       static int Use(byte? x, int[] xs, Cell c) => 1;
       static int Use(long? x, int[] xs, Cell c) => 2;

       public static int Run() =>
           Use(C, new int[] { 1 }, new Cell());
   }
   ```

   Original result: **1**. The constant can convert to `byte?`, which is the better overload target. The later constructor causes `C` to be hoisted. Roslyn classifies its conversion as implicit nullable, so the new guard accepts it—even though the underlying conversion requires a constant expression. Nullable conversions can carry an underlying constant conversion. [Roslyn conversion definitions](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Binder/Semantics/Conversions/Conversion.cs)

   The generated equivalent is `var temp = C; ... Use(temp, xs, cell);`. `temp` is a nonconstant `int`, so the `byte?` overload becomes inapplicable and generated C# returns **2**, without preservation. Main leaves `C` in the argument context and selects the original overload. The round-2 method-group/user-defined-conversion witness does not cover this case.

4. **MAJOR — Splitting array ranks breaks existing empty jagged-array Calor.** Location: [CSharpEmitter.cs:5144](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-a1e114316d44b328d/src/Calor.Compiler/CodeGen/CSharpEmitter.cs:5144).

   ```calor
   §M{m1:T}
     §CL{c1:P:pub:stat}
       §MT{f1:Make:pub:stat} () -> i32[][]
         §E{alloc}
         §R §ARR{a:i32[]} §/ARR{a}
   ```

   Main emits `Array.Empty<int[]>()`, correctly returning `int[][]`. HEAD splits the element type into base `int` and ranks `[]`, but the empty-array branch discards those ranks and emits `Array.Empty<int>()`. The generated return is now `int[]` and fails C# compilation.

   Sized and nonempty branches retain `elementRanks`; the empty branch must retain them in the generic element type too. Converter-generated fresh-empty-array tests use the sized form, so they miss this compiler regression.

VERDICT: REQUEST CHANGES
