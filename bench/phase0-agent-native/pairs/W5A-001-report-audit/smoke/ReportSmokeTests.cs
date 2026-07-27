using Xunit;

namespace ReportPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms (the
/// probe's fairness requirement) and restricted to the STARTING public
/// surface so it is green from iteration zero in a healthy arm. It
/// exercises the defective function on the defect-triggering inputs but
/// deliberately does not assert on what the defect changes: the probe's
/// question is exactly whether each arm's toolchain surfaces what these
/// tests do not assert. New-function coverage lives in the held-out suite.
/// </summary>
public class ReportSmokeTests
{
    [Fact]
    public void FormatHeader_WrapsTitle()
        => Assert.Equal("== Q3 ==", SmokeShim.FormatHeader("Q3"));

    [Fact]
    public void FormatLine_JoinsNameAndValue()
        => Assert.Equal("total: 7", SmokeShim.FormatLine("total", 7));

    [Fact]
    public void FormatSummary_ContainsHeaderAndTotal()
    {
        var summary = SmokeShim.FormatSummary("Q3", 42);
        Assert.Contains("== Q3 ==", summary);
        Assert.Contains("total: 42", summary);
    }

    [Fact]
    public void WriteReport_WritesSummary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5a-smoke-{Guid.NewGuid():N}.txt");
        try
        {
            SmokeShim.WriteReport(path, "Q3", 42);
            Assert.Contains("total: 42", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
