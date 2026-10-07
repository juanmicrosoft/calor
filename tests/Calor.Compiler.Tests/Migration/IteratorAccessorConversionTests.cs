using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Commands;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Tests.EvidenceContract;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// 0.25 F4 (#1139): a legal C# iterator accessor (a property or indexer getter that uses
/// <c>yield return</c>/<c>yield break</c>) has no Calor form — <c>§YIELD</c> in a
/// <c>§GET</c> is <c>Calor0209</c>. The converter itself must preserve the whole member
/// as <c>§CSHARP</c> on every surface, so no surface emits illegal Calor and none
/// depends on the post-conversion rescue (#1426 R0 baseline: only CLI rescue hid it).
/// Each registered row is converted, compiled with default options, run, and compared
/// with the original C#, which observes deferred execution, <c>finally</c>-on-Dispose
/// and exception timing.
/// </summary>
public class IteratorAccessorConversionTests
{
    private const string R0Fixtures = "docs/plans/evidence/v0.25-r0-1426/fixtures";

    public enum Surface
    {
        LibraryDefault,
        CliDefault,
        CliPassthrough,
        McpDefault,
        McpPassthroughOnError,
        McpPassthroughOnErrorModuleName,
        ProjectMigrate,
        ProjectMigratePassthrough
    }

    // Extra observability beyond the registered rows: the exact interleaving of
    // producer and consumer, finally on an explicit Dispose, and deferred argument
    // validation (an iterator throws on the first MoveNext, not on access).
    private const string Interleaving = """
        using System;
        using System.Collections.Generic;

        public class Trace
        {
            public static string log = "";

            public IEnumerable<int> Steps
            {
                get
                {
                    log = log + "g0";
                    try
                    {
                        log = log + "t";
                        yield return 1;
                        log = log + "g1";
                        yield return 2;
                        log = log + "g2";
                    }
                    finally
                    {
                        log = log + "f";
                    }
                }
            }

            public IEnumerable<int> this[int n]
            {
                get
                {
                    if (n < 0)
                    {
                        throw new ArgumentOutOfRangeException("n");
                    }
                    for (int i = 0; i < n; i++)
                    {
                        log = log + "i" + i;
                        yield return i;
                    }
                }
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                var t = new Trace();
                var steps = t.Steps;
                Trace.log = Trace.log + "a";
                foreach (var s in steps)
                {
                    Trace.log = Trace.log + "c" + s;
                    if (s == 2)
                    {
                        break;
                    }
                }
                Trace.log = Trace.log + "|";
                var bad = t[-1];
                Trace.log = Trace.log + "d";
                try
                {
                    foreach (var x in bad)
                    {
                        Trace.log = Trace.log + x;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    Trace.log = Trace.log + "!";
                }
                foreach (var y in t[2])
                {
                    Trace.log = Trace.log + "c" + y;
                }
                return Trace.log;
            }
        }
        """;

    // C# 13 partial iterator property and indexer, split across two type parts. Calor has
    // no partial members, so the defining declarations must be preserved with the
    // implementing ones (Codex round 1, finding 1).
    private const string PartialParts = """
        using System.Collections.Generic;

        public partial class Bag
        {
            public static int started = 0;
            public partial IEnumerable<int> Items { get; }
            public partial IEnumerable<int> this[int n] { get; }
        }

        public partial class Bag
        {
            public partial IEnumerable<int> Items
            {
                get
                {
                    started = started + 1;
                    yield return 1;
                    yield return 2;
                }
            }

            public partial IEnumerable<int> this[int n]
            {
                get
                {
                    for (int i = 0; i < n; i++)
                    {
                        yield return i;
                    }
                }
            }
        }

        public static class Probe
        {
            public static string Run()
            {
                var bag = new Bag();
                var items = bag.Items;
                int before = Bag.started;
                int sum = 0;
                foreach (var i in items)
                {
                    sum = sum + i;
                }
                int count = 0;
                foreach (var j in bag[3])
                {
                    count = count + 1;
                }
                return before + "|" + Bag.started + "|" + sum + "|" + count;
            }
        }
        """;

    public static TheoryData<string, string, Surface> Rows()
    {
        var data = new TheoryData<string, string, Surface>();
        foreach (var surface in Enum.GetValues<Surface>())
        {
            data.Add("F4-ITER-01", "0|1|3|3", surface);
            data.Add("F4-ITER-02", "sf|7:late", surface);
            data.Add("interleaving", "ag0tc1g1c2f|d!i0c0i1c1", surface);
            data.Add("partial", "0|1|3|3", surface);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task IteratorAccessors_ArePreservedLegallyOnEverySurface(
        string row, string expected, Surface surface)
    {
        var source = row switch
        {
            "interleaving" => Interleaving,
            "partial" => PartialParts,
            _ => R0Fixture(row)
        };
        // Partial members: both the defining and the implementing declaration are kept.
        var preserved = row == "partial" ? 4 : 2;
        Assert.Equal(expected, Run(source));

        var (calor, losses) = await ConvertAsync(source, surface);

        // No illegal Calor: the iterator never reaches a §GET as §YIELD/§YBRK.
        Assert.DoesNotMatch(@"§Y(IELD|BRK)", calor);
        // Honest report: both accessors are named as iterator-accessor preservations,
        // and nothing was rescued after the fact.
        Assert.Equal(preserved, losses.Count(loss => loss == "iterator-accessor"));
        Assert.DoesNotContain("post-validation-fallback", losses);
        Assert.Equal(preserved, Regex.Matches(calor, @"§CSHARP\{").Count);

        // Default compile (effects enforced), then run: same observable behavior.
        var compilation = Program.Compile(calor, row + ".calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors,
            string.Join(Environment.NewLine, compilation.Diagnostics.Errors) + "\n" + calor);
        Assert.Equal(expected, Run(compilation.GeneratedCode));
    }

    [Theory]
    [InlineData("F4-ITER-01")]
    [InlineData("F4-ITER-02")]
    public void NoFallback_RefusesExplicitly(string row)
    {
        // `calor convert --no-fallback` turns every unsupported feature into an error.
        // The iterator accessor is refused by name; no Calor is produced to mislead.
        var result = new CSharpToCalorConverter(ConvertCommand.BuildCSharpToCalorOptions(
            benchmark: false, verbose: false, explain: false, noFallback: true,
            passthrough: false, explicitCallClosers: false)).Convert(R0Fixture(row));
        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Feature == "iterator-accessor"
            && issue.Severity == ConversionIssueSeverity.Error);
    }

    [Fact]
    public void PreservedMember_IsTheVerbatimSource()
    {
        var source = R0Fixture("F4-ITER-01");
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success);
        var indexer = source[source.IndexOf("    public IEnumerable<int> this[int limit]", StringComparison.Ordinal)..];
        indexer = indexer[..(indexer.IndexOf("\n    }\n", StringComparison.Ordinal) + 6)];
        Assert.Contains(indexer.Trim(), result.CalorSource);
        Assert.Contains(result.Issues, issue => issue.Feature == "iterator-accessor");
        Assert.Equal(SupportLevel.NotSupported, FeatureSupport.GetSupportLevel("iterator-accessor"));
    }

    [Fact]
    public void NativeYieldInAnAccessor_IsStillCalor0209()
    {
        // F4-ITER-03: the regression control. Native illegal yields stay rejected.
        var calor = File.ReadAllText(Path.Combine(
            EvidenceContractTests.RepoRoot(), R0Fixtures, "F4-ITER-03.calr.txt"));
        var compilation = Program.Compile(calor, "F4-ITER-03.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.Contains(compilation.Diagnostics.Errors, d => d.Code == "Calor0209");
    }

    [Theory]
    [InlineData("public IEnumerable<int> Gen() { yield return 1; yield break; }", "§YIELD")]
    [InlineData("public IEnumerable<int> Seq => Gen(); private IEnumerable<int> Gen() { yield return 1; }", "§PROP")]
    [InlineData("public IEnumerable<int> Seq { get { return new List<int> { 1 }; } }", "§PROP")]
    public void NonIteratorAccessors_StayNative(string members, string marker)
    {
        // Negative controls: iterator methods and accessors that merely return a
        // sequence keep their native conversion and are not reported as iterator-accessor.
        var result = new CSharpToCalorConverter().Convert(
            "using System.Collections.Generic;\npublic class C\n{\n" + members + "\n}\n");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Contains(marker, result.CalorSource);
        Assert.DoesNotContain("§CSHARP", result.CalorSource);
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "iterator-accessor");
    }

    [Fact]
    public void YieldInANestedLocalFunction_IsNotAnIteratorAccessor()
    {
        var result = new CSharpToCalorConverter().Convert("""
            using System.Collections.Generic;
            public class C
            {
                public IEnumerable<int> Seq { get { return L(); IEnumerable<int> L() { yield return 3; } } }
            }
            """);
        Assert.True(result.Success);
        Assert.Contains("§CSHARP", result.CalorSource);
        Assert.DoesNotMatch(@"§Y(IELD|BRK)", result.CalorSource!);
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "iterator-accessor");
    }

    [Theory]
    [InlineData("public struct S { public int A; public IEnumerable<int> Both { get { yield return A; yield return A + 1; } } }")]
    [InlineData("public interface I { IEnumerable<int> Items { get; } } public class C : I { IEnumerable<int> I.Items { get { yield return 5; } } }")]
    [InlineData("public class C { public static IEnumerable<string> Names { get { yield return \"a\"; } } }")]
    [InlineData("public class C { public IEnumerator<int> Cursor { get { yield return 9; } } }")]
    public void OtherIteratorAccessorShapes_ArePreservedAndCompile(string types)
    {
        var result = new CSharpToCalorConverter().Convert("using System.Collections.Generic;\n" + types + "\n");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.DoesNotMatch(@"§Y(IELD|BRK)", result.CalorSource!);
        Assert.Contains(result.Losses, loss => loss.Feature == "iterator-accessor"
            && loss.Kind == ConversionLossKind.InteropPreserved);
        var compilation = Program.Compile(result.CalorSource!, "shape.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
    }

    [Theory]
    [InlineData("public partial class C { public partial int Q { get; } public partial int Q { get => 1; } }", "partial-property", 2)]
    [InlineData("public partial class @C { public partial IEnumerable<int> @P { get; } public partial IEnumerable<int> this[int n] { get; } } public partial class C { public partial IEnumerable<int> P { get { yield return 1; } } public partial IEnumerable<int> this[System.Int32 n] { get { yield return n; } } }", "iterator-accessor", 4)]
    [InlineData("public class C { public IEnumerable<int> P { get { using var d = new System.IO.MemoryStream(); yield return 1; } } }", "iterator-accessor", 1)]
    public void OverlappingShapes_AreLabelledAndCompileWithoutRescue(string types, string feature, int count)
    {
        // A non-iterator partial property is preserved as partial-property (both parts);
        // an iterator getter that also has a using declaration is still labelled
        // iterator-accessor (Codex round 1, finding 2). Neither needs the rescue.
        var result = new CSharpToCalorConverter().Convert("using System.Collections.Generic;\n" + types + "\n");
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(count, result.Losses.Count(loss => loss.Feature == feature));
        Assert.DoesNotContain(result.Losses, loss => loss.Feature == "post-validation-fallback");
        var compilation = Program.Compile(result.CalorSource!, "overlap.calr",
            new CompilationOptions { StatusWriter = TextWriter.Null });
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialIteratorParts_InSeparateFiles_MigrateWithoutRescue(bool mergePartialClasses)
    {
        // Codex round 2, finding 3: the defining declarations live in one file and the
        // iterator implementations in another. Each part is preserved where it is.
        var split = PartialParts.IndexOf("public partial class Bag", 40, StringComparison.Ordinal);
        var probe = PartialParts.IndexOf("public static class Probe", StringComparison.Ordinal);
        var defining = PartialParts[..split] + PartialParts[probe..];
        var implementing = "using System.Collections.Generic;\n\n" + PartialParts[split..probe];
        var dir = Path.Combine(Path.GetTempPath(), "calor-f4-1139-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "BagDefining.cs"), defining);
            File.WriteAllText(Path.Combine(dir, "BagIterators.cs"), implementing);
            var migrator = new ProjectMigrator(new MigrationPlanOptions
            {
                Parallel = false,
                SkipAnalyze = true,
                SkipVerify = true,
                MergePartialClasses = mergePartialClasses
            });
            var plan = await migrator.CreatePlanAsync(dir, MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var generated = new List<string>();
            foreach (var file in report.FileResults.Where(file => file.OutputPath != null))
            {
                Assert.DoesNotContain(file.Losses, loss => loss.Feature == "post-validation-fallback");
                var calor = File.ReadAllText(file.OutputPath!);
                Assert.DoesNotMatch(@"§Y(IELD|BRK)", calor);
                // Each file alone references the other's partial members, so the
                // generated C# is validated once, together, by Run below.
                var compilation = Program.Compile(calor, Path.GetFileName(file.OutputPath!),
                    new CompilationOptions { StatusWriter = TextWriter.Null, DeferGeneratedOutputValidation = true });
                Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Errors) + "\n" + calor);
                generated.Add(compilation.GeneratedCode);
            }
            var losses = report.FileResults.SelectMany(file => file.Losses).Select(loss => loss.Feature).ToList();
            Assert.Equal(4, losses.Count(feature => feature is "iterator-accessor" or "partial-property"));
            Assert.Equal(Run(PartialParts), Run(generated.ToArray()));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static string R0Fixture(string id) => File.ReadAllText(Path.Combine(
        EvidenceContractTests.RepoRoot(), R0Fixtures, id + ".cs.txt"));

    private static async Task<(string Calor, List<string> Losses)> ConvertAsync(string source, Surface surface)
    {
        switch (surface)
        {
            case Surface.LibraryDefault:
                return Library(new ConversionOptions());
            case Surface.CliDefault or Surface.CliPassthrough:
                return Library(ConvertCommand.BuildCSharpToCalorOptions(
                    benchmark: false, verbose: false, explain: false, noFallback: false,
                    passthrough: surface == Surface.CliPassthrough, explicitCallClosers: false));
            case Surface.McpDefault or Surface.McpPassthroughOnError or Surface.McpPassthroughOnErrorModuleName:
            {
                var args = new Dictionary<string, object> { ["source"] = source };
                if (surface != Surface.McpDefault)
                    args["passthroughOnError"] = true;
                if (surface == Surface.McpPassthroughOnErrorModuleName)
                    args["moduleName"] = "Custom";
                var result = await new ConvertTool().ExecuteAsync(
                    JsonDocument.Parse(JsonSerializer.Serialize(args)).RootElement);
                Assert.False(result.IsError, result.Content[0].Text);
                using var payload = JsonDocument.Parse(result.Content[0].Text!);
                var root = payload.RootElement;
                var losses = root.GetProperty("lossSummary").GetProperty("locations").EnumerateArray()
                    .Select(location => location.GetProperty("feature").GetString()!).ToList();
                return (root.GetProperty("calorSource").GetString()!, losses);
            }
            default:
            {
                var dir = Path.Combine(Path.GetTempPath(), "calor-f4-1139-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    File.WriteAllText(Path.Combine(dir, "Fixture.cs"), source);
                    var migrator = new ProjectMigrator(new MigrationPlanOptions
                    {
                        Parallel = false,
                        SkipAnalyze = true,
                        SkipVerify = true,
                        PassthroughOnError = surface == Surface.ProjectMigratePassthrough
                    });
                    var plan = await migrator.CreatePlanAsync(dir, MigrationDirection.CSharpToCalor);
                    var report = await migrator.ExecuteAsync(plan);
                    var file = Assert.Single(report.FileResults);
                    Assert.NotEqual(FileMigrationStatus.Failed, file.Status);
                    return (File.ReadAllText(file.OutputPath!), file.Losses.Select(loss => loss.Feature).ToList());
                }
                finally
                {
                    try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
                }
            }
        }

        (string, List<string>) Library(ConversionOptions options)
        {
            var result = new CSharpToCalorConverter(options).Convert(source);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Issues));
            return (result.CalorSource!, result.Losses.Select(loss => loss.Feature).ToList());
        }
    }

    private static string Run(params string[] csharp)
    {
        var compilation = CSharpCompilation.Create("F4_" + Guid.NewGuid().ToString("N"),
            csharp.Select(source => CSharpSyntaxTree.ParseText(source)),
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics) + "\n" + string.Join("\n", csharp));
        stream.Position = 0;
        var context = new AssemblyLoadContext(compilation.AssemblyName!, isCollectible: true);
        try
        {
            var probe = context.LoadFromStream(stream).GetTypes().Single(type => type.Name == "Probe");
            var run = probe.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
            return Assert.IsType<string>(run.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }
}
