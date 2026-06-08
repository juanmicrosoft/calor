using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Parser tests for Phase 1 of the v0.6 call-closer-elision RFC
/// (<c>docs/plans/v0.6-call-closer-elision.md</c>). The RFC generalizes the
/// existing statement-context implicit-close (<c>Parser.cs:1376</c>) to
/// expression context.
///
/// Two new accepted forms:
///   <c>§C{target}</c>              — zero-arg, no <c>§/C</c>
///   <c>§C{target} primary_expr</c> — one inline arg, no <c>§/C</c>
///
/// One new diagnostic:
///   <see cref="DiagnosticCode.AmbiguousCallContinuation"/> (Calor0150)
///   — two consecutive expression-start tokens follow a closer-less call.
/// </summary>
public class CallExpressionImplicitCloseTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static T FirstBindingInitializer<T>(ModuleNode module) where T : ExpressionNode
    {
        var func = module.Functions.First();
        var bind = func.Body.OfType<BindStatementNode>().First();
        Assert.NotNull(bind.Initializer);
        return Assert.IsType<T>(bind.Initializer);
    }

    [Fact]
    public void ExpressionContext_OneInlineArg_NoCloser_Parses()
    {
        // §B{x} §C{Identity} INT:1   ← NEW: no §/C
        var source = """
§M{m001:Test}
  §F{f001:Foo:i32}
      §O{i32}
      §B{x} §C{Identity} INT:1
      §R x
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstBindingInitializer<CallExpressionNode>(module);
        Assert.Equal("Identity", call.Target);
        Assert.Single(call.Arguments);
        Assert.IsType<IntLiteralNode>(call.Arguments[0]);
    }

    [Fact]
    public void ExpressionContext_OneInlineArg_WithExplicitCloser_StillParses()
    {
        // §B{x} §C{Identity} INT:1 §/C   ← LEGACY: still accepted
        var source = """
§M{m001:Test}
  §F{f001:Foo:i32}
      §O{i32}
      §B{x} §C{Identity} INT:1 §/C
      §R x
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstBindingInitializer<CallExpressionNode>(module);
        Assert.Equal("Identity", call.Target);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void ExpressionContext_ZeroArg_NoCloser_Parses()
    {
        // §B{x} §C{Vec.empty}   ← NEW: zero-arg, no §/C
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x} §C{Vec.empty}
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstBindingInitializer<CallExpressionNode>(module);
        Assert.Equal("Vec.empty", call.Target);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void ExpressionContext_ZeroArg_WithExplicitCloser_StillParses()
    {
        // §B{x} §C{Vec.empty} §/C   ← LEGACY zero-arg with closer
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x} §C{Vec.empty} §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstBindingInitializer<CallExpressionNode>(module);
        Assert.Equal("Vec.empty", call.Target);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void ExpressionContext_OneInlineArg_TrailingMemberAccess_AttachesToArg()
    {
        // §B{n} §C{Identity} obj?.Length
        // Reading A (RFC §3.2 case A): ?.Length attaches to obj, not to the call result.
        // Note: the lexer treats `obj.Length` (with bare dot) as a single dotted
        // identifier, so we use `?.` to force a parser-level disambiguation.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{obj:string} STR:"hi"
      §B{n} §C{Identity} obj?.Length
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var func = module.Functions.First();
        var nBind = func.Body.OfType<BindStatementNode>().Last();
        var call = Assert.IsType<CallExpressionNode>(nBind.Initializer);
        Assert.Equal("Identity", call.Target);
        Assert.Single(call.Arguments);

        // Argument should be a NullConditional(obj, "Length"), NOT a Reference(obj) with
        // ?.Length on the call result.
        var nc = Assert.IsType<NullConditionalNode>(call.Arguments[0]);
        var inner = Assert.IsType<ReferenceNode>(nc.Target);
        Assert.Equal("obj", inner.Name);
        Assert.Equal("Length", nc.MemberName);
    }

    [Fact]
    public void ExpressionContext_ZeroArg_TrailingNullConditionalMember_ParsesAsCallThenAccess()
    {
        // §B{n} §C{Maybe}?.Length
        // The ?.Length should attach to the (zero-arg) call result, not be
        // parsed as a malformed inline argument.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{n} §C{Maybe}?.Length
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        // Top-level expression must be a NullConditional on a Call.
        var nc = Assert.IsType<NullConditionalNode>(bind.Initializer);
        Assert.IsType<CallExpressionNode>(nc.Target);
    }

    [Fact]
    public void ExpressionContext_ZeroArg_TrailingDotMember_ParsesAsCallThenAccess()
    {
        // §B{n} §C{Maybe}.Length — dot member after zero-arg implicit close.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{n} §C{Maybe}.Length
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var ma = Assert.IsType<FieldAccessNode>(bind.Initializer);
        Assert.IsType<CallExpressionNode>(ma.Target);
        Assert.Equal("Length", ma.FieldName);
    }

    [Fact]
    public void ExpressionContext_NestedCall_BothElided_Parses()
    {
        // §B{x} §C{Foo.bar} §C{Baz.qux} y   ← RFC §3.2 case C
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{y:i32} INT:1
      §B{x} §C{Foo.bar} §C{Baz.qux} y
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var func = module.Functions.First();
        var xBind = func.Body.OfType<BindStatementNode>().Last();
        var outer = Assert.IsType<CallExpressionNode>(xBind.Initializer);
        Assert.Equal("Foo.bar", outer.Target);
        Assert.Single(outer.Arguments);

        var inner = Assert.IsType<CallExpressionNode>(outer.Arguments[0]);
        Assert.Equal("Baz.qux", inner.Target);
        Assert.Single(inner.Arguments);
        Assert.IsType<ReferenceNode>(inner.Arguments[0]);
    }

    [Fact]
    public void ExpressionContext_NestedCall_OuterStandardInnerElided_Parses()
    {
        // §C{outer} §A §C{inner} INT:1 §/C   ← one §/C goes to outer; inner elided
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x} §C{outer} §A §C{inner} INT:1 §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var outer = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("outer", outer.Target);
        Assert.Single(outer.Arguments);

        var inner = Assert.IsType<CallExpressionNode>(outer.Arguments[0]);
        Assert.Equal("inner", inner.Target);
        Assert.Single(inner.Arguments);
    }

    [Fact]
    public void ExpressionContext_NestedCall_BothExplicit_StillParses()
    {
        // §C{outer} §A §C{inner} INT:1 §/C §/C   ← LEGACY both explicit
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x} §C{outer} §A §C{inner} INT:1 §/C §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var outer = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("outer", outer.Target);
        Assert.Single(outer.Arguments);

        var inner = Assert.IsType<CallExpressionNode>(outer.Arguments[0]);
        Assert.Equal("inner", inner.Target);
        Assert.Single(inner.Arguments);
    }

    [Fact]
    public void ExpressionContext_DepthTwoNesting_AllExplicit_Parses()
    {
        // §C{o1} §A §C{o2} §A §C{i} x §/C §/C §/C   ← all three explicit
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x:i32} INT:1
      §B{r} §C{o1} §A §C{o2} §A §C{i} x §/C §/C §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var rBind = module.Functions.First().Body.OfType<BindStatementNode>().Last();
        var o1 = Assert.IsType<CallExpressionNode>(rBind.Initializer);
        Assert.Equal("o1", o1.Target);
        var o2 = Assert.IsType<CallExpressionNode>(o1.Arguments[0]);
        Assert.Equal("o2", o2.Target);
        var i = Assert.IsType<CallExpressionNode>(o2.Arguments[0]);
        Assert.Equal("i", i.Target);
        Assert.IsType<ReferenceNode>(i.Arguments[0]);
    }

    [Fact]
    public void ExpressionContext_DepthTwoNesting_TwoClosers_InnerElided_Parses()
    {
        // §C{o1} §A §C{o2} §A §C{i} x §/C §/C   ← exactly two §/C, i elided
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x:i32} INT:1
      §B{r} §C{o1} §A §C{o2} §A §C{i} x §/C §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var rBind = module.Functions.First().Body.OfType<BindStatementNode>().Last();
        var o1 = Assert.IsType<CallExpressionNode>(rBind.Initializer);
        var o2 = Assert.IsType<CallExpressionNode>(o1.Arguments[0]);
        var i = Assert.IsType<CallExpressionNode>(o2.Arguments[0]);
        Assert.Equal("i", i.Target);
        Assert.Single(i.Arguments);
    }

    [Fact]
    public void ExpressionContext_AmbiguousContinuation_EmitsCalor0150()
    {
        // §C{f} INT:1 INT:2   ← two consecutive expression-start tokens
        // Per RFC §3.2 case B → Calor0150.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x} §C{f} INT:1 INT:2
""";
        Parse(source, out var diags);
        Assert.Contains(diags.Errors, e => e.Code == DiagnosticCode.AmbiguousCallContinuation);
    }

    [Fact]
    public void ExpressionContext_AmbiguousContinuation_NestedCall_AlsoEmitsCalor0150()
    {
        // §C{f} a §C{g} b   ← a is f's inline arg; §C{g} is a second
        // expression-start token on f. Per RFC §3.2 case B → Calor0150.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{a:i32} INT:1
      §B{b:i32} INT:2
      §B{x} §C{f} a §C{g} b
""";
        Parse(source, out var diags);
        Assert.Contains(diags.Errors, e => e.Code == DiagnosticCode.AmbiguousCallContinuation);
    }

    [Fact]
    public void ExpressionContext_GenericTargetAndZeroArg_Parses()
    {
        // §B{xs} §C{Array.Empty<i32>}   ← generic target, zero-arg, no §/C
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{xs} §C{Array.Empty<i32>}
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var call = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("Array.Empty", call.Target);
        Assert.NotNull(call.TypeArguments);
        Assert.Equal("i32", call.TypeArguments[0]);
    }

    [Fact]
    public void StatementContext_ImplicitClose_StillWorks()
    {
        // Regression check: the existing statement-context implicit close
        // at Parser.cs:1376 is unaffected by the expression-context change.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §C{Console.WriteLine} STR:"hello"
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var stmt = module.Functions.First().Body.OfType<CallStatementNode>().First();
        Assert.Equal("Console.WriteLine", stmt.Target);
        Assert.Single(stmt.Arguments);
    }

    [Fact]
    public void StatementContext_OuterArgConsumesItsOwnCloser_WhenInnerIsElidedCall()
    {
        // Regression: inner elided Call inside an outer statement-call's §A
        // must NOT consume the §/C belonging to the outer call.
        // Per rubber-duck review: requires _inOuterCallArgDepth to be bumped
        // in ParseCallStatement's §A loop, not just ParseCallExpression's.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x:i32} INT:1
      §C{outer} §A §C{inner} x §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var stmt = module.Functions.First().Body.OfType<CallStatementNode>().Last();
        Assert.Equal("outer", stmt.Target);
        Assert.Single(stmt.Arguments);
        var innerCall = Assert.IsType<CallExpressionNode>(stmt.Arguments[0]);
        Assert.Equal("inner", innerCall.Target);
        Assert.Single(innerCall.Arguments);
    }

    [Fact]
    public void ExpressionContext_InlineArgThenArg_EmitsCalor0150()
    {
        // §C{f} x §A y §/C — user mixed inline form with §A. Calor0150
        // covers this confusion: the inline form already consumed one arg
        // and cannot accept a second.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x:i32} INT:1
      §B{y:i32} INT:2
      §B{r} §C{f} x §A y §/C
""";
        Parse(source, out var diags);
        Assert.Contains(diags.Errors, e => e.Code == DiagnosticCode.AmbiguousCallContinuation);
    }

    [Fact]
    public void LispContext_CallWithExplicitCloser_StillParses()
    {
        // Inside Lisp expressions, the canonical §A + §/C form must still
        // parse cleanly. The closer-elision change must not regress this.
        var source = """
§M{m001:Test}
  §F{f001:Foo:i32}
      §O{i32}
      §B{x} (+ §C{f} §A INT:1 §/C §C{g} §A INT:2 §/C)
      §R x
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var lisp = Assert.IsType<BinaryOperationNode>(bind.Initializer);
        Assert.Equal(BinaryOperator.Add, lisp.Operator);
        Assert.IsType<CallExpressionNode>(lisp.Left);
        Assert.IsType<CallExpressionNode>(lisp.Right);
    }

    // ----- Phase 2: CalorEmitter.UseImplicitCallCloser flag -----

    [Fact]
    public void Emitter_ZeroArgCall_DefaultFlag_EmitsImplicitCloser()
    {
        // v0.6.1 flipped the default: zero-arg calls now elide §/C by default.
        // RFC v0.6 §6 / docs/plans/v0.6-call-closer-elision.md.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var emitter = new Migration.CalorEmitter();
        var output = call.Accept<string>(emitter);
        Assert.Equal("§C{Foo}", output);
    }

    [Fact]
    public void Emitter_ZeroArgCall_ImplicitCloserFlagFalse_PinsExplicitCloser()
    {
        // Opt-out path: setting the flag to false restores explicit §/C.
        // Test pins this so the opt-out doesn't silently disappear.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var ctx = new Migration.ConversionContext { UseImplicitCallCloser = false };
        var emitter = new Migration.CalorEmitter(ctx);
        var output = call.Accept<string>(emitter);
        Assert.Equal("§C{Foo} §/C", output);
    }

    [Fact]
    public void Emitter_ZeroArgCall_FollowedBySiblingOpener_RoundTripsCorrectly()
    {
        // v0.6.1 regression test: a zero-arg implicit-close call followed by
        // a sibling statement that starts with an expression-starter token
        // (§IF here — IsExpressionStart returns true because expression-IF
        // is valid). Without the same-line guard in ParseCallExpression, the
        // parser would treat the §IF as the call's inline argument and emit
        // Calor0104. RFC v0.6 §3.2 / v0.6.1.
        var source = @"§M{Demo}
  §F{run:bool:pub}
    §B{x} §C{items.Any}
    §IF{cond} x → §R x §EL → §R false
";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var fn = module.Functions.First();
        var bind = Assert.IsType<BindStatementNode>(fn.Body[0]);
        var call = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("items.Any", call.Target);
        Assert.Empty(call.Arguments);
        // The §IF must parse as a sibling statement, not as the call's argument.
        Assert.IsType<IfStatementNode>(fn.Body[1]);
    }

    [Fact]
    public void Emitter_ZeroArgCall_ImplicitCloserFlagTrue_ElidesCloser()
    {
        // With the flag set, zero-arg calls drop §/C.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var ctx = new Migration.ConversionContext { UseImplicitCallCloser = true };
        var emitter = new Migration.CalorEmitter(ctx);
        var output = call.Accept<string>(emitter);
        Assert.Equal("§C{Foo}", output);
    }

    [Fact]
    public void Emitter_MultiArgCall_ImplicitCloserFlagTrue_StillEmitsCloser()
    {
        // Multi-arg form is unchanged regardless of flag — the inline-arg
        // implicit-close form is limited to a single positional argument.
        var args = new List<ExpressionNode>
        {
            new IntLiteralNode(default, 1),
            new IntLiteralNode(default, 2),
        };
        var call = new CallExpressionNode(default, "Add", args);
        var ctx = new Migration.ConversionContext { UseImplicitCallCloser = true };
        var emitter = new Migration.CalorEmitter(ctx);
        var output = call.Accept<string>(emitter);
        Assert.EndsWith("§/C", output);
    }

    [Fact]
    public void Emitter_OneArgCall_ImplicitCloserFlagTrue_StillEmitsCloser()
    {
        // v0.6.0 deliberately implements zero-arg-only emitter elision.
        // The one-arg path needs context-aware safety (a §C inside a Lisp
        // (+ a b) arg-list would consume its sibling) and is deferred to
        // v0.6.1. This test pins that intentional limitation.
        var args = new List<ExpressionNode>
        {
            new IntLiteralNode(default, 42),
        };
        var call = new CallExpressionNode(default, "Identity", args);
        var ctx = new Migration.ConversionContext { UseImplicitCallCloser = true };
        var emitter = new Migration.CalorEmitter(ctx);
        var output = call.Accept<string>(emitter);
        Assert.EndsWith("§/C", output);
    }

    [Fact]
    public void Emitter_ZeroArgCall_FlagTrue_RoundTripsThroughParser()
    {
        // End-to-end: parser accepts the emitter's flag-true output.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var ctx = new Migration.ConversionContext { UseImplicitCallCloser = true };
        var emitter = new Migration.CalorEmitter(ctx);
        var emitted = call.Accept<string>(emitter);
        Assert.Equal("§C{Foo}", emitted);

        // Wrap in a binding so we can re-parse it.
        var source = $$"""
§M{m001:Test}
  §F{f001:Bar:pub}
      §B{x} {{emitted}}
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var roundTripped = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("Foo", roundTripped.Target);
        Assert.Empty(roundTripped.Arguments);
    }
}
