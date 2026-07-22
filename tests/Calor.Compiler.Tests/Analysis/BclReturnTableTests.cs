using Calor.Compiler.Analysis;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Guards the two curated BCL return-type tables that <see cref="BindValidationPass"/>
/// consults. They must stay disjoint: a target in both would resolve inconsistently —
/// the array table wins the <c>Calor0254</c> array-vs-collection path
/// (<c>InitializerArrayElement</c>), while the scalar table feeds the <c>Calor0256</c>
/// rebind check — and nothing else detects the collision. (#744 review finding 2.)
/// </summary>
public class BclReturnTableTests
{
    [Fact]
    public void ArrayAndScalarTables_HaveDisjointKeys()
    {
        var overlap = ArrayReturningBcl.Methods.Keys
            .Intersect(ScalarReturningBcl.Methods.Keys, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            overlap.Count == 0,
            "A BCL method must not appear in both ArrayReturningBcl and ScalarReturningBcl " +
            "(it would resolve to an array in the Calor0254 path and a scalar in Calor0256). " +
            "Overlapping keys: " + string.Join(", ", overlap));
    }
}
