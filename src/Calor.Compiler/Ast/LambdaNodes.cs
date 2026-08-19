using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a lambda parameter.
/// </summary>
public sealed class LambdaParameterNode : AstNode
{
    public string Name { get; }
    public TextSpan IdentifierSpan { get; }
    public string? TypeName { get; }
    public TextSpan? TypeNameSpan { get; }

    public LambdaParameterNode(
        TextSpan span,
        string name,
        string? typeName,
        TextSpan? identifierSpan = null,
        TextSpan? typeNameSpan = null)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IdentifierSpan = identifierSpan ?? span;
        TypeName = typeName;
        TypeNameSpan = typeName == null ? null : typeNameSpan ?? TextSpan.Empty;
    }


}

/// <summary>
/// Represents a lambda expression.
/// §LAM[lam1:x:i32] §OP[kind=mul] §REF[name=x] 2 §/LAM[lam1]
/// // (int x) => x * 2
/// </summary>
public sealed class LambdaExpressionNode : ExpressionNode
{
    public string Id { get; }
    public IReadOnlyList<LambdaParameterNode> Parameters { get; }
    public EffectsNode? Effects { get; }
    public bool IsAsync { get; }
    public bool IsStatic { get; }

    /// <summary>
    /// The body can be either an expression (for expression lambdas)
    /// or statements (for statement lambdas).
    /// </summary>
    public ExpressionNode? ExpressionBody { get; }
    public IReadOnlyList<StatementNode>? StatementBody { get; }

    public AttributeCollection Attributes { get; }

    public LambdaExpressionNode(
        TextSpan span,
        string id,
        IReadOnlyList<LambdaParameterNode> parameters,
        EffectsNode? effects,
        bool isAsync,
        ExpressionNode? expressionBody,
        IReadOnlyList<StatementNode>? statementBody,
        AttributeCollection attributes,
        bool isStatic = false)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Effects = effects;
        IsAsync = isAsync;
        IsStatic = isStatic;
        ExpressionBody = expressionBody;
        StatementBody = statementBody;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public bool IsExpressionLambda => ExpressionBody != null;


}

/// <summary>
/// Represents a delegate definition.
/// §DEL[d001:Processor]
///   §I[string:input] §O[bool] §E[fr,fw]
/// §/DEL[d001]
/// </summary>
public sealed class DelegateDefinitionNode : TypeDefinitionNode
{
    public IReadOnlyList<ParameterNode> Parameters { get; }
    public OutputNode? Output { get; }
    public EffectsNode? Effects { get; }

    public DelegateDefinitionNode(
        TextSpan span,
        string id,
        string name,
        IReadOnlyList<ParameterNode> parameters,
        OutputNode? output,
        EffectsNode? effects,
        AttributeCollection attributes,
        TextSpan? identifierSpan = null)
        : base(span, id, name, attributes, identifierSpan)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Output = output;
        Effects = effects;
    }


}

/// <summary>
/// Represents an event definition.
/// Simple field: §EVT[e001:Click:pub:EventHandler]
/// With accessors:
///   §EVT[e001:Click:pub:EventHandler]
///     §EADD ... §/EADD
///     §EREM ... §/EREM
///   §/EVT[e001]
/// </summary>
public sealed class EventDefinitionNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public Visibility Visibility { get; }
    public string DelegateType { get; }
    public TextSpan DelegateTypeSpan { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>Optional body for the add accessor.</summary>
    public IReadOnlyList<StatementNode>? AddBody { get; }

    /// <summary>Optional body for the remove accessor.</summary>
    public IReadOnlyList<StatementNode>? RemoveBody { get; }

    /// <summary>True when this event has explicit add/remove accessor bodies.</summary>
    public bool HasAccessors => AddBody != null || RemoveBody != null;

    public EventDefinitionNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        string delegateType,
        AttributeCollection attributes,
        TextSpan? delegateTypeSpan = null)
        : this(
            span,
            id,
            name,
            visibility,
            delegateType,
            attributes,
            null,
            null,
            delegateTypeSpan)
    {
    }

    public EventDefinitionNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        string delegateType,
        AttributeCollection attributes,
        IReadOnlyList<StatementNode>? addBody,
        IReadOnlyList<StatementNode>? removeBody,
        TextSpan? delegateTypeSpan = null)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Visibility = visibility;
        DelegateType = delegateType ?? throw new ArgumentNullException(nameof(delegateType));
        DelegateTypeSpan = delegateTypeSpan ?? TextSpan.Empty;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        AddBody = addBody;
        RemoveBody = removeBody;
    }


}

/// <summary>
/// Represents event subscription.
/// §SUB §REF[name=button.Click] §REF[name=handler]
/// </summary>
public sealed class EventSubscribeNode : StatementNode
{
    public ExpressionNode Event { get; }
    public ExpressionNode Handler { get; }

    public EventSubscribeNode(TextSpan span, ExpressionNode @event, ExpressionNode handler)
        : base(span)
    {
        Event = @event ?? throw new ArgumentNullException(nameof(@event));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }


}

/// <summary>
/// Represents event unsubscription.
/// §UNSUB §REF[name=button.Click] §REF[name=handler]
/// </summary>
public sealed class EventUnsubscribeNode : StatementNode
{
    public ExpressionNode Event { get; }
    public ExpressionNode Handler { get; }

    public EventUnsubscribeNode(TextSpan span, ExpressionNode @event, ExpressionNode handler)
        : base(span)
    {
        Event = @event ?? throw new ArgumentNullException(nameof(@event));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }


}
