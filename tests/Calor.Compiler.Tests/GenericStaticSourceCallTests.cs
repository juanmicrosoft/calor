using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Tests.InteropScope;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Surface = Calor.Compiler.Tests.InteropScope.RefOutImplementationTests.Surface;

namespace Calor.Compiler.Tests;

public sealed class GenericStaticSourceCallTests
{
    private const string Original = "public static class Cache<T> { static Cache() { Probe.log += \"C\"; } public static int Use(int n) => n; } public class Item { public int N() { Probe.log += \"N\"; return 7; } } public static class Probe { public static string log = \"\"; static Item F() { log += \"F\"; return new Item(); } public static string Run() { int n = Cache<int>.Use(F().N()); return log + n; } }";

    [Theory]
    [InlineData(false, ContractMode.Debug)]
    [InlineData(true, ContractMode.Debug)]
    [InlineData(false, ContractMode.Off)]
    [InlineData(true, ContractMode.Off)]
    public async Task OriginalPublicOutputs_AreStrictlyNativeBeforeExecution(bool typed, ContractMode mode)
    {
        foreach (var surface in Enum.GetValues<Surface>())
        {
            var (native, losses) = await RefOutImplementationTests.ConvertAsync(Original, surface);
            Assert.Empty(losses);
            RawFrameAssertions.AssertFramed(native, TokenKind.RawCSharp, present: false);
            var compiled = Compile(native, typed, mode);
            Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics) + "\n" + native);
            Assert.Equal(DeclaredMetadata(Original), DeclaredMetadata(compiled.GeneratedCode));
            Assert.Equal("FNC7", RefOutImplementationTests.Run(Original));
            Assert.Equal("FNC7", RefOutImplementationTests.Run(compiled.GeneratedCode));
        }
    }

    [Fact]
    public void ConstructedSourceCall_RetainsSelectedSignature()
    {
        const string source = """
            module GenericStatic
              module Cache<T>
                func Use (T:value) → T
                  !
                  return value
                func Use (object:value) → int
                  !
                  return -1
              func Run () → int
                !
                return Cache<int>.Use(7)
            """;
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        var bound = new Binder(diagnostics) { ResolveSourceCallConversions = true }.Bind(module);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.NotNull(call.SelectedSourceMethod);
        Assert.Equal("int", call.SelectedSourceMethod.ContainingType.TypeArguments.Single().ToDisplayString());
        Assert.Equal("int", call.SelectedSourceMethod.Parameters.Single().Type.ToDisplayString());
        Assert.Equal("int", call.SelectedSourceMethod.ReturnType.ToDisplayString());
        Assert.Equal("T", call.ResolvedSymbol!.Parameters.Single().TypeName);
        Assert.NotNull(call.ResolvedParameterTypes);
        Assert.Equal("System.Int32", Assert.Single(call.ResolvedParameterTypes));
        Assert.Same(module.Classes.Single(), call.SourceStaticMethodOwner);
    }

    [Theory]
    [InlineData("Cache<int>")]
    [InlineData("Cases.Cache<int>")]
    [InlineData("Selected")]
    public void NamespaceAndConstructedAlias_SelectTheActualSourceOwner(string receiver)
    {
        var authored = $$"""
            module GenericStatic Cases
              using Selected = global::Cases.Cache<int>
              module Cache<T>
                func Use (T:value) → T
                  !
                  return value
              module Probe
                func Run () → int
                  !
                  return {{receiver}}.Use(7)
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var compiled = Compile(source, typed);
            Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics));
            Assert.Equal("7", RefOutImplementationTests.Run(compiled.GeneratedCode));
        }
    }

    [Theory]
    [InlineData("callee")]
    [InlineData("constructor")]
    [InlineData("field")]
    [InlineData("overload")]
    public void SelectedCalleeAndTypeInitialization_CannotEscapeAPureCaller(string effectSite)
    {
        var initialization = effectSite switch
        {
            "constructor" => "    ctor stat ()\n      set Probe . log \"C\"\n",
            "field" => """
                    field int:seed static = Initialize()
                    func Initialize private () → int
                      set Probe . log "I"
                      return 1
                """ + "\n",
            _ => ""
        };
        var authored = $$"""
            module GenericStatic
              module Cache<T>
            {{initialization}}    func Use (int:value) → int
                  {{(effectSite == "callee" ? "! mut\n      set Probe . log \"M\""
                      // 0.30 (gap A): a static method of a type with a static constructor may
                      // run that constructor, so Use's own row carries its effects too.
                      : effectSite == "constructor" ? "! mut" : "!")}}
                  return value
                {{(effectSite == "overload" ? "func Use (string:value) → int\n      ! mut\n      set Probe . log value\n      return 7" : "")}}
              module Probe
                field string:log static = ""
                func Run () → int
                  !
                  return Cache<int>.Use({{(effectSite == "overload" ? "\"M\"" : "7")}})
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        foreach (var mode in new[] { ContractMode.Debug, ContractMode.Off })
        {
            var rejected = Compile(source, typed, mode);
            Assert.Contains(rejected.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'Run'", StringComparison.Ordinal)
                && error.Message.Contains("'mut'", StringComparison.Ordinal));
            Assert.DoesNotContain(rejected.Diagnostics, error => error.Code == DiagnosticCode.UnknownExternalCall);
            var allowedSource = source.Replace("func Run () → int\n      !", "func Run () → int\n      ! mut",
                StringComparison.Ordinal);
            var allowed = Compile(allowedSource, typed, mode);
            Assert.False(allowed.HasErrors, string.Join("; ", allowed.Diagnostics) + "\n" + allowedSource);
            Assert.Equal("7", RefOutImplementationTests.Run(allowed.GeneratedCode));
        }
    }

    [Theory]
    [InlineData("namespace")]
    [InlineData("alias")]
    [InlineData("shadow")]
    [InlineData("arity")]
    [InlineData("constraint")]
    [InlineData("private")]
    [InlineData("ambiguous")]
    public void UnselectedCompetitors_CannotBorrowTheGenericSourceMethod(string competitor)
    {
        const string baseline = """
            module GenericStatic Cases
              class Left
              class Right
              module Cache<T>
                func Use (int:value) → int
                  !
                  return value
              module Probe
                func Run () → int
                  !
                  return Cache<int>.Use(7)
            """;
        var authored = competitor switch
        {
            "namespace" => baseline.Replace("Cache<int>.Use", "Missing.Cache<int>.Use", StringComparison.Ordinal),
            "alias" => baseline.Replace("  class Left", "  using Selected = Missing.Cache<int>\n  class Left",
                StringComparison.Ordinal).Replace("Cache<int>.Use", "Selected.Use", StringComparison.Ordinal),
            "shadow" => baseline.Replace("  class Left", "  using Selected = Cases.Cache<int>\n  class Left",
                StringComparison.Ordinal).Replace("Run ()", "Run (int:Selected)", StringComparison.Ordinal)
                .Replace("Cache<int>.Use", "Selected.Use", StringComparison.Ordinal),
            "arity" => baseline.Replace("Cache<int>.Use", "Cache<int,int>.Use", StringComparison.Ordinal),
            "constraint" => baseline.Replace("module Cache<T>", "module Cache<T>\n    where T : class", StringComparison.Ordinal),
            "private" => baseline.Replace("func Use (int:value) → int\n      !",
                "func Use private (int:value) → int", StringComparison.Ordinal),
            "ambiguous" => baseline.Replace("int:value", "Left:value", StringComparison.Ordinal)
                .Replace("  module Probe",
                    "    func Use (Right:value) → int\n      !\n      return 2\n  module Probe", StringComparison.Ordinal)
                .Replace("Cache<int>.Use(7)", "Cache<int>.Use(null)", StringComparison.Ordinal)
                .Replace("return value", "return 1", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(competitor))
        };
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var bindings = CallGraphAnalysis.ResolveOperatorCalls(Parse(source));
            Assert.DoesNotContain(bindings.SelectedCalls.Values, selected => selected.Method?.Name == "Use");
            var rejected = Compile(source, typed);
            Assert.True(rejected.HasErrors, rejected.GeneratedCode);
            Assert.Contains(rejected.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'unknown'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("string", DiagnosticCode.NullableArgumentToNonNullableParameter)]
    [InlineData("string?", DiagnosticCode.NullableReturnFromNonNullable)]
    public void ConstructedAnnotations_RetainExistingNullableReceivingChecks(string argument, string code)
    {
        var authored = $$"""
            module GenericStatic
              module Cache<T>
                func Use (T:value) → T
                  !
                  return value
              func Run () → string
                !
                return Cache<{{argument}}>.Use(null)
            """;
        foreach (var typed in new[] { false, true })
        {
            var rejected = Compile(authored, typed);
            Assert.Contains(rejected.Diagnostics.Errors, error => error.Code == code);
        }
    }

    [Fact]
    public void SelectedMethod_WithUnprovedStaticInitializationRemainsUnknown()
    {
        const string authored = """
            module GenericStatic
              module Cache<T>
                field int:seed static = System.Environment.TickCount
                func Use (int:value) → int
                  !
                  return value
              func Run () → int
                !
                return Cache<int>.Use(7)
            """;
        foreach (var source in new[] { authored, new CalorEmitter().Emit(Parse(authored)) })
        foreach (var typed in new[] { false, true })
        {
            var bound = new Binder(new DiagnosticBag()) { ResolveSourceCallConversions = true }.Bind(Parse(source));
            Assert.NotNull(Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound)
                .OfType<BoundCallExpression>()).SelectedSourceMethod);
            var rejected = Compile(source, typed);
            Assert.Contains(rejected.Diagnostics.Errors, error => error.Code == DiagnosticCode.ForbiddenEffect
                && error.Message.Contains("'Run'", StringComparison.Ordinal)
                && error.Message.Contains("'unknown'", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicNativeOutput_PreservesTypeInitializationExceptions(bool typed)
    {
        var source = Original.Replace("Probe.log += \"C\";",
            "int zero = 0; Probe.log += 1 / zero;", StringComparison.Ordinal);
        var converted = new CSharpToCalorConverter().Convert(source);
        Assert.Empty(converted.Losses);
        var compiled = Compile(converted.CalorSource!, typed);
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics));
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
        Assert.Equal("!TypeInitializationException", RefOutImplementationTests.Run(source));
        Assert.Equal("!TypeInitializationException", RefOutImplementationTests.Run(compiled.GeneratedCode));
    }

    private static CompilationResult Compile(string source, bool typed, ContractMode mode = ContractMode.Debug) =>
        Program.Compile(source, null, new CompilationOptions
        {
            EnableTypeChecking = typed, StrictEffects = true, ContractMode = mode, StatusWriter = TextWriter.Null
        });

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics));
        return module;
    }

    private static string[] DeclaredMetadata(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create("GenericStaticMetadata", [tree],
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        return CSharpToCalorConverter.DeclaredTypeMetadata(compilation.GetSemanticModel(tree),
            tree.GetCompilationUnitRoot());
    }
}
