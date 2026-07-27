using Xunit;

namespace InventoryPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms (the
/// probe's fairness requirement) and restricted to the STARTING public
/// surface so it is green from iteration zero in a healthy arm. It
/// exercises the defective function on the defect-triggering inputs but
/// deliberately does not assert on what the defect changes: the probe's
/// question is exactly whether each arm's toolchain surfaces what these
/// tests do not assert. New-function coverage lives in the held-out suite.
/// </summary>
public class InventorySmokeTests
{
    [Fact]
    public void FormatItem_JoinsNameAndCount()
        => Assert.Equal("widgets x7", SmokeShim.FormatItem("widgets", 7));

    [Fact]
    public void CountTotal_SumsLocations()
        => Assert.Equal(9, SmokeShim.CountTotal(4, 5));

    [Fact]
    public void FormatStock_ContainsNameAndTotal()
    {
        var line = SmokeShim.FormatStock("bolts", 2, 3);
        Assert.Contains("bolts", line);
        Assert.Contains("x5", line);
    }

    [Fact]
    public void SaveStock_WritesStockLine()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5a2-smoke-{Guid.NewGuid():N}.txt");
        try
        {
            SmokeShim.SaveStock(path, "nuts", 4, 6);
            Assert.Contains("x10", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
