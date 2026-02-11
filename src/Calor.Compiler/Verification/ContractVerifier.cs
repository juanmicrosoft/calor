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
    /// Verifies all contracts in a module.
    /// </summary>
    public void Verify(ModuleNode module)
    {
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

        // Verify that the condition only references parameters and constants
        var referencedNames = CollectReferences(requires.Condition);
        var parameterNames = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var name in referencedNames)
        {
            if (!parameterNames.Contains(name))
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

        var hasReturnValue = function.Output != null &&
                             !function.Output.TypeName.Equals("VOID", StringComparison.OrdinalIgnoreCase);

        foreach (var name in referencedNames)
        {
            if (name == "result" && !hasReturnValue)
            {
                _diagnostics.Report(
                    ensures.Span,
                    DiagnosticCode.InvalidReference,
                    "Cannot reference 'result' in postcondition of void function");
            }
            else if (!validNames.Contains(name))
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
        switch (expr)
        {
            case ForallExpressionNode forall:
                ValidateQuantifierVariableTypes(forall.BoundVariables, forall.Span);
                // Warn about nested quantifiers (multiple variables count as nested)
                var forallDepth = nestingDepth + forall.BoundVariables.Count;
                if (forallDepth > 1)
                {
                    _diagnostics.Report(
                        forall.Span,
                        DiagnosticCode.QuantifierNestedComplexity,
                        $"Nested quantifier with {forallDepth} bound variables may result in O(n^{forallDepth}) runtime checks. Consider optimizing if performance is critical.",
                        DiagnosticSeverity.Info);
                }
                VerifyQuantifierTypes(forall.Body, forallDepth);
                break;
            case ExistsExpressionNode exists:
                ValidateQuantifierVariableTypes(exists.BoundVariables, exists.Span);
                var existsDepth = nestingDepth + exists.BoundVariables.Count;
                if (existsDepth > 1)
                {
                    _diagnostics.Report(
                        exists.Span,
                        DiagnosticCode.QuantifierNestedComplexity,
                        $"Nested quantifier with {existsDepth} bound variables may result in O(n^{existsDepth}) runtime checks. Consider optimizing if performance is critical.",
                        DiagnosticSeverity.Info);
                }
                VerifyQuantifierTypes(exists.Body, existsDepth);
                break;
            case ImplicationExpressionNode impl:
                VerifyQuantifierTypes(impl.Antecedent, nestingDepth);
                VerifyQuantifierTypes(impl.Consequent, nestingDepth);
                break;
            case BinaryOperationNode binOp:
                VerifyQuantifierTypes(binOp.Left, nestingDepth);
                VerifyQuantifierTypes(binOp.Right, nestingDepth);
                break;
            case UnaryOperationNode unaryOp:
                VerifyQuantifierTypes(unaryOp.Operand, nestingDepth);
                break;
            case ConditionalExpressionNode condExpr:
                VerifyQuantifierTypes(condExpr.Condition, nestingDepth);
                VerifyQuantifierTypes(condExpr.WhenTrue, nestingDepth);
                VerifyQuantifierTypes(condExpr.WhenFalse, nestingDepth);
                break;
        }
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
        switch (expr)
        {
            case ReferenceNode refNode:
                // Only add if not a bound variable from a quantifier
                if (!boundVariables.Contains(refNode.Name))
                    references.Add(refNode.Name);
                break;
            case BinaryOperationNode binOp:
                CollectReferencesInternal(binOp.Left, references, boundVariables);
                CollectReferencesInternal(binOp.Right, references, boundVariables);
                break;
            case UnaryOperationNode unaryOp:
                CollectReferencesInternal(unaryOp.Operand, references, boundVariables);
                break;
            case ConditionalExpressionNode condExpr:
                CollectReferencesInternal(condExpr.Condition, references, boundVariables);
                CollectReferencesInternal(condExpr.WhenTrue, references, boundVariables);
                CollectReferencesInternal(condExpr.WhenFalse, references, boundVariables);
                break;
            case ForallExpressionNode forall:
                // Collect bound variables, then recurse into body
                var forallBound = new HashSet<string>(boundVariables, StringComparer.Ordinal);
                foreach (var bv in forall.BoundVariables)
                    forallBound.Add(bv.Name);
                CollectReferencesInternal(forall.Body, references, forallBound);
                break;
            case ExistsExpressionNode exists:
                // Collect bound variables, then recurse into body
                var existsBound = new HashSet<string>(boundVariables, StringComparer.Ordinal);
                foreach (var bv in exists.BoundVariables)
                    existsBound.Add(bv.Name);
                CollectReferencesInternal(exists.Body, references, existsBound);
                break;
            case ImplicationExpressionNode impl:
                CollectReferencesInternal(impl.Antecedent, references, boundVariables);
                CollectReferencesInternal(impl.Consequent, references, boundVariables);
                break;
            case ArrayAccessNode arrayAccess:
                CollectReferencesInternal(arrayAccess.Array, references, boundVariables);
                CollectReferencesInternal(arrayAccess.Index, references, boundVariables);
                break;
            case SomeExpressionNode someExpr:
                CollectReferencesInternal(someExpr.Value, references, boundVariables);
                break;
            case OkExpressionNode okExpr:
                CollectReferencesInternal(okExpr.Value, references, boundVariables);
                break;
            case ErrExpressionNode errExpr:
                CollectReferencesInternal(errExpr.Error, references, boundVariables);
                break;
            case FieldAccessNode fieldAccess:
                CollectReferencesInternal(fieldAccess.Target, references, boundVariables);
                break;
            case RecordCreationNode recordCreate:
                foreach (var field in recordCreate.Fields)
                {
                    CollectReferencesInternal(field.Value, references, boundVariables);
                }
                break;
        }
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
