using Calor.Compiler.Ast;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Single source of truth for the "return shape" of an owner — a
/// function/method/operator, constructor, property/indexer accessor, or event
/// accessor — i.e. whether a value <c>§R</c> is expected and, if not, why.
///
/// <para>This classification used to be duplicated: <see cref="ReturnValidationPass"/>
/// carried a private <c>OwnerKind</c> enum with <c>ClassifyCallable</c>/
/// <c>ClassifyAccessor</c>/iterator-detection, and
/// <see cref="Verification.ContractVerifier"/> re-derived "does this function
/// declare a value output" inline. Both now defer here so the two consumers can
/// never drift.</para>
///
/// <para>Note the distinction between two related questions:
/// <list type="bullet">
///   <item><see cref="Classify"/> answers the runtime question ("does control
///   leave this owner carrying a value") and therefore folds in async/iterator
///   lowering — an iterator that declares <c>IEnumerable&lt;T&gt;</c> is
///   <see cref="Kind.Iterator"/>, not <see cref="Kind.Value"/>.</item>
///   <item><see cref="DeclaresValueOutput"/> answers the narrower header question
///   ("did the signature declare a non-void return type"), which is what the
///   contract verifier needs for <c>result</c> referenceability and which must
///   NOT fold in iterator lowering.</item>
/// </list></para>
/// </summary>
public static class ReturnShape
{
    /// <summary>The classified return shape of an owner.</summary>
    public enum Kind
    {
        /// <summary>Not a return-bearing owner (e.g. module/class scope).</summary>
        None,

        /// <summary>Value-returning owner — a value <c>§R</c> is expected.</summary>
        Value,

        /// <summary>Non-async owner with no return type (compiles to <c>void</c>).</summary>
        Void,

        /// <summary>Async owner with no return type (compiles to <c>Task</c>).</summary>
        AsyncVoid,

        /// <summary>Iterator owner (its body yields).</summary>
        Iterator,

        /// <summary>Property/indexer <c>set</c> or <c>init</c> accessor.</summary>
        Setter,

        /// <summary>Constructor.</summary>
        Constructor,

        /// <summary>Event <c>add</c>/<c>remove</c> accessor.</summary>
        EventAccessor,
    }

    /// <summary>
    /// Classify an owner node. Non-owner nodes classify as <see cref="Kind.None"/>.
    /// </summary>
    public static Kind Classify(AstNode owner, ModuleNode? module = null) => owner switch
    {
        FunctionNode f => ClassifyCallable(f.Output, f.IsAsync, f, module),
        MethodNode m => ClassifyCallable(m.Output, m.IsAsync, m, module),
        OperatorOverloadNode op => ClassifyCallable(op.Output, isAsync: false, op, module),
        ConstructorNode => Kind.Constructor,
        PropertyAccessorNode accessor => ClassifyAccessor(accessor),
        EventDefinitionNode => Kind.EventAccessor,
        _ => Kind.None,
    };

    /// <summary>
    /// True when the owner produces no value at its return site, so a value
    /// <c>§R</c> is illegal there (void/async-void/iterator/setter/constructor/
    /// event accessor).
    /// </summary>
    public static bool IsNoValueOwner(Kind shape) => shape switch
    {
        Kind.Void or Kind.AsyncVoid or Kind.Iterator
            or Kind.Setter or Kind.Constructor or Kind.EventAccessor => true,
        _ => false,
    };

    /// <summary>
    /// Whether an <see cref="OutputNode"/> declares a non-void return type — the
    /// narrow header predicate (does NOT fold in async/iterator lowering). Used by
    /// the contract verifier to decide whether <c>result</c> is referenceable.
    /// </summary>
    public static bool DeclaresValueOutput(OutputNode? output)
        => output is not null && !IsVoidType(output.TypeName);

    /// <summary>
    /// The void-name test shared by every consumer. Only <c>void</c> (case-
    /// insensitive) is treated as no-value, matching the C# emitter's own rule
    /// (<c>returnType != "void"</c>); keeping the set this narrow avoids diverging
    /// from codegen and thus avoids false positives.
    /// </summary>
    public static bool IsVoidType(string typeName)
        => typeName.Trim().Equals("void", StringComparison.OrdinalIgnoreCase);

    public static (string CallableType, string BodyType) GetAsyncReturnTypes(
        string declaredType, bool isAsync, ModuleNode? module,
        IEnumerable<string>? typeParameterNames = null)
    {
        if (!isAsync)
            return (declaredType, declaredType);
        var surface = Parsing.AttributeHelper.ToSurfaceSpelling(declaredType);
        var open = surface.IndexOf('<');
        var carrierName = open < 0 ? surface : surface[..open];
        if (typeParameterNames?.Contains(carrierName, StringComparer.Ordinal) != true
            && Parsing.AttributeHelper.TryGetAsyncResultType(declaredType, module, out var result))
            return (declaredType, result);
        return IsVoidType(Parsing.AttributeHelper.ToSurfaceSpelling(declaredType))
            ? ("global::System.Threading.Tasks.Task", "void")
            : ($"global::System.Threading.Tasks.Task<{declaredType}>", declaredType);
    }

    /// <summary>Includes enclosing class parameters, with nearer declarations taking precedence.</summary>
    public static IReadOnlyList<TypeParameterNode> GetCallableTypeParameters(AstNode owner, ModuleNode? module)
    {
        IReadOnlyList<TypeParameterNode> ownParameters = owner switch
        {
            FunctionNode function => function.TypeParameters,
            MethodNode method => method.TypeParameters,
            ClassDefinitionNode cls => cls.TypeParameters,
            _ => []
        };
        IReadOnlyList<TypeParameterNode> enclosingParameters = [];

        bool FindOwner(AstNode node, IReadOnlyList<TypeParameterNode> context)
        {
            if (ReferenceEquals(node, owner))
            {
                enclosingParameters = context;
                return true;
            }
            if (node is FunctionNode or MethodNode or ExpressionNode)
                return false;

            var childContext = node is ClassDefinitionNode cls
                ? cls.TypeParameters.Concat(context).ToArray()
                : context;
            return RecursiveAstWalker.GetAllChildren(node).Any(child => FindOwner(child, childContext));
        }

        if (module != null)
            FindOwner(module, []);
        return ownParameters.Concat(enclosingParameters).DistinctBy(parameter => parameter.Name).ToArray();
    }

    private static Kind ClassifyCallable(OutputNode? output, bool isAsync, AstNode owner, ModuleNode? module)
    {
        if (IsIterator(owner))
        {
            return Kind.Iterator;
        }

        var typeParameters = GetCallableTypeParameters(owner, module).Select(parameter => parameter.Name);
        if (IsVoidType(GetAsyncReturnTypes(output?.TypeName ?? "void", isAsync, module, typeParameters).BodyType))
        {
            return isAsync ? Kind.AsyncVoid : Kind.Void;
        }

        return Kind.Value;
    }

    private static Kind ClassifyAccessor(PropertyAccessorNode accessor)
    {
        if (accessor.Kind == PropertyAccessorNode.AccessorKind.Get)
        {
            // Accessor iterators are fail-closed by ReturnValidationPass until
            // their deferred execution and return-type semantics are defined.
            return Kind.Value;
        }

        // set / init accessors never return a value.
        return Kind.Setter;
    }

    private static bool IsIterator(AstNode owner)
    {
        return RecursiveAstWalker.GetChildren(owner)
            .SelectMany(RecursiveAstWalker.DescendantsAndSelf)
            .OfType<StatementNode>()
            .Any(statement =>
                statement is YieldReturnStatementNode
                    or YieldBreakStatementNode);
    }
}
