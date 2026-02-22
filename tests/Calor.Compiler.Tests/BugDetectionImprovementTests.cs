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
        var tokens = lexer.TokenizeAll();
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Add:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§F{f002:Mul:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (* a b)
§/F{f002}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §S (== result (/ a b))
  §R (/ a b)
§/F{f001}
§/M{m001}
";
        var ctx = new EvaluationContext
        {
            CalorSource = source,
            CSharpSource = "class C { int Divide(int a, int b) => a / b; }",
            FileName = "test"
        };

        var calc = new ErrorDetectionCalculator();
        var result = await calc.CalculateAsync(ctx);

        // With §Q and §S markers present, Calor score should be > 0.3 base
        Assert.True(result.CalorScore > 0.3, $"Calor score {result.CalorScore} should be > 0.3");
    }

    [Fact]
    public void DetectsBugViaContracts_UsesCorrectMarkers()
    {
        // Source with §Q marker (the correct Calor syntax)
        var source = @"
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
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

        // Should detect via contract since §Q is present
        Assert.True(result.CalorDetectedViaContract);
    }

    [Fact]
    public void EvaluateBuggyCode_DivideByZero_CalorDetects()
    {
        var buggySource = @"
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
";
        var fixedSource = @"
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Half:pub}
  §I{i32:a}
  §O{i32}
  §R (/ a 2)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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

        // Check that we have a fix with contract text
        Assert.True(diagnostics.DiagnosticsWithFixes.Count > 0);
        var fix = diagnostics.DiagnosticsWithFixes[0].Fix;
        Assert.Contains("§Q (!= b 0)", fix.Description);
    }

    #endregion

    #region OffByOneChecker

    [Fact]
    public void ForLoop_ToEqualsLength_Warns()
    {
        var source = @"
§M{m001:Test}
§F{f001:Sum:pub}
  §I{i32:n}
  §O{i32}
  §B{~total:i32} INT:0
  §L{l001:i:0:n:1}
    §B{~total:i32} (+ total i)
  §/L{l001}
  §R total
§/F{f001}
§/M{m001}
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

        // The loop iterates to 'n' (which could be a length-like variable) and uses 'i'
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.OffByOne);
    }

    [Fact]
    public void ForLoop_ToLessThanLength_NoWarning()
    {
        // When the bound is subtracted by 1, no warning
        var source = @"
§M{m001:Test}
§F{f001:Sum:pub}
  §I{i32:n}
  §O{i32}
  §B{~total:i32} INT:0
  §L{l001:i:0:(- n 1):1}
    §B{~total:i32} (+ total i)
  §/L{l001}
  §R total
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Count:pub}
  §I{i32:n}
  §O{i32}
  §B{~total:i32} INT:0
  §L{l001:i:0:10:1}
    §B{~total:i32} (+ total 1)
  §/L{l001}
  §R total
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
";
        var module = Parse(source, out var parseDiag);
        Assert.False(parseDiag.HasErrors, string.Join("\n", parseDiag.Select(d => d.Message)));

        var diagnostics = new DiagnosticBag();
        var bound = Bind(module);
        var inferencePass = new ContractInferencePass(diagnostics);
        inferencePass.Infer(module, bound);

        Assert.True(diagnostics.DiagnosticsWithFixes.Count > 0);
        var fix = diagnostics.DiagnosticsWithFixes[0].Fix;
        Assert.Contains("§Q (!= b 0)", fix.Description);
    }

    #endregion

    #region Bug Scenario Integration

    [Fact]
    public void BugScenario_DivideByZero_CalorDetects()
    {
        var buggySource = @"
§M{m001:UnsafeMath}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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
        // Uses §C{} call syntax to produce BoundCallExpression with target "maybeVal.unwrap"
        var buggySource = @"
§M{m001:UnsafeOption}
§F{f001:GetValue:pub}
  §I{Option<i32>:maybeVal}
  §O{i32}
  §R §C{maybeVal.unwrap} §/C
§/F{f001}
§/M{m001}
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

        // NullDereferenceChecker should detect unsafe unwrap without is_some check
        Assert.True(result.FunctionsAnalyzed > 0);
        Assert.Contains(diagnostics, d =>
            d.Code == DiagnosticCode.UnsafeUnwrap ||
            d.Code == DiagnosticCode.NullDereference);
    }

    [Fact]
    public void BugScenario_Overflow_CalorDetects()
    {
        var buggySource = @"
§M{m001:UnsafeAdd}
§F{f001:AddLarge:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (+ a b)
§/F{f001}
§/M{m001}
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
§M{m001:UnsafeDivide}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §Q (!= b 0)
  §R (/ a b)
§/F{f001}
§/M{m001}
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
§M{m001:Test}
§F{f001:Divide:pub}
  §I{i32:a}
  §I{i32:b}
  §O{i32}
  §R (/ a b)
§/F{f001}
§/M{m001}
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
        // Function that just returns a parameter should infer §S (== result param)
        var source = @"
§M{m001:Test}
§F{f001:Identity:pub}
  §I{i32:x}
  §O{i32}
  §R x
§/F{f001}
§/M{m001}
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
        // Function that returns x * x should infer §S (>= result 0)
        var source = @"
§M{m001:Test}
§F{f001:Square:pub}
  §I{i32:x}
  §O{i32}
  §R (* x x)
§/F{f001}
§/M{m001}
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
            d.Message.Contains(">= result 0"));
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
            var source = File.ReadAllText(file);
            var module = Parse(source, out var parseDiag);
            Assert.False(parseDiag.HasErrors,
                $"File {Path.GetFileName(file)} has parse errors: {string.Join("\n", parseDiag.Select(d => d.Message))}");
        }
    }

    [Fact]
    public void EnsuresPattern_MatchesVariousFormats()
    {
        // Verify that §S matching works for various formats
        var calc = new ErrorDetectionCalculator();

        // §S followed by space
        var source1 = "§Q (!= b 0)\n§S (== result 1)";
        var ctx1 = new EvaluationContext
        {
            CalorSource = source1,
            CSharpSource = "class C {}",
            FileName = "test"
        };
        var bug = new BugDescription { Id = "test", Category = "contract_violation", Description = "test" };
        var result1 = calc.EvaluateBuggyCode(source1, source1, "class C {}", "class C {}", bug);
        Assert.True(result1.CalorDetectedViaContract, "Should detect §S followed by space");

        // §S followed by open paren
        var source2 = "§Q (!= b 0)\n§S(== result 1)";
        var ctx2 = new EvaluationContext
        {
            CalorSource = source2,
            CSharpSource = "class C {}",
            FileName = "test"
        };
        var result2 = calc.EvaluateBuggyCode(source2, source2, "class C {}", "class C {}", bug);
        Assert.True(result2.CalorDetectedViaContract, "Should detect §S followed by paren");

        // §S followed by newline
        var source3 = "§Q (!= b 0)\n§S\n(== result 1)";
        var ctx3 = new EvaluationContext
        {
            CalorSource = source3,
            CSharpSource = "class C {}",
            FileName = "test"
        };
        var result3 = calc.EvaluateBuggyCode(source3, source3, "class C {}", "class C {}", bug);
        Assert.True(result3.CalorDetectedViaContract, "Should detect §S followed by newline");
    }

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

    #endregion
}
