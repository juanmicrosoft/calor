# #1574 Codex review — round 2

- Command: `codex exec -s read-only --ephemeral -c model_reasoning_effort="high"` (codex-cli 0.163.0); prompt listed the
  round-1 dispositions and asked for verification plus any remaining silent difference.
- Reviewed: the working tree after the round-1 fixes (base `24bc55533`).
- Verdict: **CHANGES-REQUESTED** (3 MAJOR, 0 BLOCKING). Round-1 fixes confirmed for their reproducers.

## Findings (verbatim)

By inspection, the six round-1 fixes address their original reproducers. Three gaps remain. No files were modified, and no build or tests were run.

1. **MAJOR — A local can be assigned before its declaration.** [RoslynSyntaxVisitor.cs:11359](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11359)

   The initializer allowance accepts assignments to the local being declared:

   ```csharp
   public static class Probe {
       public static int Run() {
           int x = x = 1;
           return x;
       }
   }
   ```

   This is valid C#. Lowering flushes `temp = 1; x = temp;` before `int x = temp;`, producing a use before declaration. Reject this case or declare the local before flushing its assignments.

2. **MAJOR — Minimal type names can change the temporary’s type.** [RoslynSyntaxVisitor.cs:11467](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11467)

   ```csharp
   namespace N {
       public sealed class String { }
       public static class Probe {
           public static bool Run() {
               N.String a;
               N.String b;
               a = b = new N.String();
               return object.ReferenceEquals(a, b);
           }
       }
   }
   ```

   Roslyn can minimally display `N.String` as `String`. `TypeMapper.CSharpToCalor("String")` then produces `str`, so the new temporary becomes built-in `string`, which cannot accept `new N.String()`. Map symbols by identity or retain sufficient qualification.

3. **MAJOR — Oblivious reference targets gain CS8600.** [RoslynSyntaxVisitor.cs:11466](src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:11466)

   Convert this with evaluated `NullableContextOptions.Enable`:

   ```csharp
   #nullable enable
   public static class Probe {
   #nullable disable annotations
       static string value;
   #nullable enable annotations
       public static object? Run() {
           return value = null;
       }
   }
   ```

   The original assignment to oblivious `value` is warning-free. Declaration mapping preserves that target as nullable, but `CalorTypeName` emits a non-nullable `string` temporary initialized with `null`. This introduces CS8600 and breaks builds with warnings treated as errors. Apply the declaration mapper’s oblivious-reference handling to temporary types.

CHANGES-REQUESTED


## Disposition

| # | Finding | Fix | Witness |
|---|---|---|---|
| 1 | `int x = x = 1` assigns the local before its declaration | The site check rejects a chain target that is the local being declared | `R2-self-initializer` (preserved) |
| 2 | A user type named like a mapped C# type (`N.String`) becomes `str` | `IsExpressibleType` rejects a non-`System` type whose simple name `TypeMapper` maps to something else | `R2-type-named-like-builtin` (preserved) |
| 3 | Oblivious reference target gains CS8600 | `TempTypeName` annotates the temp as nullable when the converter declares the target nullable (evaluated or known-disabled annotation context, or a target declared elsewhere); nested oblivious shapes escalate | `R2-oblivious-target` (native, `?str` temp) and `ObliviousTarget_TempIsNullable_AndCompilesWithoutCS8600` |
