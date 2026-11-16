using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class IdentityFreeSyntaxTests
{
    [Theory]
    [InlineData("public static object Run() { string? text = default; return text == null ? 7 : 0; }", null, 7)]
    [InlineData("public static object Run() { string? text = null; return text!.Length; }", "null-forgiving-expression", "throws:NullReferenceException")]
    [InlineData("public static object Run() { int value; value = 9; return value; }", null, 9)]
    [InlineData("public static object Run() { System.Text.StringBuilder value; value = new System.Text.StringBuilder(); return value.Length; }", "default-expression", 0)]
    [InlineData("public enum Level { Low, High } public static object Run() { Level value; value = Level.High; return (int)value; }", "default-expression", 1)]
    [InlineData("public static object Run() { var values = new System.Collections.Generic.Dictionary<int,int> { [1] = 2, [1] = 3 }; return values[1]; }", "dictionary-initializer", 3)]
    [InlineData("public static bool Enabled { get; } = !System.AppContext.TryGetSwitch(\"Calor.Shape.Absent\", out var enabled) || enabled; public static object Run() => Enabled ? 8 : 0;", "conditional-expression-hoisting", 8)]
    [InlineData("public static object Run() { int[] values = {1, 2}; var query = from value in values let next = value + 1 select next; return System.Linq.Enumerable.Sum(query); }", "linq-query", 5)]
    public void CorpusOutputShapesPreserveOriginalClrObservations(
        string members, string? feature, object expected)
    {
        var source = "using System.Linq;\npublic class Probe { " + members + " }";
        var result = ConvertCorpusShape(source);
        var module = ParseCorpusShape(result.CalorSource!);
        if (feature is null)
            Assert.Empty(result.Losses);
        else
        {
            Assert.Contains(result.Losses, loss =>
                loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == feature);
            Assert.True(result.InteropPreservationCount > 0);
            var raw = Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(module)
                .SelectMany(CorpusInteropBlocks).ToArray();
            Assert.NotEmpty(raw);
            Assert.All(raw, block => Assert.Contains(block.CSharpCode.Trim(), source));
        }
        Assert.Equal(expected, RunCorpusShape(source));
        Assert.Equal(expected, RunCorpusShape(new CSharpEmitter().Emit(module)));
        var emitted = new CalorEmitter().Emit(module);
        var reparsed = ParseCorpusShape(emitted);
        Assert.Equal(expected, RunCorpusShape(new CSharpEmitter().Emit(reparsed)));
        Assert.DoesNotContain("§CS", emitted);
        Assert.DoesNotContain("§LIST", emitted);
    }

    [Fact]
    public void ExplicitGenericInterfaceImplementationPreservesDispatchAndExactMember()
    {
        const string member = "int IValue<int>.Get(int value) => value + 1;";
        const string source = """
            public interface IValue<T> { int Get(T value); }
            public class Probe : IValue<int>
            {
                int IValue<int>.Get(int value) => value + 1;
                public static object Run() => ((IValue<int>)new Probe()).Get(8);
            }
            """;
        var conversion = ConvertCorpusShape(source);
        Assert.Contains(conversion.Losses, loss =>
            loss.Kind == ConversionLossKind.InteropPreserved && loss.Feature == "explicit-interface");
        var module = ParseCorpusShape(conversion.CalorSource!);
        var interop = Assert.Single(Assert.Single(module.Classes).InteropBlocks);
        Assert.Contains(member, interop.CSharpCode);
        Assert.Equal(9, RunCorpusShape(source));
        Assert.Equal(9, RunCorpusShape(new CSharpEmitter().Emit(module)));
        var reparsed = ParseCorpusShape(new CalorEmitter().Emit(module));
        Assert.Equal(9, RunCorpusShape(new CSharpEmitter().Emit(reparsed)));
    }

    [Fact]
    public void GenericMethodAttributesPrecedeTypeParametersAndRetainReflectionMetadata()
    {
        const string source = """
            using System;
            public interface IValue
            {
                [Obsolete("interface")] T Get<T>(T value);
            }
            public class Probe
            {
                [Obsolete("implementation")] public static T Get<T>(T value) => value;
                public static object Run() => typeof(Probe).GetMethod("Get").GetCustomAttributes(false).Length;
            }
            """;
        var result = ConvertCorpusShape(source);
        Assert.Empty(result.Losses);
        Assert.Contains("Get[@Obsolete(\"interface\")]<T>", result.CalorSource);
        Assert.Contains("Get[@Obsolete(\"implementation\")]<T>", result.CalorSource);
        var module = ParseCorpusShape(result.CalorSource!);
        Assert.Single(Assert.Single(module.Interfaces).Methods.Single().CSharpAttributes);
        Assert.Single(Assert.Single(module.Classes).Methods.Single(method => method.Name == "Get").CSharpAttributes);
        Assert.Equal(1, RunCorpusShape(source));
        Assert.Equal(1, RunCorpusShape(new CSharpEmitter().Emit(module)));
        var reparsed = ParseCorpusShape(new CalorEmitter().Emit(module));
        Assert.Equal(1, RunCorpusShape(new CSharpEmitter().Emit(reparsed)));
    }

    [Fact]
    public void ConsecutiveMultilineLambdaArgumentsRetainScopeOrderAndDelayedExecution()
    {
        const string source = """
            using System;
            public class Probe
            {
                private static int value;
                public Probe(Action<int> first, Action<int> second, Action<int> third)
                {
                    if (value != 0) throw new Exception("eager body");
                    second(2); first(1); third(3);
                }
                public static object Run()
                {
                    var probe = new Probe(x => { value = value * 10 + x; },
                        y => { value = value * 10 + y; },
                        z => { value = value * 10 + z; });
                    return value;
                }
            }
            """;
        var conversion = ConvertCorpusShape(source);
        Assert.Empty(conversion.Losses);
        var module = ParseCorpusShape(conversion.CalorSource!);
        var run = Assert.Single(module.Classes).Methods.Single(method => method.Name == "Run");
        var call = Assert.IsType<NewExpressionNode>(run.Body.OfType<BindStatementNode>()
            .Single(binding => binding.Name == "probe").Initializer);
        Assert.Equal(3, call.Arguments.Count);
        var lambdas = call.Arguments.Select(argument =>
        {
            var reference = Assert.IsType<ReferenceNode>(argument);
            return Assert.IsType<LambdaExpressionNode>(run.Body.OfType<BindStatementNode>()
                .Single(binding => binding.Name == reference.Name).Initializer);
        }).ToArray();
        Assert.All(lambdas, lambda => Assert.Single(lambda.StatementBody!));
        var inlineCreation = new NewExpressionNode(call.Span, call.TypeName, call.TypeArguments, lambdas);
        var inline = inlineCreation.Accept(new CalorEmitter());
        var inlineModule = ParseCorpusShape("module Inline\n  func Build ()\n    let value = "
            + inline.Replace("\n", "\n    ", StringComparison.Ordinal));
        var inlineCall = Assert.IsType<NewExpressionNode>(
            Assert.IsType<BindStatementNode>(Assert.Single(inlineModule.Functions).Body[0]).Initializer);
        Assert.Equal(3, inlineCall.Arguments.Count);
        Assert.All(inlineCall.Arguments, argument =>
            Assert.Single(Assert.IsType<LambdaExpressionNode>(argument).StatementBody!));
        Assert.Equal(213, RunCorpusShape(source));
        Assert.Equal(213, RunCorpusShape(new CSharpEmitter().Emit(module)));
        var reparsed = ParseCorpusShape(new CalorEmitter().Emit(module));
        Assert.Equal(213, RunCorpusShape(new CSharpEmitter().Emit(reparsed)));
    }

    [Fact]
    public void NativeListInitializerRetainsBindingElementOrderAndOneEvaluation()
    {
        const string source = """
            using System.Collections.Generic;
            public class Probe
            {
                private static int trace;
                private static int Next(int value) { trace = trace * 10 + value; return value; }
                public static object Run()
                {
                    var values = new List<int> { Next(1), Next(2) };
                    return trace * 100 + values[0] * 10 + values[1];
                }
            }
            """;
        var conversion = ConvertCorpusShape(source);
        Assert.Empty(conversion.Losses);
        Assert.DoesNotContain("§LIST", conversion.CalorSource);
        var module = ParseCorpusShape(conversion.CalorSource!);
        var run = Assert.Single(module.Classes).Methods.Single(method => method.Name == "Run");
        var creation = Assert.IsType<NewExpressionNode>(
            Assert.IsType<BindStatementNode>(run.Body[0]).Initializer);
        Assert.Equal(2, creation.CollectionInitializers.Count);
        Assert.Equal(1212, RunCorpusShape(source));
        Assert.Equal(1212, RunCorpusShape(new CSharpEmitter().Emit(module)));
        Assert.Equal(1212, RunCorpusShape(new CSharpEmitter().Emit(
            ParseCorpusShape(new CalorEmitter().Emit(module)))));
    }

    [Fact]
    public void NamelessAndEmptyCatchesRetainFollowingSiblingAndThrowBehavior()
    {
        const string source = """
            using System;
            public class Probe
            {
                public static object Run()
                {
                    int count = 0;
                    try { throw new Exception(); } catch (Exception) { }
                    count = count + 1;
                    try { throw new Exception(); } catch (Exception) { count = count + 2; }
                    return count;
                }
            }
            """;
        var conversion = ConvertCorpusShape(source);
        Assert.Empty(conversion.Losses);
        Assert.Contains("catch Exception\n", conversion.CalorSource!.Replace("\r\n", "\n"));
        Assert.DoesNotContain("catch Exception:", conversion.CalorSource);
        var module = ParseCorpusShape(conversion.CalorSource!);
        Assert.Equal(3, RunCorpusShape(source));
        Assert.Equal(3, RunCorpusShape(new CSharpEmitter().Emit(module)));
        Assert.Equal(3, RunCorpusShape(new CSharpEmitter().Emit(
            ParseCorpusShape(new CalorEmitter().Emit(module)))));
    }

    private static ConversionResult ConvertCorpusShape(string source)
    {
        var conversion = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy,
            PreprocessorMode = PreprocessorConversionMode.SelectActiveBranchLossy,
            ParseOptions = new CSharpParseOptions(LanguageVersion.Preview,
                DocumentationMode.Parse, SourceCodeKind.Regular),
            DefinedSymbols = Array.Empty<string>(),
            ModuleName = "Calor0425Leg",
            GracefulFallback = true,
            AutoGenerateIds = true
        }).Convert(source, "original.cs");
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        return conversion;
    }

    private static IEnumerable<CSharpInteropBlockNode> CorpusInteropBlocks(AstNode node)
    {
        if (node is CSharpInteropBlockNode block)
            yield return block;
        foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
            foreach (var nested in CorpusInteropBlocks(child))
                yield return nested;
    }

    private static ModuleNode ParseCorpusShape(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join("\n", diagnostics));
        return module;
    }

    private static object? RunCorpusShape(string source)
    {
        var compilation = CSharpCompilation.Create("CorpusShape_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, source + "\n" + string.Join("\n", result.Diagnostics));
        var type = Assembly.Load(stream.ToArray()).GetTypes().Single(candidate => candidate.Name == "Probe");
        try
        {
            return type.GetMethod("Run")!.Invoke(null, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            return "throws:" + exception.InnerException.GetType().Name;
        }
    }

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
                !
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
    [InlineData("module Demo Outer block\n  class Value\n    ctor ()\n    func Run () → void\n      !")]
    [InlineData("module Demo\n  class Value\n    property int:Count\n      get\n      set\n    this[int:index] → int\n      get\n      set")]
    [InlineData("module Demo\n  interface IValue\n    func Get () → int\n      !")]
    [InlineData("module Demo\n  enum Color int\n    Red\n    Blue")]
    [InlineData("module Demo\n  delegate Handler (int:value) → void\n    ! unknown")]
    [InlineData("module Demo\n  class Value\n    event Action:Changed")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    proof{\"positive\"} 1 > 0")]
    [InlineData("module Demo\n  class Value\n    op + (Value:a, Value:b) → Value\n      !\n      return a")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    for i from 0 to 2 step 1\n      if i > 0\n        Console.WriteLine(i)")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    while false\n      break\n    do\n      break\n    while false")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    each int:value in values\n      Console.WriteLine(value)\n    each key, value in map\n      Console.WriteLine(value)")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    try\n      throw \"error\"\n    catch Exception:ex\n      Console.WriteLine(ex)")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    lock gate\n      Console.WriteLine(1)\n    unsafe\n      Console.WriteLine(2)")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    using IDisposable:resource = value\n      Console.WriteLine(1)")]
    [InlineData("module Demo\n  func Run () → int\n    !\n    return match value\n      case 0\n        return 1\n      case _\n        return 2")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let handler = (int:x) => x + 1")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let handler = () => 1")]
    [InlineData("module m001\n  func f001 () → void\n    !\n    let handler = (int:x1) => x1")]
    [InlineData("module m_7k9m2npqrstv\n  class c001\n    func mt001 () → void\n      !")]
    [InlineData("module Demo\n  enum e001 int\n    Value\n  delegate d001 () → void\n    ! unknown")]
    [InlineData("module Demo\n  class c001 extends Base\n  interface i001 extends IBase")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new int[] {1, 2}")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new System.Collections.Generic.List<int> {1, 2}")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new System.Collections.Generic.Dictionary<string,int> {{\"a\", 1}}")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new System.Collections.Generic.HashSet<string> {\"a\"}")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let items = new int[1, 2]")]
    [InlineData("module Demo\n  func Run () → void\n    !\n    let System.Collections.Generic.List<int>:items = new System.Collections.Generic.List<int> {1, 2}")]
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

    [Theory]
    [InlineData("proof 1 > 0", null)]
    [InlineData("proof{\"positive\"} 1 > 0", "positive")]
    [InlineData("proof{\"range: \\\"quoted\\\" \\\\ slash\"} 1 > 0", "range: \"quoted\" \\ slash")]
    [InlineData("proof{\"line\\nbreak\"} 1 > 0", "line\nbreak")]
    public void NativeProofEmissionRetainsConditionAndDescription(string statement, string? description)
    {
        var source = $$"""
            module ProofRoundTrip
              func Probe public () → int
                !
                {{statement}}
                return 1
            """;
        var module = ParseCorpusShape(source);
        var originalProof = Assert.IsType<ProofObligationNode>(Assert.Single(module.Functions).Body[0]);
        Assert.Equal(description, originalProof.Description);

        var emitted = new CalorEmitter().Emit(module);

        Assert.DoesNotContain("§PROOF", emitted);
        Assert.Contains(description is null ? "proof " : "proof{", emitted);
        var reparsed = ParseCorpusShape(emitted);
        var proof = Assert.IsType<ProofObligationNode>(Assert.Single(reparsed.Functions).Body[0]);
        Assert.Equal(description, proof.Description);
        Assert.Equal(new CSharpEmitter().Emit(module), new CSharpEmitter().Emit(reparsed));
        Assert.Equal(emitted, new CalorEmitter().Emit(reparsed));
    }
}
