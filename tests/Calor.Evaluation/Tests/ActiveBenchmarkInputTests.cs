using System.Text.Json.Nodes;
using Calor.Compiler.Tests;
using Calor.Evaluation.Benchmarks;
using Calor.Evaluation.Core;
using Calor.Evaluation.Reports;
using Xunit;

namespace Calor.Evaluation.Tests;

public class ActiveBenchmarkInputTests
{
    private const string BenchmarkPrefix = "tests/TestData/Benchmarks/";

    public static IEnumerable<object[]> RegisteredBenchmarks() =>
        ActiveSyntaxInputs.OriginalPaths.Where(path => path.StartsWith(BenchmarkPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(RegisteredBenchmarks))]
    public async Task CurrentContexts_ResolveEveryRegisteredBenchmarkWithoutChangingItsIdentityOrCSharp(string original)
    {
        var root = ActiveSyntaxInputs.RepositoryRoot;
        var originalPath = Path.Combine(root, original);
        var csharpPath = Path.ChangeExtension(originalPath, ".cs");
        var originalBytes = await File.ReadAllBytesAsync(originalPath);
        var csharpBytes = await File.ReadAllBytesAsync(csharpPath);
        var manifest = await BenchmarkManifest.LoadAsync(Path.Combine(root, BenchmarkPrefix, "manifest.json"));
        var entry = manifest.Benchmarks.SingleOrDefault(candidate =>
            candidate.CalorFile == original[BenchmarkPrefix.Length..]) ?? Entry(original);
        var adapter = new TestDataAdapter(Path.Combine(root, "tests/TestData"), Path.Combine(root, BenchmarkPrefix));
        var context = await adapter.CreateContextAsync(entry);
        var loaded = Assert.Single(await adapter.LoadAllBenchmarksAsync(new BenchmarkManifest { Benchmarks = [entry] }));
        Assert.Same(entry, loaded.Entry);
        Assert.Equal(entry.Id, loaded.Context.FileName);
        var direct = await TestDataAdapter.CreateContextFromFilesAsync(originalPath, csharpPath);
        var resolved = ActiveSyntaxInputs.ForCurrentCompiler(root, original);
        Assert.Equal(entry.Id, context.FileName);
        Assert.Equal(Path.GetFileNameWithoutExtension(original), direct.FileName);
        Assert.Equal(entry.Level, context.Level);
        Assert.Equal(entry.Features, context.Features);
        foreach (var current in new[] { context, direct })
        {
            Assert.Equal(await File.ReadAllTextAsync(resolved), current.CalorSource);
            Assert.Equal(await File.ReadAllTextAsync(csharpPath), current.CSharpSource);
            Assert.Equal(originalPath, current.Metadata["originalCalorPath"]);
            Assert.Equal(resolved, current.Metadata["resolvedCalorPath"]);
            Assert.Equal(csharpPath, current.Metadata["csharpPath"]);
            Assert.Equal(true, current.Metadata["usesActiveSyntax"]);
            Assert.Equal("current-compiler-active-syntax", current.Metadata["inputContract"]);
            Assert.True(current.CalorCompilation.Success, string.Join("\n", current.CalorCompilation.Errors));
            Assert.True(current.CSharpCompilation.Success, string.Join("\n", current.CSharpCompilation.Errors));
        }
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Equal(csharpBytes, await File.ReadAllBytesAsync(csharpPath));
    }

    [Fact]
    public async Task MissingRegisteredMirror_FailsTheContextAndWholeManifestLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-missing-active-" + Guid.NewGuid().ToString("N"));
        var original = ActiveSyntaxInputs.OriginalPaths.Order(StringComparer.Ordinal)
            .First(path => path.StartsWith(BenchmarkPrefix, StringComparison.Ordinal));
        var entry = Entry(original);
        var adapter = new TestDataAdapter(Path.Combine(root, "tests/TestData"), Path.Combine(root, BenchmarkPrefix), root);
        await Assert.ThrowsAsync<FileNotFoundException>(() => adapter.CreateContextAsync(entry));
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            adapter.LoadAllBenchmarksAsync(new BenchmarkManifest { Benchmarks = [entry] }));
    }

    [Fact]
    public async Task ExternalFiles_AreLiteralEvenWhenTheirNameMatchesARegisteredBenchmark()
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-external-active-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var calor = Path.Combine(directory, "Visitor.calr");
        var csharp = Path.Combine(directory, "Visitor.cs");
        const string source = "§M{m:External}\n  §CL{c:Visitor:pub,stat}\n";
        try
        {
            await File.WriteAllTextAsync(calor, source);
            await File.WriteAllTextAsync(csharp, "public static class Visitor {}");
            var context = await TestDataAdapter.CreateContextFromFilesAsync(calor, csharp);
            Assert.Equal(source, context.CalorSource);
            Assert.Equal(calor, context.Metadata["originalCalorPath"]);
            Assert.Equal(calor, context.Metadata["resolvedCalorPath"]);
            Assert.Equal(false, context.Metadata["usesActiveSyntax"]);
        }
        finally
        {
            File.Delete(calor);
            File.Delete(csharp);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task ResultAndDetailedReport_KeepActualInputPathsWithoutRunningMetrics()
    {
        var root = ActiveSyntaxInputs.RepositoryRoot;
        var original = ActiveSyntaxInputs.OriginalPaths.First(path => path.StartsWith(BenchmarkPrefix, StringComparison.Ordinal));
        var context = await TestDataAdapter.CreateContextFromFilesAsync(
            Path.Combine(root, original), Path.ChangeExtension(Path.Combine(root, original), ".cs"));
        var runner = new BenchmarkRunner(new BenchmarkRunnerOptions { Categories = ["metadata-only-control"] });
        var result = await runner.RunSingleBenchmarkAsync(Entry(original), context);
        Assert.Empty(result.Metrics);
        Assert.Equal(context.Metadata, result.InputMetadata);
        var report = JsonNode.Parse(new JsonReportGenerator().Generate(
            new EvaluationResult { CaseResults = [result] }))!;
        Assert.Equal(context.Metadata["originalCalorPath"],
            report["detailedResults"]![0]!["inputMetadata"]!["originalCalorPath"]!.GetValue<string>());
        Assert.Equal(context.Metadata["resolvedCalorPath"],
            report["detailedResults"]![0]!["inputMetadata"]!["resolvedCalorPath"]!.GetValue<string>());
    }

    [Fact]
    public async Task HistoricalHashCheckedMetricPath_StillReadsTheRawOriginal()
    {
        var root = ActiveSyntaxInputs.RepositoryRoot;
        const string original = BenchmarkPrefix + "DesignPatterns/Visitor.calr";
        var path = Path.Combine(root, original);
        var context = await EvaluationContext.FromFilesAsync(path, Path.ChangeExtension(path, ".cs"));
        Assert.Equal(await File.ReadAllTextAsync(path), context.CalorSource);
        Assert.Contains("§CL{", context.CalorSource);
        Assert.NotEqual(await File.ReadAllTextAsync(ActiveSyntaxInputs.ForCurrentCompiler(root, original)), context.CalorSource);
    }

    [Fact]
    public void DefaultDiscovery_PrefersCanonicalInputsRatherThanCopiedOutputData()
    {
        Assert.Equal(Path.Combine(ActiveSyntaxInputs.RepositoryRoot, "tests", "TestData"), TestDataAdapter.GetTestDataPath());
    }

    [Fact]
    public async Task CurrentBugCaller_LoadsBothRolesWithOriginalIdentityAndReportablePathsWithoutRunningCalculators()
    {
        var root = ActiveSyntaxInputs.RepositoryRoot;
        var manifest = await BenchmarkManifest.LoadAsync(Path.Combine(root, BenchmarkPrefix, "manifest.json"));
        var bug = manifest.BugScenarios.Single(entry =>
            entry.CalorBuggy == "ErrorDetection/NullDeref_buggy.calr");
        var (buggy, fixedInput) = await new BenchmarkRunner().LoadBugScenarioInputsAsync(bug);
        foreach (var (context, calor, csharp) in new[]
        {
            (buggy, bug.CalorBuggy, bug.CSharpBuggy),
            (fixedInput, bug.CalorFixed, bug.CSharpFixed)
        })
        {
            var original = Path.Combine(root, BenchmarkPrefix, calor);
            var resolved = ActiveSyntaxInputs.ForCurrentCompiler(root, original);
            Assert.Equal(bug.Id, context.FileName);
            Assert.Equal(bug.Level, context.Level);
            Assert.Equal(await File.ReadAllTextAsync(resolved), context.CalorSource);
            Assert.Equal(await File.ReadAllTextAsync(Path.Combine(root, BenchmarkPrefix, csharp)), context.CSharpSource);
            Assert.Equal(original, context.Metadata["originalCalorPath"]);
            Assert.Equal(resolved, context.Metadata["resolvedCalorPath"]);
            Assert.Equal(Path.Combine(root, BenchmarkPrefix, csharp), context.Metadata["csharpPath"]);
        }
        var report = JsonNode.Parse(new JsonReportGenerator().Generate(new EvaluationResult
        {
            BugScenarioInputs = [new BugScenarioInputMetadata(bug.Id, buggy.Metadata, fixedInput.Metadata)]
        }))!;
        var inputs = Assert.Single(report["bugScenarioInputs"]!.AsArray())!;
        Assert.Equal(bug.Id, inputs["bugId"]!.GetValue<string>());
        Assert.Equal(buggy.Metadata["originalCalorPath"], inputs["buggy"]!["originalCalorPath"]!.GetValue<string>());
        Assert.Equal(buggy.Metadata["resolvedCalorPath"], inputs["buggy"]!["resolvedCalorPath"]!.GetValue<string>());
        Assert.Equal(fixedInput.Metadata["originalCalorPath"], inputs["fixed"]!["originalCalorPath"]!.GetValue<string>());
        Assert.Equal(fixedInput.Metadata["resolvedCalorPath"], inputs["fixed"]!["resolvedCalorPath"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentBugCaller_PropagatesMissingRequiredMirrorForEitherRoleBeforeCalculating(bool buggyMissing)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-missing-bug-active-" + Guid.NewGuid().ToString("N"));
        var benchmarks = Path.Combine(root, BenchmarkPrefix);
        Directory.CreateDirectory(benchmarks);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(benchmarks, "Unregistered.calr"), "§M{m:Unregistered}\n");
            await File.WriteAllTextAsync(Path.Combine(benchmarks, "Unregistered.cs"), "public class Unregistered {}");
            const string registered = "DesignPatterns/Visitor.calr";
            var bug = new BugEntry
            {
                Id = "missing-mirror-control",
                Description = "Input loading must fail before any bug calculation.",
                Category = "input-control",
                CalorBuggy = buggyMissing ? registered : "Unregistered.calr",
                CalorFixed = buggyMissing ? "Unregistered.calr" : registered,
                CSharpBuggy = "Unregistered.cs",
                CSharpFixed = "Unregistered.cs"
            };
            var adapter = new TestDataAdapter(Path.Combine(root, "tests/TestData"), benchmarks, root);
            var runner = new BenchmarkRunner(adapter, new BenchmarkRunnerOptions { Categories = ["metadata-only-control"] });
            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
                runner.RunAllAsync(new BenchmarkManifest { BugScenarios = [bug] }));
            Assert.Equal(Path.Combine(root, ActiveSyntaxInputs.RelativeRoot, BenchmarkPrefix, registered), exception.FileName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BenchmarkEntry Entry(string original) => new()
    {
        Id = "original:" + original,
        CalorFile = original[BenchmarkPrefix.Length..],
        CSharpFile = Path.ChangeExtension(original[BenchmarkPrefix.Length..], ".cs"),
        Level = 3,
        Features = ["current-input-control"]
    };
}
