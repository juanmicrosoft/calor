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
            foreach (var constructor in cls.Constructors)
            {
                GenerateForConstructor(constructor);
            }
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

    private void GenerateForConstructor(ConstructorNode constructor)
    {
        foreach (var parameter in constructor.Parameters)
        {
            GenerateParameterObligation(
                parameter,
                constructor.Id,
                constructor.Visibility);
        }

        GenerateProofObligations(constructor.Body, constructor.Id);
        GenerateSubtypeObligations(
            constructor.Body,
            constructor.Parameters,
            constructor.Id);
        GenerateIndexBoundsForBody(
            constructor.Body,
            constructor.Parameters,
            constructor.Id,
            constructor.Visibility);
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
        var inlineRefinementByVariable = new Dictionary<string, ExpressionNode>(
            StringComparer.Ordinal);
        var ambiguousVariables = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            if (_refinementTypes.TryGetValue(parameter.TypeName, out var refinementType))
                refinementByVariable[parameter.Name] = refinementType;
            else if (parameter.InlineRefinement?.Predicate is { } inlinePredicate)
                inlineRefinementByVariable[parameter.Name] = inlinePredicate;
        }

        foreach (var bind in nodes.OfType<BindStatementNode>())
        {
            if (bind.TypeName is null
                || !_refinementTypes.TryGetValue(bind.TypeName, out var refinementType))
            {
                if (!bind.IsMutable && refinementByVariable.ContainsKey(bind.Name))
                    ambiguousVariables.Add(bind.Name);
                continue;
            }

            var hasEstablishedRefinement =
                refinementByVariable.ContainsKey(bind.Name)
                || inlineRefinementByVariable.ContainsKey(bind.Name);
            if (hasEstablishedRefinement && !bind.IsMutable)
                ambiguousVariables.Add(bind.Name);
            if (!bind.IsMutable || !hasEstablishedRefinement)
                refinementByVariable[bind.Name] = refinementType;

            var condition = FactCollector.SubstituteSelfRefStatic(
                refinementType.Predicate,
                bind.Initializer
                    ?? new ReferenceNode(bind.Span, bind.Name));
            var obligation = _tracker.Add(
                ObligationKind.Subtype,
                functionId,
                $"Value assigned to '{bind.Name}' must satisfy refinement type '{refinementType.Name}'",
                condition,
                bind.Span);
            obligation.ParameterName = bind.Name;
        }

        foreach (var assignment in nodes.OfType<AssignmentStatementNode>())
        {
            if (assignment.Target is ReferenceNode target
                && !ambiguousVariables.Contains(target.Name)
                && refinementByVariable.TryGetValue(target.Name, out var refinementType))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    target.Name,
                    refinementType,
                    assignment.Value,
                    assignment.Span);
            }
            else if (assignment.Target is ReferenceNode inlineTarget
                && !ambiguousVariables.Contains(inlineTarget.Name)
                && inlineRefinementByVariable.TryGetValue(
                    inlineTarget.Name,
                    out var inlinePredicate))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    inlineTarget.Name,
                    inlinePredicate,
                    "inline refinement",
                    assignment.Value,
                    assignment.Span);
            }
        }

        foreach (var assignment in nodes.OfType<CompoundAssignmentStatementNode>())
        {
            var assignedValue = BuildCompoundAssignmentValue(assignment);
            if (assignment.Target is ReferenceNode target
                && !ambiguousVariables.Contains(target.Name)
                && refinementByVariable.TryGetValue(target.Name, out var refinementType))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    target.Name,
                    refinementType,
                    assignedValue,
                    assignment.Span);
            }
            else if (assignment.Target is ReferenceNode inlineTarget
                && !ambiguousVariables.Contains(inlineTarget.Name)
                && inlineRefinementByVariable.TryGetValue(
                    inlineTarget.Name,
                    out var inlinePredicate))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    inlineTarget.Name,
                    inlinePredicate,
                    "inline refinement",
                    assignedValue,
                    assignment.Span);
            }
        }

        foreach (var unary in nodes.OfType<UnaryOperationNode>())
        {
            if (unary.Operator is not (UnaryOperator.PreIncrement
                    or UnaryOperator.PreDecrement
                    or UnaryOperator.PostIncrement
                    or UnaryOperator.PostDecrement)
                || unary.Operand is not ReferenceNode target
                || ambiguousVariables.Contains(target.Name))
            {
                continue;
            }

            var assignedValue = new BinaryOperationNode(
                unary.Span,
                unary.Operator is UnaryOperator.PreIncrement or UnaryOperator.PostIncrement
                    ? BinaryOperator.Add
                    : BinaryOperator.Subtract,
                target,
                new IntLiteralNode(unary.Span, 1));
            if (refinementByVariable.TryGetValue(target.Name, out var refinementType))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    target.Name,
                    refinementType,
                    assignedValue,
                    unary.Span);
            }
            else if (inlineRefinementByVariable.TryGetValue(
                target.Name,
                out var inlinePredicate))
            {
                AddAssignmentSubtypeObligation(
                    functionId,
                    target.Name,
                    inlinePredicate,
                    "inline refinement",
                    assignedValue,
                    unary.Span);
            }
        }
    }

    private void AddAssignmentSubtypeObligation(
        string functionId,
        string variableName,
        RefinementTypeNode refinementType,
        ExpressionNode assignedValue,
        TextSpan span)
        => AddAssignmentSubtypeObligation(
            functionId,
            variableName,
            refinementType.Predicate,
            $"refinement type '{refinementType.Name}'",
            assignedValue,
            span);

    private void AddAssignmentSubtypeObligation(
        string functionId,
        string variableName,
        ExpressionNode predicate,
        string refinementDescription,
        ExpressionNode assignedValue,
        TextSpan span)
    {
        var condition = FactCollector.SubstituteSelfRefStatic(
            predicate,
            assignedValue);
        var obligation = _tracker.Add(
            ObligationKind.Subtype,
            functionId,
            $"Assignment to '{variableName}' must preserve {refinementDescription}",
            condition,
            span);
        obligation.ParameterName = variableName;
    }

    private static ExpressionNode BuildCompoundAssignmentValue(
        CompoundAssignmentStatementNode assignment)
    {
        if (assignment.Operator == CompoundAssignmentOperator.NullCoalesce)
        {
            return new NullCoalesceNode(
                assignment.Span,
                assignment.Target,
                assignment.Value);
        }

        var binaryOperator = assignment.Operator switch
        {
            CompoundAssignmentOperator.Add => BinaryOperator.Add,
            CompoundAssignmentOperator.Subtract => BinaryOperator.Subtract,
            CompoundAssignmentOperator.Multiply => BinaryOperator.Multiply,
            CompoundAssignmentOperator.Divide => BinaryOperator.Divide,
            CompoundAssignmentOperator.Modulo => BinaryOperator.Modulo,
            CompoundAssignmentOperator.BitwiseAnd => BinaryOperator.BitwiseAnd,
            CompoundAssignmentOperator.BitwiseOr => BinaryOperator.BitwiseOr,
            CompoundAssignmentOperator.BitwiseXor => BinaryOperator.BitwiseXor,
            CompoundAssignmentOperator.LeftShift => BinaryOperator.LeftShift,
            CompoundAssignmentOperator.RightShift => BinaryOperator.RightShift,
            _ => throw new ArgumentOutOfRangeException(nameof(assignment))
        };
        return new BinaryOperationNode(
            assignment.Span,
            binaryOperator,
            assignment.Target,
            assignment.Value);
    }

    private void GenerateParameterObligation(ParameterNode param, string functionId, Visibility visibility)
    {
        if (param.InlineRefinement != null)
        {
            var isOut = param.Modifier == ParameterModifier.Out;
            var obl = _tracker.Add(
                isOut ? ObligationKind.Subtype : ObligationKind.RefinementEntry,
                functionId,
                isOut
                    ? $"Out parameter '{param.Name}' must satisfy inline refinement on assignment"
                    : $"Parameter '{param.Name}' must satisfy inline refinement",
                isOut
                    ? FactCollector.SubstituteSelfRefStatic(
                        param.InlineRefinement.Predicate,
                        param.Name)
                    : param.InlineRefinement.Predicate,
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
            var isOut = param.Modifier == ParameterModifier.Out;
            var obl = _tracker.Add(
                isOut ? ObligationKind.Subtype : ObligationKind.RefinementEntry,
                functionId,
                isOut
                    ? $"Out parameter '{param.Name}' must satisfy refinement type '{rtype.Name}' on assignment"
                    : $"Parameter '{param.Name}' must satisfy refinement type '{rtype.Name}'",
                isOut
                    ? FactCollector.SubstituteSelfRefStatic(rtype.Predicate, param.Name)
                    : rtype.Predicate,
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

        // Propagate logical bounds through local aliases. Runtime emission uses
        // the same alias relationship so reads and writes preserve the modeled
        // indexed-type boundary instead of falling back to physical capacity.
        var ambiguousAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bind in body
                     .SelectMany(DescendantsAndSelf)
                     .OfType<BindStatementNode>())
        {
            if (bind.Initializer is ReferenceNode source
                && indexedParams.TryGetValue(source.Name, out var indexedSource))
            {
                if (indexedParams.ContainsKey(bind.Name))
                {
                    ambiguousAliases.Add(bind.Name);
                }
                else
                {
                    indexedParams[bind.Name] = indexedSource;
                }
            }
        }
        foreach (var alias in ambiguousAliases)
            indexedParams.Remove(alias);
        foreach (var lambdaParameterName in body
                     .SelectMany(DescendantsAndSelf)
                     .OfType<LambdaExpressionNode>()
                     .SelectMany(lambda => lambda.Parameters)
                     .Select(parameter => parameter.Name)
                     .Distinct(StringComparer.Ordinal))
        {
            // A lambda parameter shadows any outer indexed variable with the
            // same name. The flat obligation walk cannot safely reuse the
            // outer logical size, so fall back to runtime collection bounds.
            indexedParams.Remove(lambdaParameterName);
        }

        foreach (var access in body
                     .SelectMany(DescendantsAndSelf)
                     .OfType<ArrayAccessNode>())
        {
            GenerateIndexBounds(access, indexedParams, functionId, visibility);
        }

        foreach (var write in body
                     .SelectMany(DescendantsAndSelf)
                     .OfType<CollectionSetIndexNode>())
        {
            GenerateIndexBounds(
                new ArrayAccessNode(
                    write.Span,
                    new ReferenceNode(write.Span, write.CollectionName),
                    write.Index),
                indexedParams,
                functionId,
                visibility);
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
