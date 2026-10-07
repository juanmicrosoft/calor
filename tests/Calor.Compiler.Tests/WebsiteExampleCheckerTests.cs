using Calor.Compiler.Diagnostics;
using Calor.Compiler.SelfCheck;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1143: <c>calor self-check docs</c> checks the website's MDX examples, negative-example
/// annotations and quoted compiler output against what the compiler actually does. Every
/// accepting test has a mutated counterpart that must fail (negative controls).
/// </summary>
public sealed class WebsiteExampleCheckerTests
{
    private const string Good = """
        §M{m001:Good}
          §F{f001:Add:pub} (i32:a, i32:b) -> i32
            §E{}
            §R (+ a b)
        """;

    private const string UndefinedVariable = """
        §M{m001:Calculator}
          §F{f001:Increment:pub} (i32:value) -> i32
            §E{}
            §R (+ x 1)
        """;

    private static List<Diagnostic> Check(string mdx, WebsiteExampleChecker.Coverage? coverage = null) =>
        WebsiteExampleChecker.Check([new DocFile("website/content/page.mdx", mdx)], coverage);

    private static string Fence(string info, string body) => $"```{info}\n{body}\n```\n";

    [Fact]
    public void CompleteProgramThatCompiles_Passes()
    {
        var coverage = new WebsiteExampleChecker.Coverage();
        Assert.Empty(Check(Fence("calor", Good), coverage));
        Assert.Equal(1, coverage.CompletePrograms);
    }

    [Fact]
    public void CompleteProgramThatDoesNotCompile_Fails()
    {
        var finding = Assert.Single(Check(Fence("calor", UndefinedVariable)));
        Assert.Equal(DiagnosticCode.DocDriftWebsiteExampleMismatch, finding.Code);
        Assert.Contains("Calor0200", finding.Message);
        Assert.Equal(5, finding.Span.Line); // fence opens on line 1; the error is on source line 4
    }

    [Fact]
    public void GeneratedCSharpIsValidated_NotJustParsed()
    {
        // Parses and binds, but the emitted C# names a type that does not exist (Calor1002).
        var finding = Assert.Single(Check(Fence("calor", """
            §M{m001:Orders}
              §F{f001:Save:pub} () -> void
                §E{db:w}
                §C{DbContext.SaveChanges} §/C
            """)));
        Assert.Contains("Calor1002", finding.Message);
    }

    [Fact]
    public void FragmentsAreNotCompiled()
    {
        Assert.Empty(Check(Fence("calor", "§F{f001:Broken:pub}\n  §R (+ x 1)")));
        Assert.Empty(Check(Fence("calor", "§MT{mt1:Compute:pub}\n  §R x")));
    }

    [Fact]
    public void NegativeExample_MustReportExactlyItsDeclaredCodes()
    {
        const string prose = "This fails with `Calor0200`.\n\n";
        var coverage = new WebsiteExampleChecker.Coverage();
        Assert.Empty(Check(prose + Fence("calor expect=Calor0200", UndefinedVariable), coverage));
        Assert.Equal(1, coverage.NegativePrograms);

        // Wrong code, a code that is not reported, and a negative that actually compiles.
        foreach (var (info, body) in new[]
        {
            ("calor expect=Calor0201", UndefinedVariable),
            ("calor expect=Calor0200,Calor0201", UndefinedVariable),
            ("calor expect=Calor0200", Good),
        })
        {
            var findings = Check("This fails with `Calor0200` or `Calor0201`.\n\n" + Fence(info, body));
            Assert.Contains(findings, f => f.Code == DiagnosticCode.DocDriftWebsiteExampleMismatch);
        }
    }

    [Fact]
    public void NegativeExample_ThatAlsoRaisesAnUndeclaredWarning_Fails()
    {
        // HandleRequest is public with no effect row: cross-module checking adds Calor0417.
        const string page = """
            ```calor group=g
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub}
                §O{void}
                §E{db:w}
            ```

            Fails with Calor0410 and warns with Calor0417.

            ```calor group=g expect=Calor0410
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                §O{void}
                §C{SaveOrder} §/C
            ```
            """;
        var finding = Assert.Single(Check(page));
        Assert.Equal(DiagnosticCode.DocDriftWebsiteExampleMismatch, finding.Code);
        Assert.Contains("Calor0417", finding.Message);
        Assert.Empty(Check(page.Replace("expect=Calor0410", "expect=Calor0410,Calor0417")));
    }

    [Fact]
    public void GroupedFilesCompileTogether()
    {
        const string page = """
            ```calor group=g
            §M{m001:OrderService}
              §F{f001:SaveOrder:pub}
                §O{void}
                §E{db:w}
            ```

            ```calor group=g
            §M{m002:Handler}
              §F{f001:HandleRequest:pub}
                §O{void}
                §E{db:w}
                §C{SaveOrder} §/C
            ```
            """;
        var coverage = new WebsiteExampleChecker.Coverage();
        Assert.Empty(Check(page, coverage));
        Assert.Equal(2, coverage.GroupedPrograms);
        // Without the group the caller cannot see SaveOrder and fails on its own.
        Assert.NotEmpty(Check(page.Replace(" group=g", "")));
    }

    [Fact]
    public void NegativeExample_ProseMustCiteTheCodeAndTheClaimedLocation()
    {
        var uncited = Assert.Single(Check("An example.\n\n" + Fence("calor expect=Calor0200", UndefinedVariable)));
        Assert.Equal(DiagnosticCode.DocDriftWebsiteAnnotation, uncited.Code);

        Assert.Empty(Check("### Calor0200\n\nRejected: `x` on line 4, column 11.\n\n" + Fence("calor expect=Calor0200", UndefinedVariable)));
        var wrongPlace = Assert.Single(Check("### Calor0200\n\nRejected: `x` on line 4, column 12.\n\n" + Fence("calor expect=Calor0200", UndefinedVariable)));
        Assert.Equal(DiagnosticCode.DocDriftWebsiteAnnotation, wrongPlace.Code);
        Assert.Contains("line 4, column 12", wrongPlace.Message);
    }

    [Fact]
    public void OutputFence_MustMatchTheRealDiagnostics()
    {
        string Page(string output) => Fence("calor expect=Calor0200", UndefinedVariable) + "\n" + Fence("text output", output);

        var coverage = new WebsiteExampleChecker.Coverage();
        Assert.Empty(Check(Page("Calculator.calr(4,11): error Calor0200: Undefined variable 'x'"), coverage));
        Assert.Equal(1, coverage.CheckedOutputs);
        Assert.Empty(Check(Page("Calor0200: Undefined\nvariable 'x'"))); // wrapped, no location or severity

        foreach (var wrong in new[]
        {
            "Calculator.calr(4,11): error Calor0200: Undefined variable 'y'",  // message
            "Calculator.calr(4,12): error Calor0200: Undefined variable 'x'",  // location
            "Calculator.calr(4,11): warning Calor0200: Undefined variable 'x'", // severity
            "Calor0200: Undefined variable 'x'\nCalor0201: Something else",     // extra
            "Compilation failed with 1 error",                                   // not a diagnostic
        })
        {
            Assert.Contains(Check(Page(wrong)), f => f.Code == DiagnosticCode.DocDriftWebsiteOutputMismatch);
        }

        // Omitting a diagnostic the example reports is also a mismatch.
        var omitted = Check(Fence("calor", Good) + Fence("text output", ""));
        Assert.Empty(omitted);
        Assert.Contains(Check("Calor0200\n\n" + Fence("calor expect=Calor0200", UndefinedVariable) + Fence("text output", "")),
            f => f.Code == DiagnosticCode.DocDriftWebsiteOutputMismatch && f.Message.Contains("omits"));
    }

    [Theory]
    [InlineData("```\nerror Calor0410: Function 'F' uses effect 'cw' but does not declare it\n```\n")]
    [InlineData("```text\napp.calr:11:11 function Leaky\n```\n")]
    [InlineData("```\n=== Calor Migration Analysis ===\n```\n")]
    [InlineData("```\nBLOCKED: Cannot create C# file 'A.cs'\n```\n")]
    public void OutputShapedFence_MustBeLabelled(string fence)
    {
        Assert.Equal(DiagnosticCode.DocDriftWebsiteOutputMismatch, Assert.Single(Check(fence)).Code);
        var labelled = "```text illustrative\n" + fence[(fence.IndexOf('\n') + 1)..];
        var coverage = new WebsiteExampleChecker.Coverage();
        Assert.Empty(Check(labelled, coverage));
        Assert.Equal(1, coverage.IllustrativeOutputs);
    }

    [Theory]
    [InlineData("```calor expct=Calor0200\n" + "§M{m1:A}\n```\n")]
    [InlineData("```calor expect=0200\n§M{m1:A}\n```\n")]
    [InlineData("```calor expect=\n§M{m1:A}\n```\n")]
    [InlineData("```calor expect=Calor0200\n§F{f1:A:pub}\n```\n")]
    [InlineData("```calor group=a/b\n§M{m1:A}\n```\n")]
    [InlineData("```text output illustrative\nx\n```\n")]
    [InlineData("```text verified\nx\n```\n")]
    [InlineData("```\n§M{m001:Hidden}\n```\n")]
    [InlineData("```csharp\n§M{m001:Hidden}\n```\n")]
    public void MalformedOrMisplacedAnnotation_Fails(string fence)
    {
        Assert.Contains(Check(fence), f => f.Code == DiagnosticCode.DocDriftWebsiteAnnotation);
    }

    [Fact]
    public void OutputWithoutAPrecedingExample_Fails()
    {
        Assert.Contains(Check(Fence("text output", "Calor0200: x")),
            f => f.Code == DiagnosticCode.DocDriftWebsiteOutputMismatch);
    }

    [Fact]
    public void MdxSuppressionMarkerExemptsTheNextFence()
    {
        Assert.Empty(Check(WebsiteExampleChecker.MdxSuppressionMarker + "\n" + Fence("calor", UndefinedVariable)));
    }

    // --- Repository wiring ---

    [Fact]
    public void RepositoryScan_ChecksWebsitePages_AndExcludesOnlyNamedHistoricalRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-website-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "website", "content", "guides"));
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><Version>9.9.9</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "website", "content", "guides", "broken.mdx"),
                Fence("calor", UndefinedVariable) + "Cites `Calor9876`.\n");
            File.WriteAllText(Path.Combine(root, "website", "content", "changelog.mdx"),
                Fence("calor", UndefinedVariable) + "Cites `Calor9876`.\n");

            var findings = DocDriftChecker.Check(DocDriftChecker.LoadFromRepository(root, []));
            var broken = Path.Combine("website", "content", "guides", "broken.mdx");
            Assert.Contains(findings, f => f.FilePath == broken && f.Code == DiagnosticCode.DocDriftWebsiteExampleMismatch);
            Assert.Contains(findings, f => f.FilePath == broken && f.Code == DiagnosticCode.DocDriftUnknownDiagnosticCode);
            Assert.DoesNotContain(findings, f => f.FilePath?.EndsWith("changelog.mdx", StringComparison.Ordinal) == true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RepositoryScan_MissingWebsiteContentIsAnError()
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-website-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var errors = new List<Diagnostic>();
            DocDriftChecker.LoadFromRepository(root, errors);
            Assert.Contains(errors, e => e.Code == DiagnosticCode.DocDriftMissingInput
                && e.Message.Contains(WebsiteExampleChecker.ContentRelativePath, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HistoricalExclusionsAreOnlyTheChangelog()
    {
        Assert.Equal(["changelog.mdx"], WebsiteExampleChecker.HistoricalExclusions.Keys);
    }

    [Fact]
    public void PublishedWebsite_IsClean_AndCoverageIsPinned()
    {
        var root = CliTestHarness.FindRepoRoot();
        var inputs = DocDriftChecker.LoadFromRepository(root, []);
        var coverage = new WebsiteExampleChecker.Coverage();
        var findings = WebsiteExampleChecker.Check(inputs.WebsiteDocs, coverage);
        Assert.True(findings.Count == 0, string.Join(Environment.NewLine, findings));

        // Pinned so a convention change that silently skips examples fails here. Update
        // deliberately when pages add or remove examples.
        Assert.Equal(Pinned,
            $"pages={coverage.Pages} complete={coverage.CompletePrograms} negative={coverage.NegativePrograms} " +
            $"grouped={coverage.GroupedPrograms} checkedOutputs={coverage.CheckedOutputs} illustrative={coverage.IllustrativeOutputs}");
    }

    private const string Pinned = "pages=96 complete=49 negative=5 grouped=3 checkedOutputs=2 illustrative=24";

    [Theory]
    [InlineData("guides/nullability-and-dotnet-interop.mdx", "```calor expect=Calor0272\n", "```calor\n")]
    [InlineData("guides/cross-module-effect-propagation.mdx", "expect=Calor0410,Calor0417", "expect=Calor0410")]
    [InlineData("guides/dependent-types-tutorial.mdx", "    §I{i32:amount} | (> # INT:0)\n    §O{i32}\n    §Q", "    §I{i32:amount} | (> # INT:0)\n    §O{str}\n    §Q")]
    [InlineData("benchmarking/metrics/token-economics.mdx", "```calor\n§M{m001:Name}    // Module requires tag + ID + name\n```",
        "```calor\n§M{m001:Name}    // Module requires tag + ID + name\nnamespace Name\n```")]
    [InlineData("syntax-reference/structure-tags.mdx", "      §IF{if1} (> i 5)", "      §IF (> i 5)")]
    [InlineData("cli/compile.mdx", "error Calor0200: Undefined variable 'x'", "error Calor0200: Undefined variable 'y'")]
    [InlineData("guides/cross-module-effect-propagation.mdx", "warning Calor0417: Public function 'HandleRequest'", "warning Calor0417: Public function 'HandleRequests'")]
    public void PublishedWebsite_MutatedExampleIsCaught(string page, string original, string mutated)
    {
        var root = CliTestHarness.FindRepoRoot();
        var path = Path.Combine(root, "website", "content", page);
        var content = File.ReadAllText(path).Replace("\r\n", "\n");
        Assert.Contains(original, content);
        var findings = WebsiteExampleChecker.Check([new DocFile(page, content.Replace(original, mutated))]);
        Assert.NotEmpty(findings);
    }
}
