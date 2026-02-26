using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;

namespace Calor.Compiler.Verification.Obligations;

/// <summary>
/// Walks the AST and generates obligations for refinement types, proof obligations,
/// and index bounds checks on indexed types.
/// </summary>
public sealed class ObligationGenerator
{
    private readonly ObligationTracker _tracker;

    /// <summary>
    /// Refinement type definitions indexed by name, for looking up predicates.
    /// </summary>
    private readonly Dictionary<string, RefinementTypeNode> _refinementTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Indexed type definitions indexed by name, for looking up size parameters.
    /// </summary>
    private readonly Dictionary<string, IndexedTypeNode> _indexedTypes = new(StringComparer.Ordinal);

    public ObligationGenerator(ObligationTracker tracker)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    /// <summary>
    /// Generates obligations for an entire module.
    /// </summary>
    public void Generate(ModuleNode module)
    {
        // Register refinement type definitions
        foreach (var rtype in module.RefinementTypes)
        {
            _refinementTypes[rtype.Name] = rtype;
        }

        // Register indexed type definitions
        foreach (var itype in module.IndexedTypes)
        {
            _indexedTypes[itype.Name] = itype;
        }

        // Generate obligations for each function
        foreach (var func in module.Functions)
        {
            GenerateForFunction(func);
        }

        // Generate for methods inside classes
        foreach (var cls in module.Classes)
        {
            foreach (var method in cls.Methods)
            {
                GenerateForMethod(method, cls);
            }
        }
    }

    private void GenerateForFunction(FunctionNode func)
    {
        // 1. Refined parameter entry obligations
        foreach (var param in func.Parameters)
        {
            GenerateParameterObligation(param, func.Id, func.Visibility);
        }

        // 2. Proof obligations from body
        foreach (var stmt in func.Body)
        {
            if (stmt is ProofObligationNode proof)
            {
                var obl = _tracker.Add(
                    ObligationKind.ProofObligation,
                    func.Id,
                    proof.Description ?? $"Proof obligation {proof.Id}",
                    proof.Condition,
                    proof.Span);
                obl.SourceProofId = proof.Id;
            }
        }

        // 3. Index bounds obligations from body
        GenerateIndexBoundsForBody(func.Body, func.Parameters, func.Id, func.Visibility);
    }

    private void GenerateForMethod(MethodNode method, ClassDefinitionNode cls)
    {
        foreach (var param in method.Parameters)
        {
            GenerateParameterObligation(param, method.Id, method.Visibility);
        }

        foreach (var stmt in method.Body)
        {
            if (stmt is ProofObligationNode proof)
            {
                var obl = _tracker.Add(
                    ObligationKind.ProofObligation,
                    method.Id,
                    proof.Description ?? $"Proof obligation {proof.Id}",
                    proof.Condition,
                    proof.Span);
                obl.SourceProofId = proof.Id;
            }
        }

        GenerateIndexBoundsForBody(method.Body, method.Parameters, method.Id, method.Visibility);
    }

    private void GenerateParameterObligation(ParameterNode param, string functionId, Visibility visibility)
    {
        if (param.InlineRefinement != null)
        {
            var obl = _tracker.Add(
                ObligationKind.RefinementEntry,
                functionId,
                $"Parameter '{param.Name}' must satisfy inline refinement",
                param.InlineRefinement.Predicate,
                param.Span);
            obl.ParameterName = param.Name;

            // Public functions get boundary status — can't statically verify caller behavior
            if (visibility == Visibility.Public)
            {
                obl.Status = ObligationStatus.Boundary;
                obl.SuggestedFix = $"Add runtime guard: if (!({param.Name} satisfies predicate)) throw";
            }
        }

        // Check if parameter type name matches a known refinement type
        if (_refinementTypes.TryGetValue(param.TypeName, out var rtype))
        {
            var obl = _tracker.Add(
                ObligationKind.RefinementEntry,
                functionId,
                $"Parameter '{param.Name}' must satisfy refinement type '{rtype.Name}'",
                rtype.Predicate,
                param.Span);
            obl.ParameterName = param.Name;

            if (visibility == Visibility.Public)
            {
                obl.Status = ObligationStatus.Boundary;
                obl.SuggestedFix = $"Add runtime guard for '{rtype.Name}' constraint on '{param.Name}'";
            }
        }
    }

    /// <summary>
    /// Scans statements for ArrayAccessNode on indexed-typed parameters and
    /// generates IndexBounds obligations with condition: (&amp;&amp; (&gt;= index INT:0) (&lt; index sizeParam)).
    /// </summary>
    private void GenerateIndexBoundsForBody(
        IReadOnlyList<StatementNode> body,
        IReadOnlyList<ParameterNode> parameters,
        string functionId,
        Visibility visibility)
    {
        if (_indexedTypes.Count == 0) return;

        // Build lookup: parameter name -> indexed type (if the parameter's type matches)
        var indexedParams = new Dictionary<string, (ParameterNode Param, IndexedTypeNode IType)>(StringComparer.Ordinal);
        foreach (var param in parameters)
        {
            // Match parameter type name against indexed type names
            // Support both exact name match (e.g., "SizedList") and generic syntax (e.g., "SizedList<i32>")
            var baseTypeName = param.TypeName;
            var genericIdx = baseTypeName.IndexOf('<');
            if (genericIdx > 0)
                baseTypeName = baseTypeName.Substring(0, genericIdx);

            if (_indexedTypes.TryGetValue(baseTypeName, out var itype))
            {
                indexedParams[param.Name] = (param, itype);
            }
        }

        if (indexedParams.Count == 0) return;

        ScanStatementsForIndexBounds(body, indexedParams, functionId, visibility);
    }

    private void ScanStatementsForIndexBounds(
        IReadOnlyList<StatementNode> statements,
        Dictionary<string, (ParameterNode Param, IndexedTypeNode IType)> indexedParams,
        string functionId,
        Visibility visibility)
    {
        foreach (var stmt in statements)
        {
            ScanStatementForIndexBounds(stmt, indexedParams, functionId, visibility);
        }
    }

    private void ScanStatementForIndexBounds(
        StatementNode stmt,
        Dictionary<string, (ParameterNode Param, IndexedTypeNode IType)> indexedParams,
        string functionId,
        Visibility visibility)
    {
        switch (stmt)
        {
            case ReturnStatementNode ret when ret.Expression != null:
                ScanExpressionForIndexBounds(ret.Expression, indexedParams, functionId, visibility);
                break;
            case ForStatementNode forStmt:
                ScanStatementsForIndexBounds(forStmt.Body, indexedParams, functionId, visibility);
                break;
            case WhileStatementNode whileStmt:
                ScanStatementsForIndexBounds(whileStmt.Body, indexedParams, functionId, visibility);
                break;
            case DoWhileStatementNode doWhile:
                ScanStatementsForIndexBounds(doWhile.Body, indexedParams, functionId, visibility);
                break;
            case ForeachStatementNode foreachStmt:
                ScanStatementsForIndexBounds(foreachStmt.Body, indexedParams, functionId, visibility);
                break;
            case IfStatementNode ifStmt:
                ScanStatementsForIndexBounds(ifStmt.ThenBody, indexedParams, functionId, visibility);
                foreach (var elseIf in ifStmt.ElseIfClauses)
                    ScanStatementsForIndexBounds(elseIf.Body, indexedParams, functionId, visibility);
                if (ifStmt.ElseBody != null)
                    ScanStatementsForIndexBounds(ifStmt.ElseBody, indexedParams, functionId, visibility);
                break;
            case BindStatementNode bind when bind.Initializer != null:
                ScanExpressionForIndexBounds(bind.Initializer, indexedParams, functionId, visibility);
                break;
            case AssignmentStatementNode assign:
                ScanExpressionForIndexBounds(assign.Value, indexedParams, functionId, visibility);
                break;
            case CompoundAssignmentStatementNode compAssign:
                ScanExpressionForIndexBounds(compAssign.Value, indexedParams, functionId, visibility);
                break;
            case CallStatementNode call:
                foreach (var arg in call.Arguments)
                    ScanExpressionForIndexBounds(arg, indexedParams, functionId, visibility);
                break;
            case ExpressionStatementNode exprStmt:
                ScanExpressionForIndexBounds(exprStmt.Expression, indexedParams, functionId, visibility);
                break;
            case PrintStatementNode print:
                ScanExpressionForIndexBounds(print.Expression, indexedParams, functionId, visibility);
                break;
            case YieldReturnStatementNode yieldRet when yieldRet.Expression != null:
                ScanExpressionForIndexBounds(yieldRet.Expression, indexedParams, functionId, visibility);
                break;
            case TryStatementNode tryStmt:
                ScanStatementsForIndexBounds(tryStmt.TryBody, indexedParams, functionId, visibility);
                foreach (var catchClause in tryStmt.CatchClauses)
                    ScanStatementsForIndexBounds(catchClause.Body, indexedParams, functionId, visibility);
                if (tryStmt.FinallyBody != null)
                    ScanStatementsForIndexBounds(tryStmt.FinallyBody, indexedParams, functionId, visibility);
                break;
            case MatchStatementNode matchStmt:
                foreach (var matchCase in matchStmt.Cases)
                    ScanStatementsForIndexBounds(matchCase.Body, indexedParams, functionId, visibility);
                break;
            case UsingStatementNode usingStmt:
                ScanStatementsForIndexBounds(usingStmt.Body, indexedParams, functionId, visibility);
                break;
        }
    }

    private void ScanExpressionForIndexBounds(
        ExpressionNode expr,
        Dictionary<string, (ParameterNode Param, IndexedTypeNode IType)> indexedParams,
        string functionId,
        Visibility visibility)
    {
        if (expr is ArrayAccessNode access)
        {
            // Check if the array expression references an indexed-typed parameter
            var arrayName = GetReferenceName(access.Array);
            if (arrayName != null && indexedParams.TryGetValue(arrayName, out var info))
            {
                var dummySpan = new TextSpan(0, 0, 1, 1);

                // Build obligation condition: (&& (>= index INT:0) (< index sizeParam))
                var indexExpr = access.Index;
                var zeroLit = new IntLiteralNode(dummySpan, 0);
                var sizeRef = new ReferenceNode(dummySpan, info.IType.SizeParam);

                var geZero = new BinaryOperationNode(dummySpan, BinaryOperator.GreaterOrEqual, indexExpr, zeroLit);
                var ltSize = new BinaryOperationNode(dummySpan, BinaryOperator.LessThan, indexExpr, sizeRef);
                var boundsCheck = new BinaryOperationNode(dummySpan, BinaryOperator.And, geZero, ltSize);

                var obl = _tracker.Add(
                    ObligationKind.IndexBounds,
                    functionId,
                    $"Index access on '{arrayName}' must be within bounds [0, {info.IType.SizeParam})",
                    boundsCheck,
                    access.Span);
                obl.ParameterName = arrayName;

                if (visibility == Visibility.Public)
                {
                    obl.Status = ObligationStatus.Boundary;
                    obl.SuggestedFix = $"Add runtime bounds check before accessing '{arrayName}'";
                }
            }

            // Also scan the index expression itself (it could contain nested array accesses)
            ScanExpressionForIndexBounds(access.Index, indexedParams, functionId, visibility);
        }
        else if (expr is BinaryOperationNode binOp)
        {
            ScanExpressionForIndexBounds(binOp.Left, indexedParams, functionId, visibility);
            ScanExpressionForIndexBounds(binOp.Right, indexedParams, functionId, visibility);
        }
        else if (expr is UnaryOperationNode unOp)
        {
            ScanExpressionForIndexBounds(unOp.Operand, indexedParams, functionId, visibility);
        }
        else if (expr is CallExpressionNode callExpr)
        {
            foreach (var arg in callExpr.Arguments)
                ScanExpressionForIndexBounds(arg, indexedParams, functionId, visibility);
        }
        else if (expr is ConditionalExpressionNode cond)
        {
            ScanExpressionForIndexBounds(cond.Condition, indexedParams, functionId, visibility);
            ScanExpressionForIndexBounds(cond.WhenTrue, indexedParams, functionId, visibility);
            ScanExpressionForIndexBounds(cond.WhenFalse, indexedParams, functionId, visibility);
        }
        else if (expr is MatchExpressionNode matchExpr)
        {
            ScanExpressionForIndexBounds(matchExpr.Target, indexedParams, functionId, visibility);
            foreach (var matchCase in matchExpr.Cases)
            {
                if (matchCase.Body.Count > 0)
                    ScanStatementsForIndexBounds(matchCase.Body, indexedParams, functionId, visibility);
            }
        }
        else if (expr is SomeExpressionNode someExpr)
        {
            ScanExpressionForIndexBounds(someExpr.Value, indexedParams, functionId, visibility);
        }
        else if (expr is OkExpressionNode okExpr)
        {
            ScanExpressionForIndexBounds(okExpr.Value, indexedParams, functionId, visibility);
        }
        else if (expr is ErrExpressionNode errExpr)
        {
            ScanExpressionForIndexBounds(errExpr.Error, indexedParams, functionId, visibility);
        }
        else if (expr is AwaitExpressionNode awaitExpr)
        {
            ScanExpressionForIndexBounds(awaitExpr.Awaited, indexedParams, functionId, visibility);
        }
        else if (expr is NullCoalesceNode nullCoalesce)
        {
            ScanExpressionForIndexBounds(nullCoalesce.Left, indexedParams, functionId, visibility);
            ScanExpressionForIndexBounds(nullCoalesce.Right, indexedParams, functionId, visibility);
        }
    }

    private static string? GetReferenceName(ExpressionNode expr)
    {
        if (expr is ReferenceNode refNode)
            return refNode.Name;
        return null;
    }
}
