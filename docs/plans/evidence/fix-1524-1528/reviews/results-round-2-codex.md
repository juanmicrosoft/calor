Read-only review; outputs below are code-traced predictions, not executed probes.

1. **Rollback can retain an increment and then execute it again — major-silent, newly introduced.**

   [Rollback code](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aabc5edfd940e26ac/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:12403)

   ```csharp
   using System;

   class C {
       static int ignored, i, j;
       public class P {
           public Func<int> F;
           public int A, B;
       }
       static int Use(int x, int y, P p) => 100 * i + 10 * j + p.A;
       public static int Probe() =>
           Use(ignored = 0, ignored = 0,
               new P { F = () => { return 0; }, A = i++, B = j++ });
   }
   ```

   Predicted converted C#, with generated names simplified:

   ```csharp
   var saved = i;
   i = saved + 1;
   return Use(0, 0,
       new P { F = () => { return 0; }, A = i++, B = j++ });
   ```

   `pendingBefore` is `2`, from the first two arguments. Converting the block lambda calls `ConvertBlock`, which clears that queue. The two postfix expressions then add four statements. Rollback removes only entries starting at index `2`, retaining the first increment before preserving the creation verbatim.

   Original C# returns **110**; the branch returns **211**. Main increments each counter once and returns **110** for this repro. Thus, this is a regression introduced by the rollback, despite its `InteropPreserved` loss.

   **Suggested fix:** isolate each creation’s speculative pending statements and restore the enclosing queue from a snapshot. Block lambdas and anonymous methods must save and restore enclosing pending state, as expression lambdas already do. A count assumes an append-only queue that this visitor does not provide.

2. **Emitter-only hoists still move initializer evaluation before the constructor — major-silent, incomplete round-1 fix.**

   [Emitter hoist](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aabc5edfd940e26ac/src/Calor.Compiler/Migration/CalorEmitter.cs:3154)

   ```csharp
   class C {
       static string Trace = "";
       public class P {
           public int A;
           public P() { Trace += "p"; }
       }
       public class Q {
           public int A;
           public Q() { Trace += "q"; }
       }
       public static string Probe() {
           var p = new P { A = new Q { A = 5 }.A };
           return Trace;
       }
   }
   ```

   Predicted Calor:

   ```calor
   §B{~_hoist000} §NEW{Q}
     A = 5
   §/NEW
   §B{p} §NEW{P}
     A = _hoist000.A
   §/NEW
   ```

   Both creations finish visitor conversion without adding pending statements, so rollback never triggers. Later, `CalorEmitter.Visit(FieldAccessNode)` hoists the section-bearing `Q` receiver. Original C# returns **"pq"**; converted C# returns **"qp"**, with no `object-initializer` loss.

   Preserving complete interpolated strings fixes the round-1 interpolation witness, but does not fix this ordinary initializer evaluation region.

   **Suggested fix:** keep initializer RHS emission within an evaluation region that prevents outward hoisting, or preserve the complete creation when emission requires such a hoist. Checking `_pendingStatements` alone cannot establish evaluation-order preservation.

3. **Target-typed constructor arguments still discard collection initializer elements — major-silent, remaining #1524 gap.**

   [Target-typed argument conversion](/Users/juanrivera/repos/juanmicrosoft/calor/.claude/worktrees/agent-aabc5edfd940e26ac/src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:10367)

   ```csharp
   using System.Collections.Generic;

   class C {
       public class P {
           public List<int> L;
           public int A;
           public P(List<int> l) { L = l; }
       }
       public static int Probe() {
           P p = new(new List<int> { 5 }) { A = 1 };
           return p.L.Count;
       }
   }
   ```

   Predicted converted C#:

   ```csharp
   var list = new List<int>(5);
   P p = new P(list) { A = 1 };
   return p.L.Count;
   ```

   `ConvertImplicitObjectCreation` calls `ConvertBlockLevelCollectionToNew` for the argument, turning element `5` into capacity `5`. This adds no visitor pending statement; `initializerLosesElements` checks only member RHS values. Original C# returns **1**; converted C# returns **0**, without an initializer-preservation loss.

   This argument fallback existed on main, but remains an initializer-loss path within the requested fix. The explicit outer-creation path now preserves this shape because its argument hoist triggers rollback.

   **Suggested fix:** detect block-level collections in constructor arguments before the lossy fallback and preserve the enclosing creation. Apply the same safeguard to both explicit and target-typed paths.

The collection-specialization gating, direct interpolation preservation, bare `default(float)`/`default(decimal)`, and positive floating attribute formatting address their exact round-1 witnesses. I found no new regression in the changed numeric formatting or inspected raw-string scanning paths. Finding 6 remains pre-existing; I did not reopen it.

REQUEST CHANGES