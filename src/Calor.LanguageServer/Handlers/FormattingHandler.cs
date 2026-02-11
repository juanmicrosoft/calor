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
        if (state?.Ast == null)
        {
            return Task.FromResult<TextEditContainer?>(null);
        }

        try
        {
            // Use the existing CalorFormatter to format the AST
            var formatter = new CalorFormatter();
            var formattedText = formatter.Format(state.Ast);

            // Calculate the range of the entire document
            var lines = state.Source.Split('\n');
            var lastLine = lines.Length - 1;
            var lastLineLength = lines[lastLine].Length;

            var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(0, 0),
                new Position(lastLine, lastLineLength)
            );

            var edit = new TextEdit
            {
                Range = range,
                NewText = formattedText
            };

            return Task.FromResult<TextEditContainer?>(new TextEditContainer(edit));
        }
        catch (Exception)
        {
            // If formatting fails, return no edits
            return Task.FromResult<TextEditContainer?>(null);
        }
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
