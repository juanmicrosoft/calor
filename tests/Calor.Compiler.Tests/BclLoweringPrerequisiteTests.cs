using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Migration;
using Calor.Compiler.Verification;
using Calor.Enforcement.Tests;
using Xunit;

namespace Calor.Compiler.Tests;

public class BclLoweringPrerequisiteTests
{
    [Theory]
    [InlineData("string", "sbText", "\"hello\"", 5)]
    [InlineData("int[]", "sbArray", "new int[3]", 3)]
    [InlineData("StringBuilder", "builder", "new StringBuilder(\"abcd\")", 4)]
    public void Length_UsesReceiverTypesWithoutNameHeuristics(
        string type, string name, string initializer, int expected)
    {
        var converted = new CSharpToCalorConverter().Convert($$"""
            using System.Text;
            public static class Sample
            {
                public static int Run()
                {
                    {{type}} {{name}} = {{initializer}};
                    return {{name}}.Length;
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.NotNull(converted.Ast);
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.DoesNotContain("sb-length", converted.CalorSource);
        var method = Assert.Single(Assert.Single(converted.Ast.Classes).Methods);
        var returned = Assert.IsType<ReturnStatementNode>(method.Body[^1]).Expression;
        if (type == "string")
            Assert.Equal(StringOp.Length, Assert.IsType<StringOperationNode>(returned).Operation);
        else
        {
            Assert.DoesNotContain("(len ", converted.CalorSource);
            Assert.IsType<FieldAccessNode>(returned);
        }
        var emitted = new CSharpEmitter().Emit(converted.Ast);
        Assert.True(GeneratedCSharpCompiler.Validate(emitted).CompilationSuccess);
        var compiled = Program.Compile(converted.CalorSource!, "bcl-length.calr");
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run");
        Assert.Null(runtime.Exception);
        Assert.Equal(expected, runtime.ReturnValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BclContracts_CompileAndExecuteWithAndWithoutVerification(bool verify)
    {
        var source = """
            §M{m1:BclContracts}
              §F{f1:Run:pub} (str:s) -> bool
                §E{}
                §Q §C{s.Contains} §A "b" §/C
                §S (== result §C{s.StartsWith} §A "a" §A System.StringComparison.Ordinal §/C)
                §R §C{s.StartsWith} §A "a" §A System.StringComparison.Ordinal §/C
            """;
        var options = new CompilationOptions
        {
            VerifyContracts = verify,
            StatusWriter = TextWriter.Null,
            ContractMode = ContractMode.Debug
        };
        var compiled = Program.Compile(source, "bcl-contract.calr", options);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics.Select(diagnostic => diagnostic.Message)));
        if (verify)
        {
            Assert.NotNull(options.VerificationResults);
            var function = Assert.Single(options.VerificationResults.Functions);
            Assert.Equal(ProofStatus.Proven, Assert.Single(function.PreconditionResults).EffectiveOutcome.Status);
            Assert.Equal(ProofStatus.Assumed, Assert.Single(function.PostconditionResults).EffectiveOutcome.Status);
        }
        var runtime = TestHarness.Execute(source, "Run", ["abc"], options);
        Assert.Null(runtime.Exception);
        Assert.Equal(true, runtime.ReturnValue);
    }

    [Fact]
    public void SbNamedUserReceiver_IsNotAStringBuilderOperation()
    {
        var converted = new CSharpToCalorConverter().Convert("""
            public sealed class Pretender
            {
                public int Append(int value) => value + 1;
            }
            public static class Sample
            {
                public static int Run()
                {
                    var sb = new Pretender();
                    return sb.Append(5);
                }
            }
            """);
        Assert.True(converted.Success, string.Join("\n", converted.Issues.Select(issue => issue.Message)));
        Assert.Equal(0, converted.InteropPreservationCount);
        Assert.DoesNotContain("sb-append", converted.CalorSource);
        Assert.Contains("§C{sb.Append}", converted.CalorSource);
        var runtime = TestHarness.Execute(converted.CalorSource!, "Run",
            options: new CompilationOptions { EnforceEffects = false });
        Assert.Null(runtime.Exception);
        Assert.Equal(6, runtime.ReturnValue);
    }
}
