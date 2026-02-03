using Calor.Compiler.Parsing;

namespace Calor.Compiler.Ast;

#region Phase 1: Quick Wins - Examples and Issues

/// <summary>
/// Represents an inline example/test.
/// §EX (Add 2 3) → 5
/// §EX[ex001:msg:"edge case"] (Add 0 0) → 0
/// </summary>
public sealed class ExampleNode : AstNode
{
    public string? Id { get; }
    public ExpressionNode Expression { get; }
    public ExpressionNode Expected { get; }
    public string? Message { get; }
    public AttributeCollection Attributes { get; }

    public ExampleNode(
        TextSpan span,
        string? id,
        ExpressionNode expression,
        ExpressionNode expected,
        string? message,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id;
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
        Expected = expected ?? throw new ArgumentNullException(nameof(expected));
        Message = message;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Priority levels for issues (TODO, FIXME, HACK).
/// </summary>
public enum IssuePriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Kind of issue marker.
/// </summary>
public enum IssueKind
{
    Todo,
    Fixme,
    Hack
}

/// <summary>
/// Represents a structured issue marker (TODO, FIXME, HACK).
/// §TODO[t001:perf:high] "Optimize for large n"
/// §FIXME[x001:bug:critical] "Integer overflow"
/// §HACK[h001] "Workaround for API bug"
/// </summary>
public sealed class IssueNode : AstNode
{
    public IssueKind Kind { get; }
    public string? Id { get; }
    public string? Category { get; }
    public IssuePriority Priority { get; }
    public string Description { get; }
    public AttributeCollection Attributes { get; }

    public IssueNode(
        TextSpan span,
        IssueKind kind,
        string? id,
        string? category,
        IssuePriority priority,
        string description,
        AttributeCollection attributes)
        : base(span)
    {
        Kind = kind;
        Id = id;
        Category = category;
        Priority = priority;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

#endregion

#region Phase 2: Core Features - Dependencies and Assumptions

/// <summary>
/// Represents a single dependency target in a §USES or §USEDBY declaration.
/// </summary>
public sealed class DependencyNode : AstNode
{
    public string Target { get; }
    public string? Version { get; }
    public bool IsOptional { get; }
    public AttributeCollection Attributes { get; }

    public DependencyNode(
        TextSpan span,
        string target,
        string? version,
        bool isOptional,
        AttributeCollection attributes)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Version = version;
        IsOptional = isOptional;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a dependency declaration.
/// §USES[ValidateOrder, CalculateTotal, SaveOrder]
/// </summary>
public sealed class UsesNode : AstNode
{
    public IReadOnlyList<DependencyNode> Dependencies { get; }
    public AttributeCollection Attributes { get; }

    public UsesNode(
        TextSpan span,
        IReadOnlyList<DependencyNode> dependencies,
        AttributeCollection attributes)
        : base(span)
    {
        Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a reverse dependency declaration.
/// §USEDBY[OrderController.Submit, BatchProcessor.Run]
/// </summary>
public sealed class UsedByNode : AstNode
{
    public IReadOnlyList<DependencyNode> Dependents { get; }
    public bool HasUnknownCallers { get; }
    public AttributeCollection Attributes { get; }

    public UsedByNode(
        TextSpan span,
        IReadOnlyList<DependencyNode> dependents,
        bool hasUnknownCallers,
        AttributeCollection attributes)
        : base(span)
    {
        Dependents = dependents ?? throw new ArgumentNullException(nameof(dependents));
        HasUnknownCallers = hasUnknownCallers;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Category of assumption.
/// </summary>
public enum AssumptionCategory
{
    Env,
    Auth,
    Data,
    Timing,
    Resource
}

/// <summary>
/// Represents an assumption declaration.
/// §ASSUME[env] "Database connection pool initialized"
/// §ASSUME[data] "orderId exists in database"
/// </summary>
public sealed class AssumeNode : AstNode
{
    public AssumptionCategory? Category { get; }
    public string Description { get; }
    public AttributeCollection Attributes { get; }

    public AssumeNode(
        TextSpan span,
        AssumptionCategory? category,
        string description,
        AttributeCollection attributes)
        : base(span)
    {
        Category = category;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

#endregion

#region Phase 3: Enhanced Contracts - Complexity and Versioning

/// <summary>
/// Standard complexity classes.
/// </summary>
public enum ComplexityClass
{
    O1,
    OLogN,
    ON,
    ONLogN,
    ON2,
    ON3,
    O2N,
    ONFact
}

/// <summary>
/// Represents a performance contract.
/// §COMPLEXITY[time:O(n)][space:O(1)]
/// §COMPLEXITY[worst:time:O(n)][worst:space:O(n)]
/// </summary>
public sealed class ComplexityNode : AstNode
{
    public ComplexityClass? TimeComplexity { get; }
    public ComplexityClass? SpaceComplexity { get; }
    public bool IsWorstCase { get; }
    public string? CustomExpression { get; }
    public AttributeCollection Attributes { get; }

    public ComplexityNode(
        TextSpan span,
        ComplexityClass? timeComplexity,
        ComplexityClass? spaceComplexity,
        bool isWorstCase,
        string? customExpression,
        AttributeCollection attributes)
        : base(span)
    {
        TimeComplexity = timeComplexity;
        SpaceComplexity = spaceComplexity;
        IsWorstCase = isWorstCase;
        CustomExpression = customExpression;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents API versioning information.
/// §SINCE[1.0.0]
/// </summary>
public sealed class SinceNode : AstNode
{
    public string Version { get; }
    public AttributeCollection Attributes { get; }

    public SinceNode(
        TextSpan span,
        string version,
        AttributeCollection attributes)
        : base(span)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a deprecation marker.
/// §DEPRECATED[since:1.5.0][use:NewMethod][reason:"Performance issues"]
/// </summary>
public sealed class DeprecatedNode : AstNode
{
    public string SinceVersion { get; }
    public string? Replacement { get; }
    public string? Reason { get; }
    public string? RemovedInVersion { get; }
    public AttributeCollection Attributes { get; }

    public DeprecatedNode(
        TextSpan span,
        string sinceVersion,
        string? replacement,
        string? reason,
        string? removedInVersion,
        AttributeCollection attributes)
        : base(span)
    {
        SinceVersion = sinceVersion ?? throw new ArgumentNullException(nameof(sinceVersion));
        Replacement = replacement;
        Reason = reason;
        RemovedInVersion = removedInVersion;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a breaking change marker.
/// §BREAKING[1.7.0] "Added required 'options' parameter"
/// </summary>
public sealed class BreakingChangeNode : AstNode
{
    public string Version { get; }
    public string Description { get; }
    public AttributeCollection Attributes { get; }

    public BreakingChangeNode(
        TextSpan span,
        string version,
        string description,
        AttributeCollection attributes)
        : base(span)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

#endregion

#region Phase 4: Future Extensions - Decisions, Context, Properties, Collaboration

/// <summary>
/// Represents a rejected option in a decision record.
/// </summary>
public sealed class RejectedOptionNode : AstNode
{
    public string Name { get; }
    public IReadOnlyList<string> Reasons { get; }
    public AttributeCollection Attributes { get; }

    public RejectedOptionNode(
        TextSpan span,
        string name,
        IReadOnlyList<string> reasons,
        AttributeCollection attributes)
        : base(span)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Reasons = reasons ?? throw new ArgumentNullException(nameof(reasons));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents an architectural decision record.
/// §DECISION[d001] "Algorithm selection"
///   §CHOSEN "QuickSort"
///   §REASON "Best average-case performance"
///   §REJECTED "MergeSort"
///     §REASON "Requires O(n) extra space"
///   §CONTEXT "Typical input: 1000-10000 items"
///   §DATE 2024-01-15
///   §AUTHOR "perf-team"
/// §/DECISION[d001]
/// </summary>
public sealed class DecisionNode : AstNode
{
    public string Id { get; }
    public string Title { get; }
    public string ChosenOption { get; }
    public IReadOnlyList<string> ChosenReasons { get; }
    public IReadOnlyList<RejectedOptionNode> RejectedOptions { get; }
    public string? Context { get; }
    public DateOnly? Date { get; }
    public string? Author { get; }
    public AttributeCollection Attributes { get; }

    public DecisionNode(
        TextSpan span,
        string id,
        string title,
        string chosenOption,
        IReadOnlyList<string> chosenReasons,
        IReadOnlyList<RejectedOptionNode> rejectedOptions,
        string? context,
        DateOnly? date,
        string? author,
        AttributeCollection attributes)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        ChosenOption = chosenOption ?? throw new ArgumentNullException(nameof(chosenOption));
        ChosenReasons = chosenReasons ?? throw new ArgumentNullException(nameof(chosenReasons));
        RejectedOptions = rejectedOptions ?? throw new ArgumentNullException(nameof(rejectedOptions));
        Context = context;
        Date = date;
        Author = author;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a file reference in a context marker.
/// §FILE[OrderService.calr]
/// §FILE[PaymentService.calr] "Not loaded - external"
/// </summary>
public sealed class FileRefNode : AstNode
{
    public string FilePath { get; }
    public string? Description { get; }
    public AttributeCollection Attributes { get; }

    public FileRefNode(
        TextSpan span,
        string filePath,
        string? description,
        AttributeCollection attributes)
        : base(span)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        Description = description;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a partial view marker for multi-agent collaboration.
/// §CONTEXT[partial]
///   §VISIBLE
///     §FILE[OrderService.calr]
///   §/VISIBLE
///   §HIDDEN
///     §FILE[PaymentService.calr] "Not loaded - external"
///   §/HIDDEN
///   §FOCUS[OrderService.ProcessOrder]
/// §/CONTEXT
/// </summary>
public sealed class ContextNode : AstNode
{
    public bool IsPartial { get; }
    public IReadOnlyList<FileRefNode> VisibleFiles { get; }
    public IReadOnlyList<FileRefNode> HiddenFiles { get; }
    public string? FocusTarget { get; }
    public AttributeCollection Attributes { get; }

    public ContextNode(
        TextSpan span,
        bool isPartial,
        IReadOnlyList<FileRefNode> visibleFiles,
        IReadOnlyList<FileRefNode> hiddenFiles,
        string? focusTarget,
        AttributeCollection attributes)
        : base(span)
    {
        IsPartial = isPartial;
        VisibleFiles = visibleFiles ?? throw new ArgumentNullException(nameof(visibleFiles));
        HiddenFiles = hiddenFiles ?? throw new ArgumentNullException(nameof(hiddenFiles));
        FocusTarget = focusTarget;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a property-based test declaration.
/// §PROPERTY ∀arr: (== (Reverse (Reverse arr)) arr)
/// §PROPERTY ∀arr: (== (len (Reverse arr)) (len arr))
/// </summary>
public sealed class PropertyTestNode : AstNode
{
    public IReadOnlyList<string> Quantifiers { get; }
    public ExpressionNode Predicate { get; }
    public AttributeCollection Attributes { get; }

    public PropertyTestNode(
        TextSpan span,
        IReadOnlyList<string> quantifiers,
        ExpressionNode predicate,
        AttributeCollection attributes)
        : base(span)
    {
        Quantifiers = quantifiers ?? throw new ArgumentNullException(nameof(quantifiers));
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a multi-agent locking declaration.
/// §LOCK[agent:agent-123][expires:2024-01-15T11:00:00Z]
/// </summary>
public sealed class LockNode : AstNode
{
    public string AgentId { get; }
    public DateTime? Acquired { get; }
    public DateTime? Expires { get; }
    public AttributeCollection Attributes { get; }

    public LockNode(
        TextSpan span,
        string agentId,
        DateTime? acquired,
        DateTime? expires,
        AttributeCollection attributes)
        : base(span)
    {
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        Acquired = acquired;
        Expires = expires;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents agent authorship tracking.
/// §AUTHOR[agent:agent-456][date:2024-01-10][task:PROJ-789]
/// </summary>
public sealed class AuthorNode : AstNode
{
    public string AgentId { get; }
    public DateOnly Date { get; }
    public string? TaskId { get; }
    public AttributeCollection Attributes { get; }

    public AuthorNode(
        TextSpan span,
        string agentId,
        DateOnly date,
        string? taskId,
        AttributeCollection attributes)
        : base(span)
    {
        AgentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        Date = date;
        TaskId = taskId;
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

/// <summary>
/// Represents a task reference.
/// §TASK[PROJ-789] "Implement validation"
/// </summary>
public sealed class TaskRefNode : AstNode
{
    public string TaskId { get; }
    public string Description { get; }
    public AttributeCollection Attributes { get; }

    public TaskRefNode(
        TextSpan span,
        string taskId,
        string description,
        AttributeCollection attributes)
        : base(span)
    {
        TaskId = taskId ?? throw new ArgumentNullException(nameof(taskId));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public override void Accept(IAstVisitor visitor) => visitor.Visit(this);
    public override T Accept<T>(IAstVisitor<T> visitor) => visitor.Visit(this);
}

#endregion
