using Opal.Compiler.Diagnostics;

namespace Opal.Compiler.Parsing;

/// <summary>
/// Tokenizes OPAL source code.
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private readonly DiagnosticBag _diagnostics;

    private int _position;
    private int _line = 1;
    private int _column = 1;
    private int _tokenStart;
    private int _tokenLine;
    private int _tokenColumn;

    private static readonly Dictionary<string, TokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["MODULE"] = TokenKind.Module,
        ["END_MODULE"] = TokenKind.EndModule,
        ["FUNC"] = TokenKind.Func,
        ["END_FUNC"] = TokenKind.EndFunc,
        ["IN"] = TokenKind.In,
        ["OUT"] = TokenKind.Out,
        ["EFFECTS"] = TokenKind.Effects,
        ["BODY"] = TokenKind.Body,
        ["END_BODY"] = TokenKind.EndBody,
        ["CALL"] = TokenKind.Call,
        ["END_CALL"] = TokenKind.EndCall,
        ["ARG"] = TokenKind.Arg,
        ["RETURN"] = TokenKind.Return,
        // Phase 2: Control Flow
        ["FOR"] = TokenKind.For,
        ["END_FOR"] = TokenKind.EndFor,
        ["IF"] = TokenKind.If,
        ["END_IF"] = TokenKind.EndIf,
        ["ELSE"] = TokenKind.Else,
        ["ELSEIF"] = TokenKind.ElseIf,
        ["WHILE"] = TokenKind.While,
        ["END_WHILE"] = TokenKind.EndWhile,
        ["BIND"] = TokenKind.Bind,
        ["OP"] = TokenKind.Op,
        ["REF"] = TokenKind.Ref,
        // Phase 3: Type System
        ["TYPE"] = TokenKind.Type,
        ["END_TYPE"] = TokenKind.EndType,
        ["RECORD"] = TokenKind.Record,
        ["END_RECORD"] = TokenKind.EndRecord,
        ["FIELD"] = TokenKind.Field,
        ["MATCH"] = TokenKind.Match,
        ["END_MATCH"] = TokenKind.EndMatch,
        ["CASE"] = TokenKind.Case,
        ["SOME"] = TokenKind.Some,
        ["NONE"] = TokenKind.None,
        ["OK"] = TokenKind.Ok,
        ["ERR"] = TokenKind.Err,
        ["VARIANT"] = TokenKind.Variant,
    };

    public Lexer(string source, DiagnosticBag diagnostics)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    private char Current => Peek(0);
    private char Lookahead => Peek(1);

    private char Peek(int offset)
    {
        var index = _position + offset;
        return index < _source.Length ? _source[index] : '\0';
    }

    private bool IsAtEnd => _position >= _source.Length;

    private void Advance()
    {
        if (!IsAtEnd)
        {
            if (Current == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _position++;
        }
    }

    private void StartToken()
    {
        _tokenStart = _position;
        _tokenLine = _line;
        _tokenColumn = _column;
    }

    private TextSpan CurrentSpan()
        => new(_tokenStart, _position - _tokenStart, _tokenLine, _tokenColumn);

    private string CurrentText()
        => _source[_tokenStart.._position];

    private Token MakeToken(TokenKind kind, object? value = null)
        => new(kind, CurrentText(), CurrentSpan(), value);

    public IEnumerable<Token> Tokenize()
    {
        while (!IsAtEnd)
        {
            var token = NextToken();
            if (token.Kind != TokenKind.Whitespace && token.Kind != TokenKind.Newline)
            {
                yield return token;
            }
        }

        StartToken();
        yield return MakeToken(TokenKind.Eof);
    }

    public List<Token> TokenizeAll()
        => Tokenize().ToList();

    private Token NextToken()
    {
        StartToken();

        return Current switch
        {
            '\0' => MakeToken(TokenKind.Eof),
            '§' => ScanSectionMarker(),
            '[' => ScanSingle(TokenKind.OpenBracket),
            ']' => ScanSingle(TokenKind.CloseBracket),
            '=' => ScanSingle(TokenKind.Equals),
            '"' => ScanStringLiteral(),
            '\r' or '\n' => ScanNewline(),
            ' ' or '\t' => ScanWhitespace(),
            _ when char.IsLetter(Current) || Current == '_' => ScanIdentifierOrTypedLiteral(),
            _ when char.IsDigit(Current) || (Current == '-' && char.IsDigit(Lookahead)) => ScanNumber(),
            _ => ScanError()
        };
    }

    private Token ScanSingle(TokenKind kind)
    {
        Advance();
        return MakeToken(kind);
    }

    private Token ScanSectionMarker()
    {
        Advance(); // consume §

        // Read the keyword that follows
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            Advance();
        }

        var text = CurrentText();
        var keyword = text.Length > 1 ? text[1..] : "";

        if (Keywords.TryGetValue(keyword, out var kind))
        {
            return MakeToken(kind);
        }

        // Unknown section keyword - report error but return as identifier
        _diagnostics.ReportUnexpectedCharacter(CurrentSpan(), '§');
        return MakeToken(TokenKind.Error);
    }

    private Token ScanIdentifierOrTypedLiteral()
    {
        while (char.IsLetterOrDigit(Current) || Current == '_' || Current == '.')
        {
            Advance();
        }

        var text = CurrentText();

        // Check for typed literals
        if (Current == ':')
        {
            return text.ToUpperInvariant() switch
            {
                "INT" => ScanTypedIntLiteral(),
                "STR" => ScanTypedStringLiteral(),
                "BOOL" => ScanTypedBoolLiteral(),
                "FLOAT" => ScanTypedFloatLiteral(),
                _ => MakeToken(TokenKind.Identifier)
            };
        }

        return MakeToken(TokenKind.Identifier);
    }

    private Token ScanTypedIntLiteral()
    {
        Advance(); // consume ':'
        var valueStart = _position;

        if (Current == '-')
        {
            Advance();
        }

        while (char.IsDigit(Current))
        {
            Advance();
        }

        var valueText = _source[valueStart.._position];
        if (int.TryParse(valueText, out var value))
        {
            return MakeToken(TokenKind.IntLiteral, value);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "INT");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanTypedStringLiteral()
    {
        Advance(); // consume ':'

        if (Current != '"')
        {
            _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "STR");
            return MakeToken(TokenKind.Error);
        }

        return ScanStringLiteralValue();
    }

    private Token ScanTypedBoolLiteral()
    {
        Advance(); // consume ':'
        var valueStart = _position;

        while (char.IsLetter(Current))
        {
            Advance();
        }

        var valueText = _source[valueStart.._position].ToLowerInvariant();
        if (valueText == "true")
        {
            return MakeToken(TokenKind.BoolLiteral, true);
        }
        if (valueText == "false")
        {
            return MakeToken(TokenKind.BoolLiteral, false);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "BOOL");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanTypedFloatLiteral()
    {
        Advance(); // consume ':'
        var valueStart = _position;

        if (Current == '-')
        {
            Advance();
        }

        while (char.IsDigit(Current))
        {
            Advance();
        }

        if (Current == '.')
        {
            Advance();
            while (char.IsDigit(Current))
            {
                Advance();
            }
        }

        // Handle scientific notation
        if (Current is 'e' or 'E')
        {
            Advance();
            if (Current is '+' or '-')
            {
                Advance();
            }
            while (char.IsDigit(Current))
            {
                Advance();
            }
        }

        var valueText = _source[valueStart.._position];
        if (double.TryParse(valueText, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return MakeToken(TokenKind.FloatLiteral, value);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "FLOAT");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanStringLiteral()
    {
        return ScanStringLiteralValue();
    }

    private Token ScanStringLiteralValue()
    {
        Advance(); // consume opening quote

        var sb = new System.Text.StringBuilder();
        while (!IsAtEnd && Current != '"')
        {
            if (Current == '\\')
            {
                Advance();
                var escaped = Current switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => '\0'
                };

                if (escaped == '\0')
                {
                    _diagnostics.ReportInvalidEscapeSequence(CurrentSpan(), Current);
                }
                else
                {
                    sb.Append(escaped);
                }
                Advance();
            }
            else if (Current == '\n')
            {
                _diagnostics.ReportUnterminatedString(CurrentSpan());
                return MakeToken(TokenKind.Error);
            }
            else
            {
                sb.Append(Current);
                Advance();
            }
        }

        if (IsAtEnd)
        {
            _diagnostics.ReportUnterminatedString(CurrentSpan());
            return MakeToken(TokenKind.Error);
        }

        Advance(); // consume closing quote
        return MakeToken(TokenKind.StrLiteral, sb.ToString());
    }

    private Token ScanNumber()
    {
        if (Current == '-')
        {
            Advance();
        }

        while (char.IsDigit(Current))
        {
            Advance();
        }

        // Check for float
        if (Current == '.' && char.IsDigit(Lookahead))
        {
            Advance(); // consume '.'
            while (char.IsDigit(Current))
            {
                Advance();
            }

            var floatText = CurrentText();
            if (double.TryParse(floatText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floatValue))
            {
                return MakeToken(TokenKind.FloatLiteral, floatValue);
            }
        }

        var intText = CurrentText();
        if (int.TryParse(intText, out var intValue))
        {
            return MakeToken(TokenKind.IntLiteral, intValue);
        }

        _diagnostics.ReportInvalidTypedLiteral(CurrentSpan(), "number");
        return MakeToken(TokenKind.Error);
    }

    private Token ScanWhitespace()
    {
        while (Current is ' ' or '\t')
        {
            Advance();
        }
        return MakeToken(TokenKind.Whitespace);
    }

    private Token ScanNewline()
    {
        if (Current == '\r' && Lookahead == '\n')
        {
            Advance();
        }
        Advance();
        return MakeToken(TokenKind.Newline);
    }

    private Token ScanError()
    {
        _diagnostics.ReportUnexpectedCharacter(CurrentSpan(), Current);
        Advance();
        return MakeToken(TokenKind.Error);
    }
}
