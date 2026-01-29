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

        var functions = new List<FunctionNode>();

        while (!IsAtEnd && !Check(TokenKind.EndModule))
        {
            if (Check(TokenKind.Func))
            {
                functions.Add(ParseFunction());
            }
            else
            {
                _diagnostics.ReportUnexpectedToken(Current.Span, "FUNC or END_MODULE", Current.Kind);
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
        return new ModuleNode(span, id, moduleName, functions, attrs);
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

        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
        var preconditions = new List<RequiresNode>();
        var postconditions = new List<EnsuresNode>();
        var body = new List<StatementNode>();

        // Parse optional sections before BODY
        while (!IsAtEnd && !Check(TokenKind.Body) && !Check(TokenKind.EndFunc))
        {
            if (Check(TokenKind.In))
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
        return new FunctionNode(span, id, funcName, visibility, parameters, output, effects,
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

        _diagnostics.ReportUnexpectedToken(Current.Span, "statement", Current.Kind);
        Advance();
        return null;
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
        var span = startToken.Span.Union(endToken.Span);

        return new CallStatementNode(span, target, fallible, arguments, attrs);
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
            // Phase 3: Type System
            or TokenKind.Some
            or TokenKind.None
            or TokenKind.Ok
            or TokenKind.Err
            or TokenKind.Match
            or TokenKind.Record;
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
            // Phase 3: Type System
            TokenKind.Some => ParseSomeExpression(),
            TokenKind.None => ParseNoneExpression(),
            TokenKind.Ok => ParseOkExpression(),
            TokenKind.Err => ParseErrExpression(),
            TokenKind.Match => ParseMatchExpression(),
            TokenKind.Record => ParseRecordCreation(),
            _ => throw new InvalidOperationException($"Unexpected token {Current.Kind}")
        };
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

        var endToken = Expect(TokenKind.EndIf);
        var endAttrs = ParseAttributes();
        // v2 positional: [id]
        var endId = endAttrs["_pos0"] ?? endAttrs["id"] ?? "";

        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "IF", id, "END_IF", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new IfStatementNode(span, id, condition, thenBody, elseIfClauses, elseBody, attrs);
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
}
