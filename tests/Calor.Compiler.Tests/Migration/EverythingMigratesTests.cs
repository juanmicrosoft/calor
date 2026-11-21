using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1609 "Everything migrates": default <c>calor migrate</c> and <c>calor convert</c>
/// write every input that parses. A unit that fails native conversion or a check drops
/// to exact whole-file preservation and records why; only the documented cases
/// (input that does not parse, destination safety, I/O, cancellation, dry run, compile
/// items outside the project) leave a file unwritten.
/// </summary>
public sealed class EverythingMigratesTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "calor-1609-" + Guid.NewGuid().ToString("N")[..8]);

    public EverythingMigratesTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_tempDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static ProjectMigrator Migrator(int perFileTimeoutSeconds = 60)
        => new(new MigrationPlanOptions
        {
            Parallel = false,
            MergePartialClasses = false,
            PerFileTimeoutSeconds = perFileTimeoutSeconds
        });

    private static bool IsWholeFile(FileMigrationResult result)
        => result.Losses.Any(loss => loss.Feature == "whole-file-preservation");

    private static string GeneratedCSharp(string calrPath)
    {
        var compiled = Program.Compile(File.ReadAllText(calrPath), calrPath,
            new CompilationOptions { DeferGeneratedOutputValidation = true });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        return compiled.GeneratedCode;
    }

    [Fact]
    public async Task Migrate_PerFileTimeout_PreservesTheWholeFileAndWritesIt()
    {
        // Old contract: a timed-out file was TimedOut and left unwritten.
        const string source = "public class A { public int Get() => 1; }\n";
        var input = Write("A.cs", source);
        var migrator = Migrator(perFileTimeoutSeconds: 0); // 0 = the limit expires at once
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        var report = await migrator.ExecuteAsync(plan);

        var file = Assert.Single(report.FileResults);
        Assert.Equal(FileMigrationStatus.Partial, file.Status);
        Assert.True(file.OutputWritten);
        Assert.True(IsWholeFile(file));
        Assert.Contains(file.Losses, loss => loss.Trigger == ConversionTrigger.Timeout);
        Assert.Contains(file.Issues, issue => issue.Feature == "conversion-timeout");
        Assert.Equal(0, report.Summary.TimedOutFiles);
        Assert.Equal(source, GeneratedCSharp(Path.ChangeExtension(input, ".calr")));
        Assert.True(File.Exists(Path.ChangeExtension(input, ".calr") + ".conversion.json"));
    }

    [Fact]
    public async Task Migrate_CSharpThatParsesButDoesNotCompile_IsPreservedAndWritten()
    {
        const string source = "public class Owner : System.DayOfWeek {}\n";
        var input = Write("Owner.cs", source);
        var migrator = Migrator();
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        var report = await migrator.ExecuteAsync(plan);

        var file = Assert.Single(report.FileResults);
        Assert.True(file.OutputWritten, string.Join("\n", file.Issues));
        Assert.Equal(FileMigrationStatus.Partial, file.Status);
        Assert.True(IsWholeFile(file));
        Assert.Contains(file.Issues, issue => issue.Feature == "original-csharp-validation"
            && issue.Severity == ConversionIssueSeverity.Warning);
        Assert.Equal(source, GeneratedCSharp(Path.ChangeExtension(input, ".calr")));
    }

    [Fact]
    public async Task Migrate_InputThatDoesNotParse_IsStillRefused()
    {
        var input = Write("Broken.cs", "public class Broken { public int M( { }\n");
        Write("Good.cs", "public class Good { public int Get() => 1; }\n");
        var migrator = Migrator();
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        var report = await migrator.ExecuteAsync(plan);

        var broken = Assert.Single(report.FileResults, file => file.SourcePath == input);
        Assert.Equal(FileMigrationStatus.Failed, broken.Status);
        Assert.False(File.Exists(Path.ChangeExtension(input, ".calr")));
        // The other file is unaffected.
        var good = Assert.Single(report.FileResults, file => file.SourcePath != input);
        Assert.True(good.OutputWritten);
    }

    [Fact]
    public async Task Migrate_ProjectErrorsThatNativeOutputDidNotCause_DoNotDragNativeFilesDown()
    {
        // B.cs has a real compile error, so it is preserved whole. The combined project
        // check still fails at B, and the error remains when every file is its original:
        // A's native output did not cause it, so A stays native and both are written.
        // Before #1609 the error made every file fall back, then left all unwritten.
        Write("A.cs", "public class A { public int Get() => 1; }\n");
        Write("B.cs", "public class B { public void M() { int x = \"s\"; } }\n");
        var migrator = Migrator();
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        var report = await migrator.ExecuteAsync(plan);

        Assert.Equal(0, report.Summary.FailedFiles);
        var a = Assert.Single(report.FileResults, file => file.SourcePath.EndsWith("A.cs", StringComparison.Ordinal));
        var b = Assert.Single(report.FileResults, file => file.SourcePath.EndsWith("B.cs", StringComparison.Ordinal));
        Assert.True(a.OutputWritten);
        Assert.True(b.OutputWritten);
        Assert.False(IsWholeFile(a));
        Assert.True(IsWholeFile(b));
        Assert.Contains(b.Issues, issue => issue.Feature == "roundtrip-csharp-validation"
            && issue.Message.Contains("B.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discovery_FileLargerThanTheAnalysisLimit_IsStillMigrated()
    {
        Write("Big.cs", "public class Big { public int Get() => 1; }\n");
        var migrator = new ProjectMigrator(new MigrationPlanOptions
        {
            Parallel = false, MergePartialClasses = false, MaxFileSizeBytes = 8
        });
        var plan = await migrator.CreatePlanAsync(_tempDir, MigrationDirection.CSharpToCalor);

        var entry = Assert.Single(plan.Entries);
        Assert.NotEqual(FileConvertibility.Skip, entry.Convertibility);
        Assert.Contains(entry.PotentialIssues, issue => issue.Contains("feature analysis skipped", StringComparison.Ordinal));
        var report = await migrator.ExecuteAsync(plan);
        Assert.True(Assert.Single(report.FileResults).OutputWritten);
    }

    [Fact]
    public async Task Migrate_CompileItemOutsideTheProjectDirectory_IsSkippedAndLeftUntouched()
    {
        // A linked file and a fake package contentFiles item, both outside the project
        // directory. Migrate must never write next to them (the NuGet cache in the field).
        var linked = Write("shared/Linked.cs", "public class Linked { public int Get() => 2; }\n");
        var content = Write("fakepkg/contentFiles/cs/any/External/External.cs",
            "internal static class External { }\n");
        Write("proj/Inside.cs", "public class Inside { public int Get() => new Linked().Get(); }\n");
        var project = Write("proj/Proj.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="..\shared\Linked.cs" Link="Linked.cs" />
                <Compile Include="{content}" />
              </ItemGroup>
            </Project>
            """);
        await RestoreAsync(project);
        var outsideBefore = OutsideArtifacts(linked, content);

        var migrator = Migrator();
        var plan = await migrator.CreatePlanAsync(project, MigrationDirection.CSharpToCalor);
        var report = await migrator.ExecuteAsync(plan);

        foreach (var outside in new[] { linked, content })
        {
            var entry = Assert.Single(plan.Entries, item =>
                CanonicalPath.Resolve(item.SourcePath) == CanonicalPath.Resolve(outside));
            Assert.Equal(FileConvertibility.Skip, entry.Convertibility);
            Assert.Contains("Outside the project directory", entry.SkipReason, StringComparison.Ordinal);
            var result = Assert.Single(report.FileResults, item =>
                CanonicalPath.Resolve(item.SourcePath) == CanonicalPath.Resolve(outside));
            Assert.Equal(FileMigrationStatus.Skipped, result.Status);
        }
        Assert.Equal(outsideBefore, OutsideArtifacts(linked, content));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_tempDir, "shared"), "*.calr*"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_tempDir, "fakepkg"), "*.calr*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, ".calor")));
        var inside = Assert.Single(report.FileResults, item => item.SourcePath.EndsWith("Inside.cs", StringComparison.Ordinal));
        Assert.True(inside.OutputWritten, string.Join("\n", inside.Issues));
    }

    [Fact]
    public void Convert_CSharpThatParsesButDoesNotCompile_WritesAndExitsUnverified()
    {
        var input = Write("Owner.cs", "public class Owner : System.DayOfWeek {}\n");
        var run = CliTestHarness.RunCli(_tempDir, "convert", input);
        Assert.True(run.ExitCode == 2, run.StdOut + run.StdErr);
        Assert.True(File.Exists(Path.ChangeExtension(input, ".calr")));
    }

    [Fact]
    public void Convert_MissingReference_IsACommandErrorThatWritesNothing()
    {
        var input = Write("A.cs", "public class A { }\n");
        var run = CliTestHarness.RunCli(_tempDir, "convert", input, "--reference",
            Path.Combine(_tempDir, "missing.dll"));
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Reference not found", run.StdErr, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.ChangeExtension(input, ".calr")));
    }

    private static Dictionary<string, bool> OutsideArtifacts(params string[] sources)
        => sources.SelectMany(source => new[]
            {
                Path.ChangeExtension(source, ".calr"),
                Path.ChangeExtension(source, ".calr") + ".conversion.json",
                Path.ChangeExtension(source, ".calr") + ".conversion.lock"
            })
            .ToDictionary(path => path, File.Exists);

    private async Task RestoreAsync(string project)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(project)!
        };
        foreach (var arg in new[] { "restore", project, "--source", _tempDir, "--disable-parallel",
                     "-p:NuGetAudit=false", "-p:UseSharedCompilation=false", "-nodeReuse:false" })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
