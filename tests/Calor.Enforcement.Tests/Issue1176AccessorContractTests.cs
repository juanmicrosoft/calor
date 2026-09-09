using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// #1176 — a property accessor has no <c>§E</c> surface, so what it may do is an intrinsic
/// contract. Two things were wrong with that contract.
///
/// <para><b>The getter had none at all.</b> <c>EffectEnforcementPass</c> registered only
/// <c>property.Setter</c> and <c>property.Initer</c>, so a getter fell through to
/// <c>CheckEffects</c> as an ordinary function whose declared row is the <c>effects: null</c> that
/// <c>CallGraphAnalysis.ToPropertyAccessorFunctionNode</c> hard-codes — declared pure, always, with
/// nowhere to say otherwise. Every effect in every getter body was <c>Calor0410</c> and unfixable.
/// The pass's own comment already described the rule as covering "custom accessors", so this was an
/// omission rather than a decision.</para>
///
/// <para><b>The contract was too narrow.</b> It allowed <c>mut</c> where a constructor gets
/// <c>mut, alloc</c>. The reason that mattered is <see cref="ReadingAnEffectfulGetterStillChargesTheReader"/>:
/// the contract is <b>not</b> the enforcement boundary. Reading a property charges the getter's
/// computed effects to the reader, so an effect cannot hide behind an accessor whatever the
/// contract says. Forbidding <c>alloc</c> therefore bought no soundness while costing real code —
/// a getter that returns a computed value allocates, and a memoising one mutates.</para>
///
/// <para>On the converted corpus this took <c>Calor0410</c> from <b>9 to 0</b> and <c>Calor0423</c>
/// from 7 to 2 — the residual #1173 could not close, because it was undeclarable rather than
/// under-declared.</para>
/// </summary>
public class Issue1176AccessorContractTests
{
    /// <summary>
    /// The load-bearing one. Everything above rests on effects reaching the reader regardless of
    /// the contract; if this stopped holding, widening the contract WOULD open a laundering path
    /// and the reasoning in <c>AccessorContract</c> would be wrong.
    ///
    /// <para>The probe has to use an effect the contract PERMITS. A getter that writes to the
    /// console is rejected outright (<see cref="AGetterThatWritesToTheConsoleIsStillRejected"/>),
    /// so it can never demonstrate propagation — the compile stops at the accessor. What must hold
    /// is the harder case: an accessor doing something it is *allowed* to do still charges its
    /// reader, so the permission is about where the effect may be written, never about whether it
    /// is counted.</para>
    /// </summary>
    [Fact]
    public void ReadingAGetterChargesTheReaderWithEffectsTheContractPermits()
    {
        const string source = """
            §M{m001:GetterCharge}
              §U{System.Collections.Generic}

              §CL{c001:Box:pub}
                §FLD{List<i32>:_items:priv}

                §PROP{p001:Items:List<i32>:pub}
                  §GET
                    §IF{if1} (== this._items null)
                      §ASSIGN this._items §NEW{List<i32>} §/NEW
                    §R this._items

                §MT{mt001:Peek:pub} () -> List<i32>
                  §E{}
                  §R this.Items
            """;

        var result = TestHarness.CompileWithEffects(source);

        // The accessor itself is within contract...
        Assert.DoesNotContain(
            result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.AccessorEffectContractUnavailable);

        // ...and its reader, which declares §E{}, is still charged for what it does.
        Assert.Contains(
            result.Diagnostics,
            d => d.Code == DiagnosticCode.ForbiddenEffect
                && d.Message.Contains("Peek", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAllocatingGetterIsWithinTheContract()
    {
        const string source = """
            §M{m001:AllocGetter}
              §U{System.Collections.Generic}

              §CL{c001:Box:pub}
                §PROP{p001:Items:List<i32>:pub}
                  §GET
                    §R §NEW{List<i32>} §/NEW
            """;

        var result = TestHarness.CompileWithEffects(source);

        Assert.DoesNotContain(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(
            result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.AccessorEffectContractUnavailable);
    }

    /// <summary>
    /// The memoising getter — the pattern behind every residual on the converted corpus, and the
    /// one the old <c>mut</c>-only contract could not express even after #1176 registered getters.
    /// </summary>
    [Fact]
    public void AMemoisingGetterMutatesAndAllocatesAndIsStillWithinTheContract()
    {
        const string source = """
            §M{m001:Memoise}
              §U{System.Collections.Generic}

              §CL{c001:Box:pub}
                §FLD{List<i32>:_items:priv}

                §PROP{p001:Items:List<i32>:pub}
                  §GET
                    §IF{if1} (== this._items null)
                      §ASSIGN this._items §NEW{List<i32>} §/NEW
                    §R this._items
            """;

        var result = TestHarness.CompileWithEffects(source);

        Assert.DoesNotContain(
            result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.AccessorEffectContractUnavailable
                || d.Code == DiagnosticCode.ForbiddenEffect);
    }

    /// <summary>
    /// Fail-closed is preserved, and this is what stops the widening from being a quiet
    /// relaxation: an accessor doing something it could not intrinsically need is still rejected,
    /// and must move behind a declared method.
    /// </summary>
    [Fact]
    public void AGetterThatWritesToTheConsoleIsStillRejected()
    {
        const string source = """
            §M{m001:LoudGetter}
              §CL{c001:Box:pub}
                §PROP{p001:Value:i32:pub}
                  §GET
                    §P "side effect"
                    §R INT:1
            """;

        var result = TestHarness.CompileWithEffects(source);

        Assert.Contains(
            result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.AccessorEffectContractUnavailable
                && d.Message.Contains("Value", StringComparison.Ordinal));
    }

    /// <summary>
    /// The widening applies to every accessor, not only the getter that prompted it — one contract
    /// for the whole declaration form, as constructors already had.
    /// </summary>
    [Fact]
    public void AnAllocatingSetterIsWithinTheContract()
    {
        const string source = """
            §M{m001:AllocSetter}
              §U{System.Collections.Generic}

              §CL{c001:Box:pub}
                §FLD{List<i32>:_items:priv}

                §PROP{p001:Items:List<i32>:pub}
                  §SET
                    §ASSIGN this._items §NEW{List<i32>} §/NEW
            """;

        var result = TestHarness.CompileWithEffects(source);

        Assert.DoesNotContain(
            result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.AccessorEffectContractUnavailable);
    }
}
