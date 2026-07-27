using QuotePair.Smoke;
using Xunit;

namespace QuotePair.Probe;

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
    public void Probe_QuoteJustOverCap_IsCapped()
    {
        // W5-B defect probe: the declared invariant is result <= cap for
        // ALL inputs. The injected defect leaves quotes in (cap, cap+10]
        // uncapped; 90+15 against cap 100 sits in that window.
        Assert.Equal(100, SmokeShim.QuoteWithSurcharge(90, 15, 100));
    }
}
