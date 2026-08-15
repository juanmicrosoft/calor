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

    public override async Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request,
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

        var definition = _workspace.FindSymbolDefinition(
            workspace,
            occurrence.SymbolId);
        if (definition == null)
            return null;

        var location = new Location
        {
            Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(
                definition.Doc.Uri),
            Range = PositionConverter.ToLspRange(
                definition.Span,
                definition.Snapshot.Source),
        };
        return new LocationOrLocationLinks(
            new[] { new LocationOrLocationLink(location) });
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
