using Calor.Compiler.Ast;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// A suggestion to refine a parameter's type based on usage analysis.
/// </summary>
public sealed class TypeSuggestion
{
    /// <summary>Function containing the parameter.</summary>
    public required string FunctionId { get; init; }

    /// <summary>Parameter name to refine.</summary>
    public required string ParameterName { get; init; }

    /// <summary>Current type name.</summary>
    public required string CurrentType { get; init; }

    /// <summary>Suggested refinement predicate (Calor expression).</summary>
    public required string SuggestedPredicate { get; init; }

    /// <summary>Reason for the suggestion.</summary>
    public required string Reason { get; init; }

    /// <summary>Confidence: "high", "medium", "low".</summary>
    public required string Confidence { get; init; }

    /// <summary>Calor syntax for the suggested inline refinement.</summary>
    public required string CalorSyntax { get; init; }
}

/// <summary>
/// Walks function bodies to detect usage patterns and suggest refined types for parameters.
/// Follows the PreconditionSuggester pattern: check how parameters are used,
/// then recommend refinement types based on usage.
/// </summary>
public sealed class TypeSuggester
{
    /// <summary>
    /// Analyze a module and suggest refined types for parameters.
    /// </summary>
    public List<TypeSuggestion> Suggest(ModuleNode module)
    {
        var suggestions = new List<TypeSuggestion>();

        foreach (var func in module.Functions)
        {
            suggestions.AddRange(SuggestForFunction(func));
        }

        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                suggestions.AddRange(SuggestForMethod(method));
            }
        }

        return suggestions;
    }

    private List<TypeSuggestion> SuggestForFunction(FunctionNode func)
    {
        var suggestions = new List<TypeSuggestion>();
        var paramNames = new HashSet<string>(func.Parameters.Select(p => p.Name));
        var paramTypes = func.Parameters.ToDictionary(p => p.Name, p => p.TypeName);

        // Skip parameters that already have refinements
        var refinedParams = new HashSet<string>(
            func.Parameters.Where(p => p.InlineRefinement != null).Select(p => p.Name));

        // Skip parameters that already have preconditions
        var guardedParams = new HashSet<string>();
        foreach (var pre in func.Preconditions)
        {
            CollectGuardedParams(pre.Condition, guardedParams);
        }

        var usagePatterns = new Dictionary<string, HashSet<string>>();

        foreach (var stmt in func.Body)
        {
            AnalyzeStatement(stmt, paramNames, usagePatterns);
        }

        foreach (var (paramName, patterns) in usagePatterns)
        {
            if (refinedParams.Contains(paramName) || guardedParams.Contains(paramName))
                continue;

            if (!paramTypes.TryGetValue(paramName, out var typeName))
                continue;

            suggestions.AddRange(GenerateSuggestions(func.Id, paramName, typeName, patterns));
        }

        return suggestions;
    }

    private List<TypeSuggestion> SuggestForMethod(MethodNode method)
    {
        var suggestions = new List<TypeSuggestion>();
        var paramNames = new HashSet<string>(method.Parameters.Select(p => p.Name));
        var paramTypes = method.Parameters.ToDictionary(p => p.Name, p => p.TypeName);

        var refinedParams = new HashSet<string>(
            method.Parameters.Where(p => p.InlineRefinement != null).Select(p => p.Name));

        var guardedParams = new HashSet<string>();
        foreach (var pre in method.Preconditions)
        {
            CollectGuardedParams(pre.Condition, guardedParams);
        }

        var usagePatterns = new Dictionary<string, HashSet<string>>();

        foreach (var stmt in method.Body)
        {
            AnalyzeStatement(stmt, paramNames, usagePatterns);
        }

        foreach (var (paramName, patterns) in usagePatterns)
        {
            if (refinedParams.Contains(paramName) || guardedParams.Contains(paramName))
                continue;

            if (!paramTypes.TryGetValue(paramName, out var typeName))
                continue;

            suggestions.AddRange(GenerateSuggestions(method.Id, paramName, typeName, patterns));
        }

        return suggestions;
    }

    private static void AnalyzeStatement(
        StatementNode stmt,
        HashSet<string> paramNames,
        Dictionary<string, HashSet<string>> patterns)
    {
        switch (stmt)
        {
            case CallStatementNode call:
                foreach (var arg in call.Arguments)
                    AnalyzeExpression(arg, paramNames, patterns);
                break;

            case ReturnStatementNode ret:
                if (ret.Expression != null)
                    AnalyzeExpression(ret.Expression, paramNames, patterns);
                break;

            case BindStatementNode bind:
                if (bind.Initializer != null)
                    AnalyzeExpression(bind.Initializer, paramNames, patterns);
                break;

            case IfStatementNode ifStmt:
                AnalyzeExpression(ifStmt.Condition, paramNames, patterns);
                foreach (var s in ifStmt.ThenBody) AnalyzeStatement(s, paramNames, patterns);
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody) AnalyzeStatement(s, paramNames, patterns);
                break;

            case ForStatementNode forStmt:
                foreach (var s in forStmt.Body) AnalyzeStatement(s, paramNames, patterns);
                break;

            case WhileStatementNode whileStmt:
                AnalyzeExpression(whileStmt.Condition, paramNames, patterns);
                foreach (var s in whileStmt.Body) AnalyzeStatement(s, paramNames, patterns);
                break;
        }
    }

    private static void AnalyzeExpression(
        ExpressionNode expr,
        HashSet<string> paramNames,
        Dictionary<string, HashSet<string>> patterns)
    {
        switch (expr)
        {
            case BinaryOperationNode binOp:
                // Check if a parameter is used as a divisor
                if (binOp.Operator is BinaryOperator.Divide or BinaryOperator.Modulo)
                {
                    if (binOp.Right is ReferenceNode divisorRef && paramNames.Contains(divisorRef.Name))
                    {
                        AddPattern(patterns, divisorRef.Name, "used_as_divisor");
                    }
                }

                // Check if a parameter is compared with >= 0
                if (binOp.Operator == BinaryOperator.GreaterOrEqual)
                {
                    if (binOp.Left is ReferenceNode leftRef && paramNames.Contains(leftRef.Name)
                        && binOp.Right is IntLiteralNode { Value: 0 })
                    {
                        AddPattern(patterns, leftRef.Name, "compared_geq_zero");
                    }
                }

                // Check if a parameter is compared with > 0
                if (binOp.Operator == BinaryOperator.GreaterThan)
                {
                    if (binOp.Left is ReferenceNode gtRef && paramNames.Contains(gtRef.Name)
                        && binOp.Right is IntLiteralNode { Value: 0 })
                    {
                        AddPattern(patterns, gtRef.Name, "compared_gt_zero");
                    }
                }

                AnalyzeExpression(binOp.Left, paramNames, patterns);
                AnalyzeExpression(binOp.Right, paramNames, patterns);
                break;

            case ArrayAccessNode arrayAccess:
                // Check if a parameter is used as an array index
                if (arrayAccess.Index is ReferenceNode indexRef && paramNames.Contains(indexRef.Name))
                {
                    AddPattern(patterns, indexRef.Name, "used_as_index");
                }
                AnalyzeExpression(arrayAccess.Array, paramNames, patterns);
                AnalyzeExpression(arrayAccess.Index, paramNames, patterns);
                break;

            case UnaryOperationNode unaryOp:
                AnalyzeExpression(unaryOp.Operand, paramNames, patterns);
                break;

            case ConditionalExpressionNode cond:
                AnalyzeExpression(cond.Condition, paramNames, patterns);
                AnalyzeExpression(cond.WhenTrue, paramNames, patterns);
                AnalyzeExpression(cond.WhenFalse, paramNames, patterns);
                break;
        }
    }

    private static void AddPattern(Dictionary<string, HashSet<string>> patterns, string param, string pattern)
    {
        if (!patterns.TryGetValue(param, out var set))
        {
            set = new HashSet<string>();
            patterns[param] = set;
        }
        set.Add(pattern);
    }

    private static List<TypeSuggestion> GenerateSuggestions(
        string functionId, string paramName, string typeName, HashSet<string> patterns)
    {
        var suggestions = new List<TypeSuggestion>();

        if (patterns.Contains("used_as_divisor"))
        {
            suggestions.Add(new TypeSuggestion
            {
                FunctionId = functionId,
                ParameterName = paramName,
                CurrentType = typeName,
                SuggestedPredicate = $"(!= # INT:0)",
                Reason = $"'{paramName}' is used as a divisor without a non-zero constraint",
                Confidence = "high",
                CalorSyntax = $"§I{{{typeName}:{paramName}}} | (!= # INT:0)"
            });
        }

        if (patterns.Contains("used_as_index"))
        {
            suggestions.Add(new TypeSuggestion
            {
                FunctionId = functionId,
                ParameterName = paramName,
                CurrentType = typeName,
                SuggestedPredicate = $"(>= # INT:0)",
                Reason = $"'{paramName}' is used as an array index without a non-negative constraint",
                Confidence = "high",
                CalorSyntax = $"§I{{{typeName}:{paramName}}} | (>= # INT:0)"
            });
        }

        if (patterns.Contains("compared_geq_zero") && !patterns.Contains("used_as_index"))
        {
            suggestions.Add(new TypeSuggestion
            {
                FunctionId = functionId,
                ParameterName = paramName,
                CurrentType = typeName,
                SuggestedPredicate = $"(>= # INT:0)",
                Reason = $"'{paramName}' is compared with >= 0, suggesting a non-negative constraint",
                Confidence = "medium",
                CalorSyntax = $"§I{{{typeName}:{paramName}}} | (>= # INT:0)"
            });
        }

        if (patterns.Contains("compared_gt_zero"))
        {
            suggestions.Add(new TypeSuggestion
            {
                FunctionId = functionId,
                ParameterName = paramName,
                CurrentType = typeName,
                SuggestedPredicate = $"(> # INT:0)",
                Reason = $"'{paramName}' is compared with > 0, suggesting a positive constraint",
                Confidence = "medium",
                CalorSyntax = $"§I{{{typeName}:{paramName}}} | (> # INT:0)"
            });
        }

        return suggestions;
    }

    private static void CollectGuardedParams(ExpressionNode condition, HashSet<string> guarded)
    {
        switch (condition)
        {
            case BinaryOperationNode binOp:
                if (binOp.Left is ReferenceNode refNode)
                    guarded.Add(refNode.Name);
                if (binOp.Right is ReferenceNode rightRef)
                    guarded.Add(rightRef.Name);
                if (binOp.Operator is BinaryOperator.And or BinaryOperator.Or)
                {
                    CollectGuardedParams(binOp.Left, guarded);
                    CollectGuardedParams(binOp.Right, guarded);
                }
                break;
        }
    }
}
