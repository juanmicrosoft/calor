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

    // The converter side of #731: a `x = new …` reassignment must NOT round-trip to a
    // second declaration of `x` (CS0128). The emitter now emits each creation shape into a
    // fresh temp followed by §ASSIGN. All five collection/array shapes the converter can
    // produce are covered (#750 review finding 3 — previously only 1-D array was tested).
    public static IEnumerable<object[]> ReassignmentShapes() => new[]
    {
        new object[] { "array-1d",
            "int[] v = new int[5];\n        v = new int[] { 10, 20, 30 };" },
        new object[] { "array-2d",
            "int[,] v = new int[2, 2];\n        v = new int[,] { { 1, 2 }, { 3, 4 } };" },
        new object[] { "list",
            "System.Collections.Generic.List<int> v = new();\n        v = new System.Collections.Generic.List<int> { 1, 2, 3 };" },
        new object[] { "dictionary",
            "System.Collections.Generic.Dictionary<string,int> v = new();\n        v = new System.Collections.Generic.Dictionary<string,int> { { \"a\", 1 } };" },
        new object[] { "hashset",
            "System.Collections.Generic.HashSet<int> v = new();\n        v = new System.Collections.Generic.HashSet<int> { 1, 2 };" },
    };

    [Theory]
    [MemberData(nameof(ReassignmentShapes))]
    public void ConverterCollectionReassignment_RoundTripsToRoslynCleanCSharp(string name, string body)
    {
        var csharp = "public class Test\n{\n    void M()\n    {\n        " + body + "\n    }\n}\n";

        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success, $"[{name}] conversion failed");
        Assert.NotNull(result.CalorSource);

        var compiled = Program.Compile(result.CalorSource!);
        Assert.False(compiled.Diagnostics.HasErrors,
            $"[{name}] calor -i rejected converter output:\n" + result.CalorSource);

        var roslynErrors = ExemplarCompileChecker.RoslynErrors(compiled.GeneratedCode);
        Assert.DoesNotContain(roslynErrors, e => e.StartsWith("CS0128", StringComparison.Ordinal));
    }

    [Fact]
    public void KnownGap_ConverterFlattensBareBlocks_ProducesCalor0258() // #751
    {
        // Documented limitation surfaced by #731: the converter flattens standalone `{ }`
        // block scopes (dropping the braces), so two sibling blocks that each declare `x`
        // — valid, independent C# scopes — become a same-scope duplicate that Calor0258 now
        // rejects. Pre-#731 this was a silent downstream CS0128; the root cause is the
        // converter's block-scope fidelity gap, tracked in #751. Pins the CURRENT behavior
        // so closing #751 (round-trip stays clean) trips this test to be updated.
        var csharp = """
            public class Test
            {
                void M()
                {
                    { int x = 1; System.Console.WriteLine(x); }
                    { int x = 2; System.Console.WriteLine(x); }
                }
            }
            """;

        var result = new CSharpToCalorConverter().Convert(csharp);
        Assert.True(result.Success);
        Assert.NotNull(result.CalorSource);

        var compiled = Program.Compile(result.CalorSource!);
        Assert.Contains(compiled.Diagnostics, d => d.Code == DiagnosticCode.BindDuplicateInScope);
    }
}
