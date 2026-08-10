using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles go-to-definition requests.
/// </summary>
public sealed class DefinitionHandler : DefinitionHandlerBase
{
    private readonly WorkspaceState _workspace;

    public DefinitionHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        var snapshot = state?.Snapshot;
        if (state == null || snapshot?.Ast == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // Convert LSP position to Calor position (0-based -> 1-based)
        var (line, column) = PositionConverter.ToCalorPosition(request.Position);

        // Find symbol at position
        var result = SymbolFinder.FindSymbolAtPosition(
            snapshot.Ast,
            line,
            column,
            snapshot.Source,
            snapshot.BoundModule);
        if (result == null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        if (result.SymbolId is { } symbolId
            && snapshot.BoundModule?.SymbolsById.TryGetValue(symbolId, out var localSymbol) == true)
        {
            if (localSymbol.DeclarationSpan.Length == 0)
                return Task.FromResult<LocationOrLocationLinks?>(null);

            var definitionRange = PositionConverter.ToLspRange(
                localSymbol.DeclarationSpan,
                snapshot.Source);
            var location = new Location
            {
                Uri = request.TextDocument.Uri,
                Range = definitionRange
            };
            return Task.FromResult<LocationOrLocationLinks?>(
                new LocationOrLocationLinks(new[] { new LocationOrLocationLink(location) }));
        }

        var offset = PositionConverter.ToOffset(request.Position, snapshot.Source);
        var boundCall = SymbolFinder.FindBoundCallAtOffset(snapshot.BoundModule, offset);
        var projectCall = _workspace.ResolveProjectCall(state, snapshot, boundCall);
        if (projectCall.Doc != null
            && projectCall.Snapshot != null
            && projectCall.Symbol != null)
        {
            var location = new Location
            {
                Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(projectCall.Doc.Uri),
                Range = PositionConverter.ToLspRange(
                    projectCall.Symbol.DeclarationSpan,
                    projectCall.Snapshot.Source),
            };
            return Task.FromResult<LocationOrLocationLinks?>(
                new LocationOrLocationLinks(new[] { new LocationOrLocationLink(location) }));
        }

        if (boundCall is Calor.Compiler.Binding.BoundNewExpression creation)
        {
            var projectType = _workspace.ResolveProjectType(state, snapshot, creation);
            if (projectType.Doc != null
                && projectType.Snapshot != null
                && projectType.Symbol != null)
            {
                var location = new Location
                {
                    Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(projectType.Doc.Uri),
                    Range = PositionConverter.ToLspRange(
                        projectType.Symbol.DeclarationSpan,
                        projectType.Snapshot.Source),
                };
                return Task.FromResult<LocationOrLocationLinks?>(
                    new LocationOrLocationLinks(new[] { new LocationOrLocationLink(location) }));
            }
        }

        if (boundCall != null)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }

        // If we found a definition in the same file, return it
        if (result.DefinitionSpan != null)
        {
            var definitionRange = PositionConverter.ToLspRange(result.DefinitionSpan.Value, snapshot.Source);

            var location = new Location
            {
                Uri = request.TextDocument.Uri,
                Range = definitionRange
            };

            return Task.FromResult<LocationOrLocationLinks?>(
                new LocationOrLocationLinks(new[] { new LocationOrLocationLink(location) }));
        }

        // Try to find the definition in other open documents
        // First, check if this is a member access with a known containing type
        if (!string.IsNullOrEmpty(result.ContainingTypeName))
        {
            var (memberDoc, memberNode) = _workspace.FindMemberAcrossFiles(result.ContainingTypeName, result.Name);
            if (memberDoc != null && memberNode != null)
            {
                var memberRange = PositionConverter.ToLspRange(memberNode.Span, memberDoc.Source);

                var memberLocation = new Location
                {
                    Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(memberDoc.Uri),
                    Range = memberRange
                };

                return Task.FromResult<LocationOrLocationLinks?>(
                    new LocationOrLocationLinks(new[] { new LocationOrLocationLink(memberLocation) }));
            }
        }

        // Fall back to top-level symbol lookup
        var (crossDoc, crossNode) = _workspace.FindDefinitionAcrossFiles(result.Name);
        if (crossDoc != null && crossNode != null)
        {
            var crossRange = PositionConverter.ToLspRange(crossNode.Span, crossDoc.Source);

            var crossLocation = new Location
            {
                Uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.From(crossDoc.Uri),
                Range = crossRange
            };

            return Task.FromResult<LocationOrLocationLinks?>(
                new LocationOrLocationLinks(new[] { new LocationOrLocationLink(crossLocation) }));
        }

        return Task.FromResult<LocationOrLocationLinks?>(null);
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor")
        };
    }
}
