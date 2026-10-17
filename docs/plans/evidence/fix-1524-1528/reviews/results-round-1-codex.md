Read-only review; the outputs below are predictions traced through the code, not executed probes.

1. **Non-collection initializer values still move before the constructor — major-silent.**

   ```csharp
   static int i;
   class P {
       public int A;
       public P() { i = 10; }
   }
   static int Probe() {
       i = 0;
       var p = new P { A = i++ };
       return p.A;
   }
   ```

   Predicted lowering:

   ```csharp
   var saved = i;
   i = saved + 1;
   var p = new P { A = saved };
   ```

   C# returns `10`; the converted program returns `0`. `RequiresVerbatimObjectInitializer` accepts scalar RHS expressions, but `ConvertPostfixUnaryExpression` adds pending statements that run before the entire creation. Assignments and nested call/constructor argument hoisting have the same exposure.

   **Fix:** capture pending statements while converting constructor arguments and initializer values. Preserve the entire creation if those statements cannot remain at their original evaluation point. Checking only collection syntax is insufficient. Add postfix, assignment, and nested-constructor ordering controls.

2. **Parentheses bypass `IsHoistedCollectionValue`, including the target-typed path that loses elements — major-silent.**

   ```csharp
   class P { public List<int> L { get; set; } }
   static int Probe() {
       P p = new() { L = (new List<int> { 5 }) };
       return p.L.Count;
   }
   ```

   Predicted native output contains:

   ```calor
   §NEW{P}
     L = §NEW{List<i32>} §A 5 §/NEW
   §/NEW
   ```

   This generates `new List<int>(5)`, an empty list with capacity five. The result changes from `1` to `0`, without an initializer loss. The guard sees a `ParenthesizedExpressionSyntax`; conversion subsequently unwraps it and calls `ConvertBlockLevelCollectionToNew`.

   The explicit-new equivalent also bypasses the guard and hoists the list. In a property or field default, that can leave a reference to a temporary with no declaration.

   **Fix:** unwrap parentheses before classification, and validate the converted AST and pending statements rather than maintaining a separate syntax-only approximation. Add explicit and target-typed controls, including member defaults.

3. **Constant operands do not establish that collection initialization has no effects — major-silent.**

   These declarations can be nested inside class `C`:

   ```csharp
   public static string Trace = "";
   public class V {
       public static implicit operator V(int n) {
           Trace += "v";
           return new V();
       }
   }
   public class P {
       public List<V> L;
       public P() { Trace += "k"; }
   }
   public static string Probe() {
       var p = new P { L = new List<V> { 1 } };
       return Trace;
   }
   ```

   Predicted lowering:

   ```csharp
   var list = new List<V> { 1 };
   var p = new P { L = list };
   ```

   Original order is `"kv"`; the lowered order is `"vk"`. `HasOnlyConstantOperands` accepts literal `1`, but its implicit conversion to `V` executes user code. Custom collection constructors, `Add` methods, and hashing can likewise have effects despite constant element expressions.

   **Fix:** prove the relevant constructors, conversions, and insertion operations safe before moving them, or preserve the creation. A constant-expression check alone cannot justify this hoist.

4. **List specialization runs before the new guard and mis-targets member assignments — major-silent.**

   ```csharp
   static int Capacity;
   static string Probe() {
       var l = new List<int> { Capacity = 20 };
       return $"{l.Count}:{Capacity}";
   }
   ```

   Predicted lowering:

   ```csharp
   Capacity = 20;
   var l = new List<int> { 20 };
   ```

   C# initializes `l.Capacity` and returns `"0:0"`. The conversion assigns the containing class’s field, inserts an element, and returns `"1:20"`.

   `ConvertObjectCreation` dispatches to `ConvertListCreation` before `RequiresVerbatimObjectInitializer`. That converter treats every initializer expression as a collection element; assignment conversion produces the misplaced pending write. Without the outer `Capacity` field, this instead produces invalid code.

   **Fix:** distinguish object-member initializers from collection initializers before List/HashSet specialization. Route `Capacity = 20` through native member assignment or interop preservation. Add a same-named outer-member negative control.

5. **Preserving a creation inside interpolation still permits evaluation to move before earlier holes — major-silent.**

   ```csharp
   static string Trace = "";
   static int X() { Trace += "x"; return 1; }
   class P {
       public int A;
       public P() { Trace += "k"; }
   }
   static string Probe() {
       var text = $"{X()}{new P { A = 5 }.A}";
       return Trace;
   }
   ```

   Predicted output is structurally:

   ```calor
   §B{~_hoist000} §CS{new P { A = 5 }}
   §B{text} "${X()}${_hoist000.A}"
   ```

   C# returns `"xk"`; the converted program returns `"kx"`. The new preservation branch retains the initializer, but `CalorEmitter.Visit(FieldAccessNode)` hoists its section-bearing receiver before the interpolation. Initializer expressions containing string literals have another hoist route in `ConvertInterpolatedString`.

   **Fix:** preserve evaluation regions across the complete interpolation, including both visitor and emitter hoists. The existing interpolation test embeds the interpolation in binary concatenation; that exercises a protected emitter context and misses this direct-interpolation case.

6. **Auto-property defaults still run in the wrong declaration order — major-silent.**

   ```csharp
   class P { public int A; }
   public static P Prop { get; } = new P { A = Later };
   public static int Later = 7;
   public static int Probe() => Prop.A;
   ```

   C# returns `0`: `Prop` initializes before `Later`. Ordinary class emission groups fields before properties, predicting:

   ```csharp
   public static int Later = 7;
   public static P Prop { get; } = new P { A = Later };
   ```

   The converted program returns `7`. Removing the getter rewrite restores object identity, but does not preserve initialization order. `Items` preserves ordering only on the paths that populate it; ordinary classes still use grouped emission.

   **Fix:** retain source member order for every class through both emitters. Add static and instance field/property interleaving tests with distinguishable effects. The current holder test’s two identical `"k"` constructor effects cannot detect their reversal.

7. **The float fix introduces `default(float)` and `default(decimal)` regressions — blocker.**

   ```csharp
   public static object FloatProbe() => default(float) + 2f;
   public static decimal DecimalProbe() => default(decimal) + 2m;
   ```

   `ConvertDefaultExpression` creates an ordinary `FloatLiteralNode(0.0)` for both types. Previously it emitted integer `0`; now the predicted expressions are:

   ```calor
   (+ 0.0 SINGLE:2)
   (+ 0.0 DEC:2)
   ```

   The first changes a boxed `Single` into a boxed `Double`. The second generates `double + decimal`, which cannot compile. Both expressions worked with the previous integer-zero emission.

   **Fix:** produce an `IsSingle` float node for `default(float)` and a `DecimalLiteralNode` for `default(decimal)`, using semantic types to cover aliases too. Extend the existing `default(double)` test to both types.

   The current runtime oracle stringifies results: float, double, and decimal `2.5` all compare equal. Assert runtime types and use precision-sensitive values as well.

8. **Attribute doubles still become integers through a separate formatter — major-silent.**

   ```csharp
   class KindAttribute : Attribute {
       public string Value;
       public KindAttribute(int x) { Value = "int"; }
       public KindAttribute(double x) { Value = "double"; }
   }
   [Kind(2.0)]
   class Marked { }
   ```

   Predicted Calor is `[@Kind(2)]`, followed by generated C# `[Kind(2)]`. Reflection observes `"int"` instead of `"double"`.

   Attribute arguments bypass `Visit(FloatLiteralNode)`. `CalorAttributeArgument.FormatSingleValue` still formats doubles and floats with plain `ToString()`, losing width and also depending on culture.

   **Fix:** preserve numeric type and invariant formatting in attribute arguments across both emitters, or retain their original C# expressions. Add overload-discriminating attribute tests for `2.0` and `2f`.

REQUEST CHANGES