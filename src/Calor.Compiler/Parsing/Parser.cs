using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;

namespace Calor.Compiler.Parsing;

/// <summary>
/// Recursive descent parser for Calor source code.
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private readonly DiagnosticBag _diagnostics;
    private int _position;

    public Parser(IEnumerable<Token> tokens, DiagnosticBag diagnostics)
    {
        _tokens = tokens.ToList();
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private Token Current => Peek(0);
    private Token Lookahead => Peek(1);

    private Token Peek(int offset)
    {
        var index = _position + offset;
        return index < _tokens.Count ? _tokens[index] : _tokens[^1];
    }

    private bool IsAtEnd => Current.Kind == TokenKind.Eof;

    private Token Advance()
    {
        var token = Current;
        if (!IsAtEnd)
        {
            _position++;
        }
        return token;
    }

    private bool Check(TokenKind kind) => Current.Kind == kind;

    private bool Match(TokenKind kind)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }
        return false;
    }

    private Token Expect(TokenKind kind)
    {
        if (Check(kind))
        {
            return Advance();
        }

        _diagnostics.ReportUnexpectedToken(Current.Span, kind, Current.Kind);
        return new Token(kind, "", Current.Span);
    }

    public ModuleNode Parse()
    {
        return ParseModule();
    }

    private ModuleNode ParseModule()
    {
        var startToken = Expect(TokenKind.Module);
        var attrs = ParseAttributes();

        var (id, moduleName) = AttributeHelper.InterpretModuleAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MODULE", "id");
            id = "";
        }
        if (string.IsNullOrEmpty(moduleName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MODULE", "name");
            moduleName = "";
        }

        var usings = new List<UsingDirectiveNode>();
        var interfaces = new List<InterfaceDefinitionNode>();
        var classes = new List<ClassDefinitionNode>();
        var functions = new List<FunctionNode>();

        // Extended Features: Module-level metadata
        var issues = new List<IssueNode>();
        var assumptions = new List<AssumeNode>();
        var invariants = new List<InvariantNode>();
        var decisions = new List<DecisionNode>();
        ContextNode? context = null;

        while (!IsAtEnd && !Check(TokenKind.EndModule))
        {
            if (Check(TokenKind.Using))
            {
                usings.Add(ParseUsingDirective());
            }
            else if (Check(TokenKind.Interface))
            {
                interfaces.Add(ParseInterfaceDefinition());
            }
            else if (Check(TokenKind.Class))
            {
                classes.Add(ParseClassDefinition());
            }
            else if (Check(TokenKind.Func))
            {
                functions.Add(ParseFunction());
            }
            // Extended Features: Module-level metadata
            else if (Check(TokenKind.Todo))
            {
                issues.Add(ParseTodoIssue());
            }
            else if (Check(TokenKind.Fixme))
            {
                issues.Add(ParseFixmeIssue());
            }
            else if (Check(TokenKind.Hack))
            {
                issues.Add(ParseHackIssue());
            }
            else if (Check(TokenKind.Assume))
            {
                assumptions.Add(ParseAssume());
            }
            else if (Check(TokenKind.Invariant))
            {
                // Use existing ParseInvariant if available, otherwise parse inline
                var invStartToken = Expect(TokenKind.Invariant);
                var invAttrs = ParseAttributes();
                var message = invAttrs["message"];
                var condition = ParseExpression();
                var invSpan = invStartToken.Span.Union(condition.Span);
                invariants.Add(new InvariantNode(invSpan, condition, message, invAttrs));
            }
            else if (Check(TokenKind.Decision))
            {
                decisions.Add(ParseDecision());
            }
            else if (Check(TokenKind.Context))
            {
                context = ParseContext();
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "USING, IFACE, CLASS, FUNC, or END_MODULE", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndModule);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndModuleAttributes(endAttrs);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "MODULE", id, "END_MODULE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ModuleNode(span, id, moduleName, usings, interfaces, classes, functions, attrs,
            issues, assumptions, invariants, decisions, context);
    }

    /// <summary>
    /// Parses a using directive.
    /// §U[System.Collections.Generic]            // using System.Collections.Generic;
    /// §U[Gen:System.Collections.Generic]        // using Gen = System.Collections.Generic;
    /// §U[static:System.Math]                    // using static System.Math;
    /// </summary>
    private UsingDirectiveNode ParseUsingDirective()
    {
        var startToken = Expect(TokenKind.Using);
        var attrs = ParseAttributes();

        // Interpret using attributes
        // Positional formats:
        // [namespace]                   -> using namespace;
        // [alias:namespace]             -> using alias = namespace;
        // [static:namespace]            -> using static namespace;
        var pos0 = attrs["_pos0"] ?? "";
        var pos1 = attrs["_pos1"];

        string @namespace;
        string? alias = null;
        bool isStatic = false;

        if (pos1 != null)
        {
            // Two-part format: [prefix:namespace]
            if (pos0.Equals("static", StringComparison.OrdinalIgnoreCase))
            {
                isStatic = true;
                @namespace = pos1;
            }
            else
            {
                // Alias format: [alias:namespace]
                alias = pos0;
                @namespace = pos1;
            }
        }
        else
        {
            // Single-part format: [namespace]
            @namespace = pos0;
        }

        if (string.IsNullOrEmpty(@namespace))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "USING", "namespace");
        }

        return new UsingDirectiveNode(startToken.Span, @namespace, alias, isStatic);
    }

    private FunctionNode ParseFunction()
    {
        var startToken = Expect(TokenKind.Func);
        var attrs = ParseAttributes();

        var (id, funcName, visibilityStr) = AttributeHelper.InterpretFuncAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FUNC", "id");
            id = "";
        }
        if (string.IsNullOrEmpty(funcName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FUNC", "name");
            funcName = "";
        }
        var visibility = ParseVisibility(visibilityStr);

        var typeParameters = new List<TypeParameterNode>();
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Extended Features: Function-level metadata
        var examples = new List<ExampleNode>();
        var issues = new List<IssueNode>();
        UsesNode? uses = null;
        UsedByNode? usedBy = null;
        var assumptions = new List<AssumeNode>();
        ComplexityNode? complexity = null;
        SinceNode? since = null;
        DeprecatedNode? deprecated = null;
        var breakingChanges = new List<BreakingChangeNode>();
        var properties = new List<PropertyTestNode>();
        LockNode? lockNode = null;
        AuthorNode? author = null;
        TaskRefNode? taskRef = null;

        // Parse optional sections before BODY
        while (!IsAtEnd && !Check(TokenKind.Body) && !Check(TokenKind.EndFunc))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                // WHERE clauses add constraints to existing type parameters
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Ensures))
            {
                postconditions.Add(ParseEnsures());
            }
            // Extended Features: Function-level metadata
            else if (Check(TokenKind.Example))
            {
                examples.Add(ParseExample());
            }
            else if (Check(TokenKind.Todo))
            {
                issues.Add(ParseTodoIssue());
            }
            else if (Check(TokenKind.Fixme))
            {
                issues.Add(ParseFixmeIssue());
            }
            else if (Check(TokenKind.Hack))
            {
                issues.Add(ParseHackIssue());
            }
            else if (Check(TokenKind.Uses))
            {
                uses = ParseUses();
            }
            else if (Check(TokenKind.UsedBy))
            {
                usedBy = ParseUsedBy();
            }
            else if (Check(TokenKind.Assume))
            {
                assumptions.Add(ParseAssume());
            }
            else if (Check(TokenKind.Complexity))
            {
                complexity = ParseComplexity();
            }
            else if (Check(TokenKind.Since))
            {
                since = ParseSince();
            }
            else if (Check(TokenKind.Deprecated))
            {
                deprecated = ParseDeprecated();
            }
            else if (Check(TokenKind.Breaking))
            {
                breakingChanges.Add(ParseBreaking());
            }
            else if (Check(TokenKind.PropertyTest))
            {
                properties.Add(ParsePropertyTest());
            }
            else if (Check(TokenKind.Lock))
            {
                lockNode = ParseLock();
            }
            else if (Check(TokenKind.AgentAuthor))
            {
                author = ParseAuthor();
            }
            else if (Check(TokenKind.TaskRef))
            {
                taskRef = ParseTaskRef();
            }
            else
            {
                break;
            }
        }

        // Parse BODY - either explicit §BODY/§END_BODY or implicit (no BODY markers)
        if (Check(TokenKind.Body))
        {
            // Explicit §BODY ... §END_BODY
            body = ParseBody();
        }
        else if (!Check(TokenKind.EndFunc))
        {
            // Implicit body - parse statements until §/F
            body = ParseImplicitBody();
        }

        var endToken = Expect(TokenKind.EndFunc);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndFuncAttributes(endAttrs);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "FUNC", id, "END_FUNC", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new FunctionNode(span, id, funcName, visibility, typeParameters, parameters, output, effects,
            preconditions, postconditions, body, attrs,
            examples, issues, uses, usedBy, assumptions, complexity, since, deprecated, breakingChanges,
            properties, lockNode, author, taskRef);
    }

    private ParameterNode ParseParameter()
    {
        var startToken = Expect(TokenKind.In);
        var attrs = ParseAttributes();

        var (typeName, paramName, semantic) = AttributeHelper.InterpretInputAttributes(attrs);
        if (string.IsNullOrEmpty(paramName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IN", "name");
            paramName = "";
        }
        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IN", "type");
            typeName = "";
        }

        // Add semantic to attrs if present
        if (!string.IsNullOrEmpty(semantic))
        {
            attrs.Add("semantic", semantic);
        }

        return new ParameterNode(startToken.Span, paramName, typeName, attrs);
    }

    private OutputNode ParseOutput()
    {
        var startToken = Expect(TokenKind.Out);
        var attrs = ParseAttributes();

        // Interpret positional attributes
        var typeName = AttributeHelper.InterpretOutputAttributes(attrs);
        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "OUT", "type");
            typeName = "";
        }

        return new OutputNode(startToken.Span, typeName);
    }

    private EffectsNode ParseEffects()
    {
        var startToken = Expect(TokenKind.Effects);
        var attrs = ParseAttributes();

        // Interpret positional attributes
        var effects = AttributeHelper.InterpretEffectsAttributes(attrs);

        return new EffectsNode(startToken.Span, effects);
    }

    private RequiresNode ParseRequires()
    {
        var startToken = Expect(TokenKind.Requires);
        var attrs = ParseAttributes();
        var message = attrs["message"];

        var condition = ParseExpression();

        var span = startToken.Span.Union(condition.Span);
        return new RequiresNode(span, condition, message, attrs);
    }

    private EnsuresNode ParseEnsures()
    {
        var startToken = Expect(TokenKind.Ensures);
        var attrs = ParseAttributes();
        var message = attrs["message"];

        var condition = ParseExpression();

        var span = startToken.Span.Union(condition.Span);
        return new EnsuresNode(span, condition, message, attrs);
    }

    private List<StatementNode> ParseBody()
    {
        Expect(TokenKind.Body);

        var statements = new List<StatementNode>();

        while (!IsAtEnd && !Check(TokenKind.EndBody))
        {
            var statement = ParseStatement();
            if (statement != null)
            {
                statements.Add(statement);
            }
        }

        Expect(TokenKind.EndBody);

        return statements;
    }

    /// <summary>
    /// Parses an implicit body (no §BODY markers) - statements until §/F
    /// </summary>
    private List<StatementNode> ParseImplicitBody()
    {
        var statements = new List<StatementNode>();

        while (!IsAtEnd && !Check(TokenKind.EndFunc))
        {
            var statement = ParseStatement();
            if (statement != null)
            {
                statements.Add(statement);
            }
        }

        return statements;
    }

    private StatementNode? ParseStatement()
    {
        if (Check(TokenKind.Call))
        {
            return ParseCallStatement();
        }
        else if (Check(TokenKind.Return))
        {
            return ParseReturnStatement();
        }
        else if (Check(TokenKind.For))
        {
            return ParseForStatement();
        }
        else if (Check(TokenKind.While))
        {
            return ParseWhileStatement();
        }
        else if (Check(TokenKind.Do))
        {
            return ParseDoWhileStatement();
        }
        else if (Check(TokenKind.If))
        {
            return ParseIfStatement();
        }
        else if (Check(TokenKind.Bind))
        {
            return ParseBindStatement();
        }
        else if (Check(TokenKind.Match))
        {
            return ParseMatchStatement();
        }
        else if (Check(TokenKind.Foreach))
        {
            return ParseForeachStatement();
        }
        else if (Check(TokenKind.Assign))
        {
            return ParseAssignmentStatement();
        }
        else if (Check(TokenKind.Try))
        {
            return ParseTryStatement();
        }
        else if (Check(TokenKind.Throw))
        {
            return ParseThrowStatement();
        }
        else if (Check(TokenKind.Rethrow))
        {
            return ParseRethrowStatement();
        }
        else if (Check(TokenKind.Subscribe))
        {
            return ParseEventSubscribe();
        }
        else if (Check(TokenKind.Unsubscribe))
        {
            return ParseEventUnsubscribe();
        }
        else if (Check(TokenKind.Break))
        {
            return ParseBreakStatement();
        }
        else if (Check(TokenKind.Continue))
        {
            return ParseContinueStatement();
        }
        // Print aliases
        else if (Check(TokenKind.Print))
        {
            return ParsePrintStatement(isWriteLine: true);
        }
        else if (Check(TokenKind.PrintF))
        {
            return ParsePrintStatement(isWriteLine: false);
        }

        _diagnostics.ReportUnexpectedToken(Current.Span, "statement", Current.Kind);
        Advance();
        return null;
    }

    /// <summary>
    /// Parses §P or §Pf print statements.
    /// §P expression  -> Console.WriteLine(expression)
    /// §Pf expression -> Console.Write(expression)
    /// </summary>
    private PrintStatementNode ParsePrintStatement(bool isWriteLine)
    {
        var startToken = Advance(); // consume §P or §Pf

        // Parse the expression to print
        var expression = ParseExpression();

        var span = startToken.Span.Union(expression.Span);
        return new PrintStatementNode(span, expression, isWriteLine);
    }

    private CallStatementNode ParseCallStatement()
    {
        var startToken = Expect(TokenKind.Call);
        var attrs = ParseAttributes();

        // Interpret call attributes
        var (target, fallible) = AttributeHelper.InterpretCallAttributes(attrs);
        if (string.IsNullOrEmpty(target))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CALL", "target");
        }

        var arguments = new List<ExpressionNode>();

        // Support implicit closing - if a single expression follows without §A, treat it as the argument
        // §C[Console.WriteLine] "Hello" is equivalent to §C[Console.WriteLine] §A "Hello" §/C
        if (IsExpressionStart() && !Check(TokenKind.Arg))
        {
            // Single expression argument without §A prefix - implicit closing
            arguments.Add(ParseExpression());
            var span = startToken.Span.Union(arguments[0].Span);
            return new CallStatementNode(span, target, fallible, arguments, attrs);
        }

        // Standard format with explicit §A and §/C
        while (!IsAtEnd && !Check(TokenKind.EndCall))
        {
            if (Check(TokenKind.Arg))
            {
                arguments.Add(ParseArgument());
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndCall);
        var span2 = startToken.Span.Union(endToken.Span);

        return new CallStatementNode(span2, target, fallible, arguments, attrs);
    }

    private ExpressionNode ParseArgument()
    {
        Expect(TokenKind.Arg);

        return ParseExpression();
    }

    private ReturnStatementNode ParseReturnStatement()
    {
        var startToken = Expect(TokenKind.Return);

        ExpressionNode? expression = null;

        // Check if there's an expression following RETURN
        if (IsExpressionStart())
        {
            expression = ParseExpression();
        }

        var span = expression != null ? startToken.Span.Union(expression.Span) : startToken.Span;
        return new ReturnStatementNode(span, expression);
    }

    private bool IsExpressionStart()
    {
        return Current.Kind is TokenKind.IntLiteral
            or TokenKind.StrLiteral
            or TokenKind.BoolLiteral
            or TokenKind.FloatLiteral
            or TokenKind.Identifier
            // Lisp-style expression
            or TokenKind.OpenParen
            // Phase 3: Type System
            or TokenKind.Some
            or TokenKind.None
            or TokenKind.Ok
            or TokenKind.Err
            or TokenKind.Match
            or TokenKind.Record
            // Phase 6: Arrays
            or TokenKind.Array
            or TokenKind.Index
            or TokenKind.Length
            // Phase 7: Generics
            or TokenKind.Generic
            // Phase 8: Classes
            or TokenKind.New
            or TokenKind.This
            or TokenKind.Base
            or TokenKind.Call  // Call expressions (§C[...])
            // Phase 11: Lambdas
            or TokenKind.Lambda
            // Phase 12: Async/Await
            or TokenKind.Await
            // Phase 9: String Interpolation and Modern Operators
            or TokenKind.Interpolate
            or TokenKind.NullCoalesce
            or TokenKind.NullConditional
            or TokenKind.RangeOp
            or TokenKind.IndexEnd
            // Phase 10: Advanced Patterns
            or TokenKind.With;
    }

    private ExpressionNode ParseExpression()
    {
        return Current.Kind switch
        {
            TokenKind.IntLiteral => ParseIntLiteral(),
            TokenKind.StrLiteral => ParseStringLiteral(),
            TokenKind.BoolLiteral => ParseBoolLiteral(),
            TokenKind.FloatLiteral => ParseFloatLiteral(),
            TokenKind.Identifier => ParseReference(),
            // Lisp-style expression: (op args...) or inline lambda: () → body
            TokenKind.OpenParen => ParseParenExpressionOrInlineLambda(),
            // Collection/array initializer: {elem1, elem2, ...}
            TokenKind.OpenBrace => ParseCollectionInitializer(),
            // Phase 3: Type System
            TokenKind.Some => ParseSomeExpression(),
            TokenKind.None => ParseNoneExpression(),
            TokenKind.Ok => ParseOkExpression(),
            TokenKind.Err => ParseErrExpression(),
            TokenKind.Match => ParseMatchExpression(),
            TokenKind.Record => ParseRecordCreation(),
            // Phase 6: Arrays
            TokenKind.Array => ParseArrayCreation(),
            TokenKind.Index => ParseArrayAccess(),
            TokenKind.Length => ParseArrayLength(),
            // Phase 7: Generics
            TokenKind.Generic => ParseGenericType(),
            // Phase 8: Classes
            TokenKind.New => ParseNewExpression(),
            TokenKind.This => ParseThisExpression(),
            TokenKind.Base => ParseBaseExpression(),
            TokenKind.Call => ParseCallExpression(),
            // Phase 11: Lambdas
            TokenKind.Lambda => ParseLambdaExpression(),
            // Phase 12: Async/Await
            TokenKind.Await => ParseAwaitExpression(),
            // Phase 9: String Interpolation and Modern Operators
            TokenKind.Interpolate => ParseInterpolatedString(),
            TokenKind.NullCoalesce => ParseNullCoalesce(),
            TokenKind.NullConditional => ParseNullConditional(),
            TokenKind.RangeOp => ParseRangeExpression(),
            TokenKind.IndexEnd => ParseIndexFromEnd(),
            // Phase 10: Advanced Patterns
            TokenKind.With => ParseWithExpression(),
            _ => throw new InvalidOperationException($"Unexpected token {Current.Kind}")
        };
    }

    /// <summary>
    /// Parses a collection/array initializer: {elem1, elem2, ...}
    /// </summary>
    private ExpressionNode ParseCollectionInitializer()
    {
        var startToken = Expect(TokenKind.OpenBrace);
        var elements = new List<ExpressionNode>();

        // Parse elements until closing brace
        while (!IsAtEnd && !Check(TokenKind.CloseBrace))
        {
            var element = ParseExpression();
            elements.Add(element);

            // Elements are separated by commas
            if (Check(TokenKind.Comma))
            {
                Advance();
            }
            else if (!Check(TokenKind.CloseBrace))
            {
                // If there's no comma and no closing brace, something's wrong
                break;
            }
        }

        var endToken = Expect(TokenKind.CloseBrace);
        var span = startToken.Span.Union(endToken.Span);

        // Create an ArrayCreationNode with inferred type
        return new ArrayCreationNode(
            span,
            "arr_init",
            "arr_init",
            "any", // Type will be inferred by context
            null,  // No explicit size
            elements,
            new AttributeCollection());
    }

    /// <summary>
    /// Parses either a Lisp-style expression or an inline lambda.
    /// - Inline lambda: () → body or (param) → body
    /// - Lisp expression: (op args...)
    /// </summary>
    private ExpressionNode ParseParenExpressionOrInlineLambda()
    {
        // Look ahead to determine if this is an inline lambda: () → or (params) →
        // We need to see if after the closing paren there's an arrow
        var savedPosition = _position;
        var startToken = Expect(TokenKind.OpenParen);

        // Check for empty parens followed by arrow: () →
        if (Check(TokenKind.CloseParen))
        {
            Advance(); // consume )
            if (Check(TokenKind.Arrow))
            {
                // This is an inline lambda with no parameters
                Advance(); // consume →
                var (exprBody, stmtBody, endSpan) = ParseLambdaBody();
                var span = startToken.Span.Union(endSpan);
                return new LambdaExpressionNode(
                    span,
                    "inline",
                    new List<LambdaParameterNode>(),
                    null, // effects
                    false, // not async
                    exprBody, // expressionBody
                    stmtBody, // statementBody
                    new AttributeCollection());
            }
            // Not a lambda, restore and parse as Lisp (though empty Lisp is likely an error)
            _position = savedPosition;
            return ParseLispExpression();
        }

        // Check for single parameter: (param) → or (type:param) →
        if (Check(TokenKind.Identifier))
        {
            var firstToken = Advance();
            var paramName = firstToken.Text;
            string? paramType = null;

            // Check for typed parameter: type:name
            if (Check(TokenKind.Colon))
            {
                Advance(); // consume :
                paramType = paramName;
                paramName = Expect(TokenKind.Identifier).Text;
            }

            if (Check(TokenKind.CloseParen))
            {
                Advance(); // consume )
                if (Check(TokenKind.Arrow))
                {
                    // This is an inline lambda with one parameter
                    Advance(); // consume →
                    var (exprBody, stmtBody, endSpan) = ParseLambdaBody();
                    var span = startToken.Span.Union(endSpan);
                    var param = new LambdaParameterNode(firstToken.Span, paramName, paramType);
                    return new LambdaExpressionNode(
                        span,
                        "inline",
                        new List<LambdaParameterNode> { param },
                        null, // effects
                        false, // not async
                        exprBody, // expressionBody
                        stmtBody, // statementBody
                        new AttributeCollection());
                }
            }
            // Not a lambda, restore and parse as Lisp
            _position = savedPosition;
        }
        else
        {
            // Not an identifier, restore position (we already consumed the open paren)
            _position = savedPosition;
        }

        return ParseLispExpression();
    }

    /// <summary>
    /// Parses a lambda body after the arrow (→).
    /// Can be either an expression or a statement block.
    /// Returns (expressionBody, statementBody, endSpan).
    /// </summary>
    private (ExpressionNode?, IReadOnlyList<StatementNode>?, TextSpan) ParseLambdaBody()
    {
        // Check for statement block: { ... }
        if (Check(TokenKind.OpenBrace))
        {
            var statements = new List<StatementNode>();
            Advance(); // consume {

            // Parse statements until closing brace
            while (!IsAtEnd && !Check(TokenKind.CloseBrace))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    statements.Add(stmt);
                }
            }

            var endToken = Expect(TokenKind.CloseBrace);
            return (null, statements, endToken.Span);
        }
        else
        {
            // Expression body
            var body = ParseExpression();
            return (body, null, body.Span);
        }
    }

    /// <summary>
    /// Parses a Lisp-style prefix expression: (op arg1 arg2 ...)
    /// Examples: (+ 1 2), (== x 0), (% i 15), (! flag), (- x)
    /// </summary>
    private ExpressionNode ParseLispExpression()
    {
        var startToken = Expect(TokenKind.OpenParen);

        // Get the operator
        var (opKind, opText) = ParseLispOperator();

        // Parse arguments until we hit CloseParen
        var args = new List<ExpressionNode>();
        while (!Check(TokenKind.CloseParen) && !IsAtEnd)
        {
            args.Add(ParseLispArgument());
        }

        var endToken = Expect(TokenKind.CloseParen);
        var span = startToken.Span.Union(endToken.Span);

        // Handle ternary conditional: (? cond then else)
        if (opText == "?" && args.Count == 3)
        {
            return new ConditionalExpressionNode(span, args[0], args[1], args[2]);
        }

        // Determine if this is unary or binary based on argument count and operator
        if (args.Count == 1 && IsUnaryOperator(opKind))
        {
            var unaryOp = GetUnaryOperator(opKind, opText);
            if (unaryOp.HasValue)
            {
                return new UnaryOperationNode(span, unaryOp.Value, args[0]);
            }
        }

        if (args.Count >= 2)
        {
            var binaryOp = BinaryOperatorExtensions.FromString(opText);
            if (binaryOp.HasValue)
            {
                // For more than 2 arguments, chain them: (+ a b c) => ((a + b) + c)
                var result = args[0];
                for (int i = 1; i < args.Count; i++)
                {
                    result = new BinaryOperationNode(span, binaryOp.Value, result, args[i]);
                }
                return result;
            }
        }

        // Fallback: if single argument and can be binary op with implicit self
        if (args.Count == 1)
        {
            var binaryOp = BinaryOperatorExtensions.FromString(opText);
            if (binaryOp.HasValue)
            {
                // This is likely an error - binary op with only one argument
                _diagnostics.ReportError(span, DiagnosticCode.UnexpectedToken,
                    $"Binary operator '{opText}' requires at least two operands");
                return args[0];
            }
        }

        // If we have exactly 2 args, try to interpret as binary
        if (args.Count == 2)
        {
            var binaryOp = BinaryOperatorExtensions.FromString(opText);
            if (binaryOp.HasValue)
            {
                return new BinaryOperationNode(span, binaryOp.Value, args[0], args[1]);
            }
        }

        // Unknown operator
        _diagnostics.ReportError(span, DiagnosticCode.UnexpectedToken,
            $"Unknown operator '{opText}' in Lisp expression");
        return args.Count > 0 ? args[0] : new IntLiteralNode(span, 0);
    }

    private (TokenKind kind, string text) ParseLispOperator()
    {
        var token = Current;
        var text = token.Text;

        switch (token.Kind)
        {
            case TokenKind.Plus:
                Advance();
                return (TokenKind.Plus, "+");
            case TokenKind.Minus:
                Advance();
                return (TokenKind.Minus, "-");
            case TokenKind.Star:
                Advance();
                return (TokenKind.Star, "*");
            case TokenKind.StarStar:
                Advance();
                return (TokenKind.StarStar, "**");
            case TokenKind.Slash:
                Advance();
                return (TokenKind.Slash, "/");
            case TokenKind.Percent:
                Advance();
                return (TokenKind.Percent, "%");
            case TokenKind.EqualEqual:
                Advance();
                return (TokenKind.EqualEqual, "==");
            case TokenKind.BangEqual:
                Advance();
                return (TokenKind.BangEqual, "!=");
            case TokenKind.Less:
                Advance();
                return (TokenKind.Less, "<");
            case TokenKind.LessEqual:
                Advance();
                return (TokenKind.LessEqual, "<=");
            case TokenKind.Greater:
                Advance();
                return (TokenKind.Greater, ">");
            case TokenKind.GreaterEqual:
                Advance();
                return (TokenKind.GreaterEqual, ">=");
            case TokenKind.AmpAmp:
                Advance();
                return (TokenKind.AmpAmp, "&&");
            case TokenKind.PipePipe:
                Advance();
                return (TokenKind.PipePipe, "||");
            case TokenKind.Amp:
                Advance();
                return (TokenKind.Amp, "&");
            case TokenKind.Pipe:
                Advance();
                return (TokenKind.Pipe, "|");
            case TokenKind.Caret:
                Advance();
                return (TokenKind.Caret, "^");
            case TokenKind.LessLess:
                Advance();
                return (TokenKind.LessLess, "<<");
            case TokenKind.GreaterGreater:
                Advance();
                return (TokenKind.GreaterGreater, ">>");
            case TokenKind.Exclamation:
                Advance();
                return (TokenKind.Exclamation, "!");
            case TokenKind.Tilde:
                Advance();
                return (TokenKind.Tilde, "~");
            case TokenKind.Question:
                Advance();
                return (TokenKind.Question, "?");
            case TokenKind.Identifier:
                // Support word operators like "and", "or", "not", "mod"
                Advance();
                return (TokenKind.Identifier, text.ToLowerInvariant() switch
                {
                    "and" => "&&",
                    "or" => "||",
                    "not" => "!",
                    "mod" => "%",
                    "eq" => "==",
                    "ne" or "neq" => "!=",
                    "lt" => "<",
                    "le" or "lte" => "<=",
                    "gt" => ">",
                    "ge" or "gte" => ">=",
                    _ => text
                });
            default:
                _diagnostics.ReportError(token.Span, DiagnosticCode.UnexpectedToken,
                    $"Expected operator in Lisp expression, found '{token.Text}'");
                Advance();
                return (token.Kind, text);
        }
    }

    private bool IsUnaryOperator(TokenKind kind)
    {
        return kind is TokenKind.Exclamation or TokenKind.Tilde or TokenKind.Minus
            or TokenKind.Identifier; // for 'not'
    }

    private UnaryOperator? GetUnaryOperator(TokenKind kind, string text)
    {
        return text switch
        {
            "!" or "not" => UnaryOperator.Not,
            "~" => UnaryOperator.BitwiseNot,
            "-" => UnaryOperator.Negate,
            _ => null
        };
    }

    /// <summary>
    /// Parses an argument inside a Lisp expression.
    /// Can be a literal, identifier (bare variable), or nested Lisp expression.
    /// Supports trailing member access (e.g., §C[...] §/C.Length)
    /// Also handles 'is' pattern expressions: expr is Type [variable]
    /// </summary>
    private ExpressionNode ParseLispArgument()
    {
        ExpressionNode expr = Current.Kind switch
        {
            TokenKind.IntLiteral => ParseIntLiteral(),
            TokenKind.StrLiteral => ParseStringLiteral(),
            TokenKind.BoolLiteral => ParseBoolLiteral(),
            TokenKind.FloatLiteral => ParseFloatLiteral(),
            TokenKind.Identifier => ParseBareReference(), // Bare variable reference
            TokenKind.OpenParen => ParseLispExpression(), // Nested expression
            TokenKind.Call => ParseCallExpression(), // Call expression inside Lisp
            TokenKind.New => ParseNewExpression(), // New expression inside Lisp
            TokenKind.Await => ParseAwaitExpression(), // Await expression inside Lisp
            _ => throw new InvalidOperationException($"Unexpected token {Current.Kind} in Lisp expression argument")
        };

        // Handle trailing member access (e.g., §C[...] §/C.Length or run?.Status)
        // and array access (e.g., array{index})
        while (Check(TokenKind.Dot) || Check(TokenKind.NullConditional) || Check(TokenKind.OpenBrace))
        {
            if (Check(TokenKind.OpenBrace))
            {
                // Array access: array{index}
                Advance(); // consume '{'
                var indexExpr = ParseExpression();
                var endToken = Expect(TokenKind.CloseBrace);
                var span = expr.Span.Union(endToken.Span);
                expr = new ArrayAccessNode(span, expr, indexExpr);
            }
            else
            {
                var isNullConditional = Check(TokenKind.NullConditional);
                Advance(); // consume '.' or '?.'
                var memberToken = Expect(TokenKind.Identifier);
                var span = expr.Span.Union(memberToken.Span);
                if (isNullConditional)
                {
                    expr = new NullConditionalNode(span, expr, memberToken.Text);
                }
                else
                {
                    expr = new FieldAccessNode(span, expr, memberToken.Text);
                }
            }
        }

        // Handle 'is' pattern expression: expr is Type [variable]
        // This handles C# pattern matching like "other is UnitSystem otherUnitSystem"
        if (Check(TokenKind.Identifier) && Current.Text == "is")
        {
            expr = ParseIsPatternExpression(expr);
        }

        return expr;
    }

    /// <summary>
    /// Parses an 'is' pattern expression: expr is Type [variable]
    /// The expr has already been parsed, we're at the 'is' keyword.
    /// Returns a ReferenceNode with the full pattern expression.
    /// </summary>
    private ExpressionNode ParseIsPatternExpression(ExpressionNode left)
    {
        var startSpan = left.Span;
        Advance(); // consume 'is'

        // Parse the type name (possibly qualified: Namespace.Type or generic: Type<T>)
        var typeBuilder = new System.Text.StringBuilder();
        if (!Check(TokenKind.Identifier))
        {
            _diagnostics.ReportError(Current.Span, DiagnosticCode.UnexpectedToken,
                $"Expected type name after 'is', found '{Current.Text}'");
            return left;
        }

        var typeToken = Current;
        typeBuilder.Append(Current.Text);
        Advance();

        // Handle qualified names (Namespace.Type) and generic types (Type<T>)
        while (Check(TokenKind.Dot) || Check(TokenKind.Less))
        {
            if (Check(TokenKind.Dot))
            {
                typeBuilder.Append('.');
                Advance();
                if (Check(TokenKind.Identifier))
                {
                    typeBuilder.Append(Current.Text);
                    Advance();
                }
            }
            else if (Check(TokenKind.Less))
            {
                // Parse generic type arguments: Type<T, U>
                typeBuilder.Append('<');
                Advance();
                int depth = 1;
                while (depth > 0 && !IsAtEnd)
                {
                    if (Check(TokenKind.Less))
                        depth++;
                    else if (Check(TokenKind.Greater))
                        depth--;
                    typeBuilder.Append(Current.Text);
                    Advance();
                }
            }
        }

        var typeName = typeBuilder.ToString();
        string? variableName = null;

        // Check for optional variable declaration: is Type variableName
        // Only consume if it's an identifier and not the start of another expression or close paren
        if (Check(TokenKind.Identifier) && !IsLispOperatorStart(Current.Text))
        {
            variableName = Current.Text;
            Advance();
        }

        // Build the full expression as a reference node
        var leftStr = GetExpressionString(left);
        var fullExpr = variableName != null
            ? $"{leftStr} is {typeName} {variableName}"
            : $"{leftStr} is {typeName}";

        var endSpan = Peek(-1).Span;
        return new ReferenceNode(startSpan.Union(endSpan), fullExpr);
    }

    /// <summary>
    /// Checks if the identifier could be a Lisp operator (to avoid consuming it as a variable name).
    /// </summary>
    private bool IsLispOperatorStart(string text)
    {
        // Common Lisp operators that might appear after an 'is' pattern
        return text is "and" or "or" or "not";
    }

    /// <summary>
    /// Gets the string representation of an expression for use in pattern expressions.
    /// </summary>
    private string GetExpressionString(ExpressionNode expr)
    {
        return expr switch
        {
            ReferenceNode r => r.Name,
            FieldAccessNode f => $"{GetExpressionString(f.Target)}.{f.FieldName}",
            NullConditionalNode n => $"{GetExpressionString(n.Target)}?.{n.MemberName}",
            _ => expr.ToString() ?? ""
        };
    }

    /// <summary>
    /// Parses a bare identifier as a variable reference (used in Lisp expressions).
    /// Reserved words like true/false are handled as literals by the lexer.
    /// </summary>
    private ReferenceNode ParseBareReference()
    {
        var token = Expect(TokenKind.Identifier);
        return new ReferenceNode(token.Span, token.Text);
    }

    private IntLiteralNode ParseIntLiteral()
    {
        var token = Expect(TokenKind.IntLiteral);
        var value = token.Value is int i ? i : 0;
        return new IntLiteralNode(token.Span, value);
    }

    private StringLiteralNode ParseStringLiteral()
    {
        var token = Expect(TokenKind.StrLiteral);
        var value = token.Value as string ?? "";
        return new StringLiteralNode(token.Span, value);
    }

    private BoolLiteralNode ParseBoolLiteral()
    {
        var token = Expect(TokenKind.BoolLiteral);
        var value = token.Value is bool b && b;
        return new BoolLiteralNode(token.Span, value);
    }

    private FloatLiteralNode ParseFloatLiteral()
    {
        var token = Expect(TokenKind.FloatLiteral);
        var value = token.Value is double d ? d : 0.0;
        return new FloatLiteralNode(token.Span, value);
    }

    private ExpressionNode ParseReference()
    {
        var token = Expect(TokenKind.Identifier);
        ExpressionNode expr = new ReferenceNode(token.Span, token.Text);

        // Handle trailing member access (e.g., _startOptions?.Agenda or obj.Property)
        while (Check(TokenKind.Dot) || Check(TokenKind.NullConditional))
        {
            var isNullConditional = Check(TokenKind.NullConditional);
            Advance(); // consume '.' or '?.'
            var memberToken = Expect(TokenKind.Identifier);
            var span = expr.Span.Union(memberToken.Span);
            if (isNullConditional)
            {
                expr = new NullConditionalNode(span, expr, memberToken.Text);
            }
            else
            {
                expr = new FieldAccessNode(span, expr, memberToken.Text);
            }
        }

        return expr;
    }

    // Phase 3: Type System Expression Parsing

    private SomeExpressionNode ParseSomeExpression()
    {
        var startToken = Expect(TokenKind.Some);
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new SomeExpressionNode(span, value);
    }

    private NoneExpressionNode ParseNoneExpression()
    {
        var startToken = Expect(TokenKind.None);
        var attrs = ParseAttributes();
        // v2 syntax: §NN{type} - type is positional
        var typeName = attrs["_pos0"];
        // Expand compact type name to internal form
        if (!string.IsNullOrEmpty(typeName))
        {
            typeName = AttributeHelper.ExpandType(typeName);
        }
        return new NoneExpressionNode(startToken.Span, typeName);
    }

    private OkExpressionNode ParseOkExpression()
    {
        var startToken = Expect(TokenKind.Ok);
        var value = ParseExpression();
        var span = startToken.Span.Union(value.Span);
        return new OkExpressionNode(span, value);
    }

    private ErrExpressionNode ParseErrExpression()
    {
        var startToken = Expect(TokenKind.Err);
        var error = ParseExpression();
        var span = startToken.Span.Union(error.Span);
        return new ErrExpressionNode(span, error);
    }

    private RecordCreationNode ParseRecordCreation()
    {
        var startToken = Expect(TokenKind.Record);
        var attrs = ParseAttributes();
        var typeName = GetRequiredAttribute(attrs, "type", "RECORD", startToken.Span);

        var fields = new List<FieldAssignmentNode>();
        while (Check(TokenKind.Field))
        {
            var fieldToken = Expect(TokenKind.Field);
            var fieldAttrs = ParseAttributes();
            var fieldName = GetRequiredAttribute(fieldAttrs, "name", "FIELD", fieldToken.Span);
            var value = ParseExpression();
            fields.Add(new FieldAssignmentNode(fieldToken.Span.Union(value.Span), fieldName, value));
        }

        var lastSpan = fields.Count > 0 ? fields[^1].Span : startToken.Span;
        return new RecordCreationNode(startToken.Span.Union(lastSpan), typeName, fields);
    }

    private MatchExpressionNode ParseMatchExpression()
    {
        var startToken = Expect(TokenKind.Match);
        var attrs = ParseAttributes();
        var id = AttributeHelper.InterpretMatchAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MATCH", "id");
            id = "";
        }

        var target = ParseExpression();
        var cases = ParseMatchCases();

        var endToken = Expect(TokenKind.EndMatch);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndMatchAttributes(endAttrs);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "MATCH", id, "END_MATCH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MatchExpressionNode(span, id, target, cases, attrs);
    }

    private StatementNode ParseMatchStatement()
    {
        var startToken = Expect(TokenKind.Match);
        var attrs = ParseAttributes();
        var id = AttributeHelper.InterpretMatchAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "MATCH", "id");
            id = "";
        }

        // Check if this is a match expression (indicated by :expr in second positional attribute)
        // {match003:expr} parses as _pos0="match003", _pos1="expr"
        var isExpression = attrs["_pos1"] == "expr";

        var target = ParseExpression();
        var cases = ParseMatchCases();

        var endToken = Expect(TokenKind.EndMatch);
        var endAttrs = ParseAttributes();
        var endId = AttributeHelper.InterpretEndMatchAttributes(endAttrs);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "MATCH", id, "END_MATCH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);

        // Return MatchExpressionNode wrapped in ReturnStatementNode if :expr was present
        if (isExpression)
        {
            var matchExpr = new MatchExpressionNode(span, id, target, cases, attrs);
            return new ReturnStatementNode(span, matchExpr);
        }

        return new MatchStatementNode(span, id, target, cases, attrs);
    }

    private List<MatchCaseNode> ParseMatchCases()
    {
        var cases = new List<MatchCaseNode>();

        while (Check(TokenKind.Case))
        {
            var caseToken = Expect(TokenKind.Case);
            var pattern = ParsePattern();

            // Check for guard clause: §WHEN expression
            ExpressionNode? guard = null;
            if (Check(TokenKind.When))
            {
                Expect(TokenKind.When);
                guard = ParseExpression();
            }

            var body = new List<StatementNode>();
            while (!IsAtEnd && !Check(TokenKind.Case) && !Check(TokenKind.EndMatch))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }

            var lastSpan = body.Count > 0 ? body[^1].Span : caseToken.Span;
            cases.Add(new MatchCaseNode(caseToken.Span.Union(lastSpan), pattern, guard, body));
        }

        return cases;
    }

    private PatternNode ParsePattern()
    {
        // Handle relational patterns: gte, lte, gt, lt followed by a value
        if (Check(TokenKind.Identifier))
        {
            var token = Current;
            var op = token.Text switch
            {
                "gte" => ">=",
                "lte" => "<=",
                "gt" => ">",
                "lt" => "<",
                _ => null
            };

            if (op != null)
            {
                Advance(); // consume the operator keyword
                var operand = ParseExpression();
                return new RelationalPatternNode(token.Span.Union(operand.Span), op, operand);
            }

            // Handle var pattern: var name
            if (token.Text == "var" && Peek(1).Kind == TokenKind.Identifier)
            {
                Advance(); // consume 'var'
                var nameToken = Advance(); // consume name
                return new VarPatternNode(token.Span.Union(nameToken.Span), nameToken.Text);
            }

            // Handle null pattern
            if (token.Text == "null")
            {
                Advance();
                return new ConstantPatternNode(token.Span, new ReferenceNode(token.Span, "null"));
            }

            Advance();
            if (token.Text == "_")
            {
                return new WildcardPatternNode(token.Span);
            }
            return new VariablePatternNode(token.Span, token.Text);
        }

        if (Check(TokenKind.IntLiteral) || Check(TokenKind.StrLiteral) ||
            Check(TokenKind.BoolLiteral) || Check(TokenKind.FloatLiteral))
        {
            var literal = ParseExpression();
            return new LiteralPatternNode(literal.Span, literal);
        }

        if (Check(TokenKind.Some))
        {
            var startToken = Expect(TokenKind.Some);
            var innerPattern = ParsePattern();
            return new SomePatternNode(startToken.Span.Union(innerPattern.Span), innerPattern);
        }

        if (Check(TokenKind.None))
        {
            var token = Expect(TokenKind.None);
            return new NonePatternNode(token.Span);
        }

        if (Check(TokenKind.Ok))
        {
            var startToken = Expect(TokenKind.Ok);
            var innerPattern = ParsePattern();
            return new OkPatternNode(startToken.Span.Union(innerPattern.Span), innerPattern);
        }

        if (Check(TokenKind.Err))
        {
            var startToken = Expect(TokenKind.Err);
            var innerPattern = ParsePattern();
            return new ErrPatternNode(startToken.Span.Union(innerPattern.Span), innerPattern);
        }

        // Default: wildcard
        return new WildcardPatternNode(Current.Span);
    }

    private ForStatementNode ParseForStatement()
    {
        var startToken = Expect(TokenKind.For);
        var attrs = ParseAttributes();

        // Interpret loop attributes
        var (id, varName, fromStr, toStr, stepStr) = AttributeHelper.InterpretForAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FOR", "id");
        }
        if (string.IsNullOrEmpty(varName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FOR", "var");
        }

        // Parse from/to/step - these can be expressions or literal values in attributes
        ExpressionNode from;
        ExpressionNode to;
        ExpressionNode? step = null;

        if (!string.IsNullOrEmpty(fromStr) && int.TryParse(fromStr, out var fromVal))
        {
            from = new IntLiteralNode(startToken.Span, fromVal);
        }
        else
        {
            from = ParseExpression();
        }

        if (!string.IsNullOrEmpty(toStr) && int.TryParse(toStr, out var toVal))
        {
            to = new IntLiteralNode(startToken.Span, toVal);
        }
        else
        {
            to = ParseExpression();
        }

        if (!string.IsNullOrEmpty(stepStr) && int.TryParse(stepStr, out var stepVal))
        {
            step = new IntLiteralNode(startToken.Span, stepVal);
        }

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndFor);

        var endToken = Expect(TokenKind.EndFor);
        var endAttrs = ParseAttributes();
        // Positional: [id]
        var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "FOR", id, "END_FOR", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ForStatementNode(span, id, varName, from, to, step, body, attrs);
    }

    private WhileStatementNode ParseWhileStatement()
    {
        var startToken = Expect(TokenKind.While);
        var attrs = ParseAttributes();

        // Positional: [id]
        var id = attrs["_pos0"] ?? attrs["id"] ?? "";
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "WHILE", "id");
        }

        // Parse condition expression
        var condition = ParseExpression();

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndWhile);

        var endToken = Expect(TokenKind.EndWhile);
        var endAttrs = ParseAttributes();
        // Positional: [id]
        var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "WHILE", id, "END_WHILE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new WhileStatementNode(span, id, condition, body, attrs);
    }

    private DoWhileStatementNode ParseDoWhileStatement()
    {
        var startToken = Expect(TokenKind.Do);
        var attrs = ParseAttributes();

        // Positional: [id]
        var id = attrs["_pos0"] ?? attrs["id"] ?? "";
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DO", "id");
        }

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndDo);

        var endToken = Expect(TokenKind.EndDo);
        var endAttrs = ParseAttributes();
        // Positional: [id]
        var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "DO", id, "END_DO", endId);
        }

        // Parse condition expression (comes after §/DO[id])
        var condition = ParseExpression();

        var span = startToken.Span.Union(Current.Span);
        return new DoWhileStatementNode(span, id, body, condition, attrs);
    }

    private IfStatementNode ParseIfStatement()
    {
        var startToken = Expect(TokenKind.If);
        var attrs = ParseAttributes();

        // Interpret if attributes
        var id = AttributeHelper.InterpretIfAttributes(attrs);
        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IF", "id");
        }

        // Parse condition expression
        var condition = ParseExpression();

        // Parse then body
        var thenBody = new List<StatementNode>();
        var elseIfClauses = new List<ElseIfClauseNode>();
        List<StatementNode>? elseBody = null;

        // Check for arrow syntax: §IF{id} condition → statement
        if (Check(TokenKind.Arrow))
        {
            Advance(); // consume arrow
            var singleStmt = ParseStatement();
            if (singleStmt != null)
            {
                thenBody.Add(singleStmt);
            }

            // Parse optional §EI (else if) and §EL (else) with arrow syntax
            while (Check(TokenKind.ElseIf))
            {
                var elseIfToken = Expect(TokenKind.ElseIf);
                var elseIfCondition = ParseExpression();
                var elseIfBody = new List<StatementNode>();

                if (Check(TokenKind.Arrow))
                {
                    Advance(); // consume arrow
                    var elseIfStmt = ParseStatement();
                    if (elseIfStmt != null)
                    {
                        elseIfBody.Add(elseIfStmt);
                    }
                }
                else
                {
                    // Multi-statement body (until next clause or end)
                    while (!IsAtEnd && !Check(TokenKind.EndIf) && !Check(TokenKind.Else) && !Check(TokenKind.ElseIf))
                    {
                        var stmt = ParseStatement();
                        if (stmt != null)
                        {
                            elseIfBody.Add(stmt);
                        }
                    }
                }

                elseIfClauses.Add(new ElseIfClauseNode(elseIfToken.Span, elseIfCondition, elseIfBody));
            }

            // Parse optional §EL (else) with arrow syntax
            if (Check(TokenKind.Else))
            {
                Expect(TokenKind.Else);
                elseBody = new List<StatementNode>();

                if (Check(TokenKind.Arrow))
                {
                    Advance(); // consume arrow
                    var elseStmt = ParseStatement();
                    if (elseStmt != null)
                    {
                        elseBody.Add(elseStmt);
                    }
                }
                else
                {
                    while (!IsAtEnd && !Check(TokenKind.EndIf))
                    {
                        var stmt = ParseStatement();
                        if (stmt != null)
                        {
                            elseBody.Add(stmt);
                        }
                    }
                }
            }

            var endToken = Expect(TokenKind.EndIf);
            var endAttrs = ParseAttributes();
            var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

            if (endId != id)
            {
                _diagnostics.ReportMismatchedId(endToken.Span, "IF", id, "END_IF", endId);
            }

            var span = startToken.Span.Union(endToken.Span);
            return new IfStatementNode(span, id, condition, thenBody, elseIfClauses, elseBody, attrs);
        }

        // Standard multi-statement body
        while (!IsAtEnd && !Check(TokenKind.EndIf) && !Check(TokenKind.Else) && !Check(TokenKind.ElseIf))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                thenBody.Add(stmt);
            }
        }

        // Parse ELSEIF clauses
        while (Check(TokenKind.ElseIf))
        {
            var elseIfToken = Expect(TokenKind.ElseIf);
            var elseIfCondition = ParseExpression();
            var elseIfBody = new List<StatementNode>();

            while (!IsAtEnd && !Check(TokenKind.EndIf) && !Check(TokenKind.Else) && !Check(TokenKind.ElseIf))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    elseIfBody.Add(stmt);
                }
            }

            elseIfClauses.Add(new ElseIfClauseNode(elseIfToken.Span, elseIfCondition, elseIfBody));
        }

        // Parse ELSE clause
        if (Check(TokenKind.Else))
        {
            Expect(TokenKind.Else);
            elseBody = new List<StatementNode>();

            while (!IsAtEnd && !Check(TokenKind.EndIf))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    elseBody.Add(stmt);
                }
            }
        }

        var endToken2 = Expect(TokenKind.EndIf);
        var endAttrs2 = ParseAttributes();
        // Positional: [id]
        var endId2 = endAttrs2["_pos0"] ?? endAttrs2["id"] ?? "";

        if (endId2 != id)
        {
            _diagnostics.ReportMismatchedId(endToken2.Span, "IF", id, "END_IF", endId2);
        }

        var span2 = startToken.Span.Union(endToken2.Span);
        return new IfStatementNode(span2, id, condition, thenBody, elseIfClauses, elseBody, attrs);
    }

    private BindStatementNode ParseBindStatement()
    {
        var startToken = Expect(TokenKind.Bind);
        var attrs = ParseAttributes();

        // Interpret bind attributes
        var (name, isMutable) = AttributeHelper.InterpretBindAttributes(attrs);
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "BIND", "name");
        }
        var typeName = attrs["type"];

        // Parse optional initializer expression
        ExpressionNode? initializer = null;
        if (IsExpressionStart())
        {
            initializer = ParseExpression();
        }

        var span = initializer != null ? startToken.Span.Union(initializer.Span) : startToken.Span;
        return new BindStatementNode(span, name, typeName, isMutable, initializer, attrs);
    }

    private List<StatementNode> ParseStatementBlock(params TokenKind[] terminators)
    {
        var statements = new List<StatementNode>();

        while (!IsAtEnd && !terminators.Any(Check))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                statements.Add(stmt);
            }
        }

        return statements;
    }

    private AttributeCollection ParseAttributes()
    {
        var attrs = new AttributeCollection();

        // Parse structural braces {} for tag attributes
        // Note: [] is now reserved for array types to align with LLM training
        while (Check(TokenKind.OpenBrace))
        {
            Advance(); // consume {

            // Positional format: {value1:value2:...} or {value}
            ParsePositionalAttributes(attrs);

            Expect(TokenKind.CloseBrace);
        }

        return attrs;
    }

    private void ParsePositionalAttributes(AttributeCollection attrs)
    {
        // Parse colon-separated values: {value1:value2:value3}
        // Store them as _pos0, _pos1, _pos2, etc. for later interpretation
        var values = new List<string>();
        var position = 0;

        do
        {
            var value = ParseValue();
            values.Add(value);
            attrs.Add($"_pos{position}", value);
            position++;
        }
        while (MatchColon());

        // Also store the raw positional count
        attrs.Add("_posCount", position.ToString());
    }

    /// <summary>
    /// Checks if current position has an escaped brace (\{ or \}).
    /// If so, returns the brace character and advances past both tokens.
    /// </summary>
    private char? TryParseEscapedBrace()
    {
        if (Check(TokenKind.Backslash))
        {
            var next = Peek(1).Kind;
            if (next == TokenKind.OpenBrace)
            {
                Advance(); // consume backslash
                Advance(); // consume {
                return '{';
            }
            if (next == TokenKind.CloseBrace)
            {
                Advance(); // consume backslash
                Advance(); // consume }
                return '}';
            }
        }
        return null;
    }

    private string ParseValue()
    {
        var sb = new System.Text.StringBuilder();

        // Handle ~ prefix for mutability
        if (Check(TokenKind.Tilde))
        {
            sb.Append('~');
            Advance();
        }

        // Handle # prefix for semantic shortcodes
        if (Check(TokenKind.Hash))
        {
            sb.Append('#');
            Advance();
        }

        // Handle ? prefix for Option types
        if (Check(TokenKind.Question))
        {
            sb.Append('?');
            Advance();
        }

        // Handle array types like [u8], [[i32]], [str]
        if (Check(TokenKind.OpenBracket))
        {
            var depth = 0;
            while (Check(TokenKind.OpenBracket))
            {
                sb.Append('[');
                Advance();
                depth++;
            }

            // Parse the element type
            if (Check(TokenKind.Identifier))
            {
                sb.Append(Advance().Text);

                // Handle generic element types like [List<int>]
                if (Check(TokenKind.Less))
                {
                    sb.Append('<');
                    Advance();
                    var genericDepth = 1;
                    while (!IsAtEnd && genericDepth > 0)
                    {
                        if (Check(TokenKind.Less))
                        {
                            sb.Append('<');
                            genericDepth++;
                            Advance();
                        }
                        else if (Check(TokenKind.Greater))
                        {
                            sb.Append('>');
                            genericDepth--;
                            Advance();
                        }
                        else if (Check(TokenKind.GreaterGreater))
                        {
                            // Handle >> from nested generics like [List<Task<int>>]
                            sb.Append(">>");
                            genericDepth -= 2;
                            Advance();
                        }
                        else if (Check(TokenKind.Identifier))
                        {
                            sb.Append(Advance().Text);
                        }
                        else if (Check(TokenKind.Comma))
                        {
                            sb.Append(',');
                            Advance();
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }

            // Close the array brackets
            while (depth > 0 && Check(TokenKind.CloseBracket))
            {
                sb.Append(']');
                Advance();
                depth--;
            }

            return sb.ToString();
        }

        // Parse the main value (identifier, literal, or compound identifier)
        if (Check(TokenKind.Identifier))
        {
            sb.Append(Advance().Text);

            // Handle generic types like ILogger<MeetingModeratorService> or List<string>
            if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance(); // consume <

                // Parse generic type arguments (may be nested)
                var depth = 1;
                while (!IsAtEnd && depth > 0)
                {
                    if (Check(TokenKind.Less))
                    {
                        sb.Append('<');
                        depth++;
                        Advance();
                    }
                    else if (Check(TokenKind.Greater))
                    {
                        sb.Append('>');
                        depth--;
                        Advance();
                    }
                    else if (Check(TokenKind.GreaterGreater))
                    {
                        // Handle >> from nested generics like Task<List<int>>
                        sb.Append(">>");
                        depth -= 2;
                        Advance();
                    }
                    else if (Check(TokenKind.Identifier))
                    {
                        sb.Append(Advance().Text);
                    }
                    else if (Check(TokenKind.Comma))
                    {
                        sb.Append(',');
                        Advance();
                    }
                    else if (Check(TokenKind.Question))
                    {
                        sb.Append('?');
                        Advance();
                    }
                    else if (Current.Text == ".")
                    {
                        sb.Append('.');
                        Advance();
                    }
                    else
                    {
                        // Unknown token in generic, stop parsing
                        break;
                    }
                }
            }

            // Handle compound identifiers like Console.WriteLine
            while (Current.Text == ".")
            {
                sb.Append(Advance().Text); // consume '.'
                if (Check(TokenKind.Identifier))
                {
                    sb.Append(Advance().Text);
                }
            }

            // Handle comma-separated values like partial,static for modifiers
            // Only consume comma if followed by an identifier (not a colon or close brace)
            while (Check(TokenKind.Comma) && Peek(1).Kind == TokenKind.Identifier)
            {
                sb.Append(',');
                Advance(); // consume comma
                sb.Append(Advance().Text); // consume identifier
            }
        }
        else if (Check(TokenKind.StrLiteral))
        {
            sb.Append(Advance().Value as string ?? "");
        }
        else if (Check(TokenKind.IntLiteral))
        {
            sb.Append(Advance().Value?.ToString() ?? "");
        }
        else if (Check(TokenKind.BoolLiteral))
        {
            sb.Append(Advance().Value?.ToString()?.ToLowerInvariant() ?? "");
        }

        // Handle ! suffix for fallibility or Result types (T!E)
        if (Check(TokenKind.Exclamation))
        {
            sb.Append('!');
            Advance();

            // Check for error type after ! (for Result types)
            if (Check(TokenKind.Identifier))
            {
                sb.Append(Advance().Text);
            }
        }

        // Handle complex expressions that may contain escaped braces and other tokens
        // Continue parsing until we hit a colon (separator) or unescaped close brace (end of attributes)
        while (!IsAtEnd && !Check(TokenKind.Colon) && !Check(TokenKind.CloseBrace))
        {
            // Handle escaped braces: \{ and \}
            var escapedBrace = TryParseEscapedBrace();
            if (escapedBrace.HasValue)
            {
                sb.Append(escapedBrace.Value);
                continue;
            }

            // Handle common expression tokens
            if (Check(TokenKind.Identifier))
            {
                sb.Append(Advance().Text);
            }
            else if (Current.Text == ".")
            {
                sb.Append('.');
                Advance();
            }
            else if (Check(TokenKind.OpenParen))
            {
                sb.Append('(');
                Advance();
            }
            else if (Check(TokenKind.CloseParen))
            {
                sb.Append(')');
                Advance();
            }
            else if (Check(TokenKind.Comma))
            {
                // Only consume comma if not followed by close brace (end of args)
                if (Peek(1).Kind == TokenKind.CloseBrace)
                    break;
                sb.Append(',');
                Advance();
            }
            else if (Check(TokenKind.Less))
            {
                sb.Append('<');
                Advance();
            }
            else if (Check(TokenKind.Greater))
            {
                sb.Append('>');
                Advance();
            }
            else if (Check(TokenKind.IntLiteral))
            {
                sb.Append(Advance().Value?.ToString() ?? "");
            }
            else if (Check(TokenKind.StrLiteral))
            {
                sb.Append('"');
                sb.Append(Advance().Value as string ?? "");
                sb.Append('"');
            }
            else
            {
                // Unknown token, stop parsing this value
                break;
            }
        }

        return sb.ToString();
    }

    private bool MatchColon()
    {
        if (Check(TokenKind.Colon))
        {
            Advance();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Parses C#-style attributes in the format [@AttributeName] or [@AttributeName(args)].
    /// These appear after the structural brackets: §CLASS[id:name][@Route("api")][@ApiController]
    /// </summary>
    private IReadOnlyList<CalorAttributeNode> ParseCSharpAttributes()
    {
        var attributes = new List<CalorAttributeNode>();

        // Keep parsing while we see [@
        while (Check(TokenKind.OpenBracket) && Peek(1).Kind == TokenKind.At)
        {
            var startToken = Advance(); // consume [
            Advance(); // consume @

            // Parse attribute name
            if (!Check(TokenKind.Identifier))
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "attribute name", Current.Kind);
                // Try to recover by finding the closing bracket
                while (!IsAtEnd && !Check(TokenKind.CloseBracket))
                {
                    Advance();
                }
                if (Check(TokenKind.CloseBracket))
                {
                    Advance();
                }
                continue;
            }

            var nameToken = Advance();
            var name = nameToken.Text;

            // Handle qualified names like System.ComponentModel.Description
            while (Current.Text == ".")
            {
                Advance(); // consume .
                if (Check(TokenKind.Identifier))
                {
                    name += "." + Advance().Text;
                }
            }

            var arguments = new List<CalorAttributeArgument>();

            // Check for arguments: [@Attr(args)]
            if (Check(TokenKind.OpenParen))
            {
                Advance(); // consume (
                arguments = ParseCSharpAttributeArguments();
                Expect(TokenKind.CloseParen);
            }

            Expect(TokenKind.CloseBracket);

            var span = startToken.Span.Union(Current.Span);
            attributes.Add(new CalorAttributeNode(span, name, arguments));
        }

        return attributes;
    }

    /// <summary>
    /// Parses the arguments inside a C# attribute: (arg1, "arg2", Name=value)
    /// </summary>
    private List<CalorAttributeArgument> ParseCSharpAttributeArguments()
    {
        var args = new List<CalorAttributeArgument>();

        if (Check(TokenKind.CloseParen))
        {
            return args;
        }

        do
        {
            // Check for named argument: Name=value
            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
            {
                var nameToken = Advance();
                Advance(); // consume =
                var value = ParseCSharpAttributeValue();
                args.Add(new CalorAttributeArgument(nameToken.Text, value));
            }
            else
            {
                // Positional argument
                var value = ParseCSharpAttributeValue();
                args.Add(new CalorAttributeArgument(value));
            }
        }
        while (MatchComma());

        return args;
    }

    /// <summary>
    /// Parses a single value in a C# attribute argument.
    /// </summary>
    private object ParseCSharpAttributeValue()
    {
        if (Check(TokenKind.StrLiteral))
        {
            return Advance().Value as string ?? "";
        }
        if (Check(TokenKind.IntLiteral))
        {
            return Advance().Value ?? 0;
        }
        if (Check(TokenKind.FloatLiteral))
        {
            return Advance().Value ?? 0.0;
        }
        if (Check(TokenKind.BoolLiteral))
        {
            return Advance().Value ?? false;
        }
        if (Check(TokenKind.Identifier))
        {
            var text = Advance().Text;

            // Handle typeof(TypeName)
            if (text == "typeof" && Check(TokenKind.OpenParen))
            {
                Advance(); // consume (
                var typeName = "";
                while (!IsAtEnd && !Check(TokenKind.CloseParen))
                {
                    typeName += Advance().Text;
                }
                Expect(TokenKind.CloseParen);
                return new TypeOfReference(typeName);
            }

            // Handle qualified names like System.String or enum values like AccessLevel.Admin
            while (Current.Text == ".")
            {
                text += Advance().Text; // consume .
                if (Check(TokenKind.Identifier))
                {
                    text += Advance().Text;
                }
            }

            return text;
        }

        // Unrecognized value - return empty string
        _diagnostics.ReportUnexpectedToken(Current.Span, "attribute value", Current.Kind);
        return "";
    }

    private bool MatchComma()
    {
        if (Check(TokenKind.Comma))
        {
            Advance();
            return true;
        }
        return false;
    }

    private string GetRequiredAttribute(AttributeCollection attrs, string name, string tagName, TextSpan span)
    {
        var value = attrs[name];
        if (string.IsNullOrEmpty(value))
        {
            _diagnostics.ReportMissingRequiredAttribute(span, tagName, name);
            return "";
        }
        return value;
    }

    private static Visibility ParseVisibility(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "public" or "pub" => Visibility.Public,
            "internal" or "int" => Visibility.Internal,
            "private" or "priv" => Visibility.Private,
            _ => Visibility.Private
        };
    }

    // Phase 6: Arrays and Collections

    /// <summary>
    /// Parses array creation.
    /// §ARR[arr1:i32:10]                         // int[] arr1 = new int[10]; (sized)
    /// §ARR[arr2:i32] §A 1 §A 2 §A 3 §/ARR[arr2] // int[] arr2 = { 1, 2, 3 }; (initialized)
    /// </summary>
    private ArrayCreationNode ParseArrayCreation()
    {
        var startToken = Expect(TokenKind.Array);
        var attrs = ParseAttributes();

        // Positional: [id:type:size?] or just [id:type] for initialized arrays
        var id = attrs["_pos0"] ?? "";
        var elementType = attrs["_pos1"] ?? "i32";
        var sizeStr = attrs["_pos2"];

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "ARR", "id");
        }

        ExpressionNode? size = null;
        var initializer = new List<ExpressionNode>();

        // If size is specified in attributes, use it
        if (!string.IsNullOrEmpty(sizeStr) && int.TryParse(sizeStr, out var sizeVal))
        {
            size = new IntLiteralNode(startToken.Span, sizeVal);
        }
        else
        {
            // Check for initializer elements (§A expressions until §/ARR)
            while (!IsAtEnd && !Check(TokenKind.EndArray) && Check(TokenKind.Arg))
            {
                initializer.Add(ParseArgument());
            }
        }

        var endSpan = startToken.Span;
        // Check for optional end tag (required for initialized arrays)
        if (Check(TokenKind.EndArray))
        {
            var endToken = Expect(TokenKind.EndArray);
            var endAttrs = ParseAttributes();
            var endId = endAttrs["_pos0"] ?? "";

            if (endId != id)
            {
                _diagnostics.ReportMismatchedId(endToken.Span, "ARR", id, "END_ARR", endId);
            }
            endSpan = endToken.Span;
        }

        var span = startToken.Span.Union(endSpan);
        return new ArrayCreationNode(span, id, id, elementType, size, initializer, attrs);
    }

    /// <summary>
    /// Parses array element access.
    /// §IDX §REF[name=arr] 0                     // arr[0]
    /// </summary>
    private ArrayAccessNode ParseArrayAccess()
    {
        var startToken = Expect(TokenKind.Index);

        var array = ParseExpression();
        var index = ParseExpression();

        var span = startToken.Span.Union(index.Span);
        return new ArrayAccessNode(span, array, index);
    }

    /// <summary>
    /// Parses array length access.
    /// §LEN §REF[name=arr]                       // arr.Length
    /// </summary>
    private ArrayLengthNode ParseArrayLength()
    {
        var startToken = Expect(TokenKind.Length);

        var array = ParseExpression();

        var span = startToken.Span.Union(array.Span);
        return new ArrayLengthNode(span, array);
    }

    /// <summary>
    /// Parses foreach statement.
    /// §EACH[each1:item:i32] §REF[name=arr]      // foreach (int item in arr)
    ///   ...
    /// §/EACH[each1]
    /// </summary>
    private ForeachStatementNode ParseForeachStatement()
    {
        var startToken = Expect(TokenKind.Foreach);
        var attrs = ParseAttributes();

        // Positional: [id:variable:type]
        var id = attrs["_pos0"] ?? "";
        var variableName = attrs["_pos1"] ?? "item";
        var variableType = attrs["_pos2"] ?? "var";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "EACH", "id");
        }

        // Parse collection expression
        var collection = ParseExpression();

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndForeach);

        var endToken = Expect(TokenKind.EndForeach);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "EACH", id, "END_EACH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ForeachStatementNode(span, id, variableName, variableType, collection, body, attrs);
    }

    // Phase 7: Generics

    /// <summary>
    /// Parses a type parameter declaration.
    /// §TP[T]                                    // type parameter T
    /// </summary>
    private TypeParameterNode ParseTypeParameter()
    {
        var startToken = Expect(TokenKind.TypeParam);
        var attrs = ParseAttributes();

        // Positional: [name]
        var name = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "TP", "name");
        }

        // Constraints are added later via WHERE clauses
        return new TypeParameterNode(startToken.Span, name, Array.Empty<TypeConstraintNode>());
    }

    /// <summary>
    /// Parses a WHERE clause and adds constraints to the appropriate type parameter.
    /// §WHERE[T:IComparable]                     // where T : IComparable
    /// §WHERE[T:class]                           // where T : class
    /// §WHERE[T:struct]                          // where T : struct
    /// §WHERE[T:new]                             // where T : new()
    /// </summary>
    private void ParseWhereClause(List<TypeParameterNode> typeParameters)
    {
        var startToken = Expect(TokenKind.Where);
        var attrs = ParseAttributes();

        // Positional: {typeParam:constraint1,constraint2,...}
        var typeParamName = attrs["_pos0"] ?? "";
        var constraintStr = attrs["_pos1"] ?? "";

        if (string.IsNullOrEmpty(typeParamName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "WHERE", "type parameter name");
            return;
        }

        // Find the type parameter to add the constraint to
        var typeParamIndex = typeParameters.FindIndex(tp => tp.Name == typeParamName);
        if (typeParamIndex < 0)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                $"Type parameter '{typeParamName}' not found");
            return;
        }

        // Parse multiple constraints (comma-separated)
        var constraintParts = constraintStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var newConstraints = typeParameters[typeParamIndex].Constraints.ToList();

        foreach (var part in constraintParts)
        {
            var trimmedPart = part.Trim();
            var (kind, typeName) = trimmedPart.ToLowerInvariant() switch
            {
                "class" => (TypeConstraintKind.Class, (string?)null),
                "struct" => (TypeConstraintKind.Struct, (string?)null),
                "new" => (TypeConstraintKind.New, (string?)null),
                _ => (TypeConstraintKind.TypeName, trimmedPart)
            };

            var constraint = new TypeConstraintNode(startToken.Span, kind, typeName);
            newConstraints.Add(constraint);
        }

        // Replace the type parameter with one that includes the new constraints
        var oldTypeParam = typeParameters[typeParamIndex];
        typeParameters[typeParamIndex] = new TypeParameterNode(oldTypeParam.Span, oldTypeParam.Name, newConstraints);
    }

    /// <summary>
    /// Parses a generic type instantiation.
    /// §G[List:i32]                              // List&lt;int&gt;
    /// §G[Dictionary:string:i32]                 // Dictionary&lt;string, int&gt;
    /// </summary>
    private GenericTypeNode ParseGenericType()
    {
        var startToken = Expect(TokenKind.Generic);
        var attrs = ParseAttributes();

        // Positional: [typeName:typeArg1:typeArg2:...]
        var typeName = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(typeName))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "G", "type name");
        }

        // Collect type arguments from remaining positional attributes
        var typeArgs = new List<string>();
        var posCount = attrs["_posCount"];
        if (int.TryParse(posCount, out var count))
        {
            for (int i = 1; i < count; i++)
            {
                var typeArg = attrs[$"_pos{i}"];
                if (!string.IsNullOrEmpty(typeArg))
                {
                    typeArgs.Add(typeArg);
                }
            }
        }

        return new GenericTypeNode(startToken.Span, typeName, typeArgs);
    }

    // Phase 8: Classes, Interfaces, Inheritance

    /// <summary>
    /// Parses an interface definition.
    /// §IFACE[i001:IShape]
    ///   §METHOD[m001:Area] §O[f64] §E[] §/METHOD[m001]
    /// §/IFACE[i001]
    /// </summary>
    private InterfaceDefinitionNode ParseInterfaceDefinition()
    {
        var startToken = Expect(TokenKind.Interface);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IFACE", "id");
        }
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "IFACE", "name");
        }

        var baseInterfaces = new List<string>();
        var methods = new List<MethodSignatureNode>();

        while (!IsAtEnd && !Check(TokenKind.EndInterface))
        {
            if (Check(TokenKind.Extends))
            {
                Expect(TokenKind.Extends);
                var extAttrs = ParseAttributes();
                var baseIface = extAttrs["_pos0"] ?? "";
                if (!string.IsNullOrEmpty(baseIface))
                {
                    baseInterfaces.Add(baseIface);
                }
            }
            else if (Check(TokenKind.Method))
            {
                methods.Add(ParseMethodSignature());
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "EXT, METHOD, or END_IFACE", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndInterface);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "IFACE", id, "END_IFACE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new InterfaceDefinitionNode(span, id, name, baseInterfaces, methods, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a method signature (for interfaces).
    /// §METHOD[m001:Area][@Obsolete] §O[f64] §E[] §/METHOD[m001]
    /// </summary>
    private MethodSignatureNode ParseMethodSignature()
    {
        var startToken = Expect(TokenKind.Method);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "METHOD", "id");
        }

        var typeParameters = new List<TypeParameterNode>();
        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;

        // Parse signature elements until END_METHOD
        while (!IsAtEnd && !Check(TokenKind.EndMethod))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndMethod);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "METHOD", id, "END_METHOD", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MethodSignatureNode(span, id, name, typeParameters, parameters, output, effects, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a class definition.
    /// §CLASS[c001:Shape:abs]
    ///   §IMPL[IShape]
    ///   §FLD[string:Name:pri]
    ///   §METHOD[m001:Area:pub:abs] §O[f64] §E[] §/METHOD[m001]
    /// §/CLASS[c001]
    /// </summary>
    private ClassDefinitionNode ParseClassDefinition()
    {
        var startToken = Expect(TokenKind.Class);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:modifiers?]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var modifiers = attrs["_pos2"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CLASS", "id");
        }
        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CLASS", "name");
        }

        var isAbstract = modifiers.Contains("abs", StringComparison.OrdinalIgnoreCase);
        var isSealed = modifiers.Contains("seal", StringComparison.OrdinalIgnoreCase);

        string? baseClass = null;
        var implementedInterfaces = new List<string>();
        var typeParameters = new List<TypeParameterNode>();

        // Extract type parameters from class name if present (e.g., Repository<T> or Cache<TKey,TValue>)
        var typeParamStart = name.IndexOf('<');
        if (typeParamStart >= 0)
        {
            var typeParamEnd = name.LastIndexOf('>');
            if (typeParamEnd > typeParamStart)
            {
                var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                name = name.Substring(0, typeParamStart);

                // Parse type parameter names (comma-separated)
                foreach (var tpName in typeParamStr.Split(','))
                {
                    var trimmedName = tpName.Trim();
                    if (!string.IsNullOrEmpty(trimmedName))
                    {
                        typeParameters.Add(new TypeParameterNode(startToken.Span, trimmedName, Array.Empty<TypeConstraintNode>()));
                    }
                }
            }
        }
        var fields = new List<ClassFieldNode>();
        var properties = new List<PropertyNode>();
        var constructors = new List<ConstructorNode>();
        var methods = new List<MethodNode>();

        while (!IsAtEnd && !Check(TokenKind.EndClass))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.Extends))
            {
                Expect(TokenKind.Extends);
                var extAttrs = ParseAttributes();
                baseClass = extAttrs["_pos0"] ?? "";
            }
            else if (Check(TokenKind.Implements))
            {
                Expect(TokenKind.Implements);
                var implAttrs = ParseAttributes();
                var iface = implAttrs["_pos0"] ?? "";
                if (!string.IsNullOrEmpty(iface))
                {
                    implementedInterfaces.Add(iface);
                }
            }
            else if (Check(TokenKind.FieldDef))
            {
                fields.Add(ParseClassField());
            }
            else if (Check(TokenKind.Property))
            {
                properties.Add(ParseProperty());
            }
            else if (Check(TokenKind.Constructor))
            {
                constructors.Add(ParseConstructor());
            }
            else if (Check(TokenKind.Method))
            {
                methods.Add(ParseMethodDefinition());
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "TP, WHERE, EXT, IMPL, FLD, PROP, CTOR, METHOD, or END_CLASS", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndClass);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "CLASS", id, "END_CLASS", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ClassDefinitionNode(span, id, name, isAbstract, isSealed, baseClass,
            implementedInterfaces, typeParameters, fields, properties, constructors, methods, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a class field.
    /// §FLD[string:Name:pri][@JsonIgnore]
    /// </summary>
    private ClassFieldNode ParseClassField()
    {
        var startToken = Expect(TokenKind.FieldDef);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [type:name:visibility?]
        var typeName = attrs["_pos0"] ?? "object";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? "private";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FLD", "name");
        }

        var visibility = ParseVisibility(visStr);

        // Check for optional default value (can be prefixed with = or just a direct expression)
        ExpressionNode? defaultValue = null;
        if (Check(TokenKind.Equals))
        {
            Advance(); // consume =
            defaultValue = ParseExpression();
        }
        else if (IsExpressionStart())
        {
            defaultValue = ParseExpression();
        }

        var span = defaultValue != null ? startToken.Span.Union(defaultValue.Span) : startToken.Span;
        return new ClassFieldNode(span, name, typeName, visibility, defaultValue, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a method definition (with body).
    /// §METHOD[m001:Area:pub:over][@HttpPost][@Authorize]
    ///   §O[f64] §E[]
    ///   §R §OP[kind=mul] 3.14159 §REF[name=Radius] §REF[name=Radius]
    /// §/METHOD[m001]
    /// </summary>
    private MethodNode ParseMethodDefinition()
    {
        var startToken = Expect(TokenKind.Method);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:visibility?:modifiers?]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? "private";
        var modStr = attrs["_pos3"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "METHOD", "id");
        }

        var visibility = ParseVisibility(visStr);
        var modifiers = ParseMethodModifiers(modStr);

        var typeParameters = new List<TypeParameterNode>();

        // Extract type parameters from method name if present (e.g., Create<T>)
        var typeParamStart = name.IndexOf('<');
        if (typeParamStart >= 0)
        {
            var typeParamEnd = name.LastIndexOf('>');
            if (typeParamEnd > typeParamStart)
            {
                var typeParamStr = name.Substring(typeParamStart + 1, typeParamEnd - typeParamStart - 1);
                name = name.Substring(0, typeParamStart);

                foreach (var tpName in typeParamStr.Split(','))
                {
                    var trimmedName = tpName.Trim();
                    if (!string.IsNullOrEmpty(trimmedName))
                    {
                        typeParameters.Add(new TypeParameterNode(startToken.Span, trimmedName, Array.Empty<TypeConstraintNode>()));
                    }
                }
            }
        }

        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Parse signature and body until END_METHOD
        while (!IsAtEnd && !Check(TokenKind.EndMethod))
        {
            if (Check(TokenKind.TypeParam))
            {
                typeParameters.Add(ParseTypeParameter());
            }
            else if (Check(TokenKind.Where))
            {
                ParseWhereClause(typeParameters);
            }
            else if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Out))
            {
                output = ParseOutput();
            }
            else if (Check(TokenKind.Effects))
            {
                effects = ParseEffects();
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Ensures))
            {
                postconditions.Add(ParseEnsures());
            }
            else
            {
                // Must be body statements
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        var endToken = Expect(TokenKind.EndMethod);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "METHOD", id, "END_METHOD", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MethodNode(span, id, name, visibility, modifiers, typeParameters, parameters,
            output, effects, preconditions, postconditions, body, attrs, csharpAttrs);
    }

    private static MethodModifiers ParseMethodModifiers(string modStr)
    {
        var mods = MethodModifiers.None;
        if (modStr.Contains("virt", StringComparison.OrdinalIgnoreCase)) mods |= MethodModifiers.Virtual;
        if (modStr.Contains("over", StringComparison.OrdinalIgnoreCase)) mods |= MethodModifiers.Override;
        if (modStr.Contains("abs", StringComparison.OrdinalIgnoreCase)) mods |= MethodModifiers.Abstract;
        if (modStr.Contains("seal", StringComparison.OrdinalIgnoreCase)) mods |= MethodModifiers.Sealed;
        if (modStr.Contains("stat", StringComparison.OrdinalIgnoreCase)) mods |= MethodModifiers.Static;
        return mods;
    }

    /// <summary>
    /// Parses a new expression.
    /// §NEW[Circle] §A "MyCircle" §A 5.0 §/NEW
    /// </summary>
    private NewExpressionNode ParseNewExpression()
    {
        var startToken = Expect(TokenKind.New);
        var attrs = ParseAttributes();

        // Positional: [typeName:typeArg1:typeArg2:...]
        var typeName = attrs["_pos0"] ?? "object";

        // Collect type arguments
        var typeArgs = new List<string>();
        var posCount = attrs["_posCount"];
        if (int.TryParse(posCount, out var count))
        {
            for (int i = 1; i < count; i++)
            {
                var typeArg = attrs[$"_pos{i}"];
                if (!string.IsNullOrEmpty(typeArg))
                {
                    typeArgs.Add(typeArg);
                }
            }
        }

        // Parse arguments
        var arguments = new List<ExpressionNode>();
        while (Check(TokenKind.Arg))
        {
            arguments.Add(ParseArgument());
        }

        // Check for optional end tag
        var endSpan = startToken.Span;
        if (Check(TokenKind.Identifier) && Current.Text == "/NEW")
        {
            endSpan = Advance().Span;
        }

        var span = arguments.Count > 0 ? startToken.Span.Union(arguments[^1].Span) : startToken.Span;
        return new NewExpressionNode(span, typeName, typeArgs, arguments);
    }

    /// <summary>
    /// Parses a 'this' expression with optional member access.
    /// §THIS or §THIS.property
    /// </summary>
    private ExpressionNode ParseThisExpression()
    {
        var token = Expect(TokenKind.This);
        ExpressionNode expr = new ThisExpressionNode(token.Span);

        // Handle trailing member access (e.g., §THIS.property or §THIS?.property)
        while (Check(TokenKind.Dot) || Check(TokenKind.NullConditional))
        {
            var isNullConditional = Check(TokenKind.NullConditional);
            Advance(); // consume '.' or '?.'
            if (!Check(TokenKind.Identifier))
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "member name", Current.Kind);
                break;
            }
            var memberToken = Advance();
            var span = expr.Span.Union(memberToken.Span);
            if (isNullConditional)
            {
                expr = new NullConditionalNode(span, expr, memberToken.Text);
            }
            else
            {
                expr = new FieldAccessNode(span, expr, memberToken.Text);
            }
        }

        return expr;
    }

    /// <summary>
    /// Parses a 'base' expression with optional member access.
    /// §BASE or §BASE.property
    /// </summary>
    private ExpressionNode ParseBaseExpression()
    {
        var token = Expect(TokenKind.Base);
        ExpressionNode expr = new BaseExpressionNode(token.Span);

        // Handle trailing member access (e.g., §BASE.method)
        while (Check(TokenKind.Dot) || Check(TokenKind.NullConditional))
        {
            var isNullConditional = Check(TokenKind.NullConditional);
            Advance(); // consume '.' or '?.'
            if (!Check(TokenKind.Identifier))
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "member name", Current.Kind);
                break;
            }
            var memberToken = Advance();
            var span = expr.Span.Union(memberToken.Span);
            if (isNullConditional)
            {
                expr = new NullConditionalNode(span, expr, memberToken.Text);
            }
            else
            {
                expr = new FieldAccessNode(span, expr, memberToken.Text);
            }
        }

        return expr;
    }

    /// <summary>
    /// Parses a call expression.
    /// §C[target] §A arg1 §A arg2 §/C
    /// </summary>
    private CallExpressionNode ParseCallExpression()
    {
        var startToken = Expect(TokenKind.Call);
        var attrs = ParseAttributes();

        // Positional: [target]
        var target = attrs["_pos0"] ?? "";

        var arguments = new List<ExpressionNode>();

        // Parse arguments until we hit EndCall
        while (!IsAtEnd && !Check(TokenKind.EndCall))
        {
            if (Check(TokenKind.Arg))
            {
                arguments.Add(ParseArgument());
            }
            else if (IsExpressionStart())
            {
                // Single expression argument without §A prefix
                arguments.Add(ParseExpression());
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndCall);
        var span = startToken.Span.Union(endToken.Span);
        return new CallExpressionNode(span, target, arguments);
    }

    // Phase 9: Properties and Constructors

    /// <summary>
    /// Parses a property definition.
    /// §PROP[p001:Name:string:pub][@JsonProperty("name")]
    ///   §GET
    ///   §SET[pri]
    /// §/PROP[p001]
    /// </summary>
    private PropertyNode ParseProperty()
    {
        var startToken = Expect(TokenKind.Property);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:name:type:visibility?]
        var id = attrs["_pos0"] ?? "";
        var name = attrs["_pos1"] ?? "";
        var typeName = attrs["_pos2"] ?? "object";
        var visStr = attrs["_pos3"] ?? "public";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "PROP", "id");
        }

        var visibility = ParseVisibility(visStr);

        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        PropertyAccessorNode? initer = null;
        ExpressionNode? defaultValue = null;

        while (!IsAtEnd && !Check(TokenKind.EndProperty))
        {
            if (Check(TokenKind.Get))
            {
                getter = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Get);
            }
            else if (Check(TokenKind.Set))
            {
                setter = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Set);
            }
            else if (Check(TokenKind.Init))
            {
                initer = ParsePropertyAccessor(PropertyAccessorNode.AccessorKind.Init);
            }
            else if (Check(TokenKind.Equals))
            {
                // Default value prefixed with =
                Advance(); // consume =
                defaultValue = ParseExpression();
            }
            else if (IsExpressionStart())
            {
                // Default value without = prefix
                defaultValue = ParseExpression();
            }
            else
            {
                break;
            }
        }

        var endToken = Expect(TokenKind.EndProperty);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "PROP", id, "END_PROP", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new PropertyNode(span, id, name, typeName, visibility, getter, setter, initer, defaultValue, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a property accessor.
    /// §GET
    /// §SET[pri]
    /// </summary>
    private PropertyAccessorNode ParsePropertyAccessor(PropertyAccessorNode.AccessorKind kind)
    {
        Token startToken;
        if (kind == PropertyAccessorNode.AccessorKind.Get)
        {
            startToken = Expect(TokenKind.Get);
        }
        else if (kind == PropertyAccessorNode.AccessorKind.Set)
        {
            startToken = Expect(TokenKind.Set);
        }
        else
        {
            startToken = Expect(TokenKind.Init);
        }

        var attrs = ParseAttributes();
        var visStr = attrs["_pos0"];
        var visibility = visStr != null ? ParseVisibility(visStr) : (Visibility?)null;

        var preconditions = new List<RequiresNode>();
        var body = new List<StatementNode>();

        // Determine the end token for this accessor
        var endTokenKind = kind switch
        {
            PropertyAccessorNode.AccessorKind.Get => TokenKind.EndGet,
            PropertyAccessorNode.AccessorKind.Set => TokenKind.EndSet,
            _ => TokenKind.EndProperty // Init doesn't have its own end token
        };

        // Parse optional preconditions and body (for non-auto properties)
        // Also stop at Equals for property default values, next accessor, or end tokens
        while (!IsAtEnd && !Check(TokenKind.Get) && !Check(TokenKind.Set) &&
               !Check(TokenKind.Init) && !Check(TokenKind.EndProperty) && !Check(TokenKind.Equals) &&
               !Check(TokenKind.EndGet) && !Check(TokenKind.EndSet))
        {
            if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        // Consume the closing token if present (§/GET or §/SET)
        if (Check(endTokenKind))
        {
            Advance();
        }

        return new PropertyAccessorNode(startToken.Span, kind, visibility, preconditions, body, attrs);
    }

    /// <summary>
    /// Parses a constructor.
    /// §CTOR[ctor1:pub]
    ///   §I[string:name]
    ///   §Q §OP[kind=gt] §REF[name=radius] 0
    ///   §BASE §A §REF[name=name] §/BASE
    ///   §ASSIGN §REF[name=Radius] §REF[name=radius]
    /// §/CTOR[ctor1]
    /// </summary>
    private ConstructorNode ParseConstructor()
    {
        var startToken = Expect(TokenKind.Constructor);
        var attrs = ParseAttributes();
        var csharpAttrs = ParseCSharpAttributes();

        // Positional: [id:visibility?]
        var id = attrs["_pos0"] ?? "";
        var visStr = attrs["_pos1"] ?? "public";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CTOR", "id");
        }

        var visibility = ParseVisibility(visStr);

        var parameters = new List<ParameterNode>();
        var preconditions = new List<RequiresNode>();
        ConstructorInitializerNode? initializer = null;
        var body = new List<StatementNode>();

        while (!IsAtEnd && !Check(TokenKind.EndConstructor))
        {
            if (Check(TokenKind.In))
            {
                parameters.Add(ParseParameter());
            }
            else if (Check(TokenKind.Requires))
            {
                preconditions.Add(ParseRequires());
            }
            else if (Check(TokenKind.Base))
            {
                initializer = ParseConstructorInitializer(isBase: true);
            }
            else if (Check(TokenKind.This))
            {
                initializer = ParseConstructorInitializer(isBase: false);
            }
            else
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    body.Add(stmt);
                }
            }
        }

        var endToken = Expect(TokenKind.EndConstructor);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "CTOR", id, "END_CTOR", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ConstructorNode(span, id, visibility, parameters, preconditions, initializer, body, attrs, csharpAttrs);
    }

    /// <summary>
    /// Parses a constructor initializer (: base(...) or : this(...)).
    /// </summary>
    private ConstructorInitializerNode ParseConstructorInitializer(bool isBase)
    {
        var startToken = isBase ? Expect(TokenKind.Base) : Expect(TokenKind.This);

        var arguments = new List<ExpressionNode>();

        // Parse arguments until we encounter something that's not an ARG
        while (Check(TokenKind.Arg))
        {
            arguments.Add(ParseArgument());
        }

        // Consume the closing token (§/BASE or §/THIS)
        var endTokenKind = isBase ? TokenKind.EndBase : TokenKind.EndThis;
        if (Check(endTokenKind))
        {
            Advance();
        }

        return new ConstructorInitializerNode(startToken.Span, isBase, arguments);
    }

    /// <summary>
    /// Parses an assignment statement.
    /// §ASSIGN §REF[name=Radius] §REF[name=radius]
    /// </summary>
    private AssignmentStatementNode ParseAssignmentStatement()
    {
        var startToken = Expect(TokenKind.Assign);

        var target = ParseExpression();
        var value = ParseExpression();

        var span = startToken.Span.Union(value.Span);
        return new AssignmentStatementNode(span, target, value);
    }

    // Phase 10: Try/Catch/Finally

    /// <summary>
    /// Parses a try/catch/finally statement.
    /// §TRY[try1]
    ///   ...
    /// §CATCH[IOException:ex]
    ///   ...
    /// §FINALLY
    ///   ...
    /// §/TRY[try1]
    /// </summary>
    private TryStatementNode ParseTryStatement()
    {
        var startToken = Expect(TokenKind.Try);
        var attrs = ParseAttributes();

        // Positional: [id]
        var id = attrs["_pos0"] ?? "";

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "TRY", "id");
        }

        // Parse try body
        var tryBody = new List<StatementNode>();
        while (!IsAtEnd && !Check(TokenKind.Catch) && !Check(TokenKind.Finally) && !Check(TokenKind.EndTry))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                tryBody.Add(stmt);
            }
        }

        // Parse catch clauses
        var catchClauses = new List<CatchClauseNode>();
        while (Check(TokenKind.Catch))
        {
            catchClauses.Add(ParseCatchClause());
        }

        // Parse optional finally
        List<StatementNode>? finallyBody = null;
        if (Check(TokenKind.Finally))
        {
            Expect(TokenKind.Finally);
            finallyBody = new List<StatementNode>();
            while (!IsAtEnd && !Check(TokenKind.EndTry))
            {
                var stmt = ParseStatement();
                if (stmt != null)
                {
                    finallyBody.Add(stmt);
                }
            }
        }

        var endToken = Expect(TokenKind.EndTry);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "TRY", id, "END_TRY", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new TryStatementNode(span, id, tryBody, catchClauses, finallyBody, attrs);
    }

    /// <summary>
    /// Parses a catch clause.
    /// §CATCH[IOException:ex]
    /// §CATCH
    /// </summary>
    private CatchClauseNode ParseCatchClause()
    {
        var startToken = Expect(TokenKind.Catch);
        var attrs = ParseAttributes();

        // Positional: [exceptionType:variableName?] or empty for catch-all
        var exceptionType = attrs["_pos0"];
        var variableName = attrs["_pos1"];

        // Check for when filter
        ExpressionNode? filter = null;
        if (Check(TokenKind.When))
        {
            Expect(TokenKind.When);
            filter = ParseExpression();
        }

        // Parse catch body
        var body = new List<StatementNode>();
        while (!IsAtEnd && !Check(TokenKind.Catch) && !Check(TokenKind.Finally) && !Check(TokenKind.EndTry))
        {
            var stmt = ParseStatement();
            if (stmt != null)
            {
                body.Add(stmt);
            }
        }

        var span = body.Count > 0 ? startToken.Span.Union(body[^1].Span) : startToken.Span;
        return new CatchClauseNode(span, exceptionType, variableName, filter, body, attrs);
    }

    /// <summary>
    /// Parses a throw statement.
    /// §THROW §NEW[ArgumentException] §A "Invalid" §/NEW
    /// </summary>
    private ThrowStatementNode ParseThrowStatement()
    {
        var startToken = Expect(TokenKind.Throw);

        ExpressionNode? exception = null;
        if (IsExpressionStart())
        {
            exception = ParseExpression();
        }

        var span = exception != null ? startToken.Span.Union(exception.Span) : startToken.Span;
        return new ThrowStatementNode(span, exception);
    }

    /// <summary>
    /// Parses a rethrow statement.
    /// §RETHROW
    /// </summary>
    private RethrowStatementNode ParseRethrowStatement()
    {
        var token = Expect(TokenKind.Rethrow);
        return new RethrowStatementNode(token.Span);
    }

    /// <summary>
    /// Parses a break statement.
    /// §BREAK
    /// </summary>
    private BreakStatementNode ParseBreakStatement()
    {
        var token = Expect(TokenKind.Break);
        return new BreakStatementNode(token.Span);
    }

    /// <summary>
    /// Parses a continue statement.
    /// §CONTINUE
    /// </summary>
    private ContinueStatementNode ParseContinueStatement()
    {
        var token = Expect(TokenKind.Continue);
        return new ContinueStatementNode(token.Span);
    }

    // Phase 11: Lambdas, Delegates, Events

    /// <summary>
    /// Parses a lambda expression.
    /// §LAM[lam1:x:i32] §OP[kind=mul] §REF[name=x] 2 §/LAM[lam1]
    /// </summary>
    private LambdaExpressionNode ParseLambdaExpression()
    {
        var startToken = Expect(TokenKind.Lambda);
        var attrs = ParseAttributes();

        // Positional: [id:param1:type1:param2:type2:...] or [id:async:param1:type1:...]
        var id = attrs["_pos0"] ?? "";
        var isAsync = false;

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "LAM", "id");
        }

        // Parse parameters from attributes
        var parameters = new List<LambdaParameterNode>();
        var posCount = attrs["_posCount"];
        if (int.TryParse(posCount, out var count))
        {
            int i = 1;
            // Check for async modifier
            var firstPos = attrs["_pos1"];
            if (firstPos?.Equals("async", StringComparison.OrdinalIgnoreCase) == true)
            {
                isAsync = true;
                i = 2;
            }

            // Parse parameter pairs: name:type
            while (i < count)
            {
                var paramName = attrs[$"_pos{i}"];
                var paramType = attrs[$"_pos{i + 1}"];

                if (!string.IsNullOrEmpty(paramName))
                {
                    parameters.Add(new LambdaParameterNode(startToken.Span, paramName, paramType));
                }
                i += 2;
            }
        }

        // Parse optional effects
        EffectsNode? effects = null;
        if (Check(TokenKind.Effects))
        {
            effects = ParseEffects();
        }

        // Parse body - either expression or statements
        ExpressionNode? expressionBody = null;
        List<StatementNode>? statementBody = null;

        if (IsExpressionStart() && !Check(TokenKind.EndLambda))
        {
            expressionBody = ParseExpression();
        }

        // Check if there are more statements after the expression
        while (!IsAtEnd && !Check(TokenKind.EndLambda))
        {
            if (statementBody == null)
            {
                statementBody = new List<StatementNode>();
            }
            var stmt = ParseStatement();
            if (stmt != null)
            {
                statementBody.Add(stmt);
            }
        }

        var endToken = Expect(TokenKind.EndLambda);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "LAM", id, "END_LAM", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new LambdaExpressionNode(span, id, parameters, effects, isAsync, expressionBody, statementBody, attrs);
    }

    /// <summary>
    /// Parses an event subscribe statement.
    /// §SUB §REF[name=button.Click] §REF[name=handler]
    /// </summary>
    private EventSubscribeNode ParseEventSubscribe()
    {
        var startToken = Expect(TokenKind.Subscribe);

        var @event = ParseExpression();
        var handler = ParseExpression();

        var span = startToken.Span.Union(handler.Span);
        return new EventSubscribeNode(span, @event, handler);
    }

    /// <summary>
    /// Parses an event unsubscribe statement.
    /// §UNSUB §REF[name=button.Click] §REF[name=handler]
    /// </summary>
    private EventUnsubscribeNode ParseEventUnsubscribe()
    {
        var startToken = Expect(TokenKind.Unsubscribe);

        var @event = ParseExpression();
        var handler = ParseExpression();

        var span = startToken.Span.Union(handler.Span);
        return new EventUnsubscribeNode(span, @event, handler);
    }

    // Phase 12: Async/Await

    /// <summary>
    /// Parses an await expression.
    /// §AWAIT §C[client.GetStringAsync] §A §REF[name=url] §/C
    /// §AWAIT[false] §C[reader.ReadAsync] §/C   // ConfigureAwait(false)
    /// </summary>
    private AwaitExpressionNode ParseAwaitExpression()
    {
        var startToken = Expect(TokenKind.Await);
        var attrs = ParseAttributes();

        // Check for ConfigureAwait attribute: §AWAIT[false] or §AWAIT[true]
        bool? configureAwait = null;
        var configValue = attrs["_pos0"];
        if (!string.IsNullOrEmpty(configValue))
        {
            if (configValue.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                configureAwait = false;
            }
            else if (configValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                configureAwait = true;
            }
        }

        // Parse the awaited expression
        var awaited = ParseExpression();

        var span = startToken.Span.Union(awaited.Span);
        return new AwaitExpressionNode(span, awaited, configureAwait);
    }

    // Phase 9: String Interpolation and Modern Operators

    /// <summary>
    /// Parses an interpolated string.
    /// §INTERP["Hello, " §EXP §REF[name=name] "!"] §/INTERP
    /// </summary>
    private InterpolatedStringNode ParseInterpolatedString()
    {
        var startToken = Expect(TokenKind.Interpolate);
        var attrs = ParseAttributes();

        var parts = new List<InterpolatedStringPartNode>();

        // Parse parts until we hit the end tag
        while (!IsAtEnd && !Check(TokenKind.EndInterpolate))
        {
            if (Check(TokenKind.StrLiteral))
            {
                var strToken = Expect(TokenKind.StrLiteral);
                var text = strToken.Value as string ?? "";
                parts.Add(new InterpolatedStringTextNode(strToken.Span, text));
            }
            else if (Check(TokenKind.Expression))
            {
                Advance(); // consume §EXP
                var expr = ParseExpression();
                parts.Add(new InterpolatedStringExpressionNode(expr.Span, expr));
            }
            else
            {
                // Try to parse any other expression
                var expr = ParseExpression();
                parts.Add(new InterpolatedStringExpressionNode(expr.Span, expr));
            }
        }

        var endToken = Expect(TokenKind.EndInterpolate);
        var span = startToken.Span.Union(endToken.Span);
        return new InterpolatedStringNode(span, parts);
    }

    /// <summary>
    /// Parses a null-coalescing expression.
    /// §?? §REF[name=value] "default"
    /// </summary>
    private NullCoalesceNode ParseNullCoalesce()
    {
        var startToken = Expect(TokenKind.NullCoalesce);

        var left = ParseExpression();
        var right = ParseExpression();

        var span = startToken.Span.Union(right.Span);
        return new NullCoalesceNode(span, left, right);
    }

    /// <summary>
    /// Parses a null-conditional access expression.
    /// §?. §REF[name=person] Name
    /// </summary>
    private NullConditionalNode ParseNullConditional()
    {
        var startToken = Expect(TokenKind.NullConditional);

        var target = ParseExpression();

        // Parse the member name (identifier)
        var memberToken = Expect(TokenKind.Identifier);
        var memberName = memberToken.Text;

        var span = startToken.Span.Union(memberToken.Span);
        return new NullConditionalNode(span, target, memberName);
    }

    /// <summary>
    /// Parses a range expression.
    /// §RANGE 1 5
    /// §RANGE §^ 1   (open start to ^1)
    /// </summary>
    private RangeExpressionNode ParseRangeExpression()
    {
        var startToken = Expect(TokenKind.RangeOp);

        ExpressionNode? start = null;
        ExpressionNode? end = null;

        // Parse start if present
        if (IsExpressionStart())
        {
            start = ParseExpression();
        }

        // Parse end if present
        if (IsExpressionStart())
        {
            end = ParseExpression();
        }

        var span = end != null ? startToken.Span.Union(end.Span)
                 : start != null ? startToken.Span.Union(start.Span)
                 : startToken.Span;
        return new RangeExpressionNode(span, start, end);
    }

    /// <summary>
    /// Parses an index-from-end expression.
    /// §^ 1
    /// </summary>
    private IndexFromEndNode ParseIndexFromEnd()
    {
        var startToken = Expect(TokenKind.IndexEnd);

        var offset = ParseExpression();

        var span = startToken.Span.Union(offset.Span);
        return new IndexFromEndNode(span, offset);
    }

    // Phase 10: Advanced Patterns

    /// <summary>
    /// Parses a with-expression for non-destructive mutation.
    /// §WITH §REF[name=person]
    ///   §SET[Name] "New Name"
    /// §/WITH
    /// </summary>
    private WithExpressionNode ParseWithExpression()
    {
        var startToken = Expect(TokenKind.With);

        // Parse the target expression
        var target = ParseExpression();

        // Parse property assignments
        var assignments = new List<WithPropertyAssignmentNode>();
        while (!IsAtEnd && !Check(TokenKind.EndWith))
        {
            if (Check(TokenKind.Set))
            {
                var setToken = Expect(TokenKind.Set);
                var setAttrs = ParseAttributes();
                var propName = setAttrs["_pos0"] ?? "";
                var value = ParseExpression();
                var assignSpan = setToken.Span.Union(value.Span);
                assignments.Add(new WithPropertyAssignmentNode(assignSpan, propName, value));
            }
            else
            {
                // Skip unknown tokens
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndWith);
        var span = startToken.Span.Union(endToken.Span);
        return new WithExpressionNode(span, target, assignments);
    }

    /// <summary>
    /// Parses a positional pattern.
    /// §PPOS[Point] §VAR[x] §VAR[y]
    /// </summary>
    private PositionalPatternNode ParsePositionalPattern()
    {
        var startToken = Expect(TokenKind.PositionalPattern);
        var attrs = ParseAttributes();
        var typeName = attrs["_pos0"] ?? "";

        var patterns = new List<PatternNode>();
        while (!IsAtEnd && IsPatternStart())
        {
            patterns.Add(ParsePattern());
        }

        var span = patterns.Count > 0
            ? startToken.Span.Union(patterns[^1].Span)
            : startToken.Span;
        return new PositionalPatternNode(span, typeName, patterns);
    }

    /// <summary>
    /// Parses a property pattern.
    /// §PPROP[Person] §PMATCH[Age] §PREL[gte] 18
    /// </summary>
    private PropertyPatternNode ParsePropertyPattern()
    {
        var startToken = Expect(TokenKind.PropertyPattern);
        var attrs = ParseAttributes();
        var typeName = attrs["_pos0"];

        var matches = new List<PropertyMatchNode>();
        while (!IsAtEnd && Check(TokenKind.PropertyMatch))
        {
            var matchToken = Expect(TokenKind.PropertyMatch);
            var matchAttrs = ParseAttributes();
            var propName = matchAttrs["_pos0"] ?? "";
            var pattern = ParsePattern();
            var matchSpan = matchToken.Span.Union(pattern.Span);
            matches.Add(new PropertyMatchNode(matchSpan, propName, pattern));
        }

        var span = matches.Count > 0
            ? startToken.Span.Union(matches[^1].Span)
            : startToken.Span;
        return new PropertyPatternNode(span, typeName, matches);
    }

    /// <summary>
    /// Parses a relational pattern.
    /// §PREL[gte] 18
    /// </summary>
    private RelationalPatternNode ParseRelationalPattern()
    {
        var startToken = Expect(TokenKind.RelationalPattern);
        var attrs = ParseAttributes();
        var op = attrs["_pos0"] ?? "eq";

        var value = ParseExpression();

        var span = startToken.Span.Union(value.Span);
        return new RelationalPatternNode(span, op, value);
    }

    /// <summary>
    /// Parses a list pattern.
    /// §PLIST §VAR[first] §REST[rest]
    /// </summary>
    private ListPatternNode ParseListPattern()
    {
        var startToken = Expect(TokenKind.ListPattern);

        var patterns = new List<PatternNode>();
        VarPatternNode? slicePattern = null;

        while (!IsAtEnd && (IsPatternStart() || Check(TokenKind.Rest)))
        {
            if (Check(TokenKind.Rest))
            {
                var restToken = Expect(TokenKind.Rest);
                var restAttrs = ParseAttributes();
                var restName = restAttrs["_pos0"] ?? "_";
                slicePattern = new VarPatternNode(restToken.Span, restName);
            }
            else
            {
                patterns.Add(ParsePattern());
            }
        }

        var span = slicePattern != null
            ? startToken.Span.Union(slicePattern.Span)
            : patterns.Count > 0
                ? startToken.Span.Union(patterns[^1].Span)
                : startToken.Span;
        return new ListPatternNode(span, patterns, slicePattern);
    }

    /// <summary>
    /// Parses a var pattern.
    /// §VAR[x]
    /// </summary>
    private VarPatternNode ParseVarPattern()
    {
        var token = Expect(TokenKind.Var);
        var attrs = ParseAttributes();
        var name = attrs["_pos0"] ?? "_";
        return new VarPatternNode(token.Span, name);
    }

    /// <summary>
    /// Parses a constant pattern (literal value).
    /// </summary>
    private ConstantPatternNode ParseConstantPattern()
    {
        var expr = ParseExpression();
        return new ConstantPatternNode(expr.Span, expr);
    }

    /// <summary>
    /// Checks if the current token starts a pattern.
    /// </summary>
    private bool IsPatternStart()
    {
        return Current.Kind is TokenKind.PositionalPattern
            or TokenKind.PropertyPattern
            or TokenKind.ListPattern
            or TokenKind.Var
            or TokenKind.RelationalPattern
            || IsExpressionStart();
    }

    #region Extended Features Parsing

    /// <summary>
    /// Parses an inline example/test.
    /// §EX (Add 2 3) → 5
    /// §EX[ex001:msg:"edge case"] (Add 0 0) → 0
    /// </summary>
    private ExampleNode ParseExample()
    {
        var startToken = Expect(TokenKind.Example);
        var attrs = ParseAttributes();
        var (id, message) = AttributeHelper.InterpretExampleAttributes(attrs);

        var expression = ParseExpression();
        Expect(TokenKind.Arrow);
        var expected = ParseExpression();

        var span = startToken.Span.Union(expected.Span);
        return new ExampleNode(span, id, expression, expected, message, attrs);
    }

    /// <summary>
    /// Parses a TODO issue marker.
    /// §TODO[t001:perf:high] "Optimize for large n"
    /// </summary>
    private IssueNode ParseTodoIssue()
    {
        return ParseIssue(IssueKind.Todo, TokenKind.Todo);
    }

    /// <summary>
    /// Parses a FIXME issue marker.
    /// §FIXME[x001:bug:critical] "Integer overflow"
    /// </summary>
    private IssueNode ParseFixmeIssue()
    {
        return ParseIssue(IssueKind.Fixme, TokenKind.Fixme);
    }

    /// <summary>
    /// Parses a HACK issue marker.
    /// §HACK[h001] "Workaround for API bug"
    /// </summary>
    private IssueNode ParseHackIssue()
    {
        return ParseIssue(IssueKind.Hack, TokenKind.Hack);
    }

    private IssueNode ParseIssue(IssueKind kind, TokenKind tokenKind)
    {
        var startToken = Expect(tokenKind);
        var attrs = ParseAttributes();
        var (id, category, priority) = AttributeHelper.InterpretIssueAttributes(attrs);

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }
        else
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, kind.ToString().ToUpperInvariant(), "description");
        }

        return new IssueNode(startToken.Span, kind, id, category, priority, description, attrs);
    }

    /// <summary>
    /// Parses a USES dependency declaration.
    /// §USES[ValidateOrder, CalculateTotal, SaveOrder]
    /// </summary>
    private UsesNode ParseUses()
    {
        var startToken = Expect(TokenKind.Uses);
        var attrs = ParseAttributes();
        var targets = AttributeHelper.InterpretDependencyListAttributes(attrs);

        var dependencies = new List<DependencyNode>();
        foreach (var target in targets)
        {
            var isOptional = target.EndsWith('?');
            var actualTarget = isOptional ? target[..^1] : target;

            // Check for version constraint: target@1.0.0
            string? version = null;
            var atIndex = actualTarget.IndexOf('@');
            if (atIndex > 0)
            {
                version = actualTarget[(atIndex + 1)..];
                actualTarget = actualTarget[..atIndex];
            }

            dependencies.Add(new DependencyNode(startToken.Span, actualTarget, version, isOptional, new AttributeCollection()));
        }

        return new UsesNode(startToken.Span, dependencies, attrs);
    }

    /// <summary>
    /// Parses a USEDBY reverse dependency declaration.
    /// §USEDBY[OrderController.Submit, BatchProcessor.Run]
    /// </summary>
    private UsedByNode ParseUsedBy()
    {
        var startToken = Expect(TokenKind.UsedBy);
        var attrs = ParseAttributes();
        var targets = AttributeHelper.InterpretDependencyListAttributes(attrs);

        var dependents = new List<DependencyNode>();
        bool hasUnknownCallers = false;

        foreach (var target in targets)
        {
            if (target == "*" || target.Equals("external", StringComparison.OrdinalIgnoreCase))
            {
                hasUnknownCallers = true;
                continue;
            }

            var isOptional = target.EndsWith('?');
            var actualTarget = isOptional ? target[..^1] : target;

            dependents.Add(new DependencyNode(startToken.Span, actualTarget, null, isOptional, new AttributeCollection()));
        }

        return new UsedByNode(startToken.Span, dependents, hasUnknownCallers, attrs);
    }

    /// <summary>
    /// Parses an ASSUME assumption declaration.
    /// §ASSUME[env] "Database connection pool initialized"
    /// </summary>
    private AssumeNode ParseAssume()
    {
        var startToken = Expect(TokenKind.Assume);
        var attrs = ParseAttributes();
        var category = AttributeHelper.InterpretAssumeAttributes(attrs);

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }
        else
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "ASSUME", "description");
        }

        return new AssumeNode(startToken.Span, category, description, attrs);
    }

    /// <summary>
    /// Parses a COMPLEXITY performance contract.
    /// §COMPLEXITY[time:O(n)][space:O(1)]
    /// §COMPLEXITY[worst:time:O(n)][worst:space:O(n)]
    /// </summary>
    private ComplexityNode ParseComplexity()
    {
        var startToken = Expect(TokenKind.Complexity);
        var attrs = ParseAttributes();
        var (time, space, isWorstCase, custom) = AttributeHelper.InterpretComplexityAttributes(attrs);

        return new ComplexityNode(startToken.Span, time, space, isWorstCase, custom, attrs);
    }

    /// <summary>
    /// Parses a SINCE versioning marker.
    /// §SINCE[1.0.0]
    /// </summary>
    private SinceNode ParseSince()
    {
        var startToken = Expect(TokenKind.Since);
        var attrs = ParseAttributes();
        var version = AttributeHelper.InterpretSinceAttributes(attrs);

        if (string.IsNullOrEmpty(version))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "SINCE", "version");
            version = "0.0.0";
        }

        return new SinceNode(startToken.Span, version, attrs);
    }

    /// <summary>
    /// Parses a DEPRECATED marker.
    /// §DEPRECATED[since:1.5.0][use:NewMethod][reason:"Performance issues"]
    /// </summary>
    private DeprecatedNode ParseDeprecated()
    {
        var startToken = Expect(TokenKind.Deprecated);
        var attrs = ParseAttributes();
        var (since, replacement, reason, removedIn) = AttributeHelper.InterpretDeprecatedAttributes(attrs);

        if (string.IsNullOrEmpty(since))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DEPRECATED", "since");
            since = "0.0.0";
        }

        return new DeprecatedNode(startToken.Span, since, replacement, reason, removedIn, attrs);
    }

    /// <summary>
    /// Parses a BREAKING change marker.
    /// §BREAKING[1.7.0] "Added required 'options' parameter"
    /// </summary>
    private BreakingChangeNode ParseBreaking()
    {
        var startToken = Expect(TokenKind.Breaking);
        var attrs = ParseAttributes();
        var version = AttributeHelper.InterpretBreakingAttributes(attrs);

        if (string.IsNullOrEmpty(version))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "BREAKING", "version");
            version = "0.0.0";
        }

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }
        else
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "BREAKING", "description");
        }

        return new BreakingChangeNode(startToken.Span, version, description, attrs);
    }

    /// <summary>
    /// Parses a DECISION record.
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
    private DecisionNode ParseDecision()
    {
        var startToken = Expect(TokenKind.Decision);
        var attrs = ParseAttributes();
        var id = AttributeHelper.InterpretDecisionAttributes(attrs);

        if (string.IsNullOrEmpty(id))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "DECISION", "id");
            id = "unknown";
        }

        // Parse title (string literal)
        string title = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            title = (string)strToken.Value!;
        }

        string chosenOption = "";
        var chosenReasons = new List<string>();
        var rejectedOptions = new List<RejectedOptionNode>();
        string? context = null;
        DateOnly? date = null;
        string? author = null;

        // Parse decision content
        while (!IsAtEnd && !Check(TokenKind.EndDecision))
        {
            if (Check(TokenKind.Chosen))
            {
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    chosenOption = (string)strToken.Value!;
                }
            }
            else if (Check(TokenKind.Reason) && string.IsNullOrEmpty(chosenOption) == false && rejectedOptions.Count == 0)
            {
                // This REASON belongs to CHOSEN
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    chosenReasons.Add((string)strToken.Value!);
                }
            }
            else if (Check(TokenKind.Rejected))
            {
                rejectedOptions.Add(ParseRejectedOption());
            }
            else if (Check(TokenKind.Context))
            {
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    context = (string)strToken.Value!;
                }
            }
            else if (Check(TokenKind.DateMarker))
            {
                Advance();
                var dateAttrs = ParseAttributes();
                date = AttributeHelper.InterpretDateAttributes(dateAttrs);
                // Also check for bare date literal
                if (date == null && Check(TokenKind.Identifier))
                {
                    var dateStr = Advance().Text;
                    if (DateOnly.TryParse(dateStr, out var parsedDate))
                    {
                        date = parsedDate;
                    }
                }
            }
            else if (Check(TokenKind.AgentAuthor))
            {
                Advance();
                if (Check(TokenKind.StrLiteral))
                {
                    var strToken = Advance();
                    author = (string)strToken.Value!;
                }
            }
            else if (Check(TokenKind.Reason))
            {
                // Orphan REASON - skip
                Advance();
                if (Check(TokenKind.StrLiteral)) Advance();
            }
            else
            {
                // Unknown token - skip
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndDecision);
        var endAttrs = ParseAttributes();
        var endId = endAttrs["_pos0"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "DECISION", id, "END_DECISION", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new DecisionNode(span, id, title, chosenOption, chosenReasons, rejectedOptions, context, date, author, attrs);
    }

    private RejectedOptionNode ParseRejectedOption()
    {
        var startToken = Expect(TokenKind.Rejected);
        string name = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            name = (string)strToken.Value!;
        }

        var reasons = new List<string>();
        while (Check(TokenKind.Reason))
        {
            Advance();
            if (Check(TokenKind.StrLiteral))
            {
                var strToken = Advance();
                reasons.Add((string)strToken.Value!);
            }
        }

        return new RejectedOptionNode(startToken.Span, name, reasons, new AttributeCollection());
    }

    /// <summary>
    /// Parses a CONTEXT partial view marker.
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
    private ContextNode ParseContext()
    {
        var startToken = Expect(TokenKind.Context);
        var attrs = ParseAttributes();
        var isPartial = AttributeHelper.InterpretContextAttributes(attrs);

        var visibleFiles = new List<FileRefNode>();
        var hiddenFiles = new List<FileRefNode>();
        string? focusTarget = null;

        while (!IsAtEnd && !Check(TokenKind.EndContext))
        {
            if (Check(TokenKind.Visible))
            {
                Advance();
                while (!IsAtEnd && !Check(TokenKind.EndVisible))
                {
                    if (Check(TokenKind.FileRef))
                    {
                        visibleFiles.Add(ParseFileRef());
                    }
                    else
                    {
                        Advance();
                    }
                }
                if (Check(TokenKind.EndVisible)) Advance();
            }
            else if (Check(TokenKind.HiddenSection))
            {
                Advance();
                while (!IsAtEnd && !Check(TokenKind.EndHidden))
                {
                    if (Check(TokenKind.FileRef))
                    {
                        hiddenFiles.Add(ParseFileRef());
                    }
                    else
                    {
                        Advance();
                    }
                }
                if (Check(TokenKind.EndHidden)) Advance();
            }
            else if (Check(TokenKind.Focus))
            {
                Advance();
                var focusAttrs = ParseAttributes();
                focusTarget = AttributeHelper.InterpretFocusAttributes(focusAttrs);
            }
            else if (Check(TokenKind.FileRef))
            {
                // Standalone FILE goes to visible by default
                visibleFiles.Add(ParseFileRef());
            }
            else
            {
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndContext);
        var span = startToken.Span.Union(endToken.Span);
        return new ContextNode(span, isPartial, visibleFiles, hiddenFiles, focusTarget, attrs);
    }

    private FileRefNode ParseFileRef()
    {
        var startToken = Expect(TokenKind.FileRef);
        var attrs = ParseAttributes();
        var filePath = AttributeHelper.InterpretFileRefAttributes(attrs);

        string? description = null;
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }

        return new FileRefNode(startToken.Span, filePath, description, attrs);
    }

    /// <summary>
    /// Parses a PROPERTY (property-based testing) declaration.
    /// §PROPERTY ∀arr: (== (Reverse (Reverse arr)) arr)
    /// </summary>
    private PropertyTestNode ParsePropertyTest()
    {
        var startToken = Expect(TokenKind.PropertyTest);
        var attrs = ParseAttributes();

        var quantifiers = new List<string>();

        // Check for universal quantifier ∀ or "forall" keyword
        if (Check(TokenKind.Identifier))
        {
            var quantifierText = Current.Text;
            if (quantifierText.StartsWith("∀") || quantifierText.Equals("forall", StringComparison.OrdinalIgnoreCase))
            {
                Advance();
                // Parse variable names: ∀x,y,z:
                if (quantifierText.StartsWith("∀") && quantifierText.Length > 1)
                {
                    // ∀x format - variable is part of the token
                    var varPart = quantifierText[1..];
                    if (varPart.EndsWith(':'))
                        varPart = varPart[..^1];
                    foreach (var v in varPart.Split(','))
                    {
                        if (!string.IsNullOrWhiteSpace(v))
                            quantifiers.Add(v.Trim());
                    }
                }
                else
                {
                    // Separate variable list
                    while (Check(TokenKind.Identifier))
                    {
                        var varName = Advance().Text;
                        if (varName.EndsWith(':'))
                            varName = varName[..^1];
                        foreach (var v in varName.Split(','))
                        {
                            if (!string.IsNullOrWhiteSpace(v))
                                quantifiers.Add(v.Trim());
                        }
                        // Skip colon if present
                        if (Check(TokenKind.Colon))
                        {
                            Advance();
                            break;
                        }
                    }
                }
            }
        }

        var predicate = ParseExpression();
        var span = startToken.Span.Union(predicate.Span);
        return new PropertyTestNode(span, quantifiers, predicate, attrs);
    }

    /// <summary>
    /// Parses a LOCK multi-agent locking declaration.
    /// §LOCK[agent:agent-123][expires:2024-01-15T11:00:00Z]
    /// </summary>
    private LockNode ParseLock()
    {
        var startToken = Expect(TokenKind.Lock);
        var attrs = ParseAttributes();
        var (agentId, acquired, expires) = AttributeHelper.InterpretLockAttributes(attrs);

        if (string.IsNullOrEmpty(agentId))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "LOCK", "agent");
            agentId = "unknown";
        }

        return new LockNode(startToken.Span, agentId, acquired, expires, attrs);
    }

    /// <summary>
    /// Parses an AUTHOR agent authorship tracking declaration.
    /// §AUTHOR[agent:agent-456][date:2024-01-10][task:PROJ-789]
    /// </summary>
    private AuthorNode ParseAuthor()
    {
        var startToken = Expect(TokenKind.AgentAuthor);
        var attrs = ParseAttributes();
        var (agentId, date, taskId) = AttributeHelper.InterpretAuthorAttributes(attrs);

        if (string.IsNullOrEmpty(agentId))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "AUTHOR", "agent");
            agentId = "unknown";
        }

        return new AuthorNode(startToken.Span, agentId, date, taskId, attrs);
    }

    /// <summary>
    /// Parses a TASK reference.
    /// §TASK[PROJ-789] "Implement validation"
    /// </summary>
    private TaskRefNode ParseTaskRef()
    {
        var startToken = Expect(TokenKind.TaskRef);
        var attrs = ParseAttributes();
        var taskId = AttributeHelper.InterpretTaskRefAttributes(attrs);

        if (string.IsNullOrEmpty(taskId))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "TASK", "id");
            taskId = "unknown";
        }

        // Expect a string description
        string description = "";
        if (Check(TokenKind.StrLiteral))
        {
            var strToken = Advance();
            description = (string)strToken.Value!;
        }

        return new TaskRefNode(startToken.Span, taskId, description, attrs);
    }

    /// <summary>
    /// Checks if the current token starts an extended metadata declaration.
    /// </summary>
    private bool IsExtendedMetadataStart()
    {
        return Current.Kind is TokenKind.Example
            or TokenKind.Todo
            or TokenKind.Fixme
            or TokenKind.Hack
            or TokenKind.Uses
            or TokenKind.UsedBy
            or TokenKind.Assume
            or TokenKind.Complexity
            or TokenKind.Since
            or TokenKind.Deprecated
            or TokenKind.Breaking
            or TokenKind.PropertyTest
            or TokenKind.Lock
            or TokenKind.AgentAuthor
            or TokenKind.TaskRef;
    }

    /// <summary>
    /// Checks if the current token starts a module-level extended metadata declaration.
    /// </summary>
    private bool IsModuleLevelExtendedMetadataStart()
    {
        return Current.Kind is TokenKind.Todo
            or TokenKind.Fixme
            or TokenKind.Hack
            or TokenKind.Assume
            or TokenKind.Invariant
            or TokenKind.Decision
            or TokenKind.Context;
    }

    #endregion
}
