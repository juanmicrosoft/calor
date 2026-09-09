using System.Text.RegularExpressions;
using Calor.Compiler;
using Calor.Enforcement.Tests;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Emitter regression for #732: a mutable §B rebind is a reassignment (<c>x = …</c>)
/// only while the name is visible; a rebind whose earlier declaration lives in a
/// now-closed sibling block re-declares (<c>int x = …</c>) instead — otherwise the
/// emitted C# reassigns an out-of-scope local (CS0103). The accumulator idiom (a
/// rebind of a still-live enclosing local) must stay a reassignment.
/// </summary>
public class SiblingRebindCodegenTests
{
    [Theory]
    [InlineData(false, 1, 10)]
    [InlineData(false, 2, 20)]
    [InlineData(true, 1, 10)]
    [InlineData(true, 2, 20)]
    public void StatementMatch_SiblingBindingsHaveExecutableLexicalScopes(bool mutable, int input, int expected)
    {
        var modifier = mutable ? "~" : "";
        var source = $$"""
            §M{m:SwitchScope}
              §F{f:Probe:pub} (i32:x) -> i32
                §W{w} x
                  §K 1
                    §B{ {{modifier}}v:i32} 10
                    §R v
                  §K _
                    §B{ {{modifier}}v:i32} 20
                    §R v
            """.Replace("§B{ ", "§B{");
        var generated = Emit(source);
        var sections = CSharpSyntaxTree.ParseText(generated).GetRoot()
            .DescendantNodes().OfType<SwitchSectionSyntax>().ToArray();
        Assert.Equal(2, sections.Length);
        Assert.All(sections, section => Assert.IsType<BlockSyntax>(Assert.Single(section.Statements)));
        Assert.Equal(expected, TestHarness.Execute(source, "Probe", [input]).ReturnValue);
    }

    [Theory]
    [InlineData(false, 1, 1, 10)]
    [InlineData(false, 1, 2, 20)]
    [InlineData(false, 2, 1, 30)]
    [InlineData(false, 2, 2, 40)]
    [InlineData(true, 1, 1, 10)]
    [InlineData(true, 1, 2, 20)]
    [InlineData(true, 2, 1, 30)]
    [InlineData(true, 2, 2, 40)]
    public void NestedStatementMatches_KeepSiblingBindingsIndependent(bool mutable, int x, int y, int expected)
    {
        var modifier = mutable ? "~" : "";
        var source = $$"""
            §M{m:NestedSwitchScope}
              §F{f:Probe:pub} (i32:x, i32:y) -> i32
                §W{outer} x
                  §K 1
                    §W{inner1} y
                      §K 1
                        §B{ {{modifier}}v:i32} 10
                        §R v
                      §K _
                        §B{ {{modifier}}v:i32} 20
                        §R v
                  §K _
                    §W{inner2} y
                      §K 1
                        §B{ {{modifier}}v:i32} 30
                        §R v
                      §K _
                        §B{ {{modifier}}v:i32} 40
                        §R v
            """.Replace("§B{ ", "§B{");
        Emit(source);
        Assert.Equal(expected, TestHarness.Execute(source, "Probe", [x, y]).ReturnValue);
    }

    [Theory]
    [InlineData(false, 1, 20)]
    [InlineData(false, 2, 30)]
    [InlineData(true, 1, 20)]
    [InlineData(true, 2, 30)]
    public void StatementMatch_BindingsAfterSwitchAndSiblingIfRetainTheirScopes(bool mutable, int input, int expected)
    {
        var modifier = mutable ? "~" : "";
        var source = $$"""
            §M{m:AfterSwitchScope}
              §F{f:Probe:pub} (i32:x) -> i32
                §B{~total:i32} 0
                §W{w} x
                  §K 1
                    §B{ {{modifier}}v:i32} 10
                    §B{~total} v
                  §K _
                    §B{ {{modifier}}v:i32} 20
                    §B{~total} v
                §B{after:i32} 3
                §IF{i} true
                  §B{ {{modifier}}v:i32} 7
                  §B{~total} (+ total v)
                §R (+ total after)
            """.Replace("§B{ ", "§B{");
        var generated = Emit(source);
        Assert.Equal(3, Regex.Matches(generated, @"\bint v = ").Count);
        Assert.Equal(expected, TestHarness.Execute(source, "Probe", [input]).ReturnValue);
    }

    [Fact]
    public void StatementMatch_ArmBindingCannotEscapeSwitch()
    {
        var result = Program.Compile("""
            §M{m:SwitchScope}
              §F{f:Probe:pub} (i32:x) -> i32
                §W{w} x
                  §K 1
                    §B{v:i32} 10
                  §K _
                    §B{v:i32} 20
                §R v
            """, "escape.calr");
        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
    }

    private static string Emit(string calor)
    {
        var result = Program.Compile(calor, "t.calr");
        Assert.False(result.Diagnostics.HasErrors,
            string.Join("\n", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        return result.GeneratedCode;
    }

    [Fact]
    public void SiblingMutableRebind_ReDeclaresInEachBlock()
    {
        var cs = Emit(
            "§M{m:S}\n  §F{f:Do:pub} (bool:a, bool:b) -> i32\n" +
            "    §IF{i1} (== a true)\n      §B{~x:i32} 1\n      §R x\n" +
            "    §IF{i2} (== b true)\n      §B{~x:i32} 2\n      §R x\n    §R 0\n");

        // Two independent declarations in the two sibling blocks — not a reassignment
        // of an out-of-scope local.
        Assert.Equal(2, Regex.Matches(cs, @"\bint x = ").Count);
    }

    [Fact]
    public void AccumulatorRebind_StaysAReassignment()
    {
        var cs = Emit(
            "§M{m:S}\n  §F{f:Fact:pub} (i32:n) -> i32\n    §B{~result} 1\n    §B{~i} 2\n" +
            "    §WH{w1} (<= i n)\n      §B{~result} (* result i)\n      §B{~i} (+ i 1)\n    §R result\n");

        // Declared once, reassigned inside the loop (no shadowing re-declaration).
        Assert.Single(Regex.Matches(cs, @"\b(var|int) result = 1"));
        Assert.Contains("result = checked(result * i)", cs);
        Assert.DoesNotContain("int result = checked(result * i)", cs);
    }
}
