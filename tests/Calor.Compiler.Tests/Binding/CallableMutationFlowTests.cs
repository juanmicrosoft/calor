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
}
