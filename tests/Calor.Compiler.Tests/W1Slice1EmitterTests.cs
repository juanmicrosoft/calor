using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

public class W1Slice1EmitterTests
{
    private static (string Code, DiagnosticBag Diagnostics) EmitWithDiagnostics(string source)
    {
        var diagnostics = new DiagnosticBag();
        diagnostics.SetFilePath("test.calr");

        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter(ContractMode.Debug, null, null, null, diagnostics);
        var code = emitter.Emit(module);
        return (code, diagnostics);
    }

    [Fact]
    public void NestedReturn_LowersToSharedExit()
    {
        var source = """
            §M{m001:T}
              §F{f001:Pick:pub} (i32:x) -> i32
                §S (>= result 0)
                §IF{if1} (> x 10)
                  §R x
                §R 0
            """;

        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.Contains("goto __calorPostconditionExit", code);
        Assert.Contains("ContractKind.Ensures", code);
    }

    [Fact]
    public void EarlyTopLevelReturn_LowersWithoutFallthrough()
    {
        var source = """
            §M{m001:T}
              §F{f001:First:pub} (i32:x) -> i32
                §S (>= result 0)
                §R x
                §R 0
            """;

        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.Equal(2, CountOccurrences(code, "goto __calorPostconditionExit"));
    }

    [Fact]
    public void SingleFinalReturn_UsesStructuralResultBinding()
    {
        var source = """
            §M{m001:T}
              §F{f001:Id:pub} (i32:x) -> i32
                §S (== result x)
                §R x
            """;

        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.Contains("__calorPostconditionResult", code);
        Assert.Contains("return __calorPostconditionResult", code);
    }

    [Fact]
    public void OperatorOverload_NestedReturn_UsesSharedLowering()
    {
        var source = """
            §M{m001:T}
              §CL{c1:MyType}
                §OP{op001:+:pub}
                  §I{MyType:left}
                  §I{MyType:right}
                  §O{MyType}
                  §S (!= result null)
                  §IF{if1} (!= left null)
                    §R left
                  §R right
                §/OP{op001}
            """;

        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.Contains("goto __calorPostconditionExit", code);
        Assert.Contains("ContractKind.Ensures", code);
    }

    [Fact]
    public void OperatorOverload_FinalReturn_BindsExactResultReference()
    {
        var source = """
            §M{m001:T}
              §CL{c1:MyType}
                §OP{op001:+:pub}
                  §I{MyType:left}
                  §I{MyType:right}
                  §O{MyType}
                  §S (!= result null)
                  §R left
                §/OP{op001}
            """;

        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.DoesNotContain("!(result != null)", code);
        Assert.Contains("__calorPostconditionResult", code);
    }

    [Fact]
    public void ResultSubstring_IdentifierAndStringAreNotCorrupted()
    {
        var source = """
            §M{m001:T}
              §F{f001:Echo:pub} (i32:resultCode, str:myresult) -> i32
                §S{"result text"} (== result resultCode)
                §R resultCode
            """;

        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.DoesNotContain("__calorPostconditionResultCode", code);
        Assert.Contains("resultCode", code);
        Assert.Contains("myresult", code);
        Assert.Contains("\"result text\"", code);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }
        return count;
    }
}
