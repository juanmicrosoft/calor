using System.Text.RegularExpressions;
using Calor.Compiler;
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
        Assert.Contains("result = result * i", cs);
        Assert.DoesNotContain("int result = result * i", cs);
    }
}
