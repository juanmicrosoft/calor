using System.Text.RegularExpressions;
using Calor.Compiler.Analysis;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Compiler.Tests.Analysis;

public sealed class BenchmarkSubjectRegressionTests(ITestOutputHelper output)
{
    // These are false-positive limitations, never waivers for a missing seeded finding.
    // Keep EXPECTED unchanged in the subjects and remove a gap when its analysis is fixed.
    private static readonly IReadOnlyDictionary<string, string> SafeCaseGaps = new Dictionary<string, string>
    {
        ["security/command-injection.calr:SafeEscapedCommand"] = DiagnosticCode.CommandInjection,
        ["security/path-traversal.calr:SafeSanitizedPath"] = DiagnosticCode.PathTraversal,
        ["arithmetic/overflow.calr:SafeSmallValues"] = DiagnosticCode.IntegerOverflow,
        ["arithmetic/overflow.calr:SafeBoundedShift"] = DiagnosticCode.IntegerOverflow,
        ["loops/bounds-violation.calr:SafeWithPrecondition"] = DiagnosticCode.IndexOutOfBounds,
    };

    [Theory]
    [InlineData("security/command-injection.calr", 6, 3)]
    [InlineData("security/sql-injection.calr", 6, 3)]
    [InlineData("security/path-traversal.calr", 6, 3)]
    [InlineData("arithmetic/div-by-zero.calr", 8, 4)]
    [InlineData("arithmetic/overflow.calr", 8, 4)]
    [InlineData("loops/bounds-violation.calr", 8, 4)]
    [InlineData("null-safety/null-deref.calr", 8, 4)]
    public void Subjects_ParseBindCompileAndVerifySeededFindings_WithNamedSafeCaseGaps(
        string relativePath, int functionCount, int seededFindingCount)
    {
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "benchmarks")))
            root = Path.GetDirectoryName(root)
                ?? throw new DirectoryNotFoundException("Repository benchmarks directory not found.");
        var source = File.ReadAllText(Path.Combine(root, "benchmarks", relativePath));
        Assert.Equal(seededFindingCount, Regex.Matches(source, @"// EXPECTED: Calor\d+").Count);
        Assert.Equal(functionCount - seededFindingCount, Regex.Matches(source, @"// EXPECTED: No warnings").Count);
        var parseDiagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, parseDiagnostics).TokenizeAllForParser(), parseDiagnostics).Parse();
        Assert.False(parseDiagnostics.HasErrors, string.Join("\n", parseDiagnostics));
        Assert.Equal(functionCount, module.Functions.Count);

        var bindingDiagnostics = new DiagnosticBag();
        var bound = new Binder(bindingDiagnostics).Bind(module);
        Assert.Equal(functionCount, bound.Functions.Count);
        Assert.False(bindingDiagnostics.HasErrors, string.Join("\n", bindingDiagnostics));
        Assert.DoesNotContain(bindingDiagnostics, d => d.Code == DiagnosticCode.AnalysisIncomplete);
        // Effects are a separate analysis. SQL intentionally uses the legacy external
        // db/sql_escape contracts; the other six subjects must generate Roslyn-clean C#.
        var compilation = Program.Compile(source, relativePath, new CompilationOptions { EnforceEffects = false });
        if (relativePath == "security/sql-injection.calr")
        {
            var errors = compilation.Diagnostics.Errors.ToArray();
            Assert.Equal(7, errors.Length);
            Assert.All(errors, d => Assert.Equal(DiagnosticCode.CodeGenCompilationError, d.Code));
            Assert.Equal(6, errors.Count(d => d.Message.Contains("The name 'db'", StringComparison.Ordinal)));
            Assert.Single(errors.Where(d => d.Message.Contains("The name 'sql_escape'", StringComparison.Ordinal)));
        }
        else
        {
            Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics));
            Assert.NotEmpty(compilation.GeneratedCode);
        }

        var findings = new DiagnosticBag();
        new VerificationAnalysisPass(findings, new VerificationAnalysisOptions
        {
            EnableDataflow = false,
            EnableBugPatterns = !relativePath.StartsWith("security/", StringComparison.Ordinal),
            EnableTaintAnalysis = relativePath.StartsWith("security/", StringComparison.Ordinal),
        }).Analyze(module);
        var targetCodes = relativePath switch
        {
            "arithmetic/div-by-zero.calr" => new[] { DiagnosticCode.DivisionByZero },
            "arithmetic/overflow.calr" => [DiagnosticCode.IntegerOverflow],
            "loops/bounds-violation.calr" => [DiagnosticCode.IndexOutOfBounds],
            "null-safety/null-deref.calr" => [DiagnosticCode.NullDereference, DiagnosticCode.UnsafeUnwrap],
            _ => [DiagnosticCode.SqlInjection, DiagnosticCode.CommandInjection, DiagnosticCode.PathTraversal],
        };

        var functions = Regex.Matches(source, @"§F\{[^:]+:([^:}]+):[^}]+\}");
        Assert.Equal(functionCount, functions.Count);
        var observedGaps = new HashSet<string>();
        for (var i = 0; i < functions.Count; i++)
        {
            var start = functions[i].Index;
            var end = i + 1 < functions.Count ? functions[i + 1].Index : source.Length;
            var expectation = Regex.Match(source[start..end], @"// EXPECTED: (Calor\d+|No warnings)");
            Assert.True(expectation.Success, $"Missing EXPECTED in {functions[i].Groups[1].Value}");
            var actual = findings.Where(d => targetCodes.Contains(d.Code) && d.Span.Start >= start && d.Span.Start < end)
                .Select(d => d.Code).Distinct().Order().ToArray();
            var expected = expectation.Groups[1].Value;
            output.WriteLine($"{functions[i].Groups[1].Value}: expected {expected}; actual {string.Join(", ", actual)}");
            var key = $"{relativePath}:{functions[i].Groups[1].Value}";
            if (SafeCaseGaps.TryGetValue(key, out var gapCode))
            {
                Assert.Equal("No warnings", expected);
                Assert.Equal(new[] { gapCode }, actual);
                observedGaps.Add(key);
                output.WriteLine($"KNOWN SAFE-CASE GAP (#901): {key}");
            }
            else
            {
                Assert.Equal(expected == "No warnings" ? Array.Empty<string>() : new[] { expected }, actual);
            }
        }
        Assert.Equal(SafeCaseGaps.Keys.Where(key => key.StartsWith(relativePath + ":", StringComparison.Ordinal)).Order(),
            observedGaps.Order());
    }

    [Theory]
    [InlineData("?string:value", "§R §C{value.GetHashCode} §/C", "Warning")]
    [InlineData("?string:value", "§IF{i1} (!= value null)\n      §R §C{value.GetHashCode} §/C\n    §R INT:0", null)]
    [InlineData("?string:value", "§IF{i1} (== null value)\n      §R INT:0\n    §R §C{value.GetHashCode} §/C", null)]
    [InlineData("", "§B{value:?string} null\n    §R §C{value.GetHashCode} §/C", "Error")]
    [InlineData("", "§B{value:?string} STR:\"present\"\n    §R §C{value.GetHashCode} §/C", null)]
    [InlineData("", "§B{~value:?string} STR:\"present\"\n    §ASSIGN value null\n    §R §C{value.GetHashCode} §/C", "Error")]
    public void NullableReferences_PreserveCertaintyAndSafeControls(string parameters, string body, string? severity)
    {
        var diagnostics = AnalyzeSource($"§M{{m:NullControl}}\n  §F{{f:Run:pub}} ({parameters}) -> i32\n    {body}");
        var findings = diagnostics.Where(d => d.Code == DiagnosticCode.NullDereference).ToArray();
        if (severity == null)
            Assert.Empty(findings);
        else
            Assert.Equal(severity, Assert.Single(findings).Severity.ToString());
    }

    [Theory]
    [InlineData("§R (<< x n)", true)]
    [InlineData("§R (<< INT:0 n)", false)]
    [InlineData("§R (<< INT:1 n)", true)]
    [InlineData("§R (<< INT:-1 n)", false)]
    [InlineData("§R (<< INT:1 INT:32)", false)]
    [InlineData("§R (<< INT:1 INT:-1)", true)]
    [InlineData("§IF{i1} (&& (>= x INT:-1000) (<= x INT:1000))\n      §IF{i2} (&& (>= n INT:0) (<= n INT:2))\n        §R (<< x n)\n    §R INT:0", false)]
    [InlineData("§IF{i1} (&& (>= x INT:-1000) (<= x INT:1000))\n      §IF{i2} (&& (>= n INT:-32) (<= n INT:-30))\n        §R (<< x n)\n    §R INT:0", false)]
    public void VariableShifts_RespectMaskedCountsAndBoundedControls(string body, bool overflow)
    {
        var diagnostics = AnalyzeSource($"§M{{m:ShiftControl}}\n  §F{{f:Run:pub}} (i32:x, i32:n) -> i32\n    {body}");
        Assert.Equal(overflow, diagnostics.Any(d => d.Code == DiagnosticCode.IntegerOverflow));
    }

    [Theory]
    [InlineData("INT:1", "n", true)]
    [InlineData("INT:0", "n", false)]
    [InlineData("INT:1", "INT:64", false)]
    [InlineData("INT:1", "INT:-1", true)]
    public void LongShifts_UseSixBitCountMask(string value, string count, bool overflow)
    {
        var source = $$"""
            §M{m:LongShiftControl}
              §F{f:Run:pub} (i32:n) -> i64
                §B{value:i64} {{value}}
                §R (<< value {{count}})
            """;
        Assert.Equal(overflow, AnalyzeSource(source).Any(d => d.Code == DiagnosticCode.IntegerOverflow));
    }

    [Theory]
    [InlineData("user_input", "STR:\"replacement\"", true)]
    [InlineData("STR:\"constant\"", "user_input", true)]
    [InlineData("STR:\"constant\"", "STR:\"replacement\"", false)]
    public void Taint_StringTransformPreservesReceiverAndArgumentFlows(string receiver, string replacement, bool tainted)
    {
        var source = $$"""
            §M{m:ReceiverTaint}
              §F{f:Run:pub} (string:user_input) -> void
                §B{value:string} {{receiver}}
                §B{path:string} §C{value.Replace} §A STR:"x" §A {{replacement}} §/C
                §C{System.IO.File.Delete} §A path §/C
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        new VerificationAnalysisPass(diagnostics, new VerificationAnalysisOptions
        {
            EnableDataflow = false,
            EnableBugPatterns = false,
        }).Analyze(module);
        Assert.Equal(tainted, diagnostics.Any(d => d.Code == DiagnosticCode.PathTraversal));
    }

    private static DiagnosticBag AnalyzeSource(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        new VerificationAnalysisPass(diagnostics, new VerificationAnalysisOptions
        {
            EnableDataflow = false,
            EnableTaintAnalysis = false,
        }).Analyze(module);
        return diagnostics;
    }
}
