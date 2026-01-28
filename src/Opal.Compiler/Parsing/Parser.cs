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

        var id = GetRequiredAttribute(attrs, "id", "MODULE", startToken.Span);
        var name = GetRequiredAttribute(attrs, "name", "MODULE", startToken.Span);

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
        var endId = GetRequiredAttribute(endAttrs, "id", "END_MODULE", endToken.Span);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "MODULE", id, "END_MODULE", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new ModuleNode(span, id, name, functions, attrs);
    }

    private FunctionNode ParseFunction()
    {
        var startToken = Expect(TokenKind.Func);
        var attrs = ParseAttributes();

        var id = GetRequiredAttribute(attrs, "id", "FUNC", startToken.Span);
        var name = GetRequiredAttribute(attrs, "name", "FUNC", startToken.Span);
        var visibility = ParseVisibility(attrs["visibility"]);

        var parameters = new List<ParameterNode>();
        OutputNode? output = null;
        EffectsNode? effects = null;
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
            else
            {
                break;
            }
        }

        // Parse BODY
        if (Check(TokenKind.Body))
        {
            body = ParseBody();
        }

        var endToken = Expect(TokenKind.EndFunc);
        var endAttrs = ParseAttributes();
        var endId = GetRequiredAttribute(endAttrs, "id", "END_FUNC", endToken.Span);

        // Validate ID matching
        if (endId != id)
        {
            _diagnostics.ReportMismatchedId(endToken.Span, "FUNC", id, "END_FUNC", endId);
        }

        var span = startToken.Span.Union(endToken.Span);
        return new FunctionNode(span, id, name, visibility, parameters, output, effects, body, attrs);
    }

    private ParameterNode ParseParameter()
    {
        var startToken = Expect(TokenKind.In);
        var attrs = ParseAttributes();

        var name = GetRequiredAttribute(attrs, "name", "IN", startToken.Span);
        var typeName = GetRequiredAttribute(attrs, "type", "IN", startToken.Span);

        return new ParameterNode(startToken.Span, name, typeName, attrs);
    }

    private OutputNode ParseOutput()
    {
        var startToken = Expect(TokenKind.Out);
        var attrs = ParseAttributes();

        var typeName = GetRequiredAttribute(attrs, "type", "OUT", startToken.Span);

        return new OutputNode(startToken.Span, typeName);
    }

    private EffectsNode ParseEffects()
    {
        var startToken = Expect(TokenKind.Effects);
        var attrs = ParseAttributes();

        var effects = new Dictionary<string, string>();
        foreach (var kvp in attrs.All())
        {
            effects[kvp.Key] = kvp.Value;
        }

        return new EffectsNode(startToken.Span, effects);
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

        var target = GetRequiredAttribute(attrs, "target", "CALL", startToken.Span);
        var fallibleStr = attrs["fallible"];
        var fallible = fallibleStr?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

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

        var id = GetRequiredAttribute(attrs, "id", "FOR", startToken.Span);
        var varName = GetRequiredAttribute(attrs, "var", "FOR", startToken.Span);

        // Parse from/to/step - these can be expressions or literal values in attributes
        ExpressionNode from;
        ExpressionNode to;
        ExpressionNode? step = null;

        // Check if from/to are in attributes (simple case) or need to be parsed as expressions
        var fromAttr = attrs["from"];
        var toAttr = attrs["to"];
        var stepAttr = attrs["step"];

        if (fromAttr != null && int.TryParse(fromAttr, out var fromVal))
        {
            from = new IntLiteralNode(startToken.Span, fromVal);
        }
        else
        {
            from = ParseExpression();
        }

        if (toAttr != null && int.TryParse(toAttr, out var toVal))
        {
            to = new IntLiteralNode(startToken.Span, toVal);
        }
        else
        {
            to = ParseExpression();
        }

        if (stepAttr != null && int.TryParse(stepAttr, out var stepVal))
        {
            step = new IntLiteralNode(startToken.Span, stepVal);
        }

        // Parse body statements
        var body = ParseStatementBlock(TokenKind.EndFor);

        var endToken = Expect(TokenKind.EndFor);
        var endAttrs = ParseAttributes();
        var endId = GetRequiredAttribute(endAttrs, "id", "END_FOR", endToken.Span);

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

        var id = GetRequiredAttribute(attrs, "id", "IF", startToken.Span);

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
        var endId = GetRequiredAttribute(endAttrs, "id", "END_IF", endToken.Span);

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

        var name = GetRequiredAttribute(attrs, "name", "BIND", startToken.Span);
        var typeName = attrs["type"];
        var mutableStr = attrs["mutable"];
        var isMutable = mutableStr?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

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
            var nameToken = Expect(TokenKind.Identifier);
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

            Expect(TokenKind.CloseBracket);
            attrs.Add(nameToken.Text, value);
        }

        return attrs;
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
