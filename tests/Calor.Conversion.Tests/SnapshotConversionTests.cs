using Calor.Compiler.Migration;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Conversion.Tests;

/// <summary>
/// Tests that convert C# snippets from challenge reports and compare
/// against approved .calr snapshot files.
///
/// To update snapshots, set CALOR_UPDATE_SNAPSHOTS=1 and run tests.
/// </summary>
public class SnapshotConversionTests
{
    private readonly ITestOutputHelper _output;

    public SnapshotConversionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> SupportedSnippetData()
    {
        foreach (var snippet in ConversionCatalog.SupportedSnippets)
            yield return new object[] { snippet.Id, snippet.Description, snippet.CSharpSource };
    }

    [Theory]
    [MemberData(nameof(SupportedSnippetData))]
    public void Convert_ProducesCalorOutput(string id, string description, string csharpSource)
    {
        var result = TestHelpers.ConvertCSharp(csharpSource, $"Test_{id.Replace("-", "_")}");

        _output.WriteLine($"[{id}] {description}");
        if (result.CalorSource != null)
            _output.WriteLine(result.CalorSource);

        Assert.True(result.Success,
            $"Conversion failed for [{id}] {description}: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);
        Assert.NotEmpty(result.CalorSource!);
    }

    [Theory]
    [MemberData(nameof(SupportedSnippetData))]
    public void Convert_MatchesApprovedSnapshot(string id, string description, string csharpSource)
    {
        var snapshotName = $"{id}.approved.calr";
        var approved = TestHelpers.ReadSnapshot(snapshotName);

        var result = TestHelpers.ConvertCSharp(csharpSource, $"Test_{id.Replace("-", "_")}");

        Assert.True(result.Success,
            $"Conversion failed for [{id}] {description}: " +
            string.Join("; ", result.Issues.Select(i => i.Message)));
        Assert.NotNull(result.CalorSource);

        // If running in update mode, write the snapshot
        if (Environment.GetEnvironmentVariable("CALOR_UPDATE_SNAPSHOTS") == "1")
        {
            WriteSnapshot(snapshotName, result.CalorSource!);
            _output.WriteLine($"[{id}] Snapshot updated: {snapshotName}");
            return;
        }

        if (approved == null)
        {
            // No snapshot exists yet — generate it and skip
            WriteSnapshot(snapshotName, result.CalorSource!);
            _output.WriteLine($"[{id}] Initial snapshot generated: {snapshotName}");
            return;
        }

        // Normalize line endings for comparison
        var normalizedApproved = approved.Replace("\r\n", "\n").Trim();
        var normalizedActual = result.CalorSource!.Replace("\r\n", "\n").Trim();

        Assert.Equal(normalizedApproved, normalizedActual);
    }

    [Fact]
    public void Convert_AllSupportedSnippets_ProduceCalorWithModuleTag()
    {
        var failures = new List<string>();

        foreach (var snippet in ConversionCatalog.SupportedSnippets)
        {
            var result = TestHelpers.ConvertCSharp(snippet.CSharpSource, $"Test_{snippet.Id.Replace("-", "_")}");

            if (!result.Success || result.CalorSource == null)
            {
                failures.Add($"[{snippet.Id}] {snippet.Description}: conversion failed");
                continue;
            }

            if (!result.CalorSource.Contains("§M{"))
            {
                failures.Add($"[{snippet.Id}] {snippet.Description}: missing §M module tag");
            }
        }

        Assert.Empty(failures);
    }

    private static void WriteSnapshot(string snapshotName, string content)
    {
        // Write to the source directory (not the bin output)
        var projectDir = FindProjectDirectory();
        if (projectDir == null) return;

        var snapshotDir = Path.Combine(projectDir, "Snapshots");
        Directory.CreateDirectory(snapshotDir);
        File.WriteAllText(Path.Combine(snapshotDir, snapshotName), content);
    }

    private static string? FindProjectDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Calor.Conversion.Tests.csproj")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
