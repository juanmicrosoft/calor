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

    public override Task<LocationContainer?> Handle(
        ReferenceParams request,
        CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        var snapshot = state?.Snapshot;
        if (snapshot == null)
            return Task.FromResult<LocationContainer?>(null);

        _workspace.RefreshClosedDocuments();
        var offset = PositionConverter.ToOffset(request.Position, snapshot.Source);
        var occurrence = _workspace.ResolveOccurrence(request.TextDocument.Uri, offset);
        if (occurrence == null)
            return Task.FromResult<LocationContainer?>(null);

        var locations = _workspace.FindSymbolOccurrences(
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
        return Task.FromResult<LocationContainer?>(
            locations.Length == 0 ? null : new LocationContainer(locations));
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
