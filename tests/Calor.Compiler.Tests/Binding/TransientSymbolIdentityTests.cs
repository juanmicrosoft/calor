using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class TransientSymbolIdentityTests
{
    [Fact]
    public void AmbiguousTransientDeclarationsReportAnErrorAndDoNotReuseAStaleKey()
    {
        const string original = "§M{Demo}\n  §F{Pick:pub} () -> i32\n    §R 1\n";
        var old = Assert.Single(Bind(original).Functions).Symbol.Id;
        var diagnostics = new DiagnosticBag();
        var source = original + "  §F{Pick:pub} () -> i32\n    §R 2\n";
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var current = new Binder(diagnostics, "memory:transient-tests").Bind(module);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.DuplicateFunctionSignature);
        Assert.DoesNotContain(current.Functions, function => function.Symbol.Id == old);
    }

    [Fact]
    public void InsertionAndOverloadReorderingNeverRetargetExistingFunctionKeys()
    {
        const string first = """
            §M{Demo}
              §F{Pick:pub} (i32:value) -> i32
                §R value
              §F{Pick:pub} (str:value) -> str
                §R value
            """;
        const string reordered = """
            §M{Demo}
              §F{Before:pub} () -> i32
                §R 0
              §F{Pick:pub} (str:value) -> str
                §R value
              §F{Pick:pub} (i32:value) -> i32
                §R value
            """;
        var old = Bind(first);
        var current = Bind(reordered);
        Assert.Equal(2, old.Functions.Select(f => f.Symbol.Id).Distinct().Count());
        foreach (var function in old.Functions)
        {
            var actual = Assert.Single(current.Functions.Where(f => f.Symbol.Id == function.Symbol.Id));
            Assert.Equal(function.Symbol.Name, actual.Symbol.Name);
            Assert.Equal(function.Symbol.Parameters.Single().TypeName, actual.Symbol.Parameters.Single().TypeName);
        }
    }

    [Fact]
    public void GenericArityNamespaceAndEnclosingTypesDisambiguateTransientKeys()
    {
        const string source = """
            §M{Demo}
              §NS{One}
                §F{Pick:pub} () -> i32
                  §R 1
                §F{Pick:pub}<T> () -> i32
                  §R 2
                §CL{A:pub}
                  §MT{Pick:pub} () -> i32
                    §R 3
                §CL{B:pub}
                  §MT{Pick:pub} () -> i32
                    §R 4
              §NS{Two}
                §F{Pick:pub} () -> i32
                  §R 5
            """;
        var bound = Bind(source);
        Assert.Equal(5, bound.Functions.Count);
        Assert.Equal(5, bound.Functions.Select(f => f.Symbol.Id).Distinct().Count());
        Assert.Equal(bound.Functions.Select(f => f.Symbol.Id),
            Bind(source).Functions.Select(f => f.Symbol.Id));
    }

    [Fact]
    public void SameNamedLocalsInNestedAndSiblingScopesAreSnapshotScopedNotStaleHandles()
    {
        const string source = """
            §M{Demo}
              §F{Run:pub} (bool:condition) -> i32
                §IF condition
                  §B{value:i32} 1
                  §IF condition
                    §B{value:i32} 2
                    §P value
                  §P value
                §EL
                  §B{value:i32} 3
                  §P value
                §R 0
            """;
        var old = Bind(source);
        var current = Bind(source.Replace("§B{value:i32} 1", "§B{other:i32} 0\n      §B{value:i32} 1"));
        var oldLocals = old.SymbolsById.Values.OfType<VariableSymbol>()
            .Where(s => s.Name == "value").ToArray();
        var currentLocals = current.SymbolsById.Values.OfType<VariableSymbol>()
            .Where(s => s.Name == "value").ToArray();
        Assert.Equal(3, oldLocals.Length);
        Assert.Equal(3, oldLocals.Select(s => s.Id).Distinct().Count());
        Assert.Equal(3, currentLocals.Length);
        Assert.Empty(oldLocals.Select(s => s.Id).Intersect(currentLocals.Select(s => s.Id)));
        Assert.Equal(old.Functions.Single().Symbol.Id, current.Functions.Single().Symbol.Id);
    }

    private static BoundModule Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var bound = new Binder(diagnostics, "memory:transient-tests").Bind(module);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return bound;
    }
}
