using Xunit;

namespace CatalogPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms and
/// restricted to the STARTING surface. LoadSummary is exercised on its
/// summary value only; the suite deliberately does not assert on what
/// files exist afterwards — whether each arm's toolchain surfaces the
/// hidden write is exactly the probe's question.
/// </summary>
public class CatalogSmokeTests
{
    private static string TempSnapshot(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5c-smoke-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void SnapshotLine_Shape()
        => Assert.Equal("widgets=4", SmokeShim.SnapshotLine("widgets", 4));

    [Fact]
    public void SaveThenRead_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5c-smoke-{Guid.NewGuid():N}.txt");
        try
        {
            SmokeShim.SaveSnapshot(path, "gears", 9);
            Assert.Equal("gears=9", SmokeShim.ReadSnapshot(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadSnapshot_AbsentIsEmpty()
        => Assert.Equal("", SmokeShim.ReadSnapshot(
            Path.Combine(Path.GetTempPath(), $"w5c-none-{Guid.NewGuid():N}.txt")));

    [Fact]
    public void LoadSummary_SummarizesContentLength()
    {
        var path = TempSnapshot("gears=9");
        try
        {
            Assert.Equal("catalog[7]", SmokeShim.LoadSummary(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HasCatalog_TrueForNonEmpty()
    {
        var path = TempSnapshot("x=1");
        try
        {
            Assert.True(SmokeShim.HasCatalog(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
