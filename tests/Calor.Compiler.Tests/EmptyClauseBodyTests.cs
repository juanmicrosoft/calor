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
/// </summary>
public class EmptyClauseBodyTests
{
    // The 0.25 R0 witness X-TRYCATCH-01, as the converter emitted it (CLI default).
    private const string XTryCatch01 = """
        §M{X-TRYCATCH-01:overflow=unchecked}
          §U{System}

          §NS{ns_global:_global:global}
            §M{Probe:pub}
              §FLD{i32:counter:priv:stat} = 0

              §MT{Work:pub:stat} (bool:fail)
                §E{throw,alloc}
                §IF fail
                  §TH §NEW{InvalidOperationException} §A "x" §/NEW

              §MT{Step:pub:stat} (bool:fail)
                §E{throw,alloc}
                §TR
                  §C{Work} fail
                §CA{InvalidOperationException:}
                §ASSIGN counter (+ counter 1)

              §MT{Run:pub:stat} () -> str
                §E{throw,alloc}
                §C{Step} false
                §C{Step} true
                §R (str counter)
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
            §M{T}
              §F{Step:pub} (i32:x) -> i32
                §E{throw}
                §B{~n:i32} 0
                §TR
                  §ASSIGN n (/ 10 x)
                §CA{DivideByZeroException:}
                §ASSIGN n (+ n 1)
                §R n
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
            §M{T}
              §F{Step:pub} (i32:x) -> i32
                §E{throw}
                §B{~n:i32} 0
                §TR
                  §ASSIGN n x
                §FI
                §ASSIGN n (+ n 1)
                §R n
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
            §M{T}
              §F{Step:pub} (i32:x) -> i32
                §E{throw}
                §B{~n:i32} 0
                §TR
                  §ASSIGN n (/ 10 x)
                §CA{DivideByZeroException:}
                §CA{Exception:e}
                  §ASSIGN n -1
                §ASSIGN n (+ n 100)
                §R n
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
            §M{T}
              §F{Step:pub} (i32:x) -> i32
                §E{throw}
                §B{~n:i32} 0
                §TR
                  §TR
                    §ASSIGN n (/ 10 x)
                  §CA{DivideByZeroException:}
                §CA{Exception:}
                  §ASSIGN n -1
                §R n
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
            §M{T}
              §F{Step:pub} (i32:x) -> i32
                §E{throw}
                §B{~n:i32} 0
                §TR
                  §TR
                    §ASSIGN n (/ 10 x)
                  §CA{DivideByZeroException:}
                  §ASSIGN n (+ n 1)
                §CA{Exception:}
                  §ASSIGN n -1
                §R n
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
            §M{T}
              §F{A:pub} (i32:x) -> void
                §E{throw}
                §TR
                  §B{y:i32} (/ 10 x)
                §CA{DivideByZeroException:}
              §F{B:pub} () -> i32
                §E{}
                §R 2
            """);

        Assert.Equal(2, module.Functions.Count);
        Assert.IsType<TryStatementNode>(Assert.Single(module.Functions[0].Body));
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[1].Body));
    }

    [Fact]
    public void EmptyClauses_ExecuteWithCorrectStructure()
    {
        var type = Compile("""
            §M{T}
              §F{Step:pub} (i32:x) -> i32
                §E{throw}
                §B{~n:i32} 0
                §TR
                  §ASSIGN n (/ 10 x)
                §CA{DivideByZeroException:}
                §ASSIGN n (+ n 1)
                §TR
                  §ASSIGN n (+ n 10)
                §FI
                §ASSIGN n (+ n 100)
                §R n
            """, "T.TModule");

        Assert.Equal(111, Invoke(type, "Step", 0));
        Assert.Equal(116, Invoke(type, "Step", 2));
    }

    [Fact]
    public void XTryCatch01_ReturnsTwo()
    {
        var module = Parse(XTryCatch01);
        var probe = module.Classes.Single(c => c.Name == "Probe");
        var step = probe.Methods.Single(m => m.Name == "Step");
        Assert.Equal(2, step.Body.Count);
        Assert.Empty(Assert.IsType<TryStatementNode>(step.Body[0]).CatchClauses[0].Body);

        var type = Compile(XTryCatch01, "Probe");
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

    [Theory]
    [MemberData(nameof(EmptyClauseCases))]
    public void EmptyClause_DoesNotSwallowSameColumnSibling(string name, string clause)
    {
        _ = name;
        var source = "§M{T}\n"
            + "  §F{A:pub} (bool:b, i32:n, [i32]:xs, Dict<str, i32>:d, object:o) -> i32\n"
            + "    §E{}\n"
            + "    " + clause + "\n"
            + "    §R 7\n"
            + "  §F{B:pub} () -> i32\n"
            + "    §E{}\n"
            + "    §R 8\n";
        var module = Parse(source);

        Assert.Equal(2, module.Functions.Count);
        var body = module.Functions[0].Body;
        Assert.Equal(2, body.Count);
        Assert.IsType<ReturnStatementNode>(body[1]);
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[1].Body));
    }

    [Theory]
    [MemberData(nameof(EmptyClauseCases))]
    public void EmptyClause_AtEndOfEnclosingBlock_DoesNotStealItsDedent(string name, string clause)
    {
        _ = name;
        // The empty clause is the last statement of a loop body; the
        // statement after it is at the loop's column, so it follows the loop.
        var source = "§M{T}\n"
            + "  §F{A:pub} (bool:b, i32:n, [i32]:xs, Dict<str, i32>:d, object:o) -> i32\n"
            + "    §E{}\n"
            + "    §L{j:0:1:1}\n"
            + "      " + clause.Replace("\n    ", "\n      ") + "\n"
            + "    §R 7\n";
        var module = Parse(source);

        var body = Assert.Single(module.Functions).Body;
        Assert.Equal(2, body.Count);
        var loop = Assert.IsType<ForStatementNode>(body[0]);
        Assert.Single(loop.Body);
        Assert.IsType<ReturnStatementNode>(body[1]);
    }

    [Fact]
    public void EmptyThen_InsideOuterIf_DoesNotStealOuterElse()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} (bool:a, bool:b) -> i32
                §E{}
                §IF a
                  §IF b
                §EL
                  §R 2
                §R 1
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
            §M{T}
              §F{A:pub} (i32:x) -> i32
                §E{}
                §B{~n:i32} 0
                §IF (> x 100)
                §ASSIGN n (+ n 1)
                §L{i:0:3:1}
                §ASSIGN n (+ n 10)
                §WH false
                §ASSIGN n (+ n 100)
                §IF (> x 0)
                  §ASSIGN n (+ n 1000)
                §EL
                §ASSIGN n (+ n 10000)
                §W x
                  §K 1
                    §ASSIGN n (+ n 100000)
                  §K _
                §ASSIGN n (+ n 1000000)
                §R n
            """, "T.TModule");

        Assert.Equal(1111111, Invoke(type, "A", 1));
        Assert.Equal(1010111, Invoke(type, "A", 0));
    }

    [Fact]
    public void EmptyPreprocessorBlock_DoesNotSwallowSameColumnSibling()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} () -> i32
                §E{}
                §PP{DEBUG}
                §R 7
            """);

        Assert.Equal(2, body.Count);
        Assert.Empty(Assert.IsType<PreprocessorDirectiveNode>(body[0]).Body);
        Assert.IsType<ReturnStatementNode>(body[1]);
    }

    [Fact]
    public void EmptyPreprocessorBlock_InsideIf_DoesNotStealOuterElse()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} (bool:b) -> i32
                §E{}
                §IF b
                  §PP{DEBUG}
                §EL
                  §R 2
                §R 1
            """);

        Assert.Equal(2, body.Count);
        var outer = Assert.IsType<IfStatementNode>(body[0]);
        Assert.IsType<PreprocessorDirectiveNode>(Assert.Single(outer.ThenBody));
        Assert.Single(outer.ElseBody!);
    }

    [Fact]
    public void BlockLambda_WithoutCloser_DoesNotSwallowFollowingStatements()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} () -> i32
                §E{}
                §B{a:Action} §LAM{}
                §R 7
            """);

        Assert.Equal(2, body.Count);
        Assert.IsType<BindStatementNode>(body[0]);
        Assert.IsType<ReturnStatementNode>(body[1]);
    }

    [Fact]
    public void EmptyGetter_DoesNotStealPropertyDedent()
    {
        var module = Parse("""
            §M{T}
              §CL{C:pub}
                §PROP{P:i32:pub}
                  §GET
                  §SET
                §MT{B:pub} () -> i32
                  §E{}
                  §R 7
              §CL{D:pub}
            """);

        Assert.Equal(2, module.Classes.Count);
        Assert.Single(module.Classes[0].Properties);
        Assert.Single(module.Classes[0].Methods);
    }

    [Fact]
    public void MultilineHeader_BodyOnHeaderLastLine_StaysInBody()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} (bool:b) -> void
                §E{cw}
                §IF (==
                  1 2
                ) §P "then"
                §P "after"
            """);

        Assert.Equal(2, body.Count);
        Assert.IsType<PrintStatementNode>(Assert.Single(Assert.IsType<IfStatementNode>(body[0]).ThenBody));
        Assert.IsType<PrintStatementNode>(body[1]);
    }

    [Fact]
    public void MultilineFunctionHeader_BodyOnHeaderLastLine_StaysInBody()
    {
        var module = Parse("§M{T}\n  §F{A:pub} (\n    bool:x\n  ) -> i32 §R 1\n");

        Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
    }

    [Fact]
    public void BracedLambda_OuterElseAfterEmptyInnerIf_BelongsToOuterIf()
    {
        // Inside braces the lexer emits no Dedent; the §EL column decides.
        var type = Compile("""
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
            """, "T.TModule");

        Assert.Equal(1, Invoke(type, "A"));
    }

    [Fact]
    public void BracedLambda_OuterCaseAfterInnerMatch_BelongsToOuterMatch()
    {
        var type = Compile("""
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
            """, "T.TModule");

        Assert.Equal(7, Invoke(type, "A", 3));
        Assert.Equal(0, Invoke(type, "A", 1));
    }

    [Fact]
    public void FlatCloserScan_IgnoresBracketContinuationLines()
    {
        var body = ParseSingleFunctionBody("§M{T}\n  §F{A:pub} () -> void\n    §E{cw}\n"
            + "    §UNSAFE\n    §P \"first\"\n    §B{x:i32} (+\n1 2\n    )\n    §/UNSAFE\n    §P \"after\"\n");

        Assert.Equal(2, body.Count);
        Assert.Equal(2, Assert.IsType<UnsafeBlockNode>(body[0]).Body.Count);
    }

    [Fact]
    public void BlockLambda_WithoutCloser_DoesNotTakeNextLineAsExpressionBody()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} () -> i32
                §E{}
                §B{~n:i32} 0
                §B{a:Func<i32>} §LAM{}
                (inc n)
                §R n
            """);

        Assert.Equal(4, body.Count);
        Assert.IsType<ExpressionStatementNode>(body[2]);
        Assert.IsType<ReturnStatementNode>(body[3]);
    }

    [Fact]
    public void FlatCloserScan_TracksNestedOpenerOnHeaderLine()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} (object:a, object:b) -> void
                §E{cw}
                §SYNC (a) §SYNC (b) §P "inside"
                §/SYNC
                §P "after"
            """);

        Assert.Equal(2, body.Count);
        var outer = Assert.IsType<SyncBlockNode>(body[0]);
        Assert.IsType<SyncBlockNode>(Assert.Single(outer.Body));
        Assert.IsType<PrintStatementNode>(body[1]);
    }

    [Fact]
    public void ManyEmptyBlocksWithoutClosers_ParseWithoutErrors()
    {
        var lines = string.Join("\n", Enumerable.Range(0, 3000).Select(i => $"    §UNSAFE{{u{i}}}"));
        var module = Parse("§M{T}\n  §F{A:pub} () -> void\n    §E{}\n" + lines + "\n");

        Assert.Equal(3000, Assert.Single(module.Functions).Body.Count);
    }

    // ------------------------------------------------------------------
    // Type- and function-level bodies
    // ------------------------------------------------------------------

    [Fact]
    public void EmptyClass_DoesNotAdoptSameColumnSiblingClass()
    {
        var module = Parse("""
            §M{T}
              §CL{K:pub}
              §CL{L:pub}
                §MT{B:pub} () -> i32
                  §E{}
                  §R 2
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
            §M{T}
              §CL{Outer:pub}
                §CL{Inner:pub}
                §MT{B:pub} () -> i32
                  §E{}
                  §R 2
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
            §M{T}
              §F{A:pub} () -> void
              §F{B:pub} () -> i32
                §E{}
                §R 2
            """);

        Assert.Equal(2, module.Functions.Count);
        Assert.Empty(module.Functions[0].Body);
        Assert.Single(module.Functions[1].Body);
    }

    [Fact]
    public void EmptyMemberPreprocessorBlock_DoesNotSwallowSiblingMethod()
    {
        var module = Parse("""
            §M{T}
              §CL{C:pub}
                §PP{DEBUG}
                §MT{A:pub:stat} () -> i32
                  §E{}
                  §R 7
            """);

        var cls = Assert.Single(module.Classes);
        Assert.Equal("A", Assert.Single(cls.Methods).Name);
    }

    [Fact]
    public void EmptyTypePreprocessorBlock_DoesNotSwallowSiblingClass()
    {
        var module = Parse("""
            §M{T}
              §PP{DEBUG}
              §CL{C:pub}
                §MT{A:pub:stat} () -> i32
                  §E{}
                  §R 7
            """);

        Assert.Equal("C", Assert.Single(module.Classes).Name);
    }

    [Fact]
    public void EmptyEnumExtension_DoesNotAdoptSiblingFunction()
    {
        var module = Parse("""
            §M{T}
              §EN{e:Color}
                Red
              §EEXT{x:Color}
              §F{A:pub} (Color:self) -> i32
                §E{}
                §R 1
            """);

        Assert.Empty(Assert.Single(module.EnumExtensions).Methods);
        Assert.Equal("A", Assert.Single(module.Functions).Name);
    }

    [Fact]
    public void Documentation_DoesNotSwallowSiblingFunction()
    {
        var module = Parse("""
            §M{T}
              /// First function.
              §F{A:pub} () -> i32
                §E{}
                §R 1
              §F{B:pub} () -> i32
                §E{}
                §R 2
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
            §M{T}
              §F{A:pub} () -> i32
                §E{}
                §R 1
            """);

        Assert.Null(module.Context);
        Assert.Equal("Partial view; Secret.calr is not loaded.", module.DocComment);
        Assert.Single(module.Functions);
    }

    [Fact]
    public void EmptyEnum_DoesNotSwallowSiblingEnum()
    {
        var module = Parse("""
            §M{T}
              §EN{Color}
              §EN{Size}
                Small
                Large
            """);

        Assert.Equal(2, module.Enums.Count);
        Assert.Empty(module.Enums[0].Members);
        Assert.Equal(2, module.Enums[1].Members.Count);
    }

    // ------------------------------------------------------------------
    // Flat closer form keeps its meaning
    // ------------------------------------------------------------------

    [Fact]
    public void FlatCloserForm_TryBodyAtOpenerColumn_StillOwnedUntilCloser()
    {
        var body = ParseSingleFunctionBody("""
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

        Assert.Equal(2, body.Count);
        var tryStmt = Assert.IsType<TryStatementNode>(body[0]);
        Assert.Single(tryStmt.TryBody);
        Assert.Single(Assert.Single(tryStmt.CatchClauses).Body);
    }

    [Fact]
    public void LegacyFlatLayout_FunctionBodiesAtModuleColumn_StillParse()
    {
        // Every line at column 1 and no closers: the function body sits at the
        // §F's own column. It ends at the next sibling declaration.
        var module = Parse("§M{Test}\n§F{A:pub}\n§O{i32}\n§R 1\n§F{B:pub}\n§O{i32}\n§R 2\n");

        Assert.Equal(2, module.Functions.Count);
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[0].Body));
        Assert.IsType<ReturnStatementNode>(Assert.Single(module.Functions[1].Body));
    }

    [Fact]
    public void FlatCloserForm_BodyStartingOnOpenerLine_ContinuesUntilCloser()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} () -> void
                §E{cw}
                §SYNC (this) §P "first"
                §P "second"
                §/SYNC
                §P "after"
            """);

        Assert.Equal(2, body.Count);
        Assert.Equal(2, Assert.IsType<SyncBlockNode>(body[0]).Body.Count);
    }

    [Fact]
    public void EmptyBlock_FollowedByNestedSameKindBlockWithIdlessCloser_IsNotFlat()
    {
        var body = ParseSingleFunctionBody("""
            §M{T}
              §F{A:pub} () -> void
                §E{cw}
                §UNSAFE
                §UNSAFE
                  §P "inner"
                §/UNSAFE
                §P "after"
            """);

        Assert.Equal(3, body.Count);
        Assert.Empty(Assert.IsType<UnsafeBlockNode>(body[0]).Body);
        Assert.Single(Assert.IsType<UnsafeBlockNode>(body[1]).Body);
        Assert.IsType<PrintStatementNode>(body[2]);
    }

    [Fact]
    public void ExplicitCloserAfterEmptyIndentedCatch_KeepsCloserFormMeaning()
    {
        var body = ParseSingleFunctionBody("""
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

        Assert.Equal(3, body.Count);
        var tryStmt = Assert.IsType<TryStatementNode>(body[1]);
        Assert.Single(Assert.Single(tryStmt.CatchClauses).Body);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IReadOnlyList<StatementNode> ParseSingleFunctionBody(string source)
        => Assert.Single(Parse(source).Functions).Body;

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return module;
    }

    private static object? Invoke(Type type, string name, params object?[] args) =>
        type.GetMethod(name)!.Invoke(null, args);

    private static Type Compile(string source, string typeName)
    {
        var result = Program.Compile(source, "empty-clause.calr", new CompilationOptions
        {
            EnableTypeChecking = true,
            EnforceEffects = false,
            StatusWriter = TextWriter.Null
        });
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics.Errors));
        var compilation = CSharpCompilation.Create("EmptyClause_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
            GeneratedCSharpCompiler.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Assert.True(emission.Success,
            string.Join(Environment.NewLine, emission.Diagnostics) + Environment.NewLine + result.GeneratedCode);
        var assembly = Assembly.Load(stream.ToArray());
        return assembly.GetType(typeName, throwOnError: false)
               ?? Assert.Single(assembly.GetTypes(), t => t.Name == typeName);
    }
}
