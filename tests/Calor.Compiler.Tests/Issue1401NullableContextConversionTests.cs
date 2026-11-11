using System.Reflection;
using System.Diagnostics;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Migration.Project;
using Calor.Compiler.Formatting;
using Calor.Compiler.Binding.BoundTypes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class Issue1401NullableContextConversionTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" overflow unchecked")]
    [InlineData(" overflow checked")]
    public void NativeObliviousModule_RoundTripsDeclarationMetadata(string overflow)
    {
        var source = $$"""
            module Oblivious{{overflow}} nullable oblivious
              module Fixture public
                func Echo public (string:value) → string
                  !
                  return value
                func Missing public () → string
                  !
                  return null
                func Explicit public (string?:value) → string?
                  !
                  return value
                func Nested public (System.Collections.Generic.List<string?[]>[]:value) → System.Collections.Generic.List<string?[]>[]
                  !
                  return value
            """;
        const string original = """
            public static class Fixture {
                public static string Echo(string value) => value;
                public static string Missing() => null;
                public static string? Explicit(string? value) => value;
                public static System.Collections.Generic.List<string?[]>[] Nested(
                    System.Collections.Generic.List<string?[]>[] value) => value;
            }
            """;
        var compiled = Program.Compile(source, "oblivious.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        var emitted = new CalorEmitter().Emit(compiled.Ast!);
        Assert.Contains("nullable oblivious", emitted);
        var formatted = new CalorFormatter().FormatSource(emitted, "oblivious.calr");
        Assert.True(formatted.Success, string.Join("\n", formatted.Errors));
        Assert.Contains("nullable oblivious", formatted.Formatted);
        var reparsed = Program.Compile(formatted.Formatted, "oblivious-reparsed.calr");
        Assert.False(reparsed.HasErrors, string.Join("\n", reparsed.Diagnostics.Errors));
        Assert.Equal(DeclaredMetadata(original), DeclaredMetadata(reparsed.GeneratedCode));
        var warningsAsErrors = GeneratedCSharpCompiler.Validate(
            [new(reparsed.GeneratedCode, "oblivious.g.cs")],
            new GeneratedCSharpCompilationContext { TreatWarningsAsErrors = true });
        Assert.True(warningsAsErrors.CompilationSuccess,
            string.Join("\n", warningsAsErrors.CompilationErrors));
        var annotationWarning = GeneratedCSharpCompiler.Validate(
            [new(reparsed.GeneratedCode.Replace("#pragma warning disable CS8669", "", StringComparison.Ordinal),
                "oblivious.g.cs")],
            new GeneratedCSharpCompilationContext { TreatWarningsAsErrors = true });
        Assert.False(annotationWarning.CompilationSuccess);
        Assert.All(annotationWarning.CompilationErrors, diagnostic => Assert.Equal("CS8669", diagnostic.Id));
        Assert.Equal(emitted, new CalorEmitter().Emit(reparsed.Ast!));
    }

    [Theory]
    [InlineData("nullable enabled")]
    [InlineData("nullable oblivious nullable oblivious")]
    [InlineData("nullable")]
    public void NativeObliviousModule_RejectsInvalidHeaderPolicies(string policy)
    {
        var result = Program.Compile($"module Invalid {policy}\n  func Main () → void\n    !\n");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void NativeObliviousModule_EscapesNamespaceNamedLikePolicy()
    {
        var result = Program.Compile("""
            module File @nullable nullable oblivious
              module Fixture public
                func Echo public (string:value) → string
                  !
                  return value
            """);
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        var emitted = new CalorEmitter().Emit(result.Ast!);
        Assert.Contains("module File @nullable nullable oblivious", emitted);
        var reparsed = Program.Compile(emitted);
        Assert.False(reparsed.HasErrors, string.Join("\n", reparsed.Diagnostics.Errors));
        Assert.Equal(DeclaredMetadata("namespace nullable { public static class Fixture { public static string Echo(string value) => value; } }"),
            DeclaredMetadata(reparsed.GeneratedCode));
    }

    [Fact]
    public void NativeObliviousModule_DoesNotChangeDefaultOrReceivingChecks()
    {
        var normal = Program.Compile("""
            module Checked
              func Missing () → string
                !
                return null
            """);
        Assert.Contains(normal.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
        foreach (var guard in new[] { "", "if value != null\n      " })
        {
            var result = Program.Compile($$"""
                module Oblivious nullable oblivious
                  func Probe (string:value) → void
                    !
                    {{guard}}System.Int32.Parse(value)
                """);
            Assert.Equal(guard.Length == 0, result.HasErrors);
            Assert.Equal(guard.Length == 0, result.Diagnostics.Errors.Any(
                diagnostic => diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        }
    }

    [Theory]
    [InlineData("string[]", "value[0]", "Oblivious")]
    [InlineData("System.Collections.Generic.List<string>", "value[0]", "Oblivious")]
    [InlineData("System.Collections.Generic.List<string?[]>[]", "value[0][0][0]", "Annotated")]
    public void NativeObliviousModule_NestedReadsRetainUncertainty(string type, string expression, string annotation)
    {
        var source = $$"""
            module Nested nullable oblivious
              func Probe ({{type}}:value) → int
                !
                return System.Int32.Parse({{expression}})
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics.Errors.Where(
            item => item.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        Assert.Contains(annotation, diagnostic.Message);
        if (type.Contains("List<string?[]>", StringComparison.Ordinal))
        {
            var diagnostics = new DiagnosticBag();
            var ast = new Calor.Compiler.Parsing.Parser(
                new Calor.Compiler.Parsing.Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
            var bound = new Calor.Compiler.Binding.Binder(diagnostics).Bind(ast);
            var outer = Assert.IsType<ArrayBoundType>(bound.Functions.Single().Symbol.Parameters.Single().MethodInputArrayType);
            var generic = Assert.IsType<GenericInstantiationBoundType>(outer.ElementType);
            var inner = Assert.IsType<ArrayBoundType>(Assert.Single(generic.TypeArguments));
            Assert.Equal("Oblivious", outer.NullableAnnotation.ToString());
            Assert.Equal("Oblivious", generic.NullableAnnotation.ToString());
            Assert.Equal("Oblivious", inner.NullableAnnotation.ToString());
            Assert.Equal("Annotated", Assert.IsType<NominalBoundType>(inner.ElementType).NullableAnnotation.ToString());
        }
    }

    [Theory]
    [InlineData("string", "null")]
    [InlineData("string?", "null")]
    [InlineData("string[]", "value")]
    [InlineData("System.Collections.Generic.List<string?[]>[]", "value")]
    public void NativeObliviousConversion_CertifiesActualReparsedMetadata(string type, string body)
    {
        var original = $$"""
            public static class Fixture {
                public static {{type}} Echo({{type}} value) => {{body}};
            }
            """;
        var conversion = new CSharpToCalorConverter().Convert(original, "Fixture.cs");
        Assert.True(conversion.Success, FormatIssues(conversion));
        Assert.Empty(conversion.Losses);
        if (type != "string?")
            Assert.Contains("nullable oblivious", conversion.CalorSource);
        var result = Program.Compile(conversion.CalorSource!, "converted.calr");
        Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
        Assert.Equal(ConversionRoundTrip.Verified, conversion.Context.RoundTrip);
        Assert.Equal(DeclaredMetadata(original), DeclaredMetadata(result.GeneratedCode));
    }

    [Fact]
    public void NativeObliviousConversion_DoesNotFlattenMixedContexts()
    {
        const string source = """
            #nullable disable
            public static class Fixture {
                public static string Unknown(string value) => value;
            #nullable enable
                public static string Known(string value) => value;
            }
            """;
        var result = new CSharpToCalorConverter().Convert(source, "Mixed.cs");
        Assert.True(result.Success, FormatIssues(result));
        Assert.DoesNotContain("nullable oblivious", result.CalorSource);
        Assert.True(result.InteropPreservationCount > 0);
        var compiled = Program.Compile(result.CalorSource!, "mixed.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
    }

    private static string[] DeclaredMetadata(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Metadata", [tree],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        return CSharpToCalorConverter.DeclaredTypeMetadata(
            compilation.GetSemanticModel(tree), tree.GetCompilationUnitRoot());
    }

    [Theory]
    [InlineData(NullableContextOptions.Enable, "(string:value) -> string")]
    [InlineData(NullableContextOptions.Annotations, "(string:value) -> string")]
    [InlineData(NullableContextOptions.Disable, "(string?:value) -> string?")]
    [InlineData(NullableContextOptions.Warnings, "(string?:value) -> string?")]
    public void EvaluatedProjectContextControlsObliviousDeclarations(
        NullableContextOptions nullableContext,
        string expectedSignature)
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static string Echo(string value) => value;
            }
            """,
            nullableContext);

        Assert.Equal(nullableContext.ToString(), result.Metadata.NullableContextOptions);
        Assert.True(
            result.CalorSource!.Contains(expectedSignature, StringComparison.Ordinal),
            result.CalorSource);
    }

    [Theory]
    [InlineData(
        NullableContextOptions.Enable,
        "#nullable disable",
        "(string?:value) -> string?")]
    [InlineData(
        NullableContextOptions.Disable,
        "#nullable enable",
        "(string:value) -> string")]
    public void FileDirectiveOverridesEvaluatedProjectContext(
        NullableContextOptions projectContext,
        string directive,
        string expectedSignature)
    {
        var result = Convert(
            $$"""
            {{directive}}
            public static class Fixture
            {
                public static string Echo(string value) => value;
            }
            """,
            projectContext);

        Assert.True(
            result.CalorSource!.Contains(expectedSignature, StringComparison.Ordinal),
            result.CalorSource);
    }

    [Fact]
    public void NullableDisabledSafeInitializersRemainNonNullWhileUnknownInvocationIsNullable()
    {
        var result = Convert(
            """
            public sealed class Item { }
            public static class Fixture
            {
                public static string Probe()
                {
                    string literal = "safe";
                    Item created = new Item();
                    var unknown = Read();
                    return literal;
                }

                private static string Read() => null;
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{string:literal} \"safe\"", result.CalorSource);
        Assert.Contains("§B{Item:created} §NEW{Item}", result.CalorSource);
        Assert.Contains("§B{string?:unknown}", result.CalorSource);
        Assert.DoesNotContain("Option<", result.CalorSource);
        Assert.DoesNotContain("§NN", result.CalorSource);
        Assert.DoesNotContain("§UNWRAP", result.CalorSource);
    }

    [Fact]
    public void InvocationInitializedVarRetainsSupportedSemanticNullability()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static void Probe()
                {
                    var present = Read();
                    var absent = Maybe();
                }

                private static string Read() => "value";
                private static string? Maybe() => null;
            }
            """,
            NullableContextOptions.Enable);

        Assert.True(
            result.CalorSource!.Contains(
                "§B{string:present}",
                StringComparison.Ordinal),
            result.CalorSource);
        Assert.True(
            result.CalorSource.Contains(
                "§B{string?:absent}",
                StringComparison.Ordinal),
            result.CalorSource);
    }

    [Fact]
    public void InvocationInitializedVarRetainsFlowNullabilityAttributes()
    {
        var result = Convert(
            """
            using System.Diagnostics.CodeAnalysis;
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                [return: MaybeNull]
                private static string Read() => null;
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{string?:value}", result.CalorSource);
    }

    [Fact]
    public void InvocationInitializedVarRetainsNotNullFlowAttribute()
    {
        var result = Convert(
            """
            using System.Diagnostics.CodeAnalysis;
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                [return: NotNull]
                private static string? Read() => "value";
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{string:value}", result.CalorSource);
        Assert.DoesNotContain("§B{string?:value}", result.CalorSource);
    }

    [Fact]
    public void UserDefinedNotNullAttribute_DoesNotNarrowDeclaredNullableReturn()
    {
        var result = Convert(
            """
            using System;
            public sealed class NotNullAttribute : Attribute { }
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                [return: NotNull]
                private static string? Read() => null;
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{string?:value}", result.CalorSource);
    }

    [Fact]
    public void UserDefinedMaybeNullAttribute_DoesNotWidenDeclaredNonNullReturn()
    {
        var result = Convert(
            """
            using System;
            public sealed class MaybeNullAttribute : Attribute { }
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                [return: MaybeNull]
                private static string Read() => "value";
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{string:value}", result.CalorSource);
        Assert.DoesNotContain("§B{string?:value}", result.CalorSource);
    }

    [Fact]
    public void TerminatingIsNullOrEmptyGuard_PreservesNonNullContinuation()
    {
        var result = Convert(
            """
            using System.Threading.Tasks;
            public static class Fixture
            {
                public static async Task<string> Read(string? value)
                {
                    await Task.Yield();
                    if (string.IsNullOrEmpty(value))
                    {
                        return "fallback";
                    }
                    return value;
                }
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§R value", result.CalorSource);
        var compiled = Program.Compile(
            result.CalorSource!,
            "Fixture.calr",
            new CompilationOptions
            {
                EnforceEffects = false,
                DeferGeneratedOutputValidation = true,
                StatusWriter = TextWriter.Null
            });
        Assert.DoesNotContain(compiled.Diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
        Assert.False(
            compiled.HasErrors,
            string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
    }

    [Fact]
    public void AsyncTaskResultRetainsObliviousReferenceNullability()
    {
        var result = Convert(
            """
            using System.Threading.Tasks;
            public static class Fixture
            {
                public static async Task<string> Read()
                {
                    await Task.Yield();
                    return null;
                }
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("() -> string?", result.CalorSource);
    }

    [Fact]
    public void AsyncStreamContainerIsNotAnnotatedFromItsElement()
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Disable,
                ValidateRoundTripCSharp = false
            }).Convert(
                """
                using System.Collections.Generic;
                public static class Fixture
                {
                    public static async IAsyncEnumerable<string> Read()
                    {
                        yield return "value";
                        await System.Threading.Tasks.Task.Yield();
                    }
                }
                """,
                "Fixture.cs");

        RawFrameAssertions.AssertFramed(result.CalorSource);
        var compiled = Program.Compile(result.CalorSource!, "async-stream.calr");
        Assert.False(compiled.HasErrors, string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
        var read = CSharpSyntaxTree.ParseText(compiled.GeneratedCode!).GetRoot()
            .DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Read");
        Assert.Equal("IAsyncEnumerable<string>", read.ReturnType.ToString());
        Assert.Contains(read.Modifiers, modifier => modifier.IsKind(SyntaxKind.AsyncKeyword));
    }

    [Fact]
    public void ObliviousArrayDeclarationIsReportedAsUnsupportedNestedShape()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static string[] Read() => null;
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("() -> string[]", result.CalorSource);
        Assert.DoesNotContain("() -> string[]?", result.CalorSource);
        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "nullable-oblivious-nested-shape"
                && issue.Severity == ConversionIssueSeverity.Warning);
        Assert.DoesNotContain("Option<", result.CalorSource);
    }

    [Fact]
    public void SafeInitializerDoesNotNarrowReassignedObliviousLocal()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static string Probe()
                {
                    string value = "safe";
                    value = null;
                    return value;
                }
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{~value:string?} \"safe\"", result.CalorSource);
    }

    [Theory]
    [InlineData("Clear(out value);")]
    [InlineData("(value, _) = (null, 0);")]
    public void SafeInitializerDoesNotNarrowIndirectlyWrittenObliviousLocal(
        string write)
    {
        var result = Convert(
            $$"""
            public static class Fixture
            {
                public static string Probe()
                {
                    string value = "safe";
                    {{write}}
                    return value;
                }

                private static void Clear(out string value) => value = null;
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{~value:string?} \"safe\"", result.CalorSource);
    }

    [Fact]
    public void MemberAssignmentDoesNotInvalidateSafeReferenceInitializer()
    {
        var result = Convert(
            """
            public sealed class Item
            {
                public string Name { get; set; }
            }
            public static class Fixture
            {
                public static Item Probe()
                {
                    Item value = new Item();
                    value.Name = "updated";
                    return value;
                }
            }
            """,
            NullableContextOptions.Disable);

        Assert.Contains("§B{Item:value} §NEW{Item}", result.CalorSource);
        Assert.DoesNotContain("§B{Item?:value}", result.CalorSource);
    }

    [Fact]
    public void UnresolvedInvocationInitializedVarReportsNullableCoverageLoss()
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Enable,
                ValidateRoundTripCSharp = false,
                Fidelity = ConversionFidelity.Lossy
            }).Convert(
                """
                public static class Fixture
                {
                    public static void Probe()
                    {
                        var value = External.Read();
                    }
                }
                """,
                "Fixture.cs");

        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "nullable-semantic-unresolved"
                && issue.Severity == ConversionIssueSeverity.Warning);
    }

    [Fact]
    public void NullableValueInvocationRemainsValueNullable()
    {
        var result = Convert(
            """
            public static class Fixture
            {
                public static void Probe()
                {
                    var value = Read();
                }

                private static int? Read() => null;
            }
            """,
            NullableContextOptions.Enable);

        Assert.Contains("§B{int?:value}", result.CalorSource);
        Assert.DoesNotContain("§B{int??:value}", result.CalorSource);
    }

    [Fact]
    public void ActualReferenceNullabilityDrivesInvocationVarAndPreservesRuntime()
    {
        var directory = CreateTestDirectory();
        var legacyPath = Path.Combine(directory, "Legacy.dll");
        try
        {
            EmitAssembly(
                legacyPath,
                "Legacy",
                """
                #nullable disable
                namespace Legacy;
                public static class Api
                {
                    public static string Read(bool present)
                        => present ? "value" : null;
                }
                """,
                NullableContextOptions.Disable);

            const string source = """
                #nullable enable
                public static class Consumer
                {
                    public static string? Probe(bool present)
                    {
                        var value = Legacy.Api.Read(present);
                        return value;
                    }
                }
                """;
            var sourceTree = CSharpSyntaxTree.ParseText(source);
            var sourceCompilation = CSharpCompilation.Create(
                "Issue1401SemanticControl",
                [sourceTree],
                GeneratedCSharpCompiler.References.Append(
                    MetadataReference.CreateFromFile(legacyPath)),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary)
                    .WithNullableContextOptions(
                        NullableContextOptions.Enable));
            var invocation = sourceTree.GetRoot().DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single();
            var invoked = Assert.IsAssignableFrom<IMethodSymbol>(
                sourceCompilation.GetSemanticModel(sourceTree)
                    .GetSymbolInfo(invocation).Symbol);
            Assert.Equal(
                Microsoft.CodeAnalysis.NullableAnnotation.None,
                invoked.ReturnType.NullableAnnotation);
            var options = new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Enable,
                References =
                [
                    new ConversionReference(legacyPath, Array.Empty<string>())
                ]
            };
            var result = new CSharpToCalorConverter(options)
                .Convert(source, "Consumer.cs");

            Assert.True(result.Success, FormatIssues(result));
            Assert.True(
                result.CalorSource!.Contains(
                    "§B{string?:value}",
                    StringComparison.Ordinal),
                result.CalorSource);
            Assert.DoesNotContain("Option<", result.CalorSource);
            Assert.DoesNotContain("default", result.CalorSource);
            Assert.DoesNotContain("§TH", result.CalorSource);
            Assert.DoesNotContain("§UNWRAP", result.CalorSource);

            var compiled = Program.Compile(
                result.CalorSource!,
                "Consumer.calr",
                new CompilationOptions
                {
                    EnforceEffects = false,
                    UnknownCallPolicy =
                        Calor.Compiler.Effects.UnknownCallPolicy.Permissive,
                    DeferGeneratedOutputValidation = true,
                    StatusWriter = TextWriter.Null
                });
            Assert.False(
                compiled.HasErrors,
                string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            var validation = GeneratedCSharpCompiler.Validate(
                [new GeneratedCSharpSource(compiled.GeneratedCode, "Consumer.g.cs")],
                new GeneratedCSharpCompilationContext
                {
                    References =
                    [
                        new GeneratedCSharpReference(
                            legacyPath,
                            Array.Empty<string>())
                    ],
                    NullableContextOptions = NullableContextOptions.Enable
                });
            Assert.True(
                validation.CompilationSuccess,
                string.Join(
                    Environment.NewLine,
                    validation.FormattedCompilationErrors));
            Assert.DoesNotContain("Calor.Runtime.Option", compiled.GeneratedCode);

            const string legacyRuntimeSource = """
                #nullable disable
                namespace Legacy;
                public static class Api
                {
                    public static string Read(bool present)
                        => present ? "value" : null;
                }
                """;
            var original = EmitInMemory(
                source,
                legacyRuntimeSource,
                NullableContextOptions.Enable);
            var converted = EmitInMemory(
                compiled.GeneratedCode,
                legacyRuntimeSource,
                NullableContextOptions.Enable);

            Assert.Equal(
                InvokeProbe(original, false),
                InvokeProbe(converted, false));
            Assert.Equal(
                InvokeProbe(original, true),
                InvokeProbe(converted, true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UnresolvedDeclarationRetainsExplicitDiagnostic()
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = NullableContextOptions.Enable,
                ValidateRoundTripCSharp = false,
                Fidelity = ConversionFidelity.Lossy
            }).Convert(
                """
                public static class Fixture
                {
                    public static Missing Read() => External.Read();
                }
                """,
                "Fixture.cs");

        Assert.Contains(
            result.Issues,
            issue => issue.Feature == "nullable-semantic-unresolved"
                && issue.Severity == ConversionIssueSeverity.Warning);
    }

    [Fact]
    public async Task ProjectMigratorCarriesEvaluatedNullableContextAndReferences()
    {
        var directory = CreateTestDirectory();
        try
        {
            var projectPath = Path.Combine(
                directory,
                "ContextFixture.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>disable</Nullable>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Fixture.cs"),
                """
                public static class Fixture
                {
                    public static string Echo(string value) => value;
                }
                """);
            await RestoreProjectAsync(projectPath);
            var inputs = Assert.Single(
                await ProjectMigrator.ResolveProjectCompilationInputsAsync(
                    projectPath,
                    "Debug",
                    selectedTargetFramework: null,
                    CancellationToken.None));
            var direct = new CSharpToCalorConverter(
                new ConversionOptions
                {
                    NullableContextOptions = inputs.NullableContextOptions,
                    ReferencesAreComplete = true,
                    References = inputs.ReferencePaths.Select(path =>
                        new ConversionReference(
                            path,
                            inputs.ReferenceAliases.TryGetValue(
                                path,
                                out var aliases)
                                ? aliases
                                : Array.Empty<string>()))
                        .ToArray()
                }).Convert(
                    await File.ReadAllTextAsync(
                        Path.Combine(directory, "Fixture.cs")),
                    Path.Combine(directory, "Fixture.cs"));
            Assert.True(direct.Success, FormatIssues(direct));
            Assert.Contains("(string?:value) -> string?", direct.CalorSource);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    SkipAnalyze = true,
                    SkipVerify = true,
                    ValidateOutput = true
                });
            var plan = await migrator.CreatePlanAsync(
                projectPath,
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var file = Assert.Single(
                report.FileResults,
                result => result.SourcePath.EndsWith(
                    "Fixture.cs",
                    StringComparison.Ordinal));

            Assert.Contains(
                file.Status,
                new[]
                {
                    FileMigrationStatus.Success,
                    FileMigrationStatus.Partial
                });
            Assert.DoesNotContain(
                file.Issues,
                issue => issue.Severity == ConversionIssueSeverity.Error);
            Assert.Equal(
                NullableContextOptions.Disable.ToString(),
                file.Metadata!.NullableContextOptions);
            Assert.NotEmpty(file.Metadata.References);
            Assert.True(
                file.ConvertedSource!.Contains(
                    "(string?:value) -> string?",
                    StringComparison.Ordinal),
                file.ConvertedSource);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigratorAllowsAnnotationsUnderDisabledDirectiveWithWarningsAsErrors()
    {
        var directory = CreateTestDirectory();
        try
        {
            var projectPath = Path.Combine(directory, "DirectiveFixture.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Fixture.cs"),
                """
                #nullable disable // legacy source
                public static class Fixture
                {
                    public static string Echo(string value) => value;
                }
                """);
            await RestoreProjectAsync(projectPath);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    SkipAnalyze = true,
                    SkipVerify = true,
                    ValidateOutput = true
                });
            var plan = await migrator.CreatePlanAsync(
                projectPath,
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var file = Assert.Single(
                report.FileResults,
                result => result.SourcePath.EndsWith(
                    "Fixture.cs",
                    StringComparison.Ordinal));

            Assert.Contains(
                file.Status,
                new[]
                {
                    FileMigrationStatus.Success,
                    FileMigrationStatus.Partial
                });
            Assert.DoesNotContain(
                file.Issues,
                issue => issue.Severity == ConversionIssueSeverity.Error);
            Assert.Contains("(string?:value) -> string?", file.ConvertedSource);
            var compiled = Program.Compile(
                file.ConvertedSource!,
                "Fixture.calr",
                new CompilationOptions
                {
                    EnforceEffects = false,
                    DeferGeneratedOutputValidation = true,
                    StatusWriter = TextWriter.Null
                });
            Assert.False(
                compiled.HasErrors,
                string.Join(Environment.NewLine, compiled.Diagnostics.Errors));
            Assert.Contains("#nullable disable warnings", compiled.GeneratedCode);
            Assert.Contains("#nullable enable annotations", compiled.GeneratedCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectMigratorResolvesInvocationFromSiblingSource()
    {
        var directory = CreateTestDirectory();
        try
        {
            var projectPath = Path.Combine(directory, "SiblingFixture.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Api.cs"),
                """
                public static class Api
                {
                    public static Task<string?> Read() => Task.FromResult<string?>(null);
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "Consumer.cs"),
                """
                public static class Consumer
                {
                    public static void Probe()
                    {
                        var value = Api.Read();
                    }
                }
                """);
            await RestoreProjectAsync(projectPath);

            var migrator = new ProjectMigrator(
                new MigrationPlanOptions
                {
                    Parallel = false,
                    SkipAnalyze = true,
                    SkipVerify = true,
                    ValidateOutput = true
                });
            var plan = await migrator.CreatePlanAsync(
                projectPath,
                MigrationDirection.CSharpToCalor);
            var report = await migrator.ExecuteAsync(plan);
            var consumer = Assert.Single(
                report.FileResults,
                result => result.SourcePath.EndsWith(
                    "Consumer.cs",
                    StringComparison.Ordinal));

            Assert.DoesNotContain(
                consumer.Issues,
                issue => issue.Feature == "nullable-semantic-unresolved");
            Assert.Contains("§B{Task<string?>:value}", consumer.ConvertedSource);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ConversionResult Convert(
        string source,
        NullableContextOptions nullableContext)
    {
        var result = new CSharpToCalorConverter(
            new ConversionOptions
            {
                NullableContextOptions = nullableContext
            }).Convert(source, "Fixture.cs");
        Assert.True(result.Success, FormatIssues(result));
        return result;
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "issue-1401-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void EmitAssembly(
        string path,
        string assemblyName,
        string source,
        NullableContextOptions nullableContext)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullableContext));
        using var stream = File.Create(path);
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
    }

    private static async Task RestoreProjectAsync(string projectPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("restore");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity:quiet");
        using var process = Process.Start(startInfo)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(
            process.ExitCode == 0,
            (await outputTask) + Environment.NewLine + await errorTask);
    }

    private static Assembly EmitInMemory(
        string source,
        string additionalSource,
        NullableContextOptions nullableContext)
    {
        var compilation = CSharpCompilation.Create(
            "Issue1401_" + Guid.NewGuid().ToString("N"),
            [
                CSharpSyntaxTree.ParseText(source),
                CSharpSyntaxTree.ParseText(additionalSource)
            ],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(nullableContext));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static object? InvokeProbe(Assembly assembly, bool present)
        => assembly.GetType("Consumer")!
            .GetMethod("Probe")!
            .Invoke(null, [present]);

    private static string FormatIssues(ConversionResult result)
        => string.Join(
            Environment.NewLine,
            result.Issues.Select(issue =>
                $"{issue.Severity}: {issue.Feature}: {issue.Message}"));
}
