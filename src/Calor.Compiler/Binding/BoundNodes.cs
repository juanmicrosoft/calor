using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Binding;

/// <summary>
/// Base class for all bound nodes.
/// </summary>
public abstract class BoundNode
{
    public TextSpan Span { get; }
    public virtual IEnumerable<BoundNode> ChildNodes => Array.Empty<BoundNode>();

    protected BoundNode(TextSpan span)
    {
        Span = span;
    }
}

/// <summary>
/// Base class for bound statements.
/// </summary>
public abstract class BoundStatement : BoundNode
{
    protected BoundStatement(TextSpan span) : base(span) { }
}

/// <summary>
/// Base class for bound expressions.
/// </summary>
public abstract class BoundExpression : BoundNode
{
    public abstract string TypeName { get; }
    public virtual IReadOnlyList<BoundExpression> Children => Array.Empty<BoundExpression>();
    public virtual IReadOnlyList<BoundExpression> DeferredChildren => Array.Empty<BoundExpression>();
    public override IEnumerable<BoundNode> ChildNodes => Children;

    protected BoundExpression(TextSpan span) : base(span) { }
}

/// <summary>
/// Bound module containing bound functions.
/// </summary>
public sealed class BoundModule : BoundNode
{
    public string Name { get; }
    public IReadOnlyList<BoundFunction> Functions { get; }
    public IReadOnlyDictionary<SymbolId, Symbol> SymbolsById { get; }
    public override IEnumerable<BoundNode> ChildNodes => Functions;

    public BoundModule(
        TextSpan span,
        string name,
        IReadOnlyList<BoundFunction> functions,
        IReadOnlyDictionary<SymbolId, Symbol>? symbolsById = null)
        : base(span)
    {
        Name = name;
        Functions = functions;
        SymbolsById = symbolsById ?? new Dictionary<SymbolId, Symbol>();
    }
}

/// <summary>
/// Classifies what kind of member a BoundFunction represents.
/// This is a pragmatic trade-off: an 11-variant enum pattern-matched across analysis passes.
/// If it grows beyond ~15 values or needs kind-specific fields, refactor to an ADT.
/// </summary>
public enum BoundMemberKind
{
    TopLevelFunction,
    Method,
    Constructor,
    PropertyGetter,
    PropertySetter,
    PropertyInit,
    OperatorOverload,
    IndexerGetter,
    IndexerSetter,
    EventAdd,
    EventRemove
}

/// <summary>
/// Bound function with resolved symbols. Also used to represent class members
/// (methods, constructors, property accessors, operators, indexers, events).
/// </summary>
public sealed class BoundFunction : BoundNode
{
    public FunctionSymbol Symbol { get; }
    public SymbolId SymbolId => Symbol.Id;
    public IReadOnlyList<BoundStatement> Body { get; }
    public Scope Scope { get; }
    /// <summary>
    /// Declared effects for this function (e.g., "db:w", "fs:rw").
    /// Used by taint analysis for effect-based sink detection.
    /// </summary>
    public IReadOnlyList<string> DeclaredEffects { get; }

    /// <summary>
    /// What kind of member this bound function represents.
    /// </summary>
    public BoundMemberKind MemberKind { get; }

    /// <summary>
    /// The name of the containing type, or null for top-level functions.
    /// </summary>
    public string? ContainingTypeName { get; }
    public override IEnumerable<BoundNode> ChildNodes => Body;

    public BoundFunction(TextSpan span, FunctionSymbol symbol, IReadOnlyList<BoundStatement> body, Scope scope)
        : this(span, symbol, body, scope, Array.Empty<string>(), BoundMemberKind.TopLevelFunction, null)
    {
    }

    public BoundFunction(TextSpan span, FunctionSymbol symbol, IReadOnlyList<BoundStatement> body, Scope scope, IReadOnlyList<string> declaredEffects)
        : this(span, symbol, body, scope, declaredEffects, BoundMemberKind.TopLevelFunction, null)
    {
    }

    public BoundFunction(TextSpan span, FunctionSymbol symbol, IReadOnlyList<BoundStatement> body, Scope scope,
        IReadOnlyList<string> declaredEffects, BoundMemberKind memberKind, string? containingTypeName)
        : base(span)
    {
        Symbol = symbol;
        Body = body;
        Scope = scope;
        DeclaredEffects = declaredEffects ?? Array.Empty<string>();
        MemberKind = memberKind;
        ContainingTypeName = containingTypeName;
    }
}

/// <summary>
/// Bound variable declaration.
/// </summary>
public sealed class BoundBindStatement : BoundStatement
{
    public VariableSymbol Variable { get; }
    public SymbolId SymbolId => Variable.Id;
    public BoundExpression? Initializer { get; }
    public override IEnumerable<BoundNode> ChildNodes =>
        Initializer != null ? [Initializer] : Array.Empty<BoundNode>();

    public BoundBindStatement(TextSpan span, VariableSymbol variable, BoundExpression? initializer)
        : base(span)
    {
        Variable = variable;
        Initializer = initializer;
    }
}

/// <summary>
/// Bound variable reference.
/// </summary>
public sealed class BoundVariableExpression : BoundExpression
{
    public VariableSymbol Variable { get; }
    public SymbolId SymbolId => Variable.Id;
    public override string TypeName => Variable.TypeName;

    public BoundVariableExpression(TextSpan span, VariableSymbol variable)
        : base(span)
    {
        Variable = variable;
    }
}

/// <summary>
/// Bound call statement.
/// </summary>
public sealed class BoundCallStatement : BoundStatement
{
    public string Target { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public FunctionSymbol? ResolvedSymbol { get; }
    public SymbolId? ResolvedSymbolId => ResolvedSymbol?.Id;
    public IReadOnlyList<FunctionSymbol> ResolvedSymbols { get; }
    public VariableSymbol? ReceiverSymbol { get; }
    public SymbolId? ReceiverSymbolId => ReceiverSymbol?.Id;
    public TextSpan CalleeSpan { get; }
    public TextSpan? ReceiverSpan { get; }
    public bool IsInaccessibleCall { get; }
    public IReadOnlyList<string?>? ArgumentNames { get; }
    public IReadOnlyList<string?>? ArgumentModifiers { get; }
    public override IEnumerable<BoundNode> ChildNodes => Arguments;

    public BoundCallStatement(
        TextSpan span,
        string target,
        IReadOnlyList<BoundExpression> arguments,
        FunctionSymbol? resolvedSymbol = null,
        IReadOnlyList<string?>? argumentNames = null,
        IReadOnlyList<string?>? argumentModifiers = null,
        VariableSymbol? receiverSymbol = null,
        IReadOnlyList<FunctionSymbol>? resolvedSymbols = null,
        TextSpan? calleeSpan = null,
        TextSpan? receiverSpan = null,
        bool isInaccessibleCall = false)
        : base(span)
    {
        Target = target;
        Arguments = arguments;
        ResolvedSymbol = resolvedSymbol;
        ResolvedSymbols = resolvedSymbols
            ?? (resolvedSymbol == null ? Array.Empty<FunctionSymbol>() : [resolvedSymbol]);
        ArgumentNames = argumentNames;
        ArgumentModifiers = argumentModifiers;
        ReceiverSymbol = receiverSymbol;
        CalleeSpan = calleeSpan ?? span;
        ReceiverSpan = receiverSpan;
        IsInaccessibleCall = isInaccessibleCall;
    }
}

/// <summary>
/// Bound return statement.
/// </summary>
public sealed class BoundReturnStatement : BoundStatement
{
    public BoundExpression? Expression { get; }
    public override IEnumerable<BoundNode> ChildNodes =>
        Expression != null ? [Expression] : Array.Empty<BoundNode>();

    public BoundReturnStatement(TextSpan span, BoundExpression? expression)
        : base(span)
    {
        Expression = expression;
    }
}

/// <summary>
/// Bound for loop.
/// </summary>
public sealed class BoundForStatement : BoundStatement
{
    public VariableSymbol LoopVariable { get; }
    public BoundExpression From { get; }
    public BoundExpression To { get; }
    public BoundExpression? Step { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public override IEnumerable<BoundNode> ChildNodes
    {
        get
        {
            yield return From;
            yield return To;
            if (Step != null)
                yield return Step;
            foreach (var statement in Body)
                yield return statement;
        }
    }

    public BoundForStatement(
        TextSpan span,
        VariableSymbol loopVariable,
        BoundExpression from,
        BoundExpression to,
        BoundExpression? step,
        IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        LoopVariable = loopVariable;
        From = from;
        To = to;
        Step = step;
        Body = body;
    }
}

/// <summary>
/// Bound while loop.
/// </summary>
public sealed class BoundWhileStatement : BoundStatement
{
    public BoundExpression Condition { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Condition, .. Body];

    public BoundWhileStatement(TextSpan span, BoundExpression condition, IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        Condition = condition;
        Body = body;
    }
}

/// <summary>
/// Bound if statement.
/// </summary>
public sealed class BoundIfStatement : BoundStatement
{
    public BoundExpression Condition { get; }
    public IReadOnlyList<BoundStatement> ThenBody { get; }
    public IReadOnlyList<BoundElseIfClause> ElseIfClauses { get; }
    public IReadOnlyList<BoundStatement>? ElseBody { get; }
    public override IEnumerable<BoundNode> ChildNodes
    {
        get
        {
            yield return Condition;
            foreach (var statement in ThenBody)
                yield return statement;
            foreach (var clause in ElseIfClauses)
                yield return clause;
            if (ElseBody != null)
            {
                foreach (var statement in ElseBody)
                    yield return statement;
            }
        }
    }

    public BoundIfStatement(
        TextSpan span,
        BoundExpression condition,
        IReadOnlyList<BoundStatement> thenBody,
        IReadOnlyList<BoundElseIfClause> elseIfClauses,
        IReadOnlyList<BoundStatement>? elseBody)
        : base(span)
    {
        Condition = condition;
        ThenBody = thenBody;
        ElseIfClauses = elseIfClauses;
        ElseBody = elseBody;
    }
}

/// <summary>
/// Bound else-if clause.
/// </summary>
public sealed class BoundElseIfClause : BoundNode
{
    public BoundExpression Condition { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Condition, .. Body];

    public BoundElseIfClause(TextSpan span, BoundExpression condition, IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        Condition = condition;
        Body = body;
    }
}

/// <summary>
/// Bound binary operation.
/// </summary>
public sealed class BoundBinaryExpression : BoundExpression
{
    public BinaryOperator Operator { get; }
    public BoundExpression Left { get; }
    public BoundExpression Right { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }

    public BoundBinaryExpression(
        TextSpan span,
        BinaryOperator op,
        BoundExpression left,
        BoundExpression right,
        string resultType)
        : base(span)
    {
        Operator = op;
        Left = left;
        Right = right;
        TypeName = resultType;
        Children = [left, right];
    }
}

/// <summary>
/// Bound integer literal.
/// </summary>
public sealed class BoundIntLiteral : BoundExpression
{
    public long Value { get; }
    public ulong UnsignedValue { get; }
    public bool IsUnsigned { get; }
    public override string TypeName { get; }

    public BoundIntLiteral(TextSpan span, long value)
        : this(
            span,
            value,
            unchecked((ulong)value),
            isUnsigned: false,
            value is > int.MaxValue or < int.MinValue ? "LONG" : "INT")
    {
    }

    public BoundIntLiteral(
        TextSpan span,
        long value,
        ulong unsignedValue,
        bool isUnsigned,
        string typeName)
        : base(span)
    {
        Value = value;
        UnsignedValue = unsignedValue;
        IsUnsigned = isUnsigned;
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
    }
}

/// <summary>
/// Bound string literal.
/// </summary>
public sealed class BoundStringLiteral : BoundExpression
{
    public string Value { get; }
    public bool IsMultiline { get; }
    public bool IsUtf8 { get; }
    public override string TypeName { get; }

    public BoundStringLiteral(TextSpan span, string value)
        : this(span, value, isMultiline: false, isUtf8: false)
    {
    }

    public BoundStringLiteral(
        TextSpan span,
        string value,
        bool isMultiline,
        bool isUtf8)
        : base(span)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        IsMultiline = isMultiline;
        IsUtf8 = isUtf8;
        TypeName = isUtf8 ? "ReadOnlySpan<BYTE>" : "STRING";
    }
}

/// <summary>
/// Bound boolean literal.
/// </summary>
public sealed class BoundBoolLiteral : BoundExpression
{
    public bool Value { get; }
    public override string TypeName => "BOOL";

    public BoundBoolLiteral(TextSpan span, bool value)
        : base(span)
    {
        Value = value;
    }
}

/// <summary>
/// Bound float literal.
/// </summary>
public sealed class BoundFloatLiteral : BoundExpression
{
    public double Value { get; }
    public override string TypeName { get; }

    public BoundFloatLiteral(TextSpan span, double value)
        : this(span, value, "FLOAT")
    {
    }

    public BoundFloatLiteral(TextSpan span, double value, string typeName)
        : base(span)
    {
        Value = value;
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
    }
}

/// <summary>
/// Bound decimal literal. The decimal payload is retained exactly and is never
/// round-tripped through double.
/// </summary>
public sealed class BoundDecimalLiteral : BoundExpression
{
    public decimal Value { get; }
    public override string TypeName => "DECIMAL";

    public BoundDecimalLiteral(TextSpan span, decimal value)
        : base(span)
    {
        Value = value;
    }
}

/// <summary>
/// Bound None literal (Option.None / null).
/// </summary>
public sealed class BoundNoneLiteral : BoundExpression
{
    public override string TypeName { get; }

    public BoundNoneLiteral(TextSpan span, string? optionType = null) : base(span)
    {
        TypeName = optionType ?? "NONE";
    }
}

/// <summary>
/// Bound unary operation.
/// </summary>
public sealed class BoundUnaryExpression : BoundExpression
{
    public Ast.UnaryOperator Operator { get; }
    public BoundExpression Operand { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }

    public BoundUnaryExpression(TextSpan span, Ast.UnaryOperator op, BoundExpression operand, string resultType)
        : base(span)
    {
        Operator = op;
        Operand = operand;
        TypeName = resultType;
        Children = [operand];
    }
}

/// <summary>
/// Bound call expression.
/// </summary>
public class BoundCallExpression : BoundExpression
{
    public string Target { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public FunctionSymbol? ResolvedSymbol { get; }
    public SymbolId? ResolvedSymbolId => ResolvedSymbol?.Id;
    public IReadOnlyList<FunctionSymbol> ResolvedSymbols { get; }
    public VariableSymbol? ReceiverSymbol { get; }
    public SymbolId? ReceiverSymbolId => ReceiverSymbol?.Id;
    public TextSpan CalleeSpan { get; }
    public TextSpan? ReceiverSpan { get; }
    public bool IsInaccessibleCall { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => Arguments;

    /// <summary>
    /// Named argument labels, argument modifiers, and explicit generic arguments
    /// are retained so a later overload/SymbolId pass can resolve the exact call.
    /// </summary>
    public IReadOnlyList<string?>? ArgumentNames { get; }
    public IReadOnlyList<string?>? ArgumentModifiers { get; }
    public IReadOnlyList<string>? TypeArguments { get; }

    /// <summary>
    /// Fully-qualified type name resolved during binding (e.g., "System.Console").
    /// Null if the type could not be resolved.
    /// </summary>
    public string? ResolvedTypeName { get; }

    /// <summary>
    /// Method name resolved during binding (e.g., "WriteLine").
    /// Null if the method could not be resolved.
    /// </summary>
    public string? ResolvedMethodName { get; }

    /// <summary>
    /// Parameter types resolved during binding (e.g., ["System.String"]).
    /// Null if parameter types could not be resolved.
    /// </summary>
    public IReadOnlyList<string>? ResolvedParameterTypes { get; }

    public BoundCallExpression(
        TextSpan span,
        string target,
        IReadOnlyList<BoundExpression> arguments,
        string resultType,
        string? resolvedTypeName = null,
        string? resolvedMethodName = null,
        IReadOnlyList<string>? resolvedParameterTypes = null,
        IReadOnlyList<string?>? argumentNames = null,
        IReadOnlyList<string?>? argumentModifiers = null,
        IReadOnlyList<string>? typeArguments = null,
        FunctionSymbol? resolvedSymbol = null,
        VariableSymbol? receiverSymbol = null,
        IReadOnlyList<FunctionSymbol>? resolvedSymbols = null,
        TextSpan? calleeSpan = null,
        TextSpan? receiverSpan = null,
        bool isInaccessibleCall = false)
        : base(span)
    {
        Target = target;
        Arguments = arguments;
        ResolvedSymbol = resolvedSymbol;
        ResolvedSymbols = resolvedSymbols
            ?? (resolvedSymbol == null ? Array.Empty<FunctionSymbol>() : [resolvedSymbol]);
        TypeName = resultType;
        ResolvedTypeName = resolvedTypeName;
        ResolvedMethodName = resolvedMethodName;
        ResolvedParameterTypes = resolvedParameterTypes;
        ArgumentNames = argumentNames;
        ArgumentModifiers = argumentModifiers;
        TypeArguments = typeArguments;
        ReceiverSymbol = receiverSymbol;
        CalleeSpan = calleeSpan ?? span;
        ReceiverSpan = receiverSpan;
        IsInaccessibleCall = isInaccessibleCall;
    }
}

/// <summary>
/// #762 B1: an accepted expression the binder cannot yet bind structurally. Subclasses
/// BoundCallExpression with the historical zero-child "&lt;unsupported:TypeName&gt;" shape so
/// every existing checker's pattern-match and traversal behavior is BIT-IDENTICAL to the
/// old fallback (the B1 "zero checker behavior change" exit criterion, by construction) —
/// the node stays an opaque non-constant value (the div-by-zero lesson). What B1 adds is
/// the type itself (so later phases can attach children and deferred-evaluation marking
/// without another checker-visible shape change) and the Calor0259 diagnostic that carries
/// the incomplete-fraction instrument. Children arrive per family PR; Tier-B residuals get
/// explicit extractors in B8 (scoping doc D2).
/// </summary>
public sealed class BoundIncompleteExpression : BoundCallExpression
{
    /// <summary>The concrete ExpressionNode class that lacked a binder.</summary>
    public string NodeTypeName { get; }

    /// <summary>Why this class is incomplete (F-1 tier reason or "family PR pending").</summary>
    public string Reason { get; }

    public BoundIncompleteExpression(TextSpan span, string nodeTypeName, string reason)
        : base(span, $"<unsupported:{nodeTypeName}>", Array.Empty<BoundExpression>(), "OBJECT")
    {
        NodeTypeName = nodeTypeName;
        Reason = reason;
    }
}

/// <summary>A bound name→value pair (anonymous-object, record, and with-expression members).
/// Carries the assignment's own span so checkers can point at the field, not just its value.</summary>
public sealed record BoundNamedValue(string Name, BoundExpression Value, TextSpan Span);

/// <summary>
/// Compatibility entry point for B-series analyses. Child enumeration is owned by each
/// node's universal <see cref="BoundExpression.Children"/> contract.
/// </summary>
public static class BoundChildren
{
    public static IEnumerable<BoundExpression> Of(BoundExpression expression) =>
        expression.Children;

    public static IEnumerable<BoundExpression> DeferredOf(BoundExpression expression) =>
        expression.DeferredChildren;
}

/// <summary>#762 B2: Option construction. Type composes from the payload (string types, D3).</summary>
public sealed class BoundSomeExpression : BoundExpression
{
    public BoundExpression Value { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => [Value];

    public BoundSomeExpression(TextSpan span, BoundExpression value) : base(span)
    {
        Value = value;
        TypeName = $"Option<{value.TypeName}>";
    }
}

/// <summary>#762 B2: Result success construction. The error type parameter is unknowable
/// from the value alone under 0.13's string types — "OBJECT" is the explicit placeholder
/// (never null, per D3); 0.14's typed representation replaces it.</summary>
public sealed class BoundOkExpression : BoundExpression
{
    public BoundExpression Value { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => [Value];

    public BoundOkExpression(TextSpan span, BoundExpression value) : base(span)
    {
        Value = value;
        TypeName = $"Result<{value.TypeName}, OBJECT>";
    }
}

/// <summary>#762 B2: Result error construction (see BoundOkExpression on the placeholder).</summary>
public sealed class BoundErrExpression : BoundExpression
{
    public BoundExpression Error { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => [Error];

    public BoundErrExpression(TextSpan span, BoundExpression error) : base(span)
    {
        Error = error;
        TypeName = $"Result<OBJECT, {error.TypeName}>";
    }
}

/// <summary>#762 B2: invocation of a function VALUE (computed target). Return type is
/// unknowable without function types (0.14); "OBJECT" is the explicit placeholder.</summary>
public sealed class BoundExpressionCall : BoundExpression
{
    public BoundExpression Target { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public override string TypeName => "OBJECT";
    public override IReadOnlyList<BoundExpression> Children => [Target, .. Arguments];

    public BoundExpressionCall(TextSpan span, BoundExpression target,
        IReadOnlyList<BoundExpression> arguments) : base(span)
    {
        Target = target;
        Arguments = arguments;
    }
}

/// <summary>#762 B2: anonymous object creation — member values are bound children.</summary>
public sealed class BoundAnonymousObjectCreation : BoundExpression
{
    public IReadOnlyList<BoundNamedValue> Initializers { get; }
    public override string TypeName => "OBJECT";
    public override IReadOnlyList<BoundExpression> Children =>
        Initializers.Select(initializer => initializer.Value).ToArray();

    public BoundAnonymousObjectCreation(TextSpan span, IReadOnlyList<BoundNamedValue> initializers)
        : base(span) => Initializers = initializers;
}

/// <summary>#762 B2: record creation — typed by the record name; field values bound.</summary>
public sealed class BoundRecordCreation : BoundExpression
{
    public IReadOnlyList<BoundNamedValue> Fields { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children =>
        Fields.Select(item => item.Value).ToArray();

    public BoundRecordCreation(TextSpan span, string typeName, IReadOnlyList<BoundNamedValue> fields)
        : base(span)
    {
        TypeName = typeName;
        Fields = fields;
    }
}

/// <summary>#762 B2: with-expression — same type as its target; assignments bound.</summary>
public sealed class BoundWithExpression : BoundExpression
{
    public BoundExpression Target { get; }
    public IReadOnlyList<BoundNamedValue> Assignments { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children =>
        [Target, .. Assignments.Select(assignment => assignment.Value)];

    public BoundWithExpression(TextSpan span, BoundExpression target,
        IReadOnlyList<BoundNamedValue> assignments) : base(span)
    {
        Target = target;
        Assignments = assignments;
        TypeName = target.TypeName;
    }
}

/// <summary>#762 B2: throw-expression — never produces a value; "NEVER" is the explicit type.</summary>
public sealed class BoundThrowExpression : BoundExpression
{
    public BoundExpression Exception { get; }
    public override string TypeName => "NEVER";
    public override IReadOnlyList<BoundExpression> Children => [Exception];

    public BoundThrowExpression(TextSpan span, BoundExpression exception) : base(span)
        => Exception = exception;
}

/// <summary>A bound key→value expression pair (dictionary entries).</summary>
public sealed record BoundPair(BoundExpression Key, BoundExpression Value, TextSpan Span);

/// <summary>#762 B3: array creation — size and initializers are bound children.</summary>
public sealed class BoundArrayCreation : BoundExpression
{
    public string Id { get; }
    public string Name { get; }
    public string ElementType { get; }
    public BoundExpression? Size { get; }
    public IReadOnlyList<BoundExpression> Initializer { get; }
    public AttributeCollection Attributes { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children =>
        Size == null ? Initializer : [Size, .. Initializer];

    public BoundArrayCreation(TextSpan span, string id, string name, string elementType,
        BoundExpression? size, IReadOnlyList<BoundExpression> initializer,
        AttributeCollection? attributes = null) : base(span)
    {
        Id = id; Name = name; ElementType = elementType; Size = size; Initializer = initializer;
        Attributes = attributes ?? new AttributeCollection();
        TypeName = $"{elementType}[]";
    }
}

/// <summary>#762 B3: array element access. Element type derives from the array's
/// composed type string when it has the "T[]" shape; "OBJECT" otherwise (0.13 string
/// types — 0.14's typed representation replaces the derivation).</summary>
public sealed class BoundArrayAccess : BoundExpression
{
    public BoundExpression Array { get; }
    public BoundExpression Index { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => [Array, Index];

    public BoundArrayAccess(TextSpan span, BoundExpression array, BoundExpression index) : base(span)
    {
        Array = array; Index = index;
        TypeName = array.TypeName.EndsWith("[]", StringComparison.Ordinal)
            ? array.TypeName[..^2]
            : "OBJECT";
    }
}

/// <summary>#762 B3: array length — always INT.</summary>
public sealed class BoundArrayLength : BoundExpression
{
    public BoundExpression Array { get; }
    public override string TypeName => "INT";
    public override IReadOnlyList<BoundExpression> Children => [Array];

    public BoundArrayLength(TextSpan span, BoundExpression array) : base(span) => Array = array;
}

/// <summary>#762 B3: multi-dimensional array creation.</summary>
public sealed class BoundMultiDimArrayCreation : BoundExpression
{
    public string Id { get; }
    public string Name { get; }
    public string ElementType { get; }
    public int Rank { get; }
    public IReadOnlyList<BoundExpression> DimensionSizes { get; }
    /// <summary>Row structure RETAINED (B3 review M2): rectangularity/shape-vs-rank
    /// validation needs it; flattening happens only in BoundChildren.Of.</summary>
    public IReadOnlyList<IReadOnlyList<BoundExpression>> InitializerRows { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children =>
        [.. DimensionSizes, .. InitializerRows.SelectMany(row => row)];

    public BoundMultiDimArrayCreation(TextSpan span, string id, string name, string elementType,
        int rank, IReadOnlyList<BoundExpression> dimensionSizes,
        IReadOnlyList<IReadOnlyList<BoundExpression>> initializerRows)
        : base(span)
    {
        Id = id; Name = name; ElementType = elementType; Rank = rank;
        DimensionSizes = dimensionSizes; InitializerRows = initializerRows;
        TypeName = $"{elementType}[{new string(',', Math.Max(0, rank - 1))}]";
    }
}

/// <summary>#762 B3: multi-dimensional array access.</summary>
public sealed class BoundMultiDimArrayAccess : BoundExpression
{
    public BoundExpression Array { get; }
    public IReadOnlyList<BoundExpression> Indices { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => [Array, .. Indices];

    public BoundMultiDimArrayAccess(TextSpan span, BoundExpression array,
        IReadOnlyList<BoundExpression> indices) : base(span)
    {
        Array = array; Indices = indices;
        // LastIndexOf: on jagged shapes like "i32[][,]" the element type is
        // everything before the TRAILING bracket group ("i32[]"), not "i32" (B3 review m5).
        var t = array.TypeName;
        var open = t.LastIndexOf('[');
        TypeName = open > 0 ? t[..open] : "OBJECT";
    }
}

/// <summary>#762 B3: index-from-end (^n).</summary>
public sealed class BoundIndexFromEnd : BoundExpression
{
    public BoundExpression Offset { get; }
    public override string TypeName => "INDEX";
    public override IReadOnlyList<BoundExpression> Children => [Offset];

    public BoundIndexFromEnd(TextSpan span, BoundExpression offset) : base(span) => Offset = offset;
}

/// <summary>#762 B3: range (a..b) — either bound may be absent.</summary>
public sealed class BoundRangeExpression : BoundExpression
{
    public BoundExpression? Start { get; }
    public BoundExpression? End { get; }
    public override string TypeName => "RANGE";
    public override IReadOnlyList<BoundExpression> Children =>
        new[] { Start, End }.Where(expression => expression != null).Cast<BoundExpression>().ToArray();

    public BoundRangeExpression(TextSpan span, BoundExpression? start, BoundExpression? end)
        : base(span)
    { Start = start; End = end; }
}

/// <summary>#762 B3: list creation.</summary>
public sealed class BoundListCreation : BoundExpression
{
    public string Id { get; }
    public string Name { get; }
    public string ElementType { get; }
    public IReadOnlyList<BoundExpression> Elements { get; }
    public AttributeCollection Attributes { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => Elements;

    public BoundListCreation(TextSpan span, string id, string name, string elementType,
        IReadOnlyList<BoundExpression> elements, AttributeCollection? attributes = null) : base(span)
    {
        Id = id; Name = name; ElementType = elementType; Elements = elements;
        Attributes = attributes ?? new AttributeCollection();
        // Spelling matches the PARSER's own vocabulary for the same construct
        // (B3 review M3: the bind statement types §SET as "HashSet<T>").
        TypeName = $"List<{elementType}>";
    }
}

/// <summary>#762 B3: set creation.</summary>
public sealed class BoundSetCreation : BoundExpression
{
    public string Id { get; }
    public string Name { get; }
    public string ElementType { get; }
    public IReadOnlyList<BoundExpression> Elements { get; }
    public AttributeCollection Attributes { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => Elements;

    public BoundSetCreation(TextSpan span, string id, string name, string elementType,
        IReadOnlyList<BoundExpression> elements, AttributeCollection? attributes = null) : base(span)
    {
        Id = id; Name = name; ElementType = elementType; Elements = elements;
        Attributes = attributes ?? new AttributeCollection();
        // Spelling matches the PARSER's own vocabulary for the same construct
        // (B3 review M3: the bind statement types §SET as "HashSet<T>").
        TypeName = $"HashSet<{elementType}>";
    }
}

/// <summary>#762 B3: dictionary creation — entries are bound key/value pairs.</summary>
public sealed class BoundDictionaryCreation : BoundExpression
{
    public string Name { get; }
    public string KeyType { get; }
    public string ValueType { get; }
    public IReadOnlyList<BoundPair> Entries { get; }
    public AttributeCollection Attributes { get; }
    public override string TypeName { get; }
    public string Id { get; }
    public override IReadOnlyList<BoundExpression> Children =>
        Entries.SelectMany(entry => new[] { entry.Key, entry.Value }).ToArray();

    public BoundDictionaryCreation(TextSpan span, string id, string name, string keyType,
        string valueType, IReadOnlyList<BoundPair> entries,
        AttributeCollection? attributes = null) : base(span)
    {
        Id = id; Name = name; KeyType = keyType; ValueType = valueType; Entries = entries;
        Attributes = attributes ?? new AttributeCollection();
        // No space: matches Parser.cs/RoslynSyntaxVisitor's "Dictionary<K,V>" (review M3).
        TypeName = $"Dictionary<{keyType},{valueType}>";
    }
}

/// <summary>#762 B3: collection membership test — BOOL; the collection itself is a NAME
/// (metadata, matching the AST shape), the probed key/value is a bound child.</summary>
public sealed class BoundCollectionContains : BoundExpression
{
    public string CollectionName { get; }
    public BoundExpression? Collection { get; }
    public BoundExpression KeyOrValue { get; }
    /// <summary>Value vs Key vs DictValue — THREE different operations (.Contains /
    /// .ContainsKey / .ContainsValue); dropping this was B3 review's CRITICAL.</summary>
    public Ast.ContainsMode Mode { get; }
    // The source stores the collection as a name. The binder also retains its resolved
    // expression when available so symbol-based liveness can see the receiver.
    public override string TypeName => "BOOL";
    public override IReadOnlyList<BoundExpression> Children =>
        Collection == null ? [KeyOrValue] : [Collection, KeyOrValue];

    public BoundCollectionContains(TextSpan span, string collectionName,
        BoundExpression keyOrValue, Ast.ContainsMode mode,
        BoundExpression? collection = null) : base(span)
    {
        CollectionName = collectionName;
        Collection = collection;
        KeyOrValue = keyOrValue;
        Mode = mode;
    }
}

/// <summary>#762 B3: collection count — INT.</summary>
public sealed class BoundCollectionCount : BoundExpression
{
    public BoundExpression Collection { get; }
    public override string TypeName => "INT";
    public override IReadOnlyList<BoundExpression> Children => [Collection];

    public BoundCollectionCount(TextSpan span, BoundExpression collection) : base(span)
        => Collection = collection;
}

/// <summary>#762 B3: tuple literal.</summary>
public sealed class BoundTupleLiteral : BoundExpression
{
    public IReadOnlyList<BoundExpression> Elements { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children => Elements;

    public BoundTupleLiteral(TextSpan span, IReadOnlyList<BoundExpression> elements) : base(span)
    {
        Elements = elements;
        // Element types composed rather than discarded (review M3).
        TypeName = $"Tuple<{string.Join(",", elements.Select(e => e.TypeName))}>";
    }
}

/// <summary>
/// Bound break statement (exits the enclosing loop).
/// </summary>
public sealed class BoundBreakStatement : BoundStatement
{
    public BoundBreakStatement(TextSpan span) : base(span) { }
}

/// <summary>
/// Bound continue statement (jumps to next loop iteration).
/// </summary>
public sealed class BoundContinueStatement : BoundStatement
{
    public BoundContinueStatement(TextSpan span) : base(span) { }
}

/// <summary>
/// Bound goto statement (jumps to a label).
/// </summary>
public sealed class BoundGotoStatement : BoundStatement
{
    public string Label { get; }
    public BoundGotoStatement(TextSpan span, string label) : base(span) { Label = label; }
}

/// <summary>
/// Bound label statement (defines a label).
/// </summary>
public sealed class BoundLabelStatement : BoundStatement
{
    public string Label { get; }
    public BoundLabelStatement(TextSpan span, string label) : base(span) { Label = label; }
}

/// <summary>
/// Bound try statement with catch clauses and optional finally.
/// </summary>
public sealed class BoundTryStatement : BoundStatement
{
    public IReadOnlyList<BoundStatement> TryBody { get; }
    public IReadOnlyList<BoundCatchClause> CatchClauses { get; }
    public IReadOnlyList<BoundStatement>? FinallyBody { get; }
    public override IEnumerable<BoundNode> ChildNodes
    {
        get
        {
            foreach (var statement in TryBody)
                yield return statement;
            foreach (var clause in CatchClauses)
                yield return clause;
            if (FinallyBody != null)
            {
                foreach (var statement in FinallyBody)
                    yield return statement;
            }
        }
    }

    public BoundTryStatement(
        TextSpan span,
        IReadOnlyList<BoundStatement> tryBody,
        IReadOnlyList<BoundCatchClause> catchClauses,
        IReadOnlyList<BoundStatement>? finallyBody)
        : base(span)
    {
        TryBody = tryBody;
        CatchClauses = catchClauses;
        FinallyBody = finallyBody;
    }
}

/// <summary>
/// Bound catch clause for exception handling.
/// </summary>
public sealed class BoundCatchClause : BoundNode
{
    /// <summary>
    /// The exception type to catch (null for catch-all).
    /// </summary>
    public string? ExceptionTypeName { get; }

    /// <summary>
    /// The variable to bind the caught exception to (null if not binding).
    /// </summary>
    public VariableSymbol? ExceptionVariable { get; }

    /// <summary>
    /// The body of the catch clause.
    /// </summary>
    public IReadOnlyList<BoundStatement> Body { get; }
    public override IEnumerable<BoundNode> ChildNodes => Body;

    public BoundCatchClause(
        TextSpan span,
        string? exceptionTypeName,
        VariableSymbol? exceptionVariable,
        IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        ExceptionTypeName = exceptionTypeName;
        ExceptionVariable = exceptionVariable;
        Body = body;
    }
}

/// <summary>
/// Bound match statement (pattern matching).
/// </summary>
public sealed class BoundMatchStatement : BoundStatement
{
    /// <summary>
    /// The expression being matched against.
    /// </summary>
    public BoundExpression Target { get; }

    /// <summary>
    /// The match cases.
    /// </summary>
    public IReadOnlyList<BoundMatchCase> Cases { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Target, .. Cases];

    public BoundMatchStatement(
        TextSpan span,
        BoundExpression target,
        IReadOnlyList<BoundMatchCase> cases)
        : base(span)
    {
        Target = target;
        Cases = cases;
    }
}

/// <summary>
/// Structural representation of a pattern. Pattern metadata and all expression
/// payloads/subpatterns remain reachable without pretending a pattern is an
/// ordinary value expression.
/// </summary>
public sealed class BoundPattern : BoundNode
{
    public string Kind { get; }
    public IReadOnlyDictionary<string, object?> Metadata { get; }
    public IReadOnlyList<BoundPattern> Patterns { get; }
    public IReadOnlyList<BoundExpression> Expressions { get; }
    public override IEnumerable<BoundNode> ChildNodes => Patterns.Cast<BoundNode>().Concat(Expressions);

    public BoundPattern(
        TextSpan span,
        string kind,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyList<BoundPattern>? patterns = null,
        IReadOnlyList<BoundExpression>? expressions = null)
        : base(span)
    {
        Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        Metadata = metadata ?? new Dictionary<string, object?>();
        Patterns = patterns ?? Array.Empty<BoundPattern>();
        Expressions = expressions ?? Array.Empty<BoundExpression>();
    }
}

/// <summary>
/// A case in a match statement or expression.
/// </summary>
public sealed class BoundMatchCase : BoundNode
{
    public BoundPattern Pattern { get; }
    public bool IsDefault { get; }
    public BoundExpression? Guard { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public BoundExpression? Result { get; }
    public override IEnumerable<BoundNode> ChildNodes
    {
        get
        {
            yield return Pattern;
            if (Guard != null)
                yield return Guard;
            foreach (var statement in Body)
                yield return statement;
            if (Result != null
                && !Body.OfType<BoundReturnStatement>()
                    .Any(statement => ReferenceEquals(statement.Expression, Result)))
            {
                yield return Result;
            }
        }
    }

    public BoundMatchCase(
        TextSpan span,
        BoundPattern pattern,
        bool isDefault,
        BoundExpression? guard,
        IReadOnlyList<BoundStatement> body,
        BoundExpression? result = null)
        : base(span)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        IsDefault = isDefault;
        Guard = guard;
        Body = body;
        Result = result;
    }
}

/// <summary>
/// Bound proof obligation statement.
/// </summary>
public sealed class BoundProofObligation : BoundStatement
{
    public string Id { get; }
    public string? Description { get; }
    public BoundExpression Condition { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Condition];

    public BoundProofObligation(TextSpan span, string id, string? description, BoundExpression condition)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Description = description;
    }
}

// ===== Class member analysis: new statement types =====

/// <summary>
/// Placeholder for statement types the Binder cannot fully bind.
/// Preserved in the bound tree so the CFG and dataflow analyses can account for it.
///
/// CFG model: two successors — fall-through and function-exit (may throw/return).
/// A new block is created after the unsupported statement so later statements don't
/// share the exit edge.
///
/// Dataflow model: no definitions, no uses (empty def/use sets). This is NOT conservative —
/// an opaque statement may define or use variables we can't see. The practical trade-off:
/// may-define-all/may-use-all would suppress nearly all findings in any function with an
/// unsupported statement. The current model may produce false positives (dead stores that the
/// opaque statement reads) and false negatives (defs we miss). This is best-effort.
/// </summary>
public sealed class BoundUnsupportedStatement : BoundStatement
{
    public string NodeTypeName { get; }

    public BoundUnsupportedStatement(TextSpan span, string nodeTypeName) : base(span)
    {
        NodeTypeName = nodeTypeName ?? throw new ArgumentNullException(nameof(nodeTypeName));
    }
}

/// <summary>
/// Bound assignment statement: target = value.
/// </summary>
public sealed class BoundAssignmentStatement : BoundStatement
{
    public BoundExpression Target { get; }
    public BoundExpression Value { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Target, Value];

    public BoundAssignmentStatement(TextSpan span, BoundExpression target, BoundExpression value)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Bound compound assignment statement: target op= value.
/// </summary>
public sealed class BoundCompoundAssignment : BoundStatement
{
    public BoundExpression Target { get; }
    public Ast.CompoundAssignmentOperator Operator { get; }
    public BoundExpression Value { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Target, Value];

    public BoundCompoundAssignment(TextSpan span, BoundExpression target, Ast.CompoundAssignmentOperator op, BoundExpression value)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Operator = op;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Bound foreach statement: foreach (var item in collection) { body }.
/// </summary>
public sealed class BoundForeachStatement : BoundStatement
{
    public VariableSymbol LoopVariable { get; }
    public BoundExpression Collection { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Collection, .. Body];

    public BoundForeachStatement(TextSpan span, VariableSymbol loopVariable, BoundExpression collection, IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        LoopVariable = loopVariable ?? throw new ArgumentNullException(nameof(loopVariable));
        Collection = collection ?? throw new ArgumentNullException(nameof(collection));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }
}

/// <summary>
/// Bound using statement: using (var resource = expr) { body }.
/// </summary>
public sealed class BoundUsingStatement : BoundStatement
{
    public VariableSymbol? Resource { get; }
    public BoundExpression ResourceExpression { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public override IEnumerable<BoundNode> ChildNodes => [ResourceExpression, .. Body];

    public BoundUsingStatement(TextSpan span, VariableSymbol? resource, BoundExpression resourceExpression, IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        Resource = resource;
        ResourceExpression = resourceExpression ?? throw new ArgumentNullException(nameof(resourceExpression));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }
}

/// <summary>
/// Bound throw statement: throw expr.
/// </summary>
public sealed class BoundThrowStatement : BoundStatement
{
    public BoundExpression? Expression { get; }
    public override IEnumerable<BoundNode> ChildNodes =>
        Expression != null ? [Expression] : Array.Empty<BoundNode>();

    public BoundThrowStatement(TextSpan span, BoundExpression? expression) : base(span)
    {
        Expression = expression;
    }
}

/// <summary>
/// Bound do-while statement: do { body } while (condition).
/// </summary>
public sealed class BoundDoWhileStatement : BoundStatement
{
    public BoundExpression Condition { get; }
    public IReadOnlyList<BoundStatement> Body { get; }
    public override IEnumerable<BoundNode> ChildNodes => [.. Body, Condition];

    public BoundDoWhileStatement(TextSpan span, BoundExpression condition, IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }
}

/// <summary>
/// Bound expression statement: expr; (standalone expression evaluated for side effects).
/// </summary>
public sealed class BoundExpressionStatement : BoundStatement
{
    public BoundExpression Expression { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Expression];

    public BoundExpressionStatement(TextSpan span, BoundExpression expression) : base(span)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }
}

// ===== Class member analysis: new expression types =====

/// <summary>
/// Bound 'this' expression. Carries the class name as its type.
/// </summary>
public sealed class BoundThisExpression : BoundExpression
{
    public override string TypeName { get; }

    public BoundThisExpression(TextSpan span, string className) : base(span)
    {
        TypeName = className ?? "UNKNOWN";
    }
}

/// <summary>
/// Bound 'base' expression.
/// </summary>
public sealed class BoundBaseExpression : BoundExpression
{
    public override string TypeName { get; }

    public BoundBaseExpression(TextSpan span, string typeName = "OBJECT") : base(span)
    {
        TypeName = typeName;
    }
}

/// <summary>
/// Bound field access expression: target.fieldName.
/// </summary>
public sealed class BoundFieldAccessExpression : BoundExpression
{
    public BoundExpression Target { get; }
    public string FieldName { get; }
    public TextSpan FieldNameSpan { get; }
    public VariableSymbol? ResolvedField { get; }
    public SymbolId? ResolvedSymbolId => ResolvedField?.Id;
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }

    public BoundFieldAccessExpression(
        TextSpan span,
        BoundExpression target,
        string fieldName,
        string typeName,
        VariableSymbol? resolvedField = null,
        TextSpan? fieldNameSpan = null)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        FieldNameSpan = fieldNameSpan ?? span;
        TypeName = typeName ?? "OBJECT";
        ResolvedField = resolvedField;
        Children = [target];
    }
}

public sealed class BoundObjectInitializer : BoundNode
{
    public string MemberName { get; }
    public BoundExpression Value { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Value];

    public BoundObjectInitializer(TextSpan span, string memberName, BoundExpression value)
        : base(span)
    {
        MemberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Bound new expression: new TypeName(args).
/// </summary>
public sealed class BoundNewExpression : BoundExpression
{
    public override string TypeName { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public IReadOnlyList<string> TypeArguments { get; }
    public IReadOnlyList<BoundObjectInitializer> Initializers { get; }
    public FunctionSymbol? ResolvedConstructor { get; }
    public TypeSymbol? ResolvedType { get; }
    public SymbolId? ResolvedSymbolId => ResolvedConstructor?.Id ?? ResolvedType?.Id;
    public override IReadOnlyList<BoundExpression> Children { get; }
    public override IEnumerable<BoundNode> ChildNodes =>
        Arguments.Cast<BoundNode>().Concat(Initializers);

    public BoundNewExpression(TextSpan span, string typeName, IReadOnlyList<BoundExpression> arguments)
        : this(
            span,
            typeName,
            Array.Empty<string>(),
            arguments,
            Array.Empty<BoundObjectInitializer>())
    {
    }

    public BoundNewExpression(
        TextSpan span,
        string typeName,
        IReadOnlyList<string> typeArguments,
        IReadOnlyList<BoundExpression> arguments,
        IReadOnlyList<BoundObjectInitializer> initializers,
        FunctionSymbol? resolvedConstructor = null,
        TypeSymbol? resolvedType = null)
        : base(span)
    {
        TypeName = typeName ?? "OBJECT";
        TypeArguments = typeArguments ?? Array.Empty<string>();
        Arguments = arguments ?? Array.Empty<BoundExpression>();
        Initializers = initializers ?? Array.Empty<BoundObjectInitializer>();
        ResolvedConstructor = resolvedConstructor;
        ResolvedType = resolvedType;
        Children = [.. Arguments, .. Initializers.Select(initializer => initializer.Value)];
    }
}

/// <summary>
/// Bound conditional expression: condition ? whenTrue : whenFalse.
/// </summary>
public sealed class BoundConditionalExpression : BoundExpression
{
    public BoundExpression Condition { get; }
    public BoundExpression WhenTrue { get; }
    public BoundExpression WhenFalse { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }

    public BoundConditionalExpression(TextSpan span, BoundExpression condition, BoundExpression whenTrue, BoundExpression whenFalse)
        : this(
            span,
            condition,
            whenTrue,
            whenFalse,
            string.Equals(whenTrue.TypeName, whenFalse.TypeName, StringComparison.OrdinalIgnoreCase)
                ? whenTrue.TypeName
                : whenTrue.TypeName == "NEVER"
                    ? whenFalse.TypeName
                    : whenFalse.TypeName == "NEVER"
                        ? whenTrue.TypeName
                        : "OBJECT")
    {
    }

    public BoundConditionalExpression(
        TextSpan span,
        BoundExpression condition,
        BoundExpression whenTrue,
        BoundExpression whenFalse,
        string resultType)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        WhenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
        WhenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
        TypeName = resultType ?? throw new ArgumentNullException(nameof(resultType));
        Children = [condition, whenTrue, whenFalse];
    }
}

/// <summary>
/// General non-lossy shape for expression families whose important semantics are
/// their exact AST kind, explicit type, metadata, and evaluated child expressions.
/// </summary>
public class BoundStructuralExpression : BoundExpression
{
    public string NodeTypeName { get; }
    public override string TypeName { get; }
    public IReadOnlyDictionary<string, object?> Metadata { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }
    public override IReadOnlyList<BoundExpression> DeferredChildren { get; }

    public BoundStructuralExpression(
        TextSpan span,
        string nodeTypeName,
        string typeName,
        IReadOnlyList<BoundExpression>? children = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        IReadOnlyList<BoundExpression>? deferredChildren = null)
        : base(span)
    {
        NodeTypeName = nodeTypeName ?? throw new ArgumentNullException(nameof(nodeTypeName));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Children = children ?? Array.Empty<BoundExpression>();
        Metadata = metadata ?? new Dictionary<string, object?>();
        DeferredChildren = deferredChildren ?? Array.Empty<BoundExpression>();
    }
}

/// <summary>
/// Structurally retained expression whose full semantic interpretation is not
/// available to analysis yet.
/// </summary>
public class BoundUnsupportedExpression : BoundStructuralExpression
{
    public string Reason { get; }

    public BoundUnsupportedExpression(
        TextSpan span,
        string nodeTypeName,
        string typeName,
        IReadOnlyList<BoundExpression>? children = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        string reason = "Analysis support is incomplete")
        : base(span, nodeTypeName, typeName, children, metadata)
    {
        Reason = reason;
    }
}

/// <summary>
/// Opaque source-language interop expression. Original source and feature
/// metadata are retained exactly.
/// </summary>
public sealed class BoundInteropExpression : BoundStructuralExpression
{
    public string SourceText { get; }

    public BoundInteropExpression(
        TextSpan span,
        string nodeTypeName,
        string sourceText,
        string typeName,
        IReadOnlyList<BoundExpression>? children = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
        : base(span, nodeTypeName, typeName, children, metadata)
    {
        SourceText = sourceText ?? throw new ArgumentNullException(nameof(sourceText));
    }
}

/// <summary>
/// Bound cast, as, or is operation. The operand and target type are never erased.
/// </summary>
public sealed class BoundTypeOperationExpression : BoundExpression
{
    public TypeOp Operation { get; }
    public BoundExpression Operand { get; }
    public string TargetType { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }

    public BoundTypeOperationExpression(
        TextSpan span,
        TypeOp operation,
        BoundExpression operand,
        string targetType)
        : base(span)
    {
        Operation = operation;
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        TypeName = operation == TypeOp.Is ? "BOOL" : targetType;
        Children = [operand];
    }
}

/// <summary>
/// Bound declaration/type pattern test.
/// </summary>
public sealed class BoundIsPatternExpression : BoundExpression
{
    public BoundExpression Operand { get; }
    public string TargetType { get; }
    public string? VariableName { get; }
    public override string TypeName => "BOOL";
    public override IReadOnlyList<BoundExpression> Children { get; }

    public BoundIsPatternExpression(
        TextSpan span,
        BoundExpression operand,
        string targetType,
        string? variableName)
        : base(span)
    {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
        VariableName = variableName;
        Children = [operand];
    }
}

/// <summary>
/// Bound single- or multi-dimensional index operation.
/// </summary>
public sealed class BoundArrayAccessExpression : BoundExpression
{
    public BoundExpression Array { get; }
    public IReadOnlyList<BoundExpression> Indices { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }

    public BoundArrayAccessExpression(
        TextSpan span,
        BoundExpression array,
        IReadOnlyList<BoundExpression> indices,
        string typeName)
        : base(span)
    {
        Array = array ?? throw new ArgumentNullException(nameof(array));
        Indices = indices ?? throw new ArgumentNullException(nameof(indices));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Children = [array, .. indices];
    }
}

/// <summary>
/// A call whose callee is itself an expression. It remains structurally visible
/// and can gain resolved symbol identity in the subsequent overload slice.
/// </summary>
public sealed class BoundExpressionCallExpression : BoundUnsupportedExpression
{
    public BoundExpression TargetExpression { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }

    public BoundExpressionCallExpression(
        TextSpan span,
        BoundExpression targetExpression,
        IReadOnlyList<BoundExpression> arguments,
        string typeName = "OBJECT")
        : base(
            span,
            nameof(ExpressionCallNode),
            typeName,
            [targetExpression, .. arguments],
            reason: "Expression-target call resolution is deferred to the overload/SymbolId slice")
    {
        TargetExpression = targetExpression ?? throw new ArgumentNullException(nameof(targetExpression));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }
}

public sealed class BoundInterpolatedStringPart : BoundNode
{
    public string? Text { get; }
    public BoundExpression? Expression { get; }
    public string? FormatSpecifier { get; }
    public string? AlignmentClause { get; }
    public override IEnumerable<BoundNode> ChildNodes =>
        Expression != null ? [Expression] : Array.Empty<BoundNode>();

    public BoundInterpolatedStringPart(
        TextSpan span,
        string? text,
        BoundExpression? expression,
        string? formatSpecifier = null,
        string? alignmentClause = null)
        : base(span)
    {
        Text = text;
        Expression = expression;
        FormatSpecifier = formatSpecifier;
        AlignmentClause = alignmentClause;
    }
}

public sealed class BoundInterpolatedStringExpression : BoundExpression
{
    public IReadOnlyList<BoundInterpolatedStringPart> Parts { get; }
    public override string TypeName => "STRING";
    public override IReadOnlyList<BoundExpression> Children { get; }
    public override IEnumerable<BoundNode> ChildNodes => Parts;

    public BoundInterpolatedStringExpression(
        TextSpan span,
        IReadOnlyList<BoundInterpolatedStringPart> parts)
        : base(span)
    {
        Parts = parts ?? throw new ArgumentNullException(nameof(parts));
        Children = parts
            .Where(part => part.Expression != null)
            .Select(part => part.Expression!)
            .ToArray();
    }
}

public sealed class BoundLambdaExpression : BoundExpression
{
    public string Id { get; }
    public IReadOnlyList<VariableSymbol> Parameters { get; }
    public EffectsNode? Effects { get; }
    public IReadOnlyList<string> DeclaredEffects { get; }
    public AttributeCollection Attributes { get; }
    public bool IsAsync { get; }
    public bool IsStatic { get; }
    public BoundExpression? ExpressionBody { get; }
    public IReadOnlyList<BoundStatement>? StatementBody { get; }
    public string ReturnTypeName { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }
    public override IReadOnlyList<BoundExpression> DeferredChildren => Children;
    public override IEnumerable<BoundNode> ChildNodes
    {
        get
        {
            if (ExpressionBody != null)
                yield return ExpressionBody;
            if (StatementBody != null)
            {
                foreach (var statement in StatementBody)
                    yield return statement;
            }
        }
    }

    public BoundLambdaExpression(
        TextSpan span,
        string id,
        IReadOnlyList<VariableSymbol> parameters,
        EffectsNode? effects,
        IReadOnlyList<string> declaredEffects,
        AttributeCollection attributes,
        bool isAsync,
        bool isStatic,
        BoundExpression? expressionBody,
        IReadOnlyList<BoundStatement>? statementBody,
        string returnTypeName)
        : base(span)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Effects = effects;
        DeclaredEffects = declaredEffects ?? Array.Empty<string>();
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        IsAsync = isAsync;
        IsStatic = isStatic;
        ExpressionBody = expressionBody;
        StatementBody = statementBody;
        ReturnTypeName = returnTypeName ?? throw new ArgumentNullException(nameof(returnTypeName));
        var signature = string.Join(",", parameters.Select(parameter => parameter.TypeName));
        TypeName = $"{(isAsync ? "ASYNC_" : "")}LAMBDA({signature})->{returnTypeName}";
        Children = expressionBody != null ? [expressionBody] : Array.Empty<BoundExpression>();
    }
}

public sealed class BoundQuantifierExpression : BoundExpression
{
    public string NodeTypeName { get; }
    public IReadOnlyList<VariableSymbol> BoundVariables { get; }
    public BoundExpression Body { get; }
    public override string TypeName => "BOOL";
    public override IReadOnlyList<BoundExpression> Children { get; }
    public override IReadOnlyList<BoundExpression> DeferredChildren => Children;

    public BoundQuantifierExpression(
        TextSpan span,
        string nodeTypeName,
        IReadOnlyList<VariableSymbol> boundVariables,
        BoundExpression body)
        : base(span)
    {
        NodeTypeName = nodeTypeName ?? throw new ArgumentNullException(nameof(nodeTypeName));
        BoundVariables = boundVariables ?? throw new ArgumentNullException(nameof(boundVariables));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        Children = [body];
    }
}

public sealed class BoundMatchExpression : BoundExpression
{
    public string Id { get; }
    public BoundExpression Target { get; }
    public IReadOnlyList<BoundMatchCase> Cases { get; }
    public AttributeCollection Attributes { get; }
    public override string TypeName { get; }
    public override IReadOnlyList<BoundExpression> Children { get; }
    public override IReadOnlyList<BoundExpression> DeferredChildren { get; }
    public override IEnumerable<BoundNode> ChildNodes => [Target, .. Cases];

    public BoundMatchExpression(
        TextSpan span,
        string id,
        BoundExpression target,
        IReadOnlyList<BoundMatchCase> cases,
        AttributeCollection attributes,
        string resultType)
        : base(span)
    {
        Id = id ?? "";
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Cases = cases ?? throw new ArgumentNullException(nameof(cases));
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        TypeName = resultType ?? throw new ArgumentNullException(nameof(resultType));
        Children =
        [
            target,
            .. cases.Where(matchCase => matchCase.Guard != null).Select(matchCase => matchCase.Guard!),
            .. cases.Where(matchCase => matchCase.Result != null).Select(matchCase => matchCase.Result!)
        ];
        DeferredChildren =
        [
            .. cases.Where(matchCase => matchCase.Guard != null).Select(matchCase => matchCase.Guard!),
            .. cases.Where(matchCase => matchCase.Result != null).Select(matchCase => matchCase.Result!)
        ];
    }
}
