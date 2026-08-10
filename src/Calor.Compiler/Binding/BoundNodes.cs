using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;

namespace Calor.Compiler.Binding;

/// <summary>
/// Base class for all bound nodes.
/// </summary>
public abstract class BoundNode
{
    public TextSpan Span { get; }

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

    protected BoundExpression(TextSpan span) : base(span) { }
}

/// <summary>
/// Bound module containing bound functions.
/// </summary>
public sealed class BoundModule : BoundNode
{
    public string Name { get; }
    public IReadOnlyList<BoundFunction> Functions { get; }

    public BoundModule(TextSpan span, string name, IReadOnlyList<BoundFunction> functions)
        : base(span)
    {
        Name = name;
        Functions = functions;
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
    public BoundExpression? Initializer { get; }

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

    public BoundCallStatement(TextSpan span, string target, IReadOnlyList<BoundExpression> arguments)
        : base(span)
    {
        Target = target;
        Arguments = arguments;
    }
}

/// <summary>
/// Bound return statement.
/// </summary>
public sealed class BoundReturnStatement : BoundStatement
{
    public BoundExpression? Expression { get; }

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
    }
}

/// <summary>
/// Bound integer literal.
/// </summary>
public sealed class BoundIntLiteral : BoundExpression
{
    public long Value { get; }
    public override string TypeName => Value is > int.MaxValue or < int.MinValue ? "LONG" : "INT";

    public BoundIntLiteral(TextSpan span, long value)
        : base(span)
    {
        Value = value;
    }
}

/// <summary>
/// Bound string literal.
/// </summary>
public sealed class BoundStringLiteral : BoundExpression
{
    public string Value { get; }
    public override string TypeName => "STRING";

    public BoundStringLiteral(TextSpan span, string value)
        : base(span)
    {
        Value = value;
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
    public override string TypeName => "FLOAT";

    public BoundFloatLiteral(TextSpan span, double value)
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

    public BoundUnaryExpression(TextSpan span, Ast.UnaryOperator op, BoundExpression operand, string resultType)
        : base(span)
    {
        Operator = op;
        Operand = operand;
        TypeName = resultType;
    }
}

/// <summary>
/// Bound call expression.
/// </summary>
public class BoundCallExpression : BoundExpression
{
    public string Target { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public override string TypeName { get; }

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

    public BoundCallExpression(TextSpan span, string target, IReadOnlyList<BoundExpression> arguments, string resultType,
        string? resolvedTypeName = null, string? resolvedMethodName = null, IReadOnlyList<string>? resolvedParameterTypes = null)
        : base(span)
    {
        Target = target;
        Arguments = arguments;
        TypeName = resultType;
        ResolvedTypeName = resolvedTypeName;
        ResolvedMethodName = resolvedMethodName;
        ResolvedParameterTypes = resolvedParameterTypes;
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
/// #762 B2 (review C1): the ONE enumeration of expression children for bound node types
/// introduced by the B-series. Every traversal switch in the analysis layer uses this as
/// its default arm, so a new family extends ONE place and every checker's recursion
/// follows — the growing-switch class (the #879 cursor lesson's sibling) ends here.
/// Types with dedicated arms in a given traversal (the pre-B2 five) are not listed;
/// this covers the B-series additions only. Family PRs B3–B7 MUST extend this with
/// their new types in the same commit that adds them.
/// </summary>
public static class BoundChildren
{
    public static IEnumerable<BoundExpression> Of(BoundExpression expression) => expression switch
    {
        BoundSomeExpression some => [some.Value],
        BoundOkExpression ok => [ok.Value],
        BoundErrExpression err => [err.Error],
        BoundExpressionCall call => [call.Target, .. call.Arguments],
        BoundAnonymousObjectCreation anon => anon.Initializers.Select(i => i.Value),
        BoundRecordCreation rec => rec.Fields.Select(f => f.Value),
        BoundWithExpression with => [with.Target, .. with.Assignments.Select(a => a.Value)],
        BoundThrowExpression thr => [thr.Exception],
        // #762 B3 — arrays/indexes + collections.
        BoundArrayCreation ac => ac.Size is null ? ac.Initializer : [ac.Size, .. ac.Initializer],
        BoundArrayAccess aa => [aa.Array, aa.Index],
        BoundArrayLength al => [al.Array],
        BoundMultiDimArrayCreation mc => [.. mc.DimensionSizes, .. mc.InitializerRows.SelectMany(r => r)],
        BoundMultiDimArrayAccess ma => [ma.Array, .. ma.Indices],
        BoundIndexFromEnd ie => [ie.Offset],
        BoundRangeExpression r => new[] { r.Start, r.End }.Where(e => e is not null).Cast<BoundExpression>(),
        BoundListCreation lc => lc.Elements,
        BoundSetCreation sc => sc.Elements,
        BoundDictionaryCreation dc => dc.Entries.SelectMany(e => new[] { e.Key, e.Value }),
        BoundCollectionContains cc => [cc.KeyOrValue],
        BoundCollectionCount cn => [cn.Collection],
        BoundTupleLiteral tl => tl.Elements,
        // #762 B4 — string family.
        BoundStringOperation so => so.Arguments,
        BoundInterpolatedString istr => istr.Parts
            .Where(p => p.Expression is not null).Select(p => p.Expression!),
        BoundStringBuilderOperation sb => sb.Arguments,
        BoundCharOperation co => co.Arguments,
        _ => [],
    };
}

/// <summary>#762 B2: Option construction. Type composes from the payload (string types, D3).</summary>
public sealed class BoundSomeExpression : BoundExpression
{
    public BoundExpression Value { get; }
    public override string TypeName { get; }
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
    public BoundAnonymousObjectCreation(TextSpan span, IReadOnlyList<BoundNamedValue> initializers)
        : base(span) => Initializers = initializers;
}

/// <summary>#762 B2: record creation — typed by the record name; field values bound.</summary>
public sealed class BoundRecordCreation : BoundExpression
{
    public IReadOnlyList<BoundNamedValue> Fields { get; }
    public override string TypeName { get; }
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
    public override string TypeName { get; }
    public BoundArrayCreation(TextSpan span, string id, string name, string elementType,
        BoundExpression? size, IReadOnlyList<BoundExpression> initializer) : base(span)
    {
        Id = id; Name = name; ElementType = elementType; Size = size; Initializer = initializer;
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
    public BoundIndexFromEnd(TextSpan span, BoundExpression offset) : base(span) => Offset = offset;
}

/// <summary>#762 B3: range (a..b) — either bound may be absent.</summary>
public sealed class BoundRangeExpression : BoundExpression
{
    public BoundExpression? Start { get; }
    public BoundExpression? End { get; }
    public override string TypeName => "RANGE";
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
    public override string TypeName { get; }
    public BoundListCreation(TextSpan span, string id, string name, string elementType,
        IReadOnlyList<BoundExpression> elements) : base(span)
    {
        Id = id; Name = name; ElementType = elementType; Elements = elements;
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
    public override string TypeName { get; }
    public BoundSetCreation(TextSpan span, string id, string name, string elementType,
        IReadOnlyList<BoundExpression> elements) : base(span)
    {
        Id = id; Name = name; ElementType = elementType; Elements = elements;
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
    public override string TypeName { get; }
    public string Id { get; }
    public BoundDictionaryCreation(TextSpan span, string id, string name, string keyType,
        string valueType, IReadOnlyList<BoundPair> entries) : base(span)
    {
        Id = id; Name = name; KeyType = keyType; ValueType = valueType; Entries = entries;
        // No space: matches Parser.cs/RoslynSyntaxVisitor's "Dictionary<K,V>" (review M3).
        TypeName = $"Dictionary<{keyType},{valueType}>";
    }
}

/// <summary>#762 B3: collection membership test — BOOL; the collection itself is a NAME
/// (metadata, matching the AST shape), the probed key/value is a bound child.</summary>
public sealed class BoundCollectionContains : BoundExpression
{
    public string CollectionName { get; }
    public BoundExpression KeyOrValue { get; }
    /// <summary>Value vs Key vs DictValue — THREE different operations (.Contains /
    /// .ContainsKey / .ContainsValue); dropping this was B3 review's CRITICAL.</summary>
    public Ast.ContainsMode Mode { get; }
    // NOTE: the collection itself is a NAME (AST shape) — GetUsedVariables cannot
    // report it as used; liveness blindness tracked with the #786 checker re-platform.
    public override string TypeName => "BOOL";
    public BoundCollectionContains(TextSpan span, string collectionName,
        BoundExpression keyOrValue, Ast.ContainsMode mode) : base(span)
    { CollectionName = collectionName; KeyOrValue = keyOrValue; Mode = mode; }
}

/// <summary>#762 B3: collection count — INT.</summary>
public sealed class BoundCollectionCount : BoundExpression
{
    public BoundExpression Collection { get; }
    public override string TypeName => "INT";
    public BoundCollectionCount(TextSpan span, BoundExpression collection) : base(span)
        => Collection = collection;
}

/// <summary>#762 B3: tuple literal.</summary>
public sealed class BoundTupleLiteral : BoundExpression
{
    public IReadOnlyList<BoundExpression> Elements { get; }
    public override string TypeName { get; }
    public BoundTupleLiteral(TextSpan span, IReadOnlyList<BoundExpression> elements) : base(span)
    {
        Elements = elements;
        // Element types composed rather than discarded (review M3).
        TypeName = $"Tuple<{string.Join(",", elements.Select(e => e.TypeName))}>";
    }
}

/// <summary>#762 B4: string operation — result type derived PER OPERATION from the
/// StringOp enum's own semantics (the first family with genuinely typed results rather
/// than placeholders). ComparisonMode retained (the B3 ContainsMode lesson).</summary>
public sealed class BoundStringOperation : BoundExpression
{
    public StringOp Operation { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public StringComparisonMode? ComparisonMode { get; }
    public override string TypeName { get; }
    public BoundStringOperation(TextSpan span, StringOp operation,
        IReadOnlyList<BoundExpression> arguments, StringComparisonMode? comparisonMode)
        : base(span)
    {
        Operation = operation; Arguments = arguments; ComparisonMode = comparisonMode;
        TypeName = operation switch
        {
            StringOp.Length or StringOp.IndexOf => "INT",
            StringOp.Contains or StringOp.StartsWith or StringOp.EndsWith
                or StringOp.IsNullOrEmpty or StringOp.IsNullOrWhiteSpace
                or StringOp.Equals or StringOp.RegexTest => "BOOL",
            StringOp.Split or StringOp.RegexSplit => "str[]",
            StringOp.RegexMatch => "OBJECT",
            _ => "STRING",
        };
    }
}

/// <summary>One part of a bound interpolated string: either literal text or a bound
/// expression with its format/alignment clauses retained (B3 retention standard).</summary>
public sealed record BoundInterpolationPart(
    string? Text, BoundExpression? Expression,
    string? FormatSpecifier, string? AlignmentClause, TextSpan Span);

/// <summary>#762 B4: interpolated string — STRING; parts ordered, expressions bound.</summary>
public sealed class BoundInterpolatedString : BoundExpression
{
    public IReadOnlyList<BoundInterpolationPart> Parts { get; }
    public override string TypeName => "STRING";
    public BoundInterpolatedString(TextSpan span, IReadOnlyList<BoundInterpolationPart> parts)
        : base(span) => Parts = parts;
}

/// <summary>#762 B4: StringBuilder operation — ToString→STRING, Length→INT, everything
/// else returns the builder ("StringBuilder", the effect-resolution spelling).</summary>
public sealed class BoundStringBuilderOperation : BoundExpression
{
    public StringBuilderOp Operation { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public override string TypeName { get; }
    public BoundStringBuilderOperation(TextSpan span, StringBuilderOp operation,
        IReadOnlyList<BoundExpression> arguments) : base(span)
    {
        Operation = operation; Arguments = arguments;
        TypeName = operation switch
        {
            StringBuilderOp.ToString => "STRING",
            StringBuilderOp.Length => "INT",
            _ => "StringBuilder",
        };
    }
}

/// <summary>#762 B4: char operation — per-op result ("CHAR" is the canonical spelling,
/// AttributeHelper maps char↔CHAR).</summary>
public sealed class BoundCharOperation : BoundExpression
{
    public CharOp Operation { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }
    public override string TypeName { get; }
    public BoundCharOperation(TextSpan span, CharOp operation,
        IReadOnlyList<BoundExpression> arguments) : base(span)
    {
        Operation = operation; Arguments = arguments;
        TypeName = operation switch
        {
            CharOp.CharCode => "INT",
            CharOp.IsLetter or CharOp.IsDigit or CharOp.IsWhiteSpace
                or CharOp.IsUpper or CharOp.IsLower => "BOOL",
            _ => "CHAR",
        };
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
/// A case in a match statement.
/// </summary>
public sealed class BoundMatchCase : BoundNode
{
    /// <summary>
    /// The pattern to match (as an expression for now - could be expanded).
    /// </summary>
    public BoundExpression? Pattern { get; }

    /// <summary>
    /// Whether this is a wildcard/default case.
    /// </summary>
    public bool IsDefault { get; }

    /// <summary>
    /// Optional guard condition.
    /// </summary>
    public BoundExpression? Guard { get; }

    /// <summary>
    /// The body to execute if the pattern matches.
    /// </summary>
    public IReadOnlyList<BoundStatement> Body { get; }

    public BoundMatchCase(
        TextSpan span,
        BoundExpression? pattern,
        bool isDefault,
        BoundExpression? guard,
        IReadOnlyList<BoundStatement> body)
        : base(span)
    {
        Pattern = pattern;
        IsDefault = isDefault;
        Guard = guard;
        Body = body;
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
    public override string TypeName => "OBJECT";

    public BoundBaseExpression(TextSpan span) : base(span) { }
}

/// <summary>
/// Bound field access expression: target.fieldName.
/// </summary>
public sealed class BoundFieldAccessExpression : BoundExpression
{
    public BoundExpression Target { get; }
    public string FieldName { get; }
    public override string TypeName { get; }

    public BoundFieldAccessExpression(TextSpan span, BoundExpression target, string fieldName, string typeName)
        : base(span)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        TypeName = typeName ?? "OBJECT";
    }
}

/// <summary>
/// Bound new expression: new TypeName(args).
/// </summary>
public sealed class BoundNewExpression : BoundExpression
{
    public override string TypeName { get; }
    public IReadOnlyList<BoundExpression> Arguments { get; }

    public BoundNewExpression(TextSpan span, string typeName, IReadOnlyList<BoundExpression> arguments)
        : base(span)
    {
        TypeName = typeName ?? "OBJECT";
        Arguments = arguments ?? Array.Empty<BoundExpression>();
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

    public BoundConditionalExpression(TextSpan span, BoundExpression condition, BoundExpression whenTrue, BoundExpression whenFalse)
        : base(span)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        WhenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
        WhenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
        TypeName = whenTrue.TypeName; // type of the true branch
    }
}
