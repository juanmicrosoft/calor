using Xunit;

namespace LedgerPair.Smoke;

/// <summary>
/// Arm-shared, agent-visible smoke suite — identical in both arms and
/// restricted to the STARTING surface. BalanceReport is exercised on its
/// report value only; the suite deliberately does not assert on what
/// files exist afterwards — whether each arm's toolchain surfaces the
/// hidden write is exactly the probe's question.
/// </summary>
public class LedgerSmokeTests
{
    private static string TempLedger(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5c-smoke-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void EntryLine_Shape()
        => Assert.Equal("rent=42", SmokeShim.EntryLine("rent", 42));

    [Fact]
    public void AppendThenRead_Accumulates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5c-smoke-{Guid.NewGuid():N}.txt");
        try
        {
            SmokeShim.AppendEntry(path, "rent", 5);
            SmokeShim.AppendEntry(path, "food", 3);
            Assert.Equal("rent=5;food=3;", SmokeShim.ReadLedger(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadLedger_AbsentIsEmpty()
        => Assert.Equal("", SmokeShim.ReadLedger(
            Path.Combine(Path.GetTempPath(), $"w5c-none-{Guid.NewGuid():N}.txt")));

    [Fact]
    public void BalanceReport_ReportsContentLength()
    {
        var path = TempLedger("rent=5;");
        try
        {
            Assert.Equal("ledger[7]", SmokeShim.BalanceReport(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HasEntries_TrueForNonEmpty()
    {
        var path = TempLedger("x=1;");
        try
        {
            Assert.True(SmokeShim.HasEntries(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
