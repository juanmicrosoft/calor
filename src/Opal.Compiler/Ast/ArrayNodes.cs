using Opal.Compiler.Parsing;

namespace Opal.Compiler.Ast;

/// <summary>
/// Represents array creation.
/// §ARR[arr1:i32:10]                         // int[] arr1 = new int[10];
/// §ARR[arr2:i32] §A 1 §A 2 §A 3 §/ARR[arr2] // int[] arr2 = { 1, 2, 3 };
/// </summary>
public sealed class ArrayCreationNode : ExpressionNode
{
    /// <summary>
    /// The unique ID for this array declaration.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The variable name for this array.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The element type of the array.
    /// </summary>
    public string ElementType { get; }

    /// <summary>
    /// The size of the array (for sized arrays). Null if using initializer.
    /// </summary>
    public ExpressionNode? Size { get; }

    /// <summary>
    /// The initial elements (for initialized arrays). Empty if using size.
    /// </summary>
    public IReadOnlyList<ExpressionNode> Initializer { get; }

    public AttributeCollection Attributes { get; }

    public ArrayCreationNode(
        TextSpan span,
        string id,
        string name,
        string elementType,
        ExpressionNode? size,
        IReadOnlyList<ExpressionNode> initializer,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        Size = size;
        Initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents array element access.
/// §IDX §REF[name=arr] 0                     // arr[0]
/// </summary>
public sealed class ArrayAccessNode : ExpressionNode
{
    /// <summary>
    /// The array being accessed.
    /// </summary>
    public ExpressionNode Array { get; }

    /// <summary>
    /// The index expression.
    /// </summary>
    public ExpressionNode Index { get; }

    public ArrayAccessNode(TextSpan span, ExpressionNode array, ExpressionNode index)
        : base(span)
    {
        Array = array ?? throw new ArgumentNullException(nameof(array));
        Index = index ?? throw new ArgumentNullException(nameof(index));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents array length access.
/// §LEN §REF[name=arr]                       // arr.Length
/// </summary>
public sealed class ArrayLengthNode : ExpressionNode
{
    /// <summary>
    /// The array whose length is being accessed.
    /// </summary>
    public ExpressionNode Array { get; }

    public ArrayLengthNode(TextSpan span, ExpressionNode array)
        : base(span)
    {
        Array = array ?? throw new ArgumentNullException(nameof(array));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a foreach loop.
/// §EACH[each1:item:i32] §REF[name=arr]      // foreach (int item in arr)
///   ...
/// §/EACH[each1]
/// </summary>
public sealed class ForeachStatementNode : StatementNode
{
    /// <summary>
    /// The unique ID for this foreach statement.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The loop variable name.
    /// </summary>
    public string VariableName { get; }

    /// <summary>
    /// The type of the loop variable.
    /// </summary>
    public string VariableType { get; }

    /// <summary>
    /// The collection being iterated.
    /// </summary>
    public ExpressionNode Collection { get; }

    /// <summary>
    /// The loop body statements.
    /// </summary>
    public IReadOnlyList<StatementNode> Body { get; }

    public AttributeCollection Attributes { get; }

    public ForeachStatementNode(
        TextSpan span,
        string id,
        string variableName,
        string variableType,
        ExpressionNode collection,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        VariableName = variableName ?? throw new ArgumentNullException(nameof(variableName));
        VariableType = variableType ?? throw new ArgumentNullException(nameof(variableType));
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
