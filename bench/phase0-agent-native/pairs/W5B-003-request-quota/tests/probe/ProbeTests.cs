using QuotaPair.Smoke;
using Xunit;

namespace QuotaPair.Probe;

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
    public void Probe_GrantJustOverMax_IsCapped()
    {
        // W5-B defect probe: the declared invariant is result <= maxAllowed
        // for ALL inputs. The injected defect leaves grants in
        // (maxAllowed, maxAllowed+5] uncapped; 103 against maxAllowed 100
        // sits in that window.
        Assert.Equal(100, SmokeShim.GrantRequests(103, 100));
    }
}
