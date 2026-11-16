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

    // 2026-11-14 maintainer decision: a `nullable oblivious` module mirrors C#'s disabled
    // nullable context. An oblivious argument is accepted at a non-null parameter; an explicit
    // `?T` argument and modules without the policy keep the receiving check.
    [Fact]
    public void NativeObliviousModule_AcceptsObliviousArgumentsButKeepsExplicitNullableChecks()
    {
        var normal = Program.Compile("""
            module Checked
              func Missing () → string
                !
                return null
            """);
        Assert.Contains(normal.Diagnostics.Errors,
            diagnostic => diagnostic.Code == DiagnosticCode.NullableReturnFromNonNullable);
        foreach (var type in new[] { "string", "string?" })
        foreach (var guard in new[] { "", "if value != null\n      " })
        {
            var result = Program.Compile($$"""
                module Oblivious nullable oblivious
                  func Probe ({{type}}:value) → void
                    !
                    {{guard}}System.Int32.Parse(value)
                """);
            var rejected = type == "string?" && guard.Length == 0;
            Assert.Equal(rejected, result.HasErrors);
            Assert.Equal(rejected, result.Diagnostics.Errors.Any(
                diagnostic => diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
        }
    }

    // 2026-11-14: oblivious nested reads are accepted in a `nullable oblivious` module; an
    // explicit `?` leaf is still rejected.
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
        if (annotation == "Oblivious")
        {
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics.Errors));
            return;
        }
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

    [Theory]
    [InlineData("public string M(string value) { T Identity<T>(T item) => item; return Identity(value); }", "method")]
    [InlineData("public string Value { get { T Identity<T>(T item) => item; return Identity(\"kept\"); } }", "property")]
    [InlineData("public static C operator +(C left, C right) { T Identity<T>(T item) => item; return Identity(left); }", "operator")]
    public void ObliviousConversion_DirectMemberPreservationRetainsStrictMetadata(
        string member, string feature)
    {
        var original = $"public class C {{ public int Native(int value) => value; {member} }}";
        var conversion = new CSharpToCalorConverter().Convert(original, "Hybrid.cs");

        Assert.True(conversion.Success, FormatIssues(conversion));
        var preservation = Assert.Single(conversion.Losses);
        Assert.Equal(ConversionLossKind.InteropPreserved, preservation.Kind);
        Assert.Equal(ConversionPath.Interop, preservation.Path);
        Assert.Equal(feature, preservation.Feature);
        Assert.Equal(1, conversion.InteropPreservationCount);
        Assert.Contains("nullable oblivious", conversion.CalorSource);
        Assert.Contains("func Native", conversion.CalorSource);
        var compiled = Program.Compile(conversion.CalorSource!, "hybrid.calr", new CompilationOptions
        {
            StrictEffects = true,
            UnknownCallPolicy = Effects.UnknownCallPolicy.Strict
        });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(ConversionRoundTrip.Verified, conversion.Context.RoundTrip);
        Assert.Equal(DeclaredMetadata(original), DeclaredMetadata(compiled.GeneratedCode));
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

    [Theory]
    [InlineData("\"value=\" + value")]
    [InlineData("$\"{value:F2}\"")]
    public void ObliviousCandidate_EffectFailureReportsCompilerDiagnosticInsteadOfMetadataMismatch(string expression)
    {
        var source = "public static class Fixture\r\n{\r\n"
            + $"    public static string Render(double value) => {expression};\r\n"
            + "}\r\n// exact original EOF";
        var result = new CSharpToCalorConverter().Convert(source, "Formatting.cs");

        // The restored 0.29 System.Double contract makes formatting a double pure, so the
        // oblivious candidate no longer fails Calor0410: it is delivered natively, with no
        // metadata mismatch, and behaves like the original.
        Assert.True(result.Success, FormatIssues(result));
        Assert.Empty(result.Losses);
        Assert.Equal(0, result.InteropPreservationCount);
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "roundtrip-calor-validation");
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "nullable-module-fidelity");
        var compiled = Program.Compile(result.CalorSource!, "native.calr", new CompilationOptions
        {
            StrictEffects = true,
            UnknownCallPolicy = Effects.UnknownCallPolicy.Strict
        });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
        foreach (var value in new[] { 0d, 1234.5, -0.125 })
            Assert.Equal(
                EmitInMemory(source, "", NullableContextOptions.Disable).GetType("Fixture")!
                    .GetMethod("Render")!.Invoke(null, [value]),
                EmitInMemory(compiled.GeneratedCode, "", NullableContextOptions.Disable).GetType("Fixture")!
                    .GetMethod("Render")!.Invoke(null, [value]));
    }

    [Fact]
    public void ObliviousCandidate_FailedConversionRetainsItsDistinctIssues()
    {
        const string source = "public static class Fixture { public static string Echo(string value) => value; }";
        var converter = new CSharpToCalorConverter
        {
            ParseValidatorOverride = calor => !calor.Contains("nullable oblivious", StringComparison.Ordinal)
        };
        var result = converter.Convert(source, "Candidate.cs");

        Assert.True(result.Success, FormatIssues(result));
        Assert.Equal("whole-file-preservation", Assert.Single(result.Losses).Feature);
        Assert.Equal(ConversionTrigger.UnsupportedConstruct, Assert.Single(result.Losses).Trigger);
        Assert.Contains(result.Issues, issue => issue.Feature == "generated-calor-validation"
            && issue.Message.Contains(
                "Oblivious module candidate (Error): Generated Calor failed mandatory parse validation.",
                StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Feature == "nullable-module-fidelity"
            && issue.Message.Contains("did not produce successful, entirely native Calor", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Message.Contains("changed declared type/nullability metadata", StringComparison.Ordinal));
        Assert.Equal(result.Issues.Count, result.Issues
            .DistinctBy(issue => (issue.Severity, issue.Message, issue.Feature,
                issue.Line, issue.Column, issue.Suggestion)).Count());
        var compiled = Program.Compile(result.CalorSource!, "preserved.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(source, compiled.GeneratedCode);
    }

    [Fact]
    public void ObliviousCandidate_MixedContextsReportTheUnrepresentablePolicy()
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
        Assert.Contains(result.Issues, issue => issue.Feature == "nullable-module-fidelity"
            && issue.Message.Contains("cannot represent the original lexical nullable contexts", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Issues, issue =>
            issue.Message.Contains("changed declared type/nullability metadata", StringComparison.Ordinal));
        var compiled = Program.Compile(result.CalorSource!, "preserved.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(source, compiled.GeneratedCode);
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
    }

    [Fact]
    public void ObliviousCandidate_MissingSourceContextRemainsExplicitAndUnverified()
    {
        const string source = "public static class Fixture { public static Missing Echo(string value) => null; }";
        var result = new CSharpToCalorConverter().Convert(source, "Missing.cs");

        Assert.True(result.Success, FormatIssues(result));
        Assert.Equal("whole-file-preservation", Assert.Single(result.Losses).Feature);
        Assert.Equal(ConversionRoundTrip.Failed, result.Context.RoundTrip);
        Assert.Contains(result.Issues, issue => issue.Feature == "roundtrip-csharp-validation"
            && issue.Message.Contains("CS0246", StringComparison.Ordinal)
            && issue.Line > 0 && issue.Column > 0);
        Assert.DoesNotContain(result.Issues, issue => issue.Feature == "nullable-module-fidelity");
        var compiled = Program.Compile(result.CalorSource!, "preserved.calr",
            new CompilationOptions { DeferGeneratedOutputValidation = true });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(source, compiled.GeneratedCode);
    }

    // Before the 2026-11-14 decision 062 and 075 preserved the whole file: an oblivious `string`
    // receiver (`text.Trim()`, `str.Length`) failed Calor0274 inside the oblivious module.
    // 061 preserved the whole file until the converter stopped printing a legacy `§B{~` temp
    // for its hoisted nested tuple (0.30).
    [Theory]
    [InlineData("Level4_Methods", "062_OptionalParameters.cs", false)]
    [InlineData("Level4_Methods", "062_OptionalParameters.cs", true)]
    [InlineData("Level4_Methods", "075_ExtensionMethods.cs", false)]
    [InlineData("Level4_Methods", "075_ExtensionMethods.cs", true)]
    [InlineData("Level3_Types", "061_TupleTypes.cs", false)]
    [InlineData("Level3_Types", "061_TupleTypes.cs", true)]
    public void ObliviousReceiverInputs_NowConvertNatively(string level, string fileName, bool evaluated)
    {
        var source = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests", "TestData", "CSharpImport", level, fileName));
        var project = new CSharpToCalorConverter(PreservationOptions(evaluated))
            .ConvertProject(source, fileName);
        var converted = Assert.Single(project.Outputs).Conversion;
        Assert.True(converted.Success, FormatIssues(converted));
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.Empty(converted.Losses);
        Assert.Contains("nullable oblivious", converted.CalorSource!);
        var compiled = Program.Compile(converted.CalorSource!, "native.calr",
            new CompilationOptions
            {
                StrictEffects = true,
                UnknownCallPolicy = Effects.UnknownCallPolicy.Strict
            });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PostValidationFallback_RetainsCallbackMetadataAndFaithfulBoundaries(bool nullableAlias)
    {
        var source = $$"""
            #nullable enable
            using {{(nullableAlias ? "Callback = System.Func<string, string>" : "System")}};
            namespace N {
                public static class Keepme { public static int Value() => 1; }
                public class Breakme {
                    public string Apply({{(nullableAlias ? "Callback" : "Func<string, string>")}} callback, string value) => callback(value);
                }
            }
            """;
        var converter = new CSharpToCalorConverter(PreservationOptions(evaluated: true))
        {
            ParseValidatorOverride = calor =>
                !Parsing.RawCSharpFrame.MaskForAnalysis(calor)
                    .Contains("Breakme", StringComparison.Ordinal)
        };
        var converted = converter.Convert(source, "Callbacks.cs");

        Assert.True(converted.Success, FormatIssues(converted));
        var compiled = Program.Compile(converted.CalorSource!, "callbacks.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Errors));
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
        Assert.Equal("post-validation-fallback", Assert.Single(converted.Losses).Feature);
        Assert.Equal("Keepme", Assert.Single(converted.Ast!.Classes).Name);
    }

    private static ConversionOptions PreservationOptions(bool evaluated)
    {
        var options = Commands.ConvertCommand.BuildCSharpToCalorOptions(
            false, false, false, false, false);
        if (evaluated)
        {
            options.NullableContextOptions = NullableContextOptions.Disable;
            options.ReferencesAreComplete = true;
            options.References = GeneratedCSharpCompiler.References.Select(reference =>
            {
                var image = Assert.IsAssignableFrom<PortableExecutableReference>(reference);
                Assert.NotNull(image.FilePath);
                return new ConversionReference(image.FilePath, image.Properties.Aliases);
            }).ToArray();
        }
        return options;
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
    [InlineData(NullableContextOptions.Enable, "(string:value) → string")]
    [InlineData(NullableContextOptions.Annotations, "(string:value) → string")]
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
        "(string:value) → string")]
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

        Assert.Contains("let string:literal = \"safe\"", result.CalorSource);
        Assert.Contains("let Item:created = new Item()", result.CalorSource);
        Assert.Contains("let string?:unknown", result.CalorSource);
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
                "let string:present",
                StringComparison.Ordinal),
            result.CalorSource);
        Assert.True(
            result.CalorSource.Contains(
                "let string?:absent",
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

        Assert.Contains("let string?:value", result.CalorSource);
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

        Assert.Contains("let string:value", result.CalorSource);
        Assert.DoesNotContain("string?:value", result.CalorSource);
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

        Assert.Contains("let string?:value", result.CalorSource);
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

        Assert.Contains("let string:value", result.CalorSource);
        Assert.DoesNotContain("string?:value", result.CalorSource);
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

        Assert.Contains("return value", result.CalorSource);
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

        Assert.Contains("() → string?", result.CalorSource);
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

        Assert.Contains("() → string[]", result.CalorSource);
        Assert.DoesNotContain("() → string[]?", result.CalorSource);
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

        Assert.Contains("var string?:value = \"safe\"", result.CalorSource);
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

        Assert.Contains("var string?:value = \"safe\"", result.CalorSource);
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

        Assert.Contains("let Item:value = new Item()", result.CalorSource);
        Assert.DoesNotContain("Item?:value", result.CalorSource);
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

        Assert.Contains("let int?:value", result.CalorSource);
        Assert.DoesNotContain("int??:value", result.CalorSource);
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
                    "let string?:value",
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
            Assert.Contains("(string?:value) → string?", direct.CalorSource);

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
                    "(string?:value) → string?",
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
            Assert.Contains("(string?:value) → string?", file.ConvertedSource);
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
            Assert.Contains("let Task<string?>:value", consumer.ConvertedSource);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataMatchingNullableCandidate_RefusedEffectsPreserveOriginal052(bool project)
    {
        var source = File.ReadAllText(Path.Combine(CliTestHarness.FindRepoRoot(),
            "tests/TestData/CSharpImport/Level3_Types/052_NullableTypes.cs"));
        var options = Commands.ConvertCommand.BuildCSharpToCalorOptions(false, false, false, false, false);
        var converter = new CSharpToCalorConverter(options);
        var conversion = project
            ? Assert.Single(converter.ConvertProject(source, "052_NullableTypes.cs").Outputs).Conversion
            : converter.Convert(source, "052_NullableTypes.cs");
        // The restored 0.29 Nullable<T> contract makes HasValue/Value pure, so 052 no
        // longer fails Calor0410 and is delivered natively (scorecard: 052 now native).
        Assert.True(conversion.Success, FormatIssues(conversion));
        Assert.Empty(conversion.Losses);
        Assert.Equal(0, conversion.InteropPreservationCount);
        Assert.Equal(0, conversion.LossySubstitutionCount);
        Assert.Equal(0, conversion.DropCount);
        Assert.DoesNotContain(conversion.Issues, issue => issue.Feature == "roundtrip-calor-validation");
        foreach (var typing in new[] { false, true })
        {
            var compiled = Program.Compile(conversion.CalorSource!, "native-052.calr", new CompilationOptions
            {
                EnableTypeChecking = typing, StrictEffects = true,
                UnknownCallPolicy = Effects.UnknownCallPolicy.Strict, StatusWriter = TextWriter.Null
            });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.NotEqual(source, compiled.GeneratedCode);
            Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
        }
    }

    [Fact]
    public void MetadataMatchingNullableCandidate_ProvedCopyRemainsNative()
    {
        const string source = """
            public static class NullableCopy
            {
                public static int Read(int? value) => value.GetValueOrDefault();
            }
            """;
        var options = Commands.ConvertCommand.BuildCSharpToCalorOptions(false, false, false, false, false);
        var converter = new CSharpToCalorConverter(options);
        foreach (var conversion in new[]
        {
            converter.Convert(source, "NullableCopy.cs"),
            Assert.Single(converter.ConvertProject(source, "NullableCopy.cs").Outputs).Conversion
        })
        {
            Assert.True(conversion.Success, FormatIssues(conversion));
            Assert.Empty(conversion.Losses);
            Assert.Equal(0, conversion.InteropPreservationCount);
            var compiled = Program.Compile(conversion.CalorSource!, "nullable-copy.calr", new CompilationOptions
            {
                StrictEffects = true, UnknownCallPolicy = Effects.UnknownCallPolicy.Strict,
                StatusWriter = TextWriter.Null
            });
            Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
            Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
        }
    }

    [Theory]
    [InlineData("ToUpperInvariant", "string")]
    [InlineData("ToLowerInvariant", "string")]
    public void ObliviousZeroArgumentReceiver_RetainsNullExceptionEvaluationAndMetadata(
        string method, string returnType)
    {
        var source = $$"""
            public static class Fixture
            {
                private static string Receiver(ref int calls, string value)
                {
                    calls++;
                    return value;
                }
                public static {{returnType}} Probe(ref int calls, string value)
                    => Receiver(ref calls, value).{{method}}();
            }
            """;
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy, Mode = ConversionMode.Interop
        }).Convert(source, "Receiver.cs");
        Assert.True(converted.Success, FormatIssues(converted));
        Assert.True(converted.Losses.Count == 0, FormatIssues(converted));
        var compiled = Program.Compile(converted.CalorSource!, "receiver.calr", new CompilationOptions
        {
            StrictEffects = true, UnknownCallPolicy = Effects.UnknownCallPolicy.Strict,
            StatusWriter = TextWriter.Null
        });
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
        var original = EmitInMemory(source, "", NullableContextOptions.Disable)
            .GetType("Fixture")!.GetMethod("Probe")!;
        var generated = EmitInMemory(compiled.GeneratedCode, "", NullableContextOptions.Disable)
            .GetType("Fixture")!.GetMethod("Probe")!;
        object?[] originalArguments = [0, " AbC "];
        object?[] generatedArguments = [0, " AbC "];
        Assert.Equal(original.Invoke(null, originalArguments), generated.Invoke(null, generatedArguments));
        Assert.Equal(1, originalArguments[0]);
        Assert.Equal(1, generatedArguments[0]);
        originalArguments = [0, null];
        generatedArguments = [0, null];
        var originalFailure = Assert.Throws<TargetInvocationException>(() => original.Invoke(null, originalArguments));
        Assert.IsType<NullReferenceException>(originalFailure.InnerException);
        var failure = Assert.Throws<TargetInvocationException>(() => generated.Invoke(null, generatedArguments));
        Assert.IsType<NullReferenceException>(failure.InnerException);
    }

    [Fact]
    public void ObliviousZeroArgumentExtension_DoesNotAcquireAnInstanceNullGuard()
    {
        const string source = """
            public static class Fixture
            {
                public static string Read(this string value) => value ?? "none";
                public static string Probe(string value) => value.Read();
            }
            """;
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy, Mode = ConversionMode.Interop
        }).Convert(source, "Extension.cs");
        Assert.True(converted.Success, FormatIssues(converted));
        Assert.Empty(converted.Losses);
        var compiled = Program.Compile(converted.CalorSource!, "extension.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var generated = EmitInMemory(compiled.GeneratedCode, "", NullableContextOptions.Disable);
        Assert.Equal("none", generated.GetType("Fixture")!.GetMethod("Probe")!.Invoke(null, [null]));
        Assert.Equal(DeclaredMetadata(source), DeclaredMetadata(compiled.GeneratedCode));
    }

    // 2026-11-14: the converted module is `nullable oblivious`, so an oblivious receiver with
    // arguments is accepted, as C#'s disabled nullable context accepts it.
    [Fact]
    public void ObliviousCallWithArguments_IsAcceptedInAnObliviousModule()
    {
        const string source = """
            public static class Fixture
            {
                public static string Probe(string value, int width) => value.PadLeft(width);
            }
            """;
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy, Mode = ConversionMode.Interop
        }).Convert(source, "Arguments.cs");
        Assert.True(converted.Success, FormatIssues(converted));
        Assert.Contains("nullable oblivious", converted.CalorSource!);
        var compiled = Program.Compile(converted.CalorSource!, "arguments.calr");
        Assert.DoesNotContain(compiled.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Fact]
    public void ObliviousZeroArgumentReceiver_DoesNotGrantOpenObjectDispatchEffects()
    {
        const string source = """
            public static class Fixture
            {
                public static int Probe(object value) => value.GetHashCode();
            }
            """;
        var converted = new CSharpToCalorConverter(new ConversionOptions
        {
            Fidelity = ConversionFidelity.Lossy, Mode = ConversionMode.Interop
        }).Convert(source, "Object.cs");
        Assert.True(converted.Success, FormatIssues(converted));
        // 0.30: the receiver dispatch stays Unknown, so the converter declares ! unknown on
        // Probe instead of a pure row. The output compiles, and the same body with a pure
        // row is still rejected for 'unknown'.
        Assert.Contains("    ! unknown\n", converted.CalorSource!.Replace("\r\n", "\n", StringComparison.Ordinal));
        var options = new CompilationOptions
        {
            StrictEffects = true, UnknownCallPolicy = Effects.UnknownCallPolicy.Strict,
            StatusWriter = TextWriter.Null
        };
        var compiled = Program.Compile(converted.CalorSource!, "object.calr", options);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var pure = Program.Compile(converted.CalorSource!.Replace("! unknown", "!", StringComparison.Ordinal),
            "object.calr", options);
        Assert.Contains(pure.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect
                && diagnostic.Message.Contains("'unknown'", StringComparison.Ordinal));
    }
}
