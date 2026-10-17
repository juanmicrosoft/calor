Read-only, code-traced review; no probes executed. The round-2 witnesses are addressed, but I found three regressions and one remaining pre-existing gap. Generated temporary names below are simplified.

1. **Emitter fallback loses the target type of `new()` — blocker, introduced by this PR.**

   ```csharp
   class C {
       public class P { public int A; }
       public class Q { public int A = 5; }
       static int Use(P p) => p.A;

       public static int Probe() {
           return Use(new() { A = new Q().A });
       }
   }
   ```

   Predicted Calor:

   ```calor
   §B{_newP} §CS{new() { A = new Q().A }}
   §R §C{Use} _newP
   ```

   Predicted generated C#:

   ```csharp
   var _newP = new() { A = new Q().A };
   return Use(_newP);
   ```

   `ConvertImplicitObjectCreation` initially infers `P`. The invocation’s `HoistComplexArguments` then moves that node into a binding with no declared type. During emission, `.A` requires a receiver hoist, triggering the [new fallback](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aabc5edfd940e26ac/src/Calor.Compiler/Migration/CalorEmitter.cs:3216). Its original C# contains target-typed `new()`, but the temporary has no target type: C# reports CS8754.

   Main emits an explicit `new P(...)` and returns **5** for this side-effect-free witness. The PR breaks compilation.

   **Suggested fix:** retain the inferred type on generated bindings, or expand target-typed creations to explicit types before preserving their C#.

2. **Preserving an initializer hides declarations needed by subsequent Calor statements — blocker, introduced by this PR.**

   ```csharp
   class C {
       public class P { public bool B; }
       static bool Get(out int x) { x = 7; return true; }

       public static int Probe() {
           var p = new P { B = Get(out int x) };
           return x;
       }
   }
   ```

   Predicted Calor:

   ```calor
   §B{p} §CS{new P { B = Get(out int x) }}
   §R x
   ```

   Converting `out int x` adds a pending declaration. The [rollback](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aabc5edfd940e26ac/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:12429) removes it and preserves the creation. C# still declares `x` inside that expression, but Calor’s binder treats `RawCSharpExpressionNode` as opaque, and the type checker sees no declaration for the later bare reference. Compilation with type checking enabled reports **“Undefined variable 'x'.”**

   Main emits the separate typed declaration and the native initializer call, preserving the result **7**.

   **Suggested fix:** when a preserved expression declares names used outside it, preserve the containing member, or explicitly register those declarations and their types without moving or duplicating their initialization.

3. **Alias names are mistaken for floating-point types in `default(...)` — major-silent, regression versus main.**

   ```csharp
   using Single = System.Int32;

   class C {
       public static object Probe() => default(Single);
   }
   ```

   Predicted Calor return:

   ```calor
   §R SINGLE:0
   ```

   Predicted generated C#:

   ```csharp
   return 0f;
   ```

   Original C# boxes an **Int32**. The PR boxes a **Single**, without reporting a loss. `using Double = System.Int32` similarly produces `0.0` and boxes a Double.

   [ConvertDefaultExpression](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aabc5edfd940e26ac/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:10744) selects the literal from the source spelling instead of the resolved type. That resolution defect predates this PR, but main’s bare `0` round-trips as an integer and preserves these witnesses; the new formatting changes their behavior.

   **Suggested fix:** select primitive defaults from Roslyn’s resolved `SpecialType`. Preserve `default(TypeSyntax)` when the resolved type cannot be represented faithfully.

4. **Snapshot equality misses constructor hoists erased by a block lambda — blocker, pre-existing and still unresolved.**

   ```csharp
   using System;

   class C {
       public class Q { }
       public class P {
           public P(Q q) { }
           public Func<int> F;
       }

       public static int Probe() {
           var p = new P(new Q()) { F = () => { return 0; } };
           return p.F();
       }
   }
   ```

   Predicted Calor, with no declaration for `_newQ`:

   ```calor
   §B{p} §NEW{P} §A _newQ
     F = §LAM{lam} §R 0 §/LAM{lam}
   §/NEW
   §R §C{p.F}
   ```

   The snapshot starts empty. Constructor-argument conversion adds `_newQ` and replaces the argument with its reference. Converting the block lambda calls `ConvertBlock`, which clears the queue. The final queue is empty again, so [PendingStatementsChanged](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aabc5edfd940e26ac/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:12426) returns false. The node reaches the emitter with a reference to a discarded hoist. The short lambda produces no emitter temporary, so the CSharpSource fallback also does not fire.

   This is not a regression versus main, but it shows that the snapshot fix detects a final-state difference, not every destructive queue change.

   **Suggested fix:** save and restore enclosing pending statements for block lambdas, as expression lambdas already do. Alternatively, isolate speculative creation conversion and track queue mutations.

Both constructor-argument collection guards address the round-2 truncation witnesses. The snapshot restoration fixes that round’s double-increment witness. I found no additional double-evaluation witness, member-level fallback trigger, or brace-scanning defect: `HoistToTempVar` is disabled at member scope, and `§CS` scanning skips comments and ordinary, verbatim, interpolated, and raw strings.

REQUEST CHANGES