# Codex review, round 1 (#1139)

Reviewer: OpenAI Codex (`codex exec -s read-only --ephemeral`, reasoning effort high), cross-family.
Input: the branch diff against origin/main at a8bf3332 (generated evidence excluded).

## Findings (verbatim)

1. **BLOCKING — `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:4254` and `:4401`: partial iterator properties/indexers still fail or depend on rescue.** Legal reproducer:

   ```csharp
   using System.Collections.Generic;
   public partial class C
   {
       public partial IEnumerable<int> P { get; }
       public partial IEnumerable<int> P { get { yield return 1; } }
   }
   ```

   The implementation becomes interop, but the defining declaration becomes an ordinary auto-property. Reproduced through MCP: default conversion fails with `CS9249`/`CS0102`; `passthroughOnError` succeeds only through `post-validation-fallback`; adding `moduleName: "Custom"` fails again. Partial indexers similarly fail with `CS0501`/`CS9249`/`CS0111`.

   **Fix:** preserve both parts at converter level, including defining declarations without yields, or correctly represent their partial relationship. Add property/indexer regressions covering same-class and split declarations across the required surfaces, asserting successful compilation and no rescue.

2. **NON-BLOCKING — `src/Calor.Compiler/Migration/RoslynSyntaxVisitor.cs:4254`: earlier capability detection bypasses the promised feature label.** A getter containing `using var d = new MemoryStream(); yield return 1;` is intercepted at `:3221` before this guard. Reproduced output is preserved correctly, but reports only `using-declaration`, not `iterator-accessor`. Default interface iterator getters likewise report `interface-property-semantics`. The unconditional reporting claims in `CHANGELOG.md:14` and `website/content/cli/convert.mdx:150` therefore exceed the implementation.

   **Fix:** record iterator-accessor classification before earlier preservation decisions, without duplicating preservation counts, or qualify the documentation to acknowledge existing preservation reasons. Add overlapping-feature reporting tests.

3. **NIT — `CHANGELOG.md:17` and `website/content/changelog.mdx:23`: the execution-surface count is incorrect.** Committed evidence records execution matches on six surfaces: three CLI/migration surfaces and three MCP surfaces. The seventh, `cli-no-fallback`, refuses conversion and has no execution result.

   **Fix:** say “six surfaces,” or explicitly identify and supply evidence for the additional executed surface.

REQUEST-CHANGES
