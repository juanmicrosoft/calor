using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;

namespace Calor.Compiler.Verification;

/// <summary>
/// Verifies contracts (preconditions and postconditions) in Calor code.
/// Currently performs semantic validation; future versions may include
/// static verification using SMT solvers.
/// </summary>
public sealed class ContractVerifier
{
    private readonly DiagnosticBag _diagnostics;

    public ContractVerifier(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Indexed type size parameter names, valid as identifiers in contracts.
    /// </summary>
    private readonly HashSet<string> _indexedTypeSizeParams = new(StringComparer.Ordinal);

    /// <summary>
    /// Verifies all contracts in a module.
    /// </summary>
    public void Verify(ModuleNode module)
    {
        // Collect indexed type size parameter names
        foreach (var itype in module.IndexedTypes)
        {
            _indexedTypeSizeParams.Add(itype.SizeParam);
        }

        foreach (var function in module.Functions)
        {
            VerifyFunction(function);
        }
    }

    /// <summary>
    /// Verifies contracts in a single function.
    /// </summary>
    public void VerifyFunction(FunctionNode function)
    {
        // Verify preconditions
        foreach (var requires in function.Preconditions)
        {
            VerifyPrecondition(requires, function);
        }

        // Verify postconditions
        foreach (var ensures in function.Postconditions)
        {
            VerifyPostcondition(ensures, function);
        }
    }

    private void VerifyPrecondition(RequiresNode requires, FunctionNode function)
    {
        // Verify that the condition is a boolean expression
        var conditionType = InferExpressionType(requires.Condition);
        if (conditionType != PrimitiveType.Bool)
        {
            _diagnostics.Report(
                requires.Span,
                DiagnosticCode.TypeMismatch,
                $"Precondition must be a boolean expression, got {conditionType?.Name ?? "unknown"}");
        }

        // Verify quantifier variable types
        VerifyQuantifierTypes(requires.Condition);

        // Verify that the condition only references parameters, indexed type size params, and constants
        var referencedNames = CollectReferences(requires.Condition);
        var parameterNames = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var name in referencedNames)
        {
            if (!parameterNames.Contains(name) && !_indexedTypeSizeParams.Contains(name))
            {
                _diagnostics.Report(
                    requires.Span,
                    DiagnosticCode.UndefinedReference,
                    $"Precondition can only reference parameters. Unknown identifier: '{name}'");
            }
        }
    }

    private void VerifyPostcondition(EnsuresNode ensures, FunctionNode function)
    {
        // Verify that the condition is a boolean expression
        var conditionType = InferExpressionType(ensures.Condition);
        if (conditionType != PrimitiveType.Bool)
        {
            _diagnostics.Report(
                ensures.Span,
                DiagnosticCode.TypeMismatch,
                $"Postcondition must be a boolean expression, got {conditionType?.Name ?? "unknown"}");
        }

        // Verify quantifier variable types
        VerifyQuantifierTypes(ensures.Condition);

        // Verify that the condition only references parameters, 'result', and constants
        var referencedNames = CollectReferences(ensures.Condition);
        var validNames = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        validNames.Add("result"); // Special identifier for return value

        var hasReturnValue = ReturnShape.DeclaresValueOutput(function.Output);

        foreach (var name in referencedNames)
        {
            if (name == "result" && !hasReturnValue)
            {
                _diagnostics.Report(
                    ensures.Span,
                    DiagnosticCode.InvalidReference,
                    "Cannot reference 'result' in postcondition of void function");
            }
            else if (!validNames.Contains(name) && !_indexedTypeSizeParams.Contains(name))
            {
                _diagnostics.Report(
                    ensures.Span,
                    DiagnosticCode.UndefinedReference,
                    $"Postcondition can only reference parameters and 'result'. Unknown identifier: '{name}'");
            }
        }
    }

    /// <summary>
    /// Verifies that quantifier bound variables have integer types suitable for range iteration.
    /// Also warns about nested quantifiers that may have O(n*m) runtime complexity.
    /// </summary>
    private void VerifyQuantifierTypes(ExpressionNode expr, int nestingDepth = 0)
    {
        if (expr is ForallExpressionNode forall)
        {
            ValidateQuantifierVariableTypes(forall.BoundVariables, forall.Span);
            var depth = nestingDepth + forall.BoundVariables.Count;
            ReportNestedQuantifier(forall.Span, depth);
            VerifyQuantifierTypes(forall.Body, depth);
            return;
        }

        if (expr is ExistsExpressionNode exists)
        {
            ValidateQuantifierVariableTypes(exists.BoundVariables, exists.Span);
            var depth = nestingDepth + exists.BoundVariables.Count;
            ReportNestedQuantifier(exists.Span, depth);
            VerifyQuantifierTypes(exists.Body, depth);
            return;
        }

        foreach (var child in RecursiveAstWalker.GetAllChildren(expr))
            VerifyQuantifierTypesInNode(child, nestingDepth);
    }

    private void VerifyQuantifierTypesInNode(AstNode node, int nestingDepth)
    {
        if (node is ExpressionNode expression)
        {
            VerifyQuantifierTypes(expression, nestingDepth);
            return;
        }

        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            VerifyQuantifierTypesInNode(child, nestingDepth);
    }

    private void ReportNestedQuantifier(TextSpan span, int depth)
    {
        if (depth <= 1)
            return;

        _diagnostics.Report(
            span,
            DiagnosticCode.QuantifierNestedComplexity,
            $"Nested quantifier with {depth} bound variables may result in O(n^{depth}) runtime checks. Consider optimizing if performance is critical.",
            DiagnosticSeverity.Info);
    }

    /// <summary>
    /// Validates that all bound variables in a quantifier have integer types.
    /// </summary>
    private void ValidateQuantifierVariableTypes(IReadOnlyList<QuantifierVariableNode> boundVariables, TextSpan span)
    {
        var integerTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "i8", "i16", "i32", "i64",
            "u8", "u16", "u32", "u64",
            "int", "long", "short", "byte",
            "uint", "ulong", "ushort", "sbyte"
        };

        foreach (var bv in boundVariables)
        {
            if (!integerTypes.Contains(bv.TypeName))
            {
                _diagnostics.Report(
                    span,
                    DiagnosticCode.QuantifierNonIntegerType,
                    $"Quantifier variable '{bv.Name}' has type '{bv.TypeName}' which may not support finite range iteration. Consider using an integer type (i32, i64, etc.).",
                    DiagnosticSeverity.Warning);
            }
        }
    }

    private CalorType? InferExpressionType(ExpressionNode expr)
    {
        return expr switch
        {
            IntLiteralNode => PrimitiveType.Int,
            FloatLiteralNode => PrimitiveType.Float,
            BoolLiteralNode => PrimitiveType.Bool,
            StringLiteralNode => PrimitiveType.String,
            BinaryOperationNode binOp => InferBinaryOperationType(binOp),
            UnaryOperationNode unaryOp => InferUnaryOperationType(unaryOp),
            ForallExpressionNode => PrimitiveType.Bool, // Quantifiers return bool
            ExistsExpressionNode => PrimitiveType.Bool,
            ImplicationExpressionNode => PrimitiveType.Bool,
            ReferenceNode => null, // Would need symbol table to determine
            _ => null
        };
    }

    private CalorType? InferUnaryOperationType(UnaryOperationNode unaryOp)
    {
        return unaryOp.Operator switch
        {
            UnaryOperator.Not => PrimitiveType.Bool,
            UnaryOperator.Negate => InferExpressionType(unaryOp.Operand),
            _ => null
        };
    }

    private CalorType? InferBinaryOperationType(BinaryOperationNode binOp)
    {
        // Comparison operators return bool
        return binOp.Operator switch
        {
            BinaryOperator.Equal or
            BinaryOperator.NotEqual or
            BinaryOperator.LessThan or
            BinaryOperator.LessOrEqual or
            BinaryOperator.GreaterThan or
            BinaryOperator.GreaterOrEqual or
            BinaryOperator.And or
            BinaryOperator.Or => PrimitiveType.Bool,

            // Arithmetic operators preserve type of operands
            BinaryOperator.Add or
            BinaryOperator.Subtract or
            BinaryOperator.Multiply or
            BinaryOperator.Divide or
            BinaryOperator.Modulo => InferExpressionType(binOp.Left),

            _ => null
        };
    }

    private HashSet<string> CollectReferences(ExpressionNode expr)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        var boundVariables = new HashSet<string>(StringComparer.Ordinal);
        CollectReferencesInternal(expr, references, boundVariables);
        return references;
    }

    private void CollectReferencesInternal(ExpressionNode expr, HashSet<string> references, HashSet<string> boundVariables)
    {
        if (expr is ReferenceNode reference)
        {
            if (!boundVariables.Contains(reference.Name))
                references.Add(reference.Name);
            return;
        }

        if (expr is ForallExpressionNode forall)
        {
            var nested = new HashSet<string>(boundVariables, StringComparer.Ordinal);
            nested.UnionWith(forall.BoundVariables.Select(variable => variable.Name));
            CollectReferencesInternal(forall.Body, references, nested);
            return;
        }

        if (expr is ExistsExpressionNode exists)
        {
            var nested = new HashSet<string>(boundVariables, StringComparer.Ordinal);
            nested.UnionWith(exists.BoundVariables.Select(variable => variable.Name));
            CollectReferencesInternal(exists.Body, references, nested);
            return;
        }

        foreach (var child in RecursiveAstWalker.GetAllChildren(expr))
            CollectReferencesInNode(child, references, boundVariables);
    }

    private void CollectReferencesInNode(
        AstNode node,
        HashSet<string> references,
        HashSet<string> boundVariables)
    {
        if (node is ExpressionNode expression)
        {
            CollectReferencesInternal(expression, references, boundVariables);
            return;
        }

        foreach (var child in RecursiveAstWalker.GetAllChildren(node))
            CollectReferencesInNode(child, references, boundVariables);
    }
}

/// <summary>
/// Result of contract verification.
/// </summary>
public sealed class ContractVerificationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }

    private ContractVerificationResult(bool isValid, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    public static ContractVerificationResult Success()
        => new(true, Array.Empty<string>());

    public static ContractVerificationResult Failure(IReadOnlyList<string> errors)
        => new(false, errors);
}
