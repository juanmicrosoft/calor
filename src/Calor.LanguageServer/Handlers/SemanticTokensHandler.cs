using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.State;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles semantic tokens requests for rich syntax highlighting.
/// Provides semantic meaning (class, function, parameter, etc.) beyond TextMate grammars.
/// </summary>
public sealed class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly WorkspaceState _workspace;

    // Standard LSP semantic token types
    private static readonly string[] TokenTypes =
    [
        "namespace",     // 0
        "type",          // 1
        "class",         // 2
        "enum",          // 3
        "interface",     // 4
        "struct",        // 5
        "typeParameter", // 6
        "parameter",     // 7
        "variable",      // 8
        "property",      // 9
        "enumMember",    // 10
        "event",         // 11
        "function",      // 12
        "method",        // 13
        "macro",         // 14
        "keyword",       // 15
        "modifier",      // 16
        "comment",       // 17
        "string",        // 18
        "number",        // 19
        "regexp",        // 20
        "operator",      // 21
        "decorator",     // 22
        "label",         // 23
    ];

    // Standard LSP semantic token modifiers
    private static readonly string[] TokenModifiers =
    [
        "declaration",   // 0
        "definition",    // 1
        "readonly",      // 2
        "static",        // 3
        "deprecated",    // 4
        "abstract",      // 5
        "async",         // 6
        "modification",  // 7
        "documentation", // 8
        "defaultLibrary",// 9
        "virtual",       // 10
        "override",      // 11
    ];

    public SemanticTokensHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(
                    TokenTypes.Select(t => new SemanticTokenType(t))),
                TokenModifiers = new Container<SemanticTokenModifier>(
                    TokenModifiers.Select(m => new SemanticTokenModifier(m)))
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = true
        };
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams @params,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(
            new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(
                    TokenTypes.Select(t => new SemanticTokenType(t))),
                TokenModifiers = new Container<SemanticTokenModifier>(
                    TokenModifiers.Select(m => new SemanticTokenModifier(m)))
            }));
    }

    protected override Task Tokenize(
        SemanticTokensBuilder builder,
        ITextDocumentIdentifierParams identifier,
        CancellationToken cancellationToken)
    {
        var state = _workspace.Get(identifier.TextDocument.Uri);
        if (state?.Ast == null)
        {
            return Task.CompletedTask;
        }

        var visitor = new SemanticTokenVisitor(builder, state.Source);
        visitor.Visit(state.Ast);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Visitor that generates semantic tokens from the AST.
    /// </summary>
    private sealed class SemanticTokenVisitor
    {
        private readonly SemanticTokensBuilder _builder;
        private readonly string _source;
        private readonly string[] _lines;

        public SemanticTokenVisitor(SemanticTokensBuilder builder, string source)
        {
            _builder = builder;
            _source = source;
            _lines = source.Split('\n');
        }

        public void Visit(ModuleNode module)
        {
            foreach (var func in module.Functions)
                VisitFunction(func);

            foreach (var cls in module.Classes)
                VisitClass(cls);

            foreach (var iface in module.Interfaces)
                VisitInterface(iface);

            foreach (var enumDef in module.Enums)
                VisitEnum(enumDef);

            foreach (var enumExt in module.EnumExtensions)
                VisitEnumExtension(enumExt);

            foreach (var del in module.Delegates)
                VisitDelegate(del);
        }

        private void VisitFunction(FunctionNode func)
        {
            var nameSpan = FindNameSpan(func.Span, func.Name);
            if (nameSpan.HasValue)
            {
                var mods = new List<SemanticTokenModifier> { SemanticTokenModifier.Declaration };
                if (func.IsAsync) mods.Add(new SemanticTokenModifier("async"));
                PushToken(nameSpan.Value, SemanticTokenType.Function, mods.ToArray());
            }

            foreach (var param in func.Parameters)
                VisitParameter(param);

            if (func.Output != null)
                VisitTypeReference(func.Output.Span, func.Output.TypeName);

            foreach (var stmt in func.Body)
                VisitStatement(stmt);
        }

        private void VisitClass(ClassDefinitionNode cls)
        {
            var nameSpan = FindNameSpan(cls.Span, cls.Name);
            if (nameSpan.HasValue)
            {
                var mods = new List<SemanticTokenModifier> { SemanticTokenModifier.Declaration };
                if (cls.IsAbstract) mods.Add(new SemanticTokenModifier("abstract"));
                PushToken(nameSpan.Value, SemanticTokenType.Class, mods.ToArray());
            }

            if (!string.IsNullOrEmpty(cls.BaseClass))
            {
                var baseSpan = FindNameInSource(cls.Span, cls.BaseClass);
                if (baseSpan.HasValue)
                    PushToken(baseSpan.Value, SemanticTokenType.Class);
            }

            foreach (var field in cls.Fields)
                VisitField(field);

            foreach (var prop in cls.Properties)
                VisitProperty(prop);

            foreach (var method in cls.Methods)
                VisitMethod(method);

            foreach (var ctor in cls.Constructors)
                VisitConstructor(ctor);
        }

        private void VisitInterface(InterfaceDefinitionNode iface)
        {
            var nameSpan = FindNameSpan(iface.Span, iface.Name);
            if (nameSpan.HasValue)
                PushToken(nameSpan.Value, SemanticTokenType.Interface, SemanticTokenModifier.Declaration);

            foreach (var method in iface.Methods)
            {
                var methodNameSpan = FindNameSpan(method.Span, method.Name);
                if (methodNameSpan.HasValue)
                    PushToken(methodNameSpan.Value, SemanticTokenType.Method, SemanticTokenModifier.Declaration);

                foreach (var param in method.Parameters)
                    VisitParameter(param);

                if (method.Output != null)
                    VisitTypeReference(method.Output.Span, method.Output.TypeName);
            }
        }

        private void VisitEnum(EnumDefinitionNode enumDef)
        {
            var nameSpan = FindNameSpan(enumDef.Span, enumDef.Name);
            if (nameSpan.HasValue)
                PushToken(nameSpan.Value, SemanticTokenType.Enum, SemanticTokenModifier.Declaration);

            foreach (var member in enumDef.Members)
            {
                var memberSpan = FindNameSpan(member.Span, member.Name);
                if (memberSpan.HasValue)
                    PushToken(memberSpan.Value, SemanticTokenType.EnumMember, SemanticTokenModifier.Declaration);
            }
        }

        private void VisitEnumExtension(EnumExtensionNode enumExt)
        {
            var nameSpan = FindNameSpan(enumExt.Span, enumExt.EnumName);
            if (nameSpan.HasValue)
                PushToken(nameSpan.Value, SemanticTokenType.Enum);

            // EnumExtension methods are FunctionNodes, not MethodNodes
            foreach (var method in enumExt.Methods)
                VisitFunction(method);
        }

        private void VisitDelegate(DelegateDefinitionNode del)
        {
            var nameSpan = FindNameSpan(del.Span, del.Name);
            if (nameSpan.HasValue)
                PushToken(nameSpan.Value, SemanticTokenType.Type, SemanticTokenModifier.Declaration);

            foreach (var param in del.Parameters)
                VisitParameter(param);

            if (del.Output != null)
                VisitTypeReference(del.Output.Span, del.Output.TypeName);
        }

        private void VisitMethod(MethodNode method)
        {
            var nameSpan = FindNameSpan(method.Span, method.Name);
            if (nameSpan.HasValue)
            {
                var mods = new List<SemanticTokenModifier> { SemanticTokenModifier.Declaration };
                if (method.IsAsync) mods.Add(new SemanticTokenModifier("async"));
                if (method.IsStatic) mods.Add(SemanticTokenModifier.Static);
                if (method.IsVirtual) mods.Add(new SemanticTokenModifier("virtual"));
                if (method.IsOverride) mods.Add(new SemanticTokenModifier("override"));
                if (method.IsAbstract) mods.Add(new SemanticTokenModifier("abstract"));
                PushToken(nameSpan.Value, SemanticTokenType.Method, mods.ToArray());
            }

            foreach (var param in method.Parameters)
                VisitParameter(param);

            if (method.Output != null)
                VisitTypeReference(method.Output.Span, method.Output.TypeName);

            foreach (var stmt in method.Body)
                VisitStatement(stmt);
        }

        private void VisitConstructor(ConstructorNode ctor)
        {
            foreach (var param in ctor.Parameters)
                VisitParameter(param);

            foreach (var stmt in ctor.Body)
                VisitStatement(stmt);
        }

        private void VisitField(ClassFieldNode field)
        {
            var nameSpan = FindNameSpan(field.Span, field.Name);
            if (nameSpan.HasValue)
                PushToken(nameSpan.Value, SemanticTokenType.Property, SemanticTokenModifier.Declaration);

            if (!string.IsNullOrEmpty(field.TypeName))
                VisitTypeReference(field.Span, field.TypeName);
        }

        private void VisitProperty(PropertyNode prop)
        {
            var nameSpan = FindNameSpan(prop.Span, prop.Name);
            if (nameSpan.HasValue)
                PushToken(nameSpan.Value, SemanticTokenType.Property, SemanticTokenModifier.Declaration);

            if (!string.IsNullOrEmpty(prop.TypeName))
                VisitTypeReference(prop.Span, prop.TypeName);

            if (prop.Getter != null)
                foreach (var stmt in prop.Getter.Body)
                    VisitStatement(stmt);

            if (prop.Setter != null)
                foreach (var stmt in prop.Setter.Body)
                    VisitStatement(stmt);
        }

        private void VisitParameter(ParameterNode param)
        {
            var nameSpan = FindNameSpan(param.Span, param.Name);
            if (nameSpan.HasValue)
                PushToken(nameSpan.Value, SemanticTokenType.Parameter, SemanticTokenModifier.Declaration);

            if (!string.IsNullOrEmpty(param.TypeName))
                VisitTypeReference(param.Span, param.TypeName);
        }

        private void VisitStatement(StatementNode stmt)
        {
            switch (stmt)
            {
                case BindStatementNode bind:
                    VisitBind(bind);
                    break;
                case AssignmentStatementNode assign:
                    VisitExpression(assign.Target);
                    VisitExpression(assign.Value);
                    break;
                case ReturnStatementNode ret:
                    if (ret.Expression != null) VisitExpression(ret.Expression);
                    break;
                case IfStatementNode ifStmt:
                    VisitExpression(ifStmt.Condition);
                    foreach (var s in ifStmt.ThenBody) VisitStatement(s);
                    foreach (var elseIf in ifStmt.ElseIfClauses)
                    {
                        VisitExpression(elseIf.Condition);
                        foreach (var s in elseIf.Body) VisitStatement(s);
                    }
                    if (ifStmt.ElseBody != null)
                        foreach (var s in ifStmt.ElseBody) VisitStatement(s);
                    break;
                case ForStatementNode forStmt:
                    var varSpan = FindNameSpan(forStmt.Span, forStmt.VariableName);
                    if (varSpan.HasValue)
                        PushToken(varSpan.Value, SemanticTokenType.Variable, SemanticTokenModifier.Declaration);
                    VisitExpression(forStmt.From);
                    VisitExpression(forStmt.To);
                    if (forStmt.Step != null) VisitExpression(forStmt.Step);
                    foreach (var s in forStmt.Body) VisitStatement(s);
                    break;
                case WhileStatementNode whileStmt:
                    VisitExpression(whileStmt.Condition);
                    foreach (var s in whileStmt.Body) VisitStatement(s);
                    break;
                case DoWhileStatementNode doWhile:
                    foreach (var s in doWhile.Body) VisitStatement(s);
                    VisitExpression(doWhile.Condition);
                    break;
                case ForeachStatementNode forEach:
                    var foreachVarSpan = FindNameSpan(forEach.Span, forEach.VariableName);
                    if (foreachVarSpan.HasValue)
                        PushToken(foreachVarSpan.Value, SemanticTokenType.Variable, SemanticTokenModifier.Declaration);
                    VisitExpression(forEach.Collection);
                    foreach (var s in forEach.Body) VisitStatement(s);
                    break;
                case MatchStatementNode match:
                    VisitExpression(match.Target);
                    foreach (var caseNode in match.Cases)
                        foreach (var s in caseNode.Body) VisitStatement(s);
                    break;
                case TryStatementNode tryStmt:
                    foreach (var s in tryStmt.TryBody) VisitStatement(s);
                    foreach (var catchClause in tryStmt.CatchClauses)
                    {
                        if (!string.IsNullOrEmpty(catchClause.VariableName))
                        {
                            var catchVarSpan = FindNameSpan(catchClause.Span, catchClause.VariableName);
                            if (catchVarSpan.HasValue)
                                PushToken(catchVarSpan.Value, SemanticTokenType.Variable, SemanticTokenModifier.Declaration);
                        }
                        foreach (var s in catchClause.Body) VisitStatement(s);
                    }
                    if (tryStmt.FinallyBody != null)
                        foreach (var s in tryStmt.FinallyBody) VisitStatement(s);
                    break;
                case ThrowStatementNode throwStmt:
                    if (throwStmt.Exception != null) VisitExpression(throwStmt.Exception);
                    break;
                case PrintStatementNode print:
                    VisitExpression(print.Expression);
                    break;
                case CallStatementNode call:
                    foreach (var arg in call.Arguments)
                        VisitExpression(arg);
                    break;
                case DictionaryForeachNode dictForeach:
                    var keySpan = FindNameSpan(dictForeach.Span, dictForeach.KeyName);
                    if (keySpan.HasValue)
                        PushToken(keySpan.Value, SemanticTokenType.Variable, SemanticTokenModifier.Declaration);
                    var valSpan = FindNameSpan(dictForeach.Span, dictForeach.ValueName);
                    if (valSpan.HasValue)
                        PushToken(valSpan.Value, SemanticTokenType.Variable, SemanticTokenModifier.Declaration);
                    foreach (var s in dictForeach.Body) VisitStatement(s);
                    break;
            }
        }

        private void VisitBind(BindStatementNode bind)
        {
            var nameSpan = FindNameSpan(bind.Span, bind.Name);
            if (nameSpan.HasValue)
            {
                var mods = new List<SemanticTokenModifier> { SemanticTokenModifier.Declaration };
                if (!bind.IsMutable) mods.Add(SemanticTokenModifier.Readonly);
                PushToken(nameSpan.Value, SemanticTokenType.Variable, mods.ToArray());
            }

            if (!string.IsNullOrEmpty(bind.TypeName))
                VisitTypeReference(bind.Span, bind.TypeName);

            if (bind.Initializer != null)
                VisitExpression(bind.Initializer);
        }

        private void VisitExpression(ExpressionNode? expr)
        {
            if (expr == null) return;

            switch (expr)
            {
                case ReferenceNode reference:
                    var refSpan = FindNameSpan(reference.Span, reference.Name);
                    if (refSpan.HasValue)
                        PushToken(refSpan.Value, SemanticTokenType.Variable);
                    break;
                case CallExpressionNode call:
                    // Target is a string (function/method name), not an expression
                    foreach (var arg in call.Arguments)
                        VisitExpression(arg);
                    break;
                case FieldAccessNode fieldAccess:
                    VisitExpression(fieldAccess.Target);
                    var fieldNameSpan = FindNameSpan(fieldAccess.Span, fieldAccess.FieldName);
                    if (fieldNameSpan.HasValue)
                        PushToken(fieldNameSpan.Value, SemanticTokenType.Property);
                    break;
                case IntLiteralNode:
                case FloatLiteralNode:
                    PushToken(expr.Span, SemanticTokenType.Number);
                    break;
                case StringLiteralNode:
                    PushToken(expr.Span, SemanticTokenType.String);
                    break;
                case BoolLiteralNode:
                    PushToken(expr.Span, SemanticTokenType.Keyword);
                    break;
                case BinaryOperationNode binary:
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    break;
                case UnaryOperationNode unary:
                    VisitExpression(unary.Operand);
                    break;
                case NewExpressionNode newExpr:
                    var typeSpan = FindNameSpan(newExpr.Span, newExpr.TypeName);
                    if (typeSpan.HasValue)
                        PushToken(typeSpan.Value, SemanticTokenType.Class);
                    foreach (var arg in newExpr.Arguments)
                        VisitExpression(arg);
                    break;
                case ThisExpressionNode:
                case BaseExpressionNode:
                    PushToken(expr.Span, SemanticTokenType.Keyword);
                    break;
                case LambdaExpressionNode lambda:
                    foreach (var param in lambda.Parameters)
                    {
                        var paramSpan = FindNameSpan(param.Span, param.Name);
                        if (paramSpan.HasValue)
                            PushToken(paramSpan.Value, SemanticTokenType.Parameter, SemanticTokenModifier.Declaration);
                    }
                    if (lambda.ExpressionBody != null) VisitExpression(lambda.ExpressionBody);
                    if (lambda.StatementBody != null)
                        foreach (var stmt in lambda.StatementBody) VisitStatement(stmt);
                    break;
                case ArrayAccessNode arrayAccess:
                    VisitExpression(arrayAccess.Array);
                    VisitExpression(arrayAccess.Index);
                    break;
                case AwaitExpressionNode awaitExpr:
                    VisitExpression(awaitExpr.Awaited);
                    break;
                case SomeExpressionNode some:
                    VisitExpression(some.Value);
                    break;
                case OkExpressionNode ok:
                    VisitExpression(ok.Value);
                    break;
                case ErrExpressionNode err:
                    VisitExpression(err.Error);
                    break;
                case ConditionalExpressionNode cond:
                    VisitExpression(cond.Condition);
                    VisitExpression(cond.WhenTrue);
                    VisitExpression(cond.WhenFalse);
                    break;
            }
        }

        private void VisitTypeReference(TextSpan span, string? typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return;

            var typeSpan = FindNameInSource(span, typeName);
            if (typeSpan.HasValue)
            {
                var tokenType = IsBuiltinType(typeName) ? SemanticTokenType.Keyword : SemanticTokenType.Type;
                PushToken(typeSpan.Value, tokenType);
            }
        }

        private static bool IsBuiltinType(string typeName)
        {
            return typeName.ToUpperInvariant() switch
            {
                "INT" or "I32" or "I64" => true,
                "FLOAT" or "F32" or "F64" => true,
                "BOOL" => true,
                "STRING" or "STR" => true,
                "CHAR" => true,
                "VOID" => true,
                "OBJECT" => true,
                _ => false
            };
        }

        private TextSpan? FindNameSpan(TextSpan containerSpan, string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return FindNameInSource(containerSpan, name);
        }

        private TextSpan? FindNameInSource(TextSpan containerSpan, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (containerSpan.Start >= _source.Length) return null;

            var end = Math.Min(containerSpan.End, _source.Length);
            if (end <= containerSpan.Start) return null;

            var searchText = _source.Substring(containerSpan.Start, end - containerSpan.Start);
            var index = searchText.IndexOf(name, StringComparison.Ordinal);

            if (index >= 0)
            {
                var startOffset = containerSpan.Start + index;
                var (line, col) = OffsetToLineColumn(startOffset);
                return new TextSpan(startOffset, name.Length, line + 1, col + 1);
            }

            return null;
        }

        private void PushToken(TextSpan span, SemanticTokenType type, params SemanticTokenModifier[] modifiers)
        {
            if (span.Start >= _source.Length || span.Length <= 0) return;

            var (line, col) = OffsetToLineColumn(span.Start);
            if (line < 0 || col < 0) return;

            var lineEnd = line < _lines.Length ? _lines[line].Length : 0;
            var length = Math.Min(span.Length, Math.Max(0, lineEnd - col));
            if (length <= 0) return;

            _builder.Push(line, col, length, type, modifiers);
        }

        private (int line, int col) OffsetToLineColumn(int offset)
        {
            var currentOffset = 0;
            for (var i = 0; i < _lines.Length; i++)
            {
                var lineLength = _lines[i].Length + 1;
                if (currentOffset + lineLength > offset)
                    return (i, offset - currentOffset);
                currentOffset += lineLength;
            }
            return (-1, -1);
        }
    }
}
