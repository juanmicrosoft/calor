using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

/// <summary>
/// Base class for statement nodes.
/// </summary>
public abstract class StatementNode : AstNode
{
    protected StatementNode(TextSpan span) : base(span) { }
}

/// <summary>
/// Represents a function call statement.
/// §CALL[target=xxx][fallible=xxx]
/// </summary>
public sealed class CallStatementNode : StatementNode
{
    public string Target { get; }
    public bool Fallible { get; }
    public IReadOnlyList<ExpressionNode> Arguments { get; }
    public AttributeCollection Attributes { get; }

    /// <summary>
    /// Optional named argument labels, parallel to Arguments list.
    /// </summary>
    public IReadOnlyList<string?>? ArgumentNames { get; }

    /// <summary>
    /// Optional argument modifiers (ref/out/in), parallel to Arguments list.
    /// Null entry means no modifier; non-null means "ref", "out", or "in".
    /// </summary>
    public IReadOnlyList<string?>? ArgumentModifiers { get; }

    public CallStatementNode(
        TextSpan span,
        string target,
        bool fallible,
        IReadOnlyList<ExpressionNode> arguments,
        AttributeCollection attributes)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Fallible = fallible;
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public CallStatementNode(
        TextSpan span,
        string target,
        bool fallible,
        IReadOnlyList<ExpressionNode> arguments,
        AttributeCollection attributes,
        IReadOnlyList<string?>? argumentNames)
        : this(span, target, fallible, arguments, attributes)
    {
        ArgumentNames = argumentNames;
    }

    public CallStatementNode(
        TextSpan span,
        string target,
        bool fallible,
        IReadOnlyList<ExpressionNode> arguments,
        AttributeCollection attributes,
        IReadOnlyList<string?>? argumentNames,
        IReadOnlyList<string?>? argumentModifiers)
        : this(span, target, fallible, arguments, attributes)
    {
        ArgumentNames = argumentNames;
        ArgumentModifiers = argumentModifiers;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a return statement.
/// §RETURN [expression]
/// </summary>
public sealed class ReturnStatementNode : StatementNode
{
    public ExpressionNode? Expression { get; }

    public ReturnStatementNode(TextSpan span, ExpressionNode? expression)
        : base(span)
    {
        Expression = expression;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a print statement (§P shorthand for Console.WriteLine).
/// §P expression
/// </summary>
public sealed class PrintStatementNode : StatementNode
{
    public ExpressionNode Expression { get; }
    public bool IsWriteLine { get; }  // true for §P (WriteLine), false for §Pf (Write)

    public PrintStatementNode(TextSpan span, ExpressionNode expression, bool isWriteLine = true)
        : base(span)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        IsWriteLine = isWriteLine;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a bare expression used as a statement (e.g., (inc x), (post-dec y)).
/// Emitted as: expression;
/// </summary>
public sealed class ExpressionStatementNode : StatementNode
{
    public ExpressionNode Expression { get; }

    public ExpressionStatementNode(TextSpan span, ExpressionNode expression)
        : base(span)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a fallback comment for unsupported C# statements.
/// Emitted as // TODO: Manual conversion needed [feature] with original C# code.
/// </summary>
public sealed class FallbackCommentNode : StatementNode
{
    /// <summary>
    /// The original C# code that could not be converted.
    /// </summary>
    public string OriginalCSharp { get; }

    /// <summary>
    /// The name of the unsupported feature (e.g., "goto", "stackalloc").
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Optional suggestion for how to manually convert this construct.
    /// </summary>
    public string? Suggestion { get; }

    public FallbackCommentNode(TextSpan span, string originalCSharp, string featureName, string? suggestion = null)
        : base(span)
    {
        OriginalCSharp = originalCSharp ?? throw new ArgumentNullException(nameof(originalCSharp));
        FeatureName = featureName ?? throw new ArgumentNullException(nameof(featureName));
        Suggestion = suggestion;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a raw C# passthrough block.
/// §RAW ... §/RAW
/// Content is emitted verbatim as C# code without any transformation.
/// </summary>
public sealed class RawCSharpNode : StatementNode
{
    /// <summary>
    /// The raw C# source code to emit verbatim.
    /// </summary>
    public string CSharpCode { get; }

    public RawCSharpNode(TextSpan span, string csharpCode)
        : base(span)
    {
        CSharpCode = csharpCode ?? throw new ArgumentNullException(nameof(csharpCode));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a preprocessor conditional block.
/// §PP{CONDITION}
///   ... body ...
/// §/PP{CONDITION}
/// </summary>
public sealed class PreprocessorDirectiveNode : StatementNode
{
    public string Condition { get; }
    public IReadOnlyList<StatementNode> Body { get; }
    public IReadOnlyList<StatementNode>? ElseBody { get; }

    public PreprocessorDirectiveNode(TextSpan span, string condition,
        IReadOnlyList<StatementNode> body, IReadOnlyList<StatementNode>? elseBody = null)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        ElseBody = elseBody;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a preprocessor conditional block wrapping class members.
/// §PP{CONDITION}
///   ... fields, properties, methods, etc. ...
/// §PPE (optional else/elif)
/// §/PP{CONDITION}
/// </summary>
public sealed class MemberPreprocessorBlockNode : AstNode
{
    public string Condition { get; }
    public IReadOnlyList<ClassFieldNode> Fields { get; }
    public IReadOnlyList<PropertyNode> Properties { get; }
    public IReadOnlyList<IndexerNode> Indexers { get; }
    public IReadOnlyList<ConstructorNode> Constructors { get; }
    public IReadOnlyList<MethodNode> Methods { get; }
    public IReadOnlyList<EventDefinitionNode> Events { get; }
    public IReadOnlyList<OperatorOverloadNode> OperatorOverloads { get; }
    public MemberPreprocessorBlockNode? ElseBranch { get; }

    public MemberPreprocessorBlockNode(
        TextSpan span,
        string condition,
        IReadOnlyList<ClassFieldNode> fields,
        IReadOnlyList<PropertyNode> properties,
        IReadOnlyList<ConstructorNode> constructors,
        IReadOnlyList<MethodNode> methods,
        IReadOnlyList<EventDefinitionNode> events,
        IReadOnlyList<OperatorOverloadNode> operatorOverloads,
        MemberPreprocessorBlockNode? elseBranch = null,
        IReadOnlyList<IndexerNode>? indexers = null)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Fields = fields ?? Array.Empty<ClassFieldNode>();
        Properties = properties ?? Array.Empty<PropertyNode>();
        Indexers = indexers ?? Array.Empty<IndexerNode>();
        Constructors = constructors ?? Array.Empty<ConstructorNode>();
        Methods = methods ?? Array.Empty<MethodNode>();
        Events = events ?? Array.Empty<EventDefinitionNode>();
        OperatorOverloads = operatorOverloads ?? Array.Empty<OperatorOverloadNode>();
        ElseBranch = elseBranch;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a preprocessor conditional block wrapping entire type declarations at module level.
/// §PP{CONDITION}
///   ... using directives, class, interface, enum, delegate declarations ...
/// §PPE (optional else/elif)
/// §/PP{CONDITION}
/// </summary>
public sealed class TypePreprocessorBlockNode : AstNode
{
    public string Condition { get; }
    public IReadOnlyList<UsingDirectiveNode> Usings { get; }
    public IReadOnlyList<ClassDefinitionNode> Classes { get; }
    public IReadOnlyList<InterfaceDefinitionNode> Interfaces { get; }
    public IReadOnlyList<EnumDefinitionNode> Enums { get; }
    public IReadOnlyList<DelegateDefinitionNode> Delegates { get; }
    public TypePreprocessorBlockNode? ElseBranch { get; }

    public TypePreprocessorBlockNode(
        TextSpan span,
        string condition,
        IReadOnlyList<ClassDefinitionNode> classes,
        IReadOnlyList<InterfaceDefinitionNode> interfaces,
        IReadOnlyList<EnumDefinitionNode> enums,
        IReadOnlyList<DelegateDefinitionNode> delegates,
        TypePreprocessorBlockNode? elseBranch = null,
        IReadOnlyList<UsingDirectiveNode>? usings = null)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Usings = usings ?? Array.Empty<UsingDirectiveNode>();
        Classes = classes ?? Array.Empty<ClassDefinitionNode>();
        Interfaces = interfaces ?? Array.Empty<InterfaceDefinitionNode>();
        Enums = enums ?? Array.Empty<EnumDefinitionNode>();
        Delegates = delegates ?? Array.Empty<DelegateDefinitionNode>();
        ElseBranch = elseBranch;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Kind of C# member preserved in an interop block.
/// </summary>
public enum InteropMemberKind
{
    Other,
    Method,
    Property,
    Field,
    Constructor,
    Class,
    Event
}

/// <summary>
/// Represents a raw C# interop block at module or class scope.
/// §CSHARP{...}§/CSHARP
/// Used to preserve unsupported C# members verbatim during partial conversion.
/// Unlike RawCSharpNode (statement-level), this is a member-level construct.
/// </summary>
public sealed class CSharpInteropBlockNode : AstNode
{
    /// <summary>
    /// The raw C# source code preserved verbatim.
    /// </summary>
    public string CSharpCode { get; }

    /// <summary>
    /// The unsupported feature that caused this block (e.g., "generics", "async_await").
    /// </summary>
    public string? FeatureName { get; }

    /// <summary>
    /// Human-readable reason for the interop block (e.g., "Generic type parameter on method GetAll&lt;T&gt;").
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// The kind of C# member this block represents.
    /// </summary>
    public InteropMemberKind MemberKind { get; }

    public CSharpInteropBlockNode(TextSpan span, string csharpCode,
        string? featureName = null, string? reason = null,
        InteropMemberKind memberKind = InteropMemberKind.Other)
        : base(span)
    {
        CSharpCode = csharpCode ?? throw new ArgumentNullException(nameof(csharpCode));
        FeatureName = featureName;
        Reason = reason;
        MemberKind = memberKind;
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}
