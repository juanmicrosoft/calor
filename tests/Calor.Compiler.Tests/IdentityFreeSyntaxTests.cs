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
        Assert.Contains("ctor ()", emitted);
        Assert.Contains("property int:Value", emitted);
        Assert.Contains("y) =>", emitted);
        Assert.DoesNotContain('§', emitted);
    }

    [Fact]
    public void SidecarIdentityDoesNotComeFromAnIdentityShapedSemanticName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "calor-source-free-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var name = Ids.CompactIdGenerator.Generate(Ids.IdKind.Function);
            var source = $"module m001\n  func {name} () → void\n    !\n    return";
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
            module m001
              class c001 extends Value
              func f001 () → void
                let handler = (x:lam1, @i32) => lam1
              interface i001 extends IValue
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
    [InlineData("module Demo\n  func Run () → void\n    !")]
    [InlineData("module Demo\n  func Run async () → Task\n    !")]
    [InlineData("module Demo Outer block\n  class Value\n    ctor ()\n    func Run () → void")]
    [InlineData("module Demo\n  class Value\n    property int:Count\n      get\n      set\n    this[int:index] → int\n      get\n      set")]
    [InlineData("module Demo\n  interface IValue\n    func Get () → int")]
    [InlineData("module Demo\n  enum Color int\n    Red\n    Blue")]
    [InlineData("module Demo\n  delegate Handler (int:value) → void")]
    [InlineData("module Demo\n  class Value\n    event Action:Changed")]
    [InlineData("§M{Demo}\n  §RTYPE{Positive:i32} (> # 0)\n  §ITYPE{Vector:i32:n}")]
    [InlineData("module Demo\n  func Run () → void\n    proof{\"positive\"} 1 > 0")]
    [InlineData("module Demo\n  class Value\n    op + (Value:a, Value:b) → Value\n      return a")]
    [InlineData("module Demo\n  func Run () → void\n    for i from 0 to 2 step 1\n      if i > 0\n        Console.WriteLine(i)")]
    [InlineData("module Demo\n  func Run () → void\n    while false\n      break\n    do\n      break\n    while false")]
    [InlineData("module Demo\n  func Run () → void\n    each int:value in values\n      Console.WriteLine(value)\n    each key, value in map\n      Console.WriteLine(value)")]
    [InlineData("module Demo\n  func Run () → void\n    try\n      throw \"error\"\n    catch Exception:ex\n      Console.WriteLine(ex)")]
    [InlineData("module Demo\n  func Run () → void\n    lock gate\n      Console.WriteLine(1)\n    unsafe\n      Console.WriteLine(2)")]
    [InlineData("module Demo\n  func Run () → void\n    using IDisposable:resource = value\n      Console.WriteLine(1)")]
    [InlineData("module Demo\n  func Run () → int\n    return match value\n      case 0\n        return 1\n      case _\n        return 2")]
    [InlineData("module Demo\n  func Run () → void\n    let handler = (int:x) => x + 1")]
    [InlineData("module Demo\n  func Run () → void\n    let handler = () => 1")]
    [InlineData("module m001\n  func f001 () → void\n    let handler = (int:x1) => x1")]
    [InlineData("module m_7k9m2npqrstv\n  class c001\n    func mt001 () → void")]
    [InlineData("module Demo\n  enum e001 int\n    Value\n  delegate d001 () → void")]
    [InlineData("module Demo\n  class c001 extends Base\n  interface i001 extends IBase")]
    [InlineData("module Demo\n  func Run () → void\n    let items = new int[] {1, 2}")]
    [InlineData("module Demo\n  func Run () → void\n    let items = new System.Collections.Generic.List<int> {1, 2}")]
    [InlineData("module Demo\n  func Run () → void\n    let items = new System.Collections.Generic.Dictionary<string,int> {{\"a\", 1}}")]
    [InlineData("module Demo\n  func Run () → void\n    let items = new System.Collections.Generic.HashSet<string> {\"a\"}")]
    [InlineData("module Demo\n  func Run () → void\n    let items = new int[1, 2]")]
    [InlineData("module Demo\n  func Run () → void\n    let System.Collections.Generic.List<int>:items = new System.Collections.Generic.List<int> {1, 2}")]
    [InlineData("module Demo overflow checked")]
    [InlineData("module Demo overflow unchecked")]
    public void CurrentHeadersHaveNoIdentityField(string source)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.ToString())));
    }

    [Theory]
    [InlineData("§M{m001:Demo}")]
    [InlineData("module Demo\n  §F{f001:Run:pub} () -> void")]
    [InlineData("module Demo\n  §AF{f001:Run:pub} () -> Task")]
    [InlineData("module Demo\n  §CL{c001:Value:Base:pub}")]
    [InlineData("module Demo\n  §IFACE{i001:IValue:IBase}")]
    [InlineData("module Demo\n  §EN{e001:Color:pub}\n    Red")]
    [InlineData("module Demo\n  §DEL{d001:Handler}\n    §O{void}")]
    [InlineData("module Demo\n  class Value\n    §MT{mt001:Run:pub} () -> void")]
    [InlineData("module Demo\n  class Value\n    §PROP{p001:Count:i32:pub:get,set}")]
    [InlineData("module Demo\n  class Value\n    §CTOR{ctor1:pub} ()")]
    [InlineData("module Demo\n  class Value\n    §IXER{ix1:i32:pub:get} (i32:index)")]
    [InlineData("module Demo\n  func Run () → void\n    §IF{if1} true\n      return")]
    [InlineData("module Demo\n  func Run () → void\n    §WH{wh1} false\n      break")]
    [InlineData("module Demo\n  func Run () → void\n    §TR{tr1}\n      return")]
    [InlineData("module Demo\n  func Run () → void\n    let handler = (int:x) => x §/LAM{lam1}")]
    public void SourceIdentityHeadersAreRejectedWithReplacement(string source)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var marker = source[source.IndexOf('§')..].Split(['{', ' ', '\r', '\n'])[0];
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownSectionMarker
            && diagnostic.Message.Contains($"'{marker}' is removed.", StringComparison.Ordinal)
            && (diagnostic.Message.Contains("Use ", StringComparison.Ordinal)
                || diagnostic.Message.Contains("dedenting", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("module Demo overflow bogus", "bogus", "'checked' or 'unchecked'", 22)]
    [InlineData("module Demo\n  func Run bogus () → void", "bogus", "function modifier", 12)]
    [InlineData("module Demo Outer bogus", "bogus", "[block]", 19)]
    public void InvalidCurrentFieldsAreLocatedAndNeverSuggestDeletingTheName(
        string source, string invalidField, string expected, int column)
    {
        var diagnostics = new DiagnosticBag();
        new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var diagnostic = Assert.Single(diagnostics.Errors.Where(d => d.Message.Contains(expected)));
        Assert.Contains(invalidField, diagnostic.Message);
        Assert.Contains(expected, diagnostic.Message);
        Assert.Equal(column, diagnostic.Span.Column);
        Assert.DoesNotContain("Remove the leading ID", diagnostic.Message);
    }
}
