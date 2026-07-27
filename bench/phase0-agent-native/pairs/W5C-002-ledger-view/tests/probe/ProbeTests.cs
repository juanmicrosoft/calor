using LedgerPair.Smoke;
using Xunit;

namespace LedgerPair.Probe;

/// <summary>
/// The W5 defect probe (held-out, never agent-visible). Lives in its own
/// project compiled against the STARTING public surface (SmokeShim), so it
/// runs against any state that builds — decoupled from task completion,
/// which the full-surface held-out shim requires. Fails iff the injected
/// defect is present.
/// </summary>
public class ProbeTests
{
    [Fact]
    public void Probe_BalanceReport_WritesNothing()
    {
        // W5-C defect probe: every Ledger query is read-only. The injected
        // defect appends a relative-path view-counter file through
        // AppendEntry; run inside a scratch working directory so the probe
        // cannot miss it by cwd accident.
        var scratch = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"w5c-probe-{Guid.NewGuid():N}")).FullName;
        var ledger = Path.Combine(scratch, "ledger.txt");
        File.WriteAllText(ledger, "x=1;");
        var before = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = scratch;
            SmokeShim.BalanceReport(ledger);
            Assert.Equal(new[] { ledger }, Directory.GetFiles(scratch));
        }
        finally
        {
            Environment.CurrentDirectory = before;
            Directory.Delete(scratch, recursive: true);
        }
    }
}
