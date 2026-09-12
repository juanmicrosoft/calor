using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Binding.Metadata;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using Xunit.Abstractions;
using Binder = Calor.Compiler.Binding.Binder;
using NullableAnnotation = Calor.Compiler.Binding.BoundTypes.NullableAnnotation;

namespace Calor.Compiler.Tests;

public class BclMemberAnnotationTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> ActualBclCases()
    {
        foreach (var boundary in new[] { "binding", "return", "argument" })
        {
            yield return ["System.Environment.ProcessPath", "", "str", true, boundary];
            yield return ["System.Environment.CurrentDirectory", "", "str", false, boundary];
            yield return ["error.HelpLink", "System.Exception:error", "str", true, boundary];
            yield return ["error.Message", "System.Exception:error", "str", false, boundary];
            yield return ["error.HelpLink", "System.ArgumentException:error", "str", true, boundary];
            yield return ["System.String.Empty", "", "str", false, boundary];
            yield return ["directory.Parent", "System.IO.DirectoryInfo:directory", "System.IO.DirectoryInfo", true, boundary];
            yield return ["directory.Root", "System.IO.DirectoryInfo:directory", "System.IO.DirectoryInfo", false, boundary];
            yield return ["Environment.ProcessPath", "", "str", true, boundary];
            yield return ["Environment.CurrentDirectory", "", "str", false, boundary];
            yield return ["error.HelpLink", "Exception:error", "str", true, boundary];
            yield return ["error . Message", "System.Exception:error", "str", false, boundary];
        }
    }

    [Theory]
    [MemberData(nameof(ActualBclCases))]
    public void ActualBclMember_ReachesConsumerWithResolvedType(
        string expression, string parameters, string receivingType, bool nullable, string boundary)
    {
        var source = Source(expression, parameters, receivingType, boundary);
        var (module, diagnostics, binder) = Bind(source);
        var member = Member(module, boundary);
        var type = AssertResolvedMember(member, Metadata(binder).Context, nullable);
        Assert.Equal(receivingType == "str" ? "String" : "DirectoryInfo", type.RoslynSymbol!.Name);
        Assert.Equal(expression.EndsWith("Empty", StringComparison.Ordinal) ? SymbolKind.Field : SymbolKind.Property,
            member.ResolvedMetadataMember!.Kind);
        if (expression.Contains("HelpLink", StringComparison.Ordinal))
            Assert.Equal("System.Exception", member.ResolvedMetadataMember.ContainingType.ToDisplayString());
        AssertConsumer(module, diagnostics, member, boundary, nullable);
    }

    [Theory]
    [MemberData(nameof(ActualBclCases))]
    public void ActualBclMember_ProductionRetainsRoutingAndEffectGates(
        string expression, string parameters, string receivingType, bool nullable, string boundary)
    {
        var source = Source(expression, parameters, receivingType, boundary);
        var result = Program.Compile(source, "bcl-members.calr");
        var hasUnmodeledEffect = receivingType != "str"
            && (expression.EndsWith(".Root", StringComparison.Ordinal) || boundary == "argument");
        Assert.Equal(hasUnmodeledEffect, result.HasErrors);
        Assert.All(result.Diagnostics.Errors, d => Assert.Equal("Calor0410", d.Code));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == Code(boundary));
        var (_, raw, _) = Bind(source);
        Assert.Equal(nullable, raw.Any(d => d.Code == Code(boundary)));
        // Default effect rejection above is retained, not labeled as nullability coverage.
        var withoutEffects = Program.Compile(source, "bcl-members.calr", new CompilationOptions { EnforceEffects = false });
        Assert.False(withoutEffects.HasErrors, string.Join("; ", withoutEffects.Diagnostics));
        var validation = GeneratedCSharpCompiler.Validate(withoutEffects.GeneratedCode);
        Assert.True(validation.CompilationSuccess, string.Join("; ", validation.CompilationErrors));
    }

    public static IEnumerable<object[]> ControlledCases()
    {
        foreach (var boundary in new[] { "binding", "return", "argument" })
        foreach (var member in new[] { "Field", "Property", "DirectoryField", "DirectoryProperty" })
        foreach (var nullable in new[] { false, true })
        foreach (var isStatic in new[] { false, true })
            yield return [member, nullable, isStatic, boundary];
    }

    [Theory]
    [MemberData(nameof(ControlledCases))]
    public void ControlledAnnotatedReferenceFixture_PreservesMemberAndAnnotation(
        string memberName, bool nullable, bool isStatic, string boundary)
    {
        var member = (isStatic ? "Static" : "") + (nullable ? "Nullable" : "NonNull") + memberName;
        var expression = $"{(isStatic ? "N5Fixture.Derived" : "value")}.{member}";
        var receivingType = memberName.StartsWith("Directory", StringComparison.Ordinal) ? "System.IO.DirectoryInfo" : "str";
        var source = Source(expression, isStatic ? "" : "N5Fixture.Derived:value", receivingType, boundary);
        var context = FixtureContext.Value;
        var (module, diagnostics, _) = Bind(source, context);
        var boundMember = Member(module, boundary);
        AssertResolvedMember(boundMember, context, nullable);
        Assert.Equal("N5Fixture.Members", boundMember.ResolvedMetadataMember!.ContainingType.ToDisplayString());
        Assert.Equal("CalorN5AnnotatedFixture", boundMember.ResolvedMetadataMember.ContainingAssembly.Name);
        Assert.Equal(isStatic, boundMember.ResolvedMetadataMember.IsStatic);
        Assert.Equal(memberName.EndsWith("Field", StringComparison.Ordinal) ? SymbolKind.Field : SymbolKind.Property,
            boundMember.ResolvedMetadataMember.Kind);
        AssertConsumer(module, diagnostics, boundMember, boundary, nullable);
    }

    [Theory]
    [InlineData("Field", "str", "binding")]
    [InlineData("Field", "str", "return")]
    [InlineData("Field", "str", "argument")]
    [InlineData("Property", "str", "binding")]
    [InlineData("Property", "str", "return")]
    [InlineData("Property", "str", "argument")]
    [InlineData("DirectoryField", "System.IO.DirectoryInfo", "binding")]
    [InlineData("DirectoryProperty", "System.IO.DirectoryInfo", "return")]
    public void ControlledMissingAnnotations_RemainOblivious(string member, string receivingType, string boundary)
    {
        var (module, diagnostics, _) = Bind(
            Source($"value.{member}", "N5Fixture.Legacy:value", receivingType, boundary), FixtureContext.Value);
        var boundMember = Member(module, boundary);
        var type = Assert.IsType<NominalBoundType>(boundMember.Type);
        Assert.NotNull(boundMember.ResolvedMetadataMember);
        Assert.Equal(Microsoft.CodeAnalysis.NullableAnnotation.None, type.RoslynSymbol!.NullableAnnotation);
        Assert.Equal(NullableAnnotation.Oblivious, type.NullableAnnotation);
        // Existing scalar policy is conservative; nominal Oblivious is not widened here.
        Assert.Equal(receivingType == "str", diagnostics.Any(d => d.Code == Code(boundary)));
    }

    [Theory]
    [InlineData("N5Fixture.Members.NullableField", "", "CS0120")]
    [InlineData("value.StaticNullableField", "N5Fixture.Members:value", "CS0176")]
    [InlineData("value.PrivateField", "N5Fixture.Members:value", "PrivateField")]
    [InlineData("value.PrivateGetter", "N5Fixture.Members:value", "PrivateGetter")]
    [InlineData("value.WriteOnly", "N5Fixture.Members:value", "CS0154")]
    [InlineData("value.Missing", "N5Fixture.Members:value", "CS1061")]
    [InlineData("value.ValueField", "N5Fixture.Members:value", "outside supported scalar")]
    [InlineData("value.ArrayField", "N5Fixture.Members:value", "outside supported scalar")]
    [InlineData("value.GenericField", "N5Fixture.Members:value", "outside supported scalar")]
    public void KnownExternalUnsupportedRead_IsUnresolvedNotSafe(string expression, string parameters, string reason)
    {
        var (module, diagnostics, _) = Bind(Source(expression, parameters, "str", "binding"), FixtureContext.Value);
        var member = Member(module, "binding");
        Assert.IsType<UnresolvedBoundType>(member.Type);
        Assert.Null(member.ResolvedMetadataMember);
        var diagnostic = Assert.Single(diagnostics.Where(d => d.Code == DiagnosticCode.SignatureUnresolved));
        Assert.Contains(reason, diagnostic.Message);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
    }

    [Theory]
    [InlineData("Members.NullableField", "")]
    [InlineData("missing.NullableField", "")]
    [InlineData("value.NullableField", "Members:value")]
    [InlineData("value.NullableField", "Missing.Type:value")]
    public void UnresolvedReceiver_RetainsExistingResolutionDiagnostic(string expression, string parameters)
    {
        var (module, diagnostics, _) = Bind(Source(expression, parameters, "str", "binding"), FixtureContext.Value);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.UndefinedReference);
        var value = Assert.IsType<BoundBindStatement>(Assert.Single(Assert.Single(module.Functions).Body)).Initializer;
        Assert.Null(Assert.IsType<NominalBoundType>(value!.Type).RoslynSymbol);
    }

    [Fact]
    public void ValueReceiverShadowingMappedType_DoesNotBecomeStaticMetadataAccess()
    {
        var (module, diagnostics, _) = Bind(Source(
            "Environment.ProcessPath", "System.Exception:Environment", "str", "binding"));
        var member = Member(module, "binding");
        Assert.IsType<BoundVariableExpression>(member.Target);
        Assert.IsType<UnresolvedBoundType>(member.Type);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.SignatureUnresolved);
    }

    [Fact]
    public void ActualMetadataProfile_IsTheBindingHostReferenceSet()
    {
        var (module, _, binder) = Bind(Source("System.Environment.ProcessPath", "", "str", "binding"));
        var context = Metadata(binder).Context;
        AssertResolvedMember(Member(module, "binding"), context, true);
        foreach (var reference in context.HostCompilationForBinder.References.OfType<PortableExecutableReference>()
                     .OrderBy(r => r.FilePath, StringComparer.Ordinal))
        {
            Assert.NotNull(reference.FilePath);
            using var stream = File.OpenRead(reference.FilePath);
            output.WriteLine($"{Path.GetFileName(reference.FilePath)}\t{Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()}");
        }
    }

    [Fact]
    public void MemberProbe_DoesNotSubstituteSameNamedTypeFromAnotherAssembly()
    {
        var context = FixtureContext.Value;
        var other = CSharpCompilation.Create("OtherAssembly",
            [CSharpSyntaxTree.ParseText("namespace N5Fixture { public class Members { public string NullableField = \"different\"; } }")],
            MetadataContext.Create().HostCompilationForBinder.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var foreignReceiver = other.GetTypeByMetadataName("N5Fixture.Members");
        Assert.NotNull(foreignReceiver);
        var member = new MetadataBinder(context).ResolveMemberRead(foreignReceiver, "NullableField", false, out var reason);
        Assert.Null(member);
        Assert.Contains("identity", reason);
    }

    private static NominalBoundType AssertResolvedMember(
        BoundFieldAccessExpression member, MetadataContext context, bool nullable)
    {
        Assert.NotNull(member.ResolvedMetadataMember);
        var declaredType = member.ResolvedMetadataMember switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => throw new InvalidOperationException("Expected a property or field symbol")
        };
        var type = Assert.IsType<NominalBoundType>(member.Type);
        Assert.NotNull(type.RoslynSymbol);
        Assert.True(type.RoslynSymbol.IsReferenceType);
        Assert.True(SymbolEqualityComparer.IncludeNullability.Equals(declaredType, type.RoslynSymbol));
        Assert.Equal(nullable ? NullableAnnotation.Annotated : NullableAnnotation.NotAnnotated,
            type.NullableAnnotation);
        var compilation = context.HostCompilationForBinder;
        Assert.Single(compilation.References.Where(reference => SymbolEqualityComparer.Default.Equals(
            compilation.GetAssemblyOrModuleSymbol(reference), member.ResolvedMetadataMember.ContainingAssembly)));
        return type;
    }

    private static void AssertConsumer(
        BoundModule module, DiagnosticBag diagnostics, BoundFieldAccessExpression member, string boundary, bool nullable)
    {
        var findings = diagnostics.Where(d => d.Code == Code(boundary)).ToArray();
        if (nullable)
        {
            var diagnostic = Assert.Single(findings);
            Assert.Equal(member.Span, diagnostic.Span);
            Assert.False(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        }
        else
            Assert.Empty(findings);
        Assert.DoesNotContain(diagnostics, d => d.Code is
            DiagnosticCode.UndefinedReference or DiagnosticCode.NoMatchingOverload);
        if (boundary == "argument")
        {
            var call = Consumer(module);
            Assert.NotNull(Assert.IsType<NominalBoundType>(call.Type).RoslynSymbol);
        }
    }

    private static readonly Lazy<MetadataContext> FixtureContext = new(() =>
    {
        var context = MetadataContext.Create();
        var host = context.HostCompilationForBinder;
        var fixture = CSharpCompilation.Create("CalorN5AnnotatedFixture",
            [CSharpSyntaxTree.ParseText(FixtureSource)], host.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = fixture.Emit(stream);
        Assert.True(emitted.Success, string.Join("; ", emitted.Diagnostics));
        var reference = MetadataReference.CreateFromImage(stream.ToArray());
        var references = host.References.Append(reference).ToImmutableArray();
        var constructor = typeof(MetadataContext).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (MetadataContext)constructor.Invoke(
            [host.AddReferences(reference), references, MetadataReferenceManifest.Load()]);
    });

    private const string FixtureSource = """
        #nullable enable
        namespace N5Fixture {
          public class Members {
            public string? NullableField;
            public string NonNullField = "present";
            public static string? StaticNullableField;
            public static string StaticNonNullField = "present";
            public string? NullableProperty { get; }
            public string NonNullProperty { get; } = "present";
            public static string? StaticNullableProperty { get; }
            public static string StaticNonNullProperty { get; } = "present";
            public System.IO.DirectoryInfo? NullableDirectoryField;
            public System.IO.DirectoryInfo NonNullDirectoryField = new("/");
            public static System.IO.DirectoryInfo? StaticNullableDirectoryField;
            public static System.IO.DirectoryInfo StaticNonNullDirectoryField = new("/");
            public System.IO.DirectoryInfo? NullableDirectoryProperty { get; }
            public System.IO.DirectoryInfo NonNullDirectoryProperty { get; } = new("/");
            public static System.IO.DirectoryInfo? StaticNullableDirectoryProperty { get; }
            public static System.IO.DirectoryInfo StaticNonNullDirectoryProperty { get; } = new("/");
            private string PrivateField = "private";
            public string PrivateGetter { private get; set; } = "private";
            public string WriteOnly { set { } }
            public int ValueField;
            public string[] ArrayField = [];
            public System.Collections.Generic.List<string> GenericField = [];
          }
          public class Derived : Members { }
        #nullable disable
          public class Legacy {
            public string Field;
            public string Property { get; }
            public System.IO.DirectoryInfo DirectoryField;
            public System.IO.DirectoryInfo DirectoryProperty { get; }
          }
        }
        """;

    private static string Code(string boundary) => boundary switch
    {
        "binding" => DiagnosticCode.NullableToNonNullableBinding,
        "return" => DiagnosticCode.NullableReturnFromNonNullable,
        "argument" => DiagnosticCode.NullableArgumentToNonNullableParameter,
        _ => throw new ArgumentOutOfRangeException(nameof(boundary))
    };

    private static string Source(string expression, string parameters, string receivingType, string boundary)
    {
        var consumer = receivingType == "str"
            ? "System.Int32.Parse"
            : "System.IO.FileSystemAclExtensions.GetAccessControl";
        var body = boundary switch
        {
            "binding" => $"§B{{value:{receivingType}}} {expression}",
            "return" => $"§R {expression}",
            "argument" => $"§B{{result}} §C{{{consumer}}} §A {expression} §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        return $$"""
            §M{m1:BclMembers}
              §F{probe:Probe:pub} ({{parameters}}) -> {{(boundary == "return" ? receivingType : "void")}}
                {{body}}
            """;
    }

    private static (BoundModule Module, DiagnosticBag Diagnostics, Binder Binder) Bind(
        string source, MetadataContext? context = null)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("; ", diagnostics));
        var binder = new Binder(diagnostics);
        if (context is not null)
            typeof(Binder).GetField("_metadataBinder", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(binder, new MetadataBinder(context));
        return (binder.Bind(module), diagnostics, binder);
    }

    private static MetadataBinder Metadata(Binder binder) =>
        Assert.IsType<MetadataBinder>(typeof(Binder)
            .GetField("_metadataBinder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(binder));

    private static BoundCallExpression Consumer(BoundModule module) =>
        Assert.IsType<BoundCallExpression>(
            Assert.IsType<BoundBindStatement>(Assert.Single(Assert.Single(module.Functions).Body)).Initializer);

    private static BoundFieldAccessExpression Member(BoundModule module, string boundary)
    {
        var statement = Assert.Single(Assert.Single(module.Functions).Body);
        var expression = boundary switch
        {
            "binding" => Assert.IsType<BoundBindStatement>(statement).Initializer,
            "return" => Assert.IsType<BoundReturnStatement>(statement).Expression,
            "argument" => Assert.Single(Consumer(module).Arguments),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        return Assert.IsType<BoundFieldAccessExpression>(expression);
    }
}
