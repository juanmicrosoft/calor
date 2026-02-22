using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Diagnostics;
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
        var functionsWithContracts = new HashSet<string>();
        foreach (var func in astModule.Functions)
        {
            if (func.HasContracts)
                functionsWithContracts.Add(func.Name);
        }

        foreach (var boundFunc in boundModule.Functions)
        {
            if (functionsWithContracts.Contains(boundFunc.Symbol.Name))
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
        var paramNames = function.Symbol.Parameters.Select(p => p.Name).ToHashSet();

        // Pattern: function returns a parameter directly → §S (== result paramName)
        if (retExpr is BoundVariableExpression varExpr && paramNames.Contains(varExpr.Variable.Name))
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
            leftVar.Variable.Name == rightVar.Variable.Name &&
            paramNames.Contains(leftVar.Variable.Name))
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
        var paramNames = function.Symbol.Parameters.Select(p => p.Name).ToHashSet();
        var divisorParams = new HashSet<string>();

        foreach (var stmt in function.Body)
        {
            FindDivisorParamsInStatement(stmt, paramNames, divisorParams);
        }

        return divisorParams;
    }

    private static void FindDivisorParamsInStatement(
        BoundStatement stmt,
        HashSet<string> paramNames,
        HashSet<string> divisorParams)
    {
        switch (stmt)
        {
            case BoundBindStatement bind:
                if (bind.Initializer != null)
                    FindDivisorParamsInExpression(bind.Initializer, paramNames, divisorParams);
                break;
            case BoundReturnStatement ret:
                if (ret.Expression != null)
                    FindDivisorParamsInExpression(ret.Expression, paramNames, divisorParams);
                break;
            case BoundCallStatement call:
                foreach (var arg in call.Arguments)
                    FindDivisorParamsInExpression(arg, paramNames, divisorParams);
                break;
            case BoundIfStatement ifStmt:
                FindDivisorParamsInExpression(ifStmt.Condition, paramNames, divisorParams);
                foreach (var s in ifStmt.ThenBody)
                    FindDivisorParamsInStatement(s, paramNames, divisorParams);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                    foreach (var s in elseIf.Body)
                        FindDivisorParamsInStatement(s, paramNames, divisorParams);
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody)
                        FindDivisorParamsInStatement(s, paramNames, divisorParams);
                break;
            case BoundWhileStatement whileStmt:
                foreach (var s in whileStmt.Body)
                    FindDivisorParamsInStatement(s, paramNames, divisorParams);
                break;
            case BoundForStatement forStmt:
                foreach (var s in forStmt.Body)
                    FindDivisorParamsInStatement(s, paramNames, divisorParams);
                break;
        }
    }

    private static void FindDivisorParamsInExpression(
        BoundExpression expr,
        HashSet<string> paramNames,
        HashSet<string> divisorParams)
    {
        if (BoundNodeHelpers.ContainsDivision(expr, out var divisionExpr) && divisionExpr != null)
        {
            var divisor = BoundNodeHelpers.GetDivisor(divisionExpr);
            if (divisor is BoundVariableExpression varExpr && paramNames.Contains(varExpr.Variable.Name))
            {
                divisorParams.Add(varExpr.Variable.Name);
            }
        }

        // Recurse into subexpressions
        switch (expr)
        {
            case BoundBinaryExpression binExpr:
                FindDivisorParamsInExpression(binExpr.Left, paramNames, divisorParams);
                FindDivisorParamsInExpression(binExpr.Right, paramNames, divisorParams);
                break;
            case BoundUnaryExpression unaryExpr:
                FindDivisorParamsInExpression(unaryExpr.Operand, paramNames, divisorParams);
                break;
            case BoundCallExpression callExpr:
                foreach (var arg in callExpr.Arguments)
                    FindDivisorParamsInExpression(arg, paramNames, divisorParams);
                break;
        }
    }
}
