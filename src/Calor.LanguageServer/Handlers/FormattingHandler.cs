using Calor.Compiler.Formatting;
using Calor.LanguageServer.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol;
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
    private readonly ILogger<FormattingHandler> _logger;

    public FormattingHandler(
        WorkspaceState workspace,
        ILogger<FormattingHandler>? logger = null)
    {
        _workspace = workspace;
        _logger = logger ?? NullLogger<FormattingHandler>.Instance;
    }

    public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state == null)
        {
            return Task.FromResult<TextEditContainer?>(null);
        }

        var snapshot = state.Snapshot;
        try
        {
            return Task.FromResult(
                FormatSnapshot(snapshot, state.Uri, request.TextDocument.Uri, _logger));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected formatting failure for {DocumentUri}.",
                request.TextDocument.Uri);
            throw;
        }
    }

    internal static TextEditContainer? FormatSnapshot(
        DocumentSnapshot snapshot,
        Uri documentUri,
        DocumentUri requestedDocumentUri,
        ILogger<FormattingHandler> logger)
    {
        if (snapshot.Ast == null || snapshot.HasErrors)
        {
            logger.LogWarning(
                "Formatting returned no edits for {DocumentUri} because the document has compiler errors.",
                requestedDocumentUri);
            return null;
        }

        var formatter = new CalorFormatter();
        var result = formatter.FormatSource(
            snapshot.Source,
            documentUri.IsFile ? documentUri.LocalPath : documentUri.ToString());
        if (!result.Success)
        {
            logger.LogError(
                "Formatting failed for {DocumentUri}: {Errors}",
                requestedDocumentUri,
                string.Join("; ", result.Errors));
            return null;
        }
        if (result.UsedConservativeFallback)
        {
            logger.LogWarning(
                "Formatting returned no edits for {DocumentUri}: {Reason}",
                requestedDocumentUri,
                result.ConservativeFallbackReason);
            return null;
        }
        if (string.Equals(result.Original, result.Formatted, StringComparison.Ordinal))
        {
            return null;
        }

        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
            new Position(0, 0),
            GetDocumentEnd(snapshot.Source));
        var edit = new TextEdit
        {
            Range = range,
            NewText = result.Formatted
        };

        return new TextEditContainer(edit);
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
