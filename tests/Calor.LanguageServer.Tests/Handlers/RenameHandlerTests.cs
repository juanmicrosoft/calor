using Calor.Compiler.CodeGen;
using Calor.LanguageServer.Handlers;
using Calor.LanguageServer.State;
using Calor.LanguageServer.Tests.Helpers;
using Calor.LanguageServer.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Xunit;

namespace Calor.LanguageServer.Tests.Handlers;

public class RenameHandlerTests
{
    [Fact]
    public async Task RenameUsesVersionedDocumentChangesAndExactTokenRangesAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §F{f001:Compute:pub} () -> i32
                §R 42
              §F{f002:Use:pub} () -> i32
                §R §C{Compute} §/C
            """;
        var uri = DocumentUri.From("file:///rename.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source, version: 7);
        var edit = await RenameAtAsync(workspace, uri, source, "Compute", "Calculate");

        Assert.NotNull(edit);
        Assert.Null(edit.Changes);
        var change = Assert.Single(Assert.IsType<Container<WorkspaceEditDocumentChange>>(
            edit.DocumentChanges));
        var documentEdit = Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit);
        Assert.Equal(7, documentEdit.TextDocument.Version);
        Assert.Equal(uri, documentEdit.TextDocument.Uri);
        var edits = documentEdit.Edits.ToArray();
        Assert.Equal(2, edits.Length);
        Assert.All(edits, textEdit =>
        {
            Assert.Equal("Compute", TextAt(source, textEdit.Range));
            Assert.Equal("Calculate", textEdit.NewText);
        });
    }

    [Fact]
    public async Task RenameInheritedFieldAccessesEditsOnlyFieldIdentifiersAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §CL{c001:Base:pub}
                §FLD{i32:shared:prot}
              §CL{c002:Derived:Base:pub}
                §MT{m001:Use:pub} () -> i32
                  §R (+ §THIS.shared §BASE.shared)
            """;
        var uri = DocumentUri.From("file:///rename-field.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var edit = await RenameAtAsync(
            workspace,
            uri,
            source,
            "shared:prot",
            "renamed");

        Assert.NotNull(edit);
        var change = Assert.Single(Assert.IsType<Container<WorkspaceEditDocumentChange>>(
            edit.DocumentChanges));
        var documentEdit = Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit);
        var edits = documentEdit.Edits.ToArray();
        Assert.Equal(3, edits.Length);
        Assert.All(edits, textEdit => Assert.Equal("shared", TextAt(source, textEdit.Range)));
    }

    [Fact]
    public async Task RenameTypeDoesNotEditQualifiedExternalTypeAsync()
    {
        const string source = """
            §M{m001:TestModule}
              §CL{c001:Exception:pub}
              §CL{c002:Holder:pub}
                §FLD{Exception:local:priv}
                §FLD{System.Exception:external:priv}
            """;
        var uri = DocumentUri.From("file:///rename-qualified-type.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var edit = await RenameAtAsync(
            workspace,
            uri,
            source,
            "Exception:pub",
            "LocalException");

        Assert.NotNull(edit);
        var change = Assert.Single(Assert.IsType<Container<WorkspaceEditDocumentChange>>(
            edit.DocumentChanges));
        var documentEdit = Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit);
        var edits = documentEdit.Edits.ToArray();
        Assert.Equal(2, edits.Length);
        Assert.All(edits, textEdit => Assert.Equal("Exception", TextAt(source, textEdit.Range)));
        Assert.DoesNotContain(
            edits,
            textEdit => PositionConverter.ToOffset(textEdit.Range.Start, source)
                == source.LastIndexOf("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RenameModuleUsesStableModuleSymbolIdAndExactNameSpanAsync()
    {
        const string source = """
            §M{m001:OriginalModule}
              §F{f001:Run:pub} () -> i32
                §R INT:1
            """;
        var uri = DocumentUri.From("file:///rename-module.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source, version: 3);
        var edit = await RenameAtAsync(
            workspace,
            uri,
            source,
            "OriginalModule",
            "RenamedModule");

        Assert.NotNull(edit);
        var change = Assert.Single(Assert.IsType<Container<WorkspaceEditDocumentChange>>(
            edit.DocumentChanges));
        var documentEdit = Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit);
        var textEdit = Assert.Single(documentEdit.Edits);
        Assert.Equal("OriginalModule", TextAt(source, textEdit.Range));
        Assert.Equal(3, documentEdit.TextDocument.Version);
    }

    [Fact]
    public async Task RenameTypeIncludesClosedDefinitionAndOpenReferencesAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            const string definition = """
                §M{m001:Models}
                  §CL{c001:Widget:pub}
                """;
            const string use = """
                §M{m002:App}
                  §F{f001:Make:pub} () -> Widget
                    §R §NEW{Widget} §/NEW
                """;
            var definitionPath = Path.Combine(root, "definition.calr");
            var usePath = Path.Combine(root, "use.calr");
            File.WriteAllText(definitionPath, definition);
            File.WriteAllText(usePath, use);
            var workspace = new WorkspaceState(root);
            var useUri = DocumentUri.FromFileSystemPath(usePath);
            workspace.GetOrCreate(useUri, use, version: 5);
            var edit = await RenameAtAsync(
                workspace,
                useUri,
                use,
                "Widget} §/NEW",
                "Gadget");

            Assert.NotNull(edit);
            var documentEdits = Assert.IsType<Container<WorkspaceEditDocumentChange>>(
                    edit.DocumentChanges)
                .Select(change => Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit))
                .ToArray();
            Assert.Equal(2, documentEdits.Length);
            Assert.Equal(3, documentEdits.Sum(documentEdit => documentEdit.Edits.Count()));
            Assert.Contains(
                documentEdits,
                documentEdit => documentEdit.TextDocument.Uri == useUri
                    && documentEdit.TextDocument.Version == 5);
            Assert.Contains(
                documentEdits,
                documentEdit => documentEdit.TextDocument.Uri.ToUri().LocalPath
                        == definitionPath
                    && documentEdit.TextDocument.Version == null);
            foreach (var documentEdit in documentEdits)
            {
                var source = documentEdit.TextDocument.Uri == useUri ? use : definition;
                Assert.All(
                    documentEdit.Edits,
                    textEdit => Assert.Equal("Widget", TextAt(source, textEdit.Range)));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task RenameDoesNotResolveAdjacentOrWhitespaceCursorsAsync(int relativeOffset)
    {
        const string source = """
            §M{m001:TestModule}
              §F{f001:Compute:pub} () -> i32
                §R 42
            """;
        var uri = DocumentUri.From("file:///rename-cursor.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var identifier = source.IndexOf("Compute", StringComparison.Ordinal);
        var cursor = identifier + relativeOffset;
        var (line, column) = LspTestHarness.GetLineColumn(source, cursor);
        var edit = await new RenameHandler(workspace).Handle(
            new RenameParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
                NewName = "Calculate",
            },
            CancellationToken.None);

        Assert.Null(edit);
    }

    private static async Task<WorkspaceEdit?> RenameAtAsync(
        WorkspaceState workspace,
        DocumentUri uri,
        string source,
        string cursorText,
        string newName)
    {
        var offset = source.IndexOf(cursorText, StringComparison.Ordinal);
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

    private static string TextAt(string source, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
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

public sealed class ExactSpanRefactoringGateTests
{
    [Fact]
    public async Task AdversarialWorkspaceRenameAppliesAndCompilesRoslynCleanAsync()
    {
        var serverAssembly = typeof(RenameHandler).Assembly;
        Assert.Null(serverAssembly.GetType(
            "Calor.LanguageServer.Handlers.ReferenceCollector"));
        Assert.Null(serverAssembly.GetType(
            "Calor.LanguageServer.Handlers.ReferenceCollectorForRename"));
        var programSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Calor.LanguageServer",
            "Program.cs"));
        Assert.Contains(".WithHandler<RenameHandler>()", programSource);
        Assert.DoesNotContain("CALOR_LSP_EXPERIMENTAL", programSource);
        foreach (var handlerName in new[]
                 {
                     "DefinitionHandler.cs",
                     "ReferencesHandler.cs",
                     "RenameHandler.cs",
                 })
        {
            var handlerSource = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "Calor.LanguageServer",
                "Handlers",
                handlerName));
            Assert.Contains("ResolveOccurrence", handlerSource);
            Assert.DoesNotContain("FindSymbolAtPosition", handlerSource);
            Assert.DoesNotContain("FindBoundReferences", handlerSource);
            Assert.DoesNotContain("ReferenceCollector", handlerSource);
        }

        var root = CreateWorkspaceDirectory();
        try
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["definitions.calr"] = """
                    §M{defs:Collision}
                      §CL{c001:Worker:pub:partial}
                        §FLD{i32:value:pub}
                        §MT{m001:Pick:pub} (i32:value) -> i32
                          §IF{nested} (== value INT:0)
                            §B{result:i32} INT:10
                            §R result
                          §EL
                            §B{result:i32} value
                            §R result
                        §MT{m002:Pick:pub} (str:value) -> i32
                          §R INT:2
                        §MT{m003:Run:pub} () -> i32
                          §B{value:i32} INT:3
                          §R (+ value §THIS.value)
                        §CL{c005:Nested:pub}
                          §MT{m005:Run:pub} () -> i32
                            §B{result:i32} INT:4
                            §R result
                    """,
                ["open-use.calr"] = """
                    §M{openUse:Collision}
                      §CL{c002:Worker:pub:partial}
                        §MT{m010:UseOne:pub} () -> i32
                          §R §C{Pick} §A INT:1 §/C
                    """,
                ["closed-use.calr"] = """
                    §M{closedUse:Collision}
                      §CL{c003:Worker:pub:partial}
                        §MT{m011:UseTwo:pub} () -> i32
                          §R §C{Pick} §A INT:2 §/C
                    """,
                ["collisions.calr"] = """
                    §M{other:Collision}
                      §CL{c004:OtherType:pub}
                        §FLD{i32:value:pub}
                        §MT{m020:Pick:pub} (str:value) -> i32
                          §R INT:3
                        §MT{m021:Run:pub} (i32:value) -> i32
                          §R value
                    """,
            };
            foreach (var (fileName, source) in sources)
                File.WriteAllText(Path.Combine(root, fileName), source);

            var workspace = new WorkspaceState(root);
            var openPath = Path.Combine(root, "open-use.calr");
            var openUri = DocumentUri.FromFileSystemPath(openPath);
            workspace.GetOrCreate(openUri, sources["open-use.calr"], version: 11);
            var edit = await RenameAtAsync(
                workspace,
                openUri,
                sources["open-use.calr"],
                "Pick",
                "Choose");

            Assert.NotNull(edit);
            Assert.Null(edit.Changes);
            var changes = Assert.IsType<Container<WorkspaceEditDocumentChange>>(
                    edit.DocumentChanges)
                .Select(change => Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit))
                .ToArray();
            Assert.Equal(3, changes.Length);
            Assert.Contains(
                changes,
                documentEdit => documentEdit.TextDocument.Uri == openUri
                    && documentEdit.TextDocument.Version == 11);
            Assert.Equal(
                2,
                changes.Count(documentEdit =>
                    documentEdit.TextDocument.Uri != openUri
                    && documentEdit.TextDocument.Version == null));

            var updated = sources.ToDictionary(
                pair => Path.Combine(root, pair.Key),
                pair => pair.Value,
                StringComparer.Ordinal);
            foreach (var documentEdit in changes)
            {
                var path = documentEdit.TextDocument.Uri.ToUri().LocalPath;
                var source = updated[path];
                foreach (var textEdit in documentEdit.Edits)
                {
                    Assert.Equal("Pick", TextAt(source, textEdit.Range));
                    var start = PositionConverter.ToOffset(textEdit.Range.Start, source);
                    var end = PositionConverter.ToOffset(textEdit.Range.End, source);
                    source = source[..start] + textEdit.NewText + source[end..];
                }
                updated[path] = source;
                if (documentEdit.TextDocument.Version == null)
                    File.WriteAllText(path, source);
                else
                    workspace.Update(documentEdit.TextDocument.Uri, source, version: 12);
            }

            Assert.Equal(3, updated.Values.Sum(source => Count(source, "Choose")));
            Assert.Equal(
                1,
                Count(updated[Path.Combine(root, "definitions.calr")], ":Pick:pub"));
            Assert.Equal(
                1,
                Count(updated[Path.Combine(root, "collisions.calr")], ":Pick:pub"));
            Assert.Contains("§MT{m003:Run:pub}", updated[Path.Combine(root, "definitions.calr")]);
            Assert.Contains("§MT{m021:Run:pub}", updated[Path.Combine(root, "collisions.calr")]);

            var syntaxTrees = new List<SyntaxTree>();
            foreach (var (path, source) in updated.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var state = LspTestHarness.CreateDocument(
                    source,
                    new Uri(path).AbsoluteUri);
                Assert.NotNull(state.Ast);
                Assert.NotNull(state.BoundModule);
                Assert.False(
                    state.Diagnostics.HasErrors,
                    string.Join(
                        Environment.NewLine,
                        state.Diagnostics.Select(diagnostic =>
                            $"{diagnostic.Code}: {diagnostic.Message}")));
                var generated = new CSharpEmitter().Emit(state.Ast!);
                var syntaxTree = CSharpSyntaxTree.ParseText(generated, path: path + ".cs");
                Assert.DoesNotContain(
                    syntaxTree.GetDiagnostics(),
                    diagnostic => diagnostic.Severity
                        == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
                syntaxTrees.Add(syntaxTree);
            }

            var compilation = CSharpCompilation.Create(
                "ExactSpanRefactoringGate",
                syntaxTrees,
                GetPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.DoesNotContain(
                compilation.GetDiagnostics(),
                diagnostic => diagnostic.Severity
                    == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // A module, and a type declared across several files, are one declaration in
    // the language but one SymbolId per file in the index. Renaming from a
    // file-local occurrence set edits a single part and splits the declaration —
    // and Calor reports no error, so the break surfaces only in generated C#
    // (CS0103 on the other part's members). Rename must refuse instead.
    [Theory]
    [InlineData("Collision", "Renamed")]
    [InlineData("Worker", "Employee")]
    public async Task RenameRefusesDeclarationsSplitAcrossFilesAsync(
        string cursorText,
        string newName)
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["definitions.calr"] = """
                    §M{defs:Collision}
                      §CL{c001:Worker:pub:partial}
                        §MT{m001:Pick:pub} (i32:value) -> i32
                          §R value
                    """,
                ["open-use.calr"] = """
                    §M{openUse:Collision}
                      §CL{c002:Worker:pub:partial}
                        §MT{m010:UseOne:pub} () -> i32
                          §R §C{Pick} §A INT:1 §/C
                    """,
            };
            foreach (var (fileName, source) in sources)
                File.WriteAllText(Path.Combine(root, fileName), source);

            var workspace = new WorkspaceState(root);
            var openUri = DocumentUri.FromFileSystemPath(
                Path.Combine(root, "definitions.calr"));
            workspace.GetOrCreate(openUri, sources["definitions.calr"], version: 1);

            Assert.Null(await RenameAtAsync(
                workspace,
                openUri,
                sources["definitions.calr"],
                cursorText,
                newName));

            // The refusal must be specific to split declarations: a method declared
            // in one file still renames across the workspace.
            Assert.NotNull(await RenameAtAsync(
                workspace,
                openUri,
                sources["definitions.calr"],
                "Pick",
                "Choose"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    // A module emits a C# namespace that another file can import. Using directives
    // are not indexed as occurrences, so renaming an imported module would leave the
    // importer pointing at a namespace that no longer exists.
    [Fact]
    public async Task RenameRefusesModuleImportedByAnotherFileAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["models.calr"] = """
                    §M{m001:Models}
                      §CL{c001:Widget:pub}
                    """,
                ["app.calr"] = """
                    §M{m002:App}
                      §U{Models}
                      §F{f001:Make:pub} () -> Widget
                        §R §NEW{Widget} §/NEW
                    """,
            };
            foreach (var (fileName, source) in sources)
                File.WriteAllText(Path.Combine(root, fileName), source);

            var workspace = new WorkspaceState(root);
            var openUri = DocumentUri.FromFileSystemPath(Path.Combine(root, "models.calr"));
            workspace.GetOrCreate(openUri, sources["models.calr"], version: 1);

            Assert.Null(await RenameAtAsync(
                workspace,
                openUri,
                sources["models.calr"],
                "Models",
                "Domain"));

            // The importing module is not imported anywhere, so it still renames.
            var appUri = DocumentUri.FromFileSystemPath(Path.Combine(root, "app.calr"));
            workspace.GetOrCreate(appUri, sources["app.calr"], version: 1);
            Assert.NotNull(await RenameAtAsync(
                workspace,
                appUri,
                sources["app.calr"],
                "App",
                "Application"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<WorkspaceEdit?> RenameAtAsync(
        WorkspaceState workspace,
        DocumentUri uri,
        string source,
        string cursorText,
        string newName)
    {
        var offset = source.IndexOf(cursorText, StringComparison.Ordinal);
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

    private static string TextAt(string source, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
    {
        var start = PositionConverter.ToOffset(range.Start, source);
        var end = PositionConverter.ToOffset(range.End, source);
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0;
             (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
        {
            count++;
        }
        return count;
    }

    private static string CreateWorkspaceDirectory()
    {
        var root = Path.Combine(
            FindRepositoryRoot(),
            "artifacts",
            "lsp-refactoring-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string FindRepositoryRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(directory, "Calor.sln")))
            directory = Directory.GetParent(directory)!.FullName;
        return directory;
    }

    private static IEnumerable<MetadataReference> GetPlatformReferences()
    {
        var trustedAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return trustedAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
    }
}
