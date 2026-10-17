using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.25 F5 (#1132), narrowed: array instance members keep the effect results they have on
/// main. F5's first PR (#1521) certified System.Array members for array receivers; that
/// certification was dropped, so each member below gets exactly the result it gets on
/// origin/main at 043a2ed38. The expected values were recorded by running this table, with
/// the same diagnostics compared message for message, against that commit.
/// </summary>
public class ArrayMemberEffectParityTests
{
    // An extension named like a System.Array member, and a §CSHARP helper that writes.
    private const string Extensions =
        "§CSHARP{public static class Ext { public static int GetLength(this int[] xs, long d) { System.Console.WriteLine(d); return xs.Length; } public static int Touch(this int[] xs) { System.Console.WriteLine(1); return xs.Length; } }}§/CSHARP";

    [Theory]
    [InlineData("i32[,]", "§B{x} §C{g.GetLength} §A 0 §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.GetLongLength} §A 0 §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.GetLowerBound} §A 0 §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.GetUpperBound} §A 0 §/C", true)]
    [InlineData("i32[,]", "§B{x} g.Rank", true)]
    [InlineData("i32[,]", "§B{x} g.LongLength", true)]
    [InlineData("i32[,]", "§B{x} §C{g.GetValue} §A 0 §A 0 §/C", true)]
    [InlineData("i32[,]", "§C{g.SetValue} §A 1 §A 0 §A 0 §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.Clone} §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.GetEnumerator} §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.ToString} §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.GetHashCode} §/C", true)]
    [InlineData("i32[,]", "§B{x} §C{g.Equals} §A g §/C", true)]
    [InlineData("i32[,]", "§C{g.Initialize} §/C", true)]
    [InlineData("i32[,]", "§C{g.CopyTo} §A g §A 0 §/C", true)]
    [InlineData("i32[]", "§B{x} §C{g.GetLength} §A 0 §/C", true)]
    [InlineData("i32[]", "§B{x} §C{g.GetLength} §A (cast i64 0) §/C", true)]
    [InlineData("i32[]", "§B{x} §C{g.GetValue} §A g §/C", true)]
    [InlineData("i32[]", "§B{x} §C{g.Touch} §/C", true)]
    [InlineData("i32[]", "§C{g.Initialize} §/C", true)]
    [InlineData("System.Array", "§B{x} §C{g.GetLength} §A 0 §/C", false)]
    [InlineData("System.Array", "§C{g.Initialize} §/C", false)]
    [InlineData("System.Threading.Tasks.ValueTask[]", "§C{g.Initialize} §/C", false)]
    public void ArrayInstanceMembers_HaveMainsEffectResult(string receiver, string statement, bool unknown)
    {
        var calor = $"§M{{m1:T}}\n  {Extensions}\n  §CL{{c1:G:pub:stat}}\n    §MT{{m2:Go:pub:stat}} ({receiver}:g) -> void\n      §E{{}}\n      {statement}\n";
        var compilation = Program.Compile(calor, "parity.calr", new CompilationOptions { StatusWriter = TextWriter.Null });
        var effects = compilation.Diagnostics
            .Where(d => d.Code.StartsWith("Calor04", StringComparison.Ordinal))
            .Select(d => d.Code)
            .ToList();
        // On main: an unknown external call (Calor0411) that the pure function does not
        // declare (Calor0410, effect 'unknown'); nothing else. A receiver typed System.Array
        // (or ValueTask[]) resolves through its manifest or namespace default, with no
        // diagnostic - main's behavior, kept as it is.
        string[] expected = unknown ? ["Calor0411", "Calor0410"] : [];
        Assert.Equal(expected, effects);
        Assert.Equal(unknown, compilation.Diagnostics.Errors.Any(d =>
            d.Code == "Calor0410" && d.Message.Contains("'unknown'", StringComparison.Ordinal)));
        Assert.DoesNotContain(compilation.Diagnostics.Errors, d => d.Code != "Calor0410");
    }
}
