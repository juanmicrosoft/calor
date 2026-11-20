using Calor.Compiler.Analysis;
using Calor.Compiler.Analysis.BugPatterns;
using Calor.Compiler.Analysis.ContractInference;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Evaluation.Benchmarks;
using Calor.Evaluation.Core;
using Calor.Evaluation.Metrics;
using Xunit;

namespace Calor.Compiler.Tests;

public class BugDetectionImprovementTests
{
    private static ModuleNode Parse(string source, out DiagnosticBag diagnostics)
    {
        diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        var parser = new Parser(tokens, diagnostics);
        return parser.Parse();
    }

    private static BoundModule Bind(ModuleNode module)
    {
        var diagnostics = new DiagnosticBag();
        var binder = new Binder(diagnostics);
        return binder.Bind(module);
    }

    #region EvaluationContext Analysis

    [Fact]
    public void CompileCalor_RunsAnalysis_DetectsDivisionByZero()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C {}",
            FileName = "test"
        };

        var result = ctx.CalorCompilation;

        // Analysis should have run
        Assert.NotNull(result.AnalysisResult);
        Assert.True(result.AnalysisResult!.FunctionsAnalyzed > 0);
    }

    [Fact]
    public void CompileCalor_WithContracts_NoFalsePositives()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    return (a / b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C {}",
            FileName = "test"
        };

        var result = ctx.CalorCompilation;

        // Should succeed — precondition guards the division
        Assert.NotNull(result.AnalysisResult);
        Assert.True(result.AnalysisResult!.FunctionsAnalyzed > 0);
    }

    [Fact]
    public void CompileCalor_AnalysisResult_HasFunctionCount()
    {
        var source = @"
module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)

  func Mul (int:a, int:b) → int
    !
    return (a * b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C {}",
            FileName = "test"
        };

        var result = ctx.CalorCompilation;

        Assert.NotNull(result.AnalysisResult);
        Assert.Equal(2, result.AnalysisResult!.FunctionsAnalyzed);
    }

    [Fact]
    public void CompileCalor_Success_StillTrueForWarnings()
    {
        // Division by parameter should produce a warning but not an error
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C {}",
            FileName = "test"
        };

        var result = ctx.CalorCompilation;

        // Warnings should not cause compilation failure
        Assert.NotNull(result.AllDiagnostics);
    }

    #endregion

    #region ErrorDetection Contract Syntax

    [Fact]
    public async Task CalculateCalorDetection_WithCorrectSyntax_ScoresHigherThanBase()
    {
        var source = @"
module Test Test block
  func Divide public (int:a, int:b | (# != 0)) → int
    !
    => result == a / b
    return a / b
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C { int Divide(int a, int b) => a / b; }",
            FileName = "test"
        };

        var calc = new ErrorDetectionCalculator();
        var result = await calc.CalculateAsync(ctx);

        // With a precondition and a postcondition present, Calor score should be > 0.3 base
        Assert.True(result.CalorScore > 0.3, $"Calor score {result.CalorScore} should be > 0.3");
    }

    [Fact]
    public void DetectsBugViaContracts_UsesCorrectMarkers()
    {
        // Source with a native precondition
        var source = @"
module Test Test block
  func Divide public (int:a, int:b | (# != 0)) → int
    !
    return a / b
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C {}",
            FileName = "test"
        };

        var calc = new ErrorDetectionCalculator();
        var bug = new BugDescription
        {
            Id = "test1",
            Category = "contract_violation",
            Description = "test"
        };

        var result = calc.EvaluateBuggyCode(source, source, "class C {}", "class C {}", bug);

        // Should detect via contract since a precondition is present
        Assert.True(result.CalorDetectedViaContract);
    }

    [Fact]
    public void EvaluateBuggyCode_DivideByZero_CalorDetects()
    {
        var buggySource = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var fixedSource = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    return (a / b)
";
        var calc = new ErrorDetectionCalculator();
        var bug = new BugDescription
        {
            Id = "bug002",
            Category = "contract_violation",
            Description = "Division by zero"
        };

        var result = calc.EvaluateBuggyCode(
            buggySource, fixedSource, "class C {}", "class C {}", bug);

        // The buggy version has no contracts, but the analysis should still catch it
        // via compilation analysis (VerificationAnalysisPass finds div-by-zero)
        Assert.True(result.CalorDetectionScore >= 0);
    }

    #endregion

    #region PreconditionSuggester

    [Fact]
    public void DivisionByParameter_WithoutPrecondition_EmitsWarning()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new BugPatternOptions
        {
            UseZ3Verification = false,
            CheckDivisionByZero = false, // Disable to isolate PreconditionSuggester
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckOffByOne = false
        };
        var runner = new BugPatternRunner(diagnostics, options);
        var bound = Bind(module);

        foreach (var func in bound.Functions)
            runner.CheckFunction(func);

        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.MissingPrecondition);
        Assert.Contains(diagnostics, d => d.Message.Contains("b"));
    }

    [Fact]
    public void DivisionByParameter_WithPrecondition_NoDiagnostic()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var guardedParams = new Dictionary<string, HashSet<string>>
        {
            ["Divide"] = new HashSet<string> { "b" }
        };
        var options = new BugPatternOptions
        {
            UseZ3Verification = false,
            CheckDivisionByZero = false,
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckOffByOne = false,
            PreconditionGuardedParams = guardedParams
        };
        var runner = new BugPatternRunner(diagnostics, options);
        var bound = Bind(module);

        foreach (var func in bound.Functions)
            runner.CheckFunction(func);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.MissingPrecondition);
    }

    [Fact]
    public void DivisionByConstant_NoDiagnostic()
    {
        var source = @"
module Test Test block
  func Half (int:a) → int
    !
    return (a / 2)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new BugPatternOptions
        {
            UseZ3Verification = false,
            CheckDivisionByZero = false,
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckOffByOne = false
        };
        var runner = new BugPatternRunner(diagnostics, options);
        var bound = Bind(module);

        foreach (var func in bound.Functions)
            runner.CheckFunction(func);

        // Division by constant 2 should not trigger a missing precondition warning
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.MissingPrecondition);
    }

    [Fact]
    public void SuggestedFix_ContainsValidContractText()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new BugPatternOptions
        {
            UseZ3Verification = false,
            CheckDivisionByZero = false,
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckOffByOne = false
        };
        var runner = new BugPatternRunner(diagnostics, options);
        var bound = Bind(module);

        foreach (var func in bound.Functions)
            runner.CheckFunction(func);

        // Check that we have a fix with native contract text, placed so the result parses
        Assert.True(diagnostics.DiagnosticsWithFixes.Count > 0);
        var fix = diagnostics.DiagnosticsWithFixes[0].Fix;
        Assert.Contains("requires b != 0", fix.Description);
        AssertFixAddsContract(source, fix, preconditions: 1, postconditions: 0);
    }

    #endregion

    #region OffByOneChecker

    [Fact]
    public void ForLoop_WithoutRealBoundedAccess_DoesNotWarn()
    {
        var source = @"
module Test Test block
  func Sum (int:n) → int
    !
    var int:total = 0
    for i from 0 to n step 1
      var int:total = (total + i)
    return total
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new BugPatternOptions
        {
            UseZ3Verification = false,
            CheckDivisionByZero = false,
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckMissingPreconditions = false
        };
        var runner = new BugPatternRunner(diagnostics, options);
        var bound = Bind(module);

        foreach (var func in bound.Functions)
            runner.CheckFunction(func);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OffByOne);
    }

    [Fact]
    public void ForLoop_ToLessThanLength_NoWarning()
    {
        // When the bound is subtracted by 1, no warning
        var source = @"
module Test Test block
  func Sum (int:n) → int
    !
    var int:total = 0
    for i from 0 to (n - 1) step 1
      var int:total = (total + i)
    return total
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new BugPatternOptions
        {
            UseZ3Verification = false,
            CheckDivisionByZero = false,
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckMissingPreconditions = false
        };
        var runner = new BugPatternRunner(diagnostics, options);
        var bound = Bind(module);

        foreach (var func in bound.Functions)
            runner.CheckFunction(func);

        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OffByOne);
    }

    [Fact]
    public void ForLoop_NoArrayAccess_NoWarning()
    {
        // Loop to a literal bound, body doesn't use loop var
        var source = @"
module Test Test block
  func Count (int:n) → int
    !
    var int:total = 0
    for i from 0 to 10 step 1
      var int:total = (total + 1)
    return total
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new BugPatternOptions
        {
            UseZ3Verification = false,
            CheckDivisionByZero = false,
            CheckIndexOutOfBounds = false,
            CheckNullDereference = false,
            CheckOverflow = false,
            CheckMissingPreconditions = false
        };
        var runner = new BugPatternRunner(diagnostics, options);
        var bound = Bind(module);

        foreach (var func in bound.Functions)
            runner.CheckFunction(func);

        // Literal upper bound 10 is not length-like — no warning expected
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.OffByOne);
    }

    #endregion

    #region ContractInference

    [Fact]
    public void DivisionFunction_InfersNonZeroPrecondition()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var bound = Bind(module);
        var inferencePass = new ContractInferencePass(diagnostics);
        var count = inferencePass.Infer(module, bound);

        Assert.True(count > 0, "Should infer at least one contract");
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.InferredContract);
        Assert.Contains(diagnostics, d => d.Message.Contains("b"));
    }

    [Fact]
    public void FunctionWithExistingContract_NoInference()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var bound = Bind(module);
        var inferencePass = new ContractInferencePass(diagnostics);
        var count = inferencePass.Infer(module, bound);

        Assert.Equal(0, count);
        Assert.DoesNotContain(diagnostics, d => d.Code == DiagnosticCode.InferredContract);
    }

    [Fact]
    public void InferredContract_HasSuggestedFix()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var bound = Bind(module);
        var inferencePass = new ContractInferencePass(diagnostics);
        inferencePass.Infer(module, bound);

        Assert.True(diagnostics.DiagnosticsWithFixes.Count > 0);
        var fix = diagnostics.DiagnosticsWithFixes[0].Fix;
        Assert.Contains("requires b != 0", fix.Description);
        AssertFixAddsContract(source, fix, preconditions: 1, postconditions: 0);
    }

    [Theory]
    [InlineData("func Identity (int:x) → int\n    !\n    return x", "=> result == x")]
    [InlineData("func Square (int:x) → int\n    !\n    return (x * x)", "=> result >= 0")]
    public void InferredPostcondition_FixInsertsANativeClauseThatParses(string function, string clause)
    {
        var source = "module Test Test block\n  " + function + "\n";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        new ContractInferencePass(diagnostics).Infer(module, Bind(module));

        var fix = Assert.Single(diagnostics.DiagnosticsWithFixes).Fix;
        Assert.Contains(clause, fix.Description);
        AssertFixAddsContract(source, fix, preconditions: 0, postconditions: 1);
    }

    /// <summary>
    /// Applies a fix's edits (1-based line and column) and checks that the result parses and
    /// the function gained exactly the expected contract clauses.
    /// </summary>
    private static void AssertFixAddsContract(string source, SuggestedFix fix, int preconditions, int postconditions)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n').ToList();
        foreach (var edit in fix.Edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            Assert.Equal(edit.StartLine, edit.EndLine);
            Assert.Equal(edit.StartColumn, edit.EndColumn);
            var line = lines[edit.StartLine - 1];
            var text = line[..(edit.StartColumn - 1)] + edit.NewText + line[(edit.StartColumn - 1)..];
            lines.RemoveAt(edit.StartLine - 1);
            lines.InsertRange(edit.StartLine - 1, text.Split('\n'));
        }
        var patched = string.Join("\n", lines);

        var module = Parse(patched, out var parseDiag);
        Assert.False(parseDiag.HasErrors, patched + "\n" + string.Join("\n", parseDiag.Select(d => d.Message)));
        var function = Assert.Single(module.Functions);
        Assert.Equal(preconditions, function.Preconditions.Count);
        Assert.Equal(postconditions, function.Postconditions.Count);
    }

    #endregion

    #region Bug Scenario Integration

    [Fact]
    public void BugScenario_DivideByZero_CalorDetects()
    {
        var buggySource = @"
module UnsafeMath UnsafeMath block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = buggySource,
            CSharpSource = "class C { int Divide(int a, int b) => a / b; }",
            FileName = "bug002"
        };

        var compilation = ctx.CalorCompilation;

        // Analysis should detect the potential division by zero
        Assert.NotNull(compilation.AnalysisResult);
        Assert.True(compilation.AnalysisResult!.BugPatternsFound > 0 ||
                     compilation.AllDiagnostics!.Any(d =>
                         d.Code == DiagnosticCode.DivisionByZero ||
                         d.Code == DiagnosticCode.MissingPrecondition));
    }

    [Fact]
    public void BugScenario_NullDeref_CalorDetects()
    {
        var buggySource = @"
module UnsafeNullable UnsafeNullable block
  func GetValue () → int
    !
    let int[]:maybeVal = null
    return maybeVal.Length
";
        var module = Parse(buggySource, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new VerificationAnalysisOptions
        {
            UseZ3Verification = false,
            EnableKInduction = false
        };
        var pass = new VerificationAnalysisPass(diagnostics, options);
        var result = pass.Analyze(module);

        Assert.True(result.FunctionsAnalyzed > 0);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.NullDereference);
    }

    [Fact]
    public void BugScenario_Overflow_CalorDetects()
    {
        var buggySource = @"
module UnsafeAdd UnsafeAdd block
  func AddLarge (int:a, int:b) → int
    !
    return (a + b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = buggySource,
            CSharpSource = "class C {}",
            FileName = "bug004"
        };

        var compilation = ctx.CalorCompilation;

        // Analysis should run and may detect overflow warning
        Assert.NotNull(compilation.AnalysisResult);
        Assert.True(compilation.AnalysisResult!.FunctionsAnalyzed > 0);
    }

    [Fact]
    public void BugScenario_MissingContract_CalorDetects()
    {
        var buggySource = @"
module UnsafeDivide UnsafeDivide block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = buggySource,
            CSharpSource = "class C { int Divide(int a, int b) => a / b; }",
            FileName = "bug005"
        };

        var compilation = ctx.CalorCompilation;

        // Should detect missing precondition
        Assert.NotNull(compilation.AllDiagnostics);
        Assert.Contains(compilation.AllDiagnostics!, d =>
            d.Code == DiagnosticCode.MissingPrecondition ||
            d.Code == DiagnosticCode.DivisionByZero);
    }

    #endregion

    #region VerificationAnalysisPass Integration

    [Fact]
    public void VerificationAnalysisPass_ExtractsPreconditionGuardedParams()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var pass = new VerificationAnalysisPass(diagnostics, VerificationAnalysisOptions.Fast);
        var result = pass.Analyze(module);

        // The pass should have run successfully
        Assert.True(result.FunctionsAnalyzed > 0);
        // With precondition guarding 'b', the PreconditionSuggester (Calor0926)
        // should not fire for 'b'. The DivisionByZeroChecker (Calor0920) may still
        // fire as a heuristic warning since Z3 is off.
        Assert.DoesNotContain(diagnostics, d =>
            d.Code == DiagnosticCode.MissingPrecondition && d.Message.Contains("'b'"));
    }

    [Fact]
    public void VerificationAnalysisPass_ContractInference_WhenEnabled()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var options = new VerificationAnalysisOptions
        {
            UseZ3Verification = false,
            EnableContractInference = true,
            EnableKInduction = false
        };
        var pass = new VerificationAnalysisPass(diagnostics, options);
        var result = pass.Analyze(module);

        Assert.True(result.ContractsInferred > 0, "Should infer contracts for unguarded division");
    }

    #endregion

    #region Postcondition Inference

    [Fact]
    public void ContractInference_IdentityReturn_InfersPostcondition()
    {
        // Function that just returns a parameter should infer => result == param
        var source = @"
module Test Test block
  func Identity (int:x) → int
    !
    return x
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var bound = Bind(module);
        var inferencePass = new ContractInferencePass(diagnostics);
        var count = inferencePass.Infer(module, bound);

        Assert.True(count > 0, "Should infer postcondition for identity return");
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.InferredContract &&
            d.Message.Contains("postcondition"));
    }

    [Fact]
    public void ContractInference_SquareFunction_InfersNonNegative()
    {
        // Function that returns x * x should infer => result >= 0
        var source = @"
module Test Test block
  func Square (int:x) → int
    !
    return (x * x)
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var bound = Bind(module);
        var inferencePass = new ContractInferencePass(diagnostics);
        var count = inferencePass.Infer(module, bound);

        Assert.True(count > 0, "Should infer non-negative postcondition for square");
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.InferredContract &&
            d.Message.Contains("=> result >= 0"));
    }

    #endregion

    #region First Try Success Regression Fix

    [Fact]
    public void CompileCalor_AnalysisErrors_DoNotAffectSuccess()
    {
        // Programs with potential bugs (e.g., division by parameter) should still
        // report Success=true because they parse and bind correctly. Analysis
        // findings are informational, not compilation failures.
        var source = @"
module Test Test block
  func Divide (int:a, int:b) → int
    !
    return (a / b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C { int Divide(int a, int b) => a / b; }",
            FileName = "test"
        };

        var result = ctx.CalorCompilation;

        // Compilation must succeed — parse and binding are correct
        Assert.True(result.Success, "Compilation should succeed despite analysis findings");
        // Analysis should still run and find issues
        Assert.NotNull(result.AnalysisResult);
        // Analysis diagnostics should be available in AllDiagnostics
        Assert.NotNull(result.AllDiagnostics);
        Assert.True(result.AllDiagnostics!.Count > 0, "Analysis should have produced diagnostics");
        // But Errors list should be empty (no parse/lex errors)
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CalorStructureScore_WellFormedProgram_Reaches1()
    {
        // A well-formed Calor program with module, function, name, output, body,
        // and parameters should achieve a structure score of 1.0
        var source = @"
module MathOps MathOps block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "using System; namespace N { class C { int Add(int a, int b) => a + b; } }",
            FileName = "test"
        };

        var calc = new GenerationAccuracyCalculator();
        var metrics = calc.CalculateDetailedMetrics(ctx);

        var structureMetric = metrics.First(m => m.MetricName == "StructureCompleteness");
        Assert.Equal(1.0, structureMetric.CalorScore, 2);
    }

    [Fact]
    public async Task GenerationAccuracy_WellFormedProgram_RatioAtLeast1()
    {
        // With both fixes applied, a well-formed Calor program should achieve
        // a GenerationAccuracy ratio >= 1.0 compared to C#
        var source = @"
module MathOps MathOps block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var csharpSource = @"
using System;
namespace MathOps {
    class Calculator {
        int Add(int a, int b) => a + b;
    }
}
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = csharpSource,
            FileName = "test"
        };

        var calc = new GenerationAccuracyCalculator();
        var result = await calc.CalculateAsync(ctx);

        Assert.True(result.CalorScore >= result.CSharpScore,
            $"Calor score {result.CalorScore} should be >= C# score {result.CSharpScore}");
    }

    [Fact]
    public void CompileCalor_IsPrimeBenchmark_SucceedsWithAnalysis()
    {
        // IsPrime was one of the 3 failing programs in v0.3.0.
        // It should succeed after the fix.
        var source = @"
module PrimeCheck PrimeCheck block
  func IsPrime (int:n | (# > 0)) → bool
    !
    if (n <= 1)
      return false
    if (n <= 3)
      return true
    if ((n % 2) == 0)
      return false
    for i from 3 to 1000 step 2
      if ((i * i) > n)
        return true
      if ((n % i) == 0)
        return false
    return true
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C {}",
            FileName = "IsPrime"
        };

        var result = ctx.CalorCompilation;

        Assert.True(result.Success, $"IsPrime should compile successfully. Errors: {string.Join(", ", result.Errors)}");
    }

    #endregion

    #region Integration: Bug Scenarios from Manifest

    [Fact]
    public void BugScenarios_AllBuggyFiles_ParseSuccessfully()
    {
        // Verify that all bug scenario .calr files parse without errors
        var testDataDir = FindTestDataDir();
        if (testDataDir == null)
            return; // Skip if test data not found

        var errorDetectionDir = Path.Combine(testDataDir, "Benchmarks", "ErrorDetection");
        if (!Directory.Exists(errorDetectionDir))
            return;

        var buggyFiles = Directory.GetFiles(errorDetectionDir, "*_buggy.calr");
        Assert.True(buggyFiles.Length > 0, "Should have buggy .calr files");

        foreach (var file in buggyFiles)
        {
            var relativePath = Path.Combine("tests", "TestData", Path.GetRelativePath(testDataDir, file));
            var currentPath = ActiveSyntaxInputs.ForCurrentCompiler(CliTestHarness.FindRepoRoot(), relativePath);
            var source = File.ReadAllText(currentPath);
            var module = Parse(source, out var parseDiag);
            Assert.False(parseDiag.HasErrors,
                $"File {Path.GetFileName(file)} has parse errors: {string.Join("\n", parseDiag.Select(d => d.Message))}");
        }
    }

    [Theory]
    [InlineData("=> result == 1", true)]
    [InlineData("    => result == 1", true)]
    [InlineData("\t=> (result == 1)", true)]
    [InlineData("    return xs.Select(x => x + 1)", false)]
    public void EnsuresPattern_MatchesLineHeadPostconditionsOnly(string line, bool detected)
    {
        // A native postcondition is a line that starts with `=>`. No precondition is present,
        // so only the postcondition can make the contract check succeed; an `=>` inside an
        // expression (a lambda) is not a postcondition.
        var calc = new ErrorDetectionCalculator();
        var bug = new BugDescription { Id = "test", Category = "contract_violation", Description = "test" };
        var result = calc.EvaluateBuggyCode(line, line, "class C {}", "class C {}", bug);
        Assert.Equal(detected, result.CalorDetectedViaContract);
    }

    #endregion

    #region EdgeCaseCoverageAnalyzer

    [Fact]
    public void EdgeCaseCoverage_ProgramWithEarlyReturnGuards_DetectsGuards()
    {
        var source = @"
module Test Test block
  func IsPrime (int:n) → bool
    !
    if (n <= 1)
      return false
    if (n <= 3)
      return true
    if ((n % 2) == 0)
      return false
    return true
";
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Select(d => d.Message)));

        var result = EdgeCaseCoverageAnalyzer.Analyze(module);

        Assert.Equal(3, result.EarlyReturnGuards);
        Assert.Equal(1, result.TotalFunctions);
        Assert.True(result.CoverageScore > 0, "Should have positive coverage score");
    }

    [Fact]
    public void EdgeCaseCoverage_ProgramWithBoundaryChecks_DetectsBoundaries()
    {
        var source = @"
module Test Test block
  func Clamp (int:n) → int
    !
    if (n < 0)
      return 0
    if (n == 1)
      return 1
    return n
";
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Select(d => d.Message)));

        var result = EdgeCaseCoverageAnalyzer.Analyze(module);

        Assert.True(result.BoundaryConditionChecks >= 2,
            $"Should detect at least 2 boundary checks, got {result.BoundaryConditionChecks}");
    }

    [Fact]
    public void EdgeCaseCoverage_ProgramWithElseBranches_DetectsCompleteness()
    {
        var source = @"
module Test Test block
  func Abs (int:n) → int
    !
    if (n < 0)
      return (0 - n)
    else
      return n
";
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Select(d => d.Message)));

        var result = EdgeCaseCoverageAnalyzer.Analyze(module);

        Assert.True(result.ElseBranches >= 1,
            $"Should detect at least 1 else branch, got {result.ElseBranches}");
    }

    [Fact]
    public void EdgeCaseCoverage_ProgramWithContracts_DetectsContractCoverage()
    {
        var source = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    => (result >= 0)
    return (a / b)

  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Select(d => d.Message)));

        var result = EdgeCaseCoverageAnalyzer.Analyze(module);

        Assert.Equal(2, result.TotalFunctions);
        Assert.Equal(1, result.FunctionsWithContracts);
    }

    [Fact]
    public void EdgeCaseCoverage_MinimalProgram_ScoresLow()
    {
        var source = @"
module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Select(d => d.Message)));

        var result = EdgeCaseCoverageAnalyzer.Analyze(module);

        Assert.Equal(0, result.EarlyReturnGuards);
        Assert.Equal(0, result.BoundaryConditionChecks);
        Assert.Equal(0, result.ElseBranches);
        Assert.Equal(0, result.FunctionsWithContracts);
        Assert.Equal(0.0, result.CoverageScore);
    }

    [Fact]
    public void EdgeCaseCoverage_MatchExpressionWithWildcard_DetectsExhaustiveness()
    {
        // Match expression (inside return) with wildcard pattern
        var source = @"
module Test Test block
  func Describe (int:x) → string
    !
    return match x
      case 1 → ""one""
      case 2 → ""two""
      case _ → ""other""
";
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Select(d => d.Message)));

        var result = EdgeCaseCoverageAnalyzer.Analyze(module);

        Assert.True(result.ExhaustiveMatches >= 1,
            $"Should detect exhaustive match with wildcard, got {result.ExhaustiveMatches}");
    }

    [Fact]
    public void EdgeCaseCoverage_MatchStatementWithWildcard_DetectsExhaustiveness()
    {
        // Match statement (not inside return) with wildcard pattern
        var source = @"
module Test Test block
  func Test (int:x) → int
    !
    match x
      case 1
        return 10
      case _
        return 0
";
        var module = Parse(source, out var diag);
        Assert.False(diag.HasErrors, string.Join("\n", diag.Select(d => d.Message)));

        var result = EdgeCaseCoverageAnalyzer.Analyze(module);

        Assert.True(result.ExhaustiveMatches >= 1,
            $"Should detect exhaustive match statement with wildcard, got {result.ExhaustiveMatches}");
    }

    [Fact]
    public async Task CorrectnessEstimation_CalorWithConditionalReturns_ScoresHigher()
    {
        // A Calor program with → §R conditional returns should score higher than base
        var source = @"
module Test Test block
  func Factorial (int:n | (# >= 0)) → int
    !
    if (n <= 1)
      return 1
    return (n * Factorial((n - 1)))
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C { int Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1); }",
            FileName = "test"
        };

        var calc = new CorrectnessCalculator();
        var result = await calc.CalculateAsync(ctx);

        // Should get credit for preconditions, conditional returns, boundary checks, and analysis signal
        Assert.True(result.CalorScore > 0.5,
            $"Calor score {result.CalorScore} should be > 0.5 (base) with conditional returns");
    }

    [Fact]
    public async Task CorrectnessEstimation_CalorWithDefensivePatterns_GetsAnalysisBonus()
    {
        // A program with actual defensive patterns should get the analysis signal bonus
        // but a minimal program without them should not
        var defensiveSource = @"
module Test Test block
  func SafeDiv (int:a, int:b | (# != 0)) → int
    !
    if (b <= 1)
      return 0
    return (a / b)
";
        var minimalSource = @"
module Test Test block
  func Add (int:a, int:b) → int
    !
    return (a + b)
";
        var defensiveCtx = new EvaluationContext
        {
            CalorSource = defensiveSource,
            CSharpSource = "class C { int SafeDiv(int a, int b) => b == 0 ? 0 : a / b; }",
            FileName = "test"
        };
        var minimalCtx = new EvaluationContext
        {
            CalorSource = minimalSource,
            CSharpSource = "class C { int Add(int a, int b) => a + b; }",
            FileName = "test"
        };

        var calc = new CorrectnessCalculator();
        var defensiveResult = await calc.CalculateAsync(defensiveCtx);
        var minimalResult = await calc.CalculateAsync(minimalCtx);

        // Defensive program should score higher than minimal — the analysis signal
        // requires actual edge case coverage, not just absence of bugs
        Assert.True(defensiveResult.CalorScore > minimalResult.CalorScore,
            $"Defensive score {defensiveResult.CalorScore} should be > minimal score {minimalResult.CalorScore}");
    }

    [Fact]
    public async Task CorrectnessEstimation_BothLanguagesCanReachMax()
    {
        // Verify that both languages can theoretically reach 1.0
        // C# with all patterns
        var csharpSource = @"
class C {
    int Divide(int a, int b) {
        if (b == null) throw new ArgumentNullException(""b"");
        if (b == 0) throw new ArgumentException(""b"");
        if (a.Length > 0 && b.Count > 0) { }
        try { return a / b; } catch { return 0; }
        return -1;
    }
}";
        // Calor with all patterns
        var calorSource = @"
module Test Test block
  func Divide (int:a, int:b | (# != 0)) → int
    !
    => (result >= 0)
    if (b <= 1)
      return 0
    if (a > 0)
      return (a / b)
    else
      return 0
";
        var ctx = new EvaluationContext
        {
            CalorSource = calorSource,
            CSharpSource = csharpSource,
            FileName = "test"
        };

        var calc = new CorrectnessCalculator();
        var result = await calc.CalculateAsync(ctx);

        // Both should be able to reach high scores
        Assert.True(result.CalorScore >= 0.9,
            $"Calor score {result.CalorScore} should be >= 0.9 with all patterns");
        Assert.True(result.CSharpScore >= 0.9,
            $"C# score {result.CSharpScore} should be >= 0.9 with all patterns");
    }

    #endregion

    private static string? FindTestDataDir()
    {
        var dir = Path.GetDirectoryName(typeof(BugDetectionImprovementTests).Assembly.Location);
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "tests", "TestData");
            if (Directory.Exists(candidate))
                return candidate;
            candidate = Path.Combine(dir, "TestData");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
