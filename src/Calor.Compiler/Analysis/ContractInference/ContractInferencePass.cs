using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using BinaryOperator = Calor.Compiler.Ast.BinaryOperator;

namespace Calor.Compiler.Analysis.ContractInference;

/// <summary>
/// Infers missing contracts by analyzing function bodies.
/// For functions without existing contracts, scans for patterns like
/// division (infers non-zero preconditions) and array access (infers bounds preconditions).
/// </summary>
public sealed class ContractInferencePass
{
    private readonly DiagnosticBag _diagnostics;

    public ContractInferencePass(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Runs contract inference on a bound module, using the AST module
    /// to check for existing contracts.
    /// </summary>
    /// <returns>Number of contracts inferred.</returns>
    public int Infer(ModuleNode astModule, BoundModule boundModule)
    {
        var contractsInferred = 0;

        // Build set of functions that already have contracts
        var functionsWithContracts = new HashSet<TextSpan>();
        foreach (var func in astModule.Functions)
        {
            if (func.HasContracts)
                functionsWithContracts.Add(func.Span);
        }

        foreach (var boundFunc in boundModule.Functions)
        {
            // Only run contract inference on top-level functions (not class members)
            if (boundFunc.MemberKind != BoundMemberKind.TopLevelFunction)
                continue;

            if (functionsWithContracts.Contains(boundFunc.Symbol.DeclarationSpan))
                continue;

            contractsInferred += InferForFunction(boundFunc);
        }

        return contractsInferred;
    }

    private int InferForFunction(BoundFunction function)
    {
        var inferred = 0;

        // Infer non-zero preconditions for divisors
        var divisorParams = FindDivisorParameters(function);
        foreach (var paramName in divisorParams)
        {
            var contractText = $"§Q (!= {paramName} 0)";
            var fix = new SuggestedFix(
                $"Add inferred precondition: {contractText}",
                TextEdit.Insert("", function.Span.Line, 0, $"  {contractText}\n"));

            _diagnostics.ReportWithFix(
                function.Span,
                DiagnosticCode.InferredContract,
                $"Inferred precondition: {contractText} (parameter used as divisor)",
                fix,
                DiagnosticSeverity.Info);

            inferred++;
        }

        // Infer simple postconditions for pure functions with single return
        inferred += InferPostconditions(function);

        return inferred;
    }

    /// <summary>
    /// Infers simple postconditions for functions with a single return expression.
    /// Currently handles identity returns (return value == parameter) and
    /// non-negative returns from absolute-value-like patterns.
    /// </summary>
    private int InferPostconditions(BoundFunction function)
    {
        var inferred = 0;

        // Only infer postconditions for functions with a single return
        var returns = function.Body.OfType<BoundReturnStatement>().ToList();
        if (returns.Count != 1 || returns[0].Expression == null)
            return 0;

        var retExpr = returns[0].Expression!;
        var parameterIds = function.Symbol.Parameters
            .Select(parameter => parameter.Id)
            .ToHashSet();

        // Pattern: function returns a parameter directly → §S (== result paramName)
        if (retExpr is BoundVariableExpression varExpr
            && parameterIds.Contains(varExpr.Variable.Id))
        {
            var contractText = $"§S (== result {varExpr.Variable.Name})";
            var fix = new SuggestedFix(
                $"Add inferred postcondition: {contractText}",
                TextEdit.Insert("", function.Span.Line, 0, $"  {contractText}\n"));

            _diagnostics.ReportWithFix(
                function.Span,
                DiagnosticCode.InferredContract,
                $"Inferred postcondition: {contractText} (function returns parameter directly)",
                fix,
                DiagnosticSeverity.Info);

            inferred++;
        }

        // Pattern: function returns (+ a b) where both are non-negative params → §S (>= result 0)
        // Only if both parameters have non-negative preconditions or are unsigned-like
        if (retExpr is BoundBinaryExpression binExpr &&
            binExpr.Operator == BinaryOperator.Multiply &&
            binExpr.Left is BoundVariableExpression leftVar &&
            binExpr.Right is BoundVariableExpression rightVar &&
            BoundNodeHelpers.SameSymbol(leftVar.Variable, rightVar.Variable) &&
            parameterIds.Contains(leftVar.Variable.Id))
        {
            // x * x is always non-negative for integers
            var contractText = $"§S (>= result 0)";
            var fix = new SuggestedFix(
                $"Add inferred postcondition: {contractText}",
                TextEdit.Insert("", function.Span.Line, 0, $"  {contractText}\n"));

            _diagnostics.ReportWithFix(
                function.Span,
                DiagnosticCode.InferredContract,
                $"Inferred postcondition: {contractText} (square of a value is non-negative)",
                fix,
                DiagnosticSeverity.Info);

            inferred++;
        }

        return inferred;
    }

    /// <summary>
    /// Finds parameter names used as divisors in the function body.
    /// </summary>
    private static HashSet<string> FindDivisorParameters(BoundFunction function)
    {
        var parameterIds = function.Symbol.Parameters
            .Select(parameter => parameter.Id)
            .ToHashSet();
        var divisorParams = new HashSet<string>();

        foreach (var division in BoundNodeHelpers.DescendantsAndSelf(function)
                     .OfType<BoundBinaryExpression>()
                     .Where(binary =>
                         binary.Operator is BinaryOperator.Divide or BinaryOperator.Modulo))
        {
            var divisor = BoundNodeHelpers.GetDivisor(division);
            if (divisor is BoundVariableExpression variable
                && parameterIds.Contains(variable.Variable.Id))
            {
                divisorParams.Add(variable.Variable.Name);
            }
        }

        return divisorParams;
    }
}
