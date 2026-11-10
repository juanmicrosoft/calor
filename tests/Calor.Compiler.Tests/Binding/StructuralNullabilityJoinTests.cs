using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class StructuralNullabilityJoinTests
{
    public static IEnumerable<object[]> QualifiedJoinCases()
    {
        foreach (var container in new[]
        {
            "List", "IList", "IEnumerable", "IReadOnlyList", "ICollection", "IReadOnlyCollection"
        })
        foreach (var qualifiedSource in new[] { false, true })
        foreach (var coalesce in new[] { false, true })
        foreach (var boundary in new[] { "initializer", "return", "argument" })
            yield return [container, qualifiedSource, coalesce, boundary];
    }

    [Theory]
    [MemberData(nameof(QualifiedJoinCases))]
    public void QualifiedGenericAliasesKeepPayloadAtReceivingBoundaries(
        string container, bool qualifiedSource, bool coalesce, string boundary)
    {
        var qualified = $"System.Collections.Generic.{container}";
        var sourceType = $"{(qualifiedSource ? qualified : container)}<string?>";
        var targetType = $"{(qualifiedSource ? container : qualified)}<string>";
        var expression = coalesce ? "value ?? fallback" : "flag ? value : fallback";
        var statement = boundary switch
        {
            "initializer" => $"let {targetType}:required = joined",
            "return" => "return joined",
            _ => "Take(joined)"
        };
        var source = $$"""
            module QualifiedJoins
              func Take public ({{targetType}}:value) → void
                !
              func Probe public ({{sourceType}}:value, {{targetType}}:fallback, bool:flag) → {{targetType}}
                let joined = {{expression}}
                {{statement}}
                throw null
            """;
        var code = boundary switch
        {
            "initializer" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        var options = new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null };
        Parse(source);
        var rejected = Program.Compile(source, "qualified-join.calr", options);
        Assert.True(rejected.HasErrors);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Empty(rejected.GeneratedCode);
        var safeSource = source.Replace("<string?>", "<string>", StringComparison.Ordinal);
        Parse(safeSource);
        var safe = Program.Compile(safeSource, "qualified-join.calr", options);
        Assert.False(safe.HasErrors, string.Join(Environment.NewLine, safe.Diagnostics));
    }

    [Theory]
    [InlineData("List", "System.Collections.Generic.List", true)]
    [InlineData("System.Collections.Generic.IList", "IList", true)]
    [InlineData("Other.List", "List", false)]
    [InlineData("Other.List", "System.Collections.Generic.List", false)]
    [InlineData("List", "IList", false)]
    [InlineData("Other.List", "Other.List", true)]
    public void GenericDefinitionAliasesDoNotEquateUnrelatedNamespaces(string left, string right, bool same)
    {
        Assert.Equal(same, NullabilityChecker.SameGenericDefinition(left, right));
    }

    public static IEnumerable<object[]> ReceivingCases()
    {
        foreach (var expression in new[] { "(? flag value value)", "(?? value value)" })
        foreach (var (source, target, unsafeValue) in new[]
        {
            ("[?str]", "[str]", true),
            ("?[str]", "[str]", true),
            ("List<?str>", "List<str>", true),
            ("[str]", "[str]", false),
            ("List<str>", "List<str>", false)
        })
        foreach (var boundary in new[] { "initializer", "return", "argument" })
        foreach (var local in new[] { false, true })
            yield return [expression, source, target, unsafeValue, boundary, local];
    }

    [Theory]
    [InlineData("[?str]", "[str]", false)]
    [InlineData("[?str]", "[str]", true)]
    [InlineData("List<?str>", "List<str>", false)]
    [InlineData("List<?str>", "List<str>", true)]
    public void DifferentAnnotationArmsKeepPayloadAfterInference(
        string sourceType, string targetType, bool coalesce)
    {
        var expression = coalesce ? "value ?? fallback" : "flag ? value : fallback";
        var diagnostics = Bind($$"""
            module MixedAnnotationJoin
              func Probe public ({{NativeType(sourceType)}}:value, {{NativeType(targetType)}}:fallback, bool:flag) → {{NativeType(targetType)}}
                let joined = {{expression}}
                return joined
            """);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Theory]
    [MemberData(nameof(ReceivingCases))]
    public void JoinedValuesRetainStructuralNullability(
        string expression, string sourceType, string targetType,
        bool unsafeValue, string boundary, bool local)
    {
        var nativeExpression = NativeExpression(expression);
        var nativeTarget = NativeType(targetType);
        var value = local ? "joined" : nativeExpression;
        var statement = boundary switch
        {
            "initializer" => $"let {nativeTarget}:required = {value}",
            "return" => $"return {value}",
            _ => $"Take({value})"
        };
        var source = $$"""
            module StructuralJoins
              func Take public ({{nativeTarget}}:value) → void
                !
              func Probe public ({{NativeType(sourceType)}}:value, bool:flag) → {{nativeTarget}}
                {{(local ? $"let joined = {nativeExpression}" : "")}}
                {{statement}}
                throw null
            """;
        var diagnostics = Bind(source);
        var code = boundary switch
        {
            "initializer" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        Assert.Equal(unsafeValue, diagnostics.Any(d => d.Code == code));
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.NoMatchingOverload);
        if (!unsafeValue)
            Assert.DoesNotContain(diagnostics, BindingDiagnosticPolicy.IsCompilationError);
        var compiled = Program.Compile(source, "structural-join.calr",
            new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null });
        Assert.Equal(unsafeValue, compiled.HasErrors);
        Assert.Equal(unsafeValue, compiled.Diagnostics.Any(d => d.Code == code));
    }

    [Theory]
    [InlineData("(? flag value fallback)", true)]
    [InlineData("(?? value fallback)", false)]
    [InlineData("(?? value null)", true)]
    [InlineData("(? flag value null)", true)]
    [InlineData("(?? null fallback)", false)]
    public void ArrayContainerJoinDistinguishesNullFromFallback(string expression, bool unsafeValue)
    {
        var diagnostics = Bind($$"""
            module ContainerJoins
              func Probe public (string[]?:value, string[]:fallback, bool:flag) → string[]
                return {{NativeExpression(expression)}}
            """);
        Assert.Equal(unsafeValue, diagnostics.Any(d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable));
    }

    [Theory]
    [InlineData("(? flag value fallback)", true)]
    [InlineData("(?? value fallback)", true)]
    [InlineData("(?? fallback value)", false)]
    public void CoalescingPreservesElementsWithoutInventingNullableFallbacks(
        string expression, bool unsafeValue)
    {
        var diagnostics = Bind($$"""
            module ElementJoins
              func Probe public (string?[]?:value, string[]:fallback, bool:flag) → string[]
                return {{NativeExpression(expression)}}
            """);
        Assert.Equal(unsafeValue, diagnostics.Any(d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable));
    }

    // Keep historical theory labels stable without admitting retired syntax to the frontend.
    private static string NativeType(string label) => label switch
    {
        "[?str]" => "string?[]",
        "?[str]" => "string[]?",
        "[str]" => "string[]",
        "List<?str>" => "List<string?>",
        "List<str>" => "List<string>",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown structural type label")
    };

    private static string NativeExpression(string label) => label switch
    {
        "(? flag value value)" => "flag ? value : value",
        "(?? value value)" => "value ?? value",
        "(? flag value fallback)" => "flag ? value : fallback",
        "(?? value fallback)" => "value ?? fallback",
        "(?? value null)" => "value ?? null",
        "(? flag value null)" => "flag ? value : null",
        "(?? null fallback)" => "null ?? fallback",
        "(?? fallback value)" => "fallback ?? value",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown structural expression label")
    };

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, $"{source}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}");
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, $"{source}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}");
        return module;
    }

    private static DiagnosticBag Bind(string source)
    {
        var module = Parse(source);
        var diagnostics = new DiagnosticBag();
        new Binder(diagnostics).Bind(module);
        return diagnostics;
    }
}
