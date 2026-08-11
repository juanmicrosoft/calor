using Calor.LanguageServer.State;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace Calor.LanguageServer.Handlers;

/// <summary>
/// Handles identity-safe rename requests through the workspace SymbolId index.
/// </summary>
public sealed class RenameHandler : RenameHandlerBase
{
    private readonly WorkspaceState _workspace;

    public RenameHandler(WorkspaceState workspace)
    {
        _workspace = workspace;
    }

    public override Task<WorkspaceEdit?> Handle(
        RenameParams request,
        CancellationToken cancellationToken)
    {
        var state = _workspace.Get(request.TextDocument.Uri);
        var snapshot = state?.Snapshot;
        if (snapshot == null
            || string.IsNullOrWhiteSpace(request.NewName)
            || !IsValidIdentifier(request.NewName))
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }

        var offset = PositionConverter.ToOffset(request.Position, snapshot.Source);
        var occurrence = _workspace.ResolveOccurrence(request.TextDocument.Uri, offset);
        if (occurrence == null)
            return Task.FromResult<WorkspaceEdit?>(null);

        var oldName = occurrence.Snapshot.Source.Substring(
            occurrence.Span.Start,
            occurrence.Span.Length);
        if (string.Equals(oldName, request.NewName, StringComparison.Ordinal))
            return Task.FromResult<WorkspaceEdit?>(null);

        var occurrences = _workspace.FindSymbolOccurrences(
            occurrence.SymbolId,
            includeDeclaration: true);
        if (occurrences.Count == 0
            || occurrences.Any(item =>
                !IsExactIdentifierSpan(item.Snapshot.Source, item.Span, oldName))
            || !_workspace.AreOccurrenceSnapshotsCurrent(occurrences))
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }

        var documentChanges = occurrences
            .GroupBy(item => DocumentUri.From(item.Doc.Uri))
            .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var edits = group
                    .OrderByDescending(item => item.Span.Start)
                    .Select(item => new TextEdit
                    {
                        Range = PositionConverter.ToLspRange(
                            item.Span,
                            item.Snapshot.Source),
                        NewText = request.NewName,
                    })
                    .ToArray();
                return new WorkspaceEditDocumentChange(new TextDocumentEdit
                {
                    TextDocument = new OptionalVersionedTextDocumentIdentifier
                    {
                        Uri = group.Key,
                        Version = first.IsOpen ? first.Snapshot.Version : null,
                    },
                    Edits = new TextEditContainer(edits),
                });
            })
            .ToArray();

        return Task.FromResult<WorkspaceEdit?>(
            new WorkspaceEdit
            {
                DocumentChanges = new Container<WorkspaceEditDocumentChange>(
                    documentChanges),
            });
    }

    private static bool IsExactIdentifierSpan(
        string source,
        Calor.Compiler.Parsing.TextSpan span,
        string identifier)
    {
        return span.Length == identifier.Length
            && span.Start >= 0
            && span.End <= source.Length
            && source.AsSpan(span.Start, span.Length).SequenceEqual(identifier.AsSpan());
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)
            || (!char.IsLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        return name.Skip(1).All(character =>
            char.IsLetterOrDigit(character) || character == '_');
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new RenameRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("calor"),
            PrepareProvider = false,
        };
    }
}
