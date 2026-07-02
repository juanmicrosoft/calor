using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Always-on semantic pass that emits <c>Calor0205</c> when a value-returning
/// <c>§R expr</c> appears in the body of an owner that returns no value:
///
/// <list type="bullet">
///   <item>a <c>void</c> function/method (no <c>§O</c> / no header return type);</item>
///   <item>an <c>async</c> function/method with no return type (compiles to <c>Task</c>);</item>
///   <item>an iterator (its body uses <c>§YIELD</c>/<c>§YBRK</c>);</item>
///   <item>a constructor;</item>
///   <item>a property/indexer <c>set</c>/<c>init</c> accessor;</item>
///   <item>an event <c>add</c>/<c>remove</c> accessor.</item>
/// </list>
///
/// <para>Without this pass the generated C# silently fails to compile
/// (CS0127 "since it returns void" / CS1622 for iterators). The classic case:
/// an agent writes a correct 3-field <c>void</c> header but then <c>§R INT:0</c>
/// in the body — nothing flagged it before Calor0205.</para>
///
/// <para><b>False-positive safety.</b> The pass is always-on and reports a hard
/// error, so it must never fire on legal code. The C#→Calor migration lowers a
/// void expression-bodied member such as <c>void F() =&gt; VoidCall();</c> into
/// <c>§R &lt;call&gt;</c>, which is legal. To stay sound the pass only flags a
/// return whose expression is <em>definitely</em> a non-void value that can
/// never be a valid C# statement-expression — literals, arithmetic/logical
/// operations, plain references, ternaries, and a handful of clearly-value
/// forms (see <see cref="IsDefinitelyValue"/>). Calls, object creation,
/// <c>await</c>, and increment/decrement are deliberately left unflagged
/// because they can be void-typed or valid void statement-expressions. The
/// corpus-clean pin (<c>ReturnValidationCorpusCleanTests</c>) gates this.</para>
/// </summary>
public sealed class ReturnValidationPass
{
    private readonly DiagnosticBag _diagnostics;

    public ReturnValidationPass(DiagnosticBag diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public void Check(ModuleNode module)
    {
        if (module is null)
        {
            return;
        }

        Walk(module, ReturnShape.Kind.None);
    }

    private void Walk(AstNode node, ReturnShape.Kind context)
    {
        // The context that applies to this node's *children*. Owner nodes open a
        // fresh context (their own return classification); everything else — in
        // particular control-flow bodies — inherits the enclosing context.
        var childContext = context;

        switch (node)
        {
            case FunctionNode or MethodNode or OperatorOverloadNode
                or ConstructorNode or PropertyAccessorNode or EventDefinitionNode:
                childContext = ReturnShape.Classify(node);
                break;
            case ReturnStatementNode ret:
                CheckReturn(ret, context);
                break;
        }

        foreach (var child in RecursiveAstWalker.GetChildren(node))
        {
            Walk(child, childContext);
        }
    }

    private void CheckReturn(ReturnStatementNode ret, ReturnShape.Kind context)
    {
        if (!ReturnShape.IsNoValueOwner(context))
        {
            return;
        }

        var expr = ret.Expression;
        if (expr is null)
        {
            // Bare §R (return;) is valid in every no-value owner.
            return;
        }

        if (!IsDefinitelyValue(expr))
        {
            // Conservative: the expression could be void-typed (e.g. a call) or
            // a valid void statement-expression (e.g. new / ++). Do not flag.
            return;
        }

        _diagnostics.ReportError(ret.Span, DiagnosticCode.ReturnValueInVoidOwner, MessageFor(context));
    }

    private static string MessageFor(ReturnShape.Kind kind) => kind switch
    {
        ReturnShape.Kind.Void =>
            "'§R' returns a value, but the enclosing function/method declares no return type and " +
            "compiles to 'void', which cannot return a value. Add a return type ('§O{type}' or a " +
            "return type in the header), or drop the value and use a bare '§R' to return early.",
        ReturnShape.Kind.AsyncVoid =>
            "'§R' returns a value, but the enclosing async function/method declares no return type and " +
            "compiles to 'Task', which cannot return a value. Add a return type ('§O{type}', emitted as " +
            "'Task<type>'), or drop the value and use a bare '§R'.",
        ReturnShape.Kind.Iterator =>
            "'§R' returns a value, but the enclosing member is an iterator (its body uses '§YIELD'/'§YBRK') " +
            "and cannot 'return' a value. Use '§YIELD expr' to produce a value, or a bare '§R' to stop iteration.",
        ReturnShape.Kind.Setter =>
            "'§R' returns a value, but a property/indexer 'set' or 'init' accessor cannot return a value. " +
            "Use a bare '§R' to return early.",
        ReturnShape.Kind.Constructor =>
            "'§R' returns a value, but a constructor cannot return a value. Use a bare '§R' to return early.",
        ReturnShape.Kind.EventAccessor =>
            "'§R' returns a value, but an event 'add'/'remove' accessor cannot return a value. " +
            "Use a bare '§R' to return early.",
        _ => "'§R' returns a value in a member that has no return value.",
    };

    /// <summary>
    /// True only for expressions that are <em>definitely</em> a non-void value
    /// AND can never be a valid C# statement-expression. This is a deliberate
    /// default-deny allow-list: anything not listed (calls, object creation,
    /// await, increment/decrement, match-expressions, member access, casts, …)
    /// is left unflagged because it could legitimately appear as
    /// <c>§R &lt;expr&gt;</c> from a migrated void expression-bodied member.
    /// </summary>
    private static bool IsDefinitelyValue(ExpressionNode expr) => expr switch
    {
        IntLiteralNode => true,
        FloatLiteralNode => true,
        DecimalLiteralNode => true,
        StringLiteralNode => true,
        BoolLiteralNode => true,
        BinaryOperationNode => true,
        ConditionalExpressionNode => true,
        ReferenceNode => true,
        ThisExpressionNode => true,
        BaseExpressionNode => true,
        TupleLiteralNode => true,
        InterpolatedStringNode => true,
        RangeExpressionNode => true,
        IndexFromEndNode => true,
        TypeOfExpressionNode => true,
        NameOfExpressionNode => true,
        SizeOfNode => true,
        // Prefix/postfix ++/-- ARE valid void statement-expressions, so exclude
        // them; other unary operations (-, !, ~) are always values.
        UnaryOperationNode u => u.Operator is not (
            UnaryOperator.PreIncrement or UnaryOperator.PostIncrement or
            UnaryOperator.PreDecrement or UnaryOperator.PostDecrement),
        _ => false,
    };
}
