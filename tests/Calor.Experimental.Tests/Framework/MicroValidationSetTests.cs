using System.Text.Json;
using Xunit;

namespace Calor.Experimental.Tests.Framework;

/// <summary>
/// Tests for <see cref="MicroValidationSet.Load"/> — disk layout to in-memory model.
/// Each test creates a temp directory with a specific shape and verifies the loader
/// handles it correctly (or fails with a clear error).
/// </summary>
public class MicroValidationSetTests : IDisposable
{
    private readonly string _tempDir;

    public MicroValidationSetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "micro-validation-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateTierDir(string hypothesisId)
    {
        var dir = Path.Combine(_tempDir, hypothesisId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteManifest(string dir, MicroValidationManifest manifest)
    {
        File.WriteAllText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true
            }));
    }

    private static void WritePrograms(string dir, string category, int count)
    {
        var sub = Path.Combine(dir, category);
        Directory.CreateDirectory(sub);
        for (int i = 0; i < count; i++)
        {
            File.WriteAllText(Path.Combine(sub, $"{category[..3]}_{i:D3}.calr"), "§M{m1:Test} §/M{m1}");
        }
        // Add a non-.calr file to prove enumerator filters by extension.
        File.WriteAllText(Path.Combine(sub, "README.md"), "# test data");
    }

    [Fact]
    public void Load_ValidDirectory_ReturnsPopulatedSet()
    {
        var dir = CreateTierDir("TIER-TEST");
        WriteManifest(dir, new MicroValidationManifest
        {
            HypothesisId = "TIER-TEST",
            ExperimentalFlag = "test-flag",
            ExpectedDiagnosticCode = "Calor0000"
        });
        WritePrograms(dir, "positive", 7);
        WritePrograms(dir, "negative", 5);
        WritePrograms(dir, "edge", 3);

        var set = MicroValidationSet.Load(dir);

        Assert.Equal("TIER-TEST", set.Manifest.HypothesisId);
        Assert.Equal("test-flag", set.Manifest.ExperimentalFlag);
        Assert.Equal(7, set.PositivePrograms.Count);
        Assert.Equal(5, set.NegativePrograms.Count);
        Assert.Equal(3, set.EdgePrograms.Count);
        Assert.Equal(15, set.TotalPrograms);
    }

    [Fact]
    public void Load_ProgramsAreCalrOnly()
    {
        var dir = CreateTierDir("TIER-FILTER");
        WriteManifest(dir, new MicroValidationManifest { HypothesisId = "TIER-FILTER" });
        WritePrograms(dir, "positive", 3); // plus a README.md per the helper

        var set = MicroValidationSet.Load(dir);

        // Only 3 .calr files, not 4 (README.md excluded).
        Assert.Equal(3, set.PositivePrograms.Count);
        Assert.All(set.PositivePrograms, p => Assert.EndsWith(".calr", p));
    }

    [Fact]
    public void Load_MissingCategoryDirectory_ReturnsEmptyListForThatCategory()
    {
        var dir = CreateTierDir("TIER-PARTIAL");
        WriteManifest(dir, new MicroValidationManifest { HypothesisId = "TIER-PARTIAL" });
        WritePrograms(dir, "positive", 5);
        // negative/ and edge/ deliberately not created

        var set = MicroValidationSet.Load(dir);

        Assert.Equal(5, set.PositivePrograms.Count);
        Assert.Empty(set.NegativePrograms);
        Assert.Empty(set.EdgePrograms);
    }

    [Fact]
    public void Load_MissingManifest_Throws()
    {
        var dir = CreateTierDir("TIER-NO-MANIFEST");
        WritePrograms(dir, "positive", 5);

        Assert.Throws<FileNotFoundException>(() => MicroValidationSet.Load(dir));
    }

    [Fact]
    public void Load_NonExistentDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            MicroValidationSet.Load(Path.Combine(_tempDir, "does-not-exist")));
    }

    [Fact]
    public void Load_ProgramsAreSortedDeterministically()
    {
        var dir = CreateTierDir("TIER-SORT");
        WriteManifest(dir, new MicroValidationManifest { HypothesisId = "TIER-SORT" });
        var posDir = Path.Combine(dir, "positive");
        Directory.CreateDirectory(posDir);
        File.WriteAllText(Path.Combine(posDir, "b.calr"), "");
        File.WriteAllText(Path.Combine(posDir, "a.calr"), "");
        File.WriteAllText(Path.Combine(posDir, "c.calr"), "");

        var set = MicroValidationSet.Load(dir);

        // Lexicographic: a < b < c
        Assert.EndsWith("a.calr", set.PositivePrograms[0]);
        Assert.EndsWith("b.calr", set.PositivePrograms[1]);
        Assert.EndsWith("c.calr", set.PositivePrograms[2]);
    }
}
