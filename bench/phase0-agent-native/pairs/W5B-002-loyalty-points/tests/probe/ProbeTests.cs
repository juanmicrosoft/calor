using LoyaltyPair.Smoke;
using Xunit;

namespace LoyaltyPair.Probe;

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
    public void Probe_AwardJustBelowFloor_GetsFloor()
    {
        // W5-B defect probe: the declared invariant is result >= minPoints
        // for ALL inputs. The injected defect leaves awards in
        // [minPoints-10, minPoints) unfloored; 20+5 against floor 30 sits
        // in that window.
        Assert.Equal(30, SmokeShim.AwardWithFloor(20, 5, 30));
    }
}
