using Calor.Compiler;
using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// Behavioral tests for <see cref="ReturnValidationPass"/> (Calor0205 —
/// value returned from a no-value owner). Positives assert the pass fires on a
/// value <c>§R expr</c> in a void/async-void/iterator/constructor/setter/event
/// owner (including when nested inside control flow); negatives assert it stays
/// silent on value-returning owners, bare <c>§R</c>, and expressions that might
/// legitimately be void-typed (calls) or valid void statement-expressions.
/// </summary>
public class ReturnValidationPassTests
{
    private static IReadOnlyList<Diagnostic> Run(string source)
    {
        var parseDiagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, parseDiagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, parseDiagnostics);
        var module = parser.Parse();

        Assert.False(
            parseDiagnostics.HasErrors,
            "Test source failed to parse:\n  " +
            string.Join("\n  ", parseDiagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var passDiagnostics = new DiagnosticBag();
        new ReturnValidationPass(passDiagnostics).Check(module);
        return passDiagnostics
            .Where(d => d.Code == DiagnosticCode.ReturnValueInVoidOwner)
            .ToList();
    }

    private static void AssertFires(string source) => Assert.Single(Run(source));

    private static void AssertSilent(string source) => Assert.Empty(Run(source));

    // ---------------------------------------------------------------- Positives

    [Fact]
    public void VoidFunction_InlineHeader_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub} () -> void
    §R INT:0
");
    }

    [Fact]
    public void VoidFunction_OmittedReturnType_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub}
    §R INT:0
");
    }

    [Fact]
    public void VoidFunction_OutputVoidMarker_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub}
    §O{void}
    §R INT:0
");
    }

    [Fact]
    public void AsyncVoidFunction_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §AF{DoAsync:pub}
    §R INT:0
");
    }

    [Fact]
    public void Iterator_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Nums:pub}
    §O{i32}
    §YIELD 42
    §R INT:0
");
    }

    [Fact]
    public void VoidMethod_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §CL{C:pub}
    §MT{Do:pub} () -> void
      §R INT:0
");
    }

    [Fact]
    public void Constructor_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §CL{C:pub}
    §CTOR{pub} ()
      §R INT:0
");
    }

    [Fact]
    public void Setter_ReturnsValue_Fires_ButGetterDoesNot()
    {
        AssertFires(@"
§M{T}
  §CL{C:pub}
    §PROP{Name:str:pub}
      §GET
        §R ""x""
      §/GET
      §SET
        §R INT:0
      §/SET
    §/PROP
");
    }

    [Fact]
    public void EventAddAccessor_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §CL{C:pub}
    §EVT{Click:pub:EventHandler}
    §EADD
    §R INT:0
    §/EADD
    §/EVT
");
    }

    [Fact]
    public void EnumExtensionVoidMethod_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §EEXT{Color}
    §F{Describe:pub}
      §I{Color:self}
      §R INT:0
  §/EEXT
");
    }

    [Fact]
    public void NestedInIf_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub} () -> void
    §IF BOOL:true
      §R INT:0
");
    }

    [Fact]
    public void NestedInForLoop_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub} () -> void
    §L{i:1:10:1}
      §R INT:0
");
    }

    [Fact]
    public void NestedInWhile_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub} () -> void
    §WH BOOL:true
      §R INT:0
");
    }

    [Fact]
    public void NestedInTryBody_ReturnsValue_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub} () -> void
    §TR
    §R INT:0
    §CA{Exception:e}
    §P ""x""
    §/TR
");
    }

    [Theory]
    [InlineData("INT:0")]
    [InlineData("BOOL:true")]
    [InlineData("STR:\"hello\"")]
    [InlineData("(* INT:2 INT:3)")]
    [InlineData("(> INT:1 INT:0)")]
    public void VoidFunction_DefiniteValueForms_Fire(string valueExpr)
    {
        AssertFires($@"
§M{{T}}
  §F{{Do:pub}} () -> void
    §R {valueExpr}
");
    }

    [Fact]
    public void VoidFunction_ReturnsReference_Fires()
    {
        AssertFires(@"
§M{T}
  §F{Do:pub}
    §I{i32:x}
    §O{void}
    §R x
");
    }

    // ---------------------------------------------------------------- Negatives

    [Fact]
    public void NonVoidFunction_ReturnsValue_Silent()
    {
        AssertSilent(@"
§M{T}
  §F{Do:pub} () -> i32
    §R INT:0
");
    }

    [Fact]
    public void VoidFunction_BareReturn_Silent()
    {
        AssertSilent(@"
§M{T}
  §F{Do:pub} () -> void
    §R
");
    }

    [Fact]
    public void AsyncFunctionWithReturnType_ReturnsValue_Silent()
    {
        AssertSilent(@"
§M{T}
  §AF{GetAsync:pub}
    §O{str}
    §R ""data""
");
    }

    [Fact]
    public void VoidFunction_ReturnsCall_Silent()
    {
        // A call can be void-typed (a migrated `void F() => VoidCall();` lowers to
        // `§R <call>`), so the pass conservatively does not flag it.
        AssertSilent(@"
§M{T}
  §F{Do:pub} () -> void
    §R §C{Helper.Run} INT:1
");
    }

    [Fact]
    public void Getter_ReturnsValue_Silent()
    {
        AssertSilent(@"
§M{T}
  §CL{C:pub}
    §PROP{Name:str:pub}
      §GET
        §R ""x""
      §/GET
    §/PROP
");
    }

    [Fact]
    public void AbstractMethod_NoBody_Silent()
    {
        AssertSilent(@"
§M{T}
  §CL{C:int:abs}
    §MT{Do:pub:abs} () -> void
");
    }

    [Fact]
    public void VoidMethodWithModifier_BareReturn_Silent()
    {
        AssertSilent(@"
§M{T}
  §CL{C:pub}
    §MT{Do:pub:stat} () -> void
      §R
");
    }

    // ------------------------------------------------------- End-to-end (always-on)

    [Fact]
    public void Compile_VoidFunctionReturningValue_EmitsCalor0205_WithoutTypeChecking()
    {
        var source = @"
§M{T}
  §F{Do:pub} () -> void
    §R INT:0
";
        var result = Program.Compile(source, filePath: null, new CompilationOptions { EnableTypeChecking = false });

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ReturnValueInVoidOwner);
    }

    [Fact]
    public void Compile_CleanVoidFunction_NoCalor0205()
    {
        var source = @"
§M{T}
  §F{Do:pub} () -> void
    §R
";
        var result = Program.Compile(source, filePath: null, new CompilationOptions { EnableTypeChecking = false });

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ReturnValueInVoidOwner);
    }
}
