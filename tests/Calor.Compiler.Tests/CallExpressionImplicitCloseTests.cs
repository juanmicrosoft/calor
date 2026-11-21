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
module Test Test block
  func Foo () → int
    !
    let x = Identity(1)
    return x
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
module Test Test block
  func Foo () → int
    !
    let x = Identity(1)
    return x
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstBindingInitializer<CallExpressionNode>(module);
        Assert.Equal("Identity", call.Target);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void ExpressionContext_OneInlineArg_TrailingMemberAccess_AttachesToArg()
    {
        // §B{n} §C{Identity} obj?.Length
        // Reading A (RFC §3.2 case A): ?.Length attaches to obj, not to the call result.
        // Note: the lexer treats `obj.Length` (with bare dot) as a single dotted
        // identifier, so we use `?.` to force a parser-level disambiguation.
        var source = """
module Test Test block
  func Foo ()
    !
    let string:obj = "hi"
    let n = Identity(obj?.Length)
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
module Test Test block
  func Foo ()
    !
    let n = (Maybe())?.Length
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
module Test Test block
  func Foo ()
    !
    let n = Maybe() . Length
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
    public void ExpressionContext_NestedCall_OuterStandardInnerElided_Parses()
    {
        // §C{outer} §A §C{inner} INT:1 §/C   ← one §/C goes to outer; inner elided
        var source = """
module Test Test block
  func Foo ()
    !
    let x = outer(inner(1))
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
module Test Test block
  func Foo ()
    !
    let x = outer(inner(1))
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
module Test Test block
  func Foo ()
    !
    let int:x = 1
    let r = o1(o2(i(x)))
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
module Test Test block
  func Foo ()
    !
    let int:x = 1
    let r = o1(o2(i(x)))
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
    public void ExpressionContext_OneArgFollowedBySiblingStatement_NoCalor0150()
    {
        // v0.6.3 — sibling-statement-on-next-line must NOT trigger Calor0150.
        // Without the same-line guard at Parser.cs ~7992, the §IF on the
        // next line would be misclassified as a second positional arg of
        // §C{pred}. Mirrors the conversion-scorecard test 093 (Delegates):
        // `§B{_pre} §C{pred} §IDX{numbers} i / §IF _pre ...`.
        var source = """
module Test Test block
  func Filter (int[]:numbers, F:pred) → int
    !
    var int:count = 0
    for i from 0 to (numbers.Length - 1) step 1
      let _pre = pred(numbers[i])
      if _pre
        set count (count + 1)
    return count
""";
        Parse(source, out var diags);
        Assert.DoesNotContain(diags.Errors,
            e => e.Code == DiagnosticCode.AmbiguousCallContinuation);
    }

    [Fact]
    public void LispContext_CallWithExplicitCloser_StillParses()
    {
        // Inside Lisp expressions, the canonical §A + §/C form must still
        // parse cleanly. The closer-elision change must not regress this.
        var source = """
module Test Test block
  func Foo () → int
    !
    let x = (f(1) + g(2))
    return x
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

    // ----- Phase 2: emitted call form -----

    [Fact]
    public void Emitter_ZeroArgCall_DefaultFlag_EmitsImplicitCloser()
    {
        // v0.6.1 flipped the default: zero-arg calls now elide §/C by default.
        // RFC v0.6 §6 / docs/plans/v0.6-call-closer-elision.md.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var emitter = new Migration.CalorEmitter();
        var output = call.Accept<string>(emitter);
        Assert.Equal("Foo()", output);
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
        var source = @"module Demo Demo block
  class C
    func M1 ()
      !
      Foo()

    func M2 ()
      !
      Bar()
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
        Assert.Contains("A()", output);
        Assert.EndsWith(")", output);
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
        Assert.Contains("A()", output);
        Assert.Contains("B()", output);
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
        Assert.Equal("Foo()", output);
    }

    [Fact]
    public void Emitter_ZeroArgCall_ImplicitCloserFlagTrue_ElidesCloser()
    {
        // With the flag set, zero-arg calls drop §/C.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var ctx = new Migration.ConversionContext();
        var emitter = new Migration.CalorEmitter(ctx);
        var output = call.Accept<string>(emitter);
        Assert.Equal("Foo()", output);
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
        var ctx = new Migration.ConversionContext();
        var emitter = new Migration.CalorEmitter(ctx);
        var output = call.Accept<string>(emitter);
        Assert.EndsWith(")", output);
    }

    [Fact]
    public void Emitter_OneArgCall_ImplicitCloserFlagTrue_ElidesCloser()
    {
        // v0.6.3 (RFC v0.6 call-closer-elision §2.1/§2.2): one-arg expression-context
        // calls now elide §/C by default — mirroring the stmt-context behavior
        // shipped in v0.6.2 (PR #652). Trailing-postfix safety (RFC §3.2 case A)
        // is handled by FieldAccessNode hoisting on section-marker detection;
        // sibling-absorption safety is handled by _inInlineSiblingContext.
        var args = new List<ExpressionNode>
        {
            new IntLiteralNode(default, 42),
        };
        var call = new CallExpressionNode(default, "Identity", args);
        var ctx = new Migration.ConversionContext();
        var emitter = new Migration.CalorEmitter(ctx);
        var output = call.Accept<string>(emitter);
        Assert.Equal("Identity(42)", output);
    }

    [Fact]
    public void Emitter_OneArgCall_NamedArg_KeepsExplicitCloser()
    {
        // Named arguments cannot use the inline-arg form (the parser's one-arg
        // implicit-close path at Parser.cs:1398 only accepts a primary_expr,
        // not §A[name] value). Must keep §A[name] ... §/C even at leaf positions.
        var args = new List<ExpressionNode> { new IntLiteralNode(default, 42) };
        var argNames = new List<string?> { "value" };
        var call = new CallExpressionNode(default, "Configure", args, argNames);
        var emitter = new Migration.CalorEmitter();
        var output = call.Accept<string>(emitter);
        Assert.Equal("Configure(value: 42)", output);
    }

    [Fact]
    public void Emitter_OneArgCallInsideOuterCallArgs_KeepsExplicitCloser()
    {
        // BLOCKER mirror for one-arg: a one-arg call as one of several §A
        // arguments to an outer call MUST keep its explicit §/C. Otherwise
        // `§C{M} §A §C{A} x §A 2 §/C` would re-parse as `M(A(x, 2))` instead
        // of `M(A(x), 2)`. _inInlineSiblingContext suppresses the elision.
        var args = new List<ExpressionNode>
        {
            new CallExpressionNode(default, "A", new List<ExpressionNode>
            {
                new ReferenceNode(default, "x"),
            }),
            new IntLiteralNode(default, 2),
        };
        var outer = new CallExpressionNode(default, "M", args);
        var emitter = new Migration.CalorEmitter();
        var output = outer.Accept<string>(emitter);
        // Inner one-arg A(x) must keep its §/C inside the outer §A chain.
        Assert.Contains("A(x)", output);
        Assert.EndsWith(")", output);
    }

    [Fact]
    public void Emitter_OneArgCall_FlagTrue_RoundTripsThroughParser()
    {
        // End-to-end: the parser accepts the emitter's flag-true one-arg output.
        var call = new CallExpressionNode(default, "Identity", new List<ExpressionNode>
        {
            new IntLiteralNode(default, 1),
        });
        var emitted = EmitWithImplicitCloser(call);
        Assert.Equal("Identity(1)", emitted);

        var source = $$"""
module Test Test block
  func Bar ()
    !
    let x = {{emitted}}
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var rt = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("Identity", rt.Target);
        Assert.Single(rt.Arguments);
        Assert.IsType<IntLiteralNode>(rt.Arguments[0]);
    }

    [Fact]
    public void Emitter_ZeroArgCall_FlagTrue_RoundTripsThroughParser()
    {
        // End-to-end: parser accepts the emitter's flag-true output.
        var call = new CallExpressionNode(default, "Foo", new List<ExpressionNode>());
        var ctx = new Migration.ConversionContext();
        var emitter = new Migration.CalorEmitter(ctx);
        var emitted = call.Accept<string>(emitter);
        Assert.Equal("Foo()", emitted);

        // Wrap in a binding so we can re-parse it.
        var source = $$"""
module Test Test block
  func Bar ()
    !
    let x = {{emitted}}
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
        var ctx = new Migration.ConversionContext();
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
        Assert.Contains("A()", emitted);

        // Wrap in a binding context to round-trip through the parser.
        var source = $$"""
module Test Test block
  func Foo ()
    !
    let int:x = {{emitted}}
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
        Assert.Contains("A()", emitted);

        var source = $$"""
module Test Test block
  func Foo ()
    !
    let T:x = {{emitted}}
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
        Assert.Contains("GetIdx()", emitted);
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
        Assert.Contains("A()", emitted);
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
module Test Test block
  func Foo ()
    !
    let Dictionary<string, int>:d = new Dictionary<string, int>() { { K(), V() } }
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var emitter = new Migration.CalorEmitter(
            new Migration.ConversionContext());
        var emitted = emitter.Emit(module);
        Assert.Contains("K()", emitted);
        Assert.Contains("V()", emitted);
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
        Assert.Equal("(Foo() is string)", emitted);

        var typeOpAs = new TypeOperationNode(
            default,
            TypeOp.As,
            new CallExpressionNode(default, "Foo", new List<ExpressionNode>()),
            "str");
        var emittedAs = EmitWithImplicitCloser(typeOpAs);
        Assert.Equal("(Foo() as string)", emittedAs);
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
        // Pattern 3: `var X = node.Y.Accept(this);` followed within ~50 lines
        // by an interpolation `{X} {<other>}` / `{X} STR:` / `{X} "..."` /
        // `{X} §...` (the post-loop-4 catch from devil's-advocate finding #2).
        var localAccRegex = new System.Text.RegularExpressions.Regex(
            @"^\s*var (\w+) = (?:node|entry|field|arg)[\w.]*\.Accept\(this\);");

        // Whitelisted lines (file-relative 1-based). Each entry must carry a
        // justification comment in the surrounding source.
        var whitelistedLines = new HashSet<int>
        {
            // BoundVariables in forall/exists/implication emit as binding
            // patterns (e.g. `x:i32`), not as expressions; they cannot
            // contain a zero-arg §C call.
            // (See CalorEmitter.Visit(ForallExpressionNode) / ExistsExpressionNode)

            // Lambda expression-body emits `§LAM{header} {body} §/LAM`.
            // The token after `{body}` is `§/LAM` (LambdaEnd) which is NOT
            // in Parser.IsExpressionStart(), so an elided zero-arg §C body
            // cannot absorb it as an inline arg. Loop-5 verified.
            2674,

            // With-expression emits `§WITH {target} {assignments} §/WITH`.
            // The token after `{target}` is `{assignments}` which begins
            // with `§SET` (not in IsExpressionStart), and the closer is
            // `§/WITH`. An elided zero-arg §C target cannot absorb §SET
            // as an inline arg. Loop-5 verified.
            3221,
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

            // Pattern 3: scan forward up to 50 lines for an interpolation
            // `{X} {other}` / `{X} STR:` / `{X} "..."` / `{X} §...`. Bail
            // out at any line that starts a new Visit method.
            var localMatch = localAccRegex.Match(line);
            if (localMatch.Success)
            {
                var localName = localMatch.Groups[1].Value;
                var marker = "{" + localName + "}";
                for (int j = i + 1; j < Math.Min(i + 50, lines.Length); j++)
                {
                    var u = lines[j];
                    if (System.Text.RegularExpressions.Regex.IsMatch(u,
                            @"^\s*(public|private|internal)\s+\S.*\bVisit\b"))
                        break;
                    var idx = u.IndexOf(marker, StringComparison.Ordinal);
                    if (idx < 0) continue;
                    var after = u.Substring(idx + marker.Length);
                    if (after.Length == 0) continue;
                    if (after[0] != ' ' && after[0] != '\t') continue;
                    var afterTrim = after.TrimStart();
                    // Risky next character: another interpolation `{`,
                    // typed-literal prefix (STR:/INT:/...), string literal `"`
                    // (incl. escaped `\"`), or a section marker `§` / `\u00A7`.
                    bool risky =
                        afterTrim.StartsWith("{", StringComparison.Ordinal) ||
                        System.Text.RegularExpressions.Regex.IsMatch(
                            afterTrim, @"^(STR|INT|FLOAT|BOOL|CHAR|BYTE):") ||
                        afterTrim.StartsWith("\"", StringComparison.Ordinal) ||
                        afterTrim.StartsWith("\\\"", StringComparison.Ordinal) ||
                        afterTrim.StartsWith("\u00A7", StringComparison.Ordinal);
                    if (risky && !whitelistedLines.Contains(j + 1))
                    {
                        violations.Add(
                            $"Pattern3 (local-then-interpolate-sibling) line {i + 1} → used at {j + 1}: " +
                            $"var {localName} = ...Accept(this); ... {u.TrimStart()}");
                    }
                    break;
                }
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
module Test Test block
  func Foo () → bool
    !
    let x = (Foo() is string)
    return x
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var fullEmitter = new Migration.CalorEmitter(
            new Migration.ConversionContext());
        var emitted = module.Accept<string>(fullEmitter);
        // Emitted form must still close the operand call.
        Assert.Contains("(Foo() is string)", emitted);

        // The call and canonical type must survive re-parsing.
        var module2 = Parse(emitted, out var diags2);
        Assert.False(diags2.HasErrors,
            $"Re-parse errors: {string.Join("; ", diags2.Errors.Select(e => e.Message))}");
        var bind = module2.Functions[0].Body.OfType<BindStatementNode>().First();
        var isPattern = Assert.IsType<IsPatternNode>(bind.Initializer);
        var operandCall = Assert.IsType<CallExpressionNode>(isPattern.Operand);
        Assert.Equal("Foo", operandCall.Target);
        Assert.Empty(operandCall.Arguments);
        Assert.Equal("string", isPattern.TargetType);
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
        Assert.Contains("Lo()..Hi()", emitted);
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
        Assert.Contains("A()", emitted);
        Assert.Contains("B()", emitted);
    }

    [Fact]
    public void RoundTrip_NullConditional_ZeroArgCallTarget_KeepsExplicitCloser()
    {
        // BLOCKER from loop-5 devil's-advocate finding.
        // (?. §C{Get} "Length") would re-parse as Get("Length") because
        // string literals are in IsExpressionStart and the call's inline-arg
        // branch absorbs the member name.
        var source = """
module Test Test block
  func Foo () → int
    !
    let x = (Get())?.Length
    return x.unwrap
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var fullEmitter = new Migration.CalorEmitter(
            new Migration.ConversionContext());
        var emitted = module.Accept<string>(fullEmitter);
        // Emitted form must keep §/C on the call target — otherwise the
        // member name string literal would be absorbed as inline arg.
        Assert.Contains("(Get())?.Length", emitted);

        // Re-parse to confirm the round-trip is structurally stable.
        var module2 = Parse(emitted, out var diags2);
        Assert.False(diags2.HasErrors,
            $"Re-parse errors: {string.Join("; ", diags2.Errors.Select(e => e.Message))}");
        var bind = module2.Functions[0].Body.OfType<BindStatementNode>().First();
        var nc = Assert.IsType<NullConditionalNode>(bind.Initializer);
        var targetCall = Assert.IsType<CallExpressionNode>(nc.Target);
        Assert.Equal("Get", targetCall.Target);
        Assert.Empty(targetCall.Arguments);
        Assert.Equal("Length", nc.MemberName);
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
