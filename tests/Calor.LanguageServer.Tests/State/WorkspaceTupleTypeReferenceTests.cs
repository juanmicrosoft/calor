using Calor.Compiler.Analysis.Dataflow;
using Calor.Compiler.Binding;
using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.State;

public class WorkspaceTupleTypeReferenceTests
{
    [Fact]
    public async Task TupleNewDocument_DoesNotBreakWorkspaceIndexOrLspOperationsAsync()
    {
        const string validSource = """
            §M{valid:TupleContainers}
              §CL{c001:List:pub}<T>
                §CTOR{ctor001:pub} ()
                  §P STR:"list"
              §CL{c002:Dictionary:pub}<TKey,TValue>
                §CTOR{ctor002:pub} ()
                  §P STR:"dictionary"
              §F{f001:MakeList:pub} () -> object
                §R §NEW{List<(i32,i32)>} §/NEW
              §F{f002:MakeDictionary:pub} () -> object
                §R §NEW{Dictionary<str,(i32,i32)>} §/NEW
            """;
        const string malformedSource = """
            §M{malformed:MalformedTupleNew}
              §F{f001:Make:pub} () -> object
                §R §NEW{(i32,i32)} §/NEW
            """;
        var validUri = DocumentUri.From("file:///valid-tuple-containers.calr");
        var malformedUri = DocumentUri.From("file:///malformed-tuple-new.calr");
        var workspace = new WorkspaceState();
        var malformedState = workspace.GetOrCreate(
            malformedUri,
            malformedSource,
            version: 3);
        var validState = workspace.GetOrCreate(
            validUri,
            validSource,
            version: 7);

        var snapshot = workspace.CaptureSnapshot();

        Assert.Equal(2, snapshot.Documents.Length);
        var malformedDocument = snapshot.GetDocument(malformedUri);
        Assert.NotNull(malformedDocument);
        Assert.Same(malformedState.Snapshot, malformedDocument.Analysis);
        var diagnostic = Assert.Single(
            workspace.GetDiagnostics(snapshot, malformedDocument),
            item => item.Code
                == Calor.Compiler.Diagnostics.DiagnosticCode.ExpectedTypeName);
        Assert.Equal(
            "§NEW requires a non-empty type name.",
            diagnostic.Message);
        var published = false;
        Assert.True(workspace.TryPublishDiagnostics(
            snapshot,
            malformedDocument,
            () => published = true));
        Assert.True(published);

        var validCreations = BoundNodeHelpers
            .DescendantsAndSelf(validState.BoundModule!)
            .OfType<BoundNewExpression>()
            .ToArray();
        var listCreation = Assert.Single(
            validCreations,
            creation => creation.TypeReference.Name == "List");
        var dictionaryCreation = Assert.Single(
            validCreations,
            creation => creation.TypeReference.Name == "Dictionary");
        Assert.Single(listCreation.TypeReference.TypeArguments);
        Assert.Equal(2, dictionaryCreation.TypeReference.TypeArguments.Count);
        Assert.Equal(
            string.Empty,
            listCreation.TypeReference.TypeArguments[0].Name);
        Assert.Equal(
            string.Empty,
            dictionaryCreation.TypeReference.TypeArguments[1].Name);
        Assert.Equal(
            "List`1",
            Assert.IsType<TypeSymbol>(
                workspace.ResolveProjectType(
                    validState,
                    validState.Snapshot,
                    listCreation).Symbol).QualifiedName);
        Assert.Equal(
            "Dictionary`2",
            Assert.IsType<TypeSymbol>(
                workspace.ResolveProjectType(
                    validState,
                    validState.Snapshot,
                    dictionaryCreation).Symbol).QualifiedName);

        var malformedCreation = Assert.Single(
            BoundNodeHelpers
                .DescendantsAndSelf(malformedState.BoundModule!)
                .OfType<BoundNewExpression>());
        Assert.Equal(string.Empty, malformedCreation.TypeReference.Name);
        Assert.Equal(2, malformedCreation.TypeReference.TypeArguments.Count);
        Assert.Null(
            workspace.ResolveProjectType(
                malformedState,
                malformedState.Snapshot,
                malformedCreation).Symbol);

        var listOffset = validSource.IndexOf(
            "List<(i32,i32)>",
            StringComparison.Ordinal);
        var definition = await FindDefinitionAsync(
            workspace,
            validUri,
            validSource,
            listOffset);
        Assert.NotNull(definition);
        var definitionLocation = Assert.Single(definition).Location;
        Assert.NotNull(definitionLocation);
        Assert.Equal(
            validSource.IndexOf("List:pub", StringComparison.Ordinal),
            PositionConverter.ToOffset(
                definitionLocation!.Range.Start,
                validSource));

        var references = await FindReferencesAsync(
            workspace,
            validUri,
            validSource,
            listOffset);
        Assert.Equal(2, references.Length);
        Assert.All(
            references,
            location => Assert.Equal(
                "List",
                TextAt(validSource, location.Range)));

        var rename = await RenameAsync(
            workspace,
            validUri,
            validSource,
            listOffset,
            "PairList");
        Assert.NotNull(rename);
        var documentChange = Assert.Single(
            Assert.IsType<Container<WorkspaceEditDocumentChange>>(
                rename.DocumentChanges));
        var documentEdit = Assert.IsType<TextDocumentEdit>(
            documentChange.TextDocumentEdit);
        Assert.Equal(7, documentEdit.TextDocument.Version);
        Assert.Equal(2, documentEdit.Edits.Count());
        Assert.All(
            documentEdit.Edits,
            edit => Assert.Equal("List", TextAt(validSource, edit.Range)));

        var tupleElementOffset = malformedSource.IndexOf(
            "i32",
            StringComparison.Ordinal);
        Assert.Null(await FindDefinitionAsync(
            workspace,
            malformedUri,
            malformedSource,
            tupleElementOffset));
        Assert.Empty(await FindReferencesAsync(
            workspace,
            malformedUri,
            malformedSource,
            tupleElementOffset));
        Assert.Null(await RenameAsync(
            workspace,
            malformedUri,
            malformedSource,
            tupleElementOffset,
            "Value"));
    }

    private static async Task<LocationOrLocationLinks?> FindDefinitionAsync(
        WorkspaceState workspace,
        DocumentUri uri,
        string source,
        int offset)
    {
        var (line, column) = LspTestHarness.GetLineColumn(source, offset);
        return await new DefinitionHandler(workspace).Handle(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
            },
            CancellationToken.None);
    }

    private static async Task<Location[]> FindReferencesAsync(
        WorkspaceState workspace,
        DocumentUri uri,
        string source,
        int offset)
    {
        var (line, column) = LspTestHarness.GetLineColumn(source, offset);
        var result = await new ReferencesHandler(workspace).Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
                Context = new ReferenceContext { IncludeDeclaration = true },
            },
            CancellationToken.None);
        return result?.ToArray() ?? [];
    }

    private static async Task<WorkspaceEdit?> RenameAsync(
        WorkspaceState workspace,
        DocumentUri uri,
        string source,
        int offset,
        string newName)
    {
        var (line, column) = LspTestHarness.GetLineColumn(source, offset);
        return await new RenameHandler(workspace).Handle(
            new RenameParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
                NewName = newName,
            },
            CancellationToken.None);
    }

    private static string TextAt(
        string source,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
    {
        var start = PositionConverter.ToOffset(range.Start, source);
        var end = PositionConverter.ToOffset(range.End, source);
        return source[start..end];
    }
}
