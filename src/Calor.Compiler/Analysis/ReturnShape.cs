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
    public static Kind Classify(AstNode owner) => owner switch
    {
        FunctionNode f => ClassifyCallable(f.Output, f.IsAsync, f),
        MethodNode m => ClassifyCallable(m.Output, m.IsAsync, m),
        OperatorOverloadNode op => ClassifyCallable(op.Output, isAsync: false, op),
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

    private static Kind ClassifyCallable(OutputNode? output, bool isAsync, AstNode owner)
    {
        if (IsIterator(owner))
        {
            return Kind.Iterator;
        }

        if (!DeclaresValueOutput(output))
        {
            return isAsync ? Kind.AsyncVoid : Kind.Void;
        }

        return Kind.Value;
    }

    private static Kind ClassifyAccessor(PropertyAccessorNode accessor)
    {
        if (accessor.Kind == PropertyAccessorNode.AccessorKind.Get)
        {
            // A getter returns the property value, so a value §R is expected —
            // unless the getter is itself an iterator.
            return IsIterator(accessor) ? Kind.Iterator : Kind.Value;
        }

        // set / init accessors never return a value.
        return Kind.Setter;
    }

    private static bool IsIterator(AstNode owner)
    {
        foreach (var child in RecursiveAstWalker.GetChildren(owner))
        {
            if (ContainsYield(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsYield(AstNode node)
    {
        if (node is YieldReturnStatementNode or YieldBreakStatementNode)
        {
            return true;
        }

        // Do not cross into a nested owner boundary (defensive — statement bodies
        // do not currently contain nested owners, and lambdas are expressions
        // that RecursiveAstWalker already skips).
        if (IsOwner(node))
        {
            return false;
        }

        foreach (var child in RecursiveAstWalker.GetChildren(node))
        {
            if (ContainsYield(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOwner(AstNode node) => node is
        FunctionNode or MethodNode or OperatorOverloadNode or
        ConstructorNode or PropertyAccessorNode or EventDefinitionNode;
}
