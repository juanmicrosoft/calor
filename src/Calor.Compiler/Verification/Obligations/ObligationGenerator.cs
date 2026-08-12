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
        GenerateReturnObligation(func.Output, func.Id);

        // 2. Proof obligations from the complete nested body
        GenerateProofObligations(func.Body, func.Id);
        GenerateSubtypeObligations(func.Body, func.Parameters, func.Id);

        // 3. Index bounds obligations from body
        GenerateIndexBoundsForBody(func.Body, func.Parameters, func.Id, func.Visibility);
    }

    private void GenerateForMethod(MethodNode method, ClassDefinitionNode cls)
    {
        foreach (var param in method.Parameters)
        {
            GenerateParameterObligation(param, method.Id, method.Visibility);
        }
        GenerateReturnObligation(method.Output, method.Id);

        GenerateProofObligations(method.Body, method.Id);
        GenerateSubtypeObligations(method.Body, method.Parameters, method.Id);

        GenerateIndexBoundsForBody(method.Body, method.Parameters, method.Id, method.Visibility);
    }

    private void GenerateReturnObligation(OutputNode? output, string functionId)
    {
        if (output is null
            || !_refinementTypes.TryGetValue(output.TypeName, out var refinementType))
        {
            return;
        }

        var obligation = _tracker.Add(
            ObligationKind.RefinementReturn,
            functionId,
            $"Return value must satisfy refinement type '{refinementType.Name}'",
            refinementType.Predicate,
            output.Span);
        obligation.ParameterName = "result";
    }

    private void GenerateProofObligations(
        IReadOnlyList<StatementNode> body,
        string functionId)
    {
        foreach (var proof in body
                     .SelectMany(DescendantsAndSelf)
                     .OfType<ProofObligationNode>())
        {
            var obligation = _tracker.Add(
                ObligationKind.ProofObligation,
                functionId,
                proof.Description ?? $"Proof obligation {proof.Id}",
                proof.Condition,
                proof.Span);
            obligation.SourceProofId = proof.Id;
        }
    }

    private static IEnumerable<AstNode> DescendantsAndSelf(AstNode node)
    {
        yield return node;
        foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }

    }

    private void GenerateSubtypeObligations(
        IReadOnlyList<StatementNode> body,
        IReadOnlyList<ParameterNode> parameters,
        string functionId)
    {
        var nodes = body.SelectMany(DescendantsAndSelf).ToArray();
        var refinementByVariable = new Dictionary<string, RefinementTypeNode>(
            StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            if (_refinementTypes.TryGetValue(parameter.TypeName, out var refinementType))
                refinementByVariable[parameter.Name] = refinementType;
        }

        foreach (var bind in nodes.OfType<BindStatementNode>())
        {
            if (bind.TypeName is null
                || bind.Initializer is null
                || !_refinementTypes.TryGetValue(bind.TypeName, out var refinementType))
            {
                continue;
            }

            var condition = FactCollector.SubstituteSelfRefStatic(
                refinementType.Predicate,
                bind.Name);
            var obligation = _tracker.Add(
                ObligationKind.Subtype,
                functionId,
                $"Value assigned to '{bind.Name}' must satisfy refinement type '{refinementType.Name}'",
                condition,
                bind.Span);
            obligation.ParameterName = bind.Name;
            refinementByVariable[bind.Name] = refinementType;
        }

        foreach (var assignment in nodes.OfType<AssignmentStatementNode>())
        {
            if (assignment.Target is ReferenceNode target
                && refinementByVariable.TryGetValue(target.Name, out var refinementType))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    target.Name,
                    refinementType,
                    assignment.Span);
            }
        }

        foreach (var assignment in nodes.OfType<CompoundAssignmentStatementNode>())
        {
            if (assignment.Target is ReferenceNode target
                && refinementByVariable.TryGetValue(target.Name, out var refinementType))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    target.Name,
                    refinementType,
                    assignment.Span);
            }
        }
    }

    private void AddAssignmentSubtypeObligation(
        string functionId,
        string variableName,
        RefinementTypeNode refinementType,
        TextSpan span)
    {
        var condition = FactCollector.SubstituteSelfRefStatic(
            refinementType.Predicate,
            variableName);
        var obligation = _tracker.Add(
            ObligationKind.Subtype,
            functionId,
            $"Assignment to '{variableName}' must preserve refinement type '{refinementType.Name}'",
            condition,
            span);
        obligation.ParameterName = variableName;
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

        foreach (var access in body
                     .SelectMany(DescendantsAndSelf)
                     .OfType<ArrayAccessNode>())
        {
            GenerateIndexBounds(access, indexedParams, functionId, visibility);
        }
    }

    private void GenerateIndexBounds(
        ArrayAccessNode access,
        Dictionary<string, (ParameterNode Param, IndexedTypeNode IType)> indexedParams,
        string functionId,
        Visibility visibility)
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
        else
        {
            var dummySpan = new TextSpan(0, 0, 1, 1);
            var zeroLit = new IntLiteralNode(dummySpan, 0);
            var geZero = new BinaryOperationNode(
                dummySpan,
                BinaryOperator.GreaterOrEqual,
                access.Index,
                zeroLit);
            var ltLength = new BinaryOperationNode(
                dummySpan,
                BinaryOperator.LessThan,
                access.Index,
                new ArrayLengthNode(dummySpan, access.Array));
            var boundsCheck = new BinaryOperationNode(
                dummySpan,
                BinaryOperator.And,
                geZero,
                ltLength);
            _tracker.Add(
                ObligationKind.IndexBounds,
                functionId,
                "Index access must be within the runtime collection bounds",
                boundsCheck,
                access.Span);
        }
    }

    private static string? GetReferenceName(ExpressionNode expr)
    {
        if (expr is ReferenceNode refNode)
            return refNode.Name;
        return null;
    }
}
