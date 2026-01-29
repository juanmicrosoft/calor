using Opal.Compiler.Ast;
using Opal.Compiler.Diagnostics;

namespace Opal.Compiler.Parsing;

/// <summary>
/// Recursive descent parser for OPAL source code.
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

        // Support both v1 [id=x][name=y] and v2 [x:y] formats
        var (id, moduleName) = V2AttributeHelper.InterpretModuleAttributes(attrs);
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
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "USING, IFACE, CLASS, FUNC, or END_MODULE", Current.Kind);
                Advance();
            }
        }

        var endToken = Expect(TokenKind.EndModule);
        var endAttrs = ParseAttributes();
        var endId = V2AttributeHelper.InterpretEndModuleAttributes(endAttrs);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "MODULE", id, "END_MODULE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ModuleNode(span, id, moduleName, usings, interfaces, classes, functions, attrs);
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
        // v2 positional formats:
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

        // Support both v1 and v2 formats
        var (id, funcName, visibilityStr) = V2AttributeHelper.InterpretFuncAttributes(attrs);
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
            else
            {
                break;
            }
        }

        // Parse BODY - either explicit (v1) or implicit (v2)
        if (Check(TokenKind.Body))
        {
            // v1: Explicit §BODY ... §END_BODY
            body = ParseBody();
        }
        else if (!Check(TokenKind.EndFunc))
        {
            // v2: Implicit body - parse statements until §/F or §END_FUNC
            body = ParseImplicitBody();
        }

        var endToken = Expect(TokenKind.EndFunc);
        var endAttrs = ParseAttributes();
        var endId = V2AttributeHelper.InterpretEndFuncAttributes(endAttrs);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "FUNC", id, "END_FUNC", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new FunctionNode(span, id, funcName, visibility, typeParameters, parameters, output, effects,
            preconditions, postconditions, body, attrs);
    }

    private ParameterNode ParseParameter()
    {
        var startToken = Expect(TokenKind.In);
        var attrs = ParseAttributes();

        // Support both v1 and v2 formats
        var (typeName, paramName, semantic) = V2AttributeHelper.InterpretInputAttributes(attrs);
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

        // Support both v1 and v2 formats
        var typeName = V2AttributeHelper.InterpretOutputAttributes(attrs);
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

        // Support both v1 and v2 formats
        var effects = V2AttributeHelper.InterpretEffectsAttributes(attrs);

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
    /// Parses an implicit body (v2 syntax) - statements until END_FUNC/§/F
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
        // v2: Print aliases
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

        // Use V2AttributeHelper which handles both v1 [target=...] and v2 [target] formats
        var (target, fallible) = V2AttributeHelper.InterpretCallAttributes(attrs);
        if (string.IsNullOrEmpty(target))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "CALL", "target");
        }

        var arguments = new List<ExpressionNode>();

        // v2: Support implicit closing - if a single expression follows without §A, treat it as the argument
        // §C[Console.WriteLine] "Hello" is equivalent to §C[Console.WriteLine] §A "Hello" §/C
        if (IsExpressionStart() && !Check(TokenKind.Arg))
        {
            // Single expression argument without §A prefix - implicit closing
            arguments.Add(ParseExpression());
            var span = startToken.Span.Union(arguments[0].Span);
            return new CallStatementNode(span, target, fallible, arguments, attrs);
        }

        // Standard v1 format with explicit §A and §/C
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
            or TokenKind.Op
            or TokenKind.Ref
            // v2 Lisp-style expression
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
            TokenKind.Op => ParseBinaryOperation(),
            TokenKind.Ref => ParseRefExpression(),
            // v2 Lisp-style expression: (op args...)
            TokenKind.OpenParen => ParseLispExpression(),
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
    /// </summary>
    private ExpressionNode ParseLispArgument()
    {
        return Current.Kind switch
        {
            TokenKind.IntLiteral => ParseIntLiteral(),
            TokenKind.StrLiteral => ParseStringLiteral(),
            TokenKind.BoolLiteral => ParseBoolLiteral(),
            TokenKind.FloatLiteral => ParseFloatLiteral(),
            TokenKind.Identifier => ParseBareReference(), // Bare variable reference
            TokenKind.OpenParen => ParseLispExpression(), // Nested expression
            // Also support existing OPAL expressions inside Lisp
            TokenKind.Ref => ParseRefExpression(),
            _ => throw new InvalidOperationException($"Unexpected token {Current.Kind} in Lisp expression argument")
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

    private ReferenceNode ParseReference()
    {
        var token = Expect(TokenKind.Identifier);
        return new ReferenceNode(token.Span, token.Text);
    }

    private ReferenceNode ParseRefExpression()
    {
        var startToken = Expect(TokenKind.Ref);
        var attrs = ParseAttributes();

        var name = GetRequiredAttribute(attrs, "name", "REF", startToken.Span);
        return new ReferenceNode(startToken.Span, name);
    }

    private BinaryOperationNode ParseBinaryOperation()
    {
        var startToken = Expect(TokenKind.Op);
        var attrs = ParseAttributes();

        var kindStr = GetRequiredAttribute(attrs, "kind", "OP", startToken.Span);
        var op = BinaryOperatorExtensions.FromString(kindStr);

        if (op == null)
        {
            _diagnostics.ReportError(startToken.Span, DiagnosticCode.UnexpectedToken,
                $"Unknown binary operator '{kindStr}'");
            op = BinaryOperator.Add; // Default to avoid null
        }

        var left = ParseExpression();
        var right = ParseExpression();

        var span = startToken.Span.Union(right.Span);
        return new BinaryOperationNode(span, op.Value, left, right);
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
        var typeName = attrs["type"];
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
        var id = GetRequiredAttribute(attrs, "id", "MATCH", startToken.Span);

        var target = ParseExpression();
        var cases = ParseMatchCases();

        var endToken = Expect(TokenKind.EndMatch);
        var endAttrs = ParseAttributes();
        var endId = GetRequiredAttribute(endAttrs, "id", "END_MATCH", endToken.Span);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "MATCH", id, "END_MATCH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MatchExpressionNode(span, id, target, cases, attrs);
    }

    private MatchStatementNode ParseMatchStatement()
    {
        var startToken = Expect(TokenKind.Match);
        var attrs = ParseAttributes();
        var id = GetRequiredAttribute(attrs, "id", "MATCH", startToken.Span);

        var target = ParseExpression();
        var cases = ParseMatchCases();

        var endToken = Expect(TokenKind.EndMatch);
        var endAttrs = ParseAttributes();
        var endId = GetRequiredAttribute(endAttrs, "id", "END_MATCH", endToken.Span);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "MATCH", id, "END_MATCH", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new MatchStatementNode(span, id, target, cases, attrs);
    }

    private List<MatchCaseNode> ParseMatchCases()
    {
        var cases = new List<MatchCaseNode>();

        while (Check(TokenKind.Case))
        {
            var caseToken = Expect(TokenKind.Case);
            var pattern = ParsePattern();

            ExpressionNode? guard = null;
            // Guard could be indicated by an attribute or following expression

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
        if (Check(TokenKind.Identifier))
        {
            var token = Advance();
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

        // Use V2AttributeHelper which handles both v1 and v2 positional [id:var:from:to:step] formats
        var (id, varName, fromStr, toStr, stepStr) = V2AttributeHelper.InterpretForAttributes(attrs);
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
        // v2 positional: [id]
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

        var id = GetRequiredAttribute(attrs, "id", "WHILE", startToken.Span);

        // Parse condition expression
        var condition = ParseExpression();

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndWhile);

        var endToken = Expect(TokenKind.EndWhile);
        var endAttrs = ParseAttributes();
        var endId = GetRequiredAttribute(endAttrs, "id", "END_WHILE", endToken.Span);

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "WHILE", id, "END_WHILE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new WhileStatementNode(span, id, condition, body, attrs);
    }

    private IfStatementNode ParseIfStatement()
    {
        var startToken = Expect(TokenKind.If);
        var attrs = ParseAttributes();

        // Use V2AttributeHelper which handles both v1 [id=...] and v2 [id] formats
        var id = V2AttributeHelper.InterpretIfAttributes(attrs);
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

        // v2: Check for arrow syntax: §IF[id] condition → statement
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
        // v2 positional: [id]
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

        // Use V2AttributeHelper which handles both v1 [name=...] and v2 [name] or [~name] formats
        var (name, isMutable) = V2AttributeHelper.InterpretBindAttributes(attrs);
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

        while (Match(TokenKind.OpenBracket))
        {
            // Check for v2 positional format vs v1 named format
            // v1: [name=value] - has Identifier followed by Equals
            // v2: [value1:value2] or [value] - no Equals, colon-separated

            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Equals)
            {
                // v1 format: [name=value]
                ParseV1Attribute(attrs);
            }
            else
            {
                // v2 format: [value1:value2:...] or [value]
                ParseV2PositionalAttributes(attrs);
            }

            Expect(TokenKind.CloseBracket);
        }

        return attrs;
    }

    private void ParseV1Attribute(AttributeCollection attrs)
    {
        var nameToken = Advance(); // consume identifier
        Expect(TokenKind.Equals);

        string value;
        if (Check(TokenKind.Identifier))
        {
            value = Advance().Text;
        }
        else if (Check(TokenKind.StrLiteral))
        {
            value = Advance().Value as string ?? "";
        }
        else if (Check(TokenKind.IntLiteral))
        {
            value = Advance().Value?.ToString() ?? "";
        }
        else if (Check(TokenKind.BoolLiteral))
        {
            value = Advance().Value?.ToString()?.ToLowerInvariant() ?? "";
        }
        else
        {
            _diagnostics.ReportUnexpectedToken(Current.Span, "attribute value", Current.Kind);
            value = "";
        }

        attrs.Add(nameToken.Text, value);
    }

    private void ParseV2PositionalAttributes(AttributeCollection attrs)
    {
        // Parse colon-separated values: [value1:value2:value3]
        // Store them as _pos0, _pos1, _pos2, etc. for later interpretation
        var values = new List<string>();
        var position = 0;

        do
        {
            var value = ParseV2Value();
            values.Add(value);
            attrs.Add($"_pos{position}", value);
            position++;
        }
        while (MatchColon());

        // Also store the raw positional count
        attrs.Add("_posCount", position.ToString());
    }

    private string ParseV2Value()
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

        // Parse the main value (identifier, literal, or compound identifier)
        if (Check(TokenKind.Identifier))
        {
            sb.Append(Advance().Text);

            // Handle compound identifiers like Console.WriteLine
            while (Current.Text == ".")
            {
                sb.Append(Advance().Text); // consume '.'
                if (Check(TokenKind.Identifier))
                {
                    sb.Append(Advance().Text);
                }
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
            "public" => Visibility.Public,
            "internal" => Visibility.Internal,
            "private" => Visibility.Private,
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

        // v2 positional: [id:type:size?] or just [id:type] for initialized arrays
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

        // v2 positional: [id:variable:type]
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

        // v2 positional: [name]
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

        // v2 positional: [typeParam:constraint]
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

        // Parse the constraint
        var (kind, typeName) = constraintStr.ToLowerInvariant() switch
        {
            "class" => (TypeConstraintKind.Class, (string?)null),
            "struct" => (TypeConstraintKind.Struct, (string?)null),
            "new" => (TypeConstraintKind.New, (string?)null),
            _ => (TypeConstraintKind.TypeName, constraintStr)
        };

        var constraint = new TypeConstraintNode(startToken.Span, kind, typeName);

        // Replace the type parameter with one that includes the new constraint
        var oldTypeParam = typeParameters[typeParamIndex];
        var newConstraints = oldTypeParam.Constraints.ToList();
        newConstraints.Add(constraint);
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

        // v2 positional: [typeName:typeArg1:typeArg2:...]
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

        // v2 positional: [id:name]
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
        return new InterfaceDefinitionNode(span, id, name, baseInterfaces, methods, attrs);
    }

    /// <summary>
    /// Parses a method signature (for interfaces).
    /// §METHOD[m001:Area] §O[f64] §E[] §/METHOD[m001]
    /// </summary>
    private MethodSignatureNode ParseMethodSignature()
    {
        var startToken = Expect(TokenKind.Method);
        var attrs = ParseAttributes();

        // v2 positional: [id:name]
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
        return new MethodSignatureNode(span, id, name, typeParameters, parameters, output, effects, attrs);
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

        // v2 positional: [id:name:modifiers?]
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
            implementedInterfaces, typeParameters, fields, properties, constructors, methods, attrs);
    }

    /// <summary>
    /// Parses a class field.
    /// §FLD[string:Name:pri]
    /// </summary>
    private ClassFieldNode ParseClassField()
    {
        var startToken = Expect(TokenKind.FieldDef);
        var attrs = ParseAttributes();

        // v2 positional: [type:name:visibility?]
        var typeName = attrs["_pos0"] ?? "object";
        var name = attrs["_pos1"] ?? "";
        var visStr = attrs["_pos2"] ?? "private";

        if (string.IsNullOrEmpty(name))
        {
            _diagnostics.ReportMissingRequiredAttribute(startToken.Span, "FLD", "name");
        }

        var visibility = ParseVisibility(visStr);

        // Check for optional default value
        ExpressionNode? defaultValue = null;
        if (IsExpressionStart())
        {
            defaultValue = ParseExpression();
        }

        var span = defaultValue != null ? startToken.Span.Union(defaultValue.Span) : startToken.Span;
        return new ClassFieldNode(span, name, typeName, visibility, defaultValue, attrs);
    }

    /// <summary>
    /// Parses a method definition (with body).
    /// §METHOD[m001:Area:pub:over]
    ///   §O[f64] §E[]
    ///   §R §OP[kind=mul] 3.14159 §REF[name=Radius] §REF[name=Radius]
    /// §/METHOD[m001]
    /// </summary>
    private MethodNode ParseMethodDefinition()
    {
        var startToken = Expect(TokenKind.Method);
        var attrs = ParseAttributes();

        // v2 positional: [id:name:visibility?:modifiers?]
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
            output, effects, preconditions, postconditions, body, attrs);
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

        // v2 positional: [typeName:typeArg1:typeArg2:...]
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
    /// Parses a 'this' expression.
    /// §THIS
    /// </summary>
    private ThisExpressionNode ParseThisExpression()
    {
        var token = Expect(TokenKind.This);
        return new ThisExpressionNode(token.Span);
    }

    /// <summary>
    /// Parses a 'base' expression.
    /// §BASE
    /// </summary>
    private BaseExpressionNode ParseBaseExpression()
    {
        var token = Expect(TokenKind.Base);
        return new BaseExpressionNode(token.Span);
    }

    // Phase 9: Properties and Constructors

    /// <summary>
    /// Parses a property definition.
    /// §PROP[p001:Name:string:pub]
    ///   §GET
    ///   §SET[pri]
    /// §/PROP[p001]
    /// </summary>
    private PropertyNode ParseProperty()
    {
        var startToken = Expect(TokenKind.Property);
        var attrs = ParseAttributes();

        // v2 positional: [id:name:type:visibility?]
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
            else if (IsExpressionStart())
            {
                // Default value
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
        return new PropertyNode(span, id, name, typeName, visibility, getter, setter, initer, defaultValue, attrs);
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

        // Parse optional preconditions and body (for non-auto properties)
        while (!IsAtEnd && !Check(TokenKind.Get) && !Check(TokenKind.Set) &&
               !Check(TokenKind.Init) && !Check(TokenKind.EndProperty))
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

        // v2 positional: [id:visibility?]
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
        return new ConstructorNode(span, id, visibility, parameters, preconditions, initializer, body, attrs);
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

        // v2 positional: [id]
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

        // v2 positional: [exceptionType:variableName?] or empty for catch-all
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

    // Phase 11: Lambdas, Delegates, Events

    /// <summary>
    /// Parses a lambda expression.
    /// §LAM[lam1:x:i32] §OP[kind=mul] §REF[name=x] 2 §/LAM[lam1]
    /// </summary>
    private LambdaExpressionNode ParseLambdaExpression()
    {
        var startToken = Expect(TokenKind.Lambda);
        var attrs = ParseAttributes();

        // v2 positional: [id:param1:type1:param2:type2:...] or [id:async:param1:type1:...]
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
}
