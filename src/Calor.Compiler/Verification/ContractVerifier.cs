using Calor.Compiler.Analysis;
using Calor.Compiler.Ast;
using Calor.Compiler.Binding;
using Calor.Compiler.Binding.BoundTypes;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Parsing;
using Calor.Compiler.TypeChecking;
using Calor.Compiler.Verification.Obligations;

namespace Calor.Compiler.Verification;

/// <summary>
/// Verifies contracts (preconditions and postconditions) in Calor code.
/// Currently performs semantic validation; future versions may include
/// static verification using SMT solvers.
/// </summary>
public sealed class ContractVerifier
{
    /// <summary>
    /// Projects parameter entry guards into the existing contract pipeline. Out
    /// refinements constrain writes, not the uninitialized entry value.
    /// </summary>
    internal static IReadOnlyList<RequiresNode> EffectivePreconditions(
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<RequiresNode> preconditions)
        => parameters
            .Where(parameter => !parameter.Modifier.HasFlag(ParameterModifier.Out)
                && parameter.InlineRefinement != null)
            .Select(parameter => new RequiresNode(
                parameter.InlineRefinement!.Predicate.Span,
                FactCollector.SubstituteContractSelf(parameter.InlineRefinement.Predicate, parameter.Name),
                null,
                new AttributeCollection()))
            .Concat(preconditions)
            .ToArray();

    private readonly DiagnosticBag _diagnostics;
    private readonly Binder _contractBinder = new(new DiagnosticBag());
    private IReadOnlyList<(string Name, string Type)> _contractVariables = [];

    public ContractVerifier(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    /// <summary>
    /// Indexed type size parameter names, valid as identifiers in contracts.
    /// </summary>
    private readonly HashSet<string> _indexedTypeSizeParams = new(StringComparer.Ordinal);

    /// <summary>
    /// Validates declaration-site closure without borrowing any use-site scope.
    /// </summary>
    internal static void VerifyNamedRefinementPredicates(ModuleNode module, DiagnosticBag diagnostics)
    {
        foreach (var refinement in module.RefinementTypes)
            Check(refinement.Predicate, new HashSet<string>(StringComparer.Ordinal), refinement.Name);

        void Check(ExpressionNode expression, HashSet<string> bound, string name)
        {
            switch (expression)
            {
                case SelfRefNode or IntLiteralNode or FloatLiteralNode or DecimalLiteralNode
                    or BoolLiteralNode or StringLiteralNode:
                    return;
                case ReferenceNode { Name: "null" or "true" or "false" }:
                    // The emitter always treats these as literals, never as use-site storage.
                    return;
                case ReferenceNode reference:
                    if (!bound.Contains(reference.Name))
                        Reject(reference, name,
                            $"cannot capture '{reference.Name}'; only # and locally quantified variables may name values");
                    return;
                case ForallExpressionNode forall:
                    CheckQuantifier(forall.BoundVariables, forall.Body);
                    return;
                case ExistsExpressionNode exists:
                    CheckQuantifier(exists.BoundVariables, exists.Body);
                    return;
                case BinaryOperationNode binary when Enum.IsDefined(binary.Operator):
                    Check(binary.Left, bound, name);
                    Check(binary.Right, bound, name);
                    return;
                case UnaryOperationNode unary when unary.Operator is
                    UnaryOperator.Negate or UnaryOperator.Not or UnaryOperator.BitwiseNot:
                    Check(unary.Operand, bound, name);
                    return;
                case ConditionalExpressionNode conditional:
                    Check(conditional.Condition, bound, name);
                    Check(conditional.WhenTrue, bound, name);
                    Check(conditional.WhenFalse, bound, name);
                    return;
                case ImplicationExpressionNode implication:
                    Check(implication.Antecedent, bound, name);
                    Check(implication.Consequent, bound, name);
                    return;
                case StringOperationNode text when IsClosedStringOperation(text):
                    foreach (var argument in text.Arguments)
                        Check(argument, bound, name);
                    return;
                case CharOperationNode character when character.Operation is
                    CharOp.CharLiteral or CharOp.CharAt or CharOp.CharCode or CharOp.CharFromCode
                    or CharOp.IsLetter or CharOp.IsDigit or CharOp.IsWhiteSpace or CharOp.IsUpper or CharOp.IsLower:
                    foreach (var argument in character.Arguments)
                        Check(argument, bound, name);
                    return;
                case TypeOperationNode conversion when conversion.Operation == TypeOp.Cast
                    && PrimitiveType.FromName(conversion.TargetType) != null:
                    Check(conversion.Operand, bound, name);
                    return;
                case CallExpressionNode call:
                    // A callee is not a variable reference. Without exact declaration binding,
                    // even a type-looking receiver can resolve to use-site storage or a member.
                    diagnostics.ReportError(call.CalleeSpan, DiagnosticCode.NamedRefinementNotClosed,
                        $"Named refinement '{name}' cannot use call '{call.Target}': its binding and state are not modeled. Use closed native operators instead.");
                    return;
                default:
                    Reject(expression, name,
                        $"cannot use {expression.GetType().Name}: closure and state are not modeled");
                    return;
            }

            void CheckQuantifier(IReadOnlyList<QuantifierVariableNode> variables, ExpressionNode body)
            {
                // Copy the scope: a binder in one operand must not leak into its sibling.
                var nested = new HashSet<string>(bound, StringComparer.Ordinal);
                foreach (var variable in variables)
                {
                    if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(variable.Name)
                        || PrimitiveType.FromName(AttributeHelper.ToSurfaceSpelling(variable.TypeName))?.Name
                            != PrimitiveType.Int.Name)
                    {
                        diagnostics.ReportError(variable.Span, DiagnosticCode.NamedRefinementNotClosed,
                            $"Named refinement '{name}' requires simple integer quantifier binders; '{variable.Name}: {variable.TypeName}' is not modeled.");
                        return;
                    }
                    nested.Add(variable.Name);
                }
                Check(body, nested, name);
            }
        }

        void Reject(ExpressionNode expression, string name, string reason)
            => diagnostics.ReportError(expression.Span, DiagnosticCode.NamedRefinementNotClosed,
                $"Named refinement '{name}' {reason}.");
    }

    private static bool IsClosedStringOperation(StringOperationNode operation)
        => operation.Operation switch
        {
            StringOp.Length or StringOp.Contains or StringOp.Equals or StringOp.IsNullOrEmpty
                or StringOp.IsNullOrWhiteSpace or StringOp.Substring or StringOp.SubstringFrom
                or StringOp.Replace or StringOp.Trim or StringOp.TrimStart or StringOp.TrimEnd
                or StringOp.PadLeft or StringOp.PadRight or StringOp.Split
                => operation.ComparisonMode == null || Enum.IsDefined(operation.ComparisonMode.Value),
            StringOp.StartsWith or StringOp.EndsWith or StringOp.IndexOf
                => operation.ComparisonMode is { } mode && Enum.IsDefined(mode),
            _ => false
        };

    /// <summary>
    /// Verifies all contracts in a module.
    /// </summary>
    public void Verify(ModuleNode module)
    {
        _asyncTypeModule = module;
        VerifyNamedRefinementPredicates(module, _diagnostics);
        // Collect indexed type size parameter names
        foreach (var itype in module.IndexedTypes)
        {
            _indexedTypeSizeParams.Add(itype.SizeParam);
        }

        foreach (var function in module.Functions)
        {
            VerifyFunction(function);
        }
        VerifyNestedInlineParameters(module);

        void VerifyNestedInlineParameters(AstNode node)
        {
            IReadOnlyList<ParameterNode>? parameters = node switch
            {
                MethodNode method => method.Parameters,
                MethodSignatureNode signature => signature.Parameters,
                ConstructorNode constructor => constructor.Parameters,
                OperatorOverloadNode op => op.Parameters,
                FunctionNode function when !module.Functions.Contains(function) => function.Parameters,
                _ => null
            };
            if (parameters?.Any(parameter => parameter.InlineRefinement != null) == true)
            {
                _contractVariables = parameters.Select(parameter => (parameter.Name, parameter.TypeName)).ToArray();
                foreach (var precondition in EffectivePreconditions(parameters, []))
                    VerifyPrecondition(precondition, parameters);
            }
            foreach (var child in RecursiveAstWalker.GetAllChildren(node))
                VerifyNestedInlineParameters(child);
        }
    }

    /// <summary>
    /// Verifies contracts in a single function.
    /// </summary>
    private ModuleNode? _asyncTypeModule;

    public void VerifyFunction(FunctionNode function)
    {
        var bodyType = ReturnShape.GetAsyncReturnTypes(function.Output?.TypeName ?? "void", function.IsAsync,
            _asyncTypeModule, function.TypeParameters.Select(parameter => parameter.Name)).BodyType;
        _contractVariables = function.Parameters.Select(parameter => (parameter.Name, parameter.TypeName))
            .Concat(ReturnShape.IsVoidType(bodyType) ? [] : new[] { ("result", bodyType) }).ToArray();
        // Verify preconditions
        foreach (var requires in EffectivePreconditions(function.Parameters, function.Preconditions))
        {
            VerifyPrecondition(requires, function.Parameters);
        }

        // Verify postconditions
        foreach (var ensures in function.Postconditions)
        {
            VerifyPostcondition(ensures, function);
        }
    }

    private void VerifyPrecondition(RequiresNode requires, IReadOnlyList<ParameterNode> parameters)
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
        var parameterNames = parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

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

        var hasReturnValue = !ReturnShape.IsVoidType(ReturnShape.GetAsyncReturnTypes(
            function.Output?.TypeName ?? "void", function.IsAsync, _asyncTypeModule,
            function.TypeParameters.Select(parameter => parameter.Name)).BodyType);

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
            CallExpressionNode call => InferBclCallType(call),
            StringOperationNode operation => operation.Operation switch
            {
                StringOp.Contains or StringOp.StartsWith or StringOp.EndsWith
                    or StringOp.IsNullOrEmpty or StringOp.IsNullOrWhiteSpace
                    or StringOp.Equals => PrimitiveType.Bool,
                StringOp.Length or StringOp.IndexOf => PrimitiveType.Int,
                _ => PrimitiveType.String
            },
            CharOperationNode operation => operation.Operation switch
            {
                CharOp.IsLetter or CharOp.IsDigit or CharOp.IsWhiteSpace
                    or CharOp.IsUpper or CharOp.IsLower => PrimitiveType.Bool,
                CharOp.CharCode => PrimitiveType.Int,
                _ => PrimitiveType.Char
            },
            BinaryOperationNode binOp => InferBinaryOperationType(binOp),
            UnaryOperationNode unaryOp => InferUnaryOperationType(unaryOp),
            ForallExpressionNode => PrimitiveType.Bool, // Quantifiers return bool
            ExistsExpressionNode => PrimitiveType.Bool,
            ImplicationExpressionNode => PrimitiveType.Bool,
            ReferenceNode reference => _contractVariables.FirstOrDefault(variable => variable.Name == reference.Name)
                is { Type: { } type } ? PrimitiveType.FromName(type) : null,
            ConditionalExpressionNode conditional
                when InferExpressionType(conditional.WhenTrue) is { } type
                    && type == InferExpressionType(conditional.WhenFalse) => type,
            _ => null
        };
    }

    private CalorType? InferBclCallType(CallExpressionNode call)
    {
        var dot = call.Target.LastIndexOf('.');
        if (dot <= 0)
            return null;
        var receiver = _contractBinder.BindVerificationReceiver(
            new ReferenceNode(call.Span, call.Target[..dot]), _contractVariables);
        if (receiver.Type is not NominalBoundType
            { RoslynSymbol.SpecialType: Microsoft.CodeAnalysis.SpecialType.System_String })
            return null;
        var comparison = call.Arguments.LastOrDefault() is ReferenceNode reference
            && reference.Name.StartsWith("StringComparison.", StringComparison.Ordinal)
            || call.Arguments.LastOrDefault() is ReferenceNode fullReference
            && fullReference.Name.StartsWith("System.StringComparison.", StringComparison.Ordinal);
        var method = _contractBinder.ResolveVerificationStringCall(
            call, receiver is BoundTypeReferenceExpression, comparison, _contractVariables);
        return method?.ReturnType.SpecialType switch
        {
            Microsoft.CodeAnalysis.SpecialType.System_Boolean => PrimitiveType.Bool,
            Microsoft.CodeAnalysis.SpecialType.System_Int32 => PrimitiveType.Int,
            Microsoft.CodeAnalysis.SpecialType.System_String => PrimitiveType.String,
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
            var root = reference.Name.Split('.')[0];
            if (reference.Name.Contains('.')
                && (root is "StringComparison" || reference.Name.StartsWith("System.StringComparison.", StringComparison.Ordinal))
                && !_contractVariables.Any(variable => variable.Name == root))
                return;
            if (!boundVariables.Contains(root))
                references.Add(root);
            return;
        }

        if (expr is CallExpressionNode call)
        {
            var dot = call.Target.LastIndexOf('.');
            if (dot > 0)
            {
                var name = call.Target[..dot];
                var receiver = _contractBinder.BindVerificationReceiver(
                    new ReferenceNode(call.Span, name), _contractVariables);
                if (receiver is not BoundTypeReferenceExpression)
                    CollectReferencesInternal(new ReferenceNode(call.Span, name), references, boundVariables);
            }
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
