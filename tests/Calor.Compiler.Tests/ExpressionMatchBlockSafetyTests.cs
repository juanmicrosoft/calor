using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Parsing;
using Calor.Enforcement.Tests;
using Xunit;

namespace Calor.Compiler.Tests;

public class ExpressionMatchBlockSafetyTests
{
    [Theory]
    [InlineData("System.Console.WriteLine(\"must execute\")\nreturn 7")]
    [InlineData("trace.Add(1)\nreturn 7")]
    [InlineData("let int:answer = 7\nreturn answer")]
    [InlineData("if x > 0\n  return 7\nelse\n  return 8")]
    [InlineData("System.Console.WriteLine(\"must not become default\")")]
    public void UnsupportedExpressionArms_AreRejectedWithoutDiscardingStatements(string body)
    {
        var source = $$"""
            module MatchSafety
              func Probe public (int:x, List<int>:trace) → int
                ! cw,mut
                return match x
                  case 1
            {{Indent(body, 8)}}
                  case _ → 0
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
            module MatchSafety
              func Factory public () → Func<int> ! unknown
                !
                return () =>
                  return match 1
                    case 1
                      System.Console.WriteLine("must execute")
                      return 7
                    case _ → 0
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
            module MatchSafety
              func Probe public (int:x) → int
                !
                return match x
                  case 1
                    return 7
                  case _ → 0
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
            module MatchSafety
              func Probe public (int:x, List<int>:trace) → int
                ! cw,mut
                match x
                  case 1
                    System.Console.WriteLine("must execute")
                    trace.Add(x)
                    let int:answer = 7
                    if x > 0
                      return answer
                    else
                      return 8
                  case _
                    return 0
                return -1
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
