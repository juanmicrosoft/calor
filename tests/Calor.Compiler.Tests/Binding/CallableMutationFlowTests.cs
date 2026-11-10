using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.CodeGen;
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
        var nullableType = array ? "string[]?" : "string?";
        var nonNullableType = array ? "string[]" : "string";
        var mutator = $"() => Mutate({modifier} value)";
        var body = scenario switch
        {
            "call-before-reset" => $$"""
                var Action:later = {{mutator}}
                let Action:wrapper = () =>
                  later()
                  set later Noop
                wrapper()
                """,
            "block-local-alias" => $$"""
                let Action:later = {{mutator}}
                let Action:wrapper = () =>
                  if flag
                    let Action:alias = later
                    alias()
                wrapper()
                """,
            "expression-call-before-reset" => $$"""
                var Func<bool>:later = () => MutateBool({{modifier}} value)
                let Func<bool>:wrapper = () =>
                  let bool:result = later()
                  set later NoopBool
                  return result
                let bool:result = wrapper()
                """,
            "late-target" => $$"""
                var Action:later ! mut = Noop
                let Action:wrapper = () => later()
                set later {{mutator}}
                wrapper()
                """,
            "branch-retains-target" => $$"""
                var Action:later = {{mutator}}
                if flag
                  set later Noop
                later()
                """,
            "branch-isolation" => $$"""
                var Action:later = {{mutator}}
                if flag
                  set later Noop
                else
                  let Action:wrapper = () => later()
                  wrapper()
                """,
            "mutable-rebind" => $$"""
                var Action:later ! mut = Noop
                var later = {{mutator}}
                later()
                """,
            "deferred-setter" => $$"""
                var Action:later ! mut = Noop
                let Action:mutator = {{mutator}}
                let Action:setter = () =>
                  set later mutator
                setter()
                later()
                """,
            "nested-deferred-setter" => $$"""
                var Action:later ! mut = Noop
                let Action:mutator = {{mutator}}
                let Action:setter = () =>
                  set later mutator
                let Action:wrapper = () => setter()
                wrapper()
                later()
                """,
            "deferred-rebind" => $$"""
                var Action:later ! mut = Noop
                let Action:mutator = {{mutator}}
                let Action:setter = () =>
                  var later = mutator
                setter()
                later()
                """,
            "late-write-source" => $$"""
                var Action:later ! mut = Noop
                var Action:mutator ! mut = Noop
                let Action:setter = () =>
                  set later mutator
                set mutator {{mutator}}
                setter()
                later()
                """,
            "write-source-fixed-point" => $$"""
                var Action:later ! mut = Noop
                var Action:source ! mut = Noop
                let Action:mutator = {{mutator}}
                let Action:setter = () =>
                  set source mutator
                let Action:wrapper = () =>
                  setter()
                  set later source
                wrapper()
                later()
                """,
            "setter-does-not-invoke-value" => $$"""
                var Action:later ! mut = Noop
                let Action:mutator = {{mutator}}
                let Action:setter = () =>
                  set later mutator
                setter()
                """,
            "alias-initializer" => $$"""
                let Action:later = {{mutator}}
                let Action:alias = later
                alias()
                """,
            "alias-assignment" => $$"""
                let Action:later = {{mutator}}
                var Action:alias ! mut = Noop
                set alias later
                alias()
                """,
            "invoke-member" => $$"""
                let Action:later = {{mutator}}
                let Action:wrapper = () => later.Invoke()
                wrapper.Invoke()
                """,
            "direct-write" => """
                let Action:later = () =>
                  set value null
                later()
                """,
            "nested-guard" => $$"""
                let Action:later = () =>
                  if value != null
                    Mutate({{modifier}} value)
                later()
                """,
            "repeat-invocation" => $$"""
                let Action:later = {{mutator}}
                later()
                if value != null
                  later()
                  Take(value)
                """,
            "recursive-dependencies" => $$"""
                var Action:first = Noop
                var Action:second = Noop
                let Action:mutator = {{mutator}}
                set first () => second()
                set second () =>
                  first()
                  mutator()
                first()
                """,
            "pure-cycle" => """
                var Action:first = Noop
                var Action:second = Noop
                set first () => second()
                set second () => first()
                first()
                """,
            "uninvoked-setter" => $$"""
                var Action:later ! mut = Noop
                let Action:mutator = {{mutator}}
                let Action:setter = () =>
                  set later mutator
                later()
                """,
            "uninvoked-mutator" => $$"""
                let Action:later = {{mutator}}
                """,
            "uninvoked-reset" => $$"""
                var Action:later = {{mutator}}
                let Action:setter = () =>
                  set later Noop
                later()
                """,
            "alias-is-value-copy" => $$"""
                var Action:later ! mut = Noop
                let Action:alias ! mut = later
                set later {{mutator}}
                alias()
                """,
            "all-branches-reset" => $$"""
                var Action:later = {{mutator}}
                if flag
                  set later Noop
                else
                  set later Noop
                later()
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var source = $$"""
            module CallableMutationFlow
              func Noop public () → void
                !
              func Mutate public ({{modifier}} {{nullableType}}:value) → void
                ! mut
                set value null
              func NoopBool public () → bool
                return true
              func MutateBool public ({{modifier}} {{nullableType}}:value) → bool
                ! mut
                set value null
                return true
              func Take public ({{nonNullableType}}:value) → void
                !
              func Probe public ({{nullableType}}:value, bool:flag) → void
                ! mut
                if value != null
            {{Indent(body, 6)}}
                  Take(value)
            """;
        if (scenario == "alias-is-value-copy")
        {
            // Pin the original pure-row fixture's rejection alongside the legal value-copy control.
            var originalPureAlias = source
                .Replace("var Action:later ! mut = Noop", "var Action:later = Noop", StringComparison.Ordinal)
                .Replace("let Action:alias ! mut = later", "let Action:alias ! = later", StringComparison.Ordinal);
            var narrowedAlias = source.Replace(
                "let Action:alias ! mut = later", "let Action:alias ! = later", StringComparison.Ordinal);
            foreach (var (invalidSource, destination) in new[]
            {
                (originalPureAlias, "'later'"),
                (narrowedAlias, "binding 'alias'")
            })
            {
                var invalid = Program.Compile(invalidSource, "callable-mutation-flow.calr", new CompilationOptions());
                Assert.True(invalid.HasErrors);
                Assert.Empty(invalid.GeneratedCode);
                var mismatch = Assert.Single(invalid.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                Assert.Equal("Calor0424", mismatch.Code);
                Assert.Contains(destination, mismatch.Message);
                Assert.Contains("declared row: [pure]", mismatch.Message);
                Assert.Contains("Extra effect(s): mut", mismatch.Message);
                Assert.DoesNotContain(invalid.Diagnostics, d => d.Code is "Calor0425" or "Calor0411");
            }
        }
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
        var nativeNullableType = nullableType == "?[str]" ? "string[]?" : "string?";
        var nativeNonNullableType = nonNullableType == "[str]" ? "string[]" : "string";
        var source = $$"""
            module CallableShadow
              func Mutate public ({{modifier}} {{nativeNullableType}}:value) → void
                set value null
              func Take public ({{nativeNonNullableType}}:value) → void
                !
              class Holder
                field {{nativeNullableType}}:value public
                func Probe public () → void
                  if value != null
                    let Action<{{nativeNullableType}}>:later = ({{nativeNullableType}}:value) =>
                      Mutate({{modifier}} value)
                    later(value)
                    Take(value)
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
        var nullableType = array ? "string[]?" : "string?";
        var nonNullableType = array ? "string[]" : "string";
        var header = kind switch
        {
            "while" => scenario == "zero" ? "while false" : "while flag",
            "for" => scenario == "zero" ? "for i from 1 to 0 step 1" : "for i from 1 to 2 step 1",
            "foreach" => scenario == "zero" ? "each i in \"\"" : "each i in \"xx\"",
            "do" => "do",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var setup = scenario == "zero" ? "var Action:later = mutator" : "var Action:later = noopAction";
        var body = scenario switch
        {
            "zero" => "set later noopAction",
            "backedge" => """
                later()
                Take(value)
                set later mutator
                """,
            "alias-backedge" => """
                let Action:alias = later
                alias()
                Take(value)
                set later mutator
                """,
            "reguard" => """
                later()
                if value != null
                  Take(value)
                set later mutator
                """,
            "unused" => """
                let Action:unused = () =>
                  set later mutator
                later()
                Take(value)
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var after = scenario == "zero"
            ? "later()\nTake(value)"
            : "";
        var source = $$"""
            module CallableLoops
              func Noop public () → void
                !
              func Mutate public ({{modifier}} {{nullableType}}:value) → void
                set value null
              func Take public ({{nonNullableType}}:value) → void
                !
              func Probe public ({{nullableType}}:value, bool:flag) → void
                if value != null
                  let Action:noopAction = () => Noop()
                  let Action:mutator = () => Mutate({{modifier}} value)
                  {{setup}}
                  {{header}}
            {{Indent(body, 8)}}
            {{(kind == "do" ? "      while flag" : "")}}
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
            "string" => "string",
            "array" => "string[]",
            "nominal" => "Foo",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var nullableType = requiredType + "?";
        var sink = safe
            ? "if value != null\n  Take(value)"
            : "Take(value)";
        var body = scenario switch
        {
            "elseif-else" => $$"""
                if flag
                  Noop()
                else if setter()
                  Noop()
                else
                  later()
                {{Indent(sink, 2)}}
                """,
            "elseif-condition" => $$"""
                if flag
                  Noop()
                else if setter()
                  Noop()
                else if testLater()
                  Noop()
                {{(safe ? "else if value != null\n  Take(value)" : "else\n  Take(value)")}}
                """,
            "elseif-exit" => $$"""
                if flag
                  Noop()
                else if setter()
                  Noop()
                later()
                {{sink}}
                """,
            "match-exit" => $$"""
                set later mutator
                match flag
                  case true
                    set later noopAction
                  case _
                    {{(safe ? "set later noopAction" : "Noop()")}}
                later()
                Take(value)
                """,
            "match-arm" => $$"""
                set later mutator
                match flag
                  case true
                    set later noopAction
                  case _
                    later()
                {{Indent(sink, 4)}}
                """,
            "match-guard" => $$"""
                match flag
                  case true when setter()
                    Noop()
                  case _
                    later()
                {{Indent(sink, 4)}}
                """,
            "match-next-guard" => $$"""
                match flag
                  case true when setter()
                    Noop()
                  case true when testLater()
                    Noop()
                  case _
                {{Indent(sink, 4)}}
                """,
            "match-no-default" => $$"""
                set later mutator
                match flag
                  case true
                    set later noopAction
                {{(safe ? "  case _\n    set later noopAction" : "")}}
                later()
                Take(value)
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var source = $$"""
            module BranchFallthrough
            {{(shape == "nominal" ? "  class Foo\n    func Marker () → int\n      return 0" : "")}}
              func Noop public () → void
                !
              func Mutate public ({{modifier}} {{nullableType}}:value) → void
                ! mut
                set value null
              func Take public ({{requiredType}}:value) → void
                !
              func Probe public ({{nullableType}}:value, bool:flag) → void
                ! mut
                if value != null
                  let Action:noopAction = () => Noop()
                  let Action:mutator = () => Mutate({{modifier}} value)
                  var Action:later ! mut = noopAction
                  let Func<bool>:setter = () =>
                    set later mutator
                    return false
                  let Func<bool>:testLater = () =>
                    later()
                    return false
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
        var binding = valid ? "let int:x = 1" : "let int:x = 1\n!";
        var body = LoopBody(kind, binding);
        var source = $$"""
            module LoopDiagnostics
              func Probe public () → void
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
            Assert.Equal("!", source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
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
        var body = LoopBody(kind, "let int:x = 2");
        var source = $$"""
            module LoopIncompleteAnalysis
              func Probe public () → void
            {{(previouslyReported ? "    let int:before = 1" : "")}}
            {{Indent(body, 4)}}
                let int:after = 3
            """;
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.Empty(diagnostics.Errors);

        // Raw expressions remain internal AST nodes, not an accepted source spelling.
        var function = Assert.Single(module.Functions);
        var opaqueFunction = new FunctionNode(function.Span, function.Id, function.Name,
            function.Visibility, function.Parameters, function.Output, function.Effects,
            function.Body.Select(WithOpaqueBindingInitializers).ToArray(), function.Attributes);
        var opaqueModule = new ModuleNode(module.Span, module.Id, module.Name,
            module.Usings, [opaqueFunction], module.Attributes);
        new Binder(diagnostics).Bind(opaqueModule);
        Assert.Empty(diagnostics.Errors);
        var diagnostic = Assert.Single(diagnostics.Where(item =>
            item.Code == DiagnosticCode.AnalysisUnsupportedNode));
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        var firstInitializer = source.IndexOf(previouslyReported ? "= 1" : "= 2", StringComparison.Ordinal) + 2;
        Assert.Equal(firstInitializer, diagnostic.Span.Start);
        Assert.Equal(previouslyReported ? "1" : "2",
            source.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        Assert.Contains(nameof(RawCSharpExpressionNode), diagnostic.Message);
        var emitted = new CSharpEmitter().Emit(opaqueModule);
        Assert.Contains("int x = 2;", emitted);
        Assert.Contains("int after = 3;", emitted);
        if (previouslyReported)
            Assert.Contains("int before = 1;", emitted);

        var retired = source.Replace("= 1", "= §CS{1}", StringComparison.Ordinal)
            .Replace("= 2", "= §CS{2}", StringComparison.Ordinal)
            .Replace("= 3", "= §CS{3}", StringComparison.Ordinal);
        foreach (var mode in new[] { "default", "type-off", "effects-off", "transpile", "verify" })
        {
            var result = CompileInMode(source, mode);
            Assert.False(result.HasErrors, string.Join("\n", result.Diagnostics));
            Assert.NotEmpty(result.GeneratedCode);
            Assert.DoesNotContain(result.Diagnostics, item =>
                item.Code == DiagnosticCode.AnalysisUnsupportedNode);
            var rejected = CompileInMode(retired, mode);
            Assert.True(rejected.HasErrors);
            Assert.Empty(rejected.GeneratedCode);
            Assert.Contains(rejected.Diagnostics.Errors, item =>
                item.Code == DiagnosticCode.UnknownSectionMarker);
        }
    }

    private static StatementNode WithOpaqueBindingInitializers(StatementNode statement) => statement switch
    {
        BindStatementNode { Initializer: IntLiteralNode literal } binding =>
            new BindStatementNode(binding.Span, binding.Name, binding.TypeName, binding.IsMutable,
                new RawCSharpExpressionNode(literal.Span,
                    literal.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                binding.Attributes, binding.IdentifierSpan, binding.TypeNameSpan, binding.Row),
        ForStatementNode loop => new ForStatementNode(loop.Span, loop.Id, loop.VariableName,
            loop.From, loop.To, loop.Step, loop.Body.Select(WithOpaqueBindingInitializers).ToArray(),
            loop.Attributes, loop.VariableSpan),
        WhileStatementNode loop => new WhileStatementNode(loop.Span, loop.Id, loop.Condition,
            loop.Body.Select(WithOpaqueBindingInitializers).ToArray(), loop.Attributes),
        ForeachStatementNode loop => new ForeachStatementNode(loop.Span, loop.Id, loop.VariableName,
            loop.VariableType, loop.Collection, loop.Body.Select(WithOpaqueBindingInitializers).ToArray(),
            loop.Attributes, loop.IndexVariableName, loop.VariableSpan, loop.IndexVariableSpan,
            loop.VariableTypeSpan),
        DoWhileStatementNode loop => new DoWhileStatementNode(loop.Span, loop.Id,
            loop.Body.Select(WithOpaqueBindingInitializers).ToArray(), loop.Condition, loop.Attributes),
        _ => throw new InvalidOperationException($"Unexpected deduplication control node: {statement.GetType().Name}")
    };

    private static string LoopBody(string kind, string body) => kind switch
    {
        "outside" => body,
        "for" => "for i from 1 to 2 step 1\n" + Indent(body, 2),
        "while" => "while false\n" + Indent(body, 2),
        "foreach" => "each item in \"ab\"\n" + Indent(body, 2),
        "do" => "do\n" + Indent(body, 2) + "\nwhile false",
        "nested" => "for i from 1 to 2 step 1\n  while false\n" + Indent(body, 4),
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
                "string" => "string",
                "array" => "string[]",
                _ => "Foo"
            };
            var receive = $$"""
                if value != null
                  later()
                {{(safe ? "  if value != null\n    Take(value)" : "  Take(value)")}}
                """;
            var filterWrite = scenario.StartsWith("filter-write", StringComparison.Ordinal);
            if (filterWrite)
                receive = safe ? "if value != null\n  Take(value)"
                    : "Take(value)";
            var initialNoop = scenario is "try-prefix" or "try-call-prefix" or "finally-prefix"
                or "catch-prefix-finally" or "filter-fallthrough" or "filter-wrapper" or "filter-unused";
            var receiveInside = scenario is "catch-isolation" or "try-prefix" or "try-call-prefix"
                or "finally-prefix" or "catch-prefix-finally" or "filter-fallthrough" || filterWrite;
            var body = scenario switch
            {
                "conditional" => "let bool:selected = flag ? resetter() : false",
                "coalesce" => "let string?:selected = text ?? resetString()",
                "and" => "let bool:selected = flag && resetter()",
                "or" => "let bool:selected = flag || resetter()",
                "try-reset" => """
                    try
                      MayThrow(flag)
                      set later noopAction
                    catch Exception:ex
                      Noop()
                    """,
                "catch-reset" => """
                    try
                      MayThrow(flag)
                    catch Exception:ex
                      set later noopAction
                    """,
                "catch-isolation" => $$"""
                    try
                      MayThrow(flag)
                    catch ArgumentException:ex
                      set later noopAction
                    catch Exception:other
                    {{Indent(receive, 2)}}
                    """,
                "try-prefix" or "try-call-prefix" => $$"""
                    try
                      {{(scenario == "try-prefix" ? "set later mutator" : "let bool:installed = setter()")}}
                      MayThrow(flag)
                      set later noopAction
                    catch Exception:ex
                    {{Indent(receive, 2)}}
                    """,
                "finally-prefix" => $$"""
                    try
                      set later mutator
                      MayThrow(flag)
                      set later noopAction
                    finally
                    {{Indent(receive, 2)}}
                    """,
                "catch-prefix-finally" => $$"""
                    try
                      MayThrow(flag)
                    catch Exception:ex
                      set later mutator
                      MayThrow(flag)
                      set later noopAction
                    finally
                    {{Indent(receive, 2)}}
                    """,
                "filter-fallthrough" => $$"""
                    try
                      MayThrow(flag)
                    catch Exception:ex when ex != null && setter()
                      Noop()
                    catch Exception:ex when ex != null
                    {{Indent(receive, 2)}}
                    """,
                "filter-write" => $$"""
                    if value != null
                      try
                        MayThrow(flag)
                      catch Exception:ex when ex != null && writeFilter()
                        Noop()
                      catch Exception:ex when ex != null
                    {{Indent(receive, 4)}}
                    """,
                "filter-write-wrapper" or "filter-write-unused" => $$"""
                    let Action:wrapper = () =>
                      try
                        MayThrow(flag)
                      catch Exception:ex when ex != null && writeFilter()
                        Noop()
                      catch Exception:ex when ex != null
                        Noop()
                    if value != null
                      {{(scenario == "filter-write-wrapper" ? "wrapper()" : "Noop()")}}
                    {{Indent(receive, 2)}}
                    """,
                "filter-wrapper" or "filter-unused" => $$"""
                    let Action:wrapper = () =>
                      try
                        MayThrow(flag)
                      catch Exception:ex when setter()
                        Noop()
                      catch Exception:other
                        Noop()
                    {{(scenario == "filter-wrapper" ? "wrapper()" : "")}}
                    """,
                "unused-try" => """
                    let Action:unused = () =>
                      try
                        MayThrow(flag)
                      catch Exception:ex
                        set later noopAction
                    """,
                "finally-reset" => """
                    try
                      MayThrow(flag)
                    catch Exception:ex
                      Noop()
                    finally
                      set later noopAction
                    """,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            var source = $$"""
                module ExceptionalCallableFlow
                {{(shape == "nominal" ? "  class Foo\n    func Marker () → int\n      return 0" : "")}}
                  func Noop public () → void
                    !
                  func MayThrow public (bool:flag) → void
                    ! throw, alloc
                    if flag
                      throw new Exception()
                  func Mutate public ({{modifier}} {{requiredType}}?:value) → void
                    ! mut
                    set value null
                  func Take public ({{requiredType}}:value) → void
                    !
                  func Probe public ({{requiredType}}?:value, bool:flag, string?:text) → void
                    ! mut, throw, alloc
                    let Action:noopAction = () => Noop()
                    let Action:mutator = () => Mutate({{modifier}} value)
                    var Action:later ! mut = {{(initialNoop ? "noopAction" : "mutator")}}
                    let Func<bool>:resetter = () =>
                      set later noopAction
                      return false
                    let Func<string>:resetString = () =>
                      set later noopAction
                      return "done"
                    let Func<bool>:setter = () =>
                      set later mutator
                      return false
                    let Func<bool>:writeFilter = () =>
                      Mutate({{modifier}} value)
                      return false
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

    [Theory]
    [InlineData("!", "!", true)]
    [InlineData("! cw", "! cw", true)]
    [InlineData("! cw", "!", false)]
    [InlineData("", "!", false)]
    public void NativeDelegateInvoke_ChargesTheDeclaredValueRow(
        string valueRow, string callerRow, bool accepted)
    {
        var source = $$"""
            module DelegateInvoke
              func Probe (Action:callback {{valueRow}}) → void
                {{callerRow}}
                callback.Invoke()
            """;
        var result = Program.Compile(source);
        Assert.Equal(!accepted, result.HasErrors);
        if (accepted)
        {
            Assert.NotEmpty(result.GeneratedCode);
            Assert.DoesNotContain(result.Diagnostics, diagnostic =>
                diagnostic.Code is DiagnosticCode.UnknownExternalCall or DiagnosticCode.ForbiddenEffect);
        }
        else
        {
            Assert.Empty(result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect);
            if (valueRow.Length == 0)
                Assert.Contains(result.Diagnostics, diagnostic =>
                    diagnostic.Code == DiagnosticCode.EffectRowUnknown);
        }
    }

    [Theory]
    [InlineData("default")]
    [InlineData("type-off")]
    [InlineData("effects-off")]
    [InlineData("transpile")]
    [InlineData("verify")]
    public void NativeDelegateInvoke_InvalidatesCapturedGuardsInEveryMode(string mode)
    {
        foreach (var safe in new[] { false, true })
        {
            var source = $$"""
                module CapturedInvoke
                  func Take (string:value) → void
                    !
                  func Probe (string?:value) → void
                    if value != null
                      let Action:callback = () =>
                        set value null
                      callback.Invoke()
                {{(safe ? "      if value != null\n        Take(value)" : "      Take(value)")}}
                """;
            var result = CompileInMode(source, mode);
            Assert.True(result.HasErrors != safe, string.Join("\n", result.Diagnostics));
            if (safe)
                Assert.NotEmpty(result.GeneratedCode);
            else
            {
                Assert.Empty(result.GeneratedCode);
                var error = Assert.Single(result.Diagnostics.Errors);
                Assert.Equal(DiagnosticCode.NullableArgumentToNonNullableParameter, error.Code);
                Assert.Equal("value", source.Substring(error.Span.Start, error.Span.Length));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDelegateInvoke_UsesTheBoundSiblingDeclaration(bool effectful)
    {
        var source = $$"""
            module SiblingDelegateInvoke
              func Noop () → void
                !
              func Probe (bool:flag) → void
                !
                if flag
                  let Action:callback {{(effectful ? "!" : "! cw")}} = () => Noop()
                else
                  let Action:callback {{(effectful ? "! cw" : "!")}} = () => Noop()
                  callback.Invoke()
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors == effectful, string.Join("\n", result.Diagnostics));
        if (effectful)
        {
            Assert.Empty(result.GeneratedCode);
            Assert.Contains(result.Diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.ForbiddenEffect);
        }
        else
            Assert.NotEmpty(result.GeneratedCode);
    }

    [Fact]
    public void NativeDelegateInvoke_DoesNotMistakeASourceActionClassForADelegate()
    {
        const string source = """
            module NonDelegateInvoke
              class Action
                func Invoke public () → void
                  ! cw
                  System.Console.WriteLine("effectful")
              func Probe (Action:callback) → void
                !
                callback.Invoke()
            """;
        var result = Program.Compile(source);
        Assert.True(result.HasErrors);
        Assert.Empty(result.GeneratedCode);
        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect);
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
            "string" => "string",
            "array" => "string[]",
            _ => "Foo"
        };
        var call = control == "no-mutation" ? "Noop()" : "mutator()";
        var body = join switch
        {
            "if" => "if flag\n" + Indent(call, 2),
            "match" => "match flag\n  case true\n" + Indent(call, 4)
                + "\n  case _\n    Noop()",
            "try" => "try\n" + Indent(call, 2)
                + "\ncatch Exception:ex\n  Noop()",
            "finally" => "try\n" + Indent(call, 2)
                + "\nfinally\n  Noop()",
            _ => throw new ArgumentOutOfRangeException(nameof(join))
        };
        var source = $$"""
            module ShadowedCapture
            {{(shape == "nominal" ? "  class Foo\n    func Marker () → int\n      return 0" : "")}}
              class Holder
                field {{type}}?:value public
                field {{type}}?:otherValue public
                func Noop public () → void
                  !
                func Mutate public ({{modifier}} {{type}}?:target) → void
                  ! mut
                  set target null
                func Take public ({{type}}:target) → void
                  !
                func Probe public (bool:flag) → void
                  ! mut
                  if value != null
                    let Action:mutator = () => Mutate({{modifier}} {{(control == "other-storage" ? "otherValue" : "value")}})
                    match flag
                      case var {{(control == "renamed" ? "other" : "value")}}
            {{Indent(body, 12)}}
            {{(control == "fresh" ? "        if value != null\n          Take(value)" : "        Take(value)")}}
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
