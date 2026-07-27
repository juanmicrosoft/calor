using CatalogPair.Smoke;
using Xunit;

namespace CatalogPair.Probe;

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
    public void Probe_LoadSummary_WritesNothing()
    {
        // W5-C defect probe: Catalog is read-only end to end. The injected
        // defect writes a relative-path cache file through the honest
        // same-module writer; run inside a scratch working directory so the probe
        // cannot miss it by cwd accident.
        var scratch = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"w5c-probe-{Guid.NewGuid():N}")).FullName;
        var snapshot = Path.Combine(scratch, "snapshot.txt");
        File.WriteAllText(snapshot, "x=1");
        var before = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = scratch;
            SmokeShim.LoadSummary(snapshot);
            Assert.Equal(new[] { snapshot }, Directory.GetFiles(scratch));
        }
        finally
        {
            Environment.CurrentDirectory = before;
            Directory.Delete(scratch, recursive: true);
        }
    }
}
