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
            module T T block
              func Pick (int:x) → int
                !
                => (result >= 0)
                if (x > 10)
                  return x
                return 0
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
            module T T block
              func First (int:x) → int
                !
                => (result >= 0)
                return x
                return 0
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
            module T T block
              func Id (int:x) → int
                !
                => (result == x)
                return x
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
            module T T block
              class MyType
                op + (MyType:left, MyType:right) → MyType
                  !
                  => (result != null)
                  if (left != null)
                    return left
                  return right
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
            module T T block
              class MyType
                op + (MyType:left, MyType:right) → MyType
                  !
                  => (result != null)
                  return left
            """;

        var (code, diagnostics) = EmitWithDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Code == DiagnosticCode.PostconditionCheckNotLowered);
        Assert.DoesNotContain("!(result != null)", code);
        Assert.Contains("__calorPostconditionResult", code);
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
