# #1574 Codex verification pass (after round 3)

- Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (codex-cli 0.163.0); prompt asked for
  CLOSED/OPEN on each round-3 finding and close variants, at commit `f4aa83a2e`.
- Verdict: **NOT-VERIFIED**. Both round-3 findings are fixed for their reproducers but stay **OPEN (MAJOR)** for a
  close variant each. The review budget (3 rounds + 1 verification pass) is spent, so work stopped here as instructed.

## Findings (verbatim)

By inspection, both original reproducers are fixed, but both findings remain **OPEN** for close variants. No files changed; no builds or tests run.

1. **Round-3 finding 1 — OPEN, MAJOR.** The guard catches user types named `Str`, `I32`, `Bool`, and `Any`. Identity mappings such as `N.List<T>` retain their names; collection aliases such as `Dict` are rejected.

   However, [the guard](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11467) checks `named.Name`, whereas `CalorTypeName` uses minimal display, which can select a C# `using` alias. [Roslyn’s implementation](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/SymbolDisplay/SymbolDisplayVisitor.Types.cs#L180-L185) explicitly prefers such aliases.

   ```csharp
   using I32 = N.Widget;
   namespace N {
       public sealed class Widget {
           public int V;
           public Widget(int v) { V = v; }
           public static implicit operator int(Widget w) => w.V;
           public static implicit operator Widget(int v) => new Widget(v + 1);
       }
       public static class Probe {
           public static string Run() {
               N.Widget a;
               N.Widget b;
               a = b = new N.Widget(0);
               return a.V + ":" + b.V;
           }
       }
   }
   ```

   `Widget` passes the guard, but the temporary’s displayed type is `I32`, which becomes `int`. The resulting extra conversions change the expected result from `"0:0"` to `"1:1"` silently.

2. **Round-3 finding 2 — OPEN, MAJOR.** Direct `AllowNull`, `DisallowNull`, `MaybeNull`, and `NotNull` attributes on field, property, and parameter symbols now prevent lowering.

   However, [the attribute check](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11408) misses attributes on a property setter’s implicit parameter:

   ```csharp
   #nullable enable
   using System.Diagnostics.CodeAnalysis;

   public static class Probe {
       public static string Value {
           get => "";
           [param: AllowNull] set { }
       }

       public static string? Run() {
           return Value = null;
       }
   }
   ```

   Roslyn includes setter-parameter annotations in the property’s effective nullability contract. [Source](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Symbols/SymbolExtensions.cs). The property symbol itself has no matching attribute, so lowering still introduces `string _asg = null;` and CS8600.

The two new R3 rows cover only the original reproducers. No additional BLOCKING or MAJOR regression introduced by this commit was identified; the silent difference above survives the fix.

NOT-VERIFIED.


## Open items (not fixed; budget spent)

1. **Using-alias type names.** `CalorTypeName` uses `ToMinimalDisplayString`, which prefers a `using` alias
   (`using I32 = N.Widget;` displays `I32`, which Calor reads as `int`). `ShadowsMappedTypeName` checks only
   `named.Name`. Candidate fix: check the mapping round trip on the displayed string itself (every identifier in
   it), or display with `SymbolDisplayFormat.MinimallyQualifiedFormat` plus no alias use (format the type by
   walking it, not through `ToMinimalDisplayString`), and escalate when the display contains any name that a
   mapping rewrites.
2. **Setter-parameter nullability attributes.** `[param: AllowNull] set` on a property is not seen by the
   attribute check on the property symbol. Candidate fix: also check
   `property.SetMethod?.Parameters[0].GetAttributes()` (and, more simply, escalate any target whose property or
   setter parameter carries a `System.Diagnostics.CodeAnalysis` nullability attribute).
