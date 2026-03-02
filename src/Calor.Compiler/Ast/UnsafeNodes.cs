using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents a stackalloc expression.
/// §SALLOC{type:size}                           // stackalloc int[10]
/// §SALLOC{type} expr1 expr2 §/SALLOC           // stackalloc int[] { 1, 2, 3 }
/// </summary>
public sealed class StackAllocNode : ExpressionNode
{
    /// <summary>
    /// The element type being stack-allocated.
    /// </summary>
    public string ElementType { get; }

    /// <summary>
    /// The size expression (for sized stackalloc). Null if using initializer.
    /// </summary>
    public ExpressionNode? Size { get; }

    /// <summary>
    /// The initial elements (for initialized stackalloc). Empty if using size.
    /// </summary>
    public IReadOnlyList<ExpressionNode> Initializer { get; }

    public StackAllocNode(
        TextSpan span,
        string elementType,
        ExpressionNode? size,
        IReadOnlyList<ExpressionNode> initializer)
        : base(span)
    {
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        Size = size;
        Initializer = initializer ?? Array.Empty<ExpressionNode>();
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an unsafe code block.
/// §UNSAFE{id} ... §/UNSAFE{id}
/// </summary>
public sealed class UnsafeBlockNode : StatementNode
{
    public string Id { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public UnsafeBlockNode(TextSpan span, string id, IReadOnlyList<StatementNode> body)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a fixed statement.
/// §FIXED{id:ptr:type:init} ... §/FIXED{id}
/// </summary>
public sealed class FixedStatementNode : StatementNode
{
    public string Id { get; }
    public string PointerName { get; }
    public string PointerType { get; }
    public ExpressionNode Initializer { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public FixedStatementNode(
        TextSpan span,
        string id,
        string pointerName,
        string pointerType,
        ExpressionNode initializer,
        IReadOnlyList<StatementNode> body)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        PointerName = pointerName ?? throw new ArgumentNullException(nameof(pointerName));
        PointerType = pointerType ?? throw new ArgumentNullException(nameof(pointerType));
        Initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an address-of expression: &amp;x
/// §ADDR expr
/// </summary>
public sealed class AddressOfNode : ExpressionNode
{
    public ExpressionNode Operand { get; }

    public AddressOfNode(TextSpan span, ExpressionNode operand)
        : base(span)
    {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a pointer dereference expression: *ptr
/// §DEREF expr
/// </summary>
public sealed class PointerDereferenceNode : ExpressionNode
{
    public ExpressionNode Operand { get; }

    public PointerDereferenceNode(TextSpan span, ExpressionNode operand)
        : base(span)
    {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a sizeof expression: sizeof(int)
/// §SIZEOF{type}
/// </summary>
public sealed class SizeOfNode : ExpressionNode
{
    public string TypeName { get; }

    public SizeOfNode(TextSpan span, string typeName)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a lock/synchronization block.
/// §SYNC{id} (expr) ... §/SYNC{id}
/// Compiles to: lock (expr) { body }
/// </summary>
public sealed class SyncBlockNode : StatementNode
{
    public string Id { get; }
    public ExpressionNode LockExpression { get; }
    public IReadOnlyList<StatementNode> Body { get; }

    public SyncBlockNode(
        TextSpan span,
        string id,
        ExpressionNode lockExpression,
        IReadOnlyList<StatementNode> body)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        LockExpression = lockExpression ?? throw new ArgumentNullException(nameof(lockExpression));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
