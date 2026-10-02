using System.Text;
using Calor.Evaluation.Equivalence;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// #1276 discriminating controls for the registered pair oracle. Every control is a synthetic pair
/// written for this file; none is a registered benchmark pair, so these tests execute no
/// differential check on the registered denominator. Each negative control differs from a positive
/// control in one observable respect and must be caught for that reason.
/// </summary>
public class PairDifferentialOracleTests
{
    private const string CalorIsEven = "§M{m001:Ctl}\n  §F{f001:IsEven:pub} (i32:n) -> bool\n    §R (== (% n 2) 0)\n";
    private const string CalorHello = "§M{m001:Ctl}\n  §F{f001:Main:pub} () -> void\n    §E{cw}\n    §P \"Hello\"\n";
    private const string CalorDivide = "§M{m001:Ctl}\n  §F{f001:Divide:pub} (i32:a, i32:b) -> i32\n    §R (/ a b)\n";

    private static string CSharp(string body) => $"namespace Control {{ public static class ControlModule {{ {body} }} }}";

    [Fact]
    public void EquivalentControlIsEquivalent()
    {
        var verdict = Run(CalorIsEven, CSharp("public static bool IsEven(int n) => n % 2 == 0;"));
        Assert.Equal(("EQUIVALENT", "AGREE"), (verdict.Disposition, verdict.Reason));
        Assert.True(verdict.InputCount > PairDifferentialOracle.RandomTuplesPerMember);
        Assert.Matches("^[0-9a-f]{64}$", verdict.ObservationsSha256!);
    }

    [Fact]
    public void ContainerAndParameterNamesDoNotMatter()
    {
        var verdict = Run(CalorIsEven, "public static class Other { public static bool ISEVEN(int value) => value % 2 == 0; }");
        Assert.Equal("EQUIVALENT", verdict.Disposition);
    }

    [Fact]
    public void OutputDifferingOnOneBoundaryInputIsNotEquivalent()
    {
        var verdict = Run(CalorIsEven, CSharp("public static bool IsEven(int n) => n != 15 && n % 2 == 0 || n == 15;"));
        Assert.Equal(("NOT-EQUIVALENT", "OBSERVATION_MISMATCH"), (verdict.Disposition, verdict.Reason));
        Assert.Contains(verdict.Witnesses, w => w.Contains("(15)", StringComparison.Ordinal));
    }

    [Fact]
    public void ReturningWhereTheOtherArmThrowsIsNotEquivalent()
    {
        var verdict = Run(CalorDivide, CSharp("public static int Divide(int a, int b) => b == 0 ? 0 : a / b;"));
        Assert.Equal(("NOT-EQUIVALENT", "OBSERVATION_MISMATCH"), (verdict.Disposition, verdict.Reason));
        Assert.Contains(verdict.Witnesses, w => w.Contains("throw(", StringComparison.Ordinal));
    }

    [Fact]
    public void ThrowingOnTheSameInputsIsTheSameFailureClass()
    {
        var verdict = Run(CalorDivide, CSharp("public static int Divide(int a, int b) => a / b;"));
        Assert.Equal("EQUIVALENT", verdict.Disposition);
    }

    [Fact]
    public void DifferentStdoutIsNotEquivalent()
    {
        // Main() and Main(string[]) are the same entry point.
        Assert.Equal("EQUIVALENT", Run(CalorHello, CSharp("public static void Main(string[] args) { System.Console.WriteLine(\"Hello\"); }")).Disposition);
        var verdict = Run(CalorHello, CSharp("public static void Main() { System.Console.WriteLine(\"Hello!\"); }"));
        Assert.Equal(("NOT-EQUIVALENT", "OBSERVATION_MISMATCH"), (verdict.Disposition, verdict.Reason));
    }

    [Fact]
    public void CsvParserShapedSurfaceMismatchIsNotEquivalent()
    {
        // The #1276 witness shape: same member name, different input type, plus a member only one arm has.
        const string calor = "§M{m001:Ctl}\n  §F{f001:FieldCount:pub} (i32:commaCount) -> i32\n    §R (+ commaCount 1)\n";
        var verdict = Run(calor, CSharp("public static int FieldCount(string line) => line.Split(',').Length;"
            + " public static string[] Parse(string csv) => csv.Split('\\n');"));
        Assert.Equal(("NOT-EQUIVALENT", "SURFACE_MISMATCH"), (verdict.Disposition, verdict.Reason));
        Assert.Contains("csharp-only parse(System.String)", verdict.Detail, StringComparison.Ordinal);
        Assert.Contains("calor-only fieldcount(System.Int32)", verdict.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFailureInEitherArmIsNotEquivalent()
    {
        Assert.Equal(("NOT-EQUIVALENT", "BUILD_FAILED_CSHARP"),
            Reason(Run(CalorIsEven, CSharp("public static bool IsEven(int n) => n % 2 == 0"))));
        Assert.Equal(("NOT-EQUIVALENT", "BUILD_FAILED_CALOR"),
            Reason(Run("§M{m001:Ctl}\n  §F{f001:IsEven:pub} (i32:n) -> bool\n    §R (== (% n 2)\n", CSharp("public static bool IsEven(int n) => n % 2 == 0;"))));
    }

    [Fact]
    public void ChangedBytesAreUnclassifiedNotRun()
    {
        var calor = Encoding.UTF8.GetBytes(CalorIsEven);
        var csharp = Encoding.UTF8.GetBytes(CSharp("public static bool IsEven(int n) => n % 2 == 0;"));
        var verdict = PairDifferentialOracle.Evaluate(new OraclePairInput("ctl", "ctl.calr", calor,
            PairDifferentialOracle.Sha256Hex(calor), "ctl.cs", csharp, new string('0', 64)));
        Assert.Equal(("UNCLASSIFIED", "IDENTITY_MISMATCH"), Reason(verdict));
    }

    [Fact]
    public void NondeterministicArmIsUnclassified()
    {
        var verdict = Run("§M{m001:Ctl}\n  §F{f001:Next:pub} () -> i32\n    §R 0\n", CSharp("private static int _n; public static int Next() => _n++;"));
        Assert.Equal(("UNCLASSIFIED", "NONDETERMINISTIC"), Reason(verdict));
    }

    [Fact]
    public void HangingArmIsUnclassifiedTimeout()
    {
        var verdict = Run(CalorIsEven, CSharp("public static bool IsEven(int n) { while (n == n) { } return true; }"));
        Assert.Equal(("UNCLASSIFIED", "TIMEOUT"), Reason(verdict));
    }

    [Fact]
    public void EmptySurfaceIsUnclassifiedAndUnsupportedTypesAreRejected()
    {
        const string calor = "§M{m001:Ctl}\n  §F{f001:Helper:priv} () -> i32\n    §R 0\n";
        Assert.Equal(("UNCLASSIFIED", "SURFACE_EMPTY"), Reason(Run(calor, "internal static class Hidden { public static int Helper() => 0; }")));
        Assert.False(InputGenerator.Supports(typeof(TimeSpan).GetMethod("FromTicks", [typeof(long)])!));
        Assert.True(InputGenerator.Supports(typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!));
    }

    [Fact]
    public void InputsDependOnlyOnPairMemberAndTypes()
    {
        var method = typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!;
        var first = InputGenerator.Generate("A/Pair", "max(System.Int32,System.Int32)->System.Int32", method).Select(InputGenerator.Render).ToList();
        var again = InputGenerator.Generate("A/Pair", "max(System.Int32,System.Int32)->System.Int32", method).Select(InputGenerator.Render).ToList();
        var other = InputGenerator.Generate("B/Pair", "max(System.Int32,System.Int32)->System.Int32", method).Select(InputGenerator.Render).ToList();
        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Contains("(2147483647, -2147483648)", first);
        Assert.Equal(15 * 15 + PairDifferentialOracle.RandomTuplesPerMember, first.Count);
    }

    private static (string, string) Reason(OracleVerdict verdict) => (verdict.Disposition, verdict.Reason);

    private static OracleVerdict Run(string calor, string csharp)
    {
        var calorBytes = Encoding.UTF8.GetBytes(calor);
        var csharpBytes = Encoding.UTF8.GetBytes(csharp);
        return PairDifferentialOracle.Evaluate(new OraclePairInput("control", "control.calr", calorBytes,
            PairDifferentialOracle.Sha256Hex(calorBytes), "control.cs", csharpBytes, PairDifferentialOracle.Sha256Hex(csharpBytes)));
    }
}
