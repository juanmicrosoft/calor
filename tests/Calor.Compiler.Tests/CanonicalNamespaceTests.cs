using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalNamespaceTests
{
    [Theory]
    [InlineData("module Grid Contoso.Tools", "Contoso.Tools", true)]
    [InlineData("module Grid Contoso.Tools block", "Contoso.Tools", false)]
    [InlineData("module Grid Contoso.@event", "Contoso.event", true)]
    [InlineData("module Grid @overflow block overflow unchecked", "overflow", false)]
    [InlineData("module Grid _global", "_global", true)]
    [InlineData("module Grid", "", true)]
    public void PositionalNamespaceIsSeparateFromWrapperAndStaticContainer(
        string header, string expectedNamespace, bool fileScoped)
    {
        var module = Parse($"{header}\n  module Area\n    func Square (int:side) → int\n      return side * side");
        Assert.Equal("Grid", module.Name);
        var scope = Assert.Single(module.NamespaceScopes);
        Assert.Equal(expectedNamespace, scope.FullName);
        Assert.Equal(fileScoped, scope.IsFileScoped);
        Assert.Equal(expectedNamespace.Length == 0, scope.IsGlobal);
        var container = Assert.Single(module.Classes);
        Assert.True(container.IsModule);
        Assert.Equal(expectedNamespace, container.NamespaceIdentity);
        Assert.Equal(scope.Id, container.NamespaceScopeId);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted);
        Assert.Equal(expectedNamespace, reparse.Classes.Single().NamespaceIdentity);
        Assert.Equal(fileScoped, reparse.NamespaceScopes.Single().IsFileScoped);
        var compilation = Compile(new CSharpEmitter().Emit(reparse));
        var typeName = expectedNamespace.Length == 0 ? "Area" : $"{expectedNamespace}.Area";
        Assert.NotNull(compilation.GetTypeByMetadataName(typeName));
        Assert.Null(compilation.GetTypeByMetadataName($"{expectedNamespace}.Grid"));
    }

    [Fact]
    public void NativeUsingFormsKeepCompilationUnitScopeAndTargets()
    {
        var module = Parse("""
            module Grid Contoso.Tools
              global using System
              using System.Text
              using static System.Math
              using Json = System.Text.Json.JsonDocument
              class Area
            """);
        Assert.Equal(4, module.Usings.Count);
        Assert.All(module.Usings, directive => Assert.Null(directive.NamespaceScopeId));
        Assert.True(module.Usings[2].IsStatic);
        Assert.Equal("Json", module.Usings[3].Alias);
        Assert.True(module.Usings[0].IsGlobal);
        var emitted = new CalorEmitter().Emit(module);
        Assert.DoesNotContain('§', emitted);
        var reparse = Parse(emitted);
        Assert.Equal(module.Usings.Select(directive => directive.Namespace),
            reparse.Usings.Select(directive => directive.Namespace));
        Compile(new CSharpEmitter().Emit(reparse));
    }

    [Theory]
    [InlineData("internal interface ISource { int Get(); }", "", "ISource", Accessibility.Internal)]
    [InlineData("namespace Contoso.Tools; internal interface ISource { int Get(); }",
        "Contoso.Tools", "ISource", Accessibility.Internal)]
    [InlineData("namespace Contoso.@event { public class Owner { private interface ISource { int Get(); } } }",
        "Contoso.event", "Owner+ISource", Accessibility.Private)]
    [InlineData("namespace _global { public class Owner { protected interface ISource { int Get(); } } }",
        "_global", "Owner+ISource", Accessibility.Protected)]
    public void RealConverterNativeSourceAndClrPreserveOriginalInterfaceExports(
        string source, string ns, string type, Accessibility expected)
    {
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        Assert.NotNull(conversion.CalorSource);
        Assert.DoesNotContain("raw ", conversion.CalorSource);
        Assert.DoesNotContain('§', conversion.CalorSource);
        var module = Parse(conversion.CalorSource);
        Assert.Equal(ns, module.NamespaceScopes.Single().FullName);
        var compiled = Compile(new CSharpEmitter().Emit(module));
        var original = Compile(source);
        var metadataName = ns.Length == 0 ? type : $"{ns}.{type}";
        Assert.Equal(expected, original.GetTypeByMetadataName(metadataName)!.DeclaredAccessibility);
        Assert.Equal(expected, compiled.GetTypeByMetadataName(metadataName)!.DeclaredAccessibility);
    }

    [Theory]
    [InlineData("UsualFile.cs", "UsualFile")]
    [InlineData("a4-created.cs", "a4created")]
    [InlineData("123-array-input.cs", "_123arrayinput")]
    [InlineData("space label.cs", "spacelabel")]
    [InlineData("snapshot#1.verified.cs", "snapshot_1.verified")]
    [InlineData("global::Source.cs", "global__Source")]
    [InlineData("---.cs", "_")]
    [InlineData("class.cs", "class")]
    [InlineData("module.cs", "module")]
    [InlineData("café Δ.cs", "caféΔ")]
    public void FileDerivedLabelsAreNativeIdentifiersWithoutChangingClrIdentity(
        string sourceFile, string expectedLabel)
    {
        const string source = "public static class Functions { public static int Read() => 42; }";
        var conversion = new CSharpToCalorConverter().Convert(source, sourceFile);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues));
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Equal(0, conversion.DropCount);
        var module = Parse(conversion.CalorSource!);
        Assert.Equal(expectedLabel, module.Name);
        Assert.True(Assert.Single(module.NamespaceScopes).IsGlobal);
        var original = Compile(source);
        var generated = Compile(new CSharpEmitter().Emit(module));
        var originalType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            original.GetTypeByMetadataName("Functions"));
        var generatedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            generated.GetTypeByMetadataName("Functions"));
        Assert.Equal(originalType.ToDisplayString(), generatedType.ToDisplayString());
        Assert.Equal(originalType.DeclaredAccessibility, generatedType.DeclaredAccessibility);
        Assert.Equal(SpecialType.System_Int32,
            Assert.IsAssignableFrom<IMethodSymbol>(Assert.Single(generatedType.GetMembers("Read")))
                .ReturnType.SpecialType);
        Assert.Equal(["Functions"], generated.Assembly.GlobalNamespace.GetTypeMembers().Select(type => type.Name));
    }

    [Theory]
    [InlineData("namespace A { class C {} } namespace A { class D {} }")]
    [InlineData("namespace A { namespace B { class C {} } }")]
    [InlineData("namespace A { using System; class C {} }")]
    public void OtherLexicalTopologiesPreserveTheOriginalWholeFile(string source)
    {
        var conversion = new CSharpToCalorConverter().Convert(source);
        Assert.True(conversion.Success, string.Join("\n", conversion.Context.Issues));
        var parsed = Parse(conversion.CalorSource!);
        var preserved = Assert.Single(parsed.InteropBlocks);
        Assert.True(preserved.IsCompilationUnitPassthrough);
        Assert.Equal(source, preserved.CSharpCode);
        Compile(new CSharpEmitter().Emit(parsed));
    }

    [Theory]
    [InlineData("module Grid overflow=checked")]
    [InlineData("module Grid Contoso.Tools Extra")]
    [InlineData("module Grid Contoso.event")]
    [InlineData("module Grid Contoso.Tools overflow impossible")]
    public void HeaderAliasesAndMalformedNamespaceFieldsAreDiagnosed(string header)
    {
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer($"{header}\n  class Area", diagnostics)
            .TokenizeAllForParser(), diagnostics).Parse();
        Assert.True(diagnostics.HasErrors);
    }

    [Theory]
    [InlineData("global::System.EventArgs.Empty", true, ConversionFidelity.Lossless)]
    [InlineData("global::System.EventArgs.Empty", false, ConversionFidelity.Lossless)]
    [InlineData("Root::EventArgs.Empty", true, ConversionFidelity.Lossless)]
    [InlineData("Root::EventArgs.Empty", false, ConversionFidelity.Lossless)]
    [InlineData("global::System.EventArgs.Empty", true, ConversionFidelity.Lossy)]
    [InlineData("global::System.EventArgs.Empty", false, ConversionFidelity.Lossy)]
    [InlineData("Root::EventArgs.Empty", true, ConversionFidelity.Lossy)]
    [InlineData("Root::EventArgs.Empty", false, ConversionFidelity.Lossy)]
    public void AliasQualifiedValuesPreserveTheOriginalMemberSymbolAndRuntime(
        string expression, bool shadow, ConversionFidelity fidelity)
    {
        var source = QualifiedValueWitness(expression, shadow,
            "public static object Read() { return /* keep qualifier */ VALUE; }");
        var original = Compile(source);
        AssertEmptyFieldTargetAndRuntime(original, "System.EventArgs");
        var originalMethod = original.SyntaxTrees.Single().GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var result = new CSharpToCalorConverter(new ConversionOptions { Fidelity = fidelity }).Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var loss = Assert.Single(result.Losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.Equal("alias-qualified-value", loss.Feature);
        Assert.Equal(ConversionPath.Interop, loss.Path);
        Assert.Equal("preserved", result.Paths.Outcome);
        Assert.Equal(1, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
        Assert.NotNull(FeatureSupport.GetFeatureInfo(loss.Feature));
        Assert.Equal(SupportLevel.NotSupported, FeatureSupport.GetSupportLevel(loss.Feature));

        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var module = Parse(native);
            Assert.Empty(module.InteropBlocks);
            var scope = Assert.Single(module.NamespaceScopes);
            Assert.Equal("AliasFidelity", scope.FullName);
            var alias = Assert.Single(module.Usings);
            Assert.Equal("Root", alias.Alias);
            Assert.Equal("global::System", alias.Namespace);
            Assert.Null(alias.NamespaceScopeId);
            Assert.False(alias.IsGlobal);
            var probe = module.Classes.Single(type => type.Name == "Probe");
            Assert.True(probe.IsModule);
            Assert.Empty(probe.Methods);
            var preserved = Assert.Single(probe.InteropBlocks);
            Assert.Equal("alias-qualified-value", preserved.FeatureName);
            Assert.Equal(InteropMemberKind.Method, preserved.MemberKind);
            Assert.Equal("AliasFidelity", probe.NamespaceIdentity);
            Assert.Equal(scope.Id, probe.NamespaceScopeId);
            Assert.Equal(originalMethod.ToFullString(), preserved.CSharpCode);
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(module)), "System.EventArgs");
        }

        var originalBlock = Assert.Single(result.Ast!.Classes.Single(type => type.Name == "Probe").InteropBlocks);
        Assert.Equal("AliasFidelity", originalBlock.NamespaceIdentity);
        Assert.Equal(result.Ast.NamespaceScopes.Single().Id, originalBlock.NamespaceScopeId);
        Assert.Equal(originalMethod.SpanStart, originalBlock.Span.Start);
        Assert.Equal(originalMethod.Span.Length, originalBlock.Span.Length);
        var position = originalMethod.GetLocation().GetLineSpan().StartLinePosition;
        Assert.Equal(position.Line + 1, originalBlock.Span.Line);
        Assert.Equal(position.Character + 1, originalBlock.Span.Column);
    }

    [Theory]
    [InlineData("public static object Value = VALUE; public static object Read() { return Value; }",
        InteropMemberKind.Field)]
    [InlineData("public static object Value { get { return VALUE; } } public static object Read() { return Value; }",
        InteropMemberKind.Property)]
    [InlineData("private static object Value; static Probe() { Value = VALUE; } public static object Read() { return Value; }",
        InteropMemberKind.Constructor)]
    public void AliasQualifiedInitializersAndAccessorsPreserveTheirRequiredMemberBoundary(
        string member, InteropMemberKind expectedKind)
    {
        var source = QualifiedValueWitness("global::System.EventArgs.Empty", true, member);
        AssertEmptyFieldTargetAndRuntime(Compile(source), "System.EventArgs");
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var loss = Assert.Single(result.Losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.Equal("alias-qualified-value", loss.Feature);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var module = Parse(native);
            var preserved = Assert.Single(module.Classes.Single(type => type.Name == "Probe").InteropBlocks);
            Assert.Equal(expectedKind, preserved.MemberKind);
            Assert.Equal("alias-qualified-value", preserved.FeatureName);
            Assert.Contains("global::System.EventArgs.Empty", preserved.CSharpCode);
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(module)), "System.EventArgs");
        }
    }

    [Theory]
    [InlineData(false, "System.EventArgs")]
    [InlineData(true, "AliasFidelity.System.EventArgs")]
    public void OrdinaryValuesRetainNativeShadowBinding(bool shadow, string declaringType)
    {
        var source = QualifiedValueWitness("System.EventArgs.Empty", shadow,
            "public static object Read() { return VALUE; }");
        AssertEmptyFieldTargetAndRuntime(Compile(source), declaringType);
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        Assert.Equal("native", result.Paths.Outcome);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(Parse(native))), declaringType);
    }

    [Fact]
    public void AliasQualifiedTypesUsingsConstraintsAndNameofRemainNative()
    {
        const string source = """
            using Root = global::System;
            namespace AliasFidelity;
            public class Probe<T> where T : global::System.EventArgs
            {
                public global::System.EventArgs Echo(Root::EventArgs value) { return value; }
                public string Name() { return nameof(global::System.EventArgs.Empty); }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Empty(result.Losses);
        Assert.Equal("native", result.Paths.Outcome);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var compiled = Compile(new CSharpEmitter().Emit(Parse(native)));
            var probe = compiled.GetTypeByMetadataName("AliasFidelity.Probe`1")!;
            var constraint = Assert.Single(probe.TypeParameters.Single().ConstraintTypes);
            Assert.Equal("System.EventArgs", constraint.ToDisplayString());
            var echo = Assert.Single(probe.GetMembers("Echo").OfType<IMethodSymbol>());
            Assert.Equal("System.EventArgs", echo.ReturnType.ToDisplayString());
            Assert.True(SymbolEqualityComparer.Default.Equals(echo.ReturnType, echo.Parameters.Single().Type));
            Assert.Equal("Empty", Invoke(Load(compiled).GetType("AliasFidelity.Probe`1")!
                .MakeGenericType(typeof(EventArgs)), "Name"));
        }
    }

    [Theory]
    [InlineData("global::System.EventArgs.Empty", 18)]
    [InlineData("Root::EventArgs.Empty", 16)]
    public void AuthoredAliasQualifiedValuesRemainExplicitCapabilityErrors(string expression, int column)
    {
        var source = $"module Values\n  func Read () → object\n    return {expression}";
        var diagnostics = new DiagnosticBag();
        _ = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.Contains(diagnostics.Errors, diagnostic => diagnostic.Code == DiagnosticCode.UnexpectedToken
            && diagnostic.Span.Line == 3 && diagnostic.Span.Column == column);
        Assert.Equal("::", source.Substring(source.LastIndexOf('\n') + column, 2));
    }

    [Fact]
    public void NamespaceLocalAliasContextStillPreservesTheExactWholeFile()
    {
        const string source = """
            namespace AliasFidelity
            {
                // This alias belongs only to this namespace.
            #if true
                using Root = global::System;
            #endif
                public static class Probe
                {
                    public static object Read() { return Root::EventArgs.Empty; }
                }
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        var loss = Assert.Single(result.Losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, loss.Kind);
        Assert.Equal("namespace-topology", loss.Feature);
        foreach (var native in new[] { result.CalorSource!, new CalorEmitter().Emit(Parse(result.CalorSource!)) })
        {
            var module = Parse(native);
            var preserved = Assert.Single(module.InteropBlocks);
            Assert.True(preserved.IsCompilationUnitPassthrough);
            Assert.Equal(source, preserved.CSharpCode);
            AssertEmptyFieldTargetAndRuntime(Compile(new CSharpEmitter().Emit(module)), "System.EventArgs");
        }
    }

    private static string QualifiedValueWitness(string expression, bool shadow, string member) => $$"""
        using Root = global::System;
        namespace AliasFidelity;
        {{(shadow ? "public static class System { public static class EventArgs { public static int Empty = 7; } }" : "")}}
        public static class Probe
        {
            {{member.Replace("VALUE", expression, StringComparison.Ordinal)}}
        }
        """;

    private static void AssertEmptyFieldTargetAndRuntime(CSharpCompilation compilation, string declaringType)
    {
        var tree = compilation.SyntaxTrees.Single();
        var access = tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Single(node => node.Name.Identifier.ValueText == "Empty");
        var symbol = Assert.IsAssignableFrom<IFieldSymbol>(
            compilation.GetSemanticModel(tree).GetSymbolInfo(access).Symbol);
        Assert.Equal(declaringType, symbol.ContainingType.ToDisplayString());
        Assert.True(symbol.IsStatic);
        var value = Invoke(Load(compilation).GetType("AliasFidelity.Probe")!, "Read");
        if (declaringType == "System.EventArgs")
        {
            Assert.Equal(typeof(EventArgs).Assembly.GetName().Name, symbol.ContainingAssembly.Name);
            Assert.Same(EventArgs.Empty, value);
        }
        else
        {
            Assert.True(SymbolEqualityComparer.Default.Equals(compilation.Assembly, symbol.ContainingAssembly));
            Assert.Equal(7, Assert.IsType<int>(value));
        }
    }

    private static Assembly Load(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? Invoke(Type type, string method)
    {
        var target = type.GetMethod(method)!;
        return target.Invoke(target.IsStatic ? null : Activator.CreateInstance(type), null);
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static CSharpCompilation Compile(string source)
    {
        var compilation = CSharpCompilation.Create("NamespaceExports",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        return compilation;
    }
}
