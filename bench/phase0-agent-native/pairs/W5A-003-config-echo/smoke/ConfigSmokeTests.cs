using Xunit;

namespace ConfigPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms (the
/// probe's fairness requirement) and restricted to the STARTING public
/// surface so it is green from iteration zero in a healthy arm. It
/// exercises the defective function on the defect-triggering inputs but
/// deliberately does not assert on what the defect changes: the probe's
/// question is exactly whether each arm's toolchain surfaces what these
/// tests do not assert. New-function coverage lives in the held-out suite.
/// </summary>
public class ConfigSmokeTests
{
    [Fact]
    public void FormatEntry_JoinsKeyAndValue()
        => Assert.Equal("host=local", SmokeShim.FormatEntry("host", "local"));

    [Fact]
    public void QuoteValue_WrapsInSingleQuotes()
        => Assert.Equal("'on'", SmokeShim.QuoteValue("on"));

    [Fact]
    public void FormatConfig_ContainsKeyAndQuotedValue()
    {
        var line = SmokeShim.FormatConfig("host", "local");
        Assert.Contains("host=", line);
        Assert.Contains("'local'", line);
    }

    [Fact]
    public void SaveConfig_WritesEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5a3-smoke-{Guid.NewGuid():N}.txt");
        try
        {
            SmokeShim.SaveConfig(path, "port", "8080");
            Assert.Contains("'8080'", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
