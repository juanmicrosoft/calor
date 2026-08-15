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

internal sealed record DiagnosticPublication(
    DocumentUri Uri,
    Func<bool> Publish);

internal sealed class DiagnosticPublicationCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<DocumentUri, long> _publishedGenerations = [];

    internal async Task PublishAsync(
        Func<(long Generation, IReadOnlyList<DiagnosticPublication> Publications)>
            createBatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createBatch);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var batch = createBatch();
            foreach (var publication in batch.Publications)
            {
                if (_publishedGenerations.TryGetValue(
                        publication.Uri,
                        out var publishedGeneration)
                    && publishedGeneration >= batch.Generation)
                {
                    continue;
                }

                if (publication.Publish())
                {
                    _publishedGenerations[publication.Uri] = batch.Generation;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Handles text document synchronization (open, change, close).
/// </summary>
public sealed class TextDocumentSyncHandler :
    TextDocumentSyncHandlerBase
{
    private readonly WorkspaceState _workspace;
    private readonly ILanguageServerFacade _server;
    private readonly DiagnosticPublicationCoordinator _publicationCoordinator = new();

    public TextDocumentSyncHandler(WorkspaceState workspace, ILanguageServerFacade server)
    {
        _workspace = workspace;
        _server = server;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "calor");
    }

    public override async Task<Unit> Handle(
        DidOpenTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        var document = request.TextDocument;
        var accepted = await _workspace.GetOrCreateAsync(
            document.Uri,
            document.Text,
            document.Version ?? 0,
            cancellationToken).ConfigureAwait(false);
        if (accepted)
        {
            await PublishWorkspaceDiagnosticsAsync(
                [],
                cancellationToken).ConfigureAwait(false);
        }

        return Unit.Value;
    }

    public override async Task<Unit> Handle(
        DidChangeTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        var document = request.TextDocument;

        // Get the full text from the changes (we use full sync)
        var text = request.ContentChanges.FirstOrDefault()?.Text ?? string.Empty;

        var accepted = await _workspace.UpdateAsync(
            document.Uri,
            text,
            document.Version ?? 0,
            cancellationToken).ConfigureAwait(false);
        if (accepted)
        {
            await PublishWorkspaceDiagnosticsAsync(
                [],
                cancellationToken).ConfigureAwait(false);
        }

        return Unit.Value;
    }

    public override async Task<Unit> Handle(
        DidCloseTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        await _workspace.RemoveAsync(
            request.TextDocument.Uri,
            cancellationToken).ConfigureAwait(false);

        await PublishWorkspaceDiagnosticsAsync(
            [request.TextDocument.Uri],
            cancellationToken).ConfigureAwait(false);

        return Unit.Value;
    }

    public override async Task<Unit> Handle(
        DidSaveTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        var accepted = await _workspace.ReanalyzeAsync(
            request.TextDocument.Uri,
            cancellationToken).ConfigureAwait(false);
        if (accepted)
        {
            await PublishWorkspaceDiagnosticsAsync(
                [],
                cancellationToken).ConfigureAwait(false);
        }

        return Unit.Value;
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

    private Task PublishWorkspaceDiagnosticsAsync(
        IReadOnlyCollection<DocumentUri> clearUris,
        CancellationToken cancellationToken)
    {
        return _publicationCoordinator.PublishAsync(
            () =>
            {
                var workspace = _workspace.CaptureSnapshot();
                var publications = new List<DiagnosticPublication>();
                foreach (var uri in clearUris)
                {
                    if (_workspace.Contains(uri))
                        continue;
                    publications.Add(new DiagnosticPublication(
                        uri,
                        () => _workspace.TryPublishGeneration(
                            workspace,
                            () => _server.TextDocument.PublishDiagnostics(
                                new PublishDiagnosticsParams
                                {
                                    Uri = uri,
                                    Diagnostics = new Container<Diagnostic>(),
                                }))));
                }

                foreach (var document in workspace.Documents)
                {
                    var uri = DocumentUri.From(document.Document.Uri);
                    if (!_workspace.Contains(uri))
                        continue;

                    var lspDiagnostics = DiagnosticConverter.ToLspDiagnostics(
                            _workspace.GetDiagnostics(workspace, document),
                            document.Analysis.Source)
                        .ToArray();
                    publications.Add(new DiagnosticPublication(
                        uri,
                        () => _workspace.TryPublishDiagnostics(
                            workspace,
                            document,
                            () => _server.TextDocument.PublishDiagnostics(
                                new PublishDiagnosticsParams
                                {
                                    Uri = uri,
                                    Version = document.Analysis.Version,
                                    Diagnostics =
                                        new Container<Diagnostic>(lspDiagnostics),
                                }))));
                }

                return (workspace.Generation, publications);
            },
            cancellationToken);
    }
}
