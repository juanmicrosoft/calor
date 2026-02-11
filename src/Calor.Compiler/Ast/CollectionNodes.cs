using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Represents list creation.
/// §LIST{list1:i32}        // List&lt;int&gt;
///   1
///   2
///   3
/// §/LIST{list1}
/// </summary>
public sealed class ListCreationNode : ExpressionNode
{
    /// <summary>
    /// The unique ID for this list declaration.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The variable name for this list.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The element type of the list.
    /// </summary>
    public string ElementType { get; }

    /// <summary>
    /// The initial elements of the list.
    /// </summary>
    public IReadOnlyList<ExpressionNode> Elements { get; }

    public AttributeCollection Attributes { get; }

    public ListCreationNode(
        TextSpan span,
        string id,
        string name,
        string elementType,
        IReadOnlyList<ExpressionNode> elements,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a key-value pair for dictionary initialization.
/// §KV "key" value
/// </summary>
public sealed class KeyValuePairNode : AstNode
{
    /// <summary>
    /// The key expression.
    /// </summary>
    public ExpressionNode Key { get; }

    /// <summary>
    /// The value expression.
    /// </summary>
    public ExpressionNode Value { get; }

    public KeyValuePairNode(TextSpan span, ExpressionNode key, ExpressionNode value)
        : base(span)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents dictionary creation.
/// §DICT{dict1:str:i32}    // Dictionary&lt;string, int&gt;
///   §KV "one" 1
///   §KV "two" 2
/// §/DICT{dict1}
/// </summary>
public sealed class DictionaryCreationNode : ExpressionNode
{
    /// <summary>
    /// The unique ID for this dictionary declaration.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The variable name for this dictionary.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The key type of the dictionary.
    /// </summary>
    public string KeyType { get; }

    /// <summary>
    /// The value type of the dictionary.
    /// </summary>
    public string ValueType { get; }

    /// <summary>
    /// The initial key-value pairs.
    /// </summary>
    public IReadOnlyList<KeyValuePairNode> Entries { get; }

    public AttributeCollection Attributes { get; }

    public DictionaryCreationNode(
        TextSpan span,
        string id,
        string name,
        string keyType,
        string valueType,
        IReadOnlyList<KeyValuePairNode> entries,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents HashSet creation.
/// §SET{set1:str}          // HashSet&lt;string&gt;
///   "apple"
///   "banana"
/// §/SET{set1}
/// </summary>
public sealed class SetCreationNode : ExpressionNode
{
    /// <summary>
    /// The unique ID for this set declaration.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The variable name for this set.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The element type of the set.
    /// </summary>
    public string ElementType { get; }

    /// <summary>
    /// The initial elements of the set.
    /// </summary>
    public IReadOnlyList<ExpressionNode> Elements { get; }

    public AttributeCollection Attributes { get; }

    public SetCreationNode(
        TextSpan span,
        string id,
        string name,
        string elementType,
        IReadOnlyList<ExpressionNode> elements,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents adding an element to a list or set.
/// §PUSH{list1} value      // list.Add(value)
/// </summary>
public sealed class CollectionPushNode : StatementNode
{
    /// <summary>
    /// The collection variable name.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The value to add.
    /// </summary>
    public ExpressionNode Value { get; }

    public CollectionPushNode(TextSpan span, string collectionName, ExpressionNode value)
        : base(span)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents adding/updating a dictionary entry.
/// §PUT{dict1} key value   // dict[key] = value
/// </summary>
public sealed class DictionaryPutNode : StatementNode
{
    /// <summary>
    /// The dictionary variable name.
    /// </summary>
    public string DictionaryName { get; }

    /// <summary>
    /// The key expression.
    /// </summary>
    public ExpressionNode Key { get; }

    /// <summary>
    /// The value expression.
    /// </summary>
    public ExpressionNode Value { get; }

    public DictionaryPutNode(TextSpan span, string dictionaryName, ExpressionNode key, ExpressionNode value)
        : base(span)
    {
        DictionaryName = dictionaryName ?? throw new ArgumentNullException(nameof(dictionaryName));
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents removing an element from a collection.
/// §REM{coll} keyOrValue   // coll.Remove(keyOrValue)
/// </summary>
public sealed class CollectionRemoveNode : StatementNode
{
    /// <summary>
    /// The collection variable name.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The key or value to remove.
    /// </summary>
    public ExpressionNode KeyOrValue { get; }

    public CollectionRemoveNode(TextSpan span, string collectionName, ExpressionNode keyOrValue)
        : base(span)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        KeyOrValue = keyOrValue ?? throw new ArgumentNullException(nameof(keyOrValue));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents setting a list element by index.
/// §SETIDX{list1} index value  // list[index] = value
/// </summary>
public sealed class CollectionSetIndexNode : StatementNode
{
    /// <summary>
    /// The list variable name.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The index expression.
    /// </summary>
    public ExpressionNode Index { get; }

    /// <summary>
    /// The value expression.
    /// </summary>
    public ExpressionNode Value { get; }

    public CollectionSetIndexNode(TextSpan span, string collectionName, ExpressionNode index, ExpressionNode value)
        : base(span)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        Index = index ?? throw new ArgumentNullException(nameof(index));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents clearing a collection.
/// §CLR{coll}              // coll.Clear()
/// </summary>
public sealed class CollectionClearNode : StatementNode
{
    /// <summary>
    /// The collection variable name.
    /// </summary>
    public string CollectionName { get; }

    public CollectionClearNode(TextSpan span, string collectionName)
        : base(span)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents inserting an element at an index.
/// §INS{list1} index value // list.Insert(index, value)
/// </summary>
public sealed class CollectionInsertNode : StatementNode
{
    /// <summary>
    /// The list variable name.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The index expression.
    /// </summary>
    public ExpressionNode Index { get; }

    /// <summary>
    /// The value expression.
    /// </summary>
    public ExpressionNode Value { get; }

    public CollectionInsertNode(TextSpan span, string collectionName, ExpressionNode index, ExpressionNode value)
        : base(span)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        Index = index ?? throw new ArgumentNullException(nameof(index));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// The mode for collection contains checks.
/// </summary>
public enum ContainsMode
{
    /// <summary>
    /// Check for value in list/set using .Contains().
    /// </summary>
    Value,

    /// <summary>
    /// Check for key in dictionary using .ContainsKey().
    /// </summary>
    Key,

    /// <summary>
    /// Check for value in dictionary using .ContainsValue().
    /// </summary>
    DictValue
}

/// <summary>
/// Represents a contains check on a collection.
/// §HAS{list1} value           // list.Contains(value)
/// §HAS{dict1} §KEY key        // dict.ContainsKey(key)
/// §HAS{dict1} §VAL value      // dict.ContainsValue(value)
/// </summary>
public sealed class CollectionContainsNode : ExpressionNode
{
    /// <summary>
    /// The collection variable name.
    /// </summary>
    public string CollectionName { get; }

    /// <summary>
    /// The key or value to check.
    /// </summary>
    public ExpressionNode KeyOrValue { get; }

    /// <summary>
    /// The contains check mode.
    /// </summary>
    public ContainsMode Mode { get; }

    public CollectionContainsNode(TextSpan span, string collectionName, ExpressionNode keyOrValue, ContainsMode mode)
        : base(span)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        KeyOrValue = keyOrValue ?? throw new ArgumentNullException(nameof(keyOrValue));
        Mode = mode;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents dictionary iteration with key-value pairs.
/// §EACHKV{e2:key:value} dict1  // foreach (var (key, value) in dict1)
///   §P (+ key ": " value)
/// §/EACHKV{e2}
/// </summary>
public sealed class DictionaryForeachNode : StatementNode
{
    /// <summary>
    /// The unique ID for this foreach statement.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The key variable name.
    /// </summary>
    public string KeyName { get; }

    /// <summary>
    /// The value variable name.
    /// </summary>
    public string ValueName { get; }

    /// <summary>
    /// The dictionary being iterated.
    /// </summary>
    public ExpressionNode Dictionary { get; }

    /// <summary>
    /// The loop body statements.
    /// </summary>
    public IReadOnlyList<StatementNode> Body { get; }

    public AttributeCollection Attributes { get; }

    public DictionaryForeachNode(
        TextSpan span,
        string id,
        string keyName,
        string valueName,
        ExpressionNode dictionary,
        IReadOnlyList<StatementNode> body,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        KeyName = keyName ?? throw new ArgumentNullException(nameof(keyName));
        ValueName = valueName ?? throw new ArgumentNullException(nameof(valueName));
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents collection count access.
/// §CNT collection                   // collection.Count
/// </summary>
public sealed class CollectionCountNode : ExpressionNode
{
    /// <summary>
    /// The collection whose count is being accessed.
    /// </summary>
    public ExpressionNode Collection { get; }

    public CollectionCountNode(TextSpan span, ExpressionNode collection)
        : base(span)
    {
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
