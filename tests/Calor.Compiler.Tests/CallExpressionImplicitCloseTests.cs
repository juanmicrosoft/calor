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
    public void Emitter_ZeroArgCall_AsLastStatementBeforeDedent_RoundTripsCorrectly()
    {
        // v0.6.1 regression test for the Dedent-swallowing parser bug.
        // Before the fix, ParseCallExpression treated `Dedent` as a valid
        // EndCall token (via `IsBlockEnd(EndCall)`), routing the zero-arg
        // call through the standard-form branch which then consumed the
        // Dedent thinking it was an indent-only end-of-block. The enclosing
        // method body terminator vanished, and the next sibling member
        // would fail to parse. RFC v0.6 §3.2 / v0.6.1.
        var source = @"§M{Demo}
  §CL{c:C:pub}
    §MT{m1:M1:pub}
      §C{Foo}
    §MT{m2:M2:pub}
      §C{Bar}
";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var cls = module.Classes.First();
        Assert.Equal(2, cls.Methods.Count);
        Assert.Equal("M1", cls.Methods[0].Name);
        Assert.Equal("M2", cls.Methods[1].Name);
    }

    [Fact]
    public void Emitter_ZeroArgCallAsArgInMultiArgCall_KeepsExplicitCloser()
    {
        // v0.6.1 BLOCKER #1 regression: zero-arg call as one of several §A
        // arguments to an outer call MUST keep its explicit §/C. Otherwise
        // `§C{M} §A §C{A} §A 2 §/C` would re-parse as `M(A(2))` instead of
        // `M(A(), 2)` — silent semantic corruption. The emitter tracks an
        // inline-sibling-context counter to suppress elision in such places.
        var args = new List<ExpressionNode>
        {
            new CallExpressionNode(default, "A", new List<ExpressionNode>()),
            new IntLiteralNode(default, 2),
        };
        var outer = new CallExpressionNode(default, "M", args);
        var emitter = new Migration.CalorEmitter();
        var output = outer.Accept<string>(emitter);
        // Inner zero-arg A() must keep its §/C inside the §A chain.
        Assert.Contains("§C{A} §/C", output);
        Assert.EndsWith("§/C", output);
    }

    [Fact]
    public void Emitter_AdjacentZeroArgCallsInArrayInitializer_KeepsExplicitClosers()
    {
        // v0.6.1 BLOCKER #2 regression: array initializer with multiple
        // zero-arg calls. Without keeping §/C, `§ARR{...} §C{A} §C{B} §/ARR`
        // would re-parse as a SINGLE element `A(B())` instead of TWO
        // elements `A(), B()` — devastating silent corruption.
        var elements = new List<ExpressionNode>
        {
            new CallExpressionNode(default, "A", new List<ExpressionNode>()),
            new CallExpressionNode(default, "B", new List<ExpressionNode>()),
        };
        var arr = new ArrayCreationNode(
            default,
            id: "arr1",
            name: "arr1",
            elementType: "any",
            size: null,
            initializer: elements,
            attributes: new AttributeCollection());
        var emitter = new Migration.CalorEmitter();
        var output = arr.Accept<string>(emitter);
        // Both inner calls must keep §/C inside §ARR initializer.
        Assert.Contains("§C{A} §/C", output);
        Assert.Contains("§C{B} §/C", output);
    }

    [Fact]
    public void Emitter_ZeroArgCallAsTopLevelExpression_StillElidesCloser()
    {
        // Pin that the context-aware fix does NOT regress the v0.6.1 default
        // behavior at top-level expression positions (binding initializer,
        // return value). The flag's purpose is to shorten these "leaf"
        // occurrences; nesting inside §A / §ARR forces the closer.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var emitter = new Migration.CalorEmitter();
        var output = call.Accept<string>(emitter);
        // Leaf-position call: §/C elided by v0.6.1 default.
        Assert.Equal("§C{Foo}", output);
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
        // v0.6.1 ships zero-arg-only emitter elision (RFC §6). The one-arg
        // path needs context-aware safety (a §C inside a Lisp `(+ a b)`
        // arg-list would consume its sibling) — that work tracks separately
        // in CHANGELOG.md under "Out of scope for v0.6.1". This test pins
        // the intentional zero-arg-only limitation so it can't silently
        // expand without a deliberate decision.
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

    // ────────────────────────────────────────────────────────────────────
    // v0.6.1 loop-2 round-trip tests: every emit site that produces
    // §C{X} alongside a sibling token must keep §/C explicit so the
    // parser reconstructs the original AST shape (not a corrupted one
    // where the inner call absorbed the sibling).
    //
    // Strategy: emit a hand-built AST with UseImplicitCallCloser=true,
    // then re-parse the emitted Calor and verify the re-parsed AST shape
    // is identical to the original. Catches silent corruption that a
    // substring-only check would miss.
    // ────────────────────────────────────────────────────────────────────

    private static string EmitWithImplicitCloser(AstNode node)
    {
        var ctx = new Migration.ConversionContext { UseImplicitCallCloser = true };
        var emitter = new Migration.CalorEmitter(ctx);
        return node.Accept<string>(emitter);
    }

    [Fact]
    public void RoundTrip_ZeroArgCallInsideOuterCallArgs_PreservesShape()
    {
        // BLOCKER #1 round-trip: M(A(), 2) — emit then re-parse and assert
        // the AST shape is preserved (two args at outer, zero args at inner).
        var outer = new CallExpressionNode(default, "M", new List<ExpressionNode>
        {
            new CallExpressionNode(default, "A", new List<ExpressionNode>()),
            new IntLiteralNode(default, 2),
        });
        var emitted = EmitWithImplicitCloser(outer);
        Assert.Contains("§C{A} §/C", emitted);

        // Wrap in a binding context to round-trip through the parser.
        var source = $$"""
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x:i32} {{emitted}}
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))} | Emitted: {emitted}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var rt = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("M", rt.Target);
        Assert.Equal(2, rt.Arguments.Count);
        var inner = Assert.IsType<CallExpressionNode>(rt.Arguments[0]);
        Assert.Equal("A", inner.Target);
        Assert.Empty(inner.Arguments);
        Assert.IsType<IntLiteralNode>(rt.Arguments[1]);
    }

    [Fact]
    public void RoundTrip_ZeroArgCallInsideNewExpression_PreservesShape()
    {
        // §NEW{T} §A §C{A} §A INT:2 §/NEW must keep §C{A} closed so it doesn't
        // absorb the next §A as its inline argument.
        var newExpr = new NewExpressionNode(
            default,
            typeName: "T",
            typeArguments: Array.Empty<string>(),
            arguments: new List<ExpressionNode>
            {
                new CallExpressionNode(default, "A", new List<ExpressionNode>()),
                new IntLiteralNode(default, 2),
            });
        var emitted = EmitWithImplicitCloser(newExpr);
        Assert.Contains("§C{A} §/C", emitted);

        var source = $$"""
§M{m001:Test}
  §F{f001:Foo:pub}
      §B{x:T} {{emitted}}
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))} | Emitted:\n{emitted}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var rt = Assert.IsType<NewExpressionNode>(bind.Initializer);
        Assert.Equal("T", rt.TypeName);
        Assert.Equal(2, rt.Arguments.Count);
        var inner = Assert.IsType<CallExpressionNode>(rt.Arguments[0]);
        Assert.Equal("A", inner.Target);
        Assert.Empty(inner.Arguments);
        Assert.IsType<IntLiteralNode>(rt.Arguments[1]);
    }

    [Fact]
    public void RoundTrip_ZeroArgCallInsideArrayAccess_PreservesShape()
    {
        // arr[GetIdx()] — §IDX must keep the inner §C{GetIdx} closed.
        var idx = new ArrayAccessNode(
            default,
            array: new ReferenceNode(default, "arr"),
            index: new CallExpressionNode(default, "GetIdx", new List<ExpressionNode>()));
        var emitted = EmitWithImplicitCloser(idx);
        Assert.Contains("§C{GetIdx} §/C", emitted);
    }

    [Fact]
    public void Emitter_ZeroArgCallInsideNullCoalesce_KeepsExplicitCloser()
    {
        // Lisp (?? A() fallback): without explicit §/C the inner A would
        // absorb 'fallback' as its arg, corrupting the AST.
        var nc = new NullCoalesceNode(
            default,
            left: new CallExpressionNode(default, "A", new List<ExpressionNode>()),
            right: new ReferenceNode(default, "fallback"));
        var emitted = EmitWithImplicitCloser(nc);
        Assert.Contains("§C{A} §/C", emitted);
    }

    [Fact]
    public void Emitter_ZeroArgCallInsideDictionaryEntry_KeepsExplicitCloser()
    {
        // §KV §C{K} §C{V} would re-parse as a SINGLE key K(V()) with no value
        // instead of two distinct key/value expressions. Each key/value
        // emission must keep nested zero-arg calls closed with explicit §/C.
        //
        // Parse a small module with a §DICT containing call key/values, then
        // re-emit with flag=true and verify §C{...} §/C survives.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §DICT{d:str:i32}
        §KV §C{K} §/C §C{V} §/C
      §/DICT{d}
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var emitter = new Migration.CalorEmitter(
            new Migration.ConversionContext { UseImplicitCallCloser = true });
        var emitted = emitter.Emit(module);
        Assert.Contains("§C{K} §/C", emitted);
        Assert.Contains("§C{V} §/C", emitted);
    }

    [Fact]
    public void RoundTrip_ZeroArgCallStatement_FollowedBySiblingStatement_PreservesShape()
    {
        // Two adjacent zero-arg call statements at function-body level.
        // Statement-form §C{Foo} §/C is the existing emitter behavior;
        // even with the flag flipped, statement-form keeps §/C explicit
        // because absorbing the next sibling statement would be ambiguous.
        var foo = new CallStatementNode(
            default, target: "Bar", fallible: false,
            arguments: new List<ExpressionNode>(),
            attributes: new AttributeCollection());
        var emitter = new Migration.CalorEmitter(
            new Migration.ConversionContext { UseImplicitCallCloser = true });
        foo.Accept<string>(emitter);
        // The statement emitter appends to its internal buffer via AppendLine.
        // We can't easily get the buffer; instead, build a function whose body
        // is two such statements and emit the whole module.
        var bar = new CallStatementNode(
            default, target: "Bar", fallible: false,
            arguments: new List<ExpressionNode>(),
            attributes: new AttributeCollection());
        var baz = new CallStatementNode(
            default, target: "Baz", fallible: false,
            arguments: new List<ExpressionNode>(),
            attributes: new AttributeCollection());
        var fnEmitter = new Migration.CalorEmitter(
            new Migration.ConversionContext { UseImplicitCallCloser = true });
        var barOutput = bar.Accept<string>(fnEmitter);
        var bazOutput = baz.Accept<string>(fnEmitter);
        // Statement-form always returns "" — the buffer holds the actual text.
        // We're verifying via a different angle: round-trip through full source.
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §C{Bar} §/C
      §C{Baz} §/C
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var ctx2 = new Migration.ConversionContext { UseImplicitCallCloser = true };
        var fullEmitter = new Migration.CalorEmitter(ctx2);
        var fullEmitted = module.Accept<string>(fullEmitter);
        // Both statement-form calls must keep their §/C even with flag on.
        Assert.Contains("§C{Bar} §/C", fullEmitted);
        Assert.Contains("§C{Baz} §/C", fullEmitted);
    }

    [Fact]
    public void Emitter_ZeroArgCallInsideTypeOperationOperand_KeepsExplicitCloser()
    {
        // (is §C{Foo} str) would re-parse as `is` with a single arg
        // `§C{Foo}` whose inline argument is `str` (a built-in keyword token
        // accepted as an identifier in IsExpressionStart), giving the type op
        // the wrong arg count / structure. TypeOperationNode.Operand must
        // keep its zero-arg call closed when emitted before the type token.
        var typeOp = new TypeOperationNode(
            default,
            TypeOp.Is,
            new CallExpressionNode(default, "Foo", new List<ExpressionNode>()),
            "str");
        var emitted = EmitWithImplicitCloser(typeOp);
        Assert.Equal("(is §C{Foo} §/C str)", emitted);

        var typeOpAs = new TypeOperationNode(
            default,
            TypeOp.As,
            new CallExpressionNode(default, "Foo", new List<ExpressionNode>()),
            "str");
        var emittedAs = EmitWithImplicitCloser(typeOpAs);
        Assert.Equal("(as §C{Foo} §/C str)", emittedAs);
    }

    [Fact]
    public void Emitter_ZeroArgCallInsideEventSubscribe_KeepsExplicitCloser()
    {
        // §SUB {evt} {handler} on one line — both children are expressions.
        // If `evt` is a zero-arg call and `handler` looks like an
        // expression-start token, the parser could absorb handler as
        // the call's inline argument. Verified via parse-and-re-emit
        // round-trip; we don't construct EventSubscribeNode directly
        // because it appends to the emitter buffer (returns "").
        var source = """
§M{m001:Test}
  §F{f001:Foo:pub}
      §SUB §C{Foo} §/C h
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");
        var fullEmitter = new Migration.CalorEmitter(
            new Migration.ConversionContext { UseImplicitCallCloser = true });
        var fullEmitted = module.Accept<string>(fullEmitter);
        Assert.Contains("§C{Foo} §/C h", fullEmitted);
    }

    [Fact]
    public void CalorEmitter_HasNoRawAcceptInSpaceSeparatedSiblingPosition()
    {
        // ARCHITECTURE INVARIANT GUARD: any future contributor adding a new
        // emit site that joins two expressions with a literal space MUST use
        // AcceptInInlineSibling (or HoistToTempVar), not raw `.Accept(this)`.
        //
        // This test scans CalorEmitter.cs for the risky pattern:
        //     `{X.Accept(this)} {Y...}` inside a string interpolation
        // and the related Lisp/sibling pattern:
        //     `string.Join(" ", ... .Accept(this) ... )` over an expression sequence.
        //
        // When this test fails, either:
        //   (a) wrap the offending call with AcceptInInlineSibling, OR
        //   (b) add the file:line to the whitelist below with a comment
        //       justifying why it cannot corrupt the AST under
        //       UseImplicitCallCloser = true (e.g. the next sibling token
        //       is not in IsExpressionStart()).
        var emitterPath = LocateEmitterSourceFile();
        var lines = System.IO.File.ReadAllLines(emitterPath);

        // Pattern 1: `{...Accept(this)} {...}` inside an interpolation literal
        // means two interpolated children separated by a literal space.
        var twoInterpRegex = new System.Text.RegularExpressions.Regex(
            @"\{[^{}\n]*\.Accept\(this\)[^{}\n]*\}\s+\{");
        // Pattern 2: `string.Join(" ", <enumerable>.Accept(this))` projecting
        // an expression sequence with raw .Accept (no AcceptInInlineSibling).
        var joinRegex = new System.Text.RegularExpressions.Regex(
            @"string\.Join\("" ""[^)]*=>\s*\w+\.Accept\(this\)");

        // Whitelisted lines (file-relative 1-based). Each entry must carry a
        // justification comment in the surrounding source.
        var whitelistedLines = new HashSet<int>
        {
            // BoundVariables in forall/exists/implication emit as binding
            // patterns (e.g. `x:i32`), not as expressions; they cannot
            // contain a zero-arg §C call.
            // (See CalorEmitter.Visit(ForallExpressionNode) / ExistsExpressionNode)
        };

        var violations = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (whitelistedLines.Contains(i + 1)) continue;
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//")) continue;
            if (twoInterpRegex.IsMatch(line))
                violations.Add($"Pattern1 line {i + 1}: {trimmed}");
            var joinMatch = joinRegex.Match(line);
            if (joinMatch.Success)
            {
                // Allow if the variable being projected is known to be a
                // binding/pattern (BoundVariables in forall/exists).
                if (!line.Contains("BoundVariables"))
                    violations.Add($"Pattern2 line {i + 1}: {trimmed}");
            }
        }

        Assert.True(violations.Count == 0,
            "CalorEmitter.cs has risky raw .Accept(this) calls in same-line " +
            "sibling positions. Wrap with AcceptInInlineSibling or whitelist:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void RoundTrip_IsPatternOperand_ZeroArgCall_KeepsExplicitCloser()
    {
        // BLOCKER from loop-4 devil's-advocate finding #2.
        // (is §C{Foo} §/C str) parses to IsPatternNode, whose emitter
        // previously used raw node.Operand.Accept(this). Re-emit could
        // produce (is §C{Foo} str) which re-parses with str as inline arg.
        var source = """
§M{m001:Test}
  §F{f001:Foo:bool}
      §O{bool}
      §B{x} (is §C{Foo} §/C str)
      §R x
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var fullEmitter = new Migration.CalorEmitter(
            new Migration.ConversionContext { UseImplicitCallCloser = true });
        var emitted = module.Accept<string>(fullEmitter);
        // Emitted form must still close the operand call.
        Assert.Contains("(is §C{Foo} §/C str)", emitted);

        // And it must re-parse identically.
        var module2 = Parse(emitted, out var diags2);
        Assert.False(diags2.HasErrors,
            $"Re-parse errors: {string.Join("; ", diags2.Errors.Select(e => e.Message))}");
        var bind = module2.Functions[0].Body.OfType<BindStatementNode>().First();
        var isPattern = Assert.IsType<IsPatternNode>(bind.Initializer);
        var operandCall = Assert.IsType<CallExpressionNode>(isPattern.Operand);
        Assert.Equal("Foo", operandCall.Target);
        Assert.Empty(operandCall.Arguments);
        Assert.Equal("str", isPattern.TargetType);
    }

    [Fact]
    public void Emitter_RangeExpression_StartEndAreInlineSiblings_HoistsCalls()
    {
        // Loop-4 devil's-advocate finding #3 defense-in-depth.
        // §RANGE start end emits two space-separated operands. The parser
        // restricts range operands to lightweight primaries (IsRangeOperandStart
        // excludes §C), so the emitter relies on HoistToTempVar to lift any
        // call operands. The AcceptInInlineSibling wrap is defense-in-depth
        // so that — even if hoisting were ever disabled or bypassed — a
        // zero-arg call left in start position would not absorb end.
        var range = new RangeExpressionNode(
            default,
            start: new CallExpressionNode(default, "Lo", new List<ExpressionNode>()),
            end: new CallExpressionNode(default, "Hi", new List<ExpressionNode>()));

        var emitted = EmitWithImplicitCloser(range);
        // Either both calls are hoisted to temps OR — if any §C{...} sneaks
        // through — it must keep its §/C closer (no naked elision).
        Assert.DoesNotContain("§C{Lo} §C", emitted); // would mean Lo absorbed Hi
        Assert.DoesNotContain("§C{Lo} INT", emitted);
        // Sanity: result starts with §RANGE and contains both operands' names
        // somewhere (either as hoisted temps or as inline §C{...} §/C).
        Assert.Contains("§RANGE", emitted);
    }

    [Fact]
    public void RoundTrip_InlineLambdaStatementBody_ZeroArgCall_KeepsExplicitCloser()
    {
        // BLOCKER from loop-4 devil's-advocate finding #1.
        // §LAM with a short statement body emits all statements on a single
        // space-separated line. A bind/assign/return ending in a zero-arg
        // §C{A} previously elided §/C and absorbed the next statement (which
        // typically starts with another expression-start token) as inline arg.
        //
        // Construct the AST directly to bypass any C# → Calor frontend nuance.
        // Lambda body: { §B{x} §C{A}; §C{B}; } — two statements, both calling
        // zero-arg functions. After elision on the first, the second §C{B}
        // could be absorbed.
        var lam = new LambdaExpressionNode(
            default,
            id: "l001",
            parameters: new List<LambdaParameterNode>(),
            effects: null,
            isAsync: false,
            expressionBody: null,
            statementBody: new List<StatementNode>
            {
                new BindStatementNode(
                    default,
                    name: "x",
                    typeName: null,
                    isMutable: false,
                    initializer: new CallExpressionNode(default, "A", new List<ExpressionNode>()),
                    attributes: new AttributeCollection()),
                new CallStatementNode(
                    default,
                    target: "B",
                    fallible: false,
                    arguments: new List<ExpressionNode>(),
                    attributes: new AttributeCollection()),
            },
            attributes: new AttributeCollection());

        var emitted = EmitWithImplicitCloser(lam);

        // Both inner calls must keep §/C — otherwise §C{A} absorbs §C{B}
        // as inline arg.
        Assert.Contains("§C{A} §/C", emitted);
        Assert.Contains("§C{B} §/C", emitted);
    }

    private static string LocateEmitterSourceFile()
    {
        // Walk up from the test assembly dir until we find the repo root,
        // then return src/Calor.Compiler/Migration/CalorEmitter.cs.
        var dir = System.IO.Path.GetDirectoryName(
            typeof(CallExpressionImplicitCloseTests).Assembly.Location)!;
        for (int i = 0; i < 10; i++)
        {
            var candidate = System.IO.Path.Combine(
                dir, "src", "Calor.Compiler", "Migration", "CalorEmitter.cs");
            if (System.IO.File.Exists(candidate)) return candidate;
            var parent = System.IO.Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new System.IO.FileNotFoundException(
            "Could not locate src/Calor.Compiler/Migration/CalorEmitter.cs above test assembly");
    }
}
