using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a lambda parameter.
/// </summary>
public sealed class LambdaParameterNode : AstNode
{
    public string Name { get; }
    public string? TypeName { get; }

    public LambdaParameterNode(TextSpan span, string name, string? typeName)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
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

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
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
        AttributeCollection attributes)
        : base(span, id, name, attributes)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Output = output;
        Effects = effects;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an event definition.
/// §EVT[e001:Click:pub:EventHandler]
/// </summary>
public sealed class EventDefinitionNode : AstNode
{
    public string Id { get; }
    public string Name { get; }
    public Visibility Visibility { get; }
    public string DelegateType { get; }
    public AttributeCollection Attributes { get; }

    public EventDefinitionNode(
        TextSpan span,
        string id,
        string name,
        Visibility visibility,
        string delegateType,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Visibility = visibility;
        DelegateType = delegateType ?? throw new ArgumentNullException(nameof(delegateType));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
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

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
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

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
