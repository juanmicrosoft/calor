using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Variance modifier for type parameters.
/// </summary>
public enum VarianceKind
{
    /// <summary>No variance (invariant)</summary>
    None,
    /// <summary>Covariant (out T)</summary>
    Out,
    /// <summary>Contravariant (in T)</summary>
    In
}

/// <summary>
/// Represents a type parameter declaration.
/// New syntax: §F{id:name:pub}&lt;T&gt; or §CL{id:name:pub}&lt;T, U&gt;
/// Legacy: §TP[T] (no longer supported in new code)
/// </summary>
public sealed class TypeParameterNode : AstNode
{
    /// <summary>
    /// The name of the type parameter.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The variance modifier (in/out) for this type parameter.
    /// </summary>
    public VarianceKind Variance { get; }

    /// <summary>
    /// The constraints on this type parameter (from WHERE clauses).
    /// </summary>
    public IReadOnlyList<TypeConstraintNode> Constraints { get; }

    public TypeParameterNode(TextSpan span, string name, IReadOnlyList<TypeConstraintNode> constraints, VarianceKind variance = VarianceKind.None)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));
        Variance = variance;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// The kind of type constraint.
/// </summary>
public enum TypeConstraintKind
{
    /// <summary>Type must be a reference type (class)</summary>
    Class,
    /// <summary>Type must be a value type (struct)</summary>
    Struct,
    /// <summary>Type must have a parameterless constructor (new())</summary>
    New,
    /// <summary>Type must implement an interface</summary>
    Interface,
    /// <summary>Type must derive from a base class</summary>
    BaseClass,
    /// <summary>Type must be or derive from the specified type</summary>
    TypeName
}

/// <summary>
/// Represents a constraint on a type parameter.
/// §WHERE[T:IComparable]                     // where T : IComparable
/// §WHERE[T:class]                           // where T : class
/// §WHERE[T:struct]                          // where T : struct
/// §WHERE[T:new]                             // where T : new()
/// </summary>
public sealed class TypeConstraintNode : AstNode
{
    /// <summary>
    /// The kind of constraint.
    /// </summary>
    public TypeConstraintKind Kind { get; }

    /// <summary>
    /// For Interface, BaseClass, or TypeName constraints, the type name.
    /// </summary>
    public string? TypeName { get; }

    public TypeConstraintNode(TextSpan span, TypeConstraintKind kind, string? typeName = null)
        : base(span)
    {
        Kind = kind;
        TypeName = typeName;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a generic type instantiation.
/// New syntax: Use inline generic syntax in type names: List&lt;i32&gt;, Dictionary&lt;str, i32&gt;
/// Legacy: §G[List:i32] (no longer supported in new code)
/// </summary>
public sealed class GenericTypeNode : ExpressionNode
{
    /// <summary>
    /// The name of the generic type (e.g., "List", "Dictionary").
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// The type arguments.
    /// </summary>
    public IReadOnlyList<string> TypeArguments { get; }

    public GenericTypeNode(TextSpan span, string typeName, IReadOnlyList<string> typeArguments)
        : base(span)
    {
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        TypeArguments = typeArguments ?? throw new ArgumentNullException(nameof(typeArguments));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
