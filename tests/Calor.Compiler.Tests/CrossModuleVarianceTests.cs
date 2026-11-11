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

    public CrossModuleVarianceTests() => Directory.CreateDirectory(_directory);

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
            "private" => NativeLibrary.Replace("func Pick public (int:", "func Pick private (int:"),
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
                func Probe public (Func<int>:callback {{(argumentRow is null ? "" : "! " + argumentRow)}}) → int
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
            §M{Shared}
              §IFACE{IQuiet}
                §MT{Render} () -> void
                  §E{}
            """);
        Write("implementation", """
            §M{Shared}
              §CL{Writer:pub}
                §MT{Render:pub} () -> void
                  §E{cw}
            """ + (inherited ? "" : "\n    §IMPL{IQuiet}\n"));
        if (inherited)
        {
            Write("derived", """
                §M{Shared}
                  §CL{Derived:Writer:pub}
                    §IMPL{IQuiet}
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
    [InlineData("Base", "  §U{Contracts}\n")]
    [InlineData("Chosen.Base", "  §U{Chosen:Contracts}\n")]
    public void QualifiedImportedAndAliasedBasesDoNotUseUnrelatedNames(
        string reference, string imports)
    {
        Write("base", Base("").Replace("Shared", "Contracts"));
        Write("decoy", Base("cw").Replace("Shared", "Unrelated"));
        var derived = Derived.Replace("§M{Shared}\n", "§M{Consumer}\n" + imports)
            .Replace("§CL{Derived:Base:pub}", "§CL{Derived:" + reference + ":pub}");
        Assert.Contains("§CL{Derived:" + reference + ":pub}", derived);
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
            : Derived.Replace("§M{Shared}\n",
                "§M{Consumer}\n  §U{Shared}\n  §U{Other}\n"));

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
    }

    [Fact]
    public void GenericBaseArgumentsAreSubstitutedBeforeMatchingMethods()
    {
        Write("base", """
            §M{Shared}
              §CL{Base:pub}<T>
                §MT{Render:pub:virt} (T:value) -> void
                  §E{}
            """);
        Write("derived", """
            §M{Shared}
              §CL{Derived:Base<i32>:pub}
                §MT{Render:pub:over} (i32:value) -> void
                  §E{cw}
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void PartialBaseDeclarationsShareTheirMethods()
    {
        var firstPart = Base("").Replace("§CL{Base:pub}", "§CL{Base:pub:partial}");
        Assert.Contains("§CL{Base:pub:partial}", firstPart);
        Write("base", firstPart);
        Write("other", """
            §M{Shared}
              §CL{Base:pub:partial}
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
            §M{Shared}
              §IFACE{IQuiet}<T>
                §MT{Render} (T:value) -> void
                  §E{}
            """);
        Write("child", """
            §M{Shared}
              §IFACE{IChild}<U>
                §EXT{IQuiet<U>}
            """);
        Write("base", """
            §M{Shared}
              §CL{Writer:pub}<V>
                §MT{Render:pub} (V:value) -> void
                  §E{cw}
            """);
        Write("derived", """
            §M{Shared}
              §CL{Derived:Writer<i32>:pub}
                §IMPL{IChild<i32>}
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InterfaceEffectVariance);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void NamespaceImportsDoNotLeakBetweenSiblingDeclarations()
    {
        Write("base", Base("").Replace("Shared", "Contracts"));
        Write("other", Base("cw").Replace("Shared", "Other"));
        Write("derived", """
            §M{Consumer}
              §NS{Consumer}
                §U{Contracts}
                §CL{First:Base:pub}
                  §MT{Render:pub:over} () -> void
                    §E{cw}
              §NS{Consumer}
                §U{Other}
                §CL{Second:Base:pub}
                  §MT{Render:pub:over} () -> void
                    §E{cw}
            """);

        var (_, diagnostics) = Compile();

        var violation = Assert.Single(diagnostics,
            d => d.Code == DiagnosticCode.OverrideEffectVariance);
        Assert.Contains("First.Render", violation.Message);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
    }

    [Fact]
    public void ClosedGenericTypeAliasCarriesItsArguments()
    {
        Write("base", """
            §M{Shared}
              §CL{Base:pub}<T>
                §MT{Render:pub:virt} (T:value) -> void
                  §E{}
            """);
        Write("derived", """
            §M{Consumer}
              §U{Chosen:Shared.Base<i32>}
              §CL{Derived:Chosen:pub}
                §MT{Render:pub:over} (i32:value) -> void
                  §E{cw}
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
            §M{Shared}
              §CL{Base:pub}
                §MT{Render:pub:virt}<eff e> () -> void
                  §E{ {{baseRow}} }
            """);
        Write("derived", """
            §M{Shared}
              §CL{Derived:Base:pub}
                §MT{Render:pub:over}<eff renamed> () -> void
                  §E{renamed}
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
            §M{Shared}
              §IFACE{IQuiet}
                §MT{Render} () -> void
                  §E{}
            """;
        Write("first", contract);
        Write("second", contract);
        Write("implementation", """
            §M{Shared}
              §CL{Writer:pub}
                §IMPL{IQuiet}
                §MT{Render:pub} () -> void
                  §E{cw}
            """);

        var (_, diagnostics) = Compile();

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.EffectRowUnknown);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.InterfaceEffectVariance);
    }

    [Fact]
    public void NominalParameterTypesAreNotMatchedByTheirBareNames()
    {
        Write("base", Base("").Replace("() ->", "(Thing:value) ->")
            + "\n  §CL{Thing:pub}\n");
        Write("derived", Derived.Replace("§M{Shared}", "§M{Other}")
            .Replace(":Derived:Base:pub", ":Derived:Shared.Base:pub")
            .Replace("() ->", "(Thing:value) ->") + "\n  §CL{Thing:pub}\n");

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
        Write("base", Base("cw") + "\n      §P \"body only\"\n");

        var (result, diagnostics) = Compile(cache: true);

        Assert.False(diagnostics.HasErrors);
        Assert.Equal("base.calr", Assert.Single(result.Compiled).File.Name);
        Assert.Equal("derived.calr", Assert.Single(result.Skipped).Name);
    }

    private static string Base(string effects) => $$"""
        §M{Shared}
          §CL{Base:pub}
            §MT{Render:pub:virt} () -> void
              §E{ {{effects}} }
        """;

    private const string Derived = """
        §M{Shared}
          §CL{Derived:Base:pub}
            §MT{Render:pub:over} () -> void
              §E{cw}
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
