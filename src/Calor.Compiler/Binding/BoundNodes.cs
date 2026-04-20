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
public sealed class BoundCallExpression : BoundExpression
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
