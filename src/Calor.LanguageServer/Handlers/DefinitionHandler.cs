using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles go-to-definition requests through the workspace SymbolId index.
/// </summary>
public sealed class DefinitionHandler : DefinitionHandlerBase
{
    private readonly WorkspaceState _workspace;

    public DefinitionHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request,
        CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        var snapshot = state?.Snapshot;
        if (snapshot == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        _workspace.RefreshClosedDocuments();
        var offset = PositionConverter.ToOffset(request.Position, snapshot.Source);
        var occurrence = _workspace.ResolveOccurrence(request.TextDocument.Uri, offset);
        if (occurrence == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var definition = _workspace.FindSymbolDefinition(occurrence.SymbolId);
        if (definition == null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var location = new Location
        {
            Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
                definition.Doc.Uri),
            Range = PositionConverter.ToLspRange(
                definition.Span,
                definition.Snapshot.Source),
        };
        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(
                new[] { new LocationOrLocationLink(location) }));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
        };
    }
}
