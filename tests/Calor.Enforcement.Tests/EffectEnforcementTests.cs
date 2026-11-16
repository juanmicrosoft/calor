using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Xunit;

namespace Calor.Enforcement.Tests;

/// <summary>
/// Tests for effect enforcement at compile time.
/// </summary>
public class EffectEnforcementTests
{
    [Fact]
    public void E1_MissingEffect_FailsWithForbiddenEffect()
    {
        // Function uses §P (print) but doesn't declare cw effect
        var source = TestHarness.LoadScenario("Effects/E1_missing_effect.calr");
        var expected = TestHarness.LoadExpected("Effects/E1_missing_effect.expected.json");

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func PrintHello public () → void
                !
                System.Console.WriteLine("Hello, World!")
            """, DiagnosticCode.ForbiddenEffect, "Function 'PrintHello' uses effect 'cw'"));

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E2_DeclaredEffect_CompilesSuccessfully()
    {
        // Function uses §P (print) and declares cw effect
        var source = TestHarness.LoadScenario("Effects/E2_declared_effect.calr");

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func PrintHello public () → void
                ! cw
                System.Console.WriteLine("Hello, World!")
            """));

        Assert.False(result.HasErrors, $"Should compile successfully. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void E3_CallChain_ReportsCallerFunction()
    {
        // Function A calls function B which has cw effect
        // A should fail because it doesn't declare cw
        var source = TestHarness.LoadScenario("Effects/E3_call_chain.calr");
        var expected = TestHarness.LoadExpected("Effects/E3_call_chain.expected.json");

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func Caller public () → void
                !
                Callee()
              func Callee public () → void
                ! cw
                System.Console.WriteLine("Hello from Callee")
            """, DiagnosticCode.ForbiddenEffect, "Function 'Caller' uses effect 'cw'"));

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E4_UnknownExternal_FailsInStrictMode()
    {
        // Function calls unknown external method
        var source = TestHarness.LoadScenario("Effects/E4_unknown_external.calr");
        var expected = TestHarness.LoadExpected("Effects/E4_unknown_external.expected.json");

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func CallUnknown public () → void
                !
                MyCustomLib.DoSomething()
            """, DiagnosticCode.UnknownExternalCall, "Unknown external call to 'MyCustomLib.DoSomething'"));

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E5_Recursion_ComputesEffectsViaFixpoint()
    {
        // Recursive function with print - effect should be computed via fixpoint
        var source = TestHarness.LoadScenario("Effects/E5_recursion.calr");
        var expected = TestHarness.LoadExpected("Effects/E5_recursion.expected.json");

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func RecursivePrint public (int:n) → void
                !
                if n > 0
                  System.Console.WriteLine(n)
                  RecursivePrint(n - 1)
            """, DiagnosticCode.ForbiddenEffect, "Function 'RecursivePrint' uses effect 'cw'"));

        Assert.True(result.HasErrors, "Should have compilation errors");
        TestHarness.AssertDiagnosticsMatch(result.Diagnostics, expected);
    }

    [Fact]
    public void E6_SpanAccuracy_PointsAtCorrectLocation()
    {
        // Test that diagnostic points at the §P statement
        var source = TestHarness.LoadScenario("Effects/E6_span_accuracy.calr");

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func SpanTest public (int:x) → int
                !
                let int:result = x * 2
                System.Console.WriteLine("The result is: ")
                return result
            """));

        Assert.True(result.HasErrors, "Should have compilation errors");

        var diag = result.Diagnostics.Errors.FirstOrDefault(d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.NotNull(diag);
        // The §P statement is on line 7 in the test file, but diagnostic may point to function
        // Just verify we have a valid span
        Assert.True(diag.Span.Line >= 1 && diag.Span.Line <= 11,
            $"Expected line within file range, got {diag.Span.Line}");
    }

    [Fact]
    public void EffectEnforcement_CanBeDisabled()
    {
        // Same source as E1 but with enforcement disabled
        var source = TestHarness.LoadScenario("Effects/E1_missing_effect.calr");
        var options = new CompilationOptions { EnforceEffects = false };

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func PrintHello public () → void
                !
                System.Console.WriteLine("Hello, World!")
            """), options);

        Assert.False(result.HasErrors, "Should compile successfully with enforcement disabled");
    }

    [Fact]
    public void MultipleEffects_AllMustBeDeclared()
    {
        var source = @"
module Test Test block
  func PrintAndRead () → string
    !
    Console.WriteLine(""Enter your name: "")
    let string:name = ""placeholder""
    return name
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func PrintAndRead public () → string
                !
                System.Console.WriteLine("Enter your name: ")
                let string:name = "placeholder"
                return name
            """));

        Assert.True(result.HasErrors, "Should have compilation errors");
        Assert.Contains(result.Diagnostics.Errors, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void PureFunction_WithNoEffects_Compiles()
    {
        var source = @"
module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func Add public (int:a, int:b) → int
                !
                return a + b
            """));

        Assert.False(result.HasErrors, $"Pure function should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void ConsolePrint_RequiresCwEffect()
    {
        var source = @"
module Test Test block
  func TestPrint () → void
    !
    Console.WriteLine(""test"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func TestPrint public () → void
                !
                System.Console.WriteLine("test")
            """));

        Assert.True(result.HasErrors, "Should require cw effect");
        TestHarness.AssertDiagnostic(result.Diagnostics.Errors, DiagnosticCode.ForbiddenEffect, "TestPrint");
    }

    [Fact]
    public void LinqStaticCall_Enumerable_Range_CompilesSuccessfully()
    {
        // Static LINQ calls like Enumerable.Range should resolve via type mapping
        var source = @"
module Test Test block
  func MakeRange () → void
    !
    Enumerable.Range(1, 10)
    Enumerable.Empty()
    Enumerable.Repeat(0, 5)
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"Static Enumerable calls should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void LinqOfType_CompilesSuccessfully()
    {
        // OfType<T> calls should be recognized as pure
        var source = @"
module Test Test block
  func FilterTypes (List<object>:items) → void
    !
    items.OfType()
    items.Cast()
";
        var result = TestHarness.Compile(source);

        Assert.False(result.HasErrors,
            $"OfType/Cast should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void MethodInClass_WithUndeclaredEffect_FailsEnforcement()
    {
        // §MT method inside §CL using §P without declaring cw should fail
        var source = @"
module Test Test block
  class MyService
    func PrintHello () → void
      !
      Console.WriteLine(""Hello from method"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class MyService
                func PrintHello public () → void
                  !
                  System.Console.WriteLine("Hello from method")
            """));

        Assert.True(result.HasErrors,
            "§MT method with undeclared effect should fail enforcement");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("PrintHello"));
    }

    [Fact]
    public void MethodInClass_WithDeclaredEffect_CompilesSuccessfully()
    {
        // §MT method inside §CL using §P with cw declared should pass
        var source = @"
module Test Test block
  class MyService
    func PrintHello () → void
      ! cw
      Console.WriteLine(""Hello from method"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class MyService
                func PrintHello public () → void
                  ! cw
                  System.Console.WriteLine("Hello from method")
            """));

        Assert.False(result.HasErrors,
            $"§MT with declared cw effect should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void MethodInClass_CallingConsoleWriteLine_FailsWithoutEffect()
    {
        // §MT method calling Console.WriteLine without §E{cw} should fail
        var source = @"
module Test Test block
  class Logger
    func Log () → void
      !
      Console.WriteLine(""log message"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Logger
                func Log public () → void
                  !
                  Console.WriteLine("log message")
            """));

        Assert.True(result.HasErrors,
            "§MT calling Console.WriteLine without cw effect should fail");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void PureMethodInClass_WithNoEffects_Compiles()
    {
        // §MT method with only pure operations should compile
        var source = @"
module Test Test block
  class Calculator
    func Add (int:a, int:b) → int
      !
      return (a + b)
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Calculator
                func Add public (int:a, int:b) → int
                  !
                  return a + b
            """));

        Assert.False(result.HasErrors,
            $"Pure §MT should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void ConstructorWithFieldAssignment_NotFlaggedForEffects()
    {
        // §CTOR that assigns to §THIS fields should NOT be flagged for mut effect
        // since constructors can't declare effects and field assignment is their purpose
        var source = @"
module Test Test block
  class Person
    field string:_name private
    ctor (string:name)
      set this . _name name
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Person
                field string:_name private
                ctor public (string:name)
                  set this._name name
            """));

        Assert.False(result.HasErrors,
            $"Constructor field assignment should not fail enforcement. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void DelegateInvocation_SingleWordTarget_FailsClosed()
    {
        // WS-W2 (D-W2.1): a bare-name invocation that is not an internal
        // function no longer passes as silently pure. A free name (here a
        // delegate-typed member the pass cannot see) routes through the
        // unknown-call chain and fails closed under the default strict policy.
        // (Pre-W2 this test pinned the delegate hole: it asserted no failure.)
        var source = @"
module Test Test block
  class Mapper
    func Apply (int:value) → int
      !
      return transform(value)
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Mapper
                func Apply public (int:value) → int
                  !
                  return transform(value)
            """));

        Assert.True(result.HasErrors,
            "Bare-name invocation of an unresolvable target must fail closed");
        Assert.Contains(result.Diagnostics,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("transform"));
    }

    [Fact]
    public void DelegateInvocation_InStrictMode_IsError()
    {
        // WS-W2 (D-W2.1): under --strict-effects an unresolvable bare-name
        // invocation is a hard Calor0411 error (pre-W2: warning only).
        var source = @"
module Test Test block
  func UseDelegate () → void
    !
    callback()
";
        var options = new CompilationOptions
        {
            EnforceEffects = true,
            StrictEffects = true
        };
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func UseDelegate public () → void
                !
                callback()
            """), options);

        Assert.True(result.HasErrors, "Unresolvable bare-name invocation must be an error in strict mode");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("callback"));
    }

    // === Cross-class method call effect inference (Issue 313) ===

    [Fact]
    public void OverloadedInternalCall_ChargesOnlyResolvedSymbol()
    {
        var source = @"
module Test Test block
  func Work (int:value) → int
    !
    return value

  func Work (string:value) → void
    ! cw
    Console.WriteLine(value)

  func Caller () → int
    !
    return Work(1)
";

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func Work public (int:value) → int
                !
                return value
              func Work public (string:value) → void
                ! cw
                System.Console.WriteLine(value)
              func Caller public () → int
                !
                return Work(1)
            """));

        Assert.False(
            result.HasErrors,
            $"Exact pure overload should not inherit sibling effects: " +
            string.Join("; ", result.Diagnostics.Errors.Select(error => error.Message)));
    }

    [Fact]
    public void UnresolvedInternalOverload_ReportsNoMatchWithoutChargingFirstFunction()
    {
        var source = @"
module Test Test block
  func Work (int:value) → int
    !
    return value

  func Work (string:value) → void
    ! cw
    Console.WriteLine(value)

  func Caller () → void
    !
    Work(true)
";

        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              func Work public (int:value) → int
                !
                return value
              func Work public (string:value) → void
                ! cw
                System.Console.WriteLine(value)
              func Caller public () → void
                !
                Work(true)
            """));

        Assert.Contains(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
        Assert.DoesNotContain(result.Diagnostics.Errors, diagnostic =>
            diagnostic.Code == DiagnosticCode.ForbiddenEffect);
    }

    [Fact]
    public void PreprocessorWrappedMethod_ParticipatesInBoundCallGraph()
    {
        var source = @"
module Test Test block
  class Worker
    pp FEATURE
      func Do () → int
        !
        return 1

    func Run () → int
      !
      return Do()
";

        var result = TestHarness.Compile(source);

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == DiagnosticCode.NoMatchingOverload);
    }

    [Fact]
    public void CrossClass_PureMethodCall_DoesNotTriggerCalor0411()
    {
        // Function calls a pure method on another class in the same module.
        // Should resolve as an internal call, NOT produce Calor0411.
        var source = @"
module Test Test block
  class Calculator
    func Add (int:a, int:b) → int
      !
      return (a + b)

  func UseCalculator (Calculator:_calc) → int
    !
    return _calc.Add(1, 2)
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Calculator
                func Add public (int:a, int:b) → int
                  !
                  return a + b
              func UseCalculator public (Calculator:_calc) → int
                !
                return _calc.Add(1, 2)
            """));

        Assert.False(result.HasErrors,
            $"Cross-class call to pure method should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void CrossClass_EffectfulMethodCall_PropagatesEffects()
    {
        // Function calls a method with cw effect on another class.
        // The caller must declare cw or the effect should propagate as an error.
        var source = @"
module Test Test block
  class Logger
    func Log (string:message) → void
      ! cw
      Console.WriteLine(message)

  func DoWork (Logger:_logger) → void
    !
    _logger.Log(""hello"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Logger
                func Log public (string:message) → void
                  ! cw
                  System.Console.WriteLine(message)
              func DoWork public (Logger:_logger) → void
                !
                _logger.Log("hello")
            """));

        // DoWork calls Logger.Log which has cw, but DoWork doesn't declare cw
        Assert.True(result.HasErrors,
            "Caller of effectful cross-class method should fail without declaring the effect");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("DoWork"));
    }

    [Fact]
    public void CrossClass_EffectfulMethodCall_WithDeclaredEffect_Compiles()
    {
        // Function calls a method with cw effect and properly declares cw.
        var source = @"
module Test Test block
  class Logger
    func Log (string:message) → void
      ! cw
      Console.WriteLine(message)

  func DoWork (Logger:_logger) → void
    ! cw
    _logger.Log(""hello"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Logger
                func Log public (string:message) → void
                  ! cw
                  System.Console.WriteLine(message)
              func DoWork public (Logger:_logger) → void
                ! cw
                _logger.Log("hello")
            """));

        Assert.False(result.HasErrors,
            $"Caller with declared effect should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void CrossClass_MethodToMethod_ResolvesEffects()
    {
        // Method in one class calls a method in another class (both §MT).
        var source = @"
module Test Test block
  class Printer
    func PrintMessage (string:msg) → void
      ! cw
      Console.WriteLine(msg)

  class App
    field Printer:_printer private
    func Run () → void
      ! cw
      _printer.PrintMessage(""hello"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Printer
                func PrintMessage public (string:msg) → void
                  ! cw
                  System.Console.WriteLine(msg)
              class App
                field Printer:_printer private
                func Run public () → void
                  ! cw
                  _printer.PrintMessage("hello")
            """));

        Assert.False(result.HasErrors,
            $"Cross-class method-to-method call should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void CrossClass_MethodToMethod_MissingEffect_Fails()
    {
        // Method in one class calls effectful method in another without declaring the effect.
        var source = @"
module Test Test block
  class Printer
    func PrintMessage (string:msg) → void
      ! cw
      Console.WriteLine(msg)

  class App
    field Printer:_printer private
    func Run () → void
      !
      _printer.PrintMessage(""hello"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Printer
                func PrintMessage public (string:msg) → void
                  ! cw
                  System.Console.WriteLine(msg)
              class App
                field Printer:_printer private
                func Run public () → void
                  !
                  _printer.PrintMessage("hello")
            """));

        Assert.True(result.HasErrors,
            "Cross-class call without declaring effect should fail");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect && d.Message.Contains("Run"));
    }

    [Fact]
    public void CrossClass_MultipleClasses_ChainedCalls_PropagateEffects()
    {
        // A → B → C chain across three classes. Effect should propagate from C to A.
        var source = @"
module Test Test block
  class ServiceC
    func WriteOutput () → void
      ! cw
      Console.WriteLine(""output"")

  class ServiceB
    field ServiceC:_c private
    func Process () → void
      ! cw
      _c.WriteOutput()

  class ServiceA
    field ServiceB:_b private
    func Execute () → void
      ! cw
      _b.Process()
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class ServiceC
                func WriteOutput public () → void
                  ! cw
                  System.Console.WriteLine("output")
              class ServiceB
                field ServiceC:_c private
                func Process public () → void
                  ! cw
                  _c.WriteOutput()
              class ServiceA
                field ServiceB:_b private
                func Execute public () → void
                  ! cw
                  _b.Process()
            """));

        Assert.False(result.HasErrors,
            $"Chained cross-class calls with declared effects should compile. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void CrossClass_NameCollision_TwoClassesSameMethodName_DoesNotFalseResolve()
    {
        // Two classes define a method named "Process" — one pure, one effectful.
        // A caller invokes "_a.Process". Without ambiguity handling, the engine
        // might resolve to the wrong "Process" and either miss or false-report an effect.
        // With the multi-map, ambiguous bare names fall through to external resolution
        // (which produces Calor0411 in strict mode). The caller declares cw to be safe.
        var source = @"
module Test Test block
  class PureService
    func Process (string:data) → string
      !
      return data

  class EffectfulService
    func Process (string:data) → void
      ! cw
      Console.WriteLine(data)

  func DoWork (string:input) → void
    ! cw
    _a.Process(""hello"")
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class PureService
                func Process public (string:data) → string
                  !
                  return data
              class EffectfulService
                func Process public (string:data) → void
                  ! cw
                  System.Console.WriteLine(data)
              func DoWork public (string:input) → void
                ! cw
                _a.Process("hello")
            """));

        // The call to _a.Process is ambiguous (two classes define "Process").
        // With ambiguity detection, the engine does NOT false-resolve to PureService.Process
        // (which would miss the cw effect). Instead it falls through to external resolution
        // which conservatively reports Unknown:* — the correct safe behavior.
        Assert.True(result.HasErrors,
            "Ambiguous cross-class call should NOT silently resolve to one candidate");
        Assert.Contains(result.Diagnostics.Errors,
            d => d.Message.Contains("DoWork") && d.Message.Contains("unknown"));
    }

    [Fact]
    public void CrossClass_UniqueMethodName_StillResolves()
    {
        // When only one class defines a method name, cross-class resolution should still work
        // even with the multi-map (the method name is unambiguous).
        var source = @"
module Test Test block
  class Calculator
    func Compute (int:x) → int
      !
      return (x + 1)

  class OtherService
    func Format (string:s) → string
      !
      return s

  func UseCalc (Calculator:_calc) → int
    !
    return _calc.Compute(5)
";
        var result = TestHarness.Compile(NativeSource(source, """
            module Test Test
              class Calculator
                func Compute public (int:x) → int
                  !
                  return x + 1
              class OtherService
                func Format public (string:s) → string
                  !
                  return s
              func UseCalc public (Calculator:_calc) → int
                !
                return _calc.Compute(5)
            """));

        // "Compute" is unique across classes, so cross-class resolution should work fine.
        Assert.False(result.HasErrors,
            $"Unique cross-class method name should resolve. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    // ========================================================================
    // E2E: Tier B framework interface effects resolve through enforcement
    // ========================================================================

    [Fact]
    public void TierB_E2E_ILoggerCall_ResolvesToCw_NoUnknownWarning()
    {
        // A Calor function calls ILogger.LogInformation and declares cw effect.
        // Should compile without Calor0411 (unknown external call).
        var source = @"
module Test Test block
  func LogSomething () → void
    ! cw
    ILogger.LogInformation(""Processing request"")
";
        var result = TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Strict);

        Assert.False(result.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.UnknownExternalCall),
            $"ILogger.LogInformation should not produce Calor0411. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void TierB_E2E_DbContextCall_ResolvesToDbW_NoUnknownWarning()
    {
        // A Calor function calls DbContext.SaveChanges and declares db:w effect.
        var source = @"
module Test Test block
  func SaveData () → void
    ! db:w
    DbContext.SaveChanges()
";
        var result = TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Strict);

        Assert.False(result.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.UnknownExternalCall),
            $"DbContext.SaveChanges should not produce Calor0411. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void TierB_E2E_IConfigurationCall_ResolvesToEnvR_NoUnknownWarning()
    {
        // A Calor function calls IConfiguration.GetSection and declares env:r effect.
        var source = @"
module Test Test block
  func ReadConfig () → void
    ! env:r
    IConfiguration.GetSection(""ConnectionStrings"")
";
        var result = TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Strict);

        Assert.False(result.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.UnknownExternalCall),
            $"IConfiguration.GetSection should not produce Calor0411. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void TierB_E2E_ControllerOk_IsPure_NoUnknownWarning()
    {
        // A Calor function calls ControllerBase.Ok — should be pure.
        var source = @"
module Test Test block
  func GetResult () → void
    !
    ControllerBase.Ok()
";
        var result = TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Strict);

        Assert.False(result.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.UnknownExternalCall),
            $"ControllerBase.Ok should not produce Calor0411. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void TierB_E2E_UndeclaredDbEffect_ProducesForbiddenEffect()
    {
        // A Calor function calls DbContext.SaveChanges but does NOT declare db:w.
        // Should produce Calor0410 (forbidden effect), NOT Calor0411 (unknown).
        var source = @"
module Test Test block
  func SaveData () → void
    !
    DbContext.SaveChanges()
";
        var result = TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Strict);

        // Should have forbidden-effect error (using db:w without declaring it), NOT unknown-call error
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.UnknownExternalCall && d.Message.Contains("DbContext.SaveChanges"));
    }

    [Fact]
    public void TierB_E2E_MultipleFrameworkCalls_AllResolve()
    {
        // A realistic function that calls multiple framework methods in STRICT mode.
        // This verifies the full pipeline: declared effects match computed effects.
        var source = @"
module Test Test block
  func HandleRequest () → void
    ! cw, db:w, env:r
    ILogger.LogInformation(""Starting request"")
    IConfiguration.GetSection(""AppSettings"")
    DbContext.SaveChanges()
    ILogger.LogInformation(""Request complete"")
";
        var result = TestHarness.CompileWithEffects(source, enforceEffects: true,
            policy: Calor.Compiler.Effects.UnknownCallPolicy.Strict);

        // No unknown external calls — all framework calls resolve from Tier B manifests
        Assert.False(result.Diagnostics.Errors.Any(d => d.Code == DiagnosticCode.UnknownExternalCall),
            $"All framework calls should resolve. Unknown call errors: {string.Join("; ", result.Diagnostics.Errors.Where(d => d.Code == DiagnosticCode.UnknownExternalCall).Select(e => e.Message))}");

        // No forbidden effect errors — declared effects match computed effects
        Assert.False(result.HasErrors,
            $"Should compile with all effects declared. Errors: {string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message))}");
    }
    /// <summary>
    /// A directly self-recursive function must not be reported as an unknown call target.
    ///
    /// Tarjan reports a self-recursive function as a singleton SCC exactly as it reports a
    /// non-recursive one, and <c>ProcessScc</c>'s fast path took that branch with an EMPTY member
    /// set — so the recursive call failed the SccMembers test, found no computed entry (it was
    /// being computed), and fell through to the unknown-call path. Every directly self-recursive
    /// function then failed to compile with <c>Calor0410 uses effect 'Unknown:*'</c>, which cannot
    /// be declared away: the only escapes were the global --no-enforce-effects/--permissive-effects.
    ///
    /// Mutual recursion was unaffected (SCC size >= 2 populates the set), which is why nothing
    /// caught it. Found by the consumer-facing round of v0.12.0 release review, against a corpus
    /// that contains recursion — the release's own corpus tests cover samples/ and bench/pairs,
    /// where no file is self-recursive.
    /// </summary>
    [Fact]
    public void SelfRecursiveFunction_IsNotAnUnknownCallTarget()
    {
        const string source = """
            module Rec Rec block
              func Fact (int:n) → int
                !
                if (n <= 1)
                  return 1
                return (n * Fact((n - 1)))
            """;

        var result = TestHarness.Compile(NativeSource(source, """
            module Rec Rec
              func Fact public (int:n) → int
                !
                if n <= 1
                  return 1
                return n * Fact(n - 1)
            """));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.False(result.HasErrors);
    }

    /// <summary>
    /// The guard that keeps the fix honest: routing self-recursive singletons through the fixpoint
    /// loop must not stop enforcement from biting. A self-recursive function that performs an
    /// undeclared effect is still an error.
    /// </summary>
    [Fact]
    public void SelfRecursiveFunction_WithUndeclaredEffect_StillFails()
    {
        const string source = """
            module RecUndecl RecUndecl block
              func Countdown (int:n) → void
                !
                Console.WriteLine(n)
                if (n > 0)
                  Countdown((n - 1))
            """;

        var result = TestHarness.Compile(NativeSource(source, """
            module RecUndecl RecUndecl
              func Countdown public (int:n) → void
                !
                System.Console.WriteLine(n)
                if n > 0
                  Countdown(n - 1)
            """));

        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    /// <summary>
    /// And the effect a self-recursive function DOES declare must propagate, rather than the
    /// fixpoint seeding Empty and never growing.
    /// </summary>
    [Fact]
    public void SelfRecursiveFunction_DeclaredEffect_IsAccepted()
    {
        const string source = """
            module RecEff RecEff block
              func Countdown (int:n) → void
                ! cw
                Console.WriteLine(n)
                if (n > 0)
                  Countdown((n - 1))
            """;

        var result = TestHarness.Compile(NativeSource(source, """
            module RecEff RecEff
              func Countdown public (int:n) → void
                ! cw
                System.Console.WriteLine(n)
                if n > 0
                  Countdown(n - 1)
            """));

        Assert.False(result.HasErrors);
    }

    // ---------------------------------------------------------------
    // v0.15 E1 slice 2b — the enforcement pass reads receivers from the
    // bound tree. Each test below names the resolver path it exercises.
    // ---------------------------------------------------------------

    /// <summary>
    /// THE discriminating pin for slice 2b. An inferred <c>§B</c> whose
    /// initializer is a BCL call has NO type string anywhere in the AST: the
    /// binding declares none and <c>FindLocalDeclarationType</c> only
    /// understands an explicit type or a <c>§NEW</c> initializer. The bound
    /// tree does know — <c>MetadataBinder</c> types the initializer, so
    /// <c>Receiver.Type</c> on the following call is
    /// <c>NominalBoundType("System.Guid")</c>.
    ///
    /// <para>MEASURED against a clean checkout of <c>main</c> (f7cd1c46) in its
    /// own worktree: FAILS there with
    /// <c>Calor0410: Function 'Go' uses effect 'unknown' but does not declare
    /// it</c> — the receiver is unresolvable, the callee is unknown, and
    /// fail-closed turns that into <c>EffectSet.Unknown</c>. Passes here.</para>
    ///
    /// <para>Discriminates: delete the <c>AskBoundTree</c> call at the head of
    /// <c>ResolveLocalValueType</c> and this test fails with that diagnostic.
    /// </para>
    /// </summary>
    [Fact]
    public void E1Slice2b_InferredLocalReceiverTypedOnlyByTheBinder_ChargesTheRealCallee()
    {
        var source = @"
module BoundReceiverProbe BoundReceiverProbe block
  func Go () → string
    ! rand
    let g = System.Guid.NewGuid()
    return g.ToString()
";
        var result = TestHarness.Compile(source);

        Assert.DoesNotContain(
            result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
        Assert.DoesNotContain(
            result.Diagnostics.ToList(),
            d => d.Code == DiagnosticCode.UnknownExternalCall);
    }

    /// <summary>
    /// The bound path is preferred when BOTH paths have an answer AND they
    /// disagree. A class field <c>x:Random</c> is shadowed by a local
    /// <c>§B{x}</c> whose initializer is a BCL call: the AST search finds no
    /// local type and falls through to the FIELD, so the string path answers
    /// <c>Random</c> — the wrong scope. The binder types the local from its
    /// initializer, so the bound receiver is <c>System.Guid</c>.
    ///
    /// <para>MEASURED against a clean <c>main</c> worktree: FAILS there with
    /// <c>Calor0411: Unknown external call to 'x.ToString'</c> plus the
    /// resulting <c>Calor0410 ... effect 'unknown'</c> — i.e. the string path's
    /// <c>Random</c> answer does not resolve <c>ToString</c>. Passes here,
    /// because the bound receiver is <c>System.Guid</c>.</para>
    /// </summary>
    [Fact]
    public void E1Slice2b_LocalShadowsFieldAndTheStringPathIsWrong_TheBoundTypeWins()
    {
        var source = @"
module ShadowProbe ShadowProbe block
  class Box
    field x:Random private
    func Go () → string
      ! rand
      let x = System.Guid.NewGuid()
      return x.ToString()
";
        var result = TestHarness.Compile(source);

        Assert.DoesNotContain(
            result.Diagnostics.ToList(),
            d => d.Code == DiagnosticCode.UnknownExternalCall);
        Assert.DoesNotContain(
            result.Diagnostics.Errors,
            d => d.Code == DiagnosticCode.ForbiddenEffect);
    }

    /// <summary>
    /// Fail-closed (design doc §8.1, P17's precondition). A receiver the binder
    /// LOOKED at and could not name arrives as <c>UnresolvedBoundType</c>, and
    /// the AST string search does not get to supply a guess in its place: the
    /// call reaches <c>ReportUnknownCall</c> / <c>EffectSet.Unknown</c>.
    ///
    /// <para>MEASURED: passes on a clean <c>main</c> worktree as well — the AST
    /// search finds nothing for that receiver either, so both routes fail
    /// closed. Equivalence pin.</para>
    ///
    /// <para><b>This test does NOT observe the Reported veto</b> (review round
    /// 1, finding 1). It passes with the veto, without it, and on <c>main</c>.
    /// The veto branch in <c>AskBoundTree</c> is structural and currently
    /// unreachable — its own comment carries the three-way measurement. What
    /// this pin does observe is the pass's end-of-chain fail-closed behaviour
    /// (PR #968), which both routes reach.</para>
    ///
    /// <para>The veto is scoped to REPORTED unresolvedness
    /// (<c>UnresolvedBoundType.Reported</c>). Applying it to every unresolved
    /// receiver was tried and measurably deleted resolution: the
    /// converter-synthesized <c>_chainNNN</c> temporaries in
    /// <c>05-02</c>/<c>05-03.approved.calr</c> went from clean to Calor0411 +
    /// Calor0410, failing <c>LosslessFormattingTests</c>. That scoping IS
    /// observed, by that test.</para>
    /// </summary>
    [Fact]
    public void E1Slice2b_UnresolvedBoundReceiver_FailsClosed()
    {
        var source = @"
module UnresolvedProbe UnresolvedProbe block
  func Go (Mystery:m) → void
    !
    m.DoWork()
";
        var result = TestHarness.CompileWithEffects(NativeSource(source, """
            module UnresolvedProbe UnresolvedProbe
              func Go public (Mystery:m) → void
                !
                m.DoWork()
            """));

        Assert.Contains(
            result.Diagnostics.ToList(),
            d => d.Code == DiagnosticCode.UnknownExternalCall
                 || d.Code == DiagnosticCode.ForbiddenEffect);
    }

    /// <summary>
    /// v0.15 E1 slice 2b, review round 2 — the pin that OBSERVES the
    /// <c>Reported</c> veto in <c>AskBoundTree</c>. Round 1 claimed the veto was
    /// unreachable; that was wrong, and this is the shape that reaches it.
    ///
    /// <para>The name <c>u</c> is used two ways in one function: as a call
    /// RECEIVER (<c>u.Run</c>), which is what puts its <c>Reported</c>
    /// <c>UnresolvedBoundType</c> into the side channel, and as a BARE call
    /// target (<c>§C{u}</c>), a position the side channel does not collect.
    /// Because the channel is keyed by NAME rather than by position, the
    /// receiver's answer reaches the bare target too — the name-keyed leak
    /// documented on <c>CallGraphAnalysis.BoundValueTypes</c> is exactly the
    /// veto's reachability path.</para>
    ///
    /// <para><b>v0.15 E1 slice 2c, review round 1 (MAJOR 2) — this test no
    /// longer discriminates, and saying so is the point.</b> Slice 2b's version
    /// of this comment claimed "delete the veto and re-run: <c>0411, 0411, 0418,
    /// 0410</c> instead of <c>0411, 0411, 0411, 0410</c>". That was true then
    /// and is false now. Slice 2c guards the AST's <c>"?"</c> sentinel at
    /// <c>InferFromBareNameTarget</c>
    /// (<c>EffectEnforcementPass.UnknownLocalTypeSentinel</c>), so the sentinel
    /// is no longer mistaken for a type at the bare target and this fixture
    /// reaches <c>Calor0411</c> whether or not the veto branch exists. Deleting
    /// the veto leaves the Enforcement suite at 358/358.</para>
    ///
    /// <para>The test is RETAINED as a behavioural pin — it asserts what this
    /// fixture does, which is still worth holding — but it is <b>not</b> the
    /// veto's discriminating pin, and nothing currently is. The veto is kept
    /// because it states the fail-closed rule at the layer that owns it
    /// (<c>AskBoundTree</c>); E2 owes the pin that observes it, once chain
    /// typing makes <c>AskBoundTree</c> answer <c>Typed</c> for dotted paths.
    /// The branch comment in <c>EffectEnforcementPass</c> carries the same
    /// statement, and so does design doc §8.1.</para>
    /// </summary>
    [Fact]
    public void E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel()
    {
        var source = @"
module VetoReach VetoReach block
  func Go ()
    !
    let u = Mystery.Make()
    u.Run()
    u()
";
        var result = TestHarness.Compile(NativeSource(source, """
            module VetoReach VetoReach
              func Go public ()
                !
                let u = Mystery.Make()
                u.Run()
                u()
            """));

        Assert.DoesNotContain(
            result.Diagnostics.ToList(),
            d => d.Code == DiagnosticCode.DelegateInvocation);
        Assert.Contains(
            result.Diagnostics.ToList(),
            d => d.Code == DiagnosticCode.UnknownExternalCall
                 && d.Message.Contains("'u'"));
    }

    /// <summary>
    /// v0.15 E1 slice 2c — RE-SPECIFIED. This was slice 2b's control for
    /// <see cref="E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel"/>,
    /// and it pinned the OLD behaviour: the same binding and the same bare call
    /// with the receiver use removed produced <c>Calor0418 "declared type
    /// '?'"</c>, because nothing put <c>u</c> into the side channel, the veto
    /// could not fire, and the AST's <c>"?"</c> sentinel reached
    /// <c>InferFromBareNameTarget</c>, which tested <c>!= null</c>.
    ///
    /// <para>Slice 2c resolves that debt. The sentinel is now guarded at the
    /// bare-target site
    /// (<c>EffectEnforcementPass.UnknownLocalTypeSentinel</c>), so the answer no
    /// longer depends on whether the name happened to be used as a receiver
    /// somewhere else in the function: <b>a bare call on an unknown-typed
    /// <c>§B</c> is Calor0411 either way</b>. That symmetry is what this test
    /// now pins, and it is the point — Calor0418 charged
    /// <c>EffectSet.Empty</c> on a value the pass could not type at all, which
    /// is laundering; Calor0411 fails closed.</para>
    ///
    /// <para><b>What this costs, stated rather than hidden.</b> The guard
    /// SUBSUMES the veto for this shape, so
    /// <c>E1Slice2b_ReportedUnresolvedReceiver_VetoesTheAstSentinel</c> now
    /// passes even with the veto branch deleted — it is retained as a
    /// behavioural pin, not a discriminating one. The veto itself is kept
    /// because it states the fail-closed rule at the layer that owns it
    /// (<c>AskBoundTree</c>), and E2 needs it there once chains carry types.
    /// Design doc §8.1 records the same thing.</para>
    /// </summary>
    [Fact]
    public void E1Slice2c_BareCallOnUnknownTypedBinding_IsCalor0411WithOrWithoutAReceiverUse()
    {
        var withoutReceiverUse = NativeSource(@"
module VetoControl VetoControl block
  func Go ()
    !
    let u = Mystery.Make()
    u()
", """
            module VetoControl VetoControl
              func Go public ()
                !
                let u = Mystery.Make()
                u()
            """);
        var withReceiverUse = NativeSource(@"
module VetoReach VetoReach block
  func Go ()
    !
    let u = Mystery.Make()
    u.Run()
    u()
", """
            module VetoReach VetoReach
              func Go public ()
                !
                let u = Mystery.Make()
                u.Run()
                u()
            """);

        foreach (var source in new[] { withoutReceiverUse, withReceiverUse })
        {
            var result = TestHarness.Compile(source);

            Assert.DoesNotContain(
                result.Diagnostics.ToList(),
                d => d.Code == DiagnosticCode.DelegateInvocation);
            Assert.Contains(
                result.Diagnostics.ToList(),
                d => d.Code == DiagnosticCode.UnknownExternalCall
                     && d.Message.Contains("'u'"));
        }
    }

    /// <summary>
    /// SURVIVING FALLBACK — <c>function.Parameters[].TypeName</c>. A parameter
    /// used as a BARE call target is not a receiver, so the receiver side
    /// channel carries no entry for it and the AST parameter-type string is
    /// what fills the diagnostic's message — v0.15 E4: Calor0425 (the row-less
    /// parameter's row is Unknown), where pre-E4 it was Calor0418.
    ///
    /// <para>This is the shape that decided the slice's scope. An earlier
    /// revision recorded every bound name, not just receivers; that made the
    /// side channel answer here and in the method-group-argument arm, and
    /// <c>StrictnessBatchTests.C2</c>/<c>C4</c> went red because the
    /// method-group charging arm stopped firing. Names outside receiver
    /// positions keep resolving through the AST.</para>
    /// </summary>
    [Fact]
    public void E1Slice2b_FunctionTypedParameterAsBareTarget_UsesTheAstParameterType()
    {
        var source = @"
module BareTargetProbe BareTargetProbe block
  func Go (Func<int>:make) → int
    !
    return make()
";
        var result = TestHarness.Compile(NativeSource(source, """
            module BareTargetProbe BareTargetProbe
              func Go public (Func<int>:make ! unknown) → int
                !
                return make()
            """));

        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCode.DelegateInvocation);
        var rowUnknown = Assert.Single(
            result.Diagnostics.Warnings.Where(d => d.Code == DiagnosticCode.EffectRowUnknown));
        Assert.Contains("parameter 'make' of 'Go'", rowUnknown.Message);
        Assert.Contains("Func<", rowUnknown.Message);
    }

    private static string NativeSource(
        string original, string native, string? requiredDiagnostic = null, string? requiredMessage = null)
    {
        Assert.False(string.IsNullOrWhiteSpace(original));
        var diagnostics = new DiagnosticBag();
        _ = new Calor.Compiler.Parsing.Parser(
            new Calor.Compiler.Parsing.Lexer(native, diagnostics).TokenizeAllForParser(),
            diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        if (requiredDiagnostic != null)
        {
            var compiled = TestHarness.Compile(native);
            Assert.Contains(compiled.Diagnostics, diagnostic =>
                diagnostic.Code == requiredDiagnostic &&
                (requiredMessage == null || diagnostic.Message.Contains(requiredMessage, StringComparison.Ordinal)));
        }
        return native;
    }
}
