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
        var mutator = $"§LAM{{mut}} §C{{Mutate}} §A{{{modifier}}} value §/C §/LAM{{mut}}";
        var body = scenario switch
        {
            "call-before-reset" => $$"""
                §B{~later:Action} {{mutator}}
                §B{wrapper:Action} §LAM{wrap}
                  §C{later} §/C
                  §ASSIGN later Noop
                §/LAM{wrap}
                §C{wrapper} §/C
                """,
            "block-local-alias" => $$"""
                §B{later:Action} {{mutator}}
                §B{wrapper:Action} §LAM{wrap}
                  §IF{branch} flag
                    §B{alias:Action} later
                    §C{alias} §/C
                §/LAM{wrap}
                §C{wrapper} §/C
                """,
            "expression-call-before-reset" => $$"""
                §B{~later:Func<bool>} §LAM{mut} §C{MutateBool} §A{ {{modifier}} } value §/C §/LAM{mut}
                §B{wrapper:Func<bool>} §LAM{wrap}
                  §B{result:bool} §C{later} §/C
                  §ASSIGN later NoopBool
                  §R result
                §/LAM{wrap}
                §B{result:bool} §C{wrapper} §/C
                """,
            "late-target" => $$"""
                §B{~later:Action} Noop
                §B{wrapper:Action} §LAM{wrap} §C{later} §/C §/LAM{wrap}
                §ASSIGN later {{mutator}}
                §C{wrapper} §/C
                """,
            "branch-retains-target" => $$"""
                §B{~later:Action} {{mutator}}
                §IF{branch} flag
                  §ASSIGN later Noop
                §C{later} §/C
                """,
            "branch-isolation" => $$"""
                §B{~later:Action} {{mutator}}
                §IF{branch} flag
                  §ASSIGN later Noop
                §EL
                  §B{wrapper:Action} §LAM{wrap} §C{later} §/C §/LAM{wrap}
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
                §B{setter:Action} §LAM{set}
                  §ASSIGN later mutator
                §/LAM{set}
                §C{setter} §/C
                §C{later} §/C
                """,
            "nested-deferred-setter" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{set}
                  §ASSIGN later mutator
                §/LAM{set}
                §B{wrapper:Action} §LAM{wrap} §C{setter} §/C §/LAM{wrap}
                §C{wrapper} §/C
                §C{later} §/C
                """,
            "deferred-rebind" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{set}
                  §B{~later} mutator
                §/LAM{set}
                §C{setter} §/C
                §C{later} §/C
                """,
            "late-write-source" => $$"""
                §B{~later:Action} Noop
                §B{~mutator:Action} Noop
                §B{setter:Action} §LAM{set}
                  §ASSIGN later mutator
                §/LAM{set}
                §ASSIGN mutator {{mutator}}
                §C{setter} §/C
                §C{later} §/C
                """,
            "write-source-fixed-point" => $$"""
                §B{~later:Action} Noop
                §B{~source:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{set}
                  §ASSIGN source mutator
                §/LAM{set}
                §B{wrapper:Action} §LAM{wrap}
                  §C{setter} §/C
                  §ASSIGN later source
                §/LAM{wrap}
                §C{wrapper} §/C
                §C{later} §/C
                """,
            "setter-does-not-invoke-value" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{set}
                  §ASSIGN later mutator
                §/LAM{set}
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
                §B{wrapper:Action} §LAM{wrap} §C{later.Invoke} §/C §/LAM{wrap}
                §C{wrapper.Invoke} §/C
                """,
            "direct-write" => """
                §B{later:Action} §LAM{mut}
                  §ASSIGN value null
                §/LAM{mut}
                §C{later} §/C
                """,
            "nested-guard" => $$"""
                §B{later:Action} §LAM{mut}
                  §IF{inner} (!= value null)
                    §C{Mutate} §A{ {{modifier}} } value §/C
                §/LAM{mut}
                §C{later} §/C
                """,
            "repeat-invocation" => $$"""
                §B{later:Action} {{mutator}}
                §C{later} §/C
                §IF{reguard} (!= value null)
                  §C{later} §/C
                  §C{Take} §A value §/C
                """,
            "recursive-dependencies" => $$"""
                §B{~first:Action} Noop
                §B{~second:Action} Noop
                §B{mutator:Action} {{mutator}}
                §ASSIGN first §LAM{one} §C{second} §/C §/LAM{one}
                §ASSIGN second §LAM{two}
                  §C{first} §/C
                  §C{mutator} §/C
                §/LAM{two}
                §C{first} §/C
                """,
            "pure-cycle" => """
                §B{~first:Action} Noop
                §B{~second:Action} Noop
                §ASSIGN first §LAM{one} §C{second} §/C §/LAM{one}
                §ASSIGN second §LAM{two} §C{first} §/C §/LAM{two}
                §C{first} §/C
                """,
            "uninvoked-setter" => $$"""
                §B{~later:Action} Noop
                §B{mutator:Action} {{mutator}}
                §B{setter:Action} §LAM{set}
                  §ASSIGN later mutator
                §/LAM{set}
                §C{later} §/C
                """,
            "uninvoked-mutator" => $$"""
                §B{later:Action} {{mutator}}
                """,
            "uninvoked-reset" => $$"""
                §B{~later:Action} {{mutator}}
                §B{setter:Action} §LAM{set}
                  §ASSIGN later Noop
                §/LAM{set}
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
                §IF{branch} flag
                  §ASSIGN later Noop
                §EL
                  §ASSIGN later Noop
                §C{later} §/C
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var source = $$"""
            §M{m:CallableMutationFlow}
              §F{noop:Noop:pub} () -> void
                §E{}
              §F{mutate:Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{noopBool:NoopBool:pub} () -> bool
                §R true
              §F{mutateBool:MutateBool:pub} ({{nullableType}}:value:{{modifier}}) -> bool
                §ASSIGN value null
                §R true
              §F{take:Take:pub} ({{nonNullableType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{nullableType}}:value, bool:flag) -> void
                §IF{guard} (!= value null)
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
            §M{m:CallableShadow}
              §F{mutate:Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{take:Take:pub} ({{nonNullableType}}:value) -> void
                §E{}
              §CL{holder:Holder:pub}
                §FLD{ {{nullableType}} :value:pub}
                §MT{probe:Probe:pub} () -> void
                  §IF{guard} (!= value null)
                    §B{later:Action<{{nullableType}}>} §LAM{mut:value:{{nullableType}}}
                      §C{Mutate} §A{ {{modifier}} } value §/C
                    §/LAM{mut}
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
            "while" => scenario == "zero" ? "§WH{loop} false" : "§WH{loop} flag",
            "for" => scenario == "zero" ? "§L{loop:i:1:0:1}" : "§L{loop:i:1:2:1}",
            "foreach" => scenario == "zero" ? "§EACH{loop:i} \"\"" : "§EACH{loop:i} \"xx\"",
            "do" => "§DO{loop}",
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
                §IF{fresh} (!= value null)
                  §C{Take} §A value §/C
                §ASSIGN later mutator
                """,
            "unused" => """
                §B{unused:Action} §LAM{set}
                  §ASSIGN later mutator
                §/LAM{set}
                §C{later} §/C
                §C{Take} §A value §/C
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var after = scenario == "zero"
            ? "§C{later} §/C\n§C{Take} §A value §/C"
            : "";
        var source = $$"""
            §M{m:CallableLoops}
              §F{noop:Noop:pub} () -> void
                §E{}
              §F{mutate:Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{take:Take:pub} ({{nonNullableType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{nullableType}}:value, bool:flag) -> void
                §IF{guard} (!= value null)
                  §B{noopAction:Action} §LAM{noopLambda} §C{Noop} §/C §/LAM{noopLambda}
                  §B{mutator:Action} §LAM{mut} §C{Mutate} §A{ {{modifier}} } value §/C §/LAM{mut}
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
            ? "§IF{fresh} (!= value null)\n  §C{Take} §A value §/C"
            : "§C{Take} §A value §/C";
        var body = scenario switch
        {
            "elseif-else" => $$"""
                §IF{select} flag
                  §C{Noop} §/C
                §EI §C{setter} §/C
                  §C{Noop} §/C
                §EL
                  §C{later} §/C
                {{Indent(sink, 2)}}
                """,
            "elseif-condition" => $$"""
                §IF{select} flag
                  §C{Noop} §/C
                §EI §C{setter} §/C
                  §C{Noop} §/C
                §EI §C{testLater} §/C
                  §C{Noop} §/C
                §EL
                {{Indent(sink, 2)}}
                """,
            "elseif-exit" => $$"""
                §IF{select} flag
                  §C{Noop} §/C
                §EI §C{setter} §/C
                  §C{Noop} §/C
                §C{later} §/C
                {{sink}}
                """,
            "match-exit" => $$"""
                §ASSIGN later mutator
                §W{select} flag
                  §K true
                    §ASSIGN later noopAction
                  §K _
                    {{(safe ? "§ASSIGN later noopAction" : "§C{Noop} §/C")}}
                §C{later} §/C
                §C{Take} §A value §/C
                """,
            "match-arm" => $$"""
                §ASSIGN later mutator
                §W{select} flag
                  §K true
                    §ASSIGN later noopAction
                  §K _
                    §C{later} §/C
                {{Indent(sink, 4)}}
                """,
            "match-guard" => $$"""
                §W{select} flag
                  §K true §WHEN §C{setter} §/C
                    §C{Noop} §/C
                  §K _
                    §C{later} §/C
                {{Indent(sink, 4)}}
                """,
            "match-next-guard" => $$"""
                §W{select} flag
                  §K true §WHEN §C{setter} §/C
                    §C{Noop} §/C
                  §K true §WHEN §C{testLater} §/C
                    §C{Noop} §/C
                  §K _
                {{Indent(sink, 4)}}
                """,
            "match-no-default" => $$"""
                §ASSIGN later mutator
                §W{select} flag
                  §K true
                    §ASSIGN later noopAction
                {{(safe ? "  §K _\n    §ASSIGN later noopAction" : "")}}
                §C{later} §/C
                §C{Take} §A value §/C
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var source = $$"""
            §M{m:BranchFallthrough}
            {{(shape == "nominal" ? "  §CL{c1:Foo:pub}\n    §MT{marker:Marker:pub} () -> i32\n      §R 0" : "")}}
              §F{noop:Noop:pub} () -> void
                §E{}
              §F{mutate:Mutate:pub} ({{nullableType}}:value:{{modifier}}) -> void
                §ASSIGN value null
              §F{take:Take:pub} ({{requiredType}}:value) -> void
                §E{}
              §F{probe:Probe:pub} ({{nullableType}}:value, bool:flag) -> void
                §IF{guard} (!= value null)
                  §B{noopAction:Action} §LAM{noopLambda} §C{Noop} §/C §/LAM{noopLambda}
                  §B{mutator:Action} §LAM{mutLambda} §C{Mutate} §A{ {{modifier}} } value §/C §/LAM{mutLambda}
                  §B{~later:Action} noopAction
                  §B{setter:Func<bool>} §LAM{setLambda}
                    §ASSIGN later mutator
                    §R false
                  §/LAM{setLambda}
                  §B{testLater:Func<bool>} §LAM{testLambda}
                    §C{later} §/C
                    §R false
                  §/LAM{testLambda}
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
            §M{m:LoopDiagnostics}
              §F{probe:Probe:pub} () -> void
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
            §M{m:LoopIncompleteAnalysis}
              §F{probe:Probe:pub} () -> void
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
        "for" => "§L{loop:i:1:2:1}\n" + Indent(body, 2),
        "while" => "§WH{loop} false\n" + Indent(body, 2),
        "foreach" => "§EACH{loop:item} \"ab\"\n" + Indent(body, 2),
        "do" => "§DO{loop}\n" + Indent(body, 2) + "\nfalse",
        "nested" => "§L{outer:i:1:2:1}\n  §WH{inner} false\n" + Indent(body, 4),
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
                §IF{fresh} (!= value null)
                  §C{later} §/C
                {{(safe ? "  §IF{again} (!= value null)\n    §C{Take} §A value §/C" : "  §C{Take} §A value §/C")}}
                """;
            var filterWrite = scenario.StartsWith("filter-write", StringComparison.Ordinal);
            if (filterWrite)
                receive = safe ? "§IF{again} (!= value null)\n  §C{Take} §A value §/C"
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
                    §TR{tr}
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §CA{Exception:ex}
                      §C{Noop} §/C
                    """,
                "catch-reset" => """
                    §TR{tr}
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex}
                      §ASSIGN later noopAction
                    """,
                "catch-isolation" => $$"""
                    §TR{tr}
                      §C{MayThrow} §A flag §/C
                    §CA{ArgumentException:ex}
                      §ASSIGN later noopAction
                    §CA{Exception:other}
                    {{Indent(receive, 2)}}
                    """,
                "try-prefix" or "try-call-prefix" => $$"""
                    §TR{tr}
                      {{(scenario == "try-prefix" ? "§ASSIGN later mutator" : "§B{installed:bool} §C{setter} §/C")}}
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §CA{Exception:ex}
                    {{Indent(receive, 2)}}
                    """,
                "finally-prefix" => $$"""
                    §TR{tr}
                      §ASSIGN later mutator
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §FI
                    {{Indent(receive, 2)}}
                    """,
                "catch-prefix-finally" => $$"""
                    §TR{tr}
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex}
                      §ASSIGN later mutator
                      §C{MayThrow} §A flag §/C
                      §ASSIGN later noopAction
                    §FI
                    {{Indent(receive, 2)}}
                    """,
                "filter-fallthrough" => $$"""
                    §TR{tr}
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex} §WHEN (&& (!= ex null) §C{setter} §/C)
                      §C{Noop} §/C
                    §CA{Exception:ex} §WHEN (!= ex null)
                    {{Indent(receive, 2)}}
                    """,
                "filter-write" => $$"""
                    §IF{initial} (!= value null)
                      §TR{tr}
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex} §WHEN (&& (!= ex null) §C{writeFilter} §/C)
                        §C{Noop} §/C
                      §CA{Exception:ex} §WHEN (!= ex null)
                    {{Indent(receive, 4)}}
                    """,
                "filter-write-wrapper" or "filter-write-unused" => $$"""
                    §B{wrapper:Action} §LAM{wrapperLambda}
                      §TR{tr}
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex} §WHEN (&& (!= ex null) §C{writeFilter} §/C)
                        §C{Noop} §/C
                      §CA{Exception:ex} §WHEN (!= ex null)
                        §C{Noop} §/C
                    §/LAM{wrapperLambda}
                    §IF{initial} (!= value null)
                      {{(scenario == "filter-write-wrapper" ? "§C{wrapper} §/C" : "§C{Noop} §/C")}}
                    {{Indent(receive, 2)}}
                    """,
                "filter-wrapper" or "filter-unused" => $$"""
                    §B{wrapper:Action} §LAM{wrapperLambda}
                      §TR{tr}
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex} §WHEN §C{setter} §/C
                        §C{Noop} §/C
                      §CA{Exception:other}
                        §C{Noop} §/C
                    §/LAM{wrapperLambda}
                    {{(scenario == "filter-wrapper" ? "§C{wrapper} §/C" : "")}}
                    """,
                "unused-try" => """
                    §B{unused:Action} §LAM{unusedLambda}
                      §TR{tr}
                        §C{MayThrow} §A flag §/C
                      §CA{Exception:ex}
                        §ASSIGN later noopAction
                    §/LAM{unusedLambda}
                    """,
                "finally-reset" => """
                    §TR{tr}
                      §C{MayThrow} §A flag §/C
                    §CA{Exception:ex}
                      §C{Noop} §/C
                    §FI
                      §ASSIGN later noopAction
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var source = $$"""
                §M{m:ExceptionalCallableFlow}
                {{(shape == "nominal" ? "  §CL{c1:Foo:pub}\n    §MT{marker:Marker:pub} () -> i32\n      §R 0" : "")}}
                  §F{noop:Noop:pub} () -> void
                    §E{}
                  §F{may:MayThrow:pub} (bool:flag) -> void
                    §E{throw}
                    §IF{throwIf} flag
                      §TH §CS{new Exception()}
                  §F{mut:Mutate:pub} (?{{requiredType}}:value:{{modifier}}) -> void
                    §ASSIGN value null
                  §F{take:Take:pub} ({{requiredType}}:value) -> void
                    §E{}
                  §F{probe:Probe:pub} (?{{requiredType}}:value, bool:flag, ?str:text) -> void
                    §E{throw}
                    §B{noopAction:Action} §LAM{noopLambda} §C{Noop} §/C §/LAM{noopLambda}
                    §B{mutator:Action} §LAM{mutLambda} §C{Mutate} §A{ {{modifier}} } value §/C §/LAM{mutLambda}
                    §B{~later:Action} {{(initialNoop ? "noopAction" : "mutator")}}
                    §B{resetter:Func<bool>} §LAM{resetLambda}
                      §ASSIGN later noopAction
                      §R false
                    §/LAM{resetLambda}
                    §B{resetString:Func<str>} §LAM{resetStringLambda}
                      §ASSIGN later noopAction
                      §R "done"
                    §/LAM{resetStringLambda}
                    §B{setter:Func<bool>} §LAM{setLambda}
                      §ASSIGN later mutator
                      §R false
                    §/LAM{setLambda}
                    §B{writeFilter:Func<bool>} §LAM{writeFilterLambda}
                      §C{Mutate} §A{ {{modifier}} } value §/C
                      §R false
                    §/LAM{writeFilterLambda}
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
