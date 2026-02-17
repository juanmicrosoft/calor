using Calor.Compiler.Ast;
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
        if (state == null)
        {
            return Task.FromResult<Hover?>(null);
        }

        var (line, column) = PositionConverter.ToCalorPosition(request.Position);

        // Try tag documentation hover FIRST - educational docs for Calor syntax tags
        // This takes priority because users hovering over § tags want syntax help
        var tagHover = TryGetTagHover(state.Source, request.Position);
        if (tagHover != null)
        {
            return Task.FromResult<Hover?>(tagHover);
        }

        // Fall back to symbol-based hover (when we have AST)
        if (state.Ast != null)
        {
            var result = SymbolFinder.FindSymbolAtPosition(state.Ast, line, column, state.Source);
            if (result != null)
            {
                var content = BuildHoverContent(result, state.Ast);
                var range = PositionConverter.ToLspRange(result.Span, state.Source);

                return Task.FromResult<Hover?>(new Hover
                {
                    Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                    {
                        Kind = MarkupKind.Markdown,
                        Value = content
                    }),
                    Range = range
                });
            }
        }

        return Task.FromResult<Hover?>(null);
    }

    /// <summary>
    /// Attempts to provide hover documentation for a Calor syntax tag.
    /// </summary>
    private static Hover? TryGetTagHover(string source, Position position)
    {
        // Convert LSP position (0-based) to offset
        var offset = PositionConverter.ToOffset(position, source);
        if (offset < 0 || offset >= source.Length)
            return null;

        // Try to extract a tag at this position
        var tag = TagDocumentationProvider.ExtractTagAtPosition(source, offset);
        if (tag == null)
            return null;

        // Get documentation for the tag
        var doc = TagDocumentationProvider.Instance.GetTagDocumentation(tag);
        if (doc == null)
            return null;

        // Build the hover range (find the tag boundaries)
        var (startOffset, endOffset) = FindTagBoundaries(source, offset, tag);
        var range = OffsetRangeToLspRange(source, startOffset, endOffset);

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(new MarkupContent
            {
                Kind = MarkupKind.Markdown,
                Value = doc.ToMarkdown()
            }),
            Range = range
        };
    }

    /// <summary>
    /// Finds the start and end offsets of a tag in the source.
    /// </summary>
    private static (int Start, int End) FindTagBoundaries(string source, int offset, string tag)
    {
        // Find the § character
        var start = offset;
        while (start > 0 && source[start] != '§')
            start--;

        // Find the end of the tag name (before { or whitespace)
        var end = start + 1;
        if (end < source.Length && source[end] == '/')
            end++; // Skip closing tag slash

        while (end < source.Length && (char.IsLetter(source[end]) || source[end] == '?' || source[end] == '!'))
            end++;

        return (start, end);
    }

    /// <summary>
    /// Converts offset range to LSP Range.
    /// </summary>
    private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range OffsetRangeToLspRange(string source, int startOffset, int endOffset)
    {
        var startLine = 0;
        var startChar = 0;
        var endLine = 0;
        var endChar = 0;

        for (var i = 0; i < source.Length && i <= endOffset; i++)
        {
            if (i == startOffset)
            {
                startLine = endLine;
                startChar = endChar;
            }

            if (i == endOffset)
            {
                break;
            }

            if (source[i] == '\n')
            {
                endLine++;
                endChar = 0;
            }
            else
            {
                endChar++;
            }
        }

        return new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range
        {
            Start = new Position(startLine, startChar),
            End = new Position(endLine, endChar)
        };
    }

    private static string BuildHoverContent(SymbolLookupResult result, ModuleNode ast)
    {
        var sb = new System.Text.StringBuilder();

        switch (result.Kind)
        {
            case "function":
                if (result.Node is FunctionNode func)
                {
                    sb.AppendLine("```calor");
                    sb.Append($"§F{{{func.Id}:{func.Name}");
                    if (func.Visibility == Visibility.Public) sb.Append(":pub");
                    sb.AppendLine("}");

                    foreach (var param in func.Parameters)
                    {
                        sb.AppendLine($"  §I{{{param.TypeName}:{param.Name}}}");
                    }

                    if (func.Output != null)
                    {
                        sb.AppendLine($"  §O{{{func.Output.TypeName}}}");
                    }

                    if (func.Effects != null && func.Effects.Effects.Count > 0)
                    {
                        var effects = string.Join(",", func.Effects.Effects.Values);
                        sb.AppendLine($"  §E{{{effects}}}");
                    }

                    foreach (var pre in func.Preconditions)
                    {
                        sb.AppendLine($"  §Q (...)");
                    }

                    foreach (var post in func.Postconditions)
                    {
                        sb.AppendLine($"  §S (...)");
                    }

                    sb.AppendLine("```");

                    if (func.IsAsync)
                    {
                        sb.AppendLine();
                        sb.AppendLine("*Async function*");
                    }
                }
                break;

            case "parameter":
                sb.AppendLine("```calor");
                sb.AppendLine($"(parameter) {result.Name}: {result.Type}");
                sb.AppendLine("```");
                break;

            case "variable":
            case "mutable variable":
                sb.AppendLine("```calor");
                var mutability = result.Kind == "mutable variable" ? "~" : "";
                sb.AppendLine($"(variable) §B{{{mutability}{result.Name}}} = ...{(result.Type != null ? $" : {result.Type}" : "")}");
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
                    var paramList = string.Join(", ", targetFunc.Parameters.Select(p => $"{p.Name}: {p.TypeName}"));
                    var returnType = targetFunc.Output?.TypeName ?? "void";
                    sb.AppendLine("```calor");
                    sb.AppendLine($"(function) {result.Name}({paramList}) -> {returnType}");
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
                    sb.AppendLine("```calor");
                    sb.Append($"§CL{{{cls.Id}:{cls.Name}");
                    if (cls.IsAbstract) sb.Append(":abs");
                    if (cls.IsSealed) sb.Append(":sealed");
                    sb.AppendLine("}");

                    if (cls.BaseClass != null)
                    {
                        sb.AppendLine($"  §EXT{{{cls.BaseClass}}}");
                    }

                    foreach (var implIface in cls.ImplementedInterfaces)
                    {
                        sb.AppendLine($"  §IMPL{{{implIface}}}");
                    }

                    sb.AppendLine("```");

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
                    sb.AppendLine("```calor");
                    sb.AppendLine($"(field) §FLD{{{field.TypeName}:{field.Name}:{field.Visibility.ToString().ToLower()}}}");
                    sb.AppendLine("```");
                }
                break;

            case "method":
                if (result.Node is MethodNode method)
                {
                    sb.AppendLine("```calor");
                    sb.Append($"§MT{{{method.Id}:{method.Name}");
                    if (method.IsVirtual) sb.Append(":vr");
                    if (method.IsOverride) sb.Append(":ov");
                    if (method.IsAbstract) sb.Append(":ab");
                    sb.AppendLine("}");

                    foreach (var param in method.Parameters)
                    {
                        sb.AppendLine($"  §I{{{param.TypeName}:{param.Name}}}");
                    }

                    if (method.Output != null)
                    {
                        sb.AppendLine($"  §O{{{method.Output.TypeName}}}");
                    }

                    sb.AppendLine("```");
                }
                break;

            case "property":
                if (result.Node is PropertyNode prop)
                {
                    sb.AppendLine("```calor");
                    sb.AppendLine($"(property) §PROP{{{prop.Id}:{prop.TypeName}:{prop.Name}}}");
                    sb.AppendLine("```");
                }
                break;

            case "interface":
                if (result.Node is InterfaceDefinitionNode iface)
                {
                    sb.AppendLine("```calor");
                    sb.AppendLine($"§IFACE{{{iface.Id}:{iface.Name}}}");
                    sb.AppendLine("```");

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
                    sb.AppendLine("```calor");
                    sb.Append($"§EN{{{enumDef.Id}:{enumDef.Name}");
                    if (enumDef.UnderlyingType != null)
                    {
                        sb.Append($":{enumDef.UnderlyingType}");
                    }
                    sb.AppendLine("}");
                    sb.AppendLine("```");

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
