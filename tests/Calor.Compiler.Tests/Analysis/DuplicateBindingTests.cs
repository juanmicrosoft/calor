using Calor.Compiler;
using Calor.Compiler.Analysis;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Calor0258 (#731): two §B declarations reusing a name in the SAME scope emit
/// <c>int x = 1; int x = 2;</c> → CS0128. Now rejected. This became safe only once the
/// C#→Calor converter stopped emitting <c>arr = new T[]{…}</c> reassignments as a fresh
/// same-name §B creation block (it now emits §ASSIGN via a temp) — both sides are covered
/// here.
/// </summary>
public class DuplicateBindingTests
{
    private static bool HasDuplicate(string source)
    {
        var lex = new DiagnosticBag();
        var tokens = new Lexer(source, lex).TokenizeAllForParser();
        Assert.False(lex.HasErrors, "lexer: " + string.Join("; ", lex.Select(d => d.Message)));
        var parseBag = new DiagnosticBag();
        var module = new Parser(tokens, parseBag).Parse();
        Assert.False(parseBag.HasErrors, "parser: " + string.Join("; ", parseBag.Select(d => d.Message)));

        var bag = new DiagnosticBag();
        new BindValidationPass(bag, source, strictInference: true).Check(module);
        return bag.ToList().Any(d => d.Code == DiagnosticCode.BindDuplicateInScope);
    }

    [Fact]
    public void DuplicateImmutableBindingInSameScope_IsRejected()
    {
        Assert.True(HasDuplicate(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{x:i32} 1\n    §B{x:i32} 2\n    §R x\n"));
    }

    [Fact]
    public void SiblingBlocksReusingAName_IsAccepted()
    {
        // Distinct scopes — each block's `x` is independent, no CS0128.
        Assert.False(HasDuplicate(
            "§M{m:S}\n  §F{f:Do:pub} (bool:a, bool:b) -> i32\n" +
            "    §IF{i1} (== a true)\n      §B{x:i32} 1\n      §R x\n" +
            "    §IF{i2} (== b true)\n      §B{x:i32} 2\n      §R x\n    §R 0\n"));
    }

    [Fact]
    public void MutableRebindInSameScope_IsAccepted()
    {
        // A mutable §B reusing a live name is a reassignment (`x = 2`), not a duplicate.
        Assert.False(HasDuplicate(
            "§M{m:S}\n  §F{f:Do:pub} () -> i32\n    §B{~x} 1\n    §B{~x} 2\n    §R x\n"));
    }

    [Fact]
    public void InnerBlockShadowingOuter_IsNotADuplicate()
    {
        // Reusing an ENCLOSING-scope name is Calor0255 (CS0136), not Calor0258 (CS0128).
        Assert.False(HasDuplicate(
            "§M{m:S}\n  §F{f:Do:pub} (bool:flag) -> i32\n    §B{~x:i32} 0\n" +
            "    §IF{i1} (== flag true)\n      §B{x:str} \"hi\"\n      §R 0\n    §R x\n"));
    }

    [Fact]
    public void LocalReusingParameterName_IsNotADuplicate()
    {
        // A body-top-level local reusing a parameter name is CS0136 shadowing (params live
        // in an enclosing scope), not a same-scope CS0128 duplicate.
        Assert.False(HasDuplicate(
            "§M{m:S}\n  §F{f:Do:pub} (i32:x) -> i32\n    §B{x:i32} 9\n    §R x\n"));
    }

    [Fact]
    public void ConverterArrayReassignment_RoundTripsToRoslynCleanCSharp()
    {
        // The converter side of #731: `arr = new int[]{…}` must NOT round-trip to a second
        // `int[] arr = …` declaration (CS0128). It now emits §ASSIGN via a temp, so the
        // regenerated C# compiles under Roslyn.
        var csharp = """
            public class Test
            {
                void M()
                {
                    int[] arr = new int[5];
                    arr = new int[] { 10, 20, 30 };
                }
            }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);

        var compiled = Program.Compile(result.CalorSource!);
        Assert.False(compiled.Diagnostics.HasErrors,
            "calor -i rejected converter output:\n" + result.CalorSource);

        var roslynErrors = ExemplarCompileChecker.RoslynErrors(compiled.GeneratedCode);
        Assert.DoesNotContain(roslynErrors, e => e.StartsWith("CS0128", StringComparison.Ordinal));
    }
}
