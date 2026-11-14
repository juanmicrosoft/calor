using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// #1485 -- a block-opening clause owns only the lines indented under it. An
/// empty clause body (no indented lines) is legal and must not swallow the
/// next same-column statement, nor the enclosing block's dedent.
/// Retired layouts keep their original identities as rejection/internal controls,
/// alongside native counterparts where the current grammar has an equivalent.
/// </summary>
public class EmptyClauseBodyTests
{
    // Native copy of X-TRYCATCH-01. The file label is not Probe's namespace.
    private const string XTryCatch01 = """
        module XTryCatch01 overflow unchecked
          using System
          module Probe public
            field int:counter private static = 0

            func Work public static (bool:fail)
              ! throw,alloc
              if fail
                throw new InvalidOperationException("x")

            func Step public static (bool:fail)
              ! throw,alloc
              try
                Work(fail)
              catch InvalidOperationException
              set counter counter + 1

            func Run public static () → string
              ! throw,alloc
              Step(false)
              Step(true)
              return counter.ToString()
        """;

    private const string XTryCatch01CSharp = """
        using System;

        public static class Probe
        {
            private static int counter = 0;

            public static void Work(bool fail)
            {
                if (fail)
                {
                    throw new InvalidOperationException("x");
                }
            }

            public static void Step(bool fail)
            {
                try
                {
                    Work(fail);
                }
                catch (InvalidOperationException)
                {
                }
                counter = counter + 1;
            }

            public static string Run()
            {
                Step(false);
                Step(true);
                return counter.ToString();
            }
        }
        """;

    // ------------------------------------------------------------------
    // try / catch / finally
    // ------------------------------------------------------------------

    [Fact]
    public void EmptyCatch_SameColumnStatement_FollowsTheTry()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func Step public (int:x) → int
                ! throw
                var int:n = 0
                try
                  set n 10 / x
                catch DivideByZeroException
                set n n + 1
                return n
            """);

        Assert.Equal(4, body.Count);
        var tryStmt = Assert.IsType<TryStatementNode>(body[1]);
        Assert.Empty(Assert.Single(tryStmt.CatchClauses).Body);
        Assert.IsType<AssignmentStatementNode>(body[2]);
        Assert.IsType<ReturnStatementNode>(body[3]);
    }

    [Fact]
    public void EmptyFinally_SameColumnStatement_FollowsTheTry()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func Step public (int:x) → int
                ! throw
                var int:n = 0
                try
                  set n x
                finally
                set n n + 1
                return n
            """);

        Assert.Equal(4, body.Count);
        var tryStmt = Assert.IsType<TryStatementNode>(body[1]);
        Assert.NotNull(tryStmt.FinallyBody);
        Assert.Empty(tryStmt.FinallyBody!);
        Assert.IsType<AssignmentStatementNode>(body[2]);
    }

    [Fact]
    public void MultipleCatches_FirstEmpty_SecondKeepsItsBody_StatementFollows()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func Step public (int:x) → int
                ! throw
                var int:n = 0
                try
                  set n 10 / x
                catch DivideByZeroException
                catch Exception:e
                  set n -1
                set n n + 100
                return n
            """);

        Assert.Equal(4, body.Count);
        var tryStmt = Assert.IsType<TryStatementNode>(body[1]);
        Assert.Equal(2, tryStmt.CatchClauses.Count);
        Assert.Empty(tryStmt.CatchClauses[0].Body);
        Assert.Single(tryStmt.CatchClauses[1].Body);
        Assert.IsType<AssignmentStatementNode>(body[2]);
    }

    [Fact]
    public void NestedTry_EmptyInnerCatch_DoesNotStealOuterCatch()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func Step public (int:x) → int
                ! throw
                var int:n = 0
                try
                  try
                    set n 10 / x
                  catch DivideByZeroException
                catch Exception
                  set n -1
                return n
            """);

        Assert.Equal(3, body.Count);
        var outer = Assert.IsType<TryStatementNode>(body[1]);
        var inner = Assert.IsType<TryStatementNode>(Assert.Single(outer.TryBody));
        Assert.Equal("DivideByZeroException", Assert.Single(inner.CatchClauses).ExceptionType);
        Assert.Empty(inner.CatchClauses[0].Body);
        Assert.Equal("Exception", Assert.Single(outer.CatchClauses).ExceptionType);
        Assert.Single(outer.CatchClauses[0].Body);
    }

    [Fact]
    public void NestedTry_EmptyInnerCatch_SiblingInOuterTryBody()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func Step public (int:x) → int
                ! throw
                var int:n = 0
                try
                  try
                    set n 10 / x
                  catch DivideByZeroException
                  set n n + 1
                catch Exception
                  set n -1
                return n
            """);

        var outer = Assert.IsType<TryStatementNode>(body[1]);
        Assert.Equal(2, outer.TryBody.Count);
        var inner = Assert.IsType<TryStatementNode>(outer.TryBody[0]);
        Assert.Empty(Assert.Single(inner.CatchClauses).Body);
        Assert.IsType<AssignmentStatementNode>(outer.TryBody[1]);
        Assert.Single(outer.CatchClauses);
    }

    [Fact]
    public void EmptyCatch_AtEndOfMethod_LeavesNextFunctionIntact()
    {
        var module = Parse("""
            module T
              func A public (int:x) → void
                ! throw
                try
                  let int:y = 10 / x
                catch DivideByZeroException
              func B public () → int
                !
                return 2
            """);

        Assert.Equal(2, module.Functions.Count);
        Assert.IsType<TryStatementNode>(Assert.Single(module.Functions[0].Body));
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[1].Body));
    }

    [Fact]
    public void EmptyClauses_ExecuteWithCorrectStructure()
    {
        var type = Compile("""
            module EmptyClause T
              module TModule public
                func Step public (int:x) → int
                  ! throw
                  var int:n = 0
                  try
                    set n 10 / x
                  catch DivideByZeroException
                  set n n + 1
                  try
                    set n n + 10
                  finally
                  set n n + 100
                  return n
            """, "T.TModule");

        Assert.Equal(111, Invoke(type, "Step", 0));
        Assert.Equal(116, Invoke(type, "Step", 2));
    }

    [Fact]
    public void XTryCatch01_ReturnsTwo()
    {
        var module = Parse(XTryCatch01);
        Assert.Equal("", Assert.Single(module.NamespaceScopes).FullName);
        var probe = module.Classes.Single(c => c.Name == "Probe");
        var step = probe.Methods.Single(m => m.Name == "Step");
        Assert.Equal(2, step.Body.Count);
        Assert.Empty(Assert.IsType<TryStatementNode>(step.Body[0]).CatchClauses[0].Body);

        var type = Compile(XTryCatch01, "Probe");
        Assert.Equal("Probe", type.FullName);
        Assert.Equal("2", Invoke(type, "Run"));
    }

    [Fact]
    public void XTryCatch01_ConvertedFromCSharp_BehavesLikeTheOriginal()
    {
        var conversion = new CSharpToCalorConverter(new ConversionOptions { ModuleName = "XTryCatch01" })
            .Convert(XTryCatch01CSharp);
        Assert.True(conversion.Success, string.Join("\n", conversion.Issues.Select(i => i.Message)));

        var type = Compile(conversion.CalorSource!, "Probe");
        Assert.Equal("2", Invoke(type, "Run"));
    }

    // ------------------------------------------------------------------
    // Every other clause with an optional indented body
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> EmptyClauseCases() => new[]
    {
        new object[] { "if-then", "§IF b" },
        new object[] { "elseif", "§IF b\n      §B{a:i32} 1\n    §EI (== n 2)" },
        new object[] { "else", "§IF b\n      §B{a:i32} 1\n    §EL" },
        new object[] { "block-if-else", "§IF b\n      §B{a:i32} 1\n    §EL" },
        new object[] { "for", "§L{i:0:3:1}" },
        new object[] { "while", "§WH b" },
        new object[] { "foreach", "§EACH{x:i32} xs" },
        new object[] { "eachkv", "§EACHKV{k:v} d" },
        new object[] { "using", "§USE{s:MemoryStream} §NEW{MemoryStream} §/NEW" },
        new object[] { "sync", "§SYNC (o)" },
        new object[] { "unsafe", "§UNSAFE" },
        new object[] { "fixed", "§FIXED{p:i32*:xs}" },
        new object[] { "preprocessor", "§PP{DEBUG}" },
        new object[] { "try-empty-catch", "§TR\n      §B{a:i32} 1\n    §CA{Exception:}" },
        new object[] { "try-empty-finally", "§TR\n      §B{a:i32} 1\n    §FI" },
        new object[] { "match-last-case", "§W n\n      §K 1\n        §B{a:i32} 1\n      §K _" },
    };

    // The provider above is the frozen logical-subject/xUnit identity, not source admission.
    // Each native recipe is explicit; no retired source is translated or executed.
    private static string NativeClause(string name) => name switch
    {
        "if-then" => "if b",
        "elseif" => "if b\n      let int:a = 1\n    else if n == 2",
        "else" => "if b\n      let int:a = 1\n    else",
        "block-if-else" => "if b\n      let int:a = 1\n    else",
        "for" => "for i from 0 to 3 step 1",
        "while" => "while b",
        "foreach" => "each int:x in xs",
        "eachkv" => "each k, v in d",
        "using" => "using MemoryStream:s = new MemoryStream()",
        "sync" => "lock (o)",
        "unsafe" => "unsafe",
        "fixed" => "fixed int*:p = xs",
        "preprocessor" => "pp DEBUG",
        "try-empty-catch" => "try\n      let int:a = 1\n    catch Exception",
        "try-empty-finally" => "try\n      let int:a = 1\n    finally",
        "match-last-case" => "match n\n      case 1\n        let int:a = 1\n      case _",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Theory]
    [MemberData(nameof(EmptyClauseCases))]
    public void EmptyClause_DoesNotSwallowSameColumnSibling(string name, string clause)
    {
        AssertRetiredClauseRejected(name, clause);
        var source = "module T\n"
            + "  func A public (bool:b, int:n, int[]:xs, Dictionary<string,int>:d, object:o) → int\n    !\n"
            + "    !\n"
            + "    " + NativeClause(name) + "\n"
            + "    return 7\n"
            + "  func B public () → int\n    !\n"
            + "    !\n"
            + "    return 8\n";
        var module = Parse(source);

        Assert.Equal(2, module.Functions.Count);
        var body = module.Functions[0].Body;
        Assert.Equal(2, body.Count);
        AssertEmptyClause(name, body[0]);
        Assert.IsType<ReturnStatementNode>(body[1]);
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[1].Body));
    }

    [Theory]
    [MemberData(nameof(EmptyClauseCases))]
    public void EmptyClause_AtEndOfEnclosingBlock_DoesNotStealItsDedent(string name, string clause)
    {
        AssertRetiredClauseRejected(name, clause);
        // The empty clause is the last statement of a loop body; the
        // statement after it is at the loop's column, so it follows the loop.
        var source = "module T\n"
            + "  func A public (bool:b, int:n, int[]:xs, Dictionary<string,int>:d, object:o) → int\n    !\n"
            + "    !\n"
            + "    for j from 0 to 1 step 1\n"
            + "      " + NativeClause(name).Replace("\n    ", "\n      ") + "\n"
            + "    return 7\n";
        var module = Parse(source);

        var body = Assert.Single(module.Functions).Body;
        Assert.Equal(2, body.Count);
        var loop = Assert.IsType<ForStatementNode>(body[0]);
        Assert.Single(loop.Body);
        AssertEmptyClause(name, loop.Body[0]);
        Assert.IsType<ReturnStatementNode>(body[1]);
    }

    [Fact]
    public void EmptyThen_InsideOuterIf_DoesNotStealOuterElse()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func A public (bool:a, bool:b) → int
                !
                if a
                  if b
                else
                  return 2
                return 1
            """);

        Assert.Equal(2, body.Count);
        var outer = Assert.IsType<IfStatementNode>(body[0]);
        var inner = Assert.IsType<IfStatementNode>(Assert.Single(outer.ThenBody));
        Assert.Null(inner.ElseBody);
        Assert.Single(outer.ElseBody!);
    }

    [Fact]
    public void EmptyClauses_RunWithCorrectControlFlow()
    {
        var type = Compile("""
            module EmptyClause T
              module TModule public
                func A public (int:x) → int
                  !
                  var int:n = 0
                  if x > 100
                  set n n + 1
                  for i from 0 to 3 step 1
                  set n n + 10
                  while false
                  set n n + 100
                  if x > 0
                    set n n + 1000
                  else
                  set n n + 10000
                  match x
                    case 1
                      set n n + 100000
                    case _
                  set n n + 1000000
                  return n
            """, "T.TModule");

        Assert.Equal(1111111, Invoke(type, "A", 1));
        Assert.Equal(1010111, Invoke(type, "A", 0));
    }

    [Fact]
    public void EmptyPreprocessorBlock_DoesNotSwallowSameColumnSibling()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func A public () → int
                !
                pp DEBUG
                return 7
            """);

        Assert.Equal(2, body.Count);
        Assert.Empty(Assert.IsType<PreprocessorDirectiveNode>(body[0]).Body);
        Assert.IsType<ReturnStatementNode>(body[1]);
    }

    [Fact]
    public void EmptyPreprocessorBlock_InsideIf_DoesNotStealOuterElse()
    {
        var body = ParseSingleFunctionBody("""
            module T
              func A public (bool:b) → int
                !
                if b
                  pp DEBUG
                else
                  return 2
                return 1
            """);

        Assert.Equal(2, body.Count);
        var outer = Assert.IsType<IfStatementNode>(body[0]);
        Assert.IsType<PreprocessorDirectiveNode>(Assert.Single(outer.ThenBody));
        Assert.Single(outer.ElseBody!);
    }

    [Fact]
    public void BlockLambda_WithoutCloser_DoesNotSwallowFollowingStatements()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} () -> i32
                §E{}
                §B{a:Action} §LAM{}
                §R 7
            """);
        var source = """
            module T
              func A public () → int
                !
                let Action:a = __lambda__{}
                return 7
            """;
        var body = Assert.Single(ParseInternal(source, ("__lambda__", TokenKind.Lambda)).Functions).Body;

        Assert.Equal(2, body.Count);
        Assert.IsType<BindStatementNode>(body[0]);
        Assert.IsType<ReturnStatementNode>(body[1]);

        var native = ParseSingleFunctionBody("""
            module T
              func A public () → int
                !
                let Action:a = () =>
                  return
                return 7
            """);
        Assert.Equal(2, native.Count);
        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.IsType<LambdaExpressionNode>(
            Assert.IsType<BindStatementNode>(native[0]).Initializer).StatementBody!));
        Assert.IsType<ReturnStatementNode>(native[1]);
    }

    [Fact]
    public void EmptyGetter_DoesNotStealPropertyDedent()
    {
        var module = Parse("""
            module T
              class C public
                property int:P public
                  get
                  set
                func B public () → int
                  !
                  return 7
              class D public
            """);

        Assert.Equal(2, module.Classes.Count);
        Assert.Single(module.Classes[0].Properties);
        Assert.Single(module.Classes[0].Methods);
    }

    [Fact]
    public void MultilineHeader_BodyOnHeaderLastLine_StaysInBody()
    {
        // PrintStatementNode's prefix dispatch is internal; native calls are expression statements.
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} (bool:b) -> void
                §E{cw}
                §IF (==
                  1 2
                ) §P "then"
                §P "after"
            """);
        var body = Assert.Single(ParseInternal("""
            module T
              func A public (bool:b) → void
                ! cw
                if (
                  1 == 2
                ) __print__ "then"
                __print__ "after"
            """, ("__print__", TokenKind.Print)).Functions).Body;

        Assert.Equal(2, body.Count);
        Assert.IsType<PrintStatementNode>(Assert.Single(Assert.IsType<IfStatementNode>(body[0]).ThenBody));
        Assert.IsType<PrintStatementNode>(body[1]);

        var native = ParseSingleFunctionBody("""
            module T
              func A public (bool:b) → void
                ! cw
                if (
                  1 == 2
                ) Console.WriteLine("then")
                Console.WriteLine("after")
            """);

        Assert.Equal(2, native.Count);
        Assert.IsType<ExpressionStatementNode>(Assert.Single(Assert.IsType<IfStatementNode>(native[0]).ThenBody));
        Assert.IsType<ExpressionStatementNode>(native[1]);
    }

    [Fact]
    public void MultilineFunctionHeader_BodyOnHeaderLastLine_StaysInBody()
    {
        AssertRetiredSourceRejected("§M{T}\n  §F{A:pub} (\n    bool:x\n  ) -> i32 §R 1\n");
        const string source = "module T\n  func A public (\n    bool:x\n  ) → int return 1\n";
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();

        // Ownership survives recovery; the public header still lacks its required budget.
        Assert.Equal(DiagnosticCode.MissingBoundaryEffectRow, Assert.Single(diagnostics.Errors).Code);
        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
    }

    [Fact]
    public void BracedLambda_OuterElseAfterEmptyInnerIf_BelongsToOuterIf()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} () -> i32
                §E{}
                §B{~n:i32} 0
                §B{a:Action} () → {
                  §IF false
                    §IF true
                  §EL
                    §ASSIGN n 1
                }
                §C{a.Invoke}
                §R n
            """);
        // Parentheses suppress lexer dedents just as the retired braces did. Only
        // synthetic Lambda/EndLambda tokens admit the internal statement-lambda body.
        var internalModule = ParseInternal("""
            module EmptyClause T
              module TModule public
                func A public () → int
                  !
                  var int:n = 0
                  let Action:a = (__lambda__{}
                    if false
                      if true
                    else
                      set n 1
                  __end__)
                  a.Invoke()
                  return n
            """, ("__lambda__", TokenKind.Lambda), ("__end__", TokenKind.EndLambda));
        var internalType = EmitAndLoad(new CSharpEmitter().Emit(internalModule), "T.TModule");
        Assert.Equal(1, Invoke(internalType, "A"));
        var type = Compile("""
            module EmptyClause T
              module TModule public
                func A public () → int
                  !
                  var int:n = 0
                  let Action:a = () =>
                    if false
                      if true
                    else
                      set n 1
                  a.Invoke()
                  return n
            """, "T.TModule");

        Assert.Equal(1, Invoke(type, "A"));
    }

    [Fact]
    public void BracedLambda_OuterCaseAfterInnerMatch_BelongsToOuterMatch()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} (i32:x) -> i32
                §E{}
                §B{~n:i32} 0
                §B{a:Action} () → {
                  §W x
                    §K 1
                      §W x
                        §K 2
                    §K _
                      §ASSIGN n 7
                }
                §C{a.Invoke}
                §R n
            """);
        var internalModule = ParseInternal("""
            module EmptyClause T
              module TModule public
                func A public (int:x) → int
                  !
                  var int:n = 0
                  let Action:a = (__lambda__{}
                    match x
                      case 1
                        match x
                          case 2
                      case _
                        set n 7
                  __end__)
                  a.Invoke()
                  return n
            """, ("__lambda__", TokenKind.Lambda), ("__end__", TokenKind.EndLambda));
        var internalType = EmitAndLoad(new CSharpEmitter().Emit(internalModule), "T.TModule");
        Assert.Equal(7, Invoke(internalType, "A", 3));
        Assert.Equal(0, Invoke(internalType, "A", 1));
        var type = Compile("""
            module EmptyClause T
              module TModule public
                func A public (int:x) → int
                  !
                  var int:n = 0
                  let Action:a = () =>
                    match x
                      case 1
                        match x
                          case 2
                      case _
                        set n 7
                  a.Invoke()
                  return n
            """, "T.TModule");

        Assert.Equal(7, Invoke(type, "A", 3));
        Assert.Equal(0, Invoke(type, "A", 1));
    }

    [Fact]
    public void FlatCloserScan_IgnoresBracketContinuationLines()
    {
        AssertRetiredSourceRejected("§M{T}\n  §F{A:pub} () -> void\n    §E{cw}\n"
            + "    §UNSAFE\n    §P \"first\"\n    §B{x:i32} (+\n1 2\n    )\n    §/UNSAFE\n    §P \"after\"\n");
        // Synthetic closers exercise the retired scan's internal production,
        // not native source admission. The low-column continuation must not end the scan.
        var body = Assert.Single(ParseInternal(
            "module T\n  func A public () → void\n    ! cw\n"
            + "    unsafe\n    __print__ \"first\"\n    let int:x = (\n1 + 2\n    )\n"
            + "    __end__\n    __print__ \"after\"\n",
            ("__print__", TokenKind.Print), ("__end__", TokenKind.EndUnsafe)).Functions).Body;

        Assert.Equal(2, body.Count);
        Assert.Equal(2, Assert.IsType<UnsafeBlockNode>(body[0]).Body.Count);
        var native = ParseSingleFunctionBody("""
            module T
              func A public () → void
                ! cw
                unsafe
                  Console.WriteLine("first")
                  let int:x = (
            1 + 2
                  )
                Console.WriteLine("after")
            """);
        Assert.Equal(2, native.Count);
        Assert.Equal(2, Assert.IsType<UnsafeBlockNode>(native[0]).Body.Count);
    }

    [Fact]
    public void BlockLambda_WithoutCloser_DoesNotTakeNextLineAsExpressionBody()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} () -> i32
                §E{}
                §B{~n:i32} 0
                §B{a:Func<i32>} §LAM{}
                (+ n 1)
                §R n
            """);
        var source = """
            module T
              func A public () → int
                !
                var int:n = 0
                let Func<int>:a = __lambda__{}
                (n + 1)
                return n
            """;
        // Bare arithmetic statements are no longer admitted. Preserve the exact recovered
        // structural oracle, with the error owned by that expression rather than the lambda.
        var tokens = InternalTokens(source, [("__lambda__", TokenKind.Lambda)]);
        var diagnostics = new DiagnosticBag();
        var module = new Parser(tokens, diagnostics).Parse();
        var error = Assert.Single(diagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnexpectedToken, error.Code);
        Assert.Equal("(n + 1)", source.Substring(error.Span.Start, error.Span.Length));
        Assert.Equal("A native expression statement must be an invocation or object creation.", error.Message);
        var body = Assert.Single(module.Functions).Body;

        Assert.Equal(4, body.Count);
        var binding = Assert.IsType<BindStatementNode>(body[1]);
        var lambda = Assert.IsType<LambdaExpressionNode>(binding.Initializer);
        Assert.Null(lambda.ExpressionBody);
        Assert.Null(lambda.StatementBody);
        Assert.IsType<ExpressionStatementNode>(body[2]);
        Assert.IsType<ReturnStatementNode>(body[3]);
        const string nativeSource = """
            module T
              func A public () → int
                !
                var int:n = 0
                let Func<int>:a = () => 0
                (n + 1)
                return n
            """;
        var nativeDiagnostics = new DiagnosticBag();
        var nativeTokens = new Lexer(nativeSource, nativeDiagnostics).TokenizeAllForParser();
        Assert.False(nativeDiagnostics.HasErrors, string.Join("\n", nativeDiagnostics));
        new Parser(nativeTokens, nativeDiagnostics).Parse();
        var nativeError = Assert.Single(nativeDiagnostics.Errors);
        Assert.Equal(DiagnosticCode.UnexpectedToken, nativeError.Code);
        Assert.Equal("(n + 1)", nativeSource.Substring(nativeError.Span.Start, nativeError.Span.Length));
        Assert.Equal(error.Message, nativeError.Message);
    }

    [Fact]
    public void FlatCloserScan_TracksNestedOpenerOnHeaderLine()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} (object:a, object:b) -> void
                §E{cw}
                §SYNC (a) §SYNC (b) §P "inside"
                §/SYNC
                §P "after"
            """);
        var body = Assert.Single(ParseInternal("""
            module T
              func A public (object:a, object:b) → void
                ! cw
                lock (a) lock (b) __print__ "inside"
                __end__
                __print__ "after"
            """, ("__print__", TokenKind.Print), ("__end__", TokenKind.EndSyncBlock)).Functions).Body;

        Assert.Equal(2, body.Count);
        var outer = Assert.IsType<SyncBlockNode>(body[0]);
        Assert.IsType<SyncBlockNode>(Assert.Single(outer.Body));
        Assert.IsType<PrintStatementNode>(body[1]);
        var native = ParseSingleFunctionBody("""
            module T
              func A public (object:a, object:b) → void
                ! cw
                lock (a)
                  lock (b)
                    Console.WriteLine("inside")
                Console.WriteLine("after")
            """);
        Assert.Equal(2, native.Count);
        Assert.IsType<SyncBlockNode>(Assert.Single(Assert.IsType<SyncBlockNode>(native[0]).Body));
        Assert.IsType<ExpressionStatementNode>(native[1]);
    }

    [Fact]
    public void ManyEmptyBlocksWithoutClosers_ParseWithoutErrors()
    {
        var lines = string.Join("\n", Enumerable.Repeat("    unsafe", 3000));
        var module = Parse("module T\n  func A public () → void\n    !\n" + lines + "\n");

        Assert.Equal(3000, Assert.Single(module.Functions).Body.Count);
        Assert.All(module.Functions[0].Body, statement => Assert.Empty(Assert.IsType<UnsafeBlockNode>(statement).Body));
    }

    // ------------------------------------------------------------------
    // Type- and function-level bodies
    // ------------------------------------------------------------------

    [Fact]
    public void EmptyClass_DoesNotAdoptSameColumnSiblingClass()
    {
        var module = Parse("""
            module T
              class K public
              class L public
                func B public () → int
                  !
                  return 2
            """);

        Assert.Equal(2, module.Classes.Count);
        Assert.Empty(module.Classes[0].Methods);
        Assert.Empty(module.Classes[0].NestedClasses);
        Assert.Single(module.Classes[1].Methods);
    }

    [Fact]
    public void EmptyNestedClass_DoesNotAdoptFollowingMethod()
    {
        var module = Parse("""
            module T
              class Outer public
                class Inner public
                func B public () → int
                  !
                  return 2
            """);

        var outer = Assert.Single(module.Classes);
        var inner = Assert.Single(outer.NestedClasses);
        Assert.Empty(inner.Methods);
        Assert.Equal("B", Assert.Single(outer.Methods).Name);
    }

    [Fact]
    public void EmptyFunction_DoesNotSwallowSiblingFunction()
    {
        var module = Parse("""
            module T
              func A public () → void
                !
              func B public () → int
                !
                return 2
            """);

        Assert.Equal(2, module.Functions.Count);
        Assert.Empty(module.Functions[0].Body);
        Assert.Single(module.Functions[1].Body);
    }

    [Fact]
    public void EmptyMemberPreprocessorBlock_DoesNotSwallowSiblingMethod()
    {
        var module = Parse("""
            module T
              class C public
                pp DEBUG
                func A public static () → int
                  !
                  return 7
            """);

        var cls = Assert.Single(module.Classes);
        Assert.Equal("A", Assert.Single(cls.Methods).Name);
    }

    [Fact]
    public void EmptyTypePreprocessorBlock_DoesNotSwallowSiblingClass()
    {
        var module = Parse("""
            module T
              pp DEBUG
              class C public
                func A public static () → int
                  !
                  return 7
            """);

        Assert.Equal("C", Assert.Single(module.Classes).Name);
    }

    [Fact]
    public void EmptyEnumExtension_DoesNotAdoptSiblingFunction()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §EN{Color}
                Red
              §EEXT{Color}
              §F{A:pub} (Color:self) -> i32
                §E{}
                §R 1
            """);
        // There is no native enum-extension declaration; retain this internal production's
        // ownership oracle without counting it as a native enum-extension acceptance case.
        var module = ParseInternal("""
            module T
              enum Color
                Red
              __extension__{Color}
              func A public (Color:self) → int
                !
                return 1
            """, ("__extension__", TokenKind.EnumExtension));

        Assert.Empty(Assert.Single(module.EnumExtensions).Methods);
        Assert.Equal("A", Assert.Single(module.Functions).Name);
    }

    [Fact]
    public void Documentation_DoesNotSwallowSiblingFunction()
    {
        var module = Parse("""
            module T
              /// First function.
              func A public () → int
                !
                return 1
              func B public () → int
                !
                return 2
            """);

        Assert.Empty(module.Decisions);
        Assert.Equal("First function.", module.Functions[0].DocComment);
        Assert.Null(module.Functions[1].DocComment);
        Assert.Equal(new[] { "A", "B" }, module.Functions.Select(f => f.Name));
    }

    [Fact]
    public void Documentation_DoesNotCreateContextMetadata()
    {
        var module = Parse("""
            /// Partial view; Secret.calr is not loaded.
            module T
              func A public () → int
                !
                return 1
            """);

        Assert.Null(module.Context);
        Assert.Equal("Partial view; Secret.calr is not loaded.", module.DocComment);
        Assert.Single(module.Functions);
    }

    [Fact]
    public void EmptyEnum_DoesNotSwallowSiblingEnum()
    {
        var module = Parse("""
            module T
              enum Color
              enum Size
                Small
                Large
            """);

        Assert.Equal(2, module.Enums.Count);
        Assert.Empty(module.Enums[0].Members);
        Assert.Equal(2, module.Enums[1].Members.Count);
    }

    // ------------------------------------------------------------------
    // Retired layouts: exact lexer rejection, internal parser recovery, and native counterparts
    // ------------------------------------------------------------------

    [Fact]
    public void FlatCloserForm_TryBodyAtOpenerColumn_StillOwnedUntilCloser()
    {
        AssertRetiredSourceRejected("""
            §M{Test}
              §F{SafeDivide:pub}
                §I{i32:a}
                §I{i32:b}
                §O{i32}
                §TR
                §R (/ a b)
                §CA{DivideByZeroException:ex}
                §R 0
                §/TR
                §R 5
            """);
        var body = Assert.Single(ParseInternal("""
            module Test
              func SafeDivide public (int:a, int:b) → int
                !
                try
                return a / b
                catch DivideByZeroException:ex
                return 0
                __end__
                return 5
            """, ("__end__", TokenKind.EndTry)).Functions).Body;

        Assert.Equal(2, body.Count);
        var tryStmt = Assert.IsType<TryStatementNode>(body[0]);
        Assert.Single(tryStmt.TryBody);
        Assert.Single(Assert.Single(tryStmt.CatchClauses).Body);
        var native = ParseSingleFunctionBody("""
            module Test
              func SafeDivide public (int:a, int:b) → int
                !
                try
                  return a / b
                catch DivideByZeroException:ex
                  return 0
                return 5
            """);
        Assert.Equal(2, native.Count);
        var nativeTry = Assert.IsType<TryStatementNode>(native[0]);
        Assert.Single(nativeTry.TryBody);
        Assert.Single(Assert.Single(nativeTry.CatchClauses).Body);
    }

    [Fact]
    public void LegacyFlatLayout_FunctionBodiesAtModuleColumn_StillParse()
    {
        AssertRetiredSourceRejected("§M{Test}\n§F{A:pub}\n§O{i32}\n§R 1\n§F{B:pub}\n§O{i32}\n§R 2\n");
        // Internal module-column ownership control, not recommended/native indented source.
        var module = ParseInternal(
            "module Test\nfunc A public () → int\n!\n__return__ 1\nfunc B public () → int\n!\n__return__ 2\n",
            ("__return__", TokenKind.Return));

        Assert.Equal(2, module.Functions.Count);
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[0].Body));
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[1].Body));
        var native = Parse("""
            module Test
              func A public () → int
                !
                return 1
              func B public () → int
                !
                return 2
            """);
        Assert.Equal(2, native.Functions.Count);
        Assert.IsType<ReturnStatementNode>(Assert.Single(native.Functions[0].Body));
        Assert.IsType<ReturnStatementNode>(Assert.Single(native.Functions[1].Body));
    }

    [Fact]
    public void FlatCloserForm_BodyStartingOnOpenerLine_ContinuesUntilCloser()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} () -> void
                §E{cw}
                §SYNC (this) §P "first"
                §P "second"
                §/SYNC
                §P "after"
            """);
        var body = Assert.Single(ParseInternal("""
            module T
              func A public () → void
                ! cw
                lock (this) __print__ "first"
                __print__ "second"
                __end__
                __print__ "after"
            """, ("__print__", TokenKind.Print), ("__end__", TokenKind.EndSyncBlock)).Functions).Body;

        Assert.Equal(2, body.Count);
        Assert.Equal(2, Assert.IsType<SyncBlockNode>(body[0]).Body.Count);
        var native = ParseSingleFunctionBody("""
            module T
              func A public () → void
                ! cw
                lock (this)
                  Console.WriteLine("first")
                  Console.WriteLine("second")
                Console.WriteLine("after")
            """);
        Assert.Equal(2, native.Count);
        Assert.Equal(2, Assert.IsType<SyncBlockNode>(native[0]).Body.Count);
    }

    [Fact]
    public void EmptyBlock_FollowedByNestedSameKindBlockWithIdlessCloser_IsNotFlat()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} () -> void
                §E{cw}
                §UNSAFE
                §UNSAFE
                  §P "inner"
                §/UNSAFE
                §P "after"
            """);
        var body = Assert.Single(ParseInternal("""
            module T
              func A public () → void
                ! cw
                unsafe
                unsafe
                  __print__ "inner"
                __end__
                __print__ "after"
            """, ("__print__", TokenKind.Print), ("__end__", TokenKind.EndUnsafe)).Functions).Body;

        Assert.Equal(3, body.Count);
        Assert.Empty(Assert.IsType<UnsafeBlockNode>(body[0]).Body);
        Assert.Single(Assert.IsType<UnsafeBlockNode>(body[1]).Body);
        Assert.IsType<PrintStatementNode>(body[2]);
        var native = ParseSingleFunctionBody("""
            module T
              func A public () → void
                ! cw
                unsafe
                unsafe
                  Console.WriteLine("inner")
                Console.WriteLine("after")
            """);
        Assert.Equal(3, native.Count);
        Assert.Empty(Assert.IsType<UnsafeBlockNode>(native[0]).Body);
        Assert.Single(Assert.IsType<UnsafeBlockNode>(native[1]).Body);
        Assert.IsType<ExpressionStatementNode>(native[2]);
    }

    [Fact]
    public void ExplicitCloserAfterEmptyIndentedCatch_KeepsCloserFormMeaning()
    {
        AssertRetiredSourceRejected("""
            §M{T}
              §F{A:pub} (i32:x) -> i32
                §E{throw}
                §B{~n:i32} 0
                §TR
                  §ASSIGN n (/ 10 x)
                §CA{DivideByZeroException:}
                §ASSIGN n -1
                §/TR
                §R n
            """);
        var body = Assert.Single(ParseInternal("""
            module T
              func A public (int:x) → int
                ! throw
                var int:n = 0
                try
                  set n 10 / x
                catch DivideByZeroException
                set n -1
                __end__
                return n
            """, ("__end__", TokenKind.EndTry)).Functions).Body;

        Assert.Equal(3, body.Count);
        var tryStmt = Assert.IsType<TryStatementNode>(body[1]);
        Assert.Single(Assert.Single(tryStmt.CatchClauses).Body);
        var native = ParseSingleFunctionBody("""
            module T
              func A public (int:x) → int
                ! throw
                var int:n = 0
                try
                  set n 10 / x
                catch DivideByZeroException
                  set n -1
                return n
            """);
        Assert.Equal(3, native.Count);
        Assert.Single(Assert.Single(Assert.IsType<TryStatementNode>(native[1]).CatchClauses).Body);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IReadOnlyList<StatementNode> ParseSingleFunctionBody(string source)
        => Assert.Single(Parse(source).Functions).Body;

    private static ModuleNode Parse(string source)
    {
        Assert.DoesNotContain("§", source);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join(Environment.NewLine, diagnostics.Errors));
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static void AssertRetiredClauseRejected(string name, string clause)
    {
        Assert.Equal(clause, Assert.Single(EmptyClauseCases(), row => (string)row[0] == name)[1]);
        AssertRetiredSourceRejected("module T\n"
            + "  func A public (bool:b, int:n, int[]:xs, Dictionary<string,int>:d, object:o) → int\n    !\n"
            + "    !\n    " + clause + "\n    return 7\n");
    }

    private static void AssertRetiredSourceRejected(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var markers = tokens.Where(token => token.Text.StartsWith('§')).ToArray();
        Assert.NotEmpty(markers);
        foreach (var token in markers)
        {
            Assert.Equal(TokenKind.Error, token.Kind);
            Assert.Equal(token.Text, source.Substring(token.Span.Start, token.Span.Length));
            Assert.Contains(diagnostics.Errors, diagnostic =>
                diagnostic.Code == DiagnosticCode.UnknownSectionMarker && diagnostic.Span.Equals(token.Span));
        }
        Assert.DoesNotContain(tokens, token => token.Kind is TokenKind.RawCSharpExpression or TokenKind.CSharpInterop);
    }

    private static List<Token> InternalTokens(string source, (string Marker, TokenKind Kind)[] replacements)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join("\n", diagnostics));
        foreach (var (marker, kind) in replacements)
        {
            Assert.Contains(tokens, token => token.Text == marker);
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                if (token.Text == marker)
                    tokens[index] = new Token(kind, token.Text, token.Span, token.Value)
                    {
                        IndentationDepth = token.IndentationDepth,
                        DocComment = token.DocComment,
                    };
            }
        }
        return tokens;
    }

    private static ModuleNode ParseInternal(string source, params (string Marker, TokenKind Kind)[] replacements)
    {
        var tokens = InternalTokens(source, replacements);
        if (replacements.Any(replacement => replacement.Kind == TokenKind.EndLambda))
        {
            var start = Assert.Single(tokens, token => token.Kind == TokenKind.Lambda).Span.Start;
            var end = Assert.Single(tokens, token => token.Kind == TokenKind.EndLambda).Span.Start;
            Assert.DoesNotContain(tokens, token => token.Span.Start > start && token.Span.Start < end
                && token.Kind is TokenKind.Indent or TokenKind.Dedent);
        }
        var diagnostics = new DiagnosticBag();
        var module = new Parser(tokens, diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, "Internal token/API control:\n" + source + "\n" + string.Join("\n", diagnostics));
        return module;
    }

    private static void AssertEmptyClause(string name, StatementNode statement)
    {
        IReadOnlyList<StatementNode> body = name switch
        {
            "if-then" => Assert.IsType<IfStatementNode>(statement).ThenBody,
            "elseif" => Assert.Single(Assert.IsType<IfStatementNode>(statement).ElseIfClauses).Body,
            "else" or "block-if-else" => Assert.IsType<IfStatementNode>(statement).ElseBody!,
            "for" => Assert.IsType<ForStatementNode>(statement).Body,
            "while" => Assert.IsType<WhileStatementNode>(statement).Body,
            "foreach" => Assert.IsType<ForeachStatementNode>(statement).Body,
            "eachkv" => Assert.IsType<DictionaryForeachNode>(statement).Body,
            "using" => Assert.IsType<UsingStatementNode>(statement).Body,
            "sync" => Assert.IsType<SyncBlockNode>(statement).Body,
            "unsafe" => Assert.IsType<UnsafeBlockNode>(statement).Body,
            "fixed" => Assert.IsType<FixedStatementNode>(statement).Body,
            "preprocessor" => Assert.IsType<PreprocessorDirectiveNode>(statement).Body,
            "try-empty-catch" => Assert.Single(Assert.IsType<TryStatementNode>(statement).CatchClauses).Body,
            "try-empty-finally" => Assert.IsType<TryStatementNode>(statement).FinallyBody!,
            "match-last-case" => Assert.IsType<MatchStatementNode>(statement).Cases.Last().Body,
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    private static object? Invoke(Type type, string name, params object?[] args) =>
        type.GetMethod(name)!.Invoke(null, args);

    private static Type Compile(string source, string typeName)
    {
        Parse(source);
        var result = Program.Compile(source, "empty-clause.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = false,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        return EmitAndLoad(result.GeneratedCode!, typeName);
    }

    private static Type EmitAndLoad(string generatedCode, string typeName)
    {
        var compilation = CSharpCompilation.Create("EmptyClause_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + generatedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success,
            string.Join(Environment.NewLine, emission.Diagnostics) + Environment.NewLine + generatedCode);
        var assembly = Assembly.Load(stream.ToArray());
        return assembly.GetType(typeName, throwOnError: false)
               ?? Assert.Single(assembly.GetTypes(), t => t.Name == typeName);
    }
}
