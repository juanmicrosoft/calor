using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Xunit.Abstractions;
using Binder = Calor.Compiler.Binding.Binder;

namespace Calor.Compiler.Tests;

public class NominalReferenceIdentityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("A.Foo", "A.Foo", true)]
    [InlineData("A.Foo", "B.Foo", false)]
    [InlineData("A.Value", "A.Value", false)]
    [InlineData("A.Choice", "A.Choice", false)]
    [InlineData("A.Box`1", "A.Box`1", false)]
    public void UnderlyingReferenceIdentity_RequiresResolvedReferenceSymbols(
        string sourceName, string targetName, bool expected)
    {
        var compilation = IdentityCompilation();
        var sourceSymbol = compilation.GetTypeByMetadataName(sourceName);
        var targetSymbol = compilation.GetTypeByMetadataName(targetName);
        Assert.NotNull(sourceSymbol);
        Assert.NotNull(targetSymbol);
        var source = new NominalBoundType(sourceName + "?", NullableAnnotation.Annotated,
            roslynSymbol: sourceSymbol);
        var target = new NominalBoundType(targetName, NullableAnnotation.NotAnnotated,
            roslynSymbol: targetSymbol);
        Assert.Equal(expected, source.HasSameUnderlyingReferenceType(target));
        Assert.Equal(expected, NullabilityChecker.IsPossiblyNullAssignedTo(new FixtureExpression(source), target));
    }

    [Fact]
    public void UnderlyingIdentity_DoesNotRewriteDisplayEqualityOrHash()
    {
        var compilation = IdentityCompilation();
        var symbol = compilation.GetTypeByMetadataName("A.Foo");
        Assert.NotNull(symbol);
        var annotated = new NominalBoundType("?Foo", NullableAnnotation.Annotated, roslynSymbol: symbol);
        var nonNullable = new NominalBoundType("A.Foo", NullableAnnotation.NotAnnotated, roslynSymbol: symbol);
        Assert.True(annotated.HasSameUnderlyingReferenceType(nonNullable));
        Assert.Equal("?Foo", annotated.DisplayString);
        Assert.Equal("A.Foo", nonNullable.DisplayString);
        Assert.False(annotated.Equals(nonNullable));

        var sameDisplay = new NominalBoundType("?Foo", NullableAnnotation.NotAnnotated, roslynSymbol: symbol);
        Assert.False(annotated.Equals(sameDisplay));
        Assert.Equal(2, new HashSet<BoundType> { annotated, sameDisplay }.Count);
        var copy = new NominalBoundType("?Foo", NullableAnnotation.Annotated, roslynSymbol: symbol);
        Assert.Equal(annotated, copy);
        Assert.Equal(annotated.GetHashCode(), copy.GetHashCode());

        var otherAssembly = compilation.WithAssemblyName("OtherIdentityAssembly").GetTypeByMetadataName("A.Foo");
        Assert.NotNull(otherAssembly);
        var other = new NominalBoundType("?Foo", NullableAnnotation.Annotated, roslynSymbol: otherAssembly);
        Assert.False(annotated.HasSameUnderlyingReferenceType(other));
        Assert.Equal(annotated, other);
        Assert.Equal(annotated.GetHashCode(), other.GetHashCode());
    }

    [Theory]
    [InlineData(NullableAnnotation.Annotated)]
    [InlineData(NullableAnnotation.Oblivious)]
    [InlineData(NullableAnnotation.NotAnnotated)]
    public void UnresolvedNominalName_IsNotAReferenceIdentity(NullableAnnotation annotation)
    {
        var symbol = IdentityCompilation().GetTypeByMetadataName("A.Foo");
        Assert.NotNull(symbol);
        var target = new NominalBoundType("A.Foo", NullableAnnotation.NotAnnotated, roslynSymbol: symbol);
        var source = new NominalBoundType("A.Foo", annotation);
        Assert.False(source.HasSameUnderlyingReferenceType(target));
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(new FixtureExpression(source), target));
    }

    [Fact]
    public void ObliviousNominalReference_IsNotWidenedByIdentityRepair()
    {
        var symbol = IdentityCompilation().GetTypeByMetadataName("A.Foo");
        Assert.NotNull(symbol);
        var target = new NominalBoundType("A.Foo", NullableAnnotation.NotAnnotated, roslynSymbol: symbol);
        var source = new NominalBoundType("A.Foo", NullableAnnotation.Oblivious, roslynSymbol: symbol);
        Assert.True(source.HasSameUnderlyingReferenceType(target));
        Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(new FixtureExpression(source), target));
    }

    public static IEnumerable<object?[]> AnnotationKinds()
    {
        yield return [new PrimitiveBoundType("INT"), NullableAnnotation.NotAnnotated];
        yield return [new PrimitiveBoundType("System.Int32"), NullableAnnotation.NotAnnotated];
        yield return [new PrimitiveBoundType("INT[bits=16][signed=true]"), NullableAnnotation.NotAnnotated];
        yield return [new PrimitiveBoundType("VOID"), NullableAnnotation.NotAnnotated];
        yield return [new PrimitiveBoundType("NEVER"), NullableAnnotation.NotAnnotated];
        yield return [new PrimitiveBoundType("INT?"), null];
        yield return [new PrimitiveBoundType("OPTION"), null];
        yield return [new PrimitiveBoundType("STRING"), null];
        yield return [new PrimitiveBoundType("OBJECT"), null];
        yield return [new PrimitiveBoundType("Unknown"), null];
        yield return [new NominalBoundType("Unknown"), NullableAnnotation.Oblivious];
        yield return [new NominalBoundType("STRING", NullableAnnotation.Annotated), NullableAnnotation.Annotated];
        yield return [new NominalBoundType("STRING", NullableAnnotation.NotAnnotated), NullableAnnotation.NotAnnotated];
        yield return [new ArrayBoundType(new NominalBoundType("STRING")), NullableAnnotation.Oblivious];
        yield return [new GenericInstantiationBoundType(new NominalBoundType("List"),
            [new NominalBoundType("STRING")], NullableAnnotation.Annotated), NullableAnnotation.Annotated];
        yield return [new FunctionBoundType(ImmutableArray<BoundType>.Empty, new PrimitiveBoundType("VOID")), null];
        yield return [new UnresolvedBoundType("unresolved"), null];
    }

    [Theory]
    [MemberData(nameof(AnnotationKinds))]
    public void AnnotationRead_DistinguishesUnsupportedFromKnownNonNull(
        BoundType type, NullableAnnotation? expected)
    {
        Assert.Equal(expected, NullabilityChecker.GetAnnotation(type));
        if (expected is null)
            Assert.False(NullabilityChecker.IsPossiblyNullAssignedTo(
                new FixtureExpression(type), new NominalBoundType("STRING", NullableAnnotation.NotAnnotated)));
    }

    [Fact]
    public void AnnotationRead_CoversEveryConcreteBoundTypeKind()
    {
        var actual = typeof(BoundType).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(BoundType).IsAssignableFrom(type))
            .ToHashSet();
        var covered = AnnotationKinds().Select(row => Assert.IsAssignableFrom<BoundType>(row[0]).GetType()).ToHashSet();
        Assert.True(actual.SetEquals(covered));
    }

    [Theory]
    [InlineData("A.Foo", true)]
    [InlineData("B.Foo", false)]
    public void NativeReturn_ResolvesDeclarationInCalleeNamespace(string receivingType, bool expected)
    {
        const string sourceA = """
            §M{m1:Namespaces}
              §CL{c1:Foo:pub}
                §MT{get:Get:pub:static} () -> ?Foo
                  §R §NEW{Foo}
            """;
        var sourceB = $$"""
            §M{m1:Namespaces}
              §CL{c2:Foo:pub}
                §MT{probe:Probe:pub:static} () -> void
                  §B{value:{{receivingType}}} §C{A.Foo.Get} §/C
            """;
        var diagnostics = new DiagnosticBag();
        var parsedA = new Parser(new Lexer(sourceA, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var parsedB = new Parser(new Lexer(sourceB, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        var classA = Assert.Single(parsedA.Classes);
        var classB = Assert.Single(parsedB.Classes);
        Assert.Equal("Foo", classA.Name);
        Assert.Equal("Foo", classB.Name);
        classA.NamespaceIdentity = "A";
        classB.NamespaceIdentity = "B";
        var parsed = new ModuleNode(parsedA.Span, parsedA.Id, parsedA.Name,
            parsedA.Usings, parsedA.Interfaces, [classA, classB], parsedA.Functions, parsedA.Attributes);
        var bound = new Binder(diagnostics).Bind(parsed);
        var producer = GetProducerCall(bound, "binding");
        Assert.True(producer.ResolvedSymbol is not null,
            $"Target: {producer.Target}; functions: {string.Join(", ", bound.Functions.Select(f => f.Symbol.Name))}; "
            + $"diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"))}");
        var returned = Assert.IsType<NominalBoundType>(producer.Type);
        Assert.Equal("A.Foo", returned.Declaration?.QualifiedName);
        Assert.Equal("?Foo", returned.DisplayString);
        Assert.Equal(expected, diagnostics.Any(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
    }

    [Theory]
    [InlineData("?Foo", "binding", DiagnosticCode.NullableToNonNullableBinding)]
    [InlineData("Foo?", "binding", DiagnosticCode.NullableToNonNullableBinding)]
    [InlineData("?Foo", "return", DiagnosticCode.NullableReturnFromNonNullable)]
    [InlineData("Foo?", "return", DiagnosticCode.NullableReturnFromNonNullable)]
    [InlineData("?Foo", "argument", DiagnosticCode.NullableArgumentToNonNullableParameter)]
    [InlineData("Foo?", "argument", DiagnosticCode.NullableArgumentToNonNullableParameter)]
    public void NativeNullableReturn_ReachesResolvedConsumer(
        string typeName, string boundary, string expectedCode)
    {
        var (module, diagnostics) = Bind(NativeSource(typeName, boundary));
        var getter = GetProducerCall(module, boundary);
        Assert.NotNull(getter.ResolvedSymbol);
        var returned = Assert.IsType<NominalBoundType>(getter.Type);
        Assert.Equal(typeName, returned.DisplayString);
        Assert.Equal(NullableAnnotation.Annotated, returned.NullableAnnotation);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Code == expectedCode));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.NotNull(returned.Declaration);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("return")]
    [InlineData("argument")]
    public void NativeNonNullableReturn_PreservesIdentityAndAnnotation(string boundary)
    {
        var (module, diagnostics) = Bind(NativeSource("Foo", boundary));
        var getter = GetProducerCall(module, boundary);
        Assert.NotNull(getter.ResolvedSymbol);
        var returned = Assert.IsType<NominalBoundType>(getter.Type);
        Assert.Equal("Foo", returned.DisplayString);
        Assert.Equal(NullableAnnotation.NotAnnotated, returned.NullableAnnotation);
        Assert.DoesNotContain(diagnostics, d => d.Code is
            DiagnosticCode.NullableToNonNullableBinding or
            DiagnosticCode.NullableReturnFromNonNullable or
            DiagnosticCode.NullableArgumentToNonNullableParameter or
            DiagnosticCode.NoMatchingOverload);
        Assert.NotNull(returned.Declaration);
    }

    [Fact]
    public void NativeInstanceReturn_ReachesConsumer()
    {
        var source = NativeSource("?Foo", "binding")
            .Replace(":pub:static", ":pub", StringComparison.Ordinal)
            .Replace("Foo.Get", "this.Get", StringComparison.Ordinal);
        var (module, diagnostics) = Bind(source);
        var call = GetProducerCall(module, "binding");
        Assert.NotNull(call.ResolvedSymbol);
        Assert.NotNull(Assert.IsType<NominalBoundType>(call.Type).Declaration);
        Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
    }

    [Theory]
    [InlineData("binding", DiagnosticCode.NullableToNonNullableBinding)]
    [InlineData("return", DiagnosticCode.NullableReturnFromNonNullable)]
    public void ExpandedNativeReturn_PreservesDisplayAndReachesConsumer(string boundary, string code)
    {
        var (module, diagnostics) = Bind(ExpandedNativeSource(boundary));
        var producer = GetProducerCall(module, boundary);
        Assert.NotNull(producer.ResolvedSymbol);
        var type = Assert.IsType<NominalBoundType>(producer.Type);
        Assert.Equal("OPTION[inner=Foo]", type.DisplayString);
        Assert.Equal(NullableAnnotation.Annotated, type.NullableAnnotation);
        Assert.NotNull(type.Declaration);
        Assert.Single(diagnostics.Where(d => d.Code == code));
    }

    [Fact]
    public void ExpandedNativeArgument_KeepsExistingApplicabilityRejection()
    {
        var (module, diagnostics) = Bind(ExpandedNativeSource("argument"));
        var probe = Assert.Single(module.Functions.Where(f => f.Symbol.Name.EndsWith(".Probe", StringComparison.Ordinal)));
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(Assert.Single(probe.Body)).Expression);
        Assert.Null(call.ResolvedSymbol);
        var producer = Assert.IsType<BoundCallExpression>(Assert.Single(call.Arguments));
        Assert.NotNull(producer.ResolvedSymbol);
        Assert.Equal("OPTION[inner=Foo]", producer.Type.DisplayString);
        Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.NoMatchingOverload));
    }

    [Fact]
    public void InferredObjectFallback_DoesNotGainResolvedReferenceIdentity()
    {
        const string source = """
            §M{m1:Unknown}
              §F{probe:Probe:pub} () -> void
                §B{value} §C{Missing} §/C
                §B{copy} value
            """;
        var (module, _) = Bind(source);
        var copy = Assert.IsType<BoundBindStatement>(Assert.Single(module.Functions).Body[1]);
        var type = Assert.IsType<NominalBoundType>(Assert.IsType<BoundVariableExpression>(copy.Initializer).Type);
        Assert.Equal("OBJECT", type.DisplayString);
        Assert.Equal(NullableAnnotation.Oblivious, type.NullableAnnotation);
        Assert.Null(type.RoslynSymbol);
        Assert.Null(type.Declaration);
        Assert.False(type.IsKnownReferenceType);
    }

    [Theory]
    [InlineData("Directory.GetParent")]
    [InlineData("directory.CreateSubdirectory")]
    public void UnsupportedBclReceiver_IsNotRelabeledResolved(string target)
    {
        var source = $$"""
            §M{m1:UnsupportedReceiver}
              §U{System.IO}
              §F{probe:Probe:pub} (System.IO.DirectoryInfo:directory) -> void
                §B{value:System.IO.DirectoryInfo} §C{ {{target}} } §A STR:"/" §/C
            """;
        var (module, diagnostics) = Bind(source);
        var call = GetProducerCall(module, "binding");
        Assert.Null(call.ResolvedSymbol);
        var type = Assert.IsType<NominalBoundType>(call.Type);
        Assert.Null(type.RoslynSymbol);
        Assert.Null(type.Declaration);
        Assert.False(type.IsKnownReferenceType);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    [Theory]
    [InlineData("?Foo")]
    [InlineData("Foo")]
    public void ProductionNominalRouting_RemainsAnalysisOnly(string sourceType)
    {
        var source = NativeSource(sourceType, "return");
        var result = Program.Compile(source, "nominal-identity.calr");
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
        var (_, raw) = Bind(source);
        Assert.Equal(sourceType == "?Foo",
            raw.Any(d => d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Theory]
    [InlineData("GetParent", "binding", true)]
    [InlineData("GetParent", "return", true)]
    [InlineData("CreateDirectory", "binding", false)]
    [InlineData("CreateDirectory", "return", false)]
    public void BclNominalReturn_ReachesConsumerWithActualResolvedSymbol(
        string method, string boundary, bool nullable)
    {
        var call = $"§C{{System.IO.Directory.{method}}} §A STR:\"/\" §/C";
        var body = boundary == "binding"
            ? $"§B{{value:System.IO.DirectoryInfo}} {call}"
            : $"§R {call}";
        var source = $$"""
            §M{m1:NominalMetadata}
              §F{f1:Probe:pub} () -> {{(boundary == "binding" ? "void" : "System.IO.DirectoryInfo")}}
                {{body}}
            """;
        var (module, diagnostics) = Bind(source, out var binder);
        var producer = GetProducerCall(module, boundary);
        var returned = Assert.IsType<NominalBoundType>(producer.Type);
        Assert.NotNull(returned.RoslynSymbol);
        Assert.Equal("DirectoryInfo", returned.RoslynSymbol.Name);
        Assert.True(returned.RoslynSymbol.IsReferenceType);
        Assert.Equal(nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            returned.NullableAnnotation);
        var code = boundary == "binding"
            ? DiagnosticCode.NullableToNonNullableBinding
            : DiagnosticCode.NullableReturnFromNonNullable;
        Assert.Equal(nullable, diagnostics.Any(d => d.Code == code));
        var metadataBinder = Assert.IsType<MetadataBinder>(typeof(Binder)
            .GetField("_metadataBinder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binder));
        var actualCompilation = metadataBinder.Context.HostCompilationForBinder;
        Assert.Single(actualCompilation.References.Where(reference =>
            Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
                actualCompilation.GetAssemblyOrModuleSymbol(reference), returned.RoslynSymbol.ContainingAssembly)));
        if (method == "GetParent" && boundary == "binding")
        {
            output.WriteLine($"Actual return assembly: {returned.RoslynSymbol.ContainingAssembly.Identity}");
            foreach (var reference in actualCompilation.References
                         .OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
                         .OrderBy(reference => reference.FilePath, StringComparer.Ordinal))
            {
                Assert.NotNull(reference.FilePath);
                using var stream = File.OpenRead(reference.FilePath);
                output.WriteLine($"{reference.FilePath}\t{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}");
            }
        }
    }

    [Fact]
    public void NullableBclNominalReturn_ReachesResolvedBclParameter()
    {
        const string source = """
            §M{m1:NominalBclArgument}
              §F{probe:Probe:pub} () -> void
                §B{permissions:System.Security.AccessControl.DirectorySecurity} §C{System.IO.FileSystemAclExtensions.GetAccessControl} §A §C{System.IO.Directory.GetParent} §A STR:"/" §/C §/C
            """;
        var (module, diagnostics) = Bind(source);
        var consumer = GetProducerCall(module, "binding");
        Assert.Equal("DirectorySecurity", Assert.IsType<NominalBoundType>(consumer.Type).RoslynSymbol?.Name);
        var producer = Assert.IsType<BoundCallExpression>(Assert.Single(consumer.Arguments));
        var type = Assert.IsType<NominalBoundType>(producer.Type);
        Assert.Equal("DirectoryInfo", type.RoslynSymbol?.Name);
        Assert.Equal(NullableAnnotation.Annotated, type.NullableAnnotation);
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Equal(BindingReceivingShape.Nominal, diagnostic.BindingContext?.Shape);
        Assert.Equal(producer.Span, diagnostic.Span);
        Assert.False(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    private static string NativeSource(string sourceType, string boundary)
    {
        var call = "§C{Foo.Get} §A input §/C";
        var body = boundary switch
        {
            "binding" => $"§B{{value:Foo}} {call}",
            "return" => $"§R {call}",
            "argument" => $"§R §C{{Foo.Consume}} §A {call} §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        var output = boundary switch
        {
            "binding" => "void",
            "return" => "Foo",
            _ => "i32"
        };
        return $$"""
            §M{m1:NominalNative}
              §CL{c1:Foo:pub}
                §MT{get:Get:pub:static} ({{sourceType}}:input) -> {{sourceType}}
                  §R input
                §MT{consume:Consume:pub:static} (Foo:input) -> i32
                  §R 0
                §MT{probe:Probe:pub:static} ({{sourceType}}:input) -> {{output}}
                  {{body}}
            """;
    }

    private static string ExpandedNativeSource(string boundary)
    {
        var body = boundary switch
        {
            "binding" => "§B{value:Foo} §C{Foo.Get} §/C",
            "return" => "§R §C{Foo.Get} §/C",
            _ => "§R §C{Foo.Consume} §A §C{Foo.Get} §/C §/C"
        };
        return $$"""
            §M{m1:ExpandedNative}
              §CL{c1:Foo:pub}
                §MT{get:Get:pub:static}
                  §O{?Foo}
                  §R §NEW{Foo}
                §MT{consume:Consume:pub:static} (Foo:value) -> i32
                  §R 0
                §MT{probe:Probe:pub:static} () -> {{(boundary == "binding" ? "void" : boundary == "return" ? "Foo" : "i32")}}
                  {{body}}
            """;
    }

    private static BoundCallExpression GetProducerCall(BoundModule module, string boundary)
    {
        var function = Assert.Single(module.Functions.Where(f =>
            f.Symbol.Name == "Probe" || f.Symbol.Name.EndsWith(".Probe", StringComparison.Ordinal)));
        var expression = Assert.Single(function.Body) switch
        {
            BoundBindStatement bind => bind.Initializer,
            BoundReturnStatement returned => returned.Expression,
            _ => throw new InvalidOperationException("Expected a binding or return.")
        };
        var call = Assert.IsType<BoundCallExpression>(expression);
        if (boundary == "argument")
        {
            Assert.NotNull(call.ResolvedSymbol);
            call = Assert.IsType<BoundCallExpression>(Assert.Single(call.Arguments));
        }
        return call;
    }

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source) => Bind(source, out _);

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source, out Binder binder)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        binder = new Binder(diagnostics);
        return (binder.Bind(module), diagnostics);
    }

    private static CSharpCompilation IdentityCompilation()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            namespace A {
                public class Foo { }
                public struct Value { }
                public enum Choice { First }
                public class Box<T> { }
            }
            namespace B { public class Foo { } }
            """);
        var compilation = CSharpCompilation.Create("IdentityFixtures",
            [tree], GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        return compilation;
    }

    private sealed class FixtureExpression(BoundType type) : BoundExpression(default)
    {
        public override BoundType Type => type;
    }
}
