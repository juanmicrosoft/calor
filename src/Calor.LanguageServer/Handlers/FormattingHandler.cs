using Calor.Compiler.Formatting;
using Calor.LanguageServer.State;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles document formatting requests.
/// </summary>
public sealed class FormattingHandler : DocumentFormattingHandlerBase
{
    private readonly WorkspaceState _workspace;

    public FormattingHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state?.Ast == null || state.Diagnostics.HasErrors)
        {
            return Task.FromResult<TextEditContainer?>(null);
        }

        try
        {
            var formatter = new CalorFormatter();
            var result = formatter.FormatSource(
                state.Source,
                state.Uri.IsFile ? state.Uri.LocalPath : state.Uri.ToString());
            if (!result.Success
                || string.Equals(result.Original, result.Formatted, StringComparison.Ordinal))
            {
                return Task.FromResult<TextEditContainer?>(null);
            }

            var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(0, 0),
                GetDocumentEnd(state.Source)
            );

            var edit = new TextEdit
            {
                Range = range,
                NewText = result.Formatted
            };

            return Task.FromResult<TextEditContainer?>(new TextEditContainer(edit));
        }
        catch (Exception)
        {
            // If formatting fails, return no edits
            return Task.FromResult<TextEditContainer?>(null);
        }
    }

    private static Position GetDocumentEnd(string source)
    {
        var line = 0;
        var character = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    i++;
                }
                line++;
                character = 0;
            }
            else if (source[i] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }
        return new Position(line, character);
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentFormattingRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor")
        };
    }
}
