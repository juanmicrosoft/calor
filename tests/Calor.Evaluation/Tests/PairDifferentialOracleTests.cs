using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void FailureMustMatchByExceptionType()
    {
        Assert.Equal("EQUIVALENT", Run(CalorDivide, CSharp("public static int Divide(int a, int b) => a / b;")).Disposition);
        var verdict = Run(CalorDivide, CSharp("public static int Divide(int a, int b) => b == 0 ? throw new System.ArgumentException() : a / b;"));
        Assert.Equal(("NOT-EQUIVALENT", "OBSERVATION_MISMATCH"), Reason(verdict));
    }

    [Theory]
    [InlineData("public static void Sort(int[] xs) => System.Array.Sort(xs);", "public static void Sort(int[] xs) { }", "OBSERVATION_MISMATCH")]
    [InlineData("public static void Save(string s) => System.IO.File.WriteAllText(\"out.txt\", s);", "public static void Save(string s) { }", "OBSERVATION_MISMATCH")]
    [InlineData("public static int P => 1; public static int F(int x) => x;", "public static int F(int x) => x;", "UNSUPPORTED_SURFACE")]
    [InlineData("public static void Sort(int[] xs) => System.Array.Sort(xs);", "public static void Sort(int[] xs) => System.Array.Sort(xs);", "AGREE")]
    [InlineData("public static IEnumerable<int> F(int n) => throw new InvalidOperationException();", "public static IEnumerable<int> F(int n) { if (n == n) throw new InvalidOperationException(); yield break; }", "OBSERVATION_MISMATCH")]
    [InlineData("public static int F() => 1; } public class Box { public Box() => throw new Exception(); } static class Pad {", "public static int F() => 1;", "UNSUPPORTED_SURFACE")]
    [InlineData("private static int _n; public static int Next() => _n++;", "public static int Next() => 0;", "NONDETERMINISTIC")]
    [InlineData("public static bool F(int n) { while (n == n) System.Threading.Thread.Sleep(50); return true; }", "public static bool F(int n) => true;", "TIMEOUT")]
    public void EffectsSurfacesAndRunawayArmsAreClassified(string left, string right, string reason)
    {
        var pair = new OraclePairInput("control", "a", [], "", "b", [], "");
        Assert.Equal(reason, PairDifferentialOracle.EvaluateCSharpArms(pair, CSharp(left), CSharp(right)).Reason);
    }

    [Fact]
    public void SecondRunReconcilesAndChecksTheKnownWitness()
    {
        static JsonArray Rows(params (string Id, string Disposition)[] rows) => new(rows.Select(r => (JsonNode?)JsonSerializer.SerializeToNode(
            new OracleVerdict(r.Id, r.Disposition, "R", [], 1, null, [], ""))).ToArray());
        var header = new JsonObject { ["registrationCommit"] = new string('a', 40), ["environment"] = "env" };
        JsonObject First(JsonArray rows, string commit = "") => new()
        {
            ["registrationCommit"] = commit.Length > 0 ? commit : new string('a', 40), ["environment"] = "env", ["runResults"] = rows.DeepClone(),
        };
        var good = Rows((PairOracleCommand.KnownWitness, "NOT-EQUIVALENT"), ("A/Pair", "EQUIVALENT"));
        Assert.Empty(PairOracleCommand.Reconcile(header, good, First(good)).Problems);
        Assert.NotEmpty(PairOracleCommand.Reconcile(header, good, null).Problems);
        Assert.NotEmpty(PairOracleCommand.Reconcile(header, good, First(good, new string('b', 40))).Problems);
        Assert.NotEmpty(PairOracleCommand.Reconcile(header, good, First(Rows((PairOracleCommand.KnownWitness, "NOT-EQUIVALENT"), ("B/Pair", "EQUIVALENT")))).Problems);
        var (results, problems) = PairOracleCommand.Reconcile(header, good, First(Rows((PairOracleCommand.KnownWitness, "NOT-EQUIVALENT"), ("A/Pair", "NOT-EQUIVALENT"))));
        Assert.Empty(problems);
        Assert.Equal("NONDETERMINISTIC_ACROSS_RUNS", results[1]!["Reason"]!.GetValue<string>());
        var badWitness = Rows((PairOracleCommand.KnownWitness, "EQUIVALENT"));
        Assert.Contains(PairOracleCommand.Reconcile(header, badWitness, First(badWitness)).Problems, p => p.Contains("known witness", StringComparison.Ordinal));
    }

    [Fact]
    public void PreflightRefusesAnotherCommitOrAMalformedSha()
    {
        var registration = Path.Combine(PairOracleCommand.RepoRoot(AppContext.BaseDirectory), "docs/plans/evidence/b1-1276/registration");
        Assert.Contains("full 40-hex", PairOracleCommand.Preflight(registration, "72a0a855"), StringComparison.Ordinal);
        Assert.Contains("HEAD is not", PairOracleCommand.Preflight(registration, new string('0', 40)), StringComparison.Ordinal);
        // An untracked compilation unit at the right commit is still refused.
        var root = PairOracleCommand.RepoRoot(registration);
        var probe = Path.Combine(root, $"b1-1276-preflight-probe-{Guid.NewGuid():N}.cs");
        File.WriteAllText(probe, "class Probe { }");
        try
        {
            Assert.Contains("not clean", PairOracleCommand.Preflight(registration, PairOracleCommand.Git(root, "rev-parse", "HEAD").Trim()), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(probe);
        }
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
