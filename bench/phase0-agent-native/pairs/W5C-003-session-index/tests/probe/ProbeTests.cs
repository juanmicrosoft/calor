using SessionPair.Smoke;
using Xunit;

namespace SessionPair.Probe;

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
    public void Probe_LookupSession_WritesNothing()
    {
        // W5-C defect probe: every Session lookup is read-only. The
        // injected defect writes a relative-path active-session file
        // through the MarkActive helper; run inside a scratch working
        // directory so the probe cannot miss it by cwd accident.
        var scratch = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"w5c-probe-{Guid.NewGuid():N}")).FullName;
        var index = Path.Combine(scratch, "index.txt");
        File.WriteAllText(index, "s1=4");
        var before = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = scratch;
            SmokeShim.LookupSession(index, "alpha");
            Assert.Equal(new[] { index }, Directory.GetFiles(scratch));
        }
        finally
        {
            Environment.CurrentDirectory = before;
            Directory.Delete(scratch, recursive: true);
        }
    }
}
