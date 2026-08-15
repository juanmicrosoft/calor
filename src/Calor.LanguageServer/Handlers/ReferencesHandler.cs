using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles find-all-references requests through the workspace SymbolId index.
/// </summary>
public sealed class ReferencesHandler : ReferencesHandlerBase
{
    private readonly WorkspaceState _workspace;

    public ReferencesHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override async Task<LocationContainer?> Handle(
        ReferenceParams request,
        CancellationToken cancellationToken)
    {
        var workspace = await _workspace.CaptureSnapshotAsync(
            refreshClosedDocuments: true,
            cancellationToken).ConfigureAwait(false);
        var document = workspace.GetDocument(request.TextDocument.Uri);
        if (document == null)
            return null;

        var offset = PositionConverter.ToOffset(
            request.Position,
            document.Analysis.Source);
        var occurrence = _workspace.ResolveOccurrence(
            workspace,
            request.TextDocument.Uri,
            offset);
        if (occurrence == null)
            return null;

        var locations = _workspace.FindSymbolOccurrences(
                workspace,
                occurrence.SymbolId,
                request.Context.IncludeDeclaration)
            .Select(reference => new Location
            {
                Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
                    reference.Doc.Uri),
                Range = PositionConverter.ToLspRange(
                    reference.Span,
                    reference.Snapshot.Source),
            })
            .ToArray();
        return locations.Length == 0 ? null : new LocationContainer(locations);
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new ReferenceRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
        };
    }
}
