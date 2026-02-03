using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
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

    private CalorType? InferExpressionType(ExpressionNode expr)
    {
        return expr switch
        {
            IntLiteralNode => PrimitiveType.Int,
            FloatLiteralNode => PrimitiveType.Float,
            BoolLiteralNode => PrimitiveType.Bool,
            StringLiteralNode => PrimitiveType.String,
            BinaryOperationNode binOp => InferBinaryOperationType(binOp),
            ReferenceNode => null, // Would need symbol table to determine
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
        CollectReferencesInternal(expr, references);
        return references;
    }

    private void CollectReferencesInternal(ExpressionNode expr, HashSet<string> references)
    {
        switch (expr)
        {
            case ReferenceNode refNode:
                references.Add(refNode.Name);
                break;
            case BinaryOperationNode binOp:
                CollectReferencesInternal(binOp.Left, references);
                CollectReferencesInternal(binOp.Right, references);
                break;
            case SomeExpressionNode someExpr:
                CollectReferencesInternal(someExpr.Value, references);
                break;
            case OkExpressionNode okExpr:
                CollectReferencesInternal(okExpr.Value, references);
                break;
            case ErrExpressionNode errExpr:
                CollectReferencesInternal(errExpr.Error, references);
                break;
            case FieldAccessNode fieldAccess:
                CollectReferencesInternal(fieldAccess.Target, references);
                break;
            case RecordCreationNode recordCreate:
                foreach (var field in recordCreate.Fields)
                {
                    CollectReferencesInternal(field.Value, references);
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
