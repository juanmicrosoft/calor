using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Formatting;
using Calor.Compiler.Init;
using Calor.Compiler.Mcp.Tools;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1622: canonical-usage rules in the reference and the report-only Calor1306 lint
/// (an if/else-if chain over one name and 3+ constants that a match states more briefly).
/// </summary>
public sealed class CanonicalUsageLintTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"calor-canonical-lint-{Guid.NewGuid():N}");

    public CanonicalUsageLintTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string Module(params string[] body) => string.Join("\n",
        new[]
        {
            "module Choices",
            "  enum Color",
            "    Red",
            "    Green",
            "    Blue",
        }.Concat(body)) + "\n";

    private static IReadOnlyList<Diagnostic> Lint(string source)
    {
        var compiled = Program.Compile(source, "choice.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        return LosslessSourceDocument.DiagnoseCanonicalUsage(source, "choice.calr", compiled.Ast!).ToList();
    }

    private static string Chain(string signature, string scrutinee, params string[] constants)
    {
        var lines = new List<string> { $"  func Pick public {signature} → string", "    !" };
        for (var i = 0; i < constants.Length; i++)
        {
            lines.Add(i == 0 ? $"    if {scrutinee} == {constants[i]}" : $"    else if {scrutinee} == {constants[i]}");
            lines.Add($"      return \"case{i}\"");
        }
        lines.Add("    else");
        lines.Add("      return \"other\"");
        return Module(lines.ToArray());
    }

    [Theory]
    [InlineData("(int:x)", "x", "1", "2", "3")]
    [InlineData("(long:x)", "x", "1", "2", "3")]
    [InlineData("(string:x)", "x", "\"a\"", "\"b\"", "\"c\"")]
    [InlineData("(char:x)", "x", "'a'", "'b'", "'c'")]
    [InlineData("(Color:x)", "x", "Color.Red", "Color.Green", "Color.Blue")]
    public void ChainOverOneNameAndThreeConstants_IsReportedWithoutPatch(
        string signature, string scrutinee, string first, string second, string third)
    {
        var source = Chain(signature, scrutinee, first, second, third);
        var compiled = Program.Compile(source, "choice.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        var bag = LosslessSourceDocument.DiagnoseCanonicalUsage(source, "choice.calr", compiled.Ast!);

        var finding = Assert.Single(bag);
        Assert.Equal(DiagnosticCode.LintPreferMatch, finding.Code);
        Assert.Equal(DiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal("Use match x for an if chain comparing x with 3 or more constants", finding.Message);
        Assert.DoesNotContain('\n', finding.Message);
        Assert.Equal(8, finding.Span.Line);
        Assert.Equal(5, finding.Span.Column);
        Assert.Empty(bag.DiagnosticsWithFixes);
    }

    [Fact]
    public void LocalScrutineeAndReversedOperands_AreReported()
    {
        var source = Module(
            "  func Pick public (int:seed) → string",
            "    !",
            "    let int:x = seed + 1",
            "    if 1 == x",
            "      return \"one\"",
            "    else if x == 2",
            "      return \"two\"",
            "    else if 3 == x",
            "      return \"three\"",
            "    return \"other\"");
        Assert.Equal(DiagnosticCode.LintPreferMatch, Assert.Single(Lint(source)).Code);
    }

    [Fact]
    public void TwoCases_AreNotReported()
        => Assert.Empty(Lint(Chain("(int:x)", "x", "1", "2")));

    [Fact]
    public void MemberAccessScrutinee_IsNotReported()
        => Assert.Empty(Lint(Chain("(string:s)", "s.Length", "1", "2", "3")));

    [Fact]
    public void SideEffectingCondition_IsNotReported()
    {
        var source = Module(
            "  func Next () → int",
            "    ! cw",
            "    Console.WriteLine(\"next\")",
            "    return 2",
            "  func Pick public (int:x) → string",
            "    ! cw",
            "    if x == 1",
            "      return \"one\"",
            "    else if x == Next()",
            "      return \"two\"",
            "    else if x == 3",
            "      return \"three\"",
            "    return \"other\"");
        Assert.Empty(Lint(source));
    }

    [Fact]
    public void MixedConstantTypes_AreNotReported()
        => Assert.Empty(Lint(Chain("(int:x)", "x", "1", "'a'", "3")));

    [Fact]
    public void FloatingPointScrutinee_IsNotReported()
        // A constant pattern matches NaN where == does not, so floating point is excluded.
        => Assert.Empty(Lint(Chain("(double:x)", "x", "1.5", "2.5", "3.5")));

    [Fact]
    public void DuplicateConstants_AreNotReported()
        => Assert.Empty(Lint(Chain("(int:x)", "x", "1", "2", "1")));

    [Fact]
    public void DifferentNames_AreNotReported()
    {
        var source = Module(
            "  func Pick public (int:x, int:y) → string",
            "    !",
            "    if x == 1",
            "      return \"one\"",
            "    else if y == 2",
            "      return \"two\"",
            "    else if x == 3",
            "      return \"three\"",
            "    return \"other\"");
        Assert.Empty(Lint(source));
    }

    [Fact]
    public void LoopBreakInABranch_IsNotReported()
    {
        // In a statement match the break would exit the C# switch instead of the loop.
        var source = Module(
            "  func Count public (int[]:items) → int",
            "    !",
            "    var int:total = 0",
            "    each int:item in items",
            "      if item == 1",
            "        set total total + 1",
            "      else if item == 2",
            "        break",
            "      else if item == 3",
            "        set total total + 3",
            "    return total");
        Assert.Empty(Lint(source));
    }

    [Fact]
    public void MatchRewrite_CompilesAndIsLintClean()
    {
        var source = Module(
            "  func Pick public (int:x) → string",
            "    !",
            "    return match x",
            "      case 1 → \"case0\"",
            "      case 2 → \"case1\"",
            "      case 3 → \"case2\"",
            "      case _ → \"other\"");
        Assert.Empty(Lint(source));
    }

    [Fact]
    public async Task Finding_SurfacesThroughLintCliMcpAndIsNotAutoApplied()
    {
        var source = Chain("(int:x)", "x", "1", "2", "3");

        var repaired = DiagnosticFixes.Repair(source, "choice.calr", new CompilationOptions(), out _, out var applied);
        Assert.Equal(0, applied);
        Assert.Equal(source, repaired);

        var lint = await new CheckTool().ExecuteAsync(JsonSerializer.SerializeToElement(
            new { action = "lint", source }));
        Assert.False(lint.IsError, lint.Content[0].Text);
        var issue = Assert.Single(JsonDocument.Parse(lint.Content[0].Text!).RootElement
            .GetProperty("issues").EnumerateArray());
        Assert.Equal(DiagnosticCode.LintPreferMatch, issue.GetProperty("code").GetString());

        var path = Path.Combine(_directory, "choice.calr");
        File.WriteAllText(path, source);
        var (lintExit, lintJson, _) = CliTestHarness.RunCli(_directory, "lint", path, "--format", "json");
        Assert.Equal(1, lintExit);
        Assert.Equal(DiagnosticCode.LintPreferMatch, Assert.Single(
            JsonDocument.Parse(lintJson).RootElement.GetProperty("diagnostics").EnumerateArray())
            .GetProperty("code").GetString());
        CliTestHarness.RunCli(_directory, "fix", path, "--apply", "--format", "json");
        Assert.Equal(source, File.ReadAllText(path));
    }

    [Fact]
    public void Reference_StatesEachCanonicalUsageRuleNextToItsConstruct()
    {
        var reference = EmbeddedResourceHelper.ReadResource("Calor.Compiler.Resources.current-language-reference.md");
        string[] rules =
        [
            "Branching on one value against 3 or more constants uses match; otherwise use if.",
            "Use a conditional expression when a condition chooses a value; use if for statement bodies and side effects.",
            "Default to a loop for transformations and queries: it measured fewer tokens.",
            "Use a refinement, not an if-throw guard.",
            "Count with from/to when the bound cannot change in the body; use while otherwise.",
        ];
        foreach (var rule in rules)
            Assert.Contains(rule, reference);
    }
}
