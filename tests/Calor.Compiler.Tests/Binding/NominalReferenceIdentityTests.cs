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
    public void ObliviousNominalReference_IsPossiblyNullAfterStageBPolicyAdoption()
    {
        var symbol = IdentityCompilation().GetTypeByMetadataName("A.Foo");
        Assert.NotNull(symbol);
        var target = new NominalBoundType("A.Foo", NullableAnnotation.NotAnnotated, roslynSymbol: symbol);
        var source = new NominalBoundType("A.Foo", NullableAnnotation.Oblivious, roslynSymbol: symbol);
        Assert.True(source.HasSameUnderlyingReferenceType(target));
        Assert.True(NullabilityChecker.IsPossiblyNullAssignedTo(new FixtureExpression(source), target));
    }

    [Theory]
    [InlineData("let Exception:copy = ex")]
    [InlineData("return ex")]
    [InlineData("return Take(ex)")]
    public void CatchVariables_AreKnownNonNullAcrossReceivingBoundaries(string statement)
    {
        var source = $$"""
            module CatchNullability
              func Take public (Exception:value) → Exception
                !
                return value
              func Probe public () → Exception
                !
                try
                  System.Console.WriteLine("try")
                catch Exception:ex
                  {{statement}}
            """;
        var (_, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, d => d.Code is
            DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable
            or DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Theory]
    [InlineData("let System.IO.MemoryStream:copy = stream")]
    [InlineData("return stream")]
    [InlineData("return TakeStream(stream)")]
    public void InferredUsingResources_PreserveNonNullInitializerIdentity(string statement)
    {
        var source = $$"""
            module UsingNullability
              func TakeStream public (System.IO.MemoryStream:value) → System.IO.MemoryStream
                !
                return value
              func Probe public () → System.IO.MemoryStream
                !
                using stream = new System.IO.MemoryStream()
                  {{statement}}
            """;
        var (_, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, d => d.Code is
            DiagnosticCode.NullableToNonNullableBinding
            or DiagnosticCode.NullableReturnFromNonNullable
            or DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Theory]
    [InlineData("other ?? this")]
    [InlineData("flag ? throw this : throw other")]
    public void ThisExpression_IsKnownNonNullThroughReferenceJoins(string expression)
    {
        var source = $$"""
            module ThisNullability
              class Foo public
                func Probe public (Foo?:other, bool:flag) → Foo
                  !
                  return {{expression}}
            """;
        var (_, diagnostics) = Bind(source);
        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Fact]
    public void ThisAndBaseExpressions_AreDeclaredNonNull()
    {
        Assert.Equal(NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(new BoundThisExpression(default, "Foo").Type).NullableAnnotation);
        Assert.Equal(NullableAnnotation.NotAnnotated,
            Assert.IsType<NominalBoundType>(new BoundBaseExpression(default, "BaseFoo").Type).NullableAnnotation);
    }

    [Fact]
    public void ExplicitNullableUsingResource_RemainsNullable()
    {
        const string source = """
            module NullableUsing
              func Probe public (System.IO.MemoryStream?:input) → void
                !
                using System.IO.MemoryStream?:stream = input
                  let System.IO.MemoryStream:copy = stream
            """;
        var (_, diagnostics) = Bind(source);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && BindingDiagnosticPolicy.IsCompilationError(d));
    }

    [Theory]
    [InlineData("Resource?")]
    [InlineData("System.IDisposable?")]
    [InlineData("System.IO.MemoryStream?")]
    public void NullableUsingHeader_PreservesTypeNameAndBodyAcrossNativeReparse(string typeName)
    {
        var source = $$"""
            module NullableResourceHeader
              class Resource public
              func Probe public ({{typeName}}:input) → void
                !
                using {{typeName}}:stream = input
                  let copy = stream
            """;
        var parsed = Parse(source, new DiagnosticBag());
        foreach (var module in new[] { parsed, Parse(new Calor.Compiler.Migration.CalorEmitter().Emit(parsed), new DiagnosticBag()) })
        {
            var resource = Assert.IsType<UsingStatementNode>(Assert.Single(module.Functions.Single().Body));
            Assert.Equal(typeName, resource.VariableType);
            Assert.Equal("stream", resource.VariableName);
            Assert.Equal("input", Assert.IsType<ReferenceNode>(resource.Resource).Name);
            var copy = Assert.IsType<BindStatementNode>(Assert.Single(resource.Body));
            Assert.Equal("stream", Assert.IsType<ReferenceNode>(copy.Initializer).Name);
        }
    }

    [Theory]
    [InlineData("flag ? first : second")]
    [InlineData("first ?? second")]
    public void UsingResourceExpressions_AreNotMisreadAsNullableDeclarationHeaders(string expression)
    {
        var source = $$"""
            module ResourceExpressions
              func Probe public (bool:flag, System.IDisposable:first, System.IDisposable:second) → void
                !
                using {{expression}}
                  let copy = first
            """;
        var parsed = Parse(source, new DiagnosticBag());
        var resource = Assert.IsType<UsingStatementNode>(Assert.Single(parsed.Functions.Single().Body));
        Assert.Null(resource.VariableType);
        Assert.Null(resource.VariableName);
        Assert.Equal(expression, source.Substring(resource.Resource.Span.Start, resource.Resource.Span.Length));
        Assert.Single(resource.Body);
    }

    [Fact]
    public void ExplicitNonNullableUsingResource_RejectsNullableInitializer()
    {
        const string source = """
            module NullableUsingInitializer
              func Probe public (System.IO.MemoryStream?:input) → void
                !
                using System.IO.MemoryStream:stream = input
                  let System.IO.MemoryStream?:copy = stream
            """;
        var (_, diagnostics) = Bind(source);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableToNonNullableBinding
            && BindingDiagnosticPolicy.IsCompilationError(d));
    }

    [Fact]
    public void InferredNullableUsingResource_RetainsNativeTypeForArgumentValidation()
    {
        const string source = """
            module InferredNullableUsing
              func Take public (System.IO.MemoryStream:value) → System.IO.MemoryStream
                !
                return value
              func Probe public (System.IO.MemoryStream?:input) → System.IO.MemoryStream
                !
                using stream = input
                  return Take(stream)
            """;
        var (_, diagnostics) = Bind(source);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
            && BindingDiagnosticPolicy.IsCompilationError(d));
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
    [InlineData("A.Foo", true, "Foo")]
    [InlineData("B.Foo", false, "Foo")]
    [InlineData("Foo", false, "Container")]
    public void NativeReturn_ResolvesDeclarationInCalleeNamespace(
        string receivingType, bool expected, string callerClassName)
    {
        const string sourceA = """
            module Namespaces A
              class Foo public
                func Get public static () → Foo?
                  !
                  return new Foo()
            """;
        var sourceB = $$"""
            module Namespaces B
              class {{callerClassName}} public
                func Probe public static () → void
                  !
                  let {{receivingType}}:value = A.Foo.Get()
            """;
        var diagnostics = new DiagnosticBag();
        var parsedA = new Parser(new Lexer(sourceA, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        var parsedB = new Parser(new Lexer(sourceB, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors);
        var classA = Assert.Single(parsedA.Classes);
        var classB = Assert.Single(parsedB.Classes);
        Assert.Equal("Foo", classA.Name);
        Assert.Equal(callerClassName, classB.Name);
        Assert.Equal("A", classA.NamespaceIdentity);
        Assert.Equal("B", classB.NamespaceIdentity);
        classA.NamespaceScopeId = null;
        classB.NamespaceScopeId = null;
        classA.Methods.Single().NamespaceScopeId = null;
        classB.Methods.Single().NamespaceScopeId = null;
        var parsed = new ModuleNode(parsedA.Span, parsedA.Id, parsedA.Name,
            parsedA.Usings, parsedA.Interfaces, [classA, classB], parsedA.Functions, parsedA.Attributes);
        var bound = new Binder(diagnostics).Bind(parsed);
        var producer = GetProducerCall(bound, "binding");
        Assert.True(producer.ResolvedSymbol is not null,
            $"Target: {producer.Target}; functions: {string.Join(", ", bound.Functions.Select(f => f.Symbol.Name))}; "
            + $"diagnostics: {string.Join("; ", diagnostics.Select(d => $"{d.Code}: {d.Message}"))}");
        var returned = Assert.IsType<NominalBoundType>(producer.Type);
        Assert.Equal("A.Foo", returned.Declaration?.QualifiedName);
        Assert.Equal("Foo?", returned.DisplayString);
        Assert.Equal(expected, diagnostics.Any(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
    }

    [Theory]
    [InlineData("global::Foo?", "binding", DiagnosticCode.NullableToNonNullableBinding)]
    [InlineData("Foo?", "binding", DiagnosticCode.NullableToNonNullableBinding)]
    [InlineData("global::Foo?", "return", DiagnosticCode.NullableReturnFromNonNullable)]
    [InlineData("Foo?", "return", DiagnosticCode.NullableReturnFromNonNullable)]
    [InlineData("global::Foo?", "argument", DiagnosticCode.NullableArgumentToNonNullableParameter)]
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
        var source = NativeSource("Foo?", "binding")
            .Replace("public static", "public", StringComparison.Ordinal)
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
        var (module, diagnostics) = Bind(ExpandedNativeModule(boundary));
        var producer = GetProducerCall(module, boundary);
        Assert.NotNull(producer.ResolvedSymbol);
        var type = Assert.IsType<NominalBoundType>(producer.Type);
        Assert.Equal("NULLABLE[inner=Foo]", type.DisplayString);
        Assert.Equal(NullableAnnotation.Annotated, type.NullableAnnotation);
        Assert.NotNull(type.Declaration);
        Assert.Single(diagnostics.Where(d => d.Code == code));
    }

    [Fact]
    public void ExpandedNativeArgument_KeepsExistingApplicabilityRejection()
    {
        var (module, diagnostics) = Bind(ExpandedNativeModule("argument"));
        var probe = Assert.Single(module.Functions.Where(f => f.Symbol.Name.EndsWith(".Probe", StringComparison.Ordinal)));
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(Assert.Single(probe.Body)).Expression);
        Assert.Null(call.ResolvedSymbol);
        var producer = Assert.IsType<BoundCallExpression>(Assert.Single(call.Arguments));
        Assert.NotNull(producer.ResolvedSymbol);
        Assert.Equal("NULLABLE[inner=Foo]", producer.Type.DisplayString);
        Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.NoMatchingOverload));
    }

    [Fact]
    public void InferredObjectFallback_DoesNotGainResolvedReferenceIdentity()
    {
        const string source = """
            module Unknown
              func Probe public () → void
                !
                let value = Missing()
                let copy = value
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
    [InlineData("Directory.NotARealMethod")]
    [InlineData("directory.NotARealMethod")]
    public void UnsupportedBclReceiver_IsNotRelabeledResolved(string target)
    {
        var source = $$"""
            module UnsupportedReceiver
              using System.IO
              func Probe public (System.IO.DirectoryInfo:directory) → void
                !
                let System.IO.DirectoryInfo:value = {{target}}("/")
            """;
        var (module, diagnostics) = Bind(source);
        var call = GetProducerCall(module, "binding");
        Assert.Null(call.ResolvedSymbol);
        var type = Assert.IsType<NominalBoundType>(call.Type);
        Assert.Null(type.RoslynSymbol);
        Assert.Null(type.Declaration);
        Assert.False(type.IsKnownReferenceType);
        Assert.Null(call.ResolvedMetadataMethod);
        Assert.True(call.HasUnknownInferredType);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableToNonNullableBinding);
    }

    [Theory]
    [InlineData("Directory.GetParent", true)]
    [InlineData("directory.CreateSubdirectory", false)]
    public void SupportedBclReceivers_KeepResolvedReturnIdentity(string target, bool nullable)
    {
        var source = $$"""
            module SupportedReceiver
              using System.IO
              func Probe public (System.IO.DirectoryInfo:directory) → void
                !
                let System.IO.DirectoryInfo:value = {{target}}("/")
            """;
        var (module, diagnostics) = Bind(source);
        var call = GetProducerCall(module, "binding");
        var type = Assert.IsType<NominalBoundType>(call.Type);
        Assert.NotNull(type.RoslynSymbol);
        Assert.Equal("System.IO.DirectoryInfo", type.RoslynSymbol.ToDisplayString().TrimEnd('?'));
        Assert.True(type.IsKnownReferenceType);
        Assert.Equal(nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            type.NullableAnnotation);
        var method = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IMethodSymbol>(call.ResolvedMetadataMethod);
        Assert.Equal(target[(target.LastIndexOf('.') + 1)..], method.Name);
        Assert.True(Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
            type.RoslynSymbol, method.ReturnType));
        Assert.Equal(nullable, diagnostics.Any(d => d.Code == DiagnosticCode.NullableToNonNullableBinding));
    }

    [Theory]
    [InlineData("Foo?")]
    [InlineData("Foo")]
    public void ProductionNominalRouting_ActivatesOnlyForPossiblyNullSource(string sourceType)
    {
        var source = NativeSource(sourceType, "return");
        var result = Program.Compile(source, "nominal-identity.calr");
        var (_, raw) = Bind(source);
        var shouldReject = sourceType.EndsWith('?');
        Assert.Equal(shouldReject,
            raw.Any(d => d.Code == DiagnosticCode.NullableReturnFromNonNullable));
        Assert.Equal(shouldReject, result.HasErrors);
        Assert.Equal(shouldReject, result.Diagnostics.Any(
            d => d.Code == DiagnosticCode.NullableReturnFromNonNullable
                && BindingDiagnosticPolicy.IsCompilationError(d)));
        if (!shouldReject)
            Assert.True(GeneratedCSharpCompiler.Validate(result.GeneratedCode).CompilationSuccess);
    }

    [Theory]
    [InlineData("GetParent", "binding", true)]
    [InlineData("GetParent", "return", true)]
    [InlineData("CreateDirectory", "binding", false)]
    [InlineData("CreateDirectory", "return", false)]
    public void BclNominalReturn_ReachesConsumerWithActualResolvedSymbol(
        string method, string boundary, bool nullable)
    {
        var call = $"System.IO.Directory.{method}(\"/\")";
        var body = boundary == "binding"
            ? $"let System.IO.DirectoryInfo:value = {call}"
            : $"return {call}";
        var source = $$"""
            module NominalMetadata
              func Probe public () → {{(boundary == "binding" ? "void" : "System.IO.DirectoryInfo")}}
                !
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
            module NominalBclArgument
              func Probe public () → void
                !
                let System.Security.AccessControl.DirectorySecurity:permissions = System.IO.FileSystemAclExtensions.GetAccessControl(System.IO.Directory.GetParent("/"))
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
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    private static string NativeSource(string sourceType, string boundary)
    {
        var call = "Foo.Get(input)";
        var body = boundary switch
        {
            "binding" => $"let Foo:value = {call}",
            "return" => $"return {call}",
            "argument" => $"return Foo.Consume({call})",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        var output = boundary switch
        {
            "binding" => "void",
            "return" => "Foo",
            _ => "int"
        };
        return $$"""
            module NominalNative
              class Foo public
                func Get public static ({{sourceType}}:input) → {{sourceType}}
                  !
                  return input
                func Consume public static (Foo:input) → int
                  !
                  return 0
                func Probe public static ({{sourceType}}:input) → {{output}}
                  !
                  {{body}}
            """;
    }

    private static ModuleNode ExpandedNativeModule(string boundary)
    {
        var body = boundary switch
        {
            "binding" => "let Foo:value = Foo.Get()",
            "return" => "return Foo.Get()",
            _ => "return Foo.Consume(Foo.Get())"
        };
        var source = $$"""
            module ExpandedNative
              class Foo public
                func Get public static () → Foo?
                  !
                  return new Foo()
                func Consume public static (Foo:value) → int
                  !
                  return 0
                func Probe public static () → {{(boundary == "binding" ? "void" : boundary == "return" ? "Foo" : "int")}}
                  !
                  {{body}}
            """;
        var module = Parse(source, new DiagnosticBag());
        var original = Assert.Single(module.Classes);
        var getter = original.Methods.Single(method => method.Name == "Get");
        // Expanded spellings remain internal AST compatibility cases, not active source syntax.
        var expanded = getter.CopyMetadataTo(new MethodNode(
            getter.Span, getter.LocalId, getter.Name, getter.Visibility, getter.Modifiers,
            getter.TypeParameters, getter.Parameters, new OutputNode(getter.Output!.Span, "NULLABLE[inner=Foo]"),
            getter.Effects, getter.Preconditions, getter.Postconditions, getter.Body, getter.Attributes));
        var replacement = original.CopyMetadataTo(new ClassDefinitionNode(
            original.Span, original.LocalId, original.Name, original.IsAbstract, original.IsSealed,
            original.BaseClass, original.ImplementedInterfaces, original.TypeParameters, original.Fields,
            original.Methods.Select(method => method == getter ? expanded : method).ToArray(),
            original.Attributes));
        return module.With(update =>
        {
            update.Classes = [replacement];
            update.Items = [replacement];
        });
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

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(ModuleNode module)
    {
        var diagnostics = new DiagnosticBag();
        return (new Binder(diagnostics).Bind(module), diagnostics);
    }

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source, out Binder binder)
    {
        var diagnostics = new DiagnosticBag();
        var module = Parse(source, diagnostics);
        binder = new Binder(diagnostics);
        return (binder.Bind(module), diagnostics);
    }

    private static ModuleNode Parse(string source, DiagnosticBag diagnostics)
    {
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics));
        return module;
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
