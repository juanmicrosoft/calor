using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Characterization tests pinning <c>ParseCallStatement</c>'s implicit-close
/// behavior at <c>Parser.cs:1374-1382</c>. The v0.6 call-elision RFC
/// (<c>docs/plans/v0.6-call-closer-elision.md</c>) generalizes this
/// statement-only behavior to expression-context calls; these tests pin the
/// current statement-only baseline before that change is implemented.
/// </summary>
public class CallStatementImplicitCloseTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static CallStatementNode FirstCall(ModuleNode module)
    {
        var func = module.Functions.First();
        return func.Body.OfType<CallStatementNode>().First();
    }

    [Fact]
    public void StatementContext_OneArg_NoAMarker_NoCloser_Parses()
    {
        // The implicit-close form: §C{Target} expr (no §A, no §/C)
        // is legal for STATEMENT context per Parser.cs:1374-1382.
        var source = """
module Test Test block
  func Foo ()
    !
    Console.WriteLine("hello")
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.Single(call.Arguments);
        Assert.IsType<StringLiteralNode>(call.Arguments[0]);
    }

    [Fact]
    public void StatementContext_OneArg_WithAMarker_ExplicitCloser_Parses()
    {
        // The canonical form: §C{Target} §A expr §/C
        var source = """
module Test Test block
  func Foo ()
    !
    Console.WriteLine("hello")
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("Console.WriteLine", call.Target);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void StatementContext_ZeroArg_ExplicitCloser_Parses()
    {
        // Zero-arg form requires §/C today.
        var source = """
module Test Test block
  func Foo ()
    !
    DoWork()
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("DoWork", call.Target);
        Assert.Empty(call.Arguments);
    }

    [Fact]
    public void StatementContext_MultiArg_WithAMarkers_ExplicitCloser_Parses()
    {
        // Multi-arg form: §C{Target} §A x §A y §/C
        var source = """
module Test Test block
  func Foo ()
    !
    Math.Max(1, 2)
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var call = FirstCall(module);
        Assert.Equal("Math.Max", call.Target);
        Assert.Equal(2, call.Arguments.Count);
    }

    [Fact]
    public void ExpressionContext_RequiresExplicitCloser_BaselineForRfc()
    {
        // Pins the contrast: in EXPRESSION context (here, as a binding
        // initializer) the call expression today REQUIRES §/C. The v0.6
        // call-elision RFC proposes lifting this requirement.
        //
        // This passes today because §/C is present:
        var source = """
module Test Test block
  func Foo ()
    !
    let string:x = Greeting()
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");

        var bind = module.Functions.First().Body.OfType<BindStatementNode>().First();
        var call = Assert.IsType<CallExpressionNode>(bind.Initializer);
        Assert.Equal("Greeting", call.Target);
    }

    // -------------------------------------------------------------------
    // v0.6.2 RFC §3.2 / §4 — STATEMENT-CONTEXT EMITTER ELISION
    // -------------------------------------------------------------------
    // The parser already accepts both `§C{Foo}` (zero-arg implicit close)
    // and `§C{Foo} primary_expr` (one-arg inline) since pre-v0.6.
    // Native emission writes every call as name(args).

    private static string EmitModule(string source)
    {
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Original errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");
        var emitter = new Migration.CalorEmitter(
            new Migration.ConversionContext());
        return module.Accept<string>(emitter);
    }

    [Fact]
    public void V062_ZeroArgStmt_FlagOn_ElidesCloser()
    {
        var source = """
module Test Test block
  func Foo ()
    !
    DoWork()
""";
        var emitted = EmitModule(source);
        Assert.Contains("DoWork()", emitted);
    }

    [Fact]
    public void V062_OneArgStmt_LiteralArg_FlagOn_ElidesAAndCloser()
    {
        var source = """
module Test Test block
  func Foo ()
    !
    Console.WriteLine("hello")
""";
        var emitted = EmitModule(source);
        // CalorEmitter strips typed-literal prefixes for strings: STR:"x" -> "x"
        Assert.Contains("Console.WriteLine(\"hello\")", emitted);
        Assert.DoesNotContain("§A", emitted);
        Assert.DoesNotContain("§/C", emitted);
    }

    [Fact]
    public void V062_OneArgStmt_IdentifierArg_FlagOn_ElidesAAndCloser()
    {
        var source = """
module Test Test block
  func Foo (string:msg)
    !
    Console.WriteLine(msg)
""";
        var emitted = EmitModule(source);
        Assert.Contains("Console.WriteLine(msg)", emitted);
        Assert.DoesNotContain("§A msg", emitted);
    }

    [Fact]
    public void V062_OneArgStmt_NestedZeroArgCallArg_ElidesOuterButKeepsInner()
    {
        // Inner zero-arg call is visited in inline-sibling context, so it
        // must keep §/C; outer stmt call elides because it's at top level.
        var source = """
module Test Test block
  func Foo ()
    !
    Outer(Inner())
""";
        var emitted = EmitModule(source);
        // Outer elided, inner kept its §/C
        Assert.Contains("Outer(Inner())", emitted);
        // No standalone §A on this line
        var line = emitted.Split('\n').First(l => l.Contains("Outer"));
        Assert.DoesNotContain("§A", line);
    }

    [Fact]
    public void V062_OneArgStmt_NewExpressionArg_ElidesAAndCloser()
    {
        // §NEW{T} on the same line — IsExpressionStart accepts §NEW.
        var source = """
module Test Test block
  using System.Text
  func Foo ()
    !
    Process(new StringBuilder())
""";
        var emitted = EmitModule(source);
        Assert.Contains("Process(new StringBuilder())", emitted);
    }

    [Fact]
    public void V062_NamedArg_StaysStandardForm()
    {
        // Named one-arg cannot use the inline form (§A[name] is required).
        var source = """
module Test Test block
  func Foo ()
    !
    Greet(who: "world")
""";
        var emitted = EmitModule(source);
        Assert.Contains("Greet(who: \"world\")", emitted);
    }

    [Fact]
    public void V062_MultiArgStmt_StaysStandardForm()
    {
        var source = """
module Test Test block
  func Foo ()
    !
    Math.Max(1, 2)
""";
        var emitted = EmitModule(source);
        // Emitter strips INT: prefix on int literals.
        Assert.Contains("Math.Max(1, 2)", emitted);
    }

    [Fact]
    public void V062_RoundTrip_OneArgElision_PreservesAst()
    {
        // Source has standard form; emit-then-reparse must produce the
        // same logical AST as the original.
        var source = """
module Test Test block
  func Foo ()
    !
    Console.WriteLine("hello")
    Process(42)
""";
        var module1 = Parse(source, out var diags1);
        Assert.False(diags1.HasErrors);

        var emitted = EmitModule(source);
        var module2 = Parse(emitted, out var diags2);
        Assert.False(diags2.HasErrors,
            $"Re-parse errors: {string.Join("; ", diags2.Errors.Select(e => e.Message))}");

        var calls1 = module1.Functions.First().Body.OfType<CallStatementNode>().ToList();
        var calls2 = module2.Functions.First().Body.OfType<CallStatementNode>().ToList();
        Assert.Equal(calls1.Count, calls2.Count);
        for (int i = 0; i < calls1.Count; i++)
        {
            Assert.Equal(calls1[i].Target, calls2[i].Target);
            Assert.Equal(calls1[i].Arguments.Count, calls2[i].Arguments.Count);
        }
    }

    [Fact]
    public void V062_RoundTrip_Idempotent_AfterFirstElidedEmit()
    {
        // emit(parse(emit(parse(src)))) == emit(parse(src))
        var source = """
module Test Test block
  func Foo (string:msg)
    !
    Console.WriteLine(msg)
    DoWork()
""";
        var emitted1 = EmitModule(source);
        var emitted2 = EmitModule(emitted1);
        Assert.Equal(emitted1, emitted2);
    }

    [Fact]
    public void V062_InlineSiblingContext_StmtCalls_KeepCloser()
    {
        // Lambda short-statement-body emits multiple statements space-joined
        // on a single line, inside _inInlineSiblingContext. Stmt-context
        // elision must be suppressed there to avoid sibling absorption.
        var lam = new LambdaExpressionNode(
            default,
            id: "l001",
            parameters: new List<LambdaParameterNode>(),
            effects: null,
            isAsync: false,
            expressionBody: null,
            statementBody: new List<StatementNode>
            {
                new CallStatementNode(
                    default, target: "A", fallible: false,
                    arguments: new List<ExpressionNode>(),
                    attributes: new AttributeCollection()),
                new CallStatementNode(
                    default, target: "B", fallible: false,
                    arguments: new List<ExpressionNode>(),
                    attributes: new AttributeCollection()),
            },
            attributes: new AttributeCollection());

        var emitter = new Migration.CalorEmitter(
            new Migration.ConversionContext());
        var emitted = lam.Accept<string>(emitter);

        // Both inner calls must keep §/C inside lambda short-body.
        Assert.Contains("A()", emitted);
        Assert.Contains("B()", emitted);
    }

    [Fact]
    public void V062_ZeroArgStmt_AtEndOfBlock_RoundTrips()
    {
        // Last statement in a function body — followed by Dedent, not a sibling.
        var source = """
module Test Test block
  func Foo ()
    !
    Setup()
    Run()
""";
        var emitted = EmitModule(source);
        Assert.Contains("Run()", emitted);

        // Re-parse must yield the same two-call structure.
        var module2 = Parse(emitted, out var diags);
        Assert.False(diags.HasErrors);
        var calls = module2.Functions.First().Body.OfType<CallStatementNode>().ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal("Setup", calls[0].Target);
        Assert.Equal("Run", calls[1].Target);
        Assert.Empty(calls[0].Arguments);
        Assert.Empty(calls[1].Arguments);
    }

    // -------------------------------------------------------------------
    // v0.6.4 regression coverage — last-body-stmt-before-sibling-decl bug.
    // Prior behavior: ParseCallStatement called ExpectBlockEnd(EndCall) on
    // the post-§A path which consumed the parent block's terminating Dedent.
    // When the next sibling at the parent scope was another §F (function),
    // the enclosing function body's parser could not detect its own end and
    // tried to parse §F as a statement → Calor0100. See the discussion in
    // docs/plans/v0.6.4-roadmap.md item C (resolved via parser fix, not the
    // originally-planned sample rewrite).
    // -------------------------------------------------------------------

    [Fact]
    public void V064_ZeroArgStmt_LastInBody_BeforeSiblingFunc_Parses()
    {
        var source = """
module T T block
  func Main () → void
    ! cw
    Helper()
  func Helper private () → void
    Console.WriteLine("hi")
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");
        Assert.Equal(2, module.Functions.Count);
        Assert.Equal("Main", module.Functions[0].Name);
        Assert.Equal("Helper", module.Functions[1].Name);
    }

    [Fact]
    public void V064_OneArgStmtViaA_LastInBody_BeforeSiblingFunc_Parses()
    {
        var source = """
module T T block
  func Main () → void
    ! cw
    Helper(1)
  func Helper private (int:a) → void
    Console.WriteLine("hi")
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");
        Assert.Equal(2, module.Functions.Count);
        var mainCall = (CallStatementNode)module.Functions[0].Body[0];
        Assert.Equal("Helper", mainCall.Target);
        Assert.Single(mainCall.Arguments);
    }

    [Fact]
    public void V064_OneArgStmtInline_LastInBody_BeforeSiblingFunc_Parses()
    {
        // Inline single-arg form (no §A): exercises the early-return branch
        // in ParseCallStatement, not the post-§A end-handling branch.
        var source = """
module T T block
  func Main () → void
    ! cw
    Helper(42)
  func Helper private (int:a) → void
    Console.WriteLine("hi")
""";
        var module = Parse(source, out var diags);
        Assert.False(diags.HasErrors,
            $"Errors: {string.Join("; ", diags.Errors.Select(e => e.Message))}");
        Assert.Equal(2, module.Functions.Count);
    }
}
