using LedgerPair.Harness;
using Xunit;

namespace LedgerPair.HeldOut;

/// <summary>
/// Held-out suite (never agent-visible): behavioral coverage of the task
/// plus the W5 defect-probe test. The probe test fails iff the injected
/// defect (a hidden view-counter append inside the read-only-declared
/// BalanceReport) is still present — its pass/fail at declared-done is
/// M-W1's catch signal.
/// </summary>
public class LedgerHeldOutTests
{
    private static string TempLedger(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"w5c-ho-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ReportOf_EmptyContent()
        => Assert.Equal("ledger[0]", TestShim.ReportOf(""));

    [Fact]
    public void BalanceReport_AbsentLedger()
        => Assert.Equal("ledger[0]", TestShim.BalanceReport(
            Path.Combine(Path.GetTempPath(), $"w5c-none-{Guid.NewGuid():N}.txt")));

    [Fact]
    public void HasEntries_FalseForAbsent()
        => Assert.False(TestShim.HasEntries(
            Path.Combine(Path.GetTempPath(), $"w5c-none-{Guid.NewGuid():N}.txt")));

    [Fact]
    public void DescribeLedger_PrependsLabel()
    {
        var path = TempLedger("rent=5;");
        try
        {
            Assert.Equal("ledger: ledger[7]", TestShim.DescribeLedger(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LedgerSize_LengthOfContent()
    {
        var path = TempLedger("rent=5;");
        try
        {
            Assert.Equal(7, TestShim.LedgerSize(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LedgerSize_ZeroWhenAbsent()
        => Assert.Equal(0, TestShim.LedgerSize(
            Path.Combine(Path.GetTempPath(), $"w5c-none-{Guid.NewGuid():N}.txt")));

}
