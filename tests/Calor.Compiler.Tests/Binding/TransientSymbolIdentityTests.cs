using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class TransientSymbolIdentityTests
{
    [Theory]
    [InlineData("effects")]
    [InlineData("effect-variable")]
    [InlineData("effect-order")]
    [InlineData("attributes")]
    public void SameWidthNonScalarSemanticEditsInvalidateSnapshotHandles(string edit)
    {
        var source = """
            §M{Demo}
              §F{Run:pub}<eff e, eff f> (i32:value) -> i32
                §E{cw,e,f}
                §B{offset:i32} 1
                §B{callback:System.Func<i32,i32>} §LAM{x:i32} (+ x value)
                §R value
            """;
        var changed = edit switch
        {
            "effects" => source.Replace("cw", "cr"),
            "effect-variable" => source.Replace("eff e", "eff g").Replace("cw,e,f", "cw,g,f"),
            "effect-order" => source.Replace("cw,e,f", "cw,f,e"),
            _ => source
        };
        BoundModule Read(string text, string attribute)
        {
            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer(text, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            if (edit == "attributes") module.Functions.Single().Attributes.Add("custom", attribute);
            return new Binder(diagnostics, "memory:transient-tests").Bind(module);
        }

        var first = Read(source, "first");
        var second = Read(changed, "other");
        var old = first.SymbolsById.Keys.Where(id => id.Value.Contains("snapshot")).ToArray();
        Assert.NotEmpty(old);
        Assert.Contains(old, id => id.Value.Contains("lambda"));
        Assert.All(old, id => Assert.False(second.SymbolsById.ContainsKey(id)));
    }

    [Fact]
    public void UnorderedEffectMapInsertionOrderDoesNotChangeSnapshotHandles()
    {
        const string source = "§M{Demo}\n  §F{Run:pub} () -> i32\n    §E{cw,mut:col}\n    §B{value:i32} 1\n    §R value\n";
        var first = Bind(source);
        var second = Bind(source.Replace("cw,mut:col", "mut:col,cw"));
        Assert.Equal(first.SymbolsById.Keys.OrderBy(id => id.Value), second.SymbolsById.Keys.OrderBy(id => id.Value));
    }

    [Theory]
    [InlineData("§CTOR{pub} ()", "§B{value:i32} 1", "§B{value:i32} 2", BoundMemberKind.Constructor)]
    [InlineData("§OP{+:pub} (Box:a, Box:b) -> Box", "§R a", "§R b", BoundMemberKind.OperatorOverload)]
    public void AmbiguousConstructorAndOperatorSnapshotsNeverRedirectStaleHandles(
        string header, string firstBody, string secondBody, BoundMemberKind kind)
    {
        string Source(params string[] bodies) => "§M{Demo}\n  §CL{Box:pub}\n"
            + string.Concat(bodies.Select(body => $"    {header}\n      {body}\n"));
        BoundModule Read(string source)
        {
            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
            return new Binder(diagnostics, "memory:transient-tests").Bind(module);
        }

        var old = Assert.Single(Read(Source(firstBody)).Functions.Where(f => f.MemberKind == kind)).Symbol.Id;
        var ambiguous = Read(Source(firstBody, secondBody));
        var reordered = Read(Source(secondBody, firstBody));
        var symbols = ambiguous.Functions.Where(f => f.MemberKind == kind).Select(f => f.Symbol.Id).ToArray();
        Assert.Equal(2, symbols.Length);
        Assert.Equal(2, symbols.Distinct().Count());
        Assert.False(ambiguous.SymbolsById.ContainsKey(old));
        Assert.False(reordered.SymbolsById.ContainsKey(old));
        Assert.All(symbols, id => Assert.False(reordered.SymbolsById.ContainsKey(id)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalCompilationsKeepPublicArtifactsAndSidecarIdentitiesDeterministic(bool invalidAssignment)
    {
        var source = """
            §M{Demo}
              §F{Run:pub} (i32:value) -> i32
                §B{offset:i32} 1
                §B{callback:System.Func<i32,i32>} §LAM{x:i32} (+ x value)
                §R §C{callback} §A offset §/C
            """ + "\n";
        if (invalidAssignment)
            source = source.Replace("    §R ", "    §ASSIGN offset 2\n    §R ");
        var root = Path.Combine(Path.GetTempPath(), "calor-transient-determinism-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "active.calr");
            File.WriteAllText(file, source);
            var store = new DeclarationIdentityStore(root);
            store.Sync([file]);
            var first = Program.Compile(source, file);
            var sidecar = File.ReadAllText(store.SidecarPath);
            var firstBound = new Binder(new DiagnosticBag(), file).Bind(first.Ast!);
            var second = Program.Compile(source, file);
            var secondBound = new Binder(new DiagnosticBag(), file).Bind(second.Ast!);
            Assert.Equal(invalidAssignment, first.HasErrors);
            if (!invalidAssignment) Assert.NotEmpty(first.GeneratedCode);
            else Assert.Contains(first.Diagnostics, d => d.Code == DiagnosticCode.WriteToReadOnlyLocal);
            Assert.Equal(first.GeneratedCode, second.GeneratedCode);
            Assert.Equal(new JsonDiagnosticFormatter().Format(first.Diagnostics),
                new JsonDiagnosticFormatter().Format(second.Diagnostics));
            Assert.Equal(firstBound.SymbolsById.Keys.OrderBy(id => id.Value),
                secondBound.SymbolsById.Keys.OrderBy(id => id.Value));
            Assert.Contains(firstBound.SymbolsById.Keys, id => id.Value.Contains("/snapshot/"));
            Assert.Equal(sidecar, File.ReadAllText(store.SidecarPath));
            Assert.DoesNotContain("snapshot", sidecar);
            Assert.Equal(source, File.ReadAllText(file));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
