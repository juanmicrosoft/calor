using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CallableMutationFlowTests
{
    public static IEnumerable<object[]> MutationCases()
    {
        string[] scenarios =
        [
            "call-before-reset", "block-local-alias", "late-target", "branch-retains-target",
            "branch-isolation", "mutable-rebind", "deferred-setter", "nested-deferred-setter",
            "late-write-source", "write-source-fixed-point", "setter-does-not-invoke-value",
            "expression-call-before-reset", "deferred-rebind", "pure-cycle",
            "alias-initializer", "alias-assignment", "invoke-member", "direct-write",
            "nested-guard", "repeat-invocation", "recursive-dependencies",
            "uninvoked-setter", "uninvoked-mutator", "uninvoked-reset", "alias-is-value-copy",
            "all-branches-reset"
        ];
        foreach (var scenario in scenarios)
        foreach (var modifier in new[] { "ref", "out" })
        foreach (var array in new[] { false, true })
            yield return [scenario, modifier, array];
    }

    [Theory]
    [MemberData(nameof(MutationCases))]
    public void CapturedCallableFlow_TracksInvocationTimeEffects(
        string scenario, string modifier, bool array)
    {
        var nullableType = array ? "?[str]" : "?str";
        var nonNullableType = array ? "[str]" : "str";
        var mutator = $"§LAM{{}} §C{{Mutate}} §A{{{modifier}}} value §/C §/LAM";
        var body = scenario switch
        {
            "call-before-reset" => $$"""
                §B{~later:Action} {{mutator}}
                §B{wrapper:Action} §LAM{}
                  §C{later} §/C
                  §ASSIGN later Noop
                §/LAM
                §C{wrapper} §/C
                """,
            "block-local-alias" => $$"""
                §B{later:Action} {{mutator}}
                §B{wrapper:Action} §LAM{}
                  §IF flag
                    §B{alias:Action} later
                    §C{alias} §/C
                §/LAM
                §C{wrapper} §/C
                """,
            "expression-call-before-reset" => $$"""
                §B{~later:Func<bool>} §LAM{} §C{MutateBool} §A{ {{modifier}} } value §/C §/LAM
                §B{wrapper:Func<bool>} §LAM{}
                  §B{result:bool} §C{later} §/C
                  §ASSIGN later NoopBool
                  §R result
                §/LAM
                §B{result:bool} §C{wrapper} §/C
                """,
            "late-target" => $$"""
                §B{~later:Action} Noop
                §B{wrapper:Action} §LAM{} §C{later} §/C §/LAM
                §ASSIGN later {{mutator}}
                §C{wrapper} §/C
                """,
            "branch-retains-target" => $$"""
                §B{~later:Action} {{mutator}}
                §IF flag
                  §ASSIGN later Noop
                §C{later} §/C
                """,
            "branch-isolation" => $$"""
                §B{~later:Action} {{mutator}}
                §IF flag
                  §ASSIGN later Noop
                §EL
                  §B{wrapper:Action} §LAM{} §C{later} §/C §/LAM
                  §C{wrapper} §/C
                """,
            "mutable-rebind" => $$"""
                §B{~later:Action} Noop
                §B{~later} {{mutator}}
                §C{later} §/C
                """,
            "deferred-setter" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{}
                  §ASSIGN later mutator
                §/LAM
                §C{setter} §/C
                §C{later} §/C
                """,
            "nested-deferred-setter" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{}
                  §ASSIGN later mutator
                §/LAM
                §B{wrapper:Action} §LAM{} §C{setter} §/C §/LAM
                §C{wrapper} §/C
                §C{later} §/C
                """,
            "deferred-rebind" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{}
                  §B{~later} mutator
                §/LAM
                §C{setter} §/C
                §C{later} §/C
                """,
            "late-write-source" => $$"""
                §B{~later:Action} Noop
                §B{~mutator:Action} Noop
                §B{setter:Action} §LAM{}
                  §ASSIGN later mutator
                §/LAM
                §ASSIGN mutator {{mutator}}
                §C{setter} §/C
                §C{later} §/C
                """,
            "write-source-fixed-point" => $$"""
                §B{~later:Action} Noop
                §B{~source:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{}
                  §ASSIGN source mutator
                §/LAM
                §B{wrapper:Action} §LAM{}
                  §C{setter} §/C
                  §ASSIGN later source
                §/LAM
                §C{wrapper} §/C
                §C{later} §/C
                """,
            "setter-does-not-invoke-value" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{}
                  §ASSIGN later mutator
                §/LAM
                §C{setter} §/C
                """,
            "alias-initializer" => $$"""
                §B{later:Action} {{mutator}}
                §B{alias:Action} later
                §C{alias} §/C
                """,
            "alias-assignment" => $$"""
                §B{later:Action} {{mutator}}
                §B{~alias:Action} Noop
                §ASSIGN alias later
                §C{alias} §/C
                """,
            "invoke-member" => $$"""
                §B{later:Action} {{mutator}}
                §B{wrapper:Action} §LAM{} §C{later.Invoke} §/C §/LAM
                §C{wrapper.Invoke} §/C
                """,
            "direct-write" => """
                §B{later:Action} §LAM{}
                  §ASSIGN value null
                §/LAM
                §C{later} §/C
                """,
            "nested-guard" => $$"""
                §B{later:Action} §LAM{}
                  §IF (!= value null)
                    §C{Mutate} §A{ {{modifier}} } value §/C
                §/LAM
                §C{later} §/C
                """,
            "repeat-invocation" => $$"""
                §B{later:Action} {{mutator}}
                §C{later} §/C
                §IF (!= value null)
                  §C{later} §/C
                  §C{Take} §A value §/C
                """,
            "recursive-dependencies" => $$"""
                §B{~first:Action} Noop
                §B{~second:Action} Noop
                §B{mutator:Action} {{mutator}}
                §ASSIGN first §LAM{} §C{second} §/C §/LAM
                §ASSIGN second §LAM{}
                  §C{first} §/C
                  §C{mutator} §/C
                §/LAM
                §C{first} §/C
                """,
            "pure-cycle" => """
                §B{~first:Action} Noop
                §B{~second:Action} Noop
                §ASSIGN first §LAM{} §C{second} §/C §/LAM
                §ASSIGN second §LAM{} §C{first} §/C §/LAM
                §C{first} §/C
                """,
            "uninvoked-setter" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{}
                  §ASSIGN later mutator
                §/LAM
                §C{later} §/C
                """,
            "uninvoked-mutator" => $$"""
                §B{later:Action} {{mutator}}
                """,
            "uninvoked-reset" => $$"""
                §B{~later:Action} {{mutator}}
                §B{setter:Action} §LAM{}
                  §ASSIGN later Noop
                §/LAM
                §C{later} §/C
                """,
            "alias-is-value-copy" => $$"""
                §B{~later:Action} Noop
                §B{alias:Action} §E{} later
                §ASSIGN later {{mutator}}
                §C{alias} §/C
                """,
            "all-branches-reset" => $$"""
                §B{~later:Action} {{mutator}}
                §IF flag
                  §ASSIGN later Noop
                §EL
                  §ASSIGN later Noop
                §C{later} §/C
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var source = $$"""
            §M{CallableMutationFlow}
              §F{Noop:pub} () -> void
                §E{}
              §F{Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{NoopBool:pub} () -> bool
                §R true
              §F{MutateBool:pub} ({{nullableType}}:value:{{modifier}}) -> bool
                §ASSIGN value null
                §R true
              §F{Take:pub} ({{nonNullableType}}:value) -> void
                §E{}
              §F{Probe:pub} ({{nullableType}}:value, bool:flag) -> void
                §IF (!= value null)
            {{Indent(body, 6)}}
                  §C{Take} §A value §/C
            """;
        var result = Program.Compile(source, "callable-mutation-flow.calr");
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.Code != DiagnosticCode.NullableArgumentToNonNullableParameter);
        var errors = result.Diagnostics.Where(BindingDiagnosticPolicy.IsCompilationError).ToArray();
        Assert.DoesNotContain(errors, diagnostic =>
            diagnostic.Code != DiagnosticCode.NullableArgumentToNonNullableParameter);
        var shouldInvalidate = scenario is not (
            "uninvoked-setter" or "uninvoked-mutator" or "alias-is-value-copy" or "all-branches-reset"
            or "setter-does-not-invoke-value" or "pure-cycle");
        Assert.True(shouldInvalidate == errors.Any(diagnostic =>
            diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter),
            string.Join("\n", result.Diagnostics));
        if (scenario == "repeat-invocation")
            Assert.Equal(2, errors.Count(diagnostic =>
                diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter));
    }

    [Theory]
    [InlineData("ref", "?str", "str")]
    [InlineData("out", "?str", "str")]
    [InlineData("ref", "?[str]", "[str]")]
    [InlineData("out", "?[str]", "[str]")]
    public void LambdaParameterShadowingField_DoesNotMutateField(
        string modifier, string nullableType, string nonNullableType)
    {
        var source = $$"""
            §M{CallableShadow}
              §F{Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{Take:pub} ({{nonNullableType}}:value) -> void
                §E{}
              §CL{holder:Holder:pub}
                §FLD{ {{nullableType}} :value:pub}
                §MT{Probe:pub} () -> void
                  §IF (!= value null)
                    §B{later:Action<{{nullableType}}>} §LAM{value:{{nullableType}}}
                      §C{Mutate} §A{ {{modifier}} } value §/C
                    §/LAM
                    §C{later} §A value §/C
                    §C{Take} §A value §/C
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        new Binder(diagnostics).Bind(module);
        Assert.Empty(diagnostics.Errors);
    }

    private static string Indent(string source, int spaces) =>
        string.Join("\n", source.Split('\n').Select(line => new string(' ', spaces) + line));

    public static IEnumerable<object[]> LoopCases()
    {
        foreach (var kind in new[] { "while", "for", "foreach", "do" })
        foreach (var scenario in new[] { "zero", "backedge", "alias-backedge", "reguard", "unused" })
        foreach (var modifier in new[] { "ref", "out" })
        foreach (var array in new[] { false, true })
        {
            if (kind != "do" || scenario != "zero")
                yield return [kind, scenario, modifier, array];
        }
    }

    [Theory]
    [MemberData(nameof(LoopCases))]
    public void LoopCallableFlow_JoinsZeroAndRepeatedExecutions(
        string kind, string scenario, string modifier, bool array)
    {
        var nullableType = array ? "?[str]" : "?str";
        var nonNullableType = array ? "[str]" : "str";
        var header = kind switch
        {
            "while" => scenario == "zero" ? "§WH false" : "§WH flag",
            "for" => scenario == "zero" ? "§L{i:1:0:1}" : "§L{i:1:2:1}",
            "foreach" => scenario == "zero" ? "§EACH{i} \"\"" : "§EACH{i} \"xx\"",
            "do" => "§DO",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var setup = scenario == "zero" ? "§B{~later:Action} mutator" : "§B{~later:Action} noopAction";
        var body = scenario switch
        {
            "zero" => "§ASSIGN later noopAction",
            "backedge" => """
                §C{later} §/C
                §C{Take} §A value §/C
                §ASSIGN later mutator
                """,
            "alias-backedge" => """
                §B{alias:Action} later
                §C{alias} §/C
                §C{Take} §A value §/C
                §ASSIGN later mutator
                """,
            "reguard" => """
                §C{later} §/C
                §IF (!= value null)
                  §C{Take} §A value §/C
                §ASSIGN later mutator
                """,
            "unused" => """
                §B{unused:Action} §LAM{}
                  §ASSIGN later mutator
                §/LAM
                §C{later} §/C
                §C{Take} §A value §/C
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var after = scenario == "zero"
            ? "§C{later} §/C\n§C{Take} §A value §/C"
            : "";
        var source = $$"""
            §M{CallableLoops}
              §F{Noop:pub} () -> void
                §E{}
              §F{Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{Take:pub} ({{nonNullableType}}:value) -> void
                §E{}
              §F{Probe:pub} ({{nullableType}}:value, bool:flag) -> void
                §IF (!= value null)
                  §B{noopAction:Action} §LAM{} §C{Noop} §/C §/LAM
                  §B{mutator:Action} §LAM{} §C{Mutate} §A{ {{modifier}} } value §/C §/LAM
                  {{setup}}
                  {{header}}
            {{Indent(body, 8)}}
            {{(kind == "do" ? "      flag" : "")}}
            {{Indent(after, 6)}}
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        new Binder(diagnostics).Bind(module);
        Assert.DoesNotContain(diagnostics.Errors, diagnostic =>
            diagnostic.Code != DiagnosticCode.NullableArgumentToNonNullableParameter);
        Assert.Equal(scenario is not ("reguard" or "unused"),
            diagnostics.Any(diagnostic =>
                diagnostic.Code == DiagnosticCode.NullableArgumentToNonNullableParameter
                && BindingDiagnosticPolicy.IsCompilationError(diagnostic)));
    }

    public static IEnumerable<object[]> BranchFallthroughCases()
    {
        foreach (var scenario in new[]
        {
            "elseif-else", "elseif-condition", "elseif-exit",
            "match-exit", "match-arm", "match-guard", "match-next-guard", "match-no-default"
        })
        foreach (var safe in new[] { false, true })
        foreach (var modifier in new[] { "ref", "out" })
        foreach (var shape in new[] { "string", "array", "nominal" })
        foreach (var mode in new[] { "default", "type-off", "effects-off", "transpile", "verify" })
            yield return [scenario, safe, modifier, shape, mode];
    }

    [Theory]
    [MemberData(nameof(BranchFallthroughCases))]
    public void ProductionBranchFallthrough_RetainsEffectsAndIsolatesBodies(
        string scenario, bool safe, string modifier, string shape, string mode)
    {
        var requiredType = shape switch
        {
            "string" => "str",
            "array" => "[str]",
            "nominal" => "Foo",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var nullableType = "?" + requiredType;
        var sink = safe
            ? "§IF (!= value null)\n  §C{Take} §A value §/C"
            : "§C{Take} §A value §/C";
        var body = scenario switch
        {
            "elseif-else" => $$"""
                §IF flag
                  §C{Noop} §/C
                §EI §C{setter} §/C
                  §C{Noop} §/C
                §EL
                  §C{later} §/C
                {{Indent(sink, 2)}}
                """,
            "elseif-condition" => $$"""
                §IF flag
                  §C{Noop} §/C
                §EI §C{setter} §/C
                  §C{Noop} §/C
                §EI §C{testLater} §/C
                  §C{Noop} §/C
                §EL
                {{Indent(sink, 2)}}
                """,
            "elseif-exit" => $$"""
                §IF flag
                  §C{Noop} §/C
                §EI §C{setter} §/C
                  §C{Noop} §/C
                §C{later} §/C
                {{sink}}
                """,
            "match-exit" => $$"""
                §ASSIGN later mutator
                §W flag
                  §K true
                    §ASSIGN later noopAction
                  §K _
                    {{(safe ? "§ASSIGN later noopAction" : "§C{Noop} §/C")}}
                §C{later} §/C
                §C{Take} §A value §/C
                """,
            "match-arm" => $$"""
                §ASSIGN later mutator
                §W flag
                  §K true
                    §ASSIGN later noopAction
                  §K _
                    §C{later} §/C
                {{Indent(sink, 4)}}
                """,
            "match-guard" => $$"""
                §W flag
                  §K true §WHEN §C{setter} §/C
                    §C{Noop} §/C
                  §K _
                    §C{later} §/C
                {{Indent(sink, 4)}}
                """,
            "match-next-guard" => $$"""
                §W flag
                  §K true §WHEN §C{setter} §/C
                    §C{Noop} §/C
                  §K true §WHEN §C{testLater} §/C
                    §C{Noop} §/C
                  §K _
                {{Indent(sink, 4)}}
                """,
            "match-no-default" => $$"""
                §ASSIGN later mutator
                §W flag
                  §K true
                    §ASSIGN later noopAction
                {{(safe ? "  §K _\n    §ASSIGN later noopAction" : "")}}
                §C{later} §/C
                §C{Take} §A value §/C
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var source = $$"""
            §M{BranchFallthrough}
            {{(shape == "nominal" ? "  §CL{Foo:pub}\n    §MT{Marker:pub} () -> i32\n      §R 0" : "")}}
              §F{Noop:pub} () -> void
                §E{}
              §F{Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{Take:pub} ({{requiredType}}:value) -> void
                §E{}
              §F{Probe:pub} ({{nullableType}}:value, bool:flag) -> void
                §IF (!= value null)
                  §B{noopAction:Action} §LAM{} §C{Noop} §/C §/LAM
                  §B{mutator:Action} §LAM{} §C{Mutate} §A{ {{modifier}} } value §/C §/LAM
                  §B{~later:Action} noopAction
                  §B{setter:Func<bool>} §LAM{}
                    §ASSIGN later mutator
                    §R false
                  §/LAM
                  §B{testLater:Func<bool>} §LAM{}
                    §C{later} §/C
                    §R false
                  §/LAM
            {{Indent(body, 6)}}
            """;
        var result = CompileInMode(source, mode);
        if (safe)
        {
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
        else
        {
            Assert.True(result.HasErrors, result.GeneratedCode);
            var diagnostic = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("value", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
            var receivingShape = shape switch
            {
                "array" => BindingReceivingShape.Array,
                "nominal" => BindingReceivingShape.Nominal,
                _ => BindingReceivingShape.ScalarString
            };
            Assert.Equal(receivingShape, diagnostic.BindingContext?.Shape);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
            Assert.Empty(result.GeneratedCode);
        }
    }

    public static IEnumerable<object[]> LoopDiagnosticCases()
    {
        foreach (var kind in new[] { "outside", "for", "while", "foreach", "do", "nested" })
        foreach (var valid in new[] { false, true })
        foreach (var mode in new[] { "default", "type-off", "effects-off", "transpile", "verify" })
            yield return [kind, valid, mode];
    }

    [Theory]
    [MemberData(nameof(LoopDiagnosticCases))]
    public void ProductionLoopDiscovery_DoesNotConsumeMisplacedRowDiagnostic(
        string kind, bool valid, string mode)
    {
        var binding = $"§B{{x:i32}} {(valid ? "" : "§E{} ")}1";
        var body = LoopBody(kind, binding);
        var source = $$"""
            §M{LoopDiagnostics}
              §F{Probe:pub} () -> void
            {{Indent(body, 4)}}
            """;
        var result = CompileInMode(source, mode);
        if (valid)
        {
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
        else
        {
            Assert.True(result.HasErrors, result.GeneratedCode);
            var diagnostic = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.EffectRowMisplaced, diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("§E", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData("outside", false)]
    [InlineData("outside", true)]
    [InlineData("for", false)]
    [InlineData("for", true)]
    [InlineData("while", false)]
    [InlineData("while", true)]
    [InlineData("foreach", false)]
    [InlineData("foreach", true)]
    [InlineData("do", false)]
    [InlineData("do", true)]
    [InlineData("nested", false)]
    [InlineData("nested", true)]
    public void LoopDiscovery_PreservesIncompleteAnalysisDiagnosticDeduplication(
        string kind, bool previouslyReported)
    {
        var body = LoopBody(kind, "§B{x:i32} §CS{2}");
        var source = $$"""
            §M{LoopIncompleteAnalysis}
              §F{Probe:pub} () -> void
            {{(previouslyReported ? "    §B{before:i32} §CS{1}" : "")}}
            {{Indent(body, 4)}}
                §B{after:i32} §CS{3}
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        new Binder(diagnostics).Bind(module);
        Assert.Empty(diagnostics.Errors);
        var diagnostic = Assert.Single(diagnostics.Where(item =>
            item.Code == DiagnosticCode.AnalysisUnsupportedNode));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal(source.IndexOf("§CS", StringComparison.Ordinal), diagnostic.Span.Start);
        foreach (var mode in new[] { "default", "type-off", "effects-off", "transpile", "verify" })
        {
            var result = CompileInMode(source, mode);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
    }

    private static string LoopBody(string kind, string body) => kind switch
    {
        "outside" => body,
        "for" => "§L{i:1:2:1}\n" + Indent(body, 2),
        "while" => "§WH false\n" + Indent(body, 2),
        "foreach" => "§EACH{item} \"ab\"\n" + Indent(body, 2),
        "do" => "§DO\n" + Indent(body, 2) + "\nfalse",
        "nested" => "§L{i:1:2:1}\n  §WH false\n" + Indent(body, 4),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static IEnumerable<object[]> ExceptionalAndExpressionCases()
    {
        foreach (var scenario in new[]
        {
            "conditional", "coalesce", "and", "or", "try-reset", "catch-reset",
            "catch-isolation", "try-prefix", "try-call-prefix", "finally-prefix",
            "catch-prefix-finally", "filter-fallthrough", "unused-try", "finally-reset",
            "filter-wrapper", "filter-unused", "filter-write", "filter-write-wrapper", "filter-write-unused"
        })
        foreach (var shape in new[] { "string", "array", "nominal" })
        foreach (var modifier in new[] { "ref", "out" })
        foreach (var mode in new[] { "default", "type-off", "effects-off", "transpile", "verify" })
            yield return [scenario, shape, modifier, mode];
    }

    [Theory]
    [MemberData(nameof(ExceptionalAndExpressionCases))]
    public void ProductionExceptionalAndExpressionPaths_PreserveViableCallableTargets(
        string scenario, string shape, string modifier, string mode)
    {
        foreach (var safe in new[] { false, true })
        {
            var requiredType = shape switch
            {
                "string" => "str",
                "array" => "[str]",
                _ => "Foo"
            };
            var receive = $$"""
                §IF (!= value null)
                  §C{later} §/C
                {{(safe ? "  §IF (!= value null)\n    §C{Take} §A value §/C" : "  §C{Take} §A value §/C")}}
                """;
            var filterWrite = scenario.StartsWith("filter-write", StringComparison.Ordinal);
            if (filterWrite)
                receive = safe ? "§IF (!= value null)\n  §C{Take} §A value §/C"
                    : "§C{Take} §A value §/C";
            var initialNoop = scenario is "try-prefix" or "try-call-prefix" or "finally-prefix"
                or "catch-prefix-finally" or "filter-fallthrough" or "filter-wrapper" or "filter-unused";
            var receiveInside = scenario is "catch-isolation" or "try-prefix" or "try-call-prefix"
                or "finally-prefix" or "catch-prefix-finally" or "filter-fallthrough" || filterWrite;
            var body = scenario switch
            {
                "conditional" => "§B{selected:bool} (? flag §C{resetter} §/C false)",
                "coalesce" => "§B{selected:?str} (?? text §C{resetString} §/C)",
                "and" => "§B{selected:bool} (&& flag §C{resetter} §/C)",
                "or" => "§B{selected:bool} (|| flag §C{resetter} §/C)",
                "try-reset" => """
                    §TR
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §CA{Exception:ex}
                      §C{Noop} §/C
                    """,
                "catch-reset" => """
                    §TR
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex}
                      §ASSIGN later noopAction
                    """,
                "catch-isolation" => $$"""
                    §TR
                      §C{MayThrow} §A flag §/C
                    §CA{ArgumentException:ex}
                      §ASSIGN later noopAction
                    §CA{Exception:other}
                    {{Indent(receive, 2)}}
                    """,
                "try-prefix" or "try-call-prefix" => $$"""
                    §TR
                      {{(scenario == "try-prefix" ? "§ASSIGN later mutator" : "§B{installed:bool} §C{setter} §/C")}}
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §CA{Exception:ex}
                    {{Indent(receive, 2)}}
                    """,
                "finally-prefix" => $$"""
                    §TR
                      §ASSIGN later mutator
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §FI
                    {{Indent(receive, 2)}}
                    """,
                "catch-prefix-finally" => $$"""
                    §TR
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex}
                      §ASSIGN later mutator
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §FI
                    {{Indent(receive, 2)}}
                    """,
                "filter-fallthrough" => $$"""
                    §TR
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex} §WHEN (&& (!= ex null) §C{setter} §/C)
                      §C{Noop} §/C
                    §CA{Exception:ex} §WHEN (!= ex null)
                    {{Indent(receive, 2)}}
                    """,
                "filter-write" => $$"""
                    §IF (!= value null)
                      §TR
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex} §WHEN (&& (!= ex null) §C{writeFilter} §/C)
                        §C{Noop} §/C
                      §CA{Exception:ex} §WHEN (!= ex null)
                    {{Indent(receive, 4)}}
                    """,
                "filter-write-wrapper" or "filter-write-unused" => $$"""
                    §B{wrapper:Action} §LAM{}
                      §TR
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex} §WHEN (&& (!= ex null) §C{writeFilter} §/C)
                        §C{Noop} §/C
                      §CA{Exception:ex} §WHEN (!= ex null)
                        §C{Noop} §/C
                    §/LAM
                    §IF (!= value null)
                      {{(scenario == "filter-write-wrapper" ? "§C{wrapper} §/C" : "§C{Noop} §/C")}}
                    {{Indent(receive, 2)}}
                    """,
                "filter-wrapper" or "filter-unused" => $$"""
                    §B{wrapper:Action} §LAM{}
                      §TR
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex} §WHEN §C{setter} §/C
                        §C{Noop} §/C
                      §CA{Exception:other}
                        §C{Noop} §/C
                    §/LAM
                    {{(scenario == "filter-wrapper" ? "§C{wrapper} §/C" : "")}}
                    """,
                "unused-try" => """
                    §B{unused:Action} §LAM{}
                      §TR
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex}
                        §ASSIGN later noopAction
                    §/LAM
                    """,
                "finally-reset" => """
                    §TR
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex}
                      §C{Noop} §/C
                    §FI
                      §ASSIGN later noopAction
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var source = $$"""
                §M{ExceptionalCallableFlow}
                {{(shape == "nominal" ? "  §CL{Foo:pub}\n    §MT{Marker:pub} () -> i32\n      §R 0" : "")}}
                  §F{Noop:pub} () -> void
                    §E{}
                  §F{MayThrow:pub} (bool:flag) -> void
                    §E{throw}
                    §IF flag
                      §TH §CS{new Exception()}
                  §F{Mutate:pub} (?{{requiredType}}:value:{{modifier}}) -> void
                    §ASSIGN value null
                  §F{Take:pub} ({{requiredType}}:value) -> void
                    §E{}
                  §F{Probe:pub} (?{{requiredType}}:value, bool:flag, ?str:text) -> void
                    §E{throw}
                    §B{noopAction:Action} §LAM{} §C{Noop} §/C §/LAM
                    §B{mutator:Action} §LAM{} §C{Mutate} §A{ {{modifier}} } value §/C §/LAM
                    §B{~later:Action} {{(initialNoop ? "noopAction" : "mutator")}}
                    §B{resetter:Func<bool>} §LAM{}
                      §ASSIGN later noopAction
                      §R false
                    §/LAM
                    §B{resetString:Func<str>} §LAM{}
                      §ASSIGN later noopAction
                      §R "done"
                    §/LAM
                    §B{setter:Func<bool>} §LAM{}
                      §ASSIGN later mutator
                      §R false
                    §/LAM
                    §B{writeFilter:Func<bool>} §LAM{}
                      §C{Mutate} §A{ {{modifier}} } value §/C
                      §R false
                    §/LAM
                {{Indent(body, 4)}}
                {{(receiveInside ? "" : Indent(receive, 4))}}
                """;
            var result = CompileInMode(source, mode);
            if (safe || scenario is "finally-reset" or "filter-unused" or "filter-write-unused")
            {
                Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
                Assert.NotEmpty(result.GeneratedCode);
            }
            else
            {
                Assert.True(result.HasErrors, result.GeneratedCode);
                var diagnostic = Assert.Single(result.Diagnostics.Errors);
                Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
                Assert.Equal("value", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
                Assert.Equal(shape switch
                {
                    "array" => BindingReceivingShape.Array,
                    "nominal" => BindingReceivingShape.Nominal,
                    _ => BindingReceivingShape.ScalarString
                }, diagnostic.BindingContext?.Shape);
                Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
                Assert.Empty(result.GeneratedCode);
            }
        }
    }

    [Fact]
    public void CatchFilterTraversal_PreservesExistingConstructorCallersAndEvaluationOrder()
    {
        var statement = new BoundExpressionStatement(default, new BoundIntLiteral(default, 1));
        var unfiltered = new BoundCatchClause(default, "Exception", null, [statement]);
        Assert.Null(unfiltered.Filter);
        Assert.Same(statement, Assert.Single(unfiltered.ChildNodes));

        var filter = new BoundBoolLiteral(default, false);
        var filtered = new BoundCatchClause(default, "Exception", null, [statement], filter);
        Assert.Same(filter, filtered.Filter);
        Assert.Collection(filtered.ChildNodes,
            child => Assert.Same(filter, child),
            child => Assert.Same(statement, child));
    }

    public static IEnumerable<object[]> HiddenCapturedGuardCases()
    {
        foreach (var join in new[] { "if", "match", "try", "finally" })
        foreach (var control in new[] { "shadow", "renamed", "fresh", "no-mutation", "other-storage" })
        foreach (var shape in new[] { "string", "array", "nominal" })
        foreach (var modifier in new[] { "ref", "out" })
        foreach (var mode in new[] { "default", "type-off", "effects-off", "transpile", "verify" })
            yield return [join, control, shape, modifier, mode];
    }

    [Theory]
    [MemberData(nameof(HiddenCapturedGuardCases))]
    public void ProductionHiddenCapturedGuard_JoinPreservesStorageInvalidation(
        string join, string control, string shape, string modifier, string mode)
    {
        var type = shape switch
        {
            "string" => "str",
            "array" => "[str]",
            _ => "Foo"
        };
        var call = control == "no-mutation" ? "§C{Noop} §/C" : "§C{mutator} §/C";
        var body = join switch
        {
            "if" => "§IF flag\n" + Indent(call, 2),
            "match" => "§W flag\n  §K true\n" + Indent(call, 4)
                + "\n  §K _\n    §C{Noop} §/C",
            "try" => "§TR\n" + Indent(call, 2)
                + "\n§CA{Exception:ex}\n  §C{Noop} §/C",
            "finally" => "§TR\n" + Indent(call, 2)
                + "\n§FI\n  §C{Noop} §/C",
            _ => throw new ArgumentOutOfRangeException(nameof(join))
        };
        var source = $$"""
            §M{ShadowedCapture}
            {{(shape == "nominal" ? "  §CL{Foo:pub}\n    §MT{Marker:pub} () -> i32\n      §R 0" : "")}}
              §CL{Holder:pub}
                §FLD{?{{type}}:value:pub}
                §FLD{?{{type}}:otherValue:pub}
                §MT{Noop:pub} () -> void
                  §E{}
                §MT{Mutate:pub} (?{{type}}:target:{{modifier}}) -> void
                  §ASSIGN target null
                §MT{Take:pub} ({{type}}:target) -> void
                  §E{}
                §MT{Probe:pub} (bool:flag) -> void
                  §E{mut}
                  §IF (!= value null)
                    §B{mutator:Action} §LAM{} §C{Mutate} §A{ {{modifier}} } {{(control == "other-storage" ? "otherValue" : "value")}} §/C §/LAM
                    §W flag
                      §K §VAR{ {{(control == "renamed" ? "other" : "value")}} }
            {{Indent(body, 12)}}
            {{(control == "fresh" ? "        §IF (!= value null)\n          §C{Take} §A value §/C" : "        §C{Take} §A value §/C")}}
            """;
        var result = CompileInMode(source, mode);
        if (control is "fresh" or "no-mutation" or "other-storage")
        {
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
        }
        else
        {
            Assert.True(result.HasErrors, result.GeneratedCode);
            var diagnostic = Assert.Single(result.Diagnostics.Errors);
            Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("value", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
            Assert.Equal(shape switch
            {
                "array" => BindingReceivingShape.Array,
                "nominal" => BindingReceivingShape.Nominal,
                _ => BindingReceivingShape.ScalarString
            }, diagnostic.BindingContext?.Shape);
            Assert.True(BindingDiagnosticPolicy.IsCompilationError(diagnostic));
            Assert.Empty(result.GeneratedCode);
        }
    }

    private static CompilationResult CompileInMode(string source, string mode) =>
        Program.Compile(source, "callable-control-flow.calr", new CompilationOptions
        {
            EnableTypeChecking = mode != "type-off",
            EnforceEffects = mode != "effects-off",
            UnsafeTranspileOnly = mode == "transpile",
            VerifyContracts = mode == "verify",
            StatusWriter = TextWriter.Null
        });
}
