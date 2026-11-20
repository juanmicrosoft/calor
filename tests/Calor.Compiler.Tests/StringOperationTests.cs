using System.Reflection;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Calor.Compiler.Tests;

public class StringOperationTests
{
    private static ModuleNode ParseNative(string source)
    {
        var diagnostics = new DiagnosticBag();
        var lexer = new Lexer(source, diagnostics);
        var tokens = lexer.TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join("\n", diagnostics));
        var parser = new Parser(tokens, diagnostics);
        var module = parser.Parse();
        Assert.False(diagnostics.HasErrors, source + "\n" + string.Join("\n", diagnostics));
        return module;
    }

    // Frozen labels are not source translations. Native calls have ordinary call/member
    // ASTs; only the explicitly internal parser/API controls below produce string IR.
    private sealed record OperationCase(string Native, string Result, string Fixture, string? Keyword = null);
    private sealed record ControlSource(string Label, string Parameters);

    private static readonly IReadOnlyDictionary<string, OperationCase> Operations =
        new Dictionary<string, OperationCase>
        {
            ["§R (len s)"] = new("s.Length", "int", "(len s)"),
            ["§R (contains s \"hello\")"] = new("s.Contains(\"hello\")", "bool", "(contains s \"hello\")"),
            ["§R (contains s \"x\")"] = new("s.Contains(\"x\")", "bool", "(contains s \"x\")"),
            ["§R (starts s \"prefix\")"] = new("s.StartsWith(\"prefix\")", "bool", "(starts s \"prefix\")"),
            ["§R (starts s \"x\")"] = new("s.StartsWith(\"x\")", "bool", "(starts s \"x\")"),
            ["§R (ends s \"suffix\")"] = new("s.EndsWith(\"suffix\")", "bool", "(ends s \"suffix\")"),
            ["§R (ends s \"x\")"] = new("s.EndsWith(\"x\")", "bool", "(ends s \"x\")"),
            ["§R (indexof s \"x\")"] = new("s.IndexOf(\"x\")", "int", "(indexof s \"x\")"),
            ["§R (substr s 0 5)"] = new("s.Substring(0, 5)", "string", "(substr s 0 5)"),
            ["§R (substr s 5)"] = new("s.Substring(5)", "string", "(substr s 5)"),
            ["§R (replace s \"old\" \"new\")"] = new("s.Replace(\"old\", \"new\")", "string", "(replace s \"old\" \"new\")"),
            ["§R (replace s \"a\" \"b\")"] = new("s.Replace(\"a\", \"b\")", "string", "(replace s \"a\" \"b\")"),
            ["§R (upper s)"] = new("s.ToUpper()", "string", "(upper s)"),
            ["§R (lower s)"] = new("s.ToLower()", "string", "(lower s)"),
            ["§R (trim s)"] = new("s.Trim()", "string", "(trim s)"),
            ["§R (ltrim s)"] = new("s.TrimStart()", "string", "(ltrim s)"),
            ["§R (rtrim s)"] = new("s.TrimEnd()", "string", "(rtrim s)"),
            ["§R (lpad s 10)"] = new("s.PadLeft(10)", "string", "(lpad s 10)"),
            ["§R (rpad s 10)"] = new("s.PadRight(10)", "string", "(rpad s 10)"),
            ["§R (join \", \" items)"] = new("string.Join(\", \", items)", "string", "(join \", \" items)"),
            ["§R (join \",\" items)"] = new("string.Join(\",\", items)", "string", "(join \",\" items)"),
            ["§R (fmt \"Hello {0}\" name)"] = new("string.Format(\"Hello {0}\", name)", "string", "(fmt \"Hello {0}\" name)"),
            ["§R (concat a b c)"] = new("string.Concat(a, b, c)", "string", "(concat a b c)"),
            ["§R (isempty s)"] = new("string.IsNullOrEmpty(s)", "bool", "(isempty s)"),
            ["§R (isblank s)"] = new("string.IsNullOrWhiteSpace(s)", "bool", "(isblank s)"),
            ["§R (split s \",\")"] = new("s.Split(\",\")", "string[]", "(split s \",\")"),
            ["§R (str x)"] = new("x.ToString()", "string", "(str x)"),
            ["§R (str s)"] = new("s.ToString()", "string", "(str s)"),
            ["§R (upper s \"extra\")"] = new("s.ToUpper(\"extra\")", "string", "(upper s \"extra\")"),
            ["§R (contains s)"] = new("s.Contains()", "bool", "(contains s)"),
            ["§R (substr s)"] = new("s.Substring()", "string", "(substr s)"),
            ["§R (upper (trim s))"] = new("s.Trim().ToUpper()", "string", "(upper inner)"),
            ["§R (contains (lower s) \"hello\")"] = new("s.ToLower().Contains(\"hello\")", "bool", "(contains inner \"hello\")"),
            ["§R (len (upper (trim s)))"] = new("s.Trim().ToUpper().Length", "int", "(len inner)"),
            ["§R (> (len s) 10)"] = new("s.Length > 10", "bool", "(> inner 10)"),
            ["§R (fmt \"{0} + {1} = {2}\" a b c)"] = new("string.Format(\"{0} + {1} = {2}\", a, b, c)", "string", "(fmt \"{0} + {1} = {2}\" a b c)"),
            ["§R (concat a b c d e)"] = new("string.Concat(a, b, c, d, e)", "string", "(concat a b c d e)"),
            ["§R (contains s \"hello\" :ignore-case)"] = new("s.Contains(\"hello\", StringComparison.OrdinalIgnoreCase)", "bool", "(contains s \"hello\" :mode)", "ignore-case"),
            ["§R (starts s \"prefix\" :ordinal)"] = new("s.StartsWith(\"prefix\", StringComparison.Ordinal)", "bool", "(starts s \"prefix\" :mode)", "ordinal"),
            ["§R (ends s \"suffix\" :invariant)"] = new("s.EndsWith(\"suffix\", StringComparison.InvariantCulture)", "bool", "(ends s \"suffix\" :mode)", "invariant"),
            ["§R (indexof s \"x\" :invariant-ignore-case)"] = new("s.IndexOf(\"x\", StringComparison.InvariantCultureIgnoreCase)", "int", "(indexof s \"x\" :mode)", "invariant-ignore-case"),
            ["§R (equals s \"YES\" :ignore-case)"] = new("s.Equals(\"YES\", StringComparison.OrdinalIgnoreCase)", "bool", "(equals s \"YES\" :mode)", "ignore-case"),
            ["§R (contains s \"x\" :ignore-case)"] = new("s.Contains(\"x\", StringComparison.OrdinalIgnoreCase)", "bool", "(contains s \"x\" :mode)", "ignore-case"),
            ["§R (contains s \"x\" :ordinal)"] = new("s.Contains(\"x\", StringComparison.Ordinal)", "bool", "(contains s \"x\" :mode)", "ordinal"),
            ["§R (starts s \"x\" :ignore-case)"] = new("s.StartsWith(\"x\", StringComparison.OrdinalIgnoreCase)", "bool", "(starts s \"x\" :mode)", "ignore-case"),
            ["§R (ends s \"x\" :ignore-case)"] = new("s.EndsWith(\"x\", StringComparison.OrdinalIgnoreCase)", "bool", "(ends s \"x\" :mode)", "ignore-case"),
            ["§R (indexof s \"x\" :ignore-case)"] = new("s.IndexOf(\"x\", StringComparison.OrdinalIgnoreCase)", "int", "(indexof s \"x\" :mode)", "ignore-case"),
            ["§R (equals s \"x\" :ignore-case)"] = new("s.Equals(\"x\", StringComparison.OrdinalIgnoreCase)", "bool", "(equals s \"x\" :mode)", "ignore-case"),
            ["§R (contains s \"x\" :invariant)"] = new("s.Contains(\"x\", StringComparison.InvariantCulture)", "bool", "(contains s \"x\" :mode)", "invariant"),
            ["§R (contains s \"x\" :invariant-ignore-case)"] = new("s.Contains(\"x\", StringComparison.InvariantCultureIgnoreCase)", "bool", "(contains s \"x\" :mode)", "invariant-ignore-case"),
            ["§R (starts s \"x\" :ordinal)"] = new("s.StartsWith(\"x\", StringComparison.Ordinal)", "bool", "(starts s \"x\" :mode)", "ordinal"),
            ["§R (ends s \"x\" :invariant)"] = new("s.EndsWith(\"x\", StringComparison.InvariantCulture)", "bool", "(ends s \"x\" :mode)", "invariant"),
            ["§R (equals s \"x\" :invariant-ignore-case)"] = new("s.Equals(\"x\", StringComparison.InvariantCultureIgnoreCase)", "bool", "(equals s \"x\" :mode)", "invariant-ignore-case"),
            ["§R (upper s :ignore-case)"] = new("s.ToUpper(StringComparison.OrdinalIgnoreCase)", "string", "(upper s :mode)", "ignore-case"),
            ["§R (contains s \"x\" :invalid-mode)"] = new("s.Contains(\"x\", StringComparison.InvalidMode)", "bool", "(contains s \"x\" :mode)", "invalid-mode"),
            ["§R (equals s \"hello\")"] = new("s.Equals(\"hello\")", "bool", "(equals s \"hello\")"),
        };

    private static ControlSource WrapInFunction(string body) => new(body, "string:s");

    private static ControlSource WrapInFunctionWithParams(string body, string paramStr) =>
        new(body, paramStr switch
        {
            "§I{object:items}" => "object:items",
            "§I{string:name}" => "string:name",
            "§I{string:a} §I{string:b} §I{string:c}" => "string:a, string:b, string:c",
            "§I{i32:x}" => "int:x",
            "§I{i32:a} §I{i32:b} §I{i32:c}" => "int:a, int:b, int:c",
            "§I{string:a} §I{string:b} §I{string:c} §I{string:d} §I{string:e}" =>
                "string:a, string:b, string:c, string:d, string:e",
            _ => throw new ArgumentOutOfRangeException(nameof(paramStr), paramStr, null),
        });

    private static string NativeSource(string body, string parameters = "string:s", string result = "string") =>
        $"module Test\n  using System\n  func Main public ({parameters}) → {result}\n    !\n    "
        + body.Replace("\n", "\n    ", StringComparison.Ordinal) + "\n";

    private static ModuleNode ParseInternalControl(ControlSource control, out DiagnosticBag diagnostics)
    {
        var recipe = Operations[control.Label];
        var nativeSource = NativeSource("return " + recipe.Native, control.Parameters, recipe.Result);
        var native = AssertNativeRoundTrip(nativeSource);
        var expectedShape = CSharpShape(SyntaxFactory.ParseExpression(recipe.Native));
        Assert.Equal(expectedShape, NativeShape(Returned(native)));
        var generatedReturn = Assert.Single(CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(native))
            .GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>());
        Assert.Equal(expectedShape, CSharpShape(generatedReturn.Expression!));
        AssertRetiredExpressionRejected(control, nativeSource);

        var expression = InternalExpression(control.Label, out diagnostics);
        // The original string-returning scaffold deliberately remains emission-only for
        // bool/int/array operations. Native counterparts above use their actual result type.
        return InternalModule(control.Parameters, [new ReturnStatementNode(TextSpan.Empty, expression)]);
    }

    private static ModuleNode InternalModule(string parameters, IReadOnlyList<StatementNode> body)
    {
        var scaffold = ParseNative(NativeSource("return \"\"", parameters));
        var function = Assert.Single(scaffold.Functions);
        var internalFunction = new FunctionNode(function.Span, function.LocalId, function.Name,
            function.Visibility, function.Parameters, function.Output, function.Effects, body, function.Attributes)
        {
            NamespaceScopeId = function.NamespaceScopeId,
        };
        return scaffold.With(update =>
        {
            update.Functions = [internalFunction];
            update.Items = scaffold.Items.Select(item => ReferenceEquals(item, function) ? internalFunction : item).ToArray();
        });
    }

    private static ExpressionNode InternalExpression(string label, out DiagnosticBag diagnostics)
    {
        var recipe = Operations[label];
        diagnostics = new DiagnosticBag();
        var tokens = new Lexer(recipe.Fixture, diagnostics).TokenizeAllForParser();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        if (recipe.Keyword != null)
        {
            var index = tokens.FindIndex(token => token.Text == "mode");
            Assert.True(index >= 0);
            var marker = tokens[index];
            tokens[index] = new Token(TokenKind.Identifier, recipe.Keyword, marker.Span);
        }

        var expression = BuildInternalOperation(tokens);

        ExpressionNode Inner(string innerLabel)
        {
            var inner = InternalExpression(innerLabel, out var innerDiagnostics);
            Assert.False(innerDiagnostics.HasErrors, string.Join("\n", innerDiagnostics));
            return inner;
        }
        // Explicit programmatic nesting retains IR objectives without pretending the
        // native parenthesis dispatcher still recursively accepts Lisp expressions.
        return label switch
        {
            "§R (upper (trim s))" => new StringOperationNode(expression.Span, StringOp.ToUpper, [Inner("§R (trim s)")]),
            "§R (contains (lower s) \"hello\")" => new StringOperationNode(expression.Span, StringOp.Contains,
                [Inner("§R (lower s)"), new StringLiteralNode(TextSpan.Empty, "hello")]),
            "§R (len (upper (trim s)))" => new StringOperationNode(expression.Span, StringOp.Length, [Inner("§R (upper (trim s))")]),
            "§R (> (len s) 10)" => new BinaryOperationNode(expression.Span, BinaryOperator.GreaterThan,
                Inner("§R (len s)"), new IntLiteralNode(TextSpan.Empty, 10)),
            _ => expression,
        };
    }

    // 0.30 retired the Lisp prefix parser that used to build these fixtures. The converter
    // and the verifier still create StringOperationNode, so the IR the emitter receives is
    // assembled here the way that parser assembled it: `(op arg... [:mode])`, with a
    // two-argument substr meaning SubstringFrom.
    private static ExpressionNode BuildInternalOperation(IReadOnlyList<Token> tokens)
    {
        var position = 0;
        var open = tokens[position++];
        Assert.Equal(TokenKind.OpenParen, open.Kind);
        var op = tokens[position++].Text;
        var arguments = new List<ExpressionNode>();
        StringComparisonMode? mode = null;
        while (tokens[position].Kind != TokenKind.CloseParen)
        {
            var token = tokens[position++];
            switch (token.Kind)
            {
                case TokenKind.Colon:
                    mode = StringComparisonModeExtensions.FromKeyword(tokens[position++].Text);
                    break;
                case TokenKind.StrLiteral:
                    arguments.Add(new StringLiteralNode(token.Span, Assert.IsType<string>(token.Value)));
                    break;
                case TokenKind.IntLiteral:
                    // Same node ParseIntLiteral builds from the lexer's literal info.
                    var info = Assert.IsType<IntLiteralInfo>(token.Value);
                    arguments.Add(new IntLiteralNode(token.Span, info.Magnitude, info.Sign, info.Base, info.Width, info.Signedness)
                    {
                        WidthInferred = info.WidthInferred,
                    });
                    break;
                case TokenKind.Identifier:
                    arguments.Add(new ReferenceNode(token.Span, token.Text));
                    break;
                default:
                    throw new ArgumentException($"Unexpected {token.Kind} in fixture.");
            }
        }
        var span = open.Span.Union(tokens[position++].Span);
        Assert.All(tokens.Skip(position), token => Assert.True(token.Kind is TokenKind.Dedent or TokenKind.Eof));
        // Composite labels (`(> inner 10)`) are rebuilt from their inner operations by the caller.
        if (StringOpExtensions.FromString(op) is not { } operation)
            return new ReferenceNode(span, op);
        if (operation == StringOp.Substring && arguments.Count == 2)
            operation = StringOp.SubstringFrom;
        return new StringOperationNode(span, operation, arguments, mode);
    }

    private static ExpressionNode Returned(ModuleNode module) =>
        Assert.IsAssignableFrom<ExpressionNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(module.Functions).Body)).Expression);

    private static ModuleNode AssertNativeRoundTrip(string source)
    {
        var first = ParseNative(source);
        var emitted = new CalorEmitter().Emit(first);
        Assert.DoesNotContain("§", emitted);
        var second = ParseNative(emitted);
        AssertCSharpEquivalent(new CSharpEmitter().Emit(first), new CSharpEmitter().Emit(second));
        return first;
    }

    private sealed class RemoveParentheses : CSharpSyntaxRewriter
    {
        public override Microsoft.CodeAnalysis.SyntaxNode? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node) =>
            Visit(node.Expression);
    }

    private static void AssertCSharpEquivalent(string expected, string actual) =>
        Assert.True(SyntaxFactory.AreEquivalent(
            new RemoveParentheses().Visit(CSharpSyntaxTree.ParseText(expected).GetRoot()),
            new RemoveParentheses().Visit(CSharpSyntaxTree.ParseText(actual).GetRoot())), expected + "\n!=\n" + actual);

    private static string NativeShape(ExpressionNode expression) => expression switch
    {
        ReferenceNode reference => reference.Name,
        StringLiteralNode literal => SymbolDisplay.FormatLiteral(literal.Value, true),
        IntLiteralNode integer => integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FieldAccessNode field => $"{NativeShape(field.Target)}.{field.FieldName}",
        CallExpressionNode call => $"{call.Target}({string.Join(",", call.Arguments.Select(NativeShape))})",
        ExpressionCallNode call => $"{NativeShape(call.TargetExpression)}({string.Join(",", call.Arguments.Select(NativeShape))})",
        TypeOperationNode { Operation: TypeOp.Cast } cast => $"cast<{cast.TargetType}>({NativeShape(cast.Operand)})",
        BinaryOperationNode binary => $"{binary.Operator}({NativeShape(binary.Left)},{NativeShape(binary.Right)})",
        UnaryOperationNode unary => $"{unary.Operator}({NativeShape(unary.Operand)})",
        _ => throw new InvalidOperationException("Not an ordinary native string expression: " + expression.GetType().Name),
    };

    private static string CSharpShape(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => CSharpShape(parenthesized.Expression),
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        PredefinedTypeSyntax type => type.Keyword.ValueText,
        LiteralExpressionSyntax literal => literal.Token.Text,
        MemberAccessExpressionSyntax member => $"{CSharpShape(member.Expression)}.{member.Name}",
        InvocationExpressionSyntax call => $"{CSharpShape(call.Expression)}({string.Join(",", call.ArgumentList.Arguments.Select(a => CSharpShape(a.Expression)))})",
        CastExpressionSyntax cast => $"cast<{cast.Type}>({CSharpShape(cast.Expression)})",
        CheckedExpressionSyntax checkedExpression => CSharpShape(checkedExpression.Expression),
        BinaryExpressionSyntax binary => $"{binary.Kind() switch
        {
            SyntaxKind.GreaterThanExpression => nameof(BinaryOperator.GreaterThan),
            SyntaxKind.AddExpression => nameof(BinaryOperator.Add),
            SyntaxKind.LogicalAndExpression => nameof(BinaryOperator.And),
            _ => throw new InvalidOperationException(binary.Kind().ToString()),
        }}({CSharpShape(binary.Left)},{CSharpShape(binary.Right)})",
        _ => throw new InvalidOperationException("Unexpected C# oracle: " + expression.Kind()),
    };

    private static void AssertRetiredExpressionRejected(ControlSource control, string validNative)
    {
        ParseNative(validNative);
        var recipe = Operations[control.Label];
        AssertRetiredMarkers(NativeSource(control.Label, control.Parameters, recipe.Result));
        // Test the removed prefix independently of §R and a legacy module header.
        var retired = control.Label["§R ".Length..];
        var source = NativeSource("return " + retired, control.Parameters, recipe.Result);
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        Assert.DoesNotContain(tokens, token => token.Kind is TokenKind.RawCSharpExpression or TokenKind.CSharpInterop);
        new Parser(tokens, diagnostics).Parse();
        var start = source.IndexOf(retired, StringComparison.Ordinal);
        Assert.Contains(diagnostics.Errors, diagnostic =>
            diagnostic.Span.Start >= start && diagnostic.Span.Start < start + retired.Length
            && diagnostic.Code == DiagnosticCode.UnexpectedToken);
    }

    private static void AssertRetiredMarkers(string source)
    {
        var diagnostics = new DiagnosticBag();
        var tokens = new Lexer(source, diagnostics).TokenizeAllForParser();
        var markers = tokens.Where(token => token.Text.StartsWith('§')).ToArray();
        Assert.NotEmpty(markers);
        foreach (var marker in markers)
        {
            Assert.Equal(TokenKind.Error, marker.Kind);
            Assert.Contains(diagnostics.Errors, error =>
                error.Code == DiagnosticCode.UnknownSectionMarker && error.Span.Equals(marker.Span));
            // Each sigil is also rejected at its own site in a verified native scaffold.
            ParseNative(NativeSource("return \"\""));
            var isolatedSource = NativeSource(marker.Text);
            var isolatedDiagnostics = new DiagnosticBag();
            var isolatedTokens = new Lexer(isolatedSource, isolatedDiagnostics).TokenizeAllForParser();
            var offset = isolatedSource.IndexOf(marker.Text, StringComparison.Ordinal);
            var isolated = Assert.Single(isolatedTokens, token => token.Span.Start == offset);
            Assert.Equal(TokenKind.Error, isolated.Kind);
            Assert.Contains(isolatedDiagnostics.Errors, error =>
                error.Code == DiagnosticCode.UnknownSectionMarker && error.Span.Equals(isolated.Span));
        }
        Assert.DoesNotContain(tokens, token => token.Kind is TokenKind.RawCSharpExpression or TokenKind.CSharpInterop);
    }

    private static void AssertRuntime(string source, string method, object expected, params object?[] arguments)
    {
        var module = AssertNativeRoundTrip(source);
        foreach (var input in new[] { source, new CalorEmitter().Emit(module) })
        {
            ParseNative(input);
            var result = Program.Compile(input, "string-controls.calr", new CompilationOptions
            {
                EnableTypeChecking = true,
                EnforceEffects = false,
                StatusWriter = TextWriter.Null,
            });
            Assert.False(result.HasErrors, input + "\n" + string.Join("\n", result.Diagnostics));
            var compilation = CSharpCompilation.Create("StringControls_" + Guid.NewGuid().ToString("N"),
                [CSharpSyntaxTree.ParseText(GeneratedCSharpCompiler.GlobalUsingsPreamble + result.GeneratedCode)],
                GeneratedCSharpCompiler.References,
                new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emitted = compilation.Emit(stream);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
            var assembly = Assembly.Load(stream.ToArray());
            var target = Assert.Single(assembly.GetTypes().SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Static)), candidate => candidate.Name == method);
            Assert.Equal(expected, target.Invoke(null, arguments));
        }
    }

    private static StringOperationNode GetReturnExpression(ModuleNode module)
    {
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        var strOp = returnStmt!.Expression as StringOperationNode;
        Assert.NotNull(strOp);
        return strOp!;
    }

    private static ExpressionNode GetBindExpression(ModuleNode module)
    {
        var func = module.Functions[0];
        var bindStmt = func.Body[0] as BindStatementNode;
        Assert.NotNull(bindStmt);
        return bindStmt!.Initializer!;
    }

    #region Phase 1: AST Correctness Tests

    [Fact]
    public void Parse_Length_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (len s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Length, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_Contains_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (contains s \"hello\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Contains, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_StartsWith_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (starts s \"prefix\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.StartsWith, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_EndsWith_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (ends s \"suffix\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.EndsWith, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_IndexOf_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (indexof s \"x\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.IndexOf, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SubstringWithLength_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (substr s 0 5)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Substring, strOp.Operation);
        Assert.Equal(3, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_SubstringFrom_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (substr s 5)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.SubstringFrom, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Replace_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (replace s \"old\" \"new\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Replace, strOp.Operation);
        Assert.Equal(3, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_ToUpper_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (upper s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToUpper, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_ToLower_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (lower s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToLower, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_Trim_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (trim s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Trim, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_TrimStart_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (ltrim s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.TrimStart, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_TrimEnd_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (rtrim s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.TrimEnd, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_PadLeft_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (lpad s 10)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.PadLeft, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_PadRight_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (rpad s 10)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.PadRight, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Join_ReturnsStringOperationNode()
    {
        // Test that join parses correctly - use string variables to avoid array syntax complexity
        var source = WrapInFunctionWithParams("§R (join \", \" items)", "§I{object:items}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Join, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Format_ReturnsStringOperationNode()
    {
        var source = WrapInFunctionWithParams("§R (fmt \"Hello {0}\" name)", "§I{string:name}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Format, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_Concat_ReturnsStringOperationNode()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c)", "§I{string:a} §I{string:b} §I{string:c}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Concat, strOp.Operation);
        Assert.Equal(3, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_IsNullOrEmpty_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (isempty s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.IsNullOrEmpty, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_IsNullOrWhiteSpace_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (isblank s)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.IsNullOrWhiteSpace, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    [Fact]
    public void Parse_Split_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (split s \",\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Split, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Parse_ToString_ReturnsStringOperationNode()
    {
        var source = WrapInFunctionWithParams("§R (str x)", "§I{i32:x}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToString, strOp.Operation);
        Assert.Single(strOp.Arguments);
    }

    #endregion

    #region Phase 2: C# Emission Tests

    [Theory]
    [InlineData("(len s)", "s.Length")]
    [InlineData("(contains s \"x\")", "s.Contains(\"x\")")]
    [InlineData("(starts s \"x\")", "s.StartsWith(\"x\")")]
    [InlineData("(ends s \"x\")", "s.EndsWith(\"x\")")]
    [InlineData("(indexof s \"x\")", "s.IndexOf(\"x\")")]
    [InlineData("(substr s 0 5)", "s.Substring(0, 5)")]
    [InlineData("(substr s 5)", "s.Substring(5)")]
    [InlineData("(replace s \"a\" \"b\")", "s.Replace(\"a\", \"b\")")]
    [InlineData("(upper s)", "s.ToUpper()")]
    [InlineData("(lower s)", "s.ToLower()")]
    [InlineData("(trim s)", "s.Trim()")]
    [InlineData("(ltrim s)", "s.TrimStart()")]
    [InlineData("(rtrim s)", "s.TrimEnd()")]
    [InlineData("(lpad s 10)", "s.PadLeft(10)")]
    [InlineData("(rpad s 10)", "s.PadRight(10)")]
    [InlineData("(isempty s)", "string.IsNullOrEmpty(s)")]
    [InlineData("(isblank s)", "string.IsNullOrWhiteSpace(s)")]
    [InlineData("(split s \",\")", "s.Split(\",\")")]
    [InlineData("(str s)", "s.ToString()")]
    public void Emit_StringOperation_ProducesCorrectCSharp(string calor, string expectedCSharp)
    {
        var source = WrapInFunction($"§R {calor}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains(expectedCSharp, code);
    }

    [Fact]
    public void Emit_Join_ProducesCorrectCSharp()
    {
        // Test that join emits correctly - use object type to avoid array syntax complexity
        var source = WrapInFunctionWithParams("§R (join \",\" items)", "§I{object:items}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Join(\",\", items)", code);
    }

    [Fact]
    public void Emit_Format_ProducesCorrectCSharp()
    {
        var source = WrapInFunctionWithParams("§R (fmt \"Hello {0}\" name)", "§I{string:name}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Format(\"Hello {0}\", name)", code);
    }

    [Fact]
    public void Emit_Concat_ProducesCorrectCSharp()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c)", "§I{string:a} §I{string:b} §I{string:c}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Concat(a, b, c)", code);
    }

    #endregion

    #region Phase 3: Error Cases

    [Fact]
    public void Parse_UpperWithoutArguments_IsOrdinaryIdentifierGrouping()
    {
        var source = WrapInFunction("§R (upper)");
        AssertRetiredMarkers(NativeSource(source.Label));
        var module = AssertNativeRoundTrip(NativeSource("return (upper)"));
        var statement = Assert.IsType<ReturnStatementNode>(Assert.Single(Assert.Single(module.Functions).Body));
        Assert.Equal("upper", Assert.IsType<ReferenceNode>(statement.Expression).Name);
    }




    #endregion

    #region Phase 4: Composition Tests (Nested Operations)

    [Fact]
    public void Parse_Nested_UpperTrim_ReturnsCorrectAst()
    {
        var source = WrapInFunction("§R (upper (trim s))");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.ToUpper, strOp.Operation);
        Assert.Single(strOp.Arguments);
        var inner = strOp.Arguments[0] as StringOperationNode;
        Assert.NotNull(inner);
        Assert.Equal(StringOp.Trim, inner!.Operation);
        AssertRuntime("""
            module ReceiverControl
              module Probe public
                field int:calls private static = 0
                func Next public () → string
                  ! mut
                  set calls calls + 1
                  return " x "
                func Run public () → int
                  ! mut
                  let length = Next().Trim().ToUpper().Length
                  return calls * 100 + length
            """, "Run", 101);
    }

    [Fact]
    public void Emit_Nested_UpperTrim_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (upper (trim s))");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("s.Trim().ToUpper()", code);
    }

    [Fact]
    public void Parse_Nested_ContainsLower_ReturnsCorrectAst()
    {
        var source = WrapInFunction("§R (contains (lower s) \"hello\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Contains, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
        var inner = strOp.Arguments[0] as StringOperationNode;
        Assert.NotNull(inner);
        Assert.Equal(StringOp.ToLower, inner!.Operation);
    }

    [Fact]
    public void Emit_Nested_ContainsLower_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (contains (lower s) \"hello\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("s.ToLower().Contains(\"hello\")", code);
    }

    [Fact]
    public void Emit_Nested_ThreeDeep_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (len (upper (trim s)))");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("s.Trim().ToUpper().Length", code);
    }

    #endregion

    #region Phase 5: Expression Context Tests

    [Fact]
    public void Parse_StringOp_InCondition_Works()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{string:s}
                  §O{string}
                  §IF (isempty s)
                      §R ""
                  §R s
            """;
        AssertRetiredMarkers(source);
        var native = AssertNativeRoundTrip(NativeSource("if string.IsNullOrEmpty(s)\n  return \"\"\nreturn s"));
        var nativeIf = Assert.IsType<IfStatementNode>(native.Functions[0].Body[0]);
        Assert.Equal("string.IsNullOrEmpty(s)", NativeShape(nativeIf.Condition));
        Assert.Equal("", Assert.IsType<StringLiteralNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(nativeIf.ThenBody)).Expression).Value);
        Assert.Equal("s", Assert.IsType<ReferenceNode>(Assert.IsType<ReturnStatementNode>(
            native.Functions[0].Body[1]).Expression).Name);
        var condition = InternalExpression("§R (isempty s)", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var module = InternalModule("string:s",
        [
            new IfStatementNode(TextSpan.Empty, "if", condition,
                [new ReturnStatementNode(TextSpan.Empty, new StringLiteralNode(TextSpan.Empty, ""))],
                [], null, new AttributeCollection()),
            new ReturnStatementNode(TextSpan.Empty, new ReferenceNode(TextSpan.Empty, "s")),
        ]);
        var func = module.Functions[0];
        var ifStmt = func.Body[0] as IfStatementNode;
        Assert.NotNull(ifStmt);
        var cond = ifStmt!.Condition as StringOperationNode;
        Assert.NotNull(cond);
        Assert.Equal(StringOp.IsNullOrEmpty, cond!.Operation);
    }

    [Fact]
    public void Parse_StringOp_InBinaryExpression_Works()
    {
        var source = WrapInFunction("§R (> (len s) 10)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var func = module.Functions[0];
        var returnStmt = func.Body[0] as ReturnStatementNode;
        Assert.NotNull(returnStmt);
        // Return is a binary operation containing a string operation
        var binOp = returnStmt!.Expression as BinaryOperationNode;
        Assert.NotNull(binOp);
        Assert.Equal(BinaryOperator.GreaterThan, binOp!.Operator);
        // Left operand should be the string length operation
        var strOp = binOp.Left as StringOperationNode;
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Length, strOp!.Operation);
    }

    [Fact]
    public void Parse_StringOp_InBinding_Works()
    {
        var source = """
            §M{Test}
              §F{Main:pub}
                  §I{string:s}
                  §O{string}
                  §B{x} (upper (trim s))
                  §R x
            """;
        AssertRetiredMarkers(source);
        var native = AssertNativeRoundTrip(NativeSource("let x = s.Trim().ToUpper()\nreturn x"));
        var nativeBinding = Assert.IsType<BindStatementNode>(native.Functions[0].Body[0]);
        Assert.Equal("x", nativeBinding.Name);
        Assert.Equal("s.Trim().ToUpper()", NativeShape(nativeBinding.Initializer!));
        Assert.Equal("x", Assert.IsType<ReferenceNode>(Assert.IsType<ReturnStatementNode>(
            native.Functions[0].Body[1]).Expression).Name);
        var initializer = InternalExpression("§R (upper (trim s))", out var diagnostics);
        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var module = InternalModule("string:s",
        [
            new BindStatementNode(TextSpan.Empty, "x", null, false, initializer, new AttributeCollection()),
            new ReturnStatementNode(TextSpan.Empty, new ReferenceNode(TextSpan.Empty, "x")),
        ]);
        var expr = GetBindExpression(module);
        var strOp = expr as StringOperationNode;
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.ToUpper, strOp!.Operation);
    }

    #endregion

    #region Phase 6: Round-Trip Tests (CalorEmitter)

    [Theory]
    [InlineData("(len s)")]
    [InlineData("(contains s \"x\")")]
    [InlineData("(starts s \"x\")")]
    [InlineData("(ends s \"x\")")]
    [InlineData("(indexof s \"x\")")]
    [InlineData("(substr s 0 5)")]
    [InlineData("(substr s 5)")]
    [InlineData("(replace s \"a\" \"b\")")]
    [InlineData("(upper s)")]
    [InlineData("(lower s)")]
    [InlineData("(trim s)")]
    [InlineData("(ltrim s)")]
    [InlineData("(rtrim s)")]
    [InlineData("(lpad s 10)")]
    [InlineData("(rpad s 10)")]
    [InlineData("(isempty s)")]
    [InlineData("(isblank s)")]
    [InlineData("(split s \",\")")]
    [InlineData("(str s)")]
    public void RoundTrip_StringOp_ProducesValidCalor(string op)
    {
        var source = WrapInFunction($"§R {op}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        Assert.DoesNotContain("§", roundTripped);
        var reparsed = AssertNativeRoundTrip(roundTripped);
        Assert.Equal(CSharpShape(SyntaxFactory.ParseExpression(Operations[source.Label].Native)),
            NativeShape(Returned(reparsed)));
        AssertCSharpEquivalent(new CSharpEmitter().Emit(module), new CSharpEmitter().Emit(reparsed));
    }

    #endregion

    #region Phase 7: E2E Scenario Tests

    [Fact]
    public void E2E_EmailDomainExtractor()
    {
        // Extract domain from email: get part after @
        var source = """
            §M{Test}
              §F{GetDomain:pub}
                  §I{string:email}
                  §O{string}
                  §B{atIdx} (indexof email "@")
                  §R (substr email (+ atIdx 1))
            """;
        AssertRetiredMarkers(source);
        const string nativeSource = """
            module Test
              func GetDomain public (string:email) → string
                !
                let atIdx = email.IndexOf("@")
                return email.Substring(atIdx + 1)
            """;
        var module = AssertNativeRoundTrip(nativeSource);
        var body = Assert.Single(module.Functions).Body;
        Assert.Equal("email.IndexOf(\"@\")", NativeShape(Assert.IsType<BindStatementNode>(body[0]).Initializer!));
        Assert.Equal("email.Substring(Add(atIdx,1))", NativeShape(Assert.IsType<ReturnStatementNode>(body[1]).Expression!));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("email.IndexOf(\"@\")", code);
        Assert.Contains("email.Substring(checked(atIdx + 1))", code);
        AssertRuntime(nativeSource, "GetDomain", "example.org", "ada@example.org");
        AssertRuntime(nativeSource, "GetDomain", "whole", "whole");
    }

    [Fact]
    public void E2E_SlugGenerator()
    {
        // Generate slug: lowercase, replace spaces with dashes
        var source = """
            §M{Test}
              §F{ToSlug:pub}
                  §I{string:title}
                  §O{string}
                  §B{cleaned} (lower (trim title))
                  §R (replace cleaned " " "-")
            """;
        AssertRetiredMarkers(source);
        const string nativeSource = """
            module Test
              func ToSlug public (string:title) → string
                !
                let cleaned = title.Trim().ToLower()
                return cleaned.Replace(" ", "-")
            """;
        var module = AssertNativeRoundTrip(nativeSource);
        var body = Assert.Single(module.Functions).Body;
        Assert.Equal("title.Trim().ToLower()", NativeShape(Assert.IsType<BindStatementNode>(body[0]).Initializer!));
        Assert.Equal("cleaned.Replace(\" \",\"-\")", NativeShape(Assert.IsType<ReturnStatementNode>(body[1]).Expression!));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("title.Trim().ToLower()", code);
        Assert.Contains(".Replace(\" \", \"-\")", code);
        AssertRuntime(nativeSource, "ToSlug", "hello-world", "  Hello World  ");
    }

    [Fact]
    public void E2E_StringValidator()
    {
        // Validate non-empty and starts with prefix
        var source = """
            §M{Test}
              §F{IsValid:pub}
                  §I{string:value}
                  §I{string:prefix}
                  §O{bool}
                  §R (&& (! (isempty value)) (starts value prefix))
            """;
        AssertRetiredMarkers(source);
        const string nativeSource = """
            module Test
              func IsValid public (string:value, string:prefix) → bool
                !
                return !string.IsNullOrEmpty(value) && value.StartsWith(prefix)
            """;
        var module = AssertNativeRoundTrip(nativeSource);
        Assert.Equal("And(Not(string.IsNullOrEmpty(value)),value.StartsWith(prefix))", NativeShape(Returned(module)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.IsNullOrEmpty(value)", code);
        Assert.Contains("value.StartsWith(prefix)", code);
        AssertRuntime(nativeSource, "IsValid", false, "", "");
        AssertRuntime(nativeSource, "IsValid", true, "prefix-value", "prefix");
        AssertRuntime(nativeSource, "IsValid", false, "other", "prefix");
    }

    #endregion

    #region Phase 8: StringOpExtensions Tests

    [Theory]
    [InlineData("len", StringOp.Length)]
    [InlineData("contains", StringOp.Contains)]
    [InlineData("starts", StringOp.StartsWith)]
    [InlineData("ends", StringOp.EndsWith)]
    [InlineData("indexof", StringOp.IndexOf)]
    [InlineData("substr", StringOp.Substring)]
    [InlineData("replace", StringOp.Replace)]
    [InlineData("upper", StringOp.ToUpper)]
    [InlineData("lower", StringOp.ToLower)]
    [InlineData("trim", StringOp.Trim)]
    [InlineData("ltrim", StringOp.TrimStart)]
    [InlineData("rtrim", StringOp.TrimEnd)]
    [InlineData("lpad", StringOp.PadLeft)]
    [InlineData("rpad", StringOp.PadRight)]
    [InlineData("join", StringOp.Join)]
    [InlineData("fmt", StringOp.Format)]
    [InlineData("concat", StringOp.Concat)]
    [InlineData("isempty", StringOp.IsNullOrEmpty)]
    [InlineData("isblank", StringOp.IsNullOrWhiteSpace)]
    [InlineData("split", StringOp.Split)]
    [InlineData("str", StringOp.ToString)]
    public void FromString_ValidOperator_ReturnsCorrectEnum(string name, StringOp expected)
    {
        var result = StringOpExtensions.FromString(name);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromString_InvalidOperator_ReturnsNull()
    {
        var result = StringOpExtensions.FromString("invalid");
        Assert.Null(result);
    }

    [Theory]
    [InlineData(StringOp.Length, "len")]
    [InlineData(StringOp.Contains, "contains")]
    [InlineData(StringOp.ToUpper, "upper")]
    [InlineData(StringOp.ToLower, "lower")]
    [InlineData(StringOp.Trim, "trim")]
    [InlineData(StringOp.IsNullOrEmpty, "isempty")]
    [InlineData(StringOp.IsNullOrWhiteSpace, "isblank")]
    public void ToCalorName_ReturnsCorrectName(StringOp op, string expected)
    {
        var result = op.ToCalorName();
        Assert.Equal(expected, result);
    }

    #endregion

    #region Phase 9: C# Migration Tests (CSharpToCalorConverter)

    private static StringOperationNode? FindStringOperationInResult(ConversionResult result)
    {
        if (!result.Success || result.Ast == null) return null;

        // Try functions first
        var func = result.Ast.Functions.FirstOrDefault();
        if (func != null)
        {
            var returnStmt = func.Body.FirstOrDefault() as ReturnStatementNode;
            if (returnStmt?.Expression is StringOperationNode strOp)
                return strOp;
        }

        // Try classes with methods
        foreach (var cls in result.Ast.Classes)
        {
            foreach (var method in cls.Methods)
            {
                var returnStmt = method.Body.FirstOrDefault() as ReturnStatementNode;
                if (returnStmt?.Expression is StringOperationNode strOp)
                    return strOp;
            }
        }

        return null;
    }

    private static ExpressionNode ConvertedReturn(ModuleNode module) =>
        Assert.IsAssignableFrom<ExpressionNode>(Assert.IsType<ReturnStatementNode>(
            Assert.Single(Assert.Single(Assert.Single(module.Classes).Methods).Body)).Expression);

    private static ModuleNode AssertMigrationNative(ConversionResult result, string expectedExpression)
    {
        Assert.NotNull(result.Ast);
        Assert.Empty(result.Losses);
        Assert.Equal(0, result.InteropPreservationCount);
        Assert.Equal(0, result.LossySubstitutionCount);
        Assert.Equal(0, result.DropCount);
        var source = new CalorEmitter().Emit(result.Ast);
        Assert.DoesNotContain("§", source);
        var native = AssertNativeRoundTrip(source);
        Assert.Equal(CSharpShape(SyntaxFactory.ParseExpression(expectedExpression)), NativeShape(ConvertedReturn(native)));
        AssertCSharpEquivalent(new CSharpEmitter().Emit(result.Ast), new CSharpEmitter().Emit(native));
        return native;
    }

    [Theory]
    [InlineData("s.ToUpper()", StringOp.ToUpper)]
    [InlineData("s.ToLower()", StringOp.ToLower)]
    [InlineData("s.Trim()", StringOp.Trim)]
    [InlineData("s.TrimStart()", StringOp.TrimStart)]
    [InlineData("s.TrimEnd()", StringOp.TrimEnd)]
    public void Migration_InstanceMethodNoArgs_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public string M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
        AssertMigrationNative(result, csharpExpr);
    }

    [Theory]
    [InlineData("s.Contains(\"x\")", StringOp.Contains)]
    [InlineData("s.StartsWith(\"x\")", StringOp.StartsWith)]
    [InlineData("s.EndsWith(\"x\")", StringOp.EndsWith)]
    [InlineData("s.IndexOf(\"x\")", StringOp.IndexOf)]
    public void Migration_InstanceMethodOneArg_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public object M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
        AssertMigrationNative(result, csharpExpr);
    }

    [Theory]
    [InlineData("s.Substring(5)", StringOp.SubstringFrom)]
    [InlineData("s.PadLeft(10)", StringOp.PadLeft)]
    [InlineData("s.PadRight(10)", StringOp.PadRight)]
    public void Migration_InstanceMethodIntArg_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public string M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
        AssertMigrationNative(result, csharpExpr);
    }

    [Theory]
    [InlineData("s.Substring(0, 5)", StringOp.Substring)]
    [InlineData("s.Replace(\"a\", \"b\")", StringOp.Replace)]
    public void Migration_InstanceMethodTwoArgs_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public string M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
        AssertMigrationNative(result, csharpExpr);
    }

    [Theory]
    [InlineData("string.IsNullOrEmpty(s)", StringOp.IsNullOrEmpty)]
    [InlineData("string.IsNullOrWhiteSpace(s)", StringOp.IsNullOrWhiteSpace)]
    public void Migration_StaticStringMethod_ConvertsToStringOp(string csharpExpr, StringOp expectedOp)
    {
        var csharp = $"public class Test {{ public bool M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
        AssertMigrationNative(result, csharpExpr);
    }

    [Fact]
    public void Migration_StringLength_ConvertsToLenOp()
    {
        var csharp = "public class Test { public int M(string s) => s.Length; }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Length, strOp!.Operation);
        AssertMigrationNative(result, "s.Length");
    }

    [Fact]
    public void Migration_StringConcat_ConvertsToStringOp()
    {
        var csharp = "public class Test { public string M(string a, string b) => string.Concat(a, b); }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Concat, strOp!.Operation);
        AssertMigrationNative(result, "string.Concat(a, b)");
    }

    [Fact]
    public void Migration_StringJoin_ConvertsToStringOp()
    {
        var csharp = "public class Test { public string M(string[] items) => string.Join(\",\", items); }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Join, strOp!.Operation);
        AssertMigrationNative(result, "string.Join(\",\", items)");
    }

    [Fact]
    public void Migration_StringFormat_ConvertsToStringOp()
    {
        var csharp = "public class Test { public string M(string name) => string.Format(\"Hello {0}\", name); }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(StringOp.Format, strOp!.Operation);
        AssertMigrationNative(result, "string.Format(\"Hello {0}\", name)");
    }

    [Theory]
    [InlineData("s.Contains(\"x\", StringComparison.OrdinalIgnoreCase)", StringOp.Contains, StringComparisonMode.IgnoreCase)]
    [InlineData("s.Contains(\"x\", StringComparison.Ordinal)", StringOp.Contains, StringComparisonMode.Ordinal)]
    [InlineData("s.StartsWith(\"x\", StringComparison.OrdinalIgnoreCase)", StringOp.StartsWith, StringComparisonMode.IgnoreCase)]
    [InlineData("s.EndsWith(\"x\", StringComparison.OrdinalIgnoreCase)", StringOp.EndsWith, StringComparisonMode.IgnoreCase)]
    [InlineData("s.IndexOf(\"x\", StringComparison.OrdinalIgnoreCase)", StringOp.IndexOf, StringComparisonMode.IgnoreCase)]
    [InlineData("s.Equals(\"x\", StringComparison.OrdinalIgnoreCase)", StringOp.Equals, StringComparisonMode.IgnoreCase)]
    [InlineData("s.Contains(\"x\", StringComparison.InvariantCulture)", StringOp.Contains, StringComparisonMode.Invariant)]
    [InlineData("s.Contains(\"x\", StringComparison.InvariantCultureIgnoreCase)", StringOp.Contains, StringComparisonMode.InvariantIgnoreCase)]
    public void Migration_StringMethodWithComparison_ConvertsWithMode(string csharpExpr, StringOp expectedOp, StringComparisonMode expectedMode)
    {
        var csharp = $"using System; public class Test {{ public object M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));
        var strOp = FindStringOperationInResult(result);
        Assert.NotNull(strOp);
        Assert.Equal(expectedOp, strOp!.Operation);
        Assert.Equal(expectedMode, strOp.ComparisonMode);
        AssertMigrationNative(result, csharpExpr);
    }

    [Fact]
    public void Migration_StringEqualsStatic_PreservesStaticCall()
    {
        var csharp = "using System; public class Test { public bool M(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); } }";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);
        Assert.Contains("string.Equals", calor);
        Assert.Contains("StringComparison.OrdinalIgnoreCase", calor);
        Assert.DoesNotContain("(equals", calor);
        var cast = Assert.IsType<TypeOperationNode>(ConvertedReturn(result.Ast!));
        Assert.Equal(TypeOp.Cast, cast.Operation);
        Assert.Equal("bool", cast.TargetType);
        var originalCall = Assert.IsType<CallExpressionNode>(cast.Operand);
        Assert.Equal("string.Equals", originalCall.Target);
        Assert.Equal(3, originalCall.Arguments.Count);
        AssertMigrationNative(result, "(bool)string.Equals(a, b, StringComparison.OrdinalIgnoreCase)");
    }

    [Theory]
    [InlineData("s.Contains(\"x\", StringComparison.OrdinalIgnoreCase)", ":ignore-case")]
    [InlineData("s.StartsWith(\"x\", StringComparison.OrdinalIgnoreCase)", ":ignore-case")]
    [InlineData("s.Contains(\"x\", StringComparison.Ordinal)", ":ordinal")]
    [InlineData("s.Contains(\"x\", StringComparison.InvariantCulture)", ":invariant")]
    [InlineData("s.Contains(\"x\", StringComparison.InvariantCultureIgnoreCase)", ":invariant-ignore-case")]
    public void Migration_StringComparisonMode_RoundTripsToCalor(string csharpExpr, string expectedKeyword)
    {
        var csharp = $"using System; public class Test {{ public object M(string s) => {csharpExpr}; }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        var nativeMode = expectedKeyword switch
        {
            ":ignore-case" => "StringComparison.OrdinalIgnoreCase",
            ":ordinal" => "StringComparison.Ordinal",
            ":invariant" => "StringComparison.InvariantCulture",
            ":invariant-ignore-case" => "StringComparison.InvariantCultureIgnoreCase",
            _ => throw new ArgumentOutOfRangeException(nameof(expectedKeyword), expectedKeyword, null),
        };
        Assert.Contains(nativeMode, calor);
        Assert.DoesNotContain(expectedKeyword, calor);
        AssertMigrationNative(result, csharpExpr);
        AssertRuntime(NativeSource("return " + csharpExpr, result: "bool"), "Main",
            expectedKeyword is ":ignore-case" or ":invariant-ignore-case", "X");
    }

    [Theory]
    [InlineData("s.Contains(\"x\", StringComparison.CurrentCulture)")]
    [InlineData("s.Contains(\"x\", StringComparison.CurrentCultureIgnoreCase)")]
    public void Migration_UnsupportedStringComparison_DoesNotConvertToNative(string csharpExpr)
    {
        // These modes have no StringOperationNode mode. The ordinary CLR call is
        // preserved natively, not through raw C# interop or a substituted comparison.
        var csharp = $"public class Test {{ public bool M(string s) {{ return {csharpExpr}; }} }}";
        var converter = new CSharpToCalorConverter(new ConversionOptions { Fidelity = ConversionFidelity.Lossy });
        var result = converter.Convert(csharp);

        Assert.True(result.Success, string.Join(", ", result.Issues));

        // The emitted Calor should NOT contain native string operation keywords
        var calorEmitter = new CalorEmitter();
        var calor = calorEmitter.Emit(result.Ast!);

        // Should not have native contains with comparison mode
        Assert.DoesNotContain(":ignore-case", calor);
        Assert.DoesNotContain(":ordinal", calor);
        Assert.DoesNotContain(":invariant", calor);
        Assert.IsType<CallExpressionNode>(ConvertedReturn(result.Ast!));
        Assert.Null(FindStringOperationInResult(result));
        AssertMigrationNative(result, csharpExpr);
    }

    #endregion

    #region Phase 10: Token Economics Tests

    // Historical character-length comparisons only: neither model-token measurements
    // nor evidence that today's native spelling has an advantage. Bodies stay unchanged.

    [Fact]
    public void TokenEconomics_Upper_BuiltinMoreCompact()
    {
        // Builtin: (upper s) = 3 tokens: OpenParen, Identifier, Identifier, CloseParen
        // But in source form, "(upper s)" is much shorter than interop equivalent
        var builtin = "(upper s)";
        var interop = "§C{(. s ToUpper)} §/C"; // Approximate interop syntax

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_Contains_BuiltinMoreCompact()
    {
        var builtin = "(contains s \"x\")";
        var interop = "§C{(. s Contains)} §A \"x\" §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_Substring_BuiltinMoreCompact()
    {
        var builtin = "(substr s 0 5)";
        var interop = "§C{(. s Substring)} §A 0 §A 5 §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_IsNullOrEmpty_BuiltinMoreCompact()
    {
        var builtin = "(isempty s)";
        var interop = "§C{(string.IsNullOrEmpty)} §A s §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    [Fact]
    public void TokenEconomics_Replace_BuiltinMoreCompact()
    {
        var builtin = "(replace s \"a\" \"b\")";
        var interop = "§C{(. s Replace)} §A \"a\" §A \"b\" §/C";

        Assert.True(builtin.Length < interop.Length,
            $"Builtin ({builtin.Length} chars) should be shorter than interop ({interop.Length} chars)");
    }

    #endregion

    #region Phase 11: Additional AST/Emission Coverage

    [Theory]
    [InlineData(StringOp.Length, 1, 1)]
    [InlineData(StringOp.ToUpper, 1, 1)]
    [InlineData(StringOp.ToLower, 1, 1)]
    [InlineData(StringOp.Trim, 1, 1)]
    [InlineData(StringOp.TrimStart, 1, 1)]
    [InlineData(StringOp.TrimEnd, 1, 1)]
    [InlineData(StringOp.IsNullOrEmpty, 1, 1)]
    [InlineData(StringOp.IsNullOrWhiteSpace, 1, 1)]
    [InlineData(StringOp.ToString, 1, 1)]
    [InlineData(StringOp.Contains, 2, 2)]
    [InlineData(StringOp.StartsWith, 2, 2)]
    [InlineData(StringOp.EndsWith, 2, 2)]
    [InlineData(StringOp.IndexOf, 2, 4)]
    [InlineData(StringOp.SubstringFrom, 2, 2)]
    [InlineData(StringOp.Split, 2, 2)]
    [InlineData(StringOp.Join, 2, 2)]
    [InlineData(StringOp.PadLeft, 2, 3)]
    [InlineData(StringOp.PadRight, 2, 3)]
    [InlineData(StringOp.Substring, 3, 3)]
    [InlineData(StringOp.Replace, 3, 3)]
    [InlineData(StringOp.Concat, 2, int.MaxValue)]
    [InlineData(StringOp.Format, 2, int.MaxValue)]
    public void ArgCount_AllOperations_HaveCorrectBounds(StringOp op, int expectedMin, int expectedMax)
    {
        Assert.Equal(expectedMin, op.GetMinArgCount());
        Assert.Equal(expectedMax, op.GetMaxArgCount());
    }

    [Theory]
    [InlineData(StringOp.Substring, "substr")]
    [InlineData(StringOp.SubstringFrom, "substr")]
    [InlineData(StringOp.StartsWith, "starts")]
    [InlineData(StringOp.EndsWith, "ends")]
    [InlineData(StringOp.TrimStart, "ltrim")]
    [InlineData(StringOp.TrimEnd, "rtrim")]
    [InlineData(StringOp.PadLeft, "lpad")]
    [InlineData(StringOp.PadRight, "rpad")]
    [InlineData(StringOp.Format, "fmt")]
    [InlineData(StringOp.ToString, "str")]
    [InlineData(StringOp.Join, "join")]
    [InlineData(StringOp.Concat, "concat")]
    [InlineData(StringOp.Split, "split")]
    [InlineData(StringOp.IndexOf, "indexof")]
    [InlineData(StringOp.Replace, "replace")]
    public void ToCalorName_AllOperations_ReturnValidNames(StringOp op, string expectedName)
    {
        var result = op.ToCalorName();
        Assert.Equal(expectedName, result);
    }

    [Fact]
    public void Parse_FormatMultipleArgs_ReturnsCorrectNode()
    {
        var source = WrapInFunctionWithParams("§R (fmt \"{0} + {1} = {2}\" a b c)", "§I{i32:a} §I{i32:b} §I{i32:c}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Format, strOp.Operation);
        Assert.Equal(4, strOp.Arguments.Count); // format string + 3 args
    }

    [Fact]
    public void Parse_ConcatMany_ReturnsCorrectNode()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c d e)", "§I{string:a} §I{string:b} §I{string:c} §I{string:d} §I{string:e}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Concat, strOp.Operation);
        Assert.Equal(5, strOp.Arguments.Count);
    }

    [Fact]
    public void Emit_ConcatMany_ProducesCorrectCSharp()
    {
        var source = WrapInFunctionWithParams("§R (concat a b c d e)", "§I{string:a} §I{string:b} §I{string:c} §I{string:d} §I{string:e}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("string.Concat(a, b, c, d, e)", code);
    }

    #endregion

    #region Phase 12: StringComparison Mode Tests

    [Fact]
    public void Parse_Contains_WithIgnoreCase_ReturnsStringOperationNodeWithMode()
    {
        var source = WrapInFunction("§R (contains s \"hello\" :ignore-case)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Contains, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
        Assert.Equal(StringComparisonMode.IgnoreCase, strOp.ComparisonMode);
    }

    [Fact]
    public void Parse_StartsWith_WithOrdinal_ReturnsStringOperationNodeWithMode()
    {
        var source = WrapInFunction("§R (starts s \"prefix\" :ordinal)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.StartsWith, strOp.Operation);
        Assert.Equal(StringComparisonMode.Ordinal, strOp.ComparisonMode);
    }

    [Fact]
    public void Parse_EndsWith_WithInvariant_ReturnsStringOperationNodeWithMode()
    {
        var source = WrapInFunction("§R (ends s \"suffix\" :invariant)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.EndsWith, strOp.Operation);
        Assert.Equal(StringComparisonMode.Invariant, strOp.ComparisonMode);
    }

    [Fact]
    public void Parse_IndexOf_WithInvariantIgnoreCase_ReturnsStringOperationNodeWithMode()
    {
        var source = WrapInFunction("§R (indexof s \"x\" :invariant-ignore-case)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.IndexOf, strOp.Operation);
        Assert.Equal(StringComparisonMode.InvariantIgnoreCase, strOp.ComparisonMode);
    }

    [Fact]
    public void Parse_Equals_WithIgnoreCase_ReturnsStringOperationNodeWithMode()
    {
        var source = WrapInFunction("§R (equals s \"YES\" :ignore-case)");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Equals, strOp.Operation);
        Assert.Equal(StringComparisonMode.IgnoreCase, strOp.ComparisonMode);
    }

    [Theory]
    [InlineData("(contains s \"x\" :ignore-case)", "s.Contains(\"x\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("(contains s \"x\" :ordinal)", "s.Contains(\"x\", StringComparison.Ordinal)")]
    [InlineData("(starts s \"x\" :ignore-case)", "s.StartsWith(\"x\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("(ends s \"x\" :ignore-case)", "s.EndsWith(\"x\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("(indexof s \"x\" :ignore-case)", "s.IndexOf(\"x\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("(equals s \"x\" :ignore-case)", "s.Equals(\"x\", StringComparison.OrdinalIgnoreCase)")]
    [InlineData("(contains s \"x\" :invariant)", "s.Contains(\"x\", StringComparison.InvariantCulture)")]
    [InlineData("(contains s \"x\" :invariant-ignore-case)", "s.Contains(\"x\", StringComparison.InvariantCultureIgnoreCase)")]
    public void Emit_StringOperationWithMode_ProducesCorrectCSharp(string calor, string expectedCSharp)
    {
        var source = WrapInFunction($"§R {calor}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains(expectedCSharp, code);
    }

    [Theory]
    [InlineData("(contains s \"x\" :ignore-case)")]
    [InlineData("(starts s \"x\" :ordinal)")]
    [InlineData("(ends s \"x\" :invariant)")]
    [InlineData("(equals s \"x\" :invariant-ignore-case)")]
    public void RoundTrip_StringOpWithMode_ProducesValidCalor(string op)
    {
        var source = WrapInFunction($"§R {op}");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var calorEmitter = new CalorEmitter();
        var roundTripped = calorEmitter.Emit(module);

        var expectedMode = op switch
        {
            "(contains s \"x\" :ignore-case)" => "StringComparison.OrdinalIgnoreCase",
            "(starts s \"x\" :ordinal)" => "StringComparison.Ordinal",
            "(ends s \"x\" :invariant)" => "StringComparison.InvariantCulture",
            "(equals s \"x\" :invariant-ignore-case)" => "StringComparison.InvariantCultureIgnoreCase",
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, null),
        };
        Assert.Contains(expectedMode, roundTripped);
        Assert.DoesNotContain("§", roundTripped);
        var reparsed = AssertNativeRoundTrip(roundTripped);
        Assert.Equal(CSharpShape(SyntaxFactory.ParseExpression(Operations[source.Label].Native)),
            NativeShape(Returned(reparsed)));
        AssertCSharpEquivalent(new CSharpEmitter().Emit(module), new CSharpEmitter().Emit(reparsed));
    }



    [Theory]
    [InlineData("ordinal", StringComparisonMode.Ordinal)]
    [InlineData("ignore-case", StringComparisonMode.IgnoreCase)]
    [InlineData("invariant", StringComparisonMode.Invariant)]
    [InlineData("invariant-ignore-case", StringComparisonMode.InvariantIgnoreCase)]
    public void StringComparisonMode_FromKeyword_ReturnsCorrectMode(string keyword, StringComparisonMode expected)
    {
        var result = StringComparisonModeExtensions.FromKeyword(keyword);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StringComparisonMode.Ordinal, "ordinal")]
    [InlineData(StringComparisonMode.IgnoreCase, "ignore-case")]
    [InlineData(StringComparisonMode.Invariant, "invariant")]
    [InlineData(StringComparisonMode.InvariantIgnoreCase, "invariant-ignore-case")]
    public void StringComparisonMode_ToKeyword_ReturnsCorrectKeyword(StringComparisonMode mode, string expected)
    {
        var result = mode.ToKeyword();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StringComparisonMode.Ordinal, "StringComparison.Ordinal")]
    [InlineData(StringComparisonMode.IgnoreCase, "StringComparison.OrdinalIgnoreCase")]
    [InlineData(StringComparisonMode.Invariant, "StringComparison.InvariantCulture")]
    [InlineData(StringComparisonMode.InvariantIgnoreCase, "StringComparison.InvariantCultureIgnoreCase")]
    public void StringComparisonMode_ToCSharpName_ReturnsCorrectName(StringComparisonMode mode, string expected)
    {
        var result = mode.ToCSharpName();
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StringOp.Contains, true)]
    [InlineData(StringOp.StartsWith, true)]
    [InlineData(StringOp.EndsWith, true)]
    [InlineData(StringOp.IndexOf, true)]
    [InlineData(StringOp.Equals, true)]
    [InlineData(StringOp.ToUpper, false)]
    [InlineData(StringOp.ToLower, false)]
    [InlineData(StringOp.Trim, false)]
    [InlineData(StringOp.Replace, false)]
    public void SupportsComparisonMode_ReturnsCorrectValue(StringOp op, bool expected)
    {
        var result = StringOperationNode.SupportsComparisonMode(op);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Phase 13: Equals Operation Tests

    [Fact]
    public void Parse_Equals_ReturnsStringOperationNode()
    {
        var source = WrapInFunction("§R (equals s \"hello\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));
        var strOp = GetReturnExpression(module);
        Assert.Equal(StringOp.Equals, strOp.Operation);
        Assert.Equal(2, strOp.Arguments.Count);
    }

    [Fact]
    public void Emit_Equals_ProducesCorrectCSharp()
    {
        var source = WrapInFunction("§R (equals s \"hello\")");
        var module = ParseInternalControl(source, out var diagnostics);

        Assert.False(diagnostics.HasErrors, string.Join(", ", diagnostics.Select(d => d.Message)));

        var emitter = new CSharpEmitter();
        var code = emitter.Emit(module);

        Assert.Contains("s.Equals(\"hello\")", code);
    }

    [Theory]
    [InlineData(StringOp.Equals, 2, 2)]
    public void ArgCount_Equals_HasCorrectBounds(StringOp op, int expectedMin, int expectedMax)
    {
        Assert.Equal(expectedMin, op.GetMinArgCount());
        Assert.Equal(expectedMax, op.GetMaxArgCount());
    }

    #endregion
}
