using InventoryPair.Smoke;
using Xunit;

namespace InventoryPair.Probe;

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
    public void Probe_FormatStock_PerformsNoFileWrite()
    {
        // W5-A defect probe: the pure-declared FormatStock must not touch
        // the filesystem. The injected defect appends to a relative-path
        // audit log; run inside a scratch working directory so a defective
        // build cannot miss the probe by cwd accident.
        var scratch = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"w5a2-probe-{Guid.NewGuid():N}")).FullName;
        var before = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = scratch;
            SmokeShim.FormatStock("probe", 1, 1);
            Assert.Empty(Directory.GetFiles(scratch));
        }
        finally
        {
            Environment.CurrentDirectory = before;
            Directory.Delete(scratch, recursive: true);
        }
    }
}
