# PR 2 (narrowed) Codex round 2 (#1132 F5)

- **Reviewer:** OpenAI Codex CLI, `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"`.
- **Budget:** invocation 2 of 4 (round 2 of 3).
- **Reviewed:** `git diff origin/main...HEAD` with the round-1 fixes (`d3e3f238d`) and the key-ledger
  regeneration. Round 1's fixes 1-3 were confirmed (sensitive witnesses; ledger present); fix 4 was
  judged incomplete (finding 3 below).
- **Verdict:** REQUEST CHANGES (1 BLOCKING, 1 MAJOR, 1 MINOR).

Commit ids below are after the rebase onto `origin/main` at `c020de748` (#1520, #1518 merged).

## Dispositions

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | BLOCKING | `object[] xs = new[] { "a" }`: the statement block form declared the created type (`string[] xs`), so overload resolution picked `Pick(string[])` (2, not 1), and `object[] xs = new[] { task }; xs.Initialize()` became a `Task[]` receiver that the `System.Threading.Tasks` namespace default certifies, an effect change from main | Fixed in `16d79392f`: `CalorEmitter` uses the `§ARR{name:T}` block form only when the declared element type equals the created one; otherwise the binding keeps its declared type (`§B{any[]:xs}`) and the array is written inline. Witness: `ImplicitArray_KeepsTheDeclaredStaticType` (runs `12True` like the original; asserts the `any[]` binding for the `Task` case) |
| 2 | MAJOR | An earlier argument bound to an untyped temp, because a later argument is hoisted, loses its target typing (`Use2(Id, new int[] { 1 }, new Cell())`, CS8917) or runs its user-defined conversion after the hoisted argument (`"sc"` instead of `"cs"`) | Fixed in `16d79392f`: such an argument is preserved (`conditional-expression-hoisting`) unless Roslyn classifies its conversion to the parameter as identity, numeric, reference, boxing or nullable, with a known type. Witness: `RetypedEarlierArguments_ArePreserved_AndRunLikeTheOriginal` (2 losses; runs `3,4\|cs` like the original) |
| 3 | MINOR | Docs still omit the namespace-default exception (`ValueTask[].Initialize` gives no diagnostic) | Fixed: CHANGELOG and `convert.mdx` name both manifest exceptions (`System.Array` receivers and element types in a namespace with a manifest default) |

Found while writing the finding-2 witness, not changed here (pre-existing on main, reproduced with
main's compiler on hand-written Calor): a `§B{X:v}` binding of a user type `X` followed by a `§RAW`
statement that reads `v` fails C# compilation (`CS0246`, `v` read as a type). The witness passes the
struct as a parameter instead.

## Review output (verbatim, findings section)

Read-only inspection: round-1 fixes 1 and 2 have sensitive witnesses; the corrected ledger for fix 3 is present. Fix 4 remains incomplete.

1. **BLOCKING — Array bindings lose their declared type, changing semantics and effect results.** Locations: `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:12707`, `src/Calor.Compiler/Migration/CalorEmitter.cs:1913`, `:2149`.

   ```csharp
   public static class P
   {
       static int Pick(object[] x) => 1;
       static int Pick(string[] x) => 2;

       public static int Run()
       {
           object[] xs = new[] { "a" };
           return Pick(xs);
       }
   }
   ```

   C# returns **1**. The converter now correctly infers the creation’s element type as `string`, but `EmitArrayCreationWithName` discards the binding’s `TypeName`. It emits `§ARR{xs:str}`, whose parser-generated binding declares `string[] xs`. Generated C# therefore returns **2**, without preservation or a loss. Main’s declared-element-type inference selected `object` for this witness.

   The same defect violates effect parity:

   ```csharp
   #nullable enable
   public static class P
   {
       public static void Run(System.Threading.Tasks.Task t)
       {
           object[] xs = new[] { t };
           xs.Initialize();
       }
   }
   ```

   Main emits an `object[]` binding, so `xs.Initialize()` resolves as unknown. HEAD emits a `System.Threading.Tasks.Task[]` binding; the unchanged namespace default in `Manifests/bcl-runtime.calor-effects.json:342` resolves that call as pure. The effect result changes despite the resolver being unchanged.

   The existing `object[] oa = new[] { L.S() }` test at `ArrayExpressionConversionTests.cs:380` checks runtime array type, but does not check the variable’s static type. The handwritten parity table cannot detect this converter-induced receiver change.

2. **MAJOR — Earlier argument temporaries lose contextual typing and conversion timing.** Location: `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11089–11104`, especially the untyped binding at `:11103`.

   ```csharp
   using System;

   public class Cell { }

   public static class P
   {
       static int Id(int x) => x;
       static string Id(string x) => x;
       static int Use(Func<int, int> f, int[] xs, Cell c) => f(xs[0]);

       public static int Run() =>
           Use(Id, new int[] { 1 }, new Cell());
   }
   ```

   This valid C# returns **1**. The later `new Cell()` makes the earlier method group move into an untyped temporary, equivalent to `var temp = Id;`. Its overloaded delegate type cannot be inferred (`CS8917`). Main retained `Id` in the argument’s `Func<int,int>` context. Default library conversion consequently fails round-trip validation; CLI rescue can conceal the regression.

   The round-1 local snapshot fix also leaves implicit conversions delayed:

   ```csharp
   public class Cell { }

   public struct X
   {
       public static implicit operator int(X x)
       {
           P.Log = P.Log + "c";
           return 1;
       }
   }

   public static class P
   {
       public static string Log = "";
       static int S() { Log = Log + "s"; return 2; }
       static void Use(int first, int[] xs, Cell c) { }

       public static string Run()
       {
           X x = default;
           Use(x, new int[] { S() }, new Cell());
           return Log;
       }
   }
   ```

   Original order is **`cs`**. The snapshot binds an `X`, then the hoisted array runs `S()`, and conversion to `int` occurs at the final call: **`sc`**. This compiles and receives no preservation loss. The primitive `pr2-args` witness detects its particular fix, but cannot detect delayed contextual conversions.

3. **MINOR — The public effect claim still excludes a demonstrated exception.** Locations: `website/content/cli/convert.mdx:241`, `CHANGELOG.md:103`.

   ```calor
   §M{m1:T}
     §CL{c1:P:pub:stat}
       §MT{m2:Run:pub:stat} (System.Threading.Tasks.ValueTask[]:g) -> void
         §E{}
         §C{g.Initialize} §/C
   ```

   The docs still say receivers “typed as an array” produce unknown-call diagnostics, mentioning only `System.Array` as an exception. This array-typed receiver produces neither diagnostic, exactly as pinned at `ArrayMemberEffectParityTests.cs:41`. Round-1’s documentation fix needs to qualify namespace-default behavior too.

VERDICT: REQUEST CHANGES
