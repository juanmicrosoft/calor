using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Parsing;
using Calor.Enforcement.Tests;
using Xunit;

namespace Calor.Compiler.Tests;

public class ExpressionMatchBlockSafetyTests
{
    [Theory]
    [InlineData("§P \"must execute\"\n§R INT:7")]
    [InlineData("§C{trace.Add} §A INT:1 §/C\n§R INT:7")]
    [InlineData("§B{answer:i32} INT:7\n§R answer")]
    [InlineData("§IF{if1} (> x INT:0)\n  §R INT:7\n§EL\n  §R INT:8")]
    [InlineData("§P \"must not become default\"")]
    public void UnsupportedExpressionArms_AreRejectedWithoutDiscardingStatements(string body)
    {
        var source = $$"""
            §M{m1:MatchSafety}
              §F{f1:Probe:pub} (i32:x, List<i32>:trace) -> i32
                §E{cw,mut}
                §R §W{w1} x
                  §K 1
            {{Indent(body, 8)}}
                  §K _ → INT:0
            """;
        foreach (var mode in new[] { ContractMode.Off, ContractMode.Debug })
        {
            var result = Program.Compile(source, "match-block.calr", Options(mode));
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor1006");
            Assert.Empty(result.GeneratedCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LambdaNestedArms_AreRejectedEvenWithoutOptionalTypeChecking(bool typeChecking)
    {
        const string source = """
            §M{m1:MatchSafety}
              §F{f1:Factory:pub} () -> Func<i32>
                §R §LAM{l1}
                  §R §W{w1} INT:1
                    §K 1
                      §P "must execute"
                      §R INT:7
                    §K _ → INT:0
                §/LAM{l1}
            """;
        var result = Program.Compile(source, "lambda-match.calr", new CompilationOptions
        {
            EnableTypeChecking = typeChecking,
            EnforceEffects = true,
            StatusWriter = TextWriter.Null
        });
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics.Errors, diagnostic => diagnostic.Code == "Calor1006");
        Assert.Empty(result.GeneratedCode);
    }

    [Fact]
    public void EmptyAndValuelessArms_NeverSilentlyBecomeDefault()
    {
        foreach (var body in new StatementNode[][] { [], [new ReturnStatementNode(TextSpan.Empty, null)] })
        {
            var match = new MatchExpressionNode(TextSpan.Empty, "w1",
                new IntLiteralNode(TextSpan.Empty, 1),
                [new MatchCaseNode(TextSpan.Empty, new WildcardPatternNode(TextSpan.Empty), null, body)],
                new AttributeCollection());
            var emitter = new CSharpEmitter();
            var emitted = match.Accept(emitter);
            Assert.True(emitter.EmissionDiagnostics.HasErrors);
            Assert.Contains(emitter.EmissionDiagnostics.Errors, diagnostic => diagnostic.Code == "Calor1006");
            Assert.DoesNotContain("=> default", emitted);
        }
    }

    [Fact]
    public void SingleReturnExpressionArms_ExecuteBothSourceForms()
    {
        const string source = """
            §M{m1:MatchSafety}
              §F{f1:Probe:pub} (i32:x) -> i32
                §E{}
                §R §W{w1} x
                  §K 1
                    §R INT:7
                  §K _ → INT:0
            """;
        foreach (var (input, expected) in new[] { (1, 7), (2, 0) })
        {
            var result = TestHarness.Execute(source, "Probe", [input], Options(ContractMode.Debug));
            Assert.Null(result.Exception);
            Assert.Equal(expected, result.ReturnValue);
        }
    }

    [Fact]
    public void StatementMatchBlocks_KeepPrintMutationBindingsAndNestedReturns()
    {
        const string source = """
            §M{m1:MatchSafety}
              §F{f1:Probe:pub} (i32:x, List<i32>:trace) -> i32
                §E{cw,mut}
                §W{w1} x
                  §K 1
                    §P "must execute"
                    §C{trace.Add} §A x §/C
                    §B{answer:i32} INT:7
                    §IF{if1} (> x INT:0)
                      §R answer
                    §EL
                      §R INT:8
                  §K _
                    §R INT:0
                §R INT:-1
            """;
        foreach (var (input, expected) in new[] { (1, 7), (2, 0) })
        {
            var trace = new List<int>();
            using var output = new StringWriter();
            var original = Console.Out;
            try
            {
                Console.SetOut(output);
                var result = TestHarness.Execute(source, "Probe", [input, trace], Options(ContractMode.Debug));
                Assert.Null(result.Exception);
                Assert.Equal(expected, result.ReturnValue);
            }
            finally
            {
                Console.SetOut(original);
            }
            Assert.Equal(input == 1 ? "must execute" + Environment.NewLine : "", output.ToString());
            Assert.Equal(input == 1 ? new[] { 1 } : [], trace);
        }
    }

    private static CompilationOptions Options(ContractMode mode) => new()
    {
        EnableTypeChecking = true,
        EnforceEffects = true,
        ContractMode = mode,
        StatusWriter = TextWriter.Null
    };

    private static string Indent(string source, int width) =>
        string.Join("\n", source.Split('\n').Select(line => new string(' ', width) + line));
}
