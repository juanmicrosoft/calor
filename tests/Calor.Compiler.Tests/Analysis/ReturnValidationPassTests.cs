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
module T T block
  func Do () → void
    !
    return 0
");
    }

    [Fact]
    public void VoidFunction_OmittedReturnType_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func Do ()
    !
    return 0
");
    }

    [Fact]
    public void VoidFunction_OutputVoidMarker_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func Do () → void
    !
    return 0
");
    }

    [Fact]
    public void AsyncVoidFunction_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func DoAsync async ()
    !
    return 0
");
    }

    [Fact]
    public void Iterator_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func Nums () → int
    !
    yield 42
    return 0
");
    }

    [Fact]
    public void VoidMethod_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  class C
    func Do () → void
      !
      return 0
");
    }

    [Fact]
    public void Constructor_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  class C
    ctor ()
      return 0
");
    }

    [Fact]
    public void Setter_ReturnsValue_Fires_ButGetterDoesNot()
    {
        AssertFires(@"
module T T block
  class C
    property string:Name
      get
        return ""x""
      set
        return 0
");
    }

    [Fact]
    public void EventAddAccessor_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  class C
    event EventHandler:Click
      add
        return 0
      remove
        return
");
    }

    [Fact]
    public void EnumExtensionVoidMethod_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  module ColorExtensions
    func Describe (this Color:self) → void
      !
      return 0
");
    }

    [Fact]
    public void NestedInIf_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func Do () → void
    !
    if true
      return 0
");
    }

    [Fact]
    public void NestedInForLoop_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func Do () → void
    !
    for i from 1 to 10 step 1
      return 0
");
    }

    [Fact]
    public void NestedInWhile_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func Do () → void
    !
    while true
      return 0
");
    }

    [Fact]
    public void NestedInTryBody_ReturnsValue_Fires()
    {
        AssertFires(@"
module T T block
  func Do () → void
    !
    try
      return 0
    catch Exception:e
      Console.WriteLine(""x"")
");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("\"hello\"")]
    [InlineData("2 * 3")]
    [InlineData("1 > 0")]
    public void VoidFunction_DefiniteValueForms_Fire(string valueExpr)
    {
        AssertFires($@"
module T T block
  func Do () → void
    !
    return {valueExpr}
");
    }

    [Fact]
    public void VoidFunction_ReturnsReference_Fires()
    {
        AssertFires(@"
module T T block
  func Do (int:x) → void
    !
    return x
");
    }

    // ---------------------------------------------------------------- Negatives

    [Fact]
    public void NonVoidFunction_ReturnsValue_Silent()
    {
        AssertSilent(@"
module T T block
  func Do () → int
    !
    return 0
");
    }

    [Fact]
    public void VoidFunction_BareReturn_Silent()
    {
        AssertSilent(@"
module T T block
  func Do () → void
    !
    return
");
    }

    [Fact]
    public void AsyncFunctionWithReturnType_ReturnsValue_Silent()
    {
        AssertSilent(@"
module T T block
  func GetAsync async () → string
    !
    return ""data""
");
    }

    [Fact]
    public void VoidFunction_ReturnsCall_Silent()
    {
        // A call can be void-typed (a migrated `void F() => VoidCall();` lowers to
        // `§R <call>`), so the pass conservatively does not flag it.
        AssertSilent(@"
module T T block
  func Do () → void
    !
    return Helper.Run(1)
");
    }

    [Fact]
    public void Getter_ReturnsValue_Silent()
    {
        AssertSilent(@"
module T T block
  class C
    property string:Name
      get
        return ""x""
");
    }

    [Fact]
    public void AbstractMethod_NoBody_Silent()
    {
        AssertSilent(@"
module T T block
  class C internal abstract
    func Do abstract () → void
      !
");
    }

    [Fact]
    public void VoidMethodWithModifier_BareReturn_Silent()
    {
        AssertSilent(@"
module T T block
  class C
    func Do static () → void
      !
      return
");
    }

    // ------------------------------------------------------- End-to-end (always-on)

    [Fact]
    public void Compile_VoidFunctionReturningValue_EmitsCalor0205_WithoutTypeChecking()
    {
        var source = @"
module T T block
  func Do () → void
    !
    return 0
";
        var result = Program.Compile(source, filePath: null, new CompilationOptions { EnableTypeChecking = false });

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ReturnValueInVoidOwner);
    }

    [Fact]
    public void Compile_CleanVoidFunction_NoCalor0205()
    {
        var source = @"
module T T block
  func Do () → void
    !
    return
";
        var result = Program.Compile(source, filePath: null, new CompilationOptions { EnableTypeChecking = false });

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ReturnValueInVoidOwner);
    }
}
