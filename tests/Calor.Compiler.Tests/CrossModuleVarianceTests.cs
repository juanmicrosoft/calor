using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Indexing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CrossModuleVarianceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "calor-variance-" + Guid.NewGuid().ToString("N"));
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public CrossModuleVarianceTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_directory);
    }

    private const string NativeLibrary = """
        module Library Contracts block
          module Arithmetic public
            func Pick public (int:value) → int
              !
              return (value + 1)
            func Pick public (string:value) → int
              ! cw
              System.Console.WriteLine(value)
              return 2
        """;

    [Theory]
    [InlineData("Contracts.Arithmetic", "")]
    [InlineData("Arithmetic", "  using Contracts\n")]
    [InlineData("Chosen", "  using Chosen = Contracts.Arithmetic\n")]
    public void NativeCrossFileCallRetainsExactSourceSymbolAndIndexEdge(string owner, string imports)
    {
        Write("library", NativeLibrary);
        Write("caller", $$"""
            module Caller Consumer block
            {{imports}}  module Entry public
                func Probe public () → int
                  !
                  return {{owner}}.Pick(41)
            """);
        var files = Directory.GetFiles(_directory, "*.calr").Order(StringComparer.Ordinal)
            .Select(path => new FileInfo(path)).ToArray();
        CompilationDriver.BuildCrossModuleFunctionMap(files, out var types);
        var source = File.ReadAllText(Path.Combine(_directory, "caller.calr"));
        var diagnostics = new DiagnosticBag();
        var ast = new Parsing.Parser(new Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var binder = new Binder(diagnostics, Path.Combine(_directory, "caller.calr")) { CrossModuleTypes = types };
        var bound = binder.Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var symbol = Assert.IsType<FunctionSymbol>(call.ResolvedSymbol);
        Assert.Equal("Contracts.Arithmetic.Pick", symbol.Name);
        Assert.Equal("INT", TypeIdentity.Canonicalize(Assert.Single(symbol.Parameters).TypeName));
        Assert.Equal("INT", TypeIdentity.Canonicalize(call.Type.DisplayString));
        Assert.Null(call.ResolvedMetadataMethod);
        var selected = Assert.Single(binder.ExternalSourceCalls).Value;
        Assert.Same(symbol, selected.Symbol);
        Assert.Empty(selected.Declaration.Effects!.Effects);
        Assert.Equal("Contracts.Arithmetic", selected.OwnerSymbol.QualifiedName);
        Assert.Equal(0, Assert.Single(Assert.Single(call.SelectedOverloadMatches).Arguments).ParameterIndex);

        var compiled = Compile(cache: true);
        Assert.False(compiled.Diagnostics.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(2, compiled.Result.Compiled.Count);
        var index = ProjectIndexBuilder.Build(new(_directory, "native",
            ProjectIndexBuilder.DiscoverSources(_directory)));
        var edge = Assert.Single(index.CallEdges);
        var declaration = Assert.Single(index.Declarations, declaration => declaration.SymbolId == edge.CalleeSymbolId);
        Assert.Equal("library.calr", declaration.File);
        Assert.Equal("Contracts.Arithmetic.Pick", declaration.Name);
        Assert.Empty(index.Residual.UnresolvedCalls.Where(call => call.File == "caller.calr"));
        Assert.Empty(index.Residual.EffectRowsUnavailable);
        Assert.All(index.EffectRows.Where(row => row.File == "caller.calr"),
            row => Assert.Equal("fits", row.Verdict));

        var trees = Directory.GetFiles(_directory, "*.g.cs")
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path))).ToArray();
        var compilation = CSharpCompilation.Create("CrossFile_" + Guid.NewGuid().ToString("N"),
            trees, GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        Assert.Equal(42, assembly.GetType("Consumer.Entry")!.GetMethod("Probe")!.Invoke(null, null));
    }

    [Theory]
    [InlineData("\"effectful\"", "cw")]
    [InlineData("System.Console.ReadLine() ?? \"fallback\"", "cr")]
    public void NativeCrossFileCallDoesNotEraseCalleeOrArgumentEffects(string argument, string effect)
    {
        Write("library", NativeLibrary);
        Write("caller", $$"""
            module Caller Consumer block
              module Entry public
                func Probe public () → int
                  !
                  return Contracts.Arithmetic.Pick({{argument}})
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'" + effect + "'", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Theory]
    [InlineData("Contracts.Arithmetic.Pick(true)", "", "")]
    [InlineData("Contracts.Arithmetic.Pick<int>(41)", "", "")]
    [InlineData("Contracts.Arithmetic.Pick(ref value)", "int:value", "")]
    [InlineData("Contracts.Arithmetic.Pick(41)", "", "private")]
    [InlineData("Contracts.Arithmetic.Pick(41)", "", "instance")]
    [InlineData("Contracts.Arithmetic.Pick(41)", "", "duplicate-owner")]
    [InlineData("Contracts.Arithmetic.Pick(41)", "object:Contracts", "")]
    [InlineData("Contracts.Arithmetic.Pick(41)", "", "type-parameter-shadow")]
    [InlineData("Contracts.Arithmetic.Pick(41)", "", "static-initialization")]
    [InlineData("Library.Arithmetic.Pick(41)", "", "")]
    [InlineData("Arithmetic.Pick(41)", "", "ambiguous-import")]
    [InlineData("System.String.Equals(\"x\", \"x\")", "", "source-metadata-shadow")]
    [InlineData("Contracts.Arithmetic.Pick(null)", "", "ambiguous-method")]
    public void NativeCrossFileInvalidCallsNeverAcquireSourceOrMetadataContracts(
        string expression, string parameters, string variant)
    {
        var library = variant switch
        {
            "private" => NativeLibrary.Replace("func Pick public (int:value) → int\n      !\n", "func Pick private (int:value) → int\n"),
            "instance" => NativeLibrary.Replace("module Arithmetic public", "class Arithmetic public"),
            "static-initialization" => NativeLibrary.Replace("module Arithmetic public",
                "module Arithmetic public\n    field string?:Seed public static = System.Console.ReadLine()"),
            "ambiguous-method" => """
                module Library Contracts block
                  module Arithmetic public
                    func Pick public (System.IDisposable:value) → int
                      !
                      return 1
                    func Pick public (System.ICloneable:value) → int
                      !
                      return 2
                """,
            "source-metadata-shadow" => """
                module Mask System block
                  module String public
                    func Equals public (int:value) → int
                      !
                      return value
                """,
            _ => NativeLibrary
        };
        Write("library", library);
        if (variant == "duplicate-owner")
            Write("duplicate", library);
        if (variant == "ambiguous-import")
            Write("other", library.Replace("Contracts", "Other"));
        var imports = variant == "ambiguous-import" ? "  using Contracts\n  using Other\n" : "";
        Write("caller", $$"""
            module Caller Consumer block
            {{imports}}  module Entry public
                func Probe{{(variant == "type-parameter-shadow" ? "<Contracts>" : "")}} public ({{parameters}}) → object
                  !
                  return {{expression}}
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.True(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var symbols = Refactoring.ProjectSymbolIndex.Build(
            Directory.GetFiles(_directory, "*.calr"), out var skipped);
        Assert.Empty(skipped);
        var caller = Assert.Single(symbols.Documents, document => document.FilePath.EndsWith("caller.calr", StringComparison.Ordinal));
        Assert.All(BoundNodeHelpers.DescendantsAndSelf(caller.BoundModule).OfType<BoundCallExpression>(),
            call =>
            {
                Assert.Null(call.ResolvedSymbol);
                Assert.Null(call.ResolvedMetadataMethod);
            });
        Assert.DoesNotContain(symbols.CallEdges, edge => edge.FilePath == caller.FilePath);
        Assert.Contains(symbols.Residual.UnresolvedCalls, call => call.FilePath == caller.FilePath);
    }

    [Theory]
    [InlineData("cw", "cw", null)]
    [InlineData("cw", null, DiagnosticCode.EffectRowUnknown)]
    [InlineData("", "cw", DiagnosticCode.EffectRowMismatch)]
    public void NativeCrossFileCallbackContractsStayAttachedToTheirDeclaration(
        string destination, string? argumentRow, string? expectedCode)
    {
        Write("library", $$"""
            module Library Contracts block
              module Callbacks public
                func Apply public (Func<int>:callback ! {{destination}}) → int
                  ! {{destination}}
                  return callback()
            """);
        Write("caller", $$"""
            module Caller Consumer block
              module Entry public
                func Probe public (Func<int>:callback {{(argumentRow is null ? "! unknown" : "! " + argumentRow)}}) → int
                  ! cw
                  return Contracts.Callbacks.Apply(callback)
            """);
        var (_, diagnostics) = Compile(cache: true);
        if (expectedCode is null)
            Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        else
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == expectedCode);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Theory]
    [InlineData("Echo<int>(41)", "int", "INT", false)]
    [InlineData("Maybe(null)", "string?", "STRING", true)]
    public void NativeCrossFileConstructedSignaturesPreserveGenericsAndNullability(
        string expression, string result, string canonical, bool nullable)
    {
        Write("library", """
            module Library Contracts block
              module Arithmetic public
                func Echo<T> public (T:value) → T
                  !
                  return value
                func Maybe public (string?:value) → string?
                  !
                  return value
            """);
        Write("caller", $$"""
            module Caller Consumer block
              module Entry public
                func Probe public () → {{result}}
                  !
                  return Contracts.Arithmetic.{{expression}}
            """);
        var files = Directory.GetFiles(_directory, "*.calr").Select(path => new FileInfo(path)).ToArray();
        CompilationDriver.BuildCrossModuleFunctionMap(files, out var types);
        var path = Path.Combine(_directory, "caller.calr");
        var diagnostics = new DiagnosticBag();
        var ast = new Parsing.Parser(new Parsing.Lexer(File.ReadAllText(path), diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        var bound = new Binder(diagnostics, path) { CrossModuleTypes = types }.Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        Assert.NotNull(call.ResolvedSymbol);
        Assert.Equal(nullable, call.Type is Binding.BoundTypes.NominalBoundType
            { NullableAnnotation: Binding.BoundTypes.NullableAnnotation.Annotated });
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.SelectedSourceMethod);
        Assert.Equal("Contracts.Arithmetic", selected.ContainingType.ToDisplayString());
        Assert.Contains(canonical, TypeIdentity.Canonicalize(call.Type.DisplayString), StringComparison.Ordinal);
        Assert.Contains(canonical,
            TypeIdentity.Canonicalize(Assert.Single(Assert.Single(call.SelectedOverloadMatches).Arguments).ParameterType),
            StringComparison.Ordinal);
        var compiled = Compile(cache: true);
        Assert.False(compiled.Diagnostics.HasErrors, string.Join("\n", compiled.Diagnostics));
    }

    [Fact]
    public void NativeCrossFileWarmBuildInvalidatesChangedSourceSignaturesAndRows()
    {
        Write("library", NativeLibrary);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe public () → int
                  !
                  return Contracts.Arithmetic.Pick(41)
            """);
        var cold = Compile(cache: true);
        Assert.False(cold.Diagnostics.HasErrors, string.Join("\n", cold.Diagnostics));
        Assert.Equal(2, cold.Result.Compiled.Count);
        var warm = Compile(cache: true);
        Assert.False(warm.Diagnostics.HasErrors, string.Join("\n", warm.Diagnostics));
        Assert.Equal(2, warm.Result.Skipped.Count);
        Write("library", NativeLibrary.Replace(
            "func Pick public (int:value) → int\n      !",
            "func Pick public (int:value) → int\n      ! cw"));
        var changed = Compile(cache: true);
        Assert.Contains(changed.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        Assert.Empty(changed.Result.Skipped);
        Write("library", NativeLibrary.Replace("func Pick public (int:value)", "func Pick public (bool:value)")
            .Replace("return (value + 1)", "return 42"));
        var signature = Compile(cache: true);
        Assert.True(signature.Diagnostics.HasErrors);
        Assert.Empty(signature.Result.Skipped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCrossFileZeroArgumentSelectionSurvivesPartialAndUnrelatedPreprocessorMembers(bool partial)
    {
        Write("library", $$"""
            module Library Contracts block
              module Arithmetic public {{(partial ? "partial" : "")}}
                func Zero public () → int
                  !
                  return 42
                pp FEATURE
                  func Other public () → int
                    !
                    return 1
            """);
        if (partial)
            Write("part", """
                module More Contracts block
                  module Arithmetic public partial
                    func Echo public (int:value) → int
                      !
                      return value
                """);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe public () → int
                  !
                  return Contracts.Arithmetic.Zero()
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var index = ProjectIndexBuilder.Build(new(_directory, "native",
            ProjectIndexBuilder.DiscoverSources(_directory)));
        Assert.Single(index.CallEdges);
        Assert.Empty(index.Residual.UnresolvedCalls);
        Assert.Empty(index.Residual.EffectRowsUnavailable);
    }

    [Fact]
    public void NativeCrossFileConfigurationDependentDeclarationsRemainUnresolved()
    {
        Write("library", """
            module Library Contracts block
              module Arithmetic public
                pp FEATURE
                  func Zero public () → int
                    !
                    return 1
                pp else
                  func Zero public () → int
                    !
                    return 2
            """);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe public () → int
                  !
                  return Contracts.Arithmetic.Zero()
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        var index = Refactoring.ProjectSymbolIndex.Build(
            Directory.GetFiles(_directory, "*.calr"), out var skipped);
        Assert.Empty(skipped);
        Assert.Empty(index.CallEdges);
        Assert.Contains(index.Residual.UnresolvedCalls, call => call.FilePath.EndsWith("caller.calr", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeCrossFileNullableArgumentsUseTheSelectedConstructedParameter()
    {
        Write("library", NativeLibrary);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe public (string?:value) → int
                  ! cw
                  return Contracts.Arithmetic.Pick(value)
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
    }

    [Fact]
    public void NativeCrossFileSourceSnapshotChangesAreExplicitFailures()
    {
        Write("library", NativeLibrary);
        var files = Directory.GetFiles(_directory, "*.calr").Select(path => new FileInfo(path)).ToArray();
        CompilationDriver.BuildCrossModuleFunctionMap(files, out var types);
        Write("library", NativeLibrary.Replace("value + 1", "value + 2"));
        var error = Assert.Throws<IOException>(() => types.EnsureSourceSnapshots());
        Assert.Contains("changed after cross-module declaration collection", error.Message);
    }

    [Fact]
    public void NativeCrossFileCallerTypeParametersKeepTheirLexicalIdentity()
    {
        Write("library", """
            module Library Contracts block
              module Arithmetic public
                func Echo<T> public (T:value) → T
                  !
                  return value
            """);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe<T> public (T:value) → T
                  !
                  return Contracts.Arithmetic.Echo<T>(value)
            """);
        var files = Directory.GetFiles(_directory, "*.calr").Select(path => new FileInfo(path)).ToArray();
        CompilationDriver.BuildCrossModuleFunctionMap(files, out var types);
        var path = Path.Combine(_directory, "caller.calr");
        var diagnostics = new DiagnosticBag();
        var ast = new Parsing.Parser(new Parsing.Lexer(File.ReadAllText(path), diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        var bound = new Binder(diagnostics, path) { CrossModuleTypes = types }.Bind(ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>());
        var selected = Assert.IsAssignableFrom<IMethodSymbol>(call.SelectedSourceMethod);
        var argument = Assert.IsAssignableFrom<ITypeParameterSymbol>(Assert.Single(selected.TypeArguments));
        Assert.Equal("Probe", argument.ContainingSymbol.Name);
        Assert.Equal("Consumer.Entry", argument.ContainingType.ToDisplayString());
        var (_, compiledDiagnostics) = Compile(cache: true);
        Assert.False(compiledDiagnostics.HasErrors, string.Join("\n", compiledDiagnostics));
    }

    [Fact]
    public void NativeCrossFileNominalOverloadsDoNotBorrowAnotherParametersRow()
    {
        Write("library", """
            module Library Contracts block
              class Item public
              module Arithmetic public
                func Pick public (Contracts.Item:value) → int
                  !
                  return 1
                func Pick public (System.Text.StringBuilder:value) → int
                  ! cw
                  return 2
            """);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe public (Contracts.Item:value) → int
                  !
                  return Contracts.Arithmetic.Pick(value)
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        var index = Refactoring.ProjectSymbolIndex.Build(
            Directory.GetFiles(_directory, "*.calr"), out var skipped);
        Assert.Empty(skipped);
        var caller = Assert.Single(index.Documents, document => document.FilePath.EndsWith("caller.calr", StringComparison.Ordinal));
        var call = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(caller.BoundModule).OfType<BoundCallExpression>());
        Assert.Equal("Contracts.Item", Assert.Single(call.ResolvedSymbol!.Parameters).TypeName);
        Assert.Equal("Contracts.Item", Assert.Single(call.SelectedSourceMethod!.Parameters).Type.ToDisplayString());
    }

    [Theory]
    [InlineData(false, "ref")]
    [InlineData(true, "ref")]
    [InlineData(false, "out")]
    [InlineData(true, "out")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public void NativeCrossFileRefusedOuterCallInvalidatesOnlyByRefLocalNarrowing(bool extension, string modifier)
    {
        var parameter = string.IsNullOrEmpty(modifier) ? "string?:text" : $"{modifier} string?:text";
        Write("library", $$"""
            module Library Contracts block
              class Receiver public
            {{(extension ? "  module Extensions public" : "")}}
                func Touch public ({{(extension ? "this Receiver:receiver, " : "")}}{{parameter}}) → void
                  ! mut
                  set text null
            """);
        Write("caller", $$"""
            module Caller Consumer block
              using Contracts
              module Entry public
                func Take public (string:text) → int
                  !
                  return 7
                func Probe public (Contracts.Receiver:receiver, string?:text) → int
                  ! mut
                  var string?:value = text
                  if value != null
                    Entry.Take(value)
                    receiver.Touch({{(string.IsNullOrEmpty(modifier) ? "" : modifier + " ")}}value)
                    return Entry.Take(value)
                  return 0
            """);
        var files = Directory.GetFiles(_directory, "*.calr").Select(path => new FileInfo(path)).ToArray();
        CompilationDriver.BuildCrossModuleFunctionMap(files, out var types);
        var path = Path.Combine(_directory, "caller.calr");
        var diagnostics = new DiagnosticBag();
        var ast = new Parsing.Parser(new Parsing.Lexer(File.ReadAllText(path), diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        var bound = new Binder(diagnostics, path) { CrossModuleTypes = types }.Bind(ast);
        var calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>().ToArray();
        var outer = Assert.Single(calls, call => call.HasOpaqueOverloads);
        var takes = calls.Where(call => call.ResolvedSymbol?.Name == "Consumer.Entry.Take")
            .OrderBy(call => call.Span.Start).ToArray();
        Assert.Equal(2, takes.Length);
        Assert.Null(outer.ResolvedSymbol);
        Assert.Null(outer.ResolvedMetadataMethod);
        Assert.Null(outer.SelectedSourceMethod);
        Assert.True(outer.HasUnknownInferredType);
        Assert.Equal(string.IsNullOrEmpty(modifier) ? null : modifier, outer.ArgumentModifiers?.Single());
        Assert.Same(outer.Receiver, outer.EvaluatedReceiver);

        var compilationDiagnostics = new DiagnosticBag();
        var compiled = CompilationDriver.CompileAll(files,
            _ => new CompilationOptions { EnforceEffects = true, UnknownCallPolicy = UnknownCallPolicy.Permissive },
            crossModuleEnforcement: true, crossModulePolicy: UnknownCallPolicy.Permissive,
            diagnosticSink: compilationDiagnostics);
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            extension, modifier,
            BeforeType = Assert.Single(takes[0].Arguments).Type.DisplayString,
            AfterType = Assert.Single(takes[1].Arguments).Type.DisplayString,
            OuterModifier = outer.ArgumentModifiers,
            CompiledOutputs = compiled.Compiled.Count,
            BinderDiagnostics = diagnostics.Select(diagnostic => diagnostic.ToString()),
            PermissiveDiagnostics = compilationDiagnostics.Select(diagnostic => diagnostic.ToString())
        }));
        var expected = string.IsNullOrEmpty(modifier) ? 0 : 1;
        var nullable = diagnostics.Where(diagnostic => diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter).ToArray();
        Assert.Equal(expected, nullable.Length);
        Assert.Equal(expected, compilationDiagnostics.Count(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        if (expected != 0)
            Assert.Equal(Assert.Single(takes[1].Arguments).Span, Assert.Single(nullable).Span);
        Assert.DoesNotContain(compilationDiagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.All(compilationDiagnostics.Errors, diagnostic =>
            Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidByReferenceOperand);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("cw", false)]
    public void NativeCrossFileOverrideVarianceRetainsTheOriginalBaseDeclaration(string row, bool violation)
    {
        Write("base", $$"""
            module BaseFile Contracts block
              class Base public
                func Render public virtual () → void
                  ! {{row}}
                  return
            """);
        Write("derived", """
            module DerivedFile Consumer block
              class Derived public
                extends Contracts.Base
                func Render public override () → void
                  ! cw
                  return
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.Equal(violation, diagnostics.HasErrors);
        Assert.Equal(violation, diagnostics.Any(diagnostic => diagnostic.Code == DiagnosticCode.OverrideEffectVariance));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void NativeCrossFileLocalCallsKeepTheirBodiesAndAreNotForeignDeclarations()
    {
        Write("library", NativeLibrary);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe public () → int
                  !
                  return Entry.Local(41)
                func Local public (int:value) → int
                  !
                  return Contracts.Arithmetic.Pick(value)
            """);
        var symbols = Refactoring.ProjectSymbolIndex.Build(
            Directory.GetFiles(_directory, "*.calr"), out var skipped);
        Assert.Empty(skipped);
        var caller = Assert.Single(symbols.Documents, document => document.FilePath.EndsWith("caller.calr", StringComparison.Ordinal));
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics, caller.FilePath) { CrossModuleTypes = symbols.SourceTypes };
        binder.Bind(caller.Ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        Assert.Equal("Contracts.Arithmetic.Pick", Assert.Single(binder.ExternalSourceCalls).Value.Symbol.Name);
        var index = ProjectIndexBuilder.Build(new(_directory, "native",
            ProjectIndexBuilder.DiscoverSources(_directory)));
        Assert.Equal(2, index.CallEdges.Count);
        Assert.DoesNotContain(index.Residual.UnresolvedCalls, call => call.File == "caller.calr");
        Assert.Empty(index.Residual.EffectRowsUnavailable);
        Assert.Equal(4, index.EffectRows.Count);
        Assert.All(index.EffectRows, row => Assert.Equal("fits", row.Verdict));
        var (_, compiledDiagnostics) = Compile(cache: true);
        Assert.False(compiledDiagnostics.HasErrors, string.Join("\n", compiledDiagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredRow_CrossFileDeclarationWithoutABodyContractRemainsUnknown(bool declared)
    {
        Write("library", $$"""
            module Library Contracts block
              module Helpers public
                func Write {{(declared ? "public" : "internal")}} () → void
                  {{(declared ? "! cw" : "")}}
                  System.Console.WriteLine("value")
            """);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Probe public () → void
                  ! cw
                  Contracts.Helpers.Write()
            """);
        var (_, diagnostics) = Compile(cache: true);
        Assert.Equal(!declared, diagnostics.HasErrors);
        if (!declared)
            Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false, "cw")]
    [InlineData(true, "cw")]
    [InlineData(false, "mut")]
    [InlineData(true, "mut")]
    public void NativeCrossFileRefusedCallsRetainKnownReceiverEffectsAndEdges(bool extension, string effect)
    {
        Write("library", extension ? """
            module Library Contracts block
              class Receiver public
              module Extensions public
                func Touch public (this Receiver:receiver, int:value) → int
                  !
                  return value
            """ : """
            module Library Contracts block
              class Receiver public
                func Touch public (int:value) → int
                  !
                  return value
            """);
        Write("caller", $$"""
            module Caller Consumer block
              using Contracts
              module Entry public
                func Make public (Contracts.Receiver:receiver, int[]:visits) → Contracts.Receiver
                  ! {{effect}}
                  {{(effect == "cw" ? "System.Console.WriteLine(1)" : "set visits[0] (visits[0] + 1)")}}
                  return receiver
                func Argument public () → int
                  !
                  return 7
                func Probe public (Contracts.Receiver:receiver, int[]:visits) → int
                  !
                  return Entry.Make(receiver, visits).Touch(Entry.Argument())
            """);
        var symbols = Refactoring.ProjectSymbolIndex.Build(
            Directory.GetFiles(_directory, "*.calr"), out var skipped);
        Assert.Empty(skipped);
        var caller = Assert.Single(symbols.Documents, document => document.FilePath.EndsWith("caller.calr", StringComparison.Ordinal));
        Assert.False(caller.BindHadErrors);
        var probe = Assert.Single(caller.BoundModule.Functions, function => function.Symbol.Name.EndsWith(".Probe", StringComparison.Ordinal));
        var outer = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(Assert.Single(probe.Body)).Expression);
        var calls = BoundNodeHelpers.DescendantsAndSelf(probe).OfType<BoundCallExpression>().ToArray();
        var diagnostics = new DiagnosticBag();
        var pass = new EffectEnforcementPass(diagnostics, UnknownCallPolicy.Permissive)
        {
            CrossModuleTypes = symbols.SourceTypes,
            SourcePath = caller.FilePath
        };
        pass.Enforce(caller.Ast);
        var fact = Assert.Single(pass.DeclarationFacts.Values, fact => fact.Name == "Probe");
        var targets = symbols.CallEdges.Where(edge => edge.CallerSymbolId == probe.SymbolId)
            .Select(edge => caller.BoundModule.SymbolsById[edge.CalleeSymbolId].Name).ToArray();
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            extension, effect,
            BoundCalls = calls.Select(call => new { call.Target, Symbol = call.ResolvedSymbol?.Name, call.Span.Start }),
            ReceiverRetained = outer.EvaluatedReceiver is not null,
            Targets = targets,
            Inferred = fact.InferredRow.ToString(),
            fact.Forbidden,
            Diagnostics = diagnostics.Select(diagnostic => diagnostic.ToString())
        }));
        Assert.Null(outer.ResolvedSymbol);
        Assert.Null(outer.ResolvedMetadataMethod);
        Assert.Null(outer.SelectedSourceMethod);
        Assert.True(outer.HasOpaqueOverloads);
        Assert.True(outer.HasUnknownInferredType);
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.ForbiddenEffect
            && diagnostic.Message.Contains("'" + effect + "'", StringComparison.Ordinal));
        Assert.Equal(new[] { effect }, fact.Forbidden);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        var receiverCall = Assert.IsType<BoundCallExpression>(outer.EvaluatedReceiver);
        Assert.Same(receiverCall, outer.Receiver);
        Assert.Equal("Consumer.Entry.Make", receiverCall.ResolvedSymbol!.Name);
        Assert.Equal(new[] { receiverCall, Assert.Single(outer.Arguments) }, outer.Children);
        Assert.Equal(3, calls.Length);
        Assert.Single(calls, call => call.ResolvedSymbol?.Name == "Consumer.Entry.Make");
        Assert.Single(calls, call => call.ResolvedSymbol?.Name == "Consumer.Entry.Argument");
        Assert.Equal(new[] { "Consumer.Entry.Argument", "Consumer.Entry.Make" }, targets.Order(StringComparer.Ordinal));
        var index = ProjectIndexBuilder.Build(new(_directory, "native",
            ProjectIndexBuilder.DiscoverSources(_directory)));
        Assert.Equal(targets.Order(StringComparer.Ordinal), index.CallEdges
            .Select(edge => Assert.Single(index.Declarations, declaration => declaration.SymbolId == edge.CalleeSymbolId).Name)
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NativeCrossFileRefusedReceiverMutationPrecedesArgumentBinding()
    {
        Write("library", """
            module Library Contracts block
              class Receiver public
                func Touch public (int:value) → int
                  !
                  return value
            """);
        Write("caller", """
            module Caller Consumer block
              module Entry public
                func Make public (Contracts.Receiver:receiver, ref string?:text) → Contracts.Receiver
                  ! mut
                  set text null
                  return receiver
                func Argument public (string:text) → int
                  !
                  return 7
                func Probe public (Contracts.Receiver:receiver, string?:text) → int
                  ! mut
                  var string?:value = text
                  if value != null
                    return Entry.Make(receiver, ref value).Touch(Entry.Argument(value))
                  return 0
            """);
        var files = Directory.GetFiles(_directory, "*.calr").Select(path => new FileInfo(path)).ToArray();
        CompilationDriver.BuildCrossModuleFunctionMap(files, out var types);
        var path = Path.Combine(_directory, "caller.calr");
        var diagnostics = new DiagnosticBag();
        var ast = new Parsing.Parser(new Parsing.Lexer(File.ReadAllText(path), diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        var bound = new Binder(diagnostics, path) { CrossModuleTypes = types }.Bind(ast);
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            Diagnostics = diagnostics.Select(diagnostic => diagnostic.ToString()),
            Calls = BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>()
                .Select(call => new { call.Target, Symbol = call.ResolvedSymbol?.Name, call.Span.Start })
        }));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
        var make = Assert.Single(BoundNodeHelpers.DescendantsAndSelf(bound).OfType<BoundCallExpression>(),
            call => call.ResolvedSymbol?.Name == "Consumer.Entry.Make");
        Assert.Equal("ref", make.ArgumentModifiers![1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCrossFileRefusedReceiverEvaluatesOnceBeforeArguments(bool extension)
    {
        Write("library", extension ? """
            module Library Contracts block
              class Receiver public
              module Extensions public
                func Touch public (this Receiver:receiver, int:value) → int
                  !
                  return value
            """ : """
            module Library Contracts block
              class Receiver public
                func Touch public (int:value) → int
                  !
                  return value
            """);
        Write("caller", """
            module Caller Consumer block
              using Contracts
              module Entry public
                func Make public (Contracts.Receiver:receiver, int[]:visits) → Contracts.Receiver
                  ! mut
                  set visits[0] (visits[0] * 10 + 1)
                  return receiver
                func Argument public (int[]:visits) → int
                  ! mut
                  set visits[0] (visits[0] * 10 + 2)
                  return 7
                func Probe public (Contracts.Receiver:receiver, int[]:visits) → int
                  ! mut
                  return Entry.Make(receiver, visits).Touch(value:Entry.Argument(visits))
            """);
        var symbols = Refactoring.ProjectSymbolIndex.Build(
            Directory.GetFiles(_directory, "*.calr"), out var skipped);
        Assert.Empty(skipped);
        var caller = Assert.Single(symbols.Documents, document => document.FilePath.EndsWith("caller.calr", StringComparison.Ordinal));
        Assert.False(caller.BindHadErrors);
        var probe = Assert.Single(caller.BoundModule.Functions, function => function.Symbol.Name.EndsWith(".Probe", StringComparison.Ordinal));
        var outer = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(Assert.Single(probe.Body)).Expression);
        Assert.Null(outer.ResolvedSymbol);
        Assert.Null(outer.ResolvedMetadataMethod);
        Assert.Null(outer.SelectedSourceMethod);
        Assert.True(outer.HasOpaqueOverloads);
        Assert.True(outer.HasUnknownInferredType);
        Assert.Equal(new[] { "value" }, outer.ArgumentNames);
        var receiver = Assert.IsType<BoundCallExpression>(outer.EvaluatedReceiver);
        Assert.Same(receiver, outer.Receiver);
        Assert.Equal(new[] { receiver, Assert.Single(outer.Arguments) }, outer.Children);
        Assert.Equal(new[] { "Consumer.Entry.Make", "Consumer.Entry.Argument" },
            BoundNodeHelpers.DescendantsAndSelf(probe).OfType<BoundCallExpression>()
                .Where(call => call.ResolvedSymbol is not null).Select(call => call.ResolvedSymbol!.Name));
        var diagnostics = new DiagnosticBag();
        new EffectEnforcementPass(diagnostics, UnknownCallPolicy.Permissive)
        {
            CrossModuleTypes = symbols.SourceTypes,
            SourcePath = caller.FilePath
        }.Enforce(caller.Ast);
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));

        var compilation = CSharpCompilation.Create("RefusedReceiver_" + Guid.NewGuid().ToString("N"),
            symbols.Documents.Select(document => CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(document.Ast))),
            GeneratedCSharpCompiler.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(image.ToArray());
        var visits = new[] { 0 };
        var result = assembly.GetType("Consumer.Entry")!.GetMethod("Probe")!
            .Invoke(null, [Activator.CreateInstance(assembly.GetType("Contracts.Receiver")!), visits]);
        Assert.Equal(7, result);
        Assert.Equal(12, visits[0]);
        _output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { extension, Result = result, Trace = visits[0] }));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("cw", false)]
    public void OverrideUsesBaseDeclarationFromAnotherFile(string baseEffects, bool violation)
    {
        Write("base", Base(baseEffects));
        Write("derived", Derived);

        var (_, diagnostics) = Compile();

        Assert.Equal(violation,
            diagnostics.Any(d => d.Code == DiagnosticCode.OverrideEffectVariance));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.Equal(violation, diagnostics.HasErrors);
    }

    [Fact]
    public void UnrelatedBareNameDoesNotSupplyAnExternalBasesContract()
    {
        Write("base", Base("cw").Replace("Shared", "Unrelated"));
        Write("derived", Derived);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterfaceVarianceChecksDirectAndInheritedImplementations(bool inherited)
    {
        Write("interface", """
            module Shared Shared block
              interface IQuiet
                func Render () → void
                  !
            """);
        Write("implementation", $$"""
            module Shared Shared block
              class Writer
            {{(inherited ? "" : "    implements IQuiet\n")}}
                func Render () → void
                  ! cw
                  return
            """);
        if (inherited)
        {
            Write("derived", """
                module Shared Shared block
                  class Derived internal
                    extends Writer
                    implements IQuiet
                """);
        }

        var (_, diagnostics) = Compile();

        var diagnostic = Assert.Single(diagnostics,
            d => d.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.Equal(Path.Combine(_directory, inherited ? "derived.calr" : "implementation.calr"),
            diagnostic.FilePath);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void WarmBuildRechecksVarianceAfterBaseContractChanges()
    {
        Write("base", Base("cw"));
        Write("derived", Derived);
        var (cold, coldDiagnostics) = Compile(cache: true);
        Assert.False(coldDiagnostics.HasErrors);
        Assert.Equal(2, cold.Compiled.Count);

        var (warm, warmDiagnostics) = Compile(cache: true);
        Assert.False(warmDiagnostics.HasErrors);
        Assert.Equal(2, warm.Skipped.Count);

        Write("base", Base(""));
        var (_, changedDiagnostics) = Compile(cache: true);
        Assert.Contains(changedDiagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(changedDiagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Theory]
    [InlineData("Contracts.Base", "")]
    [InlineData("global::Contracts.Base", "")]
    [InlineData("Base", "  using Contracts\n")]
    [InlineData("Chosen.Base", "  using Chosen = Contracts\n")]
    public void QualifiedImportedAndAliasedBasesDoNotUseUnrelatedNames(
        string reference, string imports)
    {
        Write("base", Base("").Replace("Shared", "Contracts"));
        Write("decoy", Base("cw").Replace("Shared", "Unrelated"));
        var derived = Derived.Replace("module Shared Shared block\n", "module Consumer Consumer block\n" + imports)
            .Replace("    extends Base\n", "    extends " + reference + "\n");
        Assert.Contains("    extends " + reference + "\n", derived);
        Write("derived", derived);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousTypesRemainUnknown(bool duplicateIdentity)
    {
        Write("base", Base(""));
        Write("other", duplicateIdentity ? Base("cw") : Base("cw").Replace("Shared", "Other"));
        Write("derived", duplicateIdentity ? Derived
            : Derived.Replace("module Shared Shared block\n",
                "module Consumer Consumer block\n  using Shared\n  using Other\n"));

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
    }

    [Fact]
    public void GenericBaseArgumentsAreSubstitutedBeforeMatchingMethods()
    {
        Write("base", """
            module Shared Shared block
              class Base<T>
                func Render virtual (T:value) → void
                  !
                  return
            """);
        Write("derived", """
            module Shared Shared block
              class Derived internal
                extends Base<int>

                func Render override (int:value) → void
                  ! cw
                  return
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void PartialBaseDeclarationsShareTheirMethods()
    {
        var firstPart = Base("").Replace("  class Base\n", "  class Base partial\n");
        Assert.Contains("  class Base partial\n", firstPart);
        Write("base", firstPart);
        Write("other", """
            module Shared Shared block
              class Base partial
            """);
        Write("derived", Derived);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void GenericInterfaceAndImplementationChainsPreserveTypeArguments()
    {
        Write("parent", """
            module Shared Shared block
              interface IQuiet<T>
                func Render (T:value) → void
                  !
            """);
        Write("child", """
            module Shared Shared block
              interface IChild<U>
                extends IQuiet<U>
            """);
        Write("base", """
            module Shared Shared block
              class Writer<V>
                func Render (V:value) → void
                  ! cw
                  return
            """);
        Write("derived", """
            module Shared Shared block
              class Derived internal
                extends Writer<int>
                implements IChild<int>
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void ClosedGenericTypeAliasCarriesItsArguments()
    {
        Write("base", """
            module Shared Shared block
              class Base<T>
                func Render virtual (T:value) → void
                  !
                  return
            """);
        Write("derived", """
            module Consumer Consumer block
              using Chosen = Shared.Base<int>
              class Derived
                extends Chosen

                func Render override (int:value) → void
                  ! cw
                  return
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void SourceChangedAfterDeclarationCollectionFailsClosed()
    {
        Write("base", Base("cw"));
        Write("derived", Derived);

        Assert.Throws<IOException>(() => Compile(beforeCompile: file =>
        {
            if (file.Name == "base.calr")
                Write("base", Base(""));
        }));
        Assert.Empty(Directory.GetFiles(_directory, "*.g.cs"));
    }

    [Theory]
    [InlineData("e", false)]
    [InlineData("", true)]
    public void CrossFileEffectVariablesAreComparedByOrdinal(string baseRow, bool violation)
    {
        Write("base", $$"""
            module Shared Shared block
              class Base
                func Render <! e> virtual () → void
                  !{{(baseRow.Length == 0 ? "" : " " + baseRow)}}
                  return
            """);
        Write("derived", """
            module Shared Shared block
              class Derived internal
                extends Base

                func Render <! renamed> override () → void
                  ! renamed
                  return
            """);

        var (_, diagnostics) = Compile();

        Assert.Equal(violation,
            diagnostics.Any(d => d.Code == DiagnosticCode.OverrideEffectVariance));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.Equal(violation, diagnostics.HasErrors);
    }

    [Fact]
    public void AmbiguousInterfaceContractsRemainUnknown()
    {
        const string contract = """
            module Shared Shared block
              interface IQuiet
                func Render () → void
                  !
            """;
        Write("first", contract);
        Write("second", contract);
        Write("implementation", """
            module Shared Shared block
              class Writer
                implements IQuiet

                func Render () → void
                  ! cw
                  return
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.InterfaceEffectVariance);
    }

    [Fact]
    public void NominalParameterTypesAreNotMatchedByTheirBareNames()
    {
        Write("base", Base("").Replace("() →", "(Thing:value) →")
            + "\n  class Thing\n");
        Write("derived", Derived.Replace("module Shared Shared block", "module Other Other block")
            .Replace("    extends Base\n", "    extends Shared.Base\n")
            .Replace("() →", "(Thing:value) →") + "\n  class Thing\n");

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
    }

    [Fact]
    public void BodyOnlyEditsDoNotInvalidateUnchangedDerivedContracts()
    {
        Write("base", Base("cw"));
        Write("derived", Derived);
        Assert.False(Compile(cache: true).Diagnostics.HasErrors);
        Write("base", Base("cw").Replace("      return", "      System.Console.WriteLine(\"body only\")\n      return"));

        var (result, diagnostics) = Compile(cache: true);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("base.calr", Assert.Single(result.Compiled).File.Name);
        Assert.Equal("derived.calr", Assert.Single(result.Skipped).Name);
    }

    private static string Base(string effects) => $$"""
        module Shared Shared block
          class Base
            func Render virtual () → void
              !{{(effects.Length == 0 ? "" : " " + effects)}}
              return
        """;

    private const string Derived = """
        module Shared Shared block
          class Derived
            extends Base

            func Render override () → void
              ! cw
              return
        """;

    private void Write(string name, string source) =>
        File.WriteAllText(Path.Combine(_directory, name + ".calr"), source);

    private (CompilationDriver.DriverResult Result, DiagnosticBag Diagnostics) Compile(
        bool cache = false, Action<FileInfo>? beforeCompile = null)
    {
        var diagnostics = new DiagnosticBag();
        var sources = Directory.GetFiles(_directory, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FileInfo(path)).ToList();
        var result = CompilationDriver.CompileAll(
            sources,
            file =>
            {
                beforeCompile?.Invoke(file);
                return new CompilationOptions
                {
                    EnforceEffects = true,
                    UnknownCallPolicy = UnknownCallPolicy.Strict,
                    UnsafeTranspileOnly = !cache
                };
            },
            crossModuleEnforcement: true,
            crossModulePolicy: UnknownCallPolicy.Strict,
            onCompiled: (file, compiled) =>
                File.WriteAllText(Path.ChangeExtension(file.FullName, ".g.cs"), compiled.GeneratedCode),
            diagnosticSink: diagnostics,
            cache: cache ? new CompilationDriver.DriverCacheSettings(
                _directory, "variance", false,
                file => Path.ChangeExtension(file.FullName, ".g.cs")) : null);
        return (result, diagnostics);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
