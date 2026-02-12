using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles text document synchronization (open, change, close).
/// </summary>
public sealed class TextDocumentSyncHandler :
    TextDocumentSyncHandlerBase
{
    private readonly WorkspaceState _workspace;
    private readonly ILanguageServerFacade _server;

    public TextDocumentSyncHandler(WorkspaceState workspace, ILanguageServerFacade server)
    {
        _workspace = workspace;
        _server = server;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "calor");
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var document = request.TextDocument;
        var state = _workspace.GetOrCreate(document.Uri, document.Text, document.Version ?? 0);

        PublishDiagnostics(document.Uri, state);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var document = request.TextDocument;

        // Get the full text from the changes (we use full sync)
        var text = request.ContentChanges.FirstOrDefault()?.Text ?? string.Empty;

        var state = _workspace.Update(document.Uri, text, document.Version ?? 0);

        PublishDiagnostics(document.Uri, state);

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        _workspace.Remove(request.TextDocument.Uri);

        // Clear diagnostics for closed document
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>()
        });

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
    {
        // Optionally re-analyze on save
        var state = _workspace.Get(request.TextDocument.Uri);
        if (state != null)
        {
            state.Reanalyze();
            PublishDiagnostics(request.TextDocument.Uri, state);
        }

        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
            Change = TextDocumentSyncKind.Full,
            Save = new SaveOptions { IncludeText = false }
        };
    }

    private void PublishDiagnostics(DocumentUri uri, DocumentState state)
    {
        var lspDiagnostics = DiagnosticConverter.ToLspDiagnostics(state.Diagnostics, state.Source);

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Version = state.Version,
            Diagnostics = new Container<Diagnostic>(lspDiagnostics)
        });
    }
}
