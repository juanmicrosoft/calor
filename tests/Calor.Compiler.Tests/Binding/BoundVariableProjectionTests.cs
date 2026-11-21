using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class BoundVariableProjectionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("INT")]
    [InlineData("INT,INT")]
    [InlineData("i32,INT")]
    [InlineData("INT,STRING")]
    [InlineData("INT,STRING,INT")]
    [InlineData("STRING,STR")]
    public void TypeProjectionMatchesTheOriginalDistinctCanonicalTypeRule(string types)
    {
        var symbols = types.Length == 0 ? [] : types.Split(',')
            .Select(type => new VariableSymbol("candidate", type, false)).ToArray();
        var variable = new VariableSymbol("reference", "INT", false);
        var canonicalTypes = symbols.Select(symbol => TypeIdentity.Canonicalize(symbol.TypeName))
            .Distinct(StringComparer.Ordinal).ToArray();
        var expression = new BoundVariableExpression(TextSpan.Empty, variable, symbols);
        Assert.Equal(canonicalTypes.Length == 1 ? "INT" : "OBJECT", expression.Type.DisplayString);
        Assert.Same(symbols, expression.ResolvedSymbols);
        Assert.Same(variable, expression.Variable);
    }

    [Fact]
    public void LaterInvalidCandidatesAreStillValidatedAfterTheTypesAlreadyDisagree()
    {
        var symbols = new[] { "INT", "STRING", " " }
            .Select(type => new VariableSymbol("candidate", type, false)).ToArray();
        var expected = Assert.Throws<ArgumentException>(() => symbols
            .Select(symbol => TypeIdentity.Canonicalize(symbol.TypeName)).Distinct(StringComparer.Ordinal).ToArray());
        var actual = Assert.Throws<ArgumentException>(() =>
            new BoundVariableExpression(TextSpan.Empty, symbols[0], symbols));
        Assert.Equal(expected.Message, actual.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageAuthorityAndExplicitTypeOverrideRemainIndependentOfDisplayProjection(bool unknown)
    {
        var storage = new VariableSymbol("storage", "OBJECT", false) { HasUnknownInferredType = unknown };
        var alias = new VariableSymbol("alias", " ", false) { StorageIdentity = storage };
        var known = new VariableSymbol("known", "INT", false);
        var type = new UnresolvedBoundType("not-established");
        var expression = new BoundVariableExpression(TextSpan.Empty, alias, [known, alias], type);
        Assert.Same(type, expression.Type);
        Assert.Equal(unknown, expression.HasUnknownInferredType);
        var reversed = new BoundVariableExpression(TextSpan.Empty, alias, [alias, known], type);
        Assert.Equal(unknown, reversed.HasUnknownInferredType);
    }
}
