using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// #1618: preconditions are parameter refinements, so the metrics count one precondition per
/// refined parameter on a declaration line, and nothing else that happens to contain a bar.
/// </summary>
public sealed class CalorSyntaxPreconditionTests
{
    [Theory]
    [InlineData("  func F (int:x | (# > 0)) → int", 1)]
    [InlineData("  func F public (int:lo, int:hi | (# > lo)) → int", 1)]
    [InlineData("  func F (int:a | (# != 0), int:b | ((# > 0) && (# < 10))) → int", 2)]
    [InlineData("  ctor public (string:name | (#.Length > 0))", 1)]
    [InlineData("  [@Obsolete] func F (int:x | (# >= 0)) → int", 1)]
    [InlineData("  op + public (Money:a, Money:b | (# != null)) → Money", 1)]
    [InlineData("  func F (int:x) → int", 0)]
    [InlineData("    return x || (y)", 0)]
    [InlineData("    let int:n | (# > 0) = 1", 0)]
    [InlineData("    if a | (b)", 0)]
    [InlineData("  func F (bool:x) → bool\n    return x || (true)", 0)]
    public void CountsRefinedParametersOnDeclarationLines(string source, int expected)
        => Assert.Equal(expected, CalorSyntax.Precondition.Matches(source).Count);
}
