using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class ReferencesHandlerTests
{
    [Fact]
    public async Task NestedShadowedLocalsRemainSeparatedBySymbolIdAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §F{f001:Test:pub} (bool:flag) -> i32
                §B{value:i32} INT:1
                §IF{nested} (== flag BOOL:true)
                  §B{value:i32} INT:2
                  §R value
                §R value
            """;
        var uri = DocumentUri.From("file:///references-shadow.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);

        var outer = await FindReferencesAsync(
            workspace,
            uri,
            source,
            source.IndexOf("§B{value", StringComparison.Ordinal) + "§B{".Length);
        var innerOffset = source.LastIndexOf("§B{value", StringComparison.Ordinal)
            + "§B{".Length;
        var inner = await FindReferencesAsync(
            workspace,
            uri,
            source,
            innerOffset);

        Assert.Equal(2, outer.Length);
        Assert.Equal(2, inner.Length);
        Assert.DoesNotContain(
            outer,
            location => PositionConverter.ToOffset(location.Range.Start, source)
                == innerOffset);
        Assert.DoesNotContain(
            inner,
            location => PositionConverter.ToOffset(location.Range.Start, source)
                == source.IndexOf("§B{value", StringComparison.Ordinal) + "§B{".Length);
        Assert.All(outer.Concat(inner), location =>
            Assert.Equal("value", TextAt(source, location.Range)));
    }

    [Fact]
    public async Task SameMethodNameOnUnrelatedTypesDoesNotCollideAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §CL{c001:First:pub}
                §MT{m001:Run:pub} () -> i32
                  §R INT:1
              §CL{c002:Second:pub}
                §MT{m002:Run:pub} () -> i32
                  §R INT:2
              §F{f001:Use:pub} () -> i32
                §B{first:First} §NEW{First} §/NEW
                §R §C{first.Run} §/C
            """;
        var uri = DocumentUri.From("file:///references-method-collision.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var firstRun = source.IndexOf("Run:pub", StringComparison.Ordinal);
        var references = await FindReferencesAsync(
            workspace,
            uri,
            source,
            firstRun);

        Assert.Equal(2, references.Length);
        Assert.All(references, location => Assert.Equal("Run", TextAt(source, location.Range)));
        Assert.DoesNotContain(
            references,
            location => PositionConverter.ToOffset(location.Range.Start, source)
                == source.LastIndexOf("Run:pub", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OverloadsResolveToExactSignatureAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §F{f001:Pick:pub} (i32:value) -> i32
                §R value
              §F{f002:Pick:pub} (str:value) -> str
                §R value
              §F{f003:Use:pub} () -> i32
                §R §C{Pick} §A INT:1 §/C
            """;
        var uri = DocumentUri.From("file:///references-overloads.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var references = await FindReferencesAsync(
            workspace,
            uri,
            source,
            source.IndexOf("Pick:pub", StringComparison.Ordinal));

        Assert.Equal(2, references.Length);
        Assert.DoesNotContain(
            references,
            location => PositionConverter.ToOffset(location.Range.Start, source)
                == source.LastIndexOf("Pick:pub", StringComparison.Ordinal));
        Assert.All(references, location => Assert.Equal("Pick", TextAt(source, location.Range)));
    }

    [Fact]
    public async Task ClosedWorkspaceFilesAreIncludedAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            const string definition = """
                §M{defs:Collision}
                  §CL{worker1:Worker:pub:partial}
                    §MT{pick:Pick:pub} (i32:value) -> i32
                      §R value
                """;
            const string openUse = """
                §M{openUse:Collision}
                  §CL{worker2:Worker:pub:partial}
                    §MT{use:Use:pub} () -> i32
                      §R §C{Pick} §A INT:1 §/C
                """;
            const string closedUse = """
                §M{closedUse:Collision}
                  §CL{worker3:Worker:pub:partial}
                    §MT{use:UseAgain:pub} () -> i32
                      §R §C{Pick} §A INT:2 §/C
                """;
            File.WriteAllText(Path.Combine(root, "definition.calr"), definition);
            File.WriteAllText(Path.Combine(root, "open.calr"), openUse);
            File.WriteAllText(Path.Combine(root, "closed.calr"), closedUse);

            var workspace = new WorkspaceState(root);
            var openUri = DocumentUri.FromFileSystemPath(Path.Combine(root, "open.calr"));
            workspace.GetOrCreate(openUri, openUse, version: 4);
            var references = await FindReferencesAsync(
                workspace,
                openUri,
                openUse,
                openUse.IndexOf("Pick", StringComparison.Ordinal));

            Assert.Equal(3, references.Length);
            Assert.Equal(
                3,
                references.Select(location => location.Uri.ToUri().LocalPath)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ClosedWorkspaceIndexInvalidatesSameLengthChangesAndDeletesAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            const string originalDefinition = """
                §M{defs:Collision}
                  §CL{c001:Worker:pub:partial}
                    §MT{m001:Pick:pub} (i32:value) -> i32
                      §R value
                """;
            const string changedDefinition = """
                §M{defs:Collision}
                  §CL{c001:Worker:pub:partial}
                    §MT{m001:Pock:pub} (i32:value) -> i32
                      §R value
                """;
            const string originalUse = """
                §M{use:Collision}
                  §CL{c002:Worker:pub:partial}
                    §MT{m002:Use:pub} () -> i32
                      §R §C{Pick} §A INT:1 §/C
                """;
            const string changedUse = """
                §M{use:Collision}
                  §CL{c002:Worker:pub:partial}
                    §MT{m002:Use:pub} () -> i32
                      §R §C{Pock} §A INT:1 §/C
                """;
            var definitionPath = Path.Combine(root, "definition.calr");
            var usePath = Path.Combine(root, "use.calr");
            File.WriteAllText(definitionPath, originalDefinition);
            File.WriteAllText(usePath, originalUse);
            var workspace = new WorkspaceState(root);
            var useUri = DocumentUri.FromFileSystemPath(usePath);
            workspace.GetOrCreate(useUri, originalUse, version: 1);

            Assert.Equal(
                2,
                (await FindReferencesAsync(
                    workspace,
                    useUri,
                    originalUse,
                    originalUse.IndexOf("Pick", StringComparison.Ordinal))).Length);

            var occurrence = workspace.ResolveOccurrence(
                useUri,
                originalUse.IndexOf("Pick", StringComparison.Ordinal));
            Assert.NotNull(occurrence);
            var originalSnapshots = workspace.FindSymbolOccurrences(
                occurrence!.SymbolId,
                includeDeclaration: true);
            File.WriteAllText(definitionPath, changedDefinition);
            Assert.False(workspace.AreOccurrenceSnapshotsCurrent(originalSnapshots));

            workspace.Update(useUri, changedUse, version: 2);
            var changedReferences = await FindReferencesAsync(
                workspace,
                useUri,
                changedUse,
                changedUse.IndexOf("Pock", StringComparison.Ordinal));
            Assert.Equal(2, changedReferences.Length);
            Assert.Contains(
                changedReferences,
                location => location.Uri.ToUri().LocalPath == definitionPath
                    && TextAt(changedDefinition, location.Range) == "Pock");

            File.Delete(definitionPath);
            Assert.Empty(await FindReferencesAsync(
                workspace,
                useUri,
                changedUse,
                changedUse.IndexOf("Pock", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WhitespaceAndEndAdjacentCursorsReturnNoReferencesAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §F{f001:Compute:pub} () -> i32
                §R INT:1
            """;
        var uri = DocumentUri.From("file:///references-cursor.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var identifier = source.IndexOf("Compute", StringComparison.Ordinal);

        Assert.Empty(await FindReferencesAsync(
            workspace,
            uri,
            source,
            identifier - 1));
        Assert.Empty(await FindReferencesAsync(
            workspace,
            uri,
            source,
            identifier + "Compute".Length));
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

    private static string TextAt(
        string source,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
    {
        var start = PositionConverter.ToOffset(range.Start, source);
        var end = PositionConverter.ToOffset(range.End, source);
        return source[start..end];
    }

    private static string CreateWorkspaceDirectory()
    {
        var directory = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(directory, "Calor.sln")))
            directory = Directory.GetParent(directory)!.FullName;

        var root = Path.Combine(
            directory,
            "artifacts",
            "lsp-refactoring-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
