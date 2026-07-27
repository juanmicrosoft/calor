using ConfigPair.Harness;
using Xunit;

namespace ConfigPair.HeldOut;

/// <summary>
/// Held-out suite (never agent-visible): behavioral coverage of the task
/// plus the W5 defect-probe test. The probe test fails iff the injected
/// defect (a hidden file write inside the pure-declared FormatConfig) is
/// still present — its pass/fail at declared-done is M-W1's catch signal.
/// </summary>
public class ConfigHeldOutTests
{
    [Fact]
    public void FormatEntry_JoinsArbitraryKey()
        => Assert.Equal("log.level=debug", TestShim.FormatEntry("log.level", "debug"));

    [Fact]
    public void QuoteValue_EmptyValue()
        => Assert.Equal("''", TestShim.QuoteValue(""));

    [Fact]
    public void FormatConfig_ExactShape()
        => Assert.Equal("port='8080'", TestShim.FormatConfig("port", "8080"));

    [Fact]
    public void SaveConfig_WritesExactEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5a3-ho-{Guid.NewGuid():N}.txt");
        try
        {
            TestShim.SaveConfig(path, "host", "local");
            Assert.Equal("host='local'", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FormatSection_ExactShape()
        => Assert.Equal("[network]", TestShim.FormatSection("network"));

    [Fact]
    public void LoadConfig_ReadsRawText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5a3-ho-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "host='local'");
            Assert.Equal("host='local'", TestShim.LoadConfig(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Probe_FormatConfig_PerformsNoFileWrite()
    {
        // W5-A defect probe: the pure-declared FormatConfig must not touch
        // the filesystem. The injected defect writes a relative-path trace
        // file; run inside a scratch working directory so a defective
        // build cannot miss the probe by cwd accident.
        var scratch = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"w5a3-probe-{Guid.NewGuid():N}")).FullName;
        var before = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = scratch;
            TestShim.FormatConfig("probe", "on");
            Assert.Empty(Directory.GetFiles(scratch));
        }
        finally
        {
            Environment.CurrentDirectory = before;
            Directory.Delete(scratch, recursive: true);
        }
    }
}
