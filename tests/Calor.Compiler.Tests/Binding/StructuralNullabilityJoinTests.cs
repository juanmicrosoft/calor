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
        var sourceType = $"{(qualifiedSource ? qualified : container)}<?str>";
        var targetType = $"{(qualifiedSource ? container : qualified)}<str>";
        var expression = coalesce ? "(?? value fallback)" : "(? flag value fallback)";
        var statement = boundary switch
        {
            "initializer" => $"§B{{required:{targetType}}} joined",
            "return" => "§R joined",
            _ => "§C{Take} §A joined §/C"
        };
        var source = $$"""
            §M{m1:QualifiedJoins}
              §F{take:Take:pub} ({{targetType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{sourceType}}:value, {{targetType}}:fallback, bool:flag) -> {{targetType}}
                §B{joined} {{expression}}
                {{statement}}
                §TH null
            """;
        var code = boundary switch
        {
            "initializer" => DiagnosticCode.NullableToNonNullableBinding,
            "return" => DiagnosticCode.NullableReturnFromNonNullable,
            _ => DiagnosticCode.NullableArgumentToNonNullableParameter
        };
        var options = new CompilationOptions { EnforceEffects = false, StatusWriter = TextWriter.Null };
        var rejected = Program.Compile(source, "qualified-join.calr", options);
        Assert.True(rejected.HasErrors);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Empty(rejected.GeneratedCode);
        var safe = Program.Compile(
            source.Replace("<?str>", "<str>", StringComparison.Ordinal), "qualified-join.calr", options);
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
        var expression = coalesce ? "(?? value fallback)" : "(? flag value fallback)";
        var diagnostics = Bind($$"""
            §M{m1:MixedAnnotationJoin}
              §F{probe:Probe:pub} ({{sourceType}}:value, {{targetType}}:fallback, bool:flag) -> {{targetType}}
                §B{joined} {{expression}}
                §R joined
            """);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.NullableReturnFromNonNullable);
    }

    [Theory]
    [MemberData(nameof(ReceivingCases))]
    public void JoinedValuesRetainStructuralNullability(
        string expression, string sourceType, string targetType,
        bool unsafeValue, string boundary, bool local)
    {
        var value = local ? "joined" : expression;
        var statement = boundary switch
        {
            "initializer" => $"§B{{required:{targetType}}} {value}",
            "return" => $"§R {value}",
            _ => $"§C{{Take}} §A {value} §/C"
        };
        var source = $$"""
            §M{m1:StructuralJoins}
              §F{take:Take:pub} ({{targetType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{sourceType}}:value, bool:flag) -> {{targetType}}
                {{(local ? $"§B{{joined}} {expression}" : "")}}
                {{statement}}
                §TH null
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
            §M{m1:ContainerJoins}
              §F{probe:Probe:pub} (?[str]:value, [str]:fallback, bool:flag) -> [str]
                §R {{expression}}
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
            §M{m1:ElementJoins}
              §F{probe:Probe:pub} (?[?str]:value, [str]:fallback, bool:flag) -> [str]
                §R {{expression}}
            """);
        Assert.Equal(unsafeValue, diagnostics.Any(d =>
            d.Code == DiagnosticCode.NullableReturnFromNonNullable));
    }

    private static DiagnosticBag Bind(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        new Binder(diagnostics).Bind(module);
        return diagnostics;
    }
}
