using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeStringApplicabilityTests
{
    private const string NullableProducer =
        "System.Environment.GetEnvironmentVariable(\"CALOR_N4_UNSET\")";

    public static IEnumerable<object[]> FormsAndAliases()
    {
        foreach (var expression in new[] { false, true })
        foreach (var alias in new[] { "string", "System.String" })
            yield return [expression, alias];
    }

    [Theory]
    [MemberData(nameof(FormsAndAliases))]
    public void NullableInput_SelectsNativeMethod_AndHandsOffActualRejection(bool expression, string alias)
    {
        var source = Source(expression, alias, NullableProducer);
        var (bound, diagnostics) = Bind(source);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.True(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        Assert.Equal(NullableProducer, source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.Equal(alias, SelectedFunction(bound, expression).Parameters.Single().TypeName);

        foreach (var typeChecking in new[] { true, false })
        foreach (var transpile in new[] { true, false })
        {
            var compiled = Program.Compile(source, "native-string.calr", new CompilationOptions
            {
                EnableTypeChecking = typeChecking,
                UnsafeTranspileOnly = transpile
            });
            Assert.True(compiled.HasErrors);
            Assert.Contains(compiled.Diagnostics, d =>
                d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter && d.IsError);
            Assert.DoesNotContain(compiled.Diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
        }
    }

    [Theory]
    [MemberData(nameof(FormsAndAliases))]
    public void SafeInputsAndNullableTargets_AreAccepted(bool expression, string alias)
    {
        foreach (var (target, argument) in new[]
        {
            (alias, "\"safe\""), (alias + "?", "\"safe\""), (alias + "?", NullableProducer)
        })
        {
            var source = Source(expression, target, argument);
            var (bound, diagnostics) = Bind(source);
            Assert.Empty(diagnostics.Errors);
            Assert.Equal(target, SelectedFunction(bound, expression).Parameters.Single().TypeName);
            var compiled = Program.Compile(source, "native-string.calr");
            Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics));
            Assert.DoesNotContain("??", compiled.GeneratedCode);
            Assert.DoesNotContain("Calor.Runtime.Option", compiled.GeneratedCode);
            Assert.DoesNotContain(".Unwrap", compiled.GeneratedCode);
        }
    }

    [Theory]
    [MemberData(nameof(FormsAndAliases))]
    public void SelectedScalarOverload_ActivatesWithoutInventingPriorRejection(bool expression, string alias)
    {
        var source = Source(expression, alias, NullableProducer, """
              func Take (object:value) → int
                !
                return 2
            """);
        var (bound, diagnostics) = Bind(source);
        Assert.Equal(alias, SelectedFunction(bound, expression).Parameters.Single().TypeName);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.False(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
        var compiled = Program.Compile(source, "native-string.calr");
        Assert.True(compiled.HasErrors);
        Assert.Empty(compiled.GeneratedCode);
    }

    [Theory]
    [MemberData(nameof(FormsAndAliases))]
    public void NativeNullableReturn_ReachesTheSameArgumentPredicate(bool expression, string alias)
    {
        var source = Source(expression, alias, "Get()", $$"""
              func Get () → {{alias}}?
                ! env
                return {{NullableProducer}}
            """);
        var (_, diagnostics) = Bind(source);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.True(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(Program.Compile(source, "native-producer.calr").HasErrors);
    }

    [Theory]
    [InlineData(false, "global::System.String")]
    [InlineData(true, "global::System.String")]
    [InlineData(false, "Text")]
    [InlineData(true, "Text")]
    public void ResolvedStringAliases_PreserveDeclaredIdentityAndNullableRejection(bool expression, string alias)
    {
        foreach (var nullable in new[] { false, true })
        {
            var source = Source(expression, alias, nullable ? NullableProducer : "\"safe\"")
                .Replace("module NativeString", """
                    module NativeString
                      using Text = System.String
                    """, StringComparison.Ordinal);
            var (bound, diagnostics) = Bind(source);
            Assert.Equal(alias, SelectedFunction(bound, expression).Parameters.Single().TypeName);
            if (nullable)
            {
                var diagnostic = Assert.Single(diagnostics.Errors);
                Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
                Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
            }
            else
            {
                Assert.Empty(diagnostics.Errors);
            }
            var compiled = Program.Compile(source, "resolved-string.calr");
            Assert.Equal(nullable, compiled.HasErrors);
        }
    }

    [Theory]
    [InlineData("System.String", false)]
    [InlineData("@System.String", false)]
    [InlineData("Text", false)]
    [InlineData("global::System.String", true)]
    public void SourceStringLookalikes_RequireTheActualBuiltinIdentity(string target, bool builtin)
    {
        var source = $$"""
            module NativeString Example
              using Text = Example.System.String
              class System public
                class String public
              func Take ({{target}}:value) → int
                !
                return 1
              func Caller () → int
                !
                return Take("safe")
            """;
        var (_, diagnostics) = Bind(source);
        if (builtin)
            Assert.Empty(diagnostics.Errors);
        else
            Assert.Equal(DiagnosticCode.NoMatchingOverload, Assert.Single(diagnostics.Errors).Code);
        var compiled = Program.Compile(source, "source-string.calr");
        Assert.Equal(!builtin, compiled.HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainingTypeParameters_ShadowMetadataStringAliases(bool nested)
    {
        const string members = """
            func Take (Text:value) → int
              !
              return 1
            func Caller (Text:value) → int
              !
              return Take(value)
            """;
        var source = """
            module GenericStringAlias
              using Text = System.String
              class Outer<Text>
            """ + "\n" + (nested ? "    class Inner\n" : "")
            + string.Join("\n", members.Split('\n').Select(line =>
                (nested ? "      " : "    ") + line));
        var (bound, diagnostics) = Bind(source);
        Assert.Empty(diagnostics.Errors);
        var call = Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(
            Assert.Single(bound.Functions.Single(function =>
                function.Symbol.Name.EndsWith(".Caller", StringComparison.Ordinal)).Body)).Expression);
        Assert.NotNull(call.ResolvedSymbol);
        Assert.Equal("Text", call.ResolvedSymbol.Parameters.Single().TypeName);
        Assert.Equal("Text", Assert.Single(call.Arguments).Type.DisplayString);
        Assert.False(call.Arguments[0].Type is Binding.BoundTypes.NominalBoundType
            { RoslynSymbol.SpecialType: SpecialType.System_String });
        var compiled = Program.Compile(source, "generic-string-alias.calr");
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics));
    }

    [Theory]
    [InlineData("ref")]
    [InlineData("out")]
    [InlineData("in")]
    public void QualifiedStringByReferenceInputs_RetainDeclaredAnnotations(string modifier)
    {
        foreach (var target in new[] { "System.String", "global::System.String" })
        foreach (var nullable in new[] { false, true })
        {
            var annotation = nullable ? "?" : "";
            var effects = modifier == "in" ? "!" : "! mut";
            var source = $$"""
                module NativeString
                  func Take ({{modifier}} {{target}}{{annotation}}:value) → int
                    {{effects}}
                    {{(modifier == "out" ? "set value \"safe\"" : "")}}
                    return 1
                  func Caller ({{modifier}} string{{annotation}}:value) → int
                    {{effects}}
                    return Take({{modifier}} value)
                """;
            var (_, diagnostics) = Bind(source);
            Assert.Empty(diagnostics.Errors);
            var compiled = Program.Compile(source, "reference-string.calr");
            Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedInputs_KeepN2LocalTransferAndN3FormalMapping(bool expression)
    {
        var source = $$"""
            module NativeString
              func Take (string:required, string?:optional) → int
                !
                return 1
              func Caller (string?:maybe, string:sure) → int
                !
                let first = maybe
                let second = first
                {{(expression ? "return " : "")}}Take(optional: sure, required: second)
                {{(expression ? "" : "return 0")}}
            """;
        var (bound, diagnostics) = Bind(source);
        var body = bound.Functions.Single(function => function.Symbol.Name == "Caller").Body;
        var matches = expression
            ? Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(body[2]).Expression).SelectedOverloadMatches
            : Assert.IsType<BoundCallExpression>(Assert.IsType<BoundExpressionStatement>(body[2]).Expression).SelectedOverloadMatches;
        Assert.Equal(new[] { 1, 0 }, Assert.Single(matches).Arguments.Select(mapping => mapping.ParameterIndex));
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.Contains("'required'", diagnostic.Message);
        Assert.Equal(source.LastIndexOf("second", StringComparison.Ordinal), diagnostic.Span.Start);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    [Theory]
    [InlineData("§SM \"value\"")]
    [InlineData("§NN{str}")]
    public void RemovedConstructors_AreRejectedBeforeNativeApplicability(string argument)
    {
        foreach (var nullableTarget in new[] { false, true })
        {
            var source = Source(false, nullableTarget ? "string?" : "string", argument);
            Assert.True(Program.Compile(source, "runtime-option.calr").HasErrors);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenuineAmbiguityAndUnknownNames_KeepTheirDiagnostics(bool expression)
    {
        var csharp = CSharpCompilation.Create("NumericAmbiguity",
            [CSharpSyntaxTree.ParseText("""
                public static class NumericAmbiguity
                {
                    public static int Take(double value) => 1;
                    public static int Take(decimal value) => 2;
                    public static int Caller() => Take(1);
                }
                """)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Equal("CS0121", Assert.Single(csharp.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)).Id);
        var ambiguous = Source(expression, "double", "1", """
              func Take (decimal:value) → int
                !
                return 2
            """);
        var (_, ambiguousDiagnostics) = Bind(ambiguous);
        Assert.Equal(DiagnosticCode.AmbiguousOverload, Assert.Single(ambiguousDiagnostics.Errors).Code);
        var unknownName = Source(expression, "string", "\"safe\"")
            .Replace("Take(\"safe\")", "Take(missing: \"safe\")", StringComparison.Ordinal);
        var (_, nameDiagnostics) = Bind(unknownName);
        Assert.Equal(DiagnosticCode.NoMatchingOverload, Assert.Single(nameDiagnostics.Errors).Code);
    }

    [Fact]
    public void InvisibleSiblingArgument_DoesNotChangeScalarActivationProvenance()
    {
        const string source = """
            module NativeString
              func Take (string:value, Invisible:other) → int
                !
                return 1
              func Caller (string?:value, Invisible:other) → int
                !
                return Take(value, other)
            """;
        var (_, diagnostics) = Bind(source);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.False(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    [Fact]
    public void PreviousAmbiguity_IsNotMistakenForPreviousAcceptance()
    {
        var converted = new Migration.CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert("""
            public static class NativeString
            {
                public static int Take(string value) => 1;
                public static int Take(object value, int count = 0) => 2;
                public static int Take(object value, bool enabled = false) => 3;
                public static int Caller(string? value) => Take(value);
            }
            """);
        var (_, diagnostics) = Bind(Assert.IsType<string>(converted.CalorSource));
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.True(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
    }

    [Fact]
    public void Constructors_RemainOutsideTheCompatibilityChange()
    {
        var converted = new Migration.CSharpToCalorConverter(
            new() { ValidateRoundTripCSharp = false }).Convert("""
            public class Box
            {
                public Box(string? value) { }
                public static Box Create(string value) => new Box(value);
            }
            """);
        var (_, diagnostics) = Bind(Assert.IsType<string>(converted.CalorSource));
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
    }

    [Fact]
    public void GenericSubstitution_IsNotAReferenceAnnotationConversion()
    {
        var scope = new Scope();
        var function = new FunctionSymbol("Take", "T", [new VariableSymbol("value", "T", false)], ["T"]);
        Assert.True(scope.TryDeclareOverload("Take", function, out _));
        var explicitType = scope.ResolveOverload("Take", ["?str"], null, null, ["str"], null,
            allowNullableStringCompatibility: true);
        Assert.Equal(OverloadResolutionKind.NoMatch, explicitType.Kind);
        var inferred = scope.ResolveOverload("Take", ["?str"], null, null, null, null,
            allowNullableStringCompatibility: true);
        Assert.Equal(OverloadResolutionKind.Resolved, inferred.Kind);
        Assert.Equal("STRING?", TypeIdentity.Canonicalize(inferred.ResolvedReturnType!));
    }

    [Theory]
    [InlineData("str")]
    [InlineData("STRING")]
    public void GenericParametersNamedLikeBuiltins_DoNotGainAnnotationConversion(string typeParameter)
    {
        var scope = new Scope();
        var function = new FunctionSymbol("Take", typeParameter,
            [new VariableSymbol("value", typeParameter, false)], [typeParameter]);
        Assert.True(scope.TryDeclareOverload("Take", function, out _));
        var result = scope.ResolveOverload("Take", ["?str"], null, null, ["str"], null,
            allowNullableStringCompatibility: true);
        Assert.Equal(OverloadResolutionKind.NoMatch, result.Kind);
    }

    [Fact]
    public void ConcreteStringOverload_WinsNullableCompatibilityTieWithInferredGeneric()
    {
        var scope = new Scope();
        var generic = new FunctionSymbol("Take", "T", [new VariableSymbol("value", "T", false)], ["T"]);
        var concrete = new FunctionSymbol("Take", "i32", [new VariableSymbol("value", "str", false)]);
        Assert.True(scope.TryDeclareOverload("Take", generic, out _));
        Assert.True(scope.TryDeclareOverload("Take", concrete, out _));

        var result = scope.ResolveOverload("Take", ["?str"], null, null, null, null,
            allowNullableStringCompatibility: true);

        Assert.Equal(OverloadResolutionKind.Resolved, result.Kind);
        Assert.Same(concrete, result.Function);
    }

    [Fact]
    public void ConditionalConcreteStringAlternatives_WinNullableCompatibilityTieWithInferredGeneric()
    {
        var scope = new Scope();
        var generic = new FunctionSymbol("Take", "T", [new VariableSymbol("value", "T", false)], ["T"]);
        var first = new FunctionSymbol(
            SymbolId.None,
            "Take",
            "i32",
            [new VariableSymbol("value", "str", false)],
            conditionalAlternative: new ConditionalAlternative("take", 0));
        var second = new FunctionSymbol(
            SymbolId.None,
            "Take",
            "i32",
            [new VariableSymbol("value", "str", false)],
            conditionalAlternative: new ConditionalAlternative("take", 1));
        Assert.True(scope.TryDeclareOverload("Take", generic, out _));
        Assert.True(scope.TryDeclareOverload("Take", first, out _));
        Assert.True(scope.TryDeclareOverload("Take", second, out _));

        var result = scope.ResolveOverload("Take", ["?str"], null, null, null, null,
            allowNullableStringCompatibility: true);

        Assert.Equal(OverloadResolutionKind.Resolved, result.Kind);
        Assert.Same(first, result.Function);
        Assert.Equal([first, second], result.Functions);
    }

    [Theory]
    [InlineData("str")]
    [InlineData("STRING")]
    public void AliasNamedTypeParameter_DoesNotDisableConcreteStringCompatibility(string typeParameter)
    {
        var scope = new Scope();
        var function = new FunctionSymbol("Take", "i32",
            [
                new VariableSymbol("item", typeParameter, false),
                new VariableSymbol("value", "string", false)
            ],
            [typeParameter]);
        Assert.True(scope.TryDeclareOverload("Take", function, out _));

        var result = scope.ResolveOverload(
            "Take",
            ["i32", "?str"],
            null,
            null,
            ["i32"],
            (parameter, argument) =>
                TypeIdentity.Canonicalize(parameter) == TypeIdentity.Canonicalize(argument) ? 0 : null,
            allowNullableStringCompatibility: true);

        Assert.Equal(OverloadResolutionKind.Resolved, result.Kind);
        Assert.Same(function, result.Function);
    }

    [Theory]
    [InlineData("Option<str>")]
    [InlineData("?i32")]
    [InlineData("bool")]
    [InlineData("[str]")]
    [InlineData("List<str>")]
    public void OtherRepresentations_DoNotGainScalarCompatibility(string argumentType)
    {
        var scope = new Scope();
        var function = new FunctionSymbol("Take", "i32", [new VariableSymbol("value", "str", false)]);
        Assert.True(scope.TryDeclareOverload("Take", function, out _));
        var resolution = scope.ResolveOverload("Take", [argumentType], null, null, null, null,
            allowNullableStringCompatibility: true);
        Assert.Equal(OverloadResolutionKind.NoMatch, resolution.Kind);
    }

    [Theory]
    [InlineData(ParameterModifier.Ref, "ref")]
    [InlineData(ParameterModifier.Out, "out")]
    [InlineData(ParameterModifier.In, "in")]
    [InlineData(ParameterModifier.In, null)]
    public void ByReferenceParameters_RetainTheirExistingCompatibility(ParameterModifier modifier, string? argumentModifier)
    {
        var scope = new Scope();
        var function = new FunctionSymbol("Take", "i32",
            [new VariableSymbol("value", "str", true, true, modifier)]);
        Assert.True(scope.TryDeclareOverload("Take", function, out _));
        var resolution = scope.ResolveOverload("Take", ["?str"], null, [argumentModifier], null, null,
            allowNullableStringCompatibility: true);
        Assert.Equal(OverloadResolutionKind.NoMatch, resolution.Kind);
    }

    [Fact]
    public void RejectionProvenance_DoesNotActivateOtherBoundariesOrShapes()
    {
        foreach (var boundary in Enum.GetValues<BindingReceivingBoundary>())
        foreach (var shape in Enum.GetValues<BindingReceivingShape>())
        {
            var context = new BindingDiagnosticContext(boundary, shape) { ReplacesNativeOverloadError = true };
            var rule = BindingDiagnosticPolicy.GetRule(DiagnosticCode.NullableArgumentToNonNullableParameter, context);
            Assert.Equal(boundary == BindingReceivingBoundary.MethodArgument
                && shape is BindingReceivingShape.ScalarString
                    or BindingReceivingShape.Array
                    or BindingReceivingShape.Generic
                    or BindingReceivingShape.Nominal
                ? BindingDiagnosticDisposition.CompilationError
                : BindingDiagnosticDisposition.AnalysisOnly, rule.Disposition);
        }
    }

    private static string Source(bool expression, string target, string argument, string extra = "") => $$"""
        module NativeString
          func Take ({{target}}:value) → int
            !
            return 1
        {{extra}}
          func Caller () → int
            ! env
            {{(expression ? "return " : "")}}Take({{argument}})
            {{(expression ? "" : "return 0")}}
        """;

    private static (BoundModule Module, DiagnosticBag Diagnostics) Bind(string source)
    {
        var parse = new DiagnosticBag();
        var module = new Parser(new Lexer(source, parse).TokenizeAllForParser(), parse).Parse();
        Assert.False(parse.HasErrors, string.Join("; ", parse));
        var diagnostics = new DiagnosticBag();
        return (new Binder(diagnostics, "native-string.calr").Bind(module), diagnostics);
    }

    private static FunctionSymbol SelectedFunction(BoundModule module, bool expression)
    {
        var body = module.Functions.Single(function => function.Symbol.Name == "Caller").Body;
        return expression
            ? Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(body[0]).Expression).ResolvedSymbol!
            : Assert.IsType<BoundCallExpression>(Assert.IsType<BoundExpressionStatement>(body[0]).Expression).ResolvedSymbol!;
    }
}
