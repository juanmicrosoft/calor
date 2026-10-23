# #1574 Codex review — round 3 (last review round)

- Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (codex-cli 0.163.0); prompt pointed at
  commit `90eb0545f` and the round-1/2 records.
- Verdict: **CHANGES-REQUESTED** (2 MAJOR, 0 BLOCKING). Round-2 fixes confirmed for their reproducers.

## Findings (verbatim)

By inspection, the three round-2 fixes address their original reproducers. Two compilation regressions remain. No files changed; no builds or tests run.

1. **MAJOR — The type-name guard misses Calor aliases.** [RoslynSyntaxVisitor.cs:11461](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11461)

   ```csharp
   namespace N {
       public sealed class Str { }
       public static class Probe {
           public static bool Run() {
               N.Str a;
               N.Str b;
               a = b = new N.Str();
               return object.ReferenceEquals(a, b);
           }
       }
   }
   ```

   `CSharpToCalor("Str")` returns `"Str"`, so the guard accepts it. Roslyn’s minimal display names the temporary’s type `Str`, but the case-insensitive `CalorToCSharp` map converts that to built-in `string`. The resulting temporary cannot accept `new N.Str()`. Check the complete type round trip or retain qualification.

2. **MAJOR — Typed temporaries lose `[AllowNull]` write contracts.** [RoslynSyntaxVisitor.cs:11442](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11442)

   ```csharp
   #nullable enable
   using System.Diagnostics.CodeAnalysis;

   public static class Probe {
       [AllowNull] public static string Value = "";

       public static string? Run() {
           return Value = null;
       }
   }
   ```

   The original assignment is warning-free because `[AllowNull]` permits writing null. The field’s type remains non-nullable `string`, so `TempTypeName` returns `str` immediately. Lowering introduces the equivalent of `string _asg = null;`, raising CS8600 and breaking warnings-as-errors builds. Account for the target’s write contract or preserve this assignment as C#.

CHANGES-REQUESTED


## Disposition

| # | Finding | Fix | Witness |
|---|---|---|---|
| 1 | A user type named like a Calor alias (`N.Str`) is read back as `string` | `ShadowsMappedTypeName` now requires the simple name to survive `TypeMapper.CSharpToCalor`, `TypeMapper.CalorToCSharp` and `AttributeHelper.ExpandType` unchanged | `R3-type-named-like-calor-alias` (preserved) |
| 2 | `[AllowNull]` write contract lost by the typed temp | A target carrying `AllowNull`, `DisallowNull`, `MaybeNull` or `NotNull` is not a lowerable target; the statement stays C# | `R3-allow-null-target` (preserved) |

The verification pass (`verification.md`) found both fixed for their reproducers but OPEN for a close variant each.
