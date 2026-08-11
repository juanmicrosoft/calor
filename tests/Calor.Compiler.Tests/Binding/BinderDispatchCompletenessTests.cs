using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Xunit;

// Flat test namespace: a Calor.Compiler.Tests.Binding namespace would shadow other test
// files' relative `Binding.X` qualifiers against Calor.Compiler.Binding.
namespace Calor.Compiler.Tests;

/// <summary>
/// #762 B1 (scoping doc D1): the dispatch table is the one authoritative expression
/// binding surface, and it must cover the concrete ExpressionNode subclass set EXACTLY —
/// in both directions. A new node class with no dispatch entry, or a stale entry for a
/// removed class, fails here by construction (the code-side half of F-1's bidirectional
/// completeness rule). The parser-totality work leaves 60 accepted expression classes;
/// KeywordArgNode is a parser helper rather than a value expression.
/// than the number so an F-1 amendment and a code change must move together.
/// </summary>
public class BinderDispatchCompletenessTests
{
    private static IReadOnlyList<Type> ConcreteExpressionTypes() =>
        typeof(ExpressionNode).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ExpressionNode).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void DispatchTable_CoversEveryConcreteExpressionNode_Exactly()
    {
        var concrete = ConcreteExpressionTypes();
        var dispatched = Binder.ExpressionDispatch.Keys.ToHashSet();

        var missing = concrete.Where(t => !dispatched.Contains(t)).Select(t => t.Name).ToList();
        var stale = dispatched.Except(concrete).Select(t => t.Name).ToList();

        Assert.True(missing.Count == 0,
            $"Concrete ExpressionNode classes with no dispatch entry: {string.Join(", ", missing)}");
        Assert.True(stale.Count == 0,
            $"Dispatch entries for types that are not concrete ExpressionNodes: {string.Join(", ", stale)}");
    }

    [Fact]
    public void ConcreteExpressionNodeCount_MatchesTheF1Freeze()
    {
        Assert.Equal(60, ConcreteExpressionTypes().Count);
    }
}
