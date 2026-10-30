using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class IdentityFreeSyntaxTests
{
    [Fact]
    public void ConversionAndReemissionNeverSerializeDeclarationOrBlockIdentity()
    {
        const string csharp = """
            using System;
            public class Sample
            {
                public int Value { get; set; }
                public int this[int index] { get => index; }
                public Sample() { }
                public int Run(int x)
                {
                    Func<int, int> f = y => y + 1;
                    for (int i = 0; i < 2; i = i + 1) { x = f(x); }
                    while (x < 0) { x = x + 1; }
                    return x;
                }
            }
            """;
        var conversion = new Migration.CSharpToCalorConverter().Convert(csharp);
        Assert.False(conversion.HasErrors, string.Join("\n", conversion.Issues) + "\n" + conversion.CalorSource);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(conversion.CalorSource!, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var emitted = new Migration.CalorEmitter().Emit(module);
        module.ToolingId = Ids.CompactIdGenerator.Generate(Ids.IdKind.Module);
        foreach (var cls in module.Classes)
        {
            cls.ToolingId = Ids.CompactIdGenerator.Generate(Ids.IdKind.Class);
            foreach (var method in cls.Methods)
                method.ToolingId = Ids.CompactIdGenerator.Generate(Ids.IdKind.Method);
        }
        Assert.Equal(emitted, new Migration.CalorEmitter().Emit(module));
        var roundTripDiagnostics = new DiagnosticBag();
        new Parser(new Lexer(emitted, roundTripDiagnostics).TokenizeAllForParser(), roundTripDiagnostics).Parse();
        Assert.False(roundTripDiagnostics.HasErrors, string.Join("\n", roundTripDiagnostics));
        Assert.Contains("§CTOR{pub}", emitted);
        Assert.Contains("§PROP{Value:", emitted);
        Assert.Contains("§LAM{y:", emitted);
        Assert.DoesNotContain("§/LAM{", emitted);
    }

    [Fact]
    public void SidecarIdentityDoesNotComeFromAnIdentityShapedSemanticName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-source-free-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var name = Ids.CompactIdGenerator.Generate(Ids.IdKind.Function);
            var source = $"§M{{m001}}\n  §F{{{name}:pub}} () -> void\n    §E{{}}\n";
            var file = Path.Combine(directory, "active.calr");
            File.WriteAllText(file, source);
            var store = new Ids.DeclarationIdentityStore(directory);
            var state = store.Sync([file]);
            var identity = Assert.Single(state.Declarations.Where(d => d.Kind == Ids.IdKind.Function));
            Assert.Equal(name, identity.Name);
            Assert.NotEqual(name, identity.Id);
            var diagnostics = new DiagnosticBag();
            var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            store.Attach(file, source, module);
            Assert.Equal(identity.Id, Assert.Single(module.Functions).Id);
            Assert.Equal(source, File.ReadAllText(file));
            Assert.DoesNotContain(identity.Id, new Migration.CalorEmitter().Emit(module));
            Assert.False(diagnostics.HasErrors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IdentityShapedNamesHaveOnlyCurrentSemanticMeaning()
    {
        const string source = """
            §M{m001}
              §CL{c001:Value:pub}
              §F{f001:pub} () -> void
                §B{handler} §LAM{lam1:x:i32} lam1 §/LAM
              §IFACE{i001:IValue}
            """;
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal("m001", module.Name);
        Assert.NotEqual(module.Name, module.Id);
        var cls = Assert.Single(module.Classes);
        Assert.Equal("c001", cls.Name);
        Assert.Equal("Value", cls.BaseClass);
        Assert.NotEqual(cls.Name, cls.Id);
        Assert.Equal("i001", Assert.Single(module.Interfaces).Name);
        var function = Assert.Single(module.Functions);
        Assert.Equal("f001", function.Name);
        Assert.NotEqual(function.Name, function.Id);
        var binding = Assert.IsType<Ast.BindStatementNode>(Assert.Single(function.Body));
        var lambda = Assert.IsType<Ast.LambdaExpressionNode>(binding.Initializer);
        Assert.Collection(lambda.Parameters,
            parameter => { Assert.Equal("lam1", parameter.Name); Assert.Equal("x", parameter.TypeName); },
            parameter => { Assert.Equal("i32", parameter.Name); Assert.Null(parameter.TypeName); });
    }

    [Theory]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §E{}")]
    [InlineData("§M{Demo}\n  §AF{Run:pub} () -> Task\n    §E{}")]
    [InlineData("§M{Demo}\n  §NS{Outer:named}\n    §CL{Value:pub}\n      §CTOR{pub} ()\n      §MT{Run:pub} () -> void")]
    [InlineData("§M{Demo}\n  §CL{Value:pub}\n    §PROP{Count:i32:pub:get,set}\n    §IXER{i32:pub:get,set} (i32:index)")]
    [InlineData("§M{Demo}\n  §IFACE{IValue}\n    §MT{Get:pub} () -> i32")]
    [InlineData("§M{Demo}\n  §EN{Color:pub:i32}\n    Red\n    Blue")]
    [InlineData("§M{Demo}\n  §DEL{Handler}\n    §I{i32:value}\n    §O{void}")]
    [InlineData("§M{Demo}\n  §CL{Value:pub}\n    §EVT{Changed:pub:Action}")]
    [InlineData("§M{Demo}\n  §RTYPE{Positive:i32} (> # 0)\n  §ITYPE{Vector:i32:n}")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §PROOF{\"positive\"} (> 1 0)")]
    [InlineData("§M{Demo}\n  §CL{Value:pub}\n    §OP{+:pub} (Value:a, Value:b) -> Value\n      §R a")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §L{i:0:2:1}\n      §IF (> i 0)\n        §P i")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §WH false\n      §BK\n    §DO\n      §BK\n    §/DO false")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §EACH{value:i32} values\n      §P value\n    §EACHKV{key:value} map\n      §P value")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §TR\n      §TH \"error\"\n    §CA{Exception:ex}\n      §P ex")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §SYNC (gate)\n      §P 1\n    §UNSAFE\n      §P 2")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §USE{resource:IDisposable} value\n      §P 1")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> i32\n    §R §W value\n      §K 0\n        §R 1\n      §K _\n        §R 2")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{handler} §LAM{x:i32} (+ x 1) §/LAM")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{handler} §LAM{} 1 §/LAM")]
    [InlineData("§M{m001}\n  §F{f001:pub} () -> void\n    §B{handler} §LAM{x1:i32} x1 §/LAM")]
    [InlineData("§M{m_7k9m2npqrstv}\n  §CL{c001:pub}\n    §MT{mt001:pub} () -> void")]
    [InlineData("§M{Demo}\n  §EN{e001:pub:i32}\n    Value\n  §DEL{d001}\n    §O{void}")]
    [InlineData("§M{Demo}\n  §CL{c001:Base:pub}\n  §IFACE{i001:IBase}")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{items} §ARR{i32} 1 2 §/ARR")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{items} §LIST{i32} 1 2 §/LIST")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{items} §DICT{str:i32} §KV \"a\" 1 §/DICT")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{items} §HSET{str} \"a\" §/HSET")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{items} §ARR2D{i32:1:2} §/ARR2D")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §LIST{items:i32}\n      1\n      2")]
    [InlineData("§M{Demo:overflow=checked}")]
    [InlineData("§M{Demo:overflow=unchecked}")]
    public void CurrentHeadersHaveNoIdentityField(string source)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    [Theory]
    [InlineData("§M{m001:Demo}")]
    [InlineData("§M{Demo}\n  §F{f001:Run:pub} () -> void")]
    [InlineData("§M{Demo}\n  §AF{f001:Run:pub} () -> Task")]
    [InlineData("§M{Demo}\n  §CL{c001:Value:Base:pub}")]
    [InlineData("§M{Demo}\n  §IFACE{i001:IValue:IBase}")]
    [InlineData("§M{Demo}\n  §EN{e001:Color:pub}\n    Red")]
    [InlineData("§M{Demo}\n  §DEL{d001:Handler}\n    §O{void}")]
    [InlineData("§M{Demo}\n  §CL{Value:pub}\n    §MT{mt001:Run:pub} () -> void")]
    [InlineData("§M{Demo}\n  §CL{Value:pub}\n    §PROP{p001:Count:i32:pub:get,set}")]
    [InlineData("§M{Demo}\n  §CL{Value:pub}\n    §CTOR{ctor1:pub} ()")]
    [InlineData("§M{Demo}\n  §CL{Value:pub}\n    §IXER{ix1:i32:pub:get} (i32:index)")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §IF{if1} true\n      §R")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §WH{wh1} false\n      §BRK")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §TR{tr1}\n      §R")]
    [InlineData("§M{Demo}\n  §F{Run:pub} () -> void\n    §B{handler} §LAM{x:i32} x §/LAM{lam1}")]
    public void SourceIdentityHeadersAreRejectedWithReplacement(string source)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnexpectedToken
            && (diagnostic.Message.Contains("Source IDs are removed", StringComparison.Ordinal)
                && diagnostic.Message.Contains(".calor/ids.json", StringComparison.Ordinal)
                || diagnostic.Message.Contains("Invalid header field", StringComparison.Ordinal)
                && diagnostic.Message.Contains("expected", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("§M{Demo:overflow=bogus}", "overflow= bogus", "overflow=checked", 9)]
    [InlineData("§M{Demo}\n  §F{Run:bogus} () -> void", "bogus", "visibility", 10)]
    [InlineData("§M{Demo}\n  §NS{Outer:bogus}", "bogus", "namespace marker", 13)]
    public void InvalidCurrentFieldsAreLocatedAndNeverSuggestDeletingTheName(
        string source, string invalidField, string expected, int column)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Message.Contains("Invalid header field")));
        Assert.Contains(invalidField, diagnostic.Message);
        Assert.Contains(expected, diagnostic.Message);
        Assert.Equal(column, diagnostic.Span.Column);
        Assert.DoesNotContain("Remove the leading ID", diagnostic.Message);
    }
}
