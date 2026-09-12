using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class NativeStringApplicabilityTests
{
    private const string NullableProducer =
        "§C{System.Environment.GetEnvironmentVariable} §A \"CALOR_N4_UNSET\" §/C";

    public static IEnumerable<object[]> FormsAndAliases()
    {
        foreach (var expression in new[] { false, true })
        foreach (var alias in new[] { "str", "string" })
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
            (alias, "\"safe\""), ("?" + alias, "\"safe\""), ("?" + alias, NullableProducer)
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
    public void ExistingObjectOverload_DoesNotBecomeNewNullabilityEnforcement(bool expression, string alias)
    {
        var source = Source(expression, alias, NullableProducer, """
              §F{object:Take:pub} (object:value) -> i32
                §E{}
                §R 2
            """);
        var (bound, diagnostics) = Bind(source);
        Assert.Equal(alias, SelectedFunction(bound, expression).Parameters.Single().TypeName);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.False(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(BindingDiagnosticPolicy.IsAnalysisOnly(diagnostic));
        var compiled = Program.Compile(source, "native-string.calr");
        Assert.False(compiled.HasErrors, string.Join("; ", compiled.Diagnostics));
    }

    [Theory]
    [MemberData(nameof(FormsAndAliases))]
    public void NativeNullableReturn_ReachesTheSameArgumentPredicate(bool expression, string alias)
    {
        var source = Source(expression, alias, "§C{Get} §/C", $$"""
              §F{get:Get:pub} () -> ?{{alias}}
                §E{env}
                §R {{NullableProducer}}
            """);
        var (_, diagnostics) = Bind(source);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.True(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(Program.Compile(source, "native-producer.calr").HasErrors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedInputs_KeepN2LocalTransferAndN3FormalMapping(bool expression)
    {
        var source = $$"""
            §M{m1:NativeString}
              §F{take:Take:pub} (str:required,?str:optional) -> i32
                §E{}
                §R 1
              §F{caller:Caller:pub} (?str:maybe,str:sure) -> i32
                §E{}
                §B{first} maybe
                §B{second} first
                {{(expression ? "§R " : "")}}§C{Take} §A[optional] sure §A[required] second §/C
                {{(expression ? "" : "§R 0")}}
            """;
        var (bound, diagnostics) = Bind(source);
        var body = bound.Functions.Single(function => function.Symbol.Name == "Caller").Body;
        var matches = expression
            ? Assert.IsType<BoundCallExpression>(Assert.IsType<BoundReturnStatement>(body[2]).Expression).SelectedOverloadMatches
            : Assert.IsType<BoundCallStatement>(body[2]).SelectedOverloadMatches;
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
    public void RuntimeSomeAndNone_AreNotUnwrapped(string argument)
    {
        foreach (var nullableTarget in new[] { false, true })
        {
            var source = Source(false, nullableTarget ? "?str" : "str", argument);
            var (_, diagnostics) = Bind(source);
            Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
            Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NullableArgumentToNonNullableParameter);
            Assert.True(Program.Compile(source, "runtime-option.calr").HasErrors);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenuineAmbiguityAndUnknownNames_KeepTheirDiagnostics(bool expression)
    {
        var ambiguous = Source(expression, "i64", "1", """
              §F{double:Take:pub} (f64:value) -> i32
                §E{}
                §R 2
            """);
        var (_, ambiguousDiagnostics) = Bind(ambiguous);
        Assert.Equal(DiagnosticCode.AmbiguousOverload, Assert.Single(ambiguousDiagnostics.Errors).Code);
        var unknownName = Source(expression, "str", "\"safe\"")
            .Replace("§A \"safe\"", "§A[missing] \"safe\"", StringComparison.Ordinal);
        var (_, nameDiagnostics) = Bind(unknownName);
        Assert.Equal(DiagnosticCode.NoMatchingOverload, Assert.Single(nameDiagnostics.Errors).Code);
    }

    [Fact]
    public void InvisibleArgument_DoesNotAcquireAnInventedPriorRejection()
    {
        const string source = """
            §M{m1:NativeString}
              §F{take:Take:pub} (str:value,Invisible:other) -> i32
                §E{}
                §R 1
              §F{caller:Caller:pub} (?str:value,Invisible:other) -> i32
                §E{}
                §R §C{Take} §A value §A other §/C
            """;
        var (_, diagnostics) = Bind(source);
        var diagnostic = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
        Assert.False(diagnostic.BindingContext!.ReplacesNativeOverloadError);
        Assert.True(BindingDiagnosticPolicy.IsAnalysisOnly(diagnostic));
    }

    [Fact]
    public void PreviousAmbiguity_IsNotMistakenForPreviousAcceptance()
    {
        var converted = new Migration.CSharpToCalorConverter().Convert("""
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
        var converted = new Migration.CSharpToCalorConverter().Convert("""
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
            Assert.Equal(boundary == BindingReceivingBoundary.MethodArgument && shape == BindingReceivingShape.ScalarString
                ? BindingDiagnosticDisposition.CompilationError
                : BindingDiagnosticDisposition.AnalysisOnly, rule.Disposition);
        }
    }

    private static string Source(bool expression, string target, string argument, string extra = "") => $$"""
        §M{m1:NativeString}
          §F{take:Take:pub} ({{target}}:value) -> i32
            §E{}
            §R 1
        {{extra}}
          §F{caller:Caller:pub} () -> i32
            §E{env}
            {{(expression ? "§R " : "")}}§C{Take} §A {{argument}} §/C
            {{(expression ? "" : "§R 0")}}
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
            : Assert.IsType<BoundCallStatement>(body[0]).ResolvedSymbol!;
    }
}
