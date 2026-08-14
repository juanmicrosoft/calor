using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Calor.Compiler.Analysis;

/// <summary>
/// Always-on structural control-flow validation for returns and yields.
/// It emits <c>Calor0205</c> when a value-returning <c>§R expr</c> appears in
/// the body of an owner that returns no value, and validates that every yield
/// can be represented by legal generated C#.
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
    private enum YieldOwner
    {
        None,
        Callable,
        AsyncCallable,
        Unsupported,
    }

    private readonly record struct YieldContext(
        YieldOwner Owner,
        string OwnerDescription,
        bool InLambda,
        bool InExpressionContainer,
        bool InCatch,
        bool InFinally,
        bool InUnsafe,
        bool InFixed,
        bool InTryWithCatch)
    {
        public static YieldContext None =>
            new(YieldOwner.None, "module scope", false, false, false, false,
                false, false, false);
    }

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
        WalkYields(module, YieldContext.None);
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

    private void WalkYields(AstNode node, YieldContext context)
    {
        switch (node)
        {
            case FunctionNode function:
                CheckIteratorParameters(
                    function.Parameters,
                    function.Body,
                    $"function '{function.Name}'");
                break;
            case MethodNode method:
                CheckIteratorParameters(
                    method.Parameters,
                    method.Body,
                    $"method '{method.Name}'");
                break;
            case RawCSharpNode raw:
                CheckRawCSharpIteratorParameters(raw.CSharpCode, raw.Span);
                break;
            case CSharpInteropBlockNode interop:
                CheckRawCSharpIteratorParameters(
                    interop.CSharpCode,
                    interop.Span);
                break;
        }

        var childContext = EnterYieldOwner(node, context);
        if (node is YieldReturnStatementNode yieldReturn)
        {
            CheckYieldReturn(yieldReturn, context);
        }
        else if (node is YieldBreakStatementNode yieldBreak)
        {
            CheckYieldBreak(yieldBreak, context);
        }

        foreach (var edge in RecursiveAstWalker.GetAllChildEdges(node))
        {
            var edgeContext = ContextForEdge(node, edge, childContext);
            WalkYields(edge.Node, edgeContext);
        }
    }

    private void CheckIteratorParameters(
        IReadOnlyList<ParameterNode> parameters,
        IReadOnlyList<StatementNode> body,
        string ownerDescription)
    {
        if (!RecursiveAstWalker.EnumerateStatements(body).Any(statement =>
                statement is YieldReturnStatementNode
                    or YieldBreakStatementNode))
        {
            return;
        }

        foreach (var parameter in parameters)
        {
            var forbidden = parameter.Modifier
                & (ParameterModifier.Ref
                    | ParameterModifier.In
                    | ParameterModifier.Out);
            if (forbidden == ParameterModifier.None)
                continue;

            _diagnostics.ReportError(
                parameter.IdentifierSpan,
                DiagnosticCode.IllegalYield,
                $"Iterator {ownerDescription} cannot declare parameter " +
                $"'{parameter.Name}' with modifier '{FormatModifiers(forbidden)}'. " +
                "Iterator parameters must be passed by value.");
        }
    }

    private void CheckRawCSharpIteratorParameters(
        string source,
        Parsing.TextSpan span)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var callable in root.DescendantNodesAndSelf().Where(node =>
                     node is MethodDeclarationSyntax
                         or LocalFunctionStatementSyntax))
        {
            if (!ContainsOwnedYield(callable))
                continue;

            var (description, parameters) = callable switch
            {
                MethodDeclarationSyntax method =>
                    ($"method '{method.Identifier.ValueText}'",
                        method.ParameterList.Parameters),
                LocalFunctionStatementSyntax local =>
                    ($"local function '{local.Identifier.ValueText}'",
                        local.ParameterList.Parameters),
                _ => throw new InvalidOperationException(),
            };

            foreach (var parameter in parameters)
            {
                var modifiers = parameter.Modifiers
                    .Where(token => token.IsKind(SyntaxKind.RefKeyword)
                        || token.IsKind(SyntaxKind.InKeyword)
                        || token.IsKind(SyntaxKind.OutKeyword))
                    .Select(token => token.ValueText)
                    .ToArray();
                if (modifiers.Length == 0)
                    continue;

                _diagnostics.ReportError(
                    span,
                    DiagnosticCode.IllegalYield,
                    $"Iterator {description} cannot declare parameter " +
                    $"'{parameter.Identifier.ValueText}' with modifier " +
                    $"'{string.Join(", ", modifiers)}'. Iterator parameters " +
                    "must be passed by value.");
            }
        }
    }

    private static bool ContainsOwnedYield(SyntaxNode callable) =>
        callable.DescendantNodes(descendIntoChildren: node =>
                ReferenceEquals(node, callable)
                || node is not AnonymousFunctionExpressionSyntax
                    and not LocalFunctionStatementSyntax
                    and not BaseMethodDeclarationSyntax)
            .OfType<YieldStatementSyntax>()
            .Any();

    private static string FormatModifiers(ParameterModifier modifiers)
    {
        var names = new List<string>(3);
        if (modifiers.HasFlag(ParameterModifier.Ref))
            names.Add("ref");
        if (modifiers.HasFlag(ParameterModifier.In))
            names.Add("in");
        if (modifiers.HasFlag(ParameterModifier.Out))
            names.Add("out");
        return string.Join(", ", names);
    }

    private static YieldContext EnterYieldOwner(
        AstNode node,
        YieldContext context) =>
        node switch
        {
            FunctionNode function => ResetYieldContext(
                function.IsAsync ? YieldOwner.AsyncCallable : YieldOwner.Callable,
                $"function '{function.Name}'"),
            MethodNode method => ResetYieldContext(
                method.IsAsync ? YieldOwner.AsyncCallable : YieldOwner.Callable,
                $"method '{method.Name}'"),
            OperatorOverloadNode => ResetYieldContext(
                YieldOwner.Unsupported,
                "operator"),
            ConstructorNode => ResetYieldContext(
                YieldOwner.Unsupported,
                "constructor"),
            PropertyAccessorNode => ResetYieldContext(
                YieldOwner.Unsupported,
                "property/indexer accessor"),
            EventDefinitionNode => ResetYieldContext(
                YieldOwner.Unsupported,
                "event accessor"),
            LambdaExpressionNode => context with { InLambda = true },
            MatchExpressionNode => context with { InExpressionContainer = true },
            CatchClauseNode => context with { InCatch = true },
            _ => context,
        };

    private static YieldContext ResetYieldContext(
        YieldOwner owner,
        string description) =>
        new(
            owner,
            description,
            InLambda: false,
            InExpressionContainer: false,
            InCatch: false,
            InFinally: false,
            InUnsafe: false,
            InFixed: false,
            InTryWithCatch: false);

    private static YieldContext ContextForEdge(
        AstNode parent,
        RecursiveAstWalker.ChildEdge edge,
        YieldContext context)
    {
        if (parent is TryStatementNode tryStatement)
        {
            if (edge.Property.Name == nameof(TryStatementNode.FinallyBody))
            {
                return context with { InFinally = true };
            }
            if (edge.Property.Name == nameof(TryStatementNode.TryBody)
                && tryStatement.CatchClauses.Count > 0)
            {
                return context with { InTryWithCatch = true };
            }
        }

        if (parent is UnsafeBlockNode
            && edge.Property.Name == nameof(UnsafeBlockNode.Body))
        {
            return context with { InUnsafe = true };
        }

        if (parent is FixedStatementNode
            && edge.Property.Name == nameof(FixedStatementNode.Body))
        {
            return context with { InFixed = true, InUnsafe = true };
        }

        return context;
    }

    private void CheckYieldReturn(
        YieldReturnStatementNode yieldReturn,
        YieldContext context)
    {
        if (yieldReturn.Expression is null)
        {
            _diagnostics.ReportError(
                yieldReturn.Span,
                DiagnosticCode.YieldRequiresValue,
                "'§YIELD' requires a value. Use '§YBRK' for 'yield break'.");
            return;
        }

        var reason = GetIllegalYieldReason(context, returnsValue: true);
        if (reason != null)
        {
            _diagnostics.ReportError(
                yieldReturn.Span,
                DiagnosticCode.IllegalYield,
                $"'§YIELD' is illegal {reason}.");
        }
    }

    private void CheckYieldBreak(
        YieldBreakStatementNode yieldBreak,
        YieldContext context)
    {
        var reason = GetIllegalYieldReason(context, returnsValue: false);
        if (reason != null)
        {
            _diagnostics.ReportError(
                yieldBreak.Span,
                DiagnosticCode.IllegalYield,
                $"'§YBRK' is illegal {reason}.");
        }
    }

    private static string? GetIllegalYieldReason(
        YieldContext context,
        bool returnsValue)
    {
        if (context.InLambda)
            return "inside a lambda expression";
        if (context.InExpressionContainer)
            return "inside an expression-valued statement container";
        if (context.Owner == YieldOwner.None)
            return "outside a function or method";
        if (context.Owner == YieldOwner.AsyncCallable)
            return $"inside async {context.OwnerDescription}; async iterators are not supported";
        if (context.Owner == YieldOwner.Unsupported)
            return $"inside a {context.OwnerDescription}";
        if (context.InFinally)
            return "inside a finally block";

        if (!returnsValue)
            return null;

        if (context.InCatch)
            return "inside a catch block";
        if (context.InFixed)
            return "inside a fixed block";
        if (context.InUnsafe)
            return "inside an unsafe block";
        if (context.InTryWithCatch)
            return "inside a try block that has a catch clause";

        return null;
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
