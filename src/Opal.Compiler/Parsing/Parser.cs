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
            or TokenKind.Identifier;
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
