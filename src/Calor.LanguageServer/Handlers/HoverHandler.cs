using Calor.Compiler.Ast;
using Calor.Compiler.Parsing;
using Calor.LanguageServer.Documentation;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles hover requests to show type info and documentation.
/// </summary>
public sealed class HoverHandler : HoverHandlerBase
{
    private readonly WorkspaceState _workspace;

    public HoverHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        var snapshot = state?.Snapshot;
        if (snapshot == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        return Task.FromResult(BuildHover(snapshot, request.Position));
    }

    internal static Hover? BuildHover(
        DocumentSnapshot snapshot,
        Position position)
    {
        var (line, column) = PositionConverter.ToCalorPosition(position);
        var offset = PositionConverter.ToOffset(position, snapshot.Source);
        var raw = Calor.Compiler.Parsing.RawCSharpFrame.FindAtOffset(snapshot.Source, offset);
        if (raw is { } token)
        {
            var headerEnd = token.Text.IndexOfAny(['\r', '\n']);
            if (headerEnd < 0 || offset >= token.Span.Start + headerEnd)
                return null;
            return new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = "**raw reason N** — preserves exactly N UTF-8 payload bytes after the header newline. Payload indentation and line endings are not Calor syntax."
                }),
                Range = PositionConverter.ToLspRange(
                    new Calor.Compiler.Parsing.TextSpan(token.Span.Start, headerEnd,
                        token.Span.Line, token.Span.Column), snapshot.Source)
            };
        }

        if (snapshot.GetTokenAtOffset(offset) is not { } syntaxToken || syntaxToken.Kind == TokenKind.Error)
            return null;

        var tagHover = TryGetTagHover(snapshot.Source, syntaxToken,
            syntaxToken.Span.Start > 0 ? snapshot.GetTokenAtOffset(syntaxToken.Span.Start - 1) : null);
        if (tagHover != null)
        {
            return tagHover;
        }

        if (syntaxToken.Kind != TokenKind.Identifier && !syntaxToken.IsLiteral)
            return null;

        // Fall back to symbol-based hover (when we have AST)
        if (snapshot.Ast != null)
        {
            var result = SymbolFinder.FindSymbolAtPosition(
                snapshot.Ast,
                line,
                column,
                snapshot.Source,
                snapshot.BoundModule);
            if (result != null)
            {
                var content = BuildHoverContent(result, snapshot.Ast, snapshot);
                var range = PositionConverter.ToLspRange(result.Span, snapshot.Source);

                return new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = content
                    }),
                    Range = range
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to provide hover documentation for a Calor syntax tag.
    /// </summary>
    private static Hover? TryGetTagHover(string source, Token token, Token? previous)
    {
        var tag = TagDocumentationProvider.GetSyntaxTag(token, previous);
        if (tag == null)
            return null;
        var doc = TagDocumentationProvider.Instance.GetTagDocumentation(tag);
        if (doc == null)
            return null;

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = doc.ToMarkdown()
            }),
            Range = PositionConverter.ToLspRange(token.Span, source)
        };
    }

    private static string BuildHoverContent(SymbolLookupResult result, ModuleNode ast, DocumentSnapshot snapshot)
    {
        var sb = new System.Text.StringBuilder();

        switch (result.Kind)
        {
            case "function":
                if (result.Node is FunctionNode func)
                {
                    AppendDeclarationHeader(sb, func, snapshot);
                    if (func.Effects != null)
                        sb.AppendLine($"**Effects:** `! {string.Join(",", func.Effects.Effects.Values)}`");
                    // #1618: parameter refinements are the preconditions.
                    var preconditionCount = RefinementPreconditions.Effective(func.Parameters, func.Preconditions).Count;
                    if (preconditionCount > 0)
                        sb.AppendLine($"**Preconditions:** {preconditionCount}");
                    if (func.Postconditions.Count > 0)
                        sb.AppendLine($"**Postconditions:** {func.Postconditions.Count}");

                    if (func.IsAsync)
                    {
                        sb.AppendLine();
                        sb.AppendLine("*Async function*");
                    }
                }
                break;

            case "parameter":
                sb.AppendLine("```calor");
                sb.AppendLine($"(parameter) {result.Type}:{result.Name}");
                sb.AppendLine("```");
                break;

            case "variable":
            case "mutable variable":
                sb.AppendLine("```calor");
                var binding = result.Kind == "mutable variable" ? "var" : "let";
                sb.AppendLine($"{binding} {(result.Type != null ? result.Type + ":" : "")}{result.Name} = ...");
                sb.AppendLine("```");
                break;

            case "variable reference":
                sb.AppendLine("```calor");
                sb.AppendLine($"(reference) {result.Name}{(result.Type != null ? $": {result.Type}" : "")}");
                sb.AppendLine("```");
                break;

            case "function call":
                var targetFunc = SymbolFinder.FindFunction(ast, result.Name);
                if (targetFunc != null)
                {
                    var paramList = string.Join(", ", targetFunc.Parameters.Select(p => $"{p.TypeName}:{p.Name}"));
                    var returnType = Compiler.Analysis.ReturnShape.GetAsyncReturnTypes(
                        targetFunc.Output?.TypeName ?? "void", targetFunc.IsAsync, ast,
                        targetFunc.TypeParameters.Select(parameter => parameter.Name)).CallableType;
                    sb.AppendLine("```calor");
                    sb.AppendLine($"(function) {result.Name}({paramList}) → {returnType}");
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine($"**Function call:** `{result.Name}`");
                }
                break;

            case "class":
                if (result.Node is ClassDefinitionNode cls)
                {
                    AppendDeclarationHeader(sb, cls, snapshot);
                    if (cls.BaseClass != null)
                        sb.AppendLine($"**Extends:** `{cls.BaseClass}`");
                    foreach (var implIface in cls.ImplementedInterfaces)
                        sb.AppendLine($"**Implements:** `{implIface}`");

                    if (cls.Fields.Count > 0 || cls.Methods.Count > 0 || cls.Properties.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"*{cls.Fields.Count} fields, {cls.Properties.Count} properties, {cls.Methods.Count} methods*");
                    }
                }
                break;

            case "field":
                if (result.Node is ClassFieldNode field)
                {
                    AppendDeclarationHeader(sb, field, snapshot);
                }
                break;

            case "method":
                if (result.Node is MethodNode method)
                {
                    AppendDeclarationHeader(sb, method, snapshot);
                }
                break;

            case "property":
                if (result.Node is PropertyNode prop)
                {
                    AppendDeclarationHeader(sb, prop, snapshot);
                }
                break;

            case "interface":
                if (result.Node is InterfaceDefinitionNode iface)
                {
                    AppendDeclarationHeader(sb, iface, snapshot);

                    if (iface.Methods.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine($"*{iface.Methods.Count} method signatures*");
                    }
                }
                break;

            case "enum":
                if (result.Node is EnumDefinitionNode enumDef)
                {
                    AppendDeclarationHeader(sb, enumDef, snapshot);

                    if (enumDef.Members.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("**Members:**");
                        foreach (var member in enumDef.Members.Take(5))
                        {
                            sb.AppendLine($"- `{member.Name}`{(member.Value != null ? $" = {member.Value}" : "")}");
                        }
                        if (enumDef.Members.Count > 5)
                        {
                            sb.AppendLine($"- *... and {enumDef.Members.Count - 5} more*");
                        }
                    }
                }
                break;

            case "enum member":
                sb.AppendLine("```calor");
                sb.AppendLine($"(enum member) {result.Name}{(result.Type != null ? $" : {result.Type}" : "")}");
                sb.AppendLine("```");
                break;

            default:
                sb.AppendLine($"**{result.Kind}:** `{result.Name}`");
                if (result.Type != null)
                {
                    sb.AppendLine($"**Type:** `{result.Type}`");
                }
                break;
        }

        return sb.ToString();
    }

    private static void AppendDeclarationHeader(
        System.Text.StringBuilder builder,
        AstNode node,
        DocumentSnapshot snapshot)
    {
        var end = node.Span.Start;
        var line = node.Span.Line;
        var depth = 0;
        foreach (var token in snapshot.Tokens)
        {
            if (token.Span.Start < node.Span.Start || token.Span.Length == 0 || token.IsTrivia)
                continue;
            if (token.Span.Line > line && depth == 0)
                break;
            if (token.Kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace)
                depth++;
            else if (token.Kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace)
                depth--;
            end = token.Span.End;
            line = token.Span.Line;
        }
        builder.AppendLine("```calor");
        builder.AppendLine(snapshot.Source[node.Span.Start..end].Trim());
        builder.AppendLine("```");
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor")
        };
    }
}
