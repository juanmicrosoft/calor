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
                §M{m002:Models}
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

    [Fact]
    public async Task GenericModuleFunctionRefactoringUsesBaseIdentifierAndCompilesAsync()
    {
        const string source = """
            §M{m001:GenericModule}
              §F{f001:Identity<T>:pub} (T:value) -> T
                §R value
              §F{f002:Use:pub} () -> i32
                §R §C{Identity<i32>} §A INT:1 §/C
            """;
        var uri = DocumentUri.From("file:///generic-refactor.calr");
        var workspace = new WorkspaceState();
        var state = workspace.GetOrCreate(uri, source, version: 4);
        var function = Assert.Single(state.Ast!.Functions, function =>
            function.Name == "Identity<T>");
        Assert.Equal("Identity", source[function.IdentifierSpan.Start..function.IdentifierSpan.End]);

        var callOffset = source.IndexOf("Identity<i32>", StringComparison.Ordinal);
        var (line, column) = LspTestHarness.GetLineColumn(source, callOffset);
        var definition = await new DefinitionHandler(workspace).Handle(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
            },
            CancellationToken.None);
        var definitionLocation = Assert.Single(definition!).Location!;
        Assert.Equal("Identity", TextAt(source, definitionLocation.Range));

        var references = await new ReferencesHandler(workspace).Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
                Context = new ReferenceContext { IncludeDeclaration = true },
            },
            CancellationToken.None);
        Assert.Equal(2, references!.Count());
        Assert.All(references!, location => Assert.Equal("Identity", TextAt(source, location.Range)));

        var edit = await RenameAtAsync(workspace, uri, source, "Identity<i32>", "Transform");
        Assert.NotNull(edit);
        var updated = ApplySingleDocumentEdit(source, edit!);
        Assert.Contains("§F{f001:Transform<T>:pub}", updated);
        Assert.Contains("§C{Transform<i32>}", updated);
        AssertRoslynClean(updated);
    }

    [Fact]
    public async Task TypeRenamesCoverStaticCallsAndTypeBearingPositionsAsync()
    {
        const string source = """
            §M{m001:TypeRefactor}
              §IFACE{i001:IWidget}
                §MT{m000:Marker} () -> void
              §CL{c001:WidgetException:pub}
                §EXT{System.Exception}
                §IMPL{IWidget}
                §MT{m001:Create:pub:static} () -> WidgetException
                  §R §NEW{WidgetException} §/NEW
                §MT{m002:Marker:pub} () -> void
                  §P STR:"marker"
              §CL{c002:DerivedException:pub}
                §EXT{WidgetException}
                §FLD{i32:Code:pub}
              §F{f001:Use:pub} (object:value) -> WidgetException
                §ARR{items:WidgetException:1}
                §LIST{values:WidgetException}
                  §NEW{WidgetException} §/NEW
                §/LIST{values}
                §TR{t001}
                  §R (cast WidgetException value)
                §CA{WidgetException:ex}
                  §R §C{WidgetException.Create} §/C
                §/TR{t001}
            """;
        var uri = DocumentUri.From("file:///type-refactor.calr");
        var workspace = new WorkspaceState();
        var state = workspace.GetOrCreate(uri, source, version: 1);
        Assert.False(
            state.Diagnostics.HasErrors,
            string.Join(Environment.NewLine, state.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var classOffset = source.IndexOf(
            "WidgetException:pub",
            StringComparison.Ordinal);
        var classOccurrence = workspace.ResolveOccurrence(uri, classOffset);
        Assert.NotNull(classOccurrence);
        Assert.NotNull(workspace.FindSymbolDefinition(classOccurrence!.SymbolId));
        Assert.Contains(
            workspace.FindSymbolOccurrences(classOccurrence.SymbolId, includeDeclaration: true),
            occurrence => occurrence.Kind == SymbolOccurrenceKind.Definition);
        var typeIndex = TypeReferenceIndex.BuildDetailed(
            state.Ast!,
            state.BoundModule!,
            source,
            state.BoundModule!.SymbolsById.Values.OfType<Calor.Compiler.Binding.TypeSymbol>().ToArray());
        var spanless = DescendantsAndSelf(state.Ast!)
            .Select(node => node switch
            {
                Calor.Compiler.Ast.EventDefinitionNode value => value.DelegateType,
                Calor.Compiler.Ast.LambdaParameterNode value => value.TypeName,
                Calor.Compiler.Ast.QuantifierVariableNode value => value.TypeName,
                Calor.Compiler.Ast.PositionalPatternNode value => value.TypeName,
                Calor.Compiler.Ast.PropertyPatternNode value => value.TypeName,
                Calor.Compiler.Ast.TypePatternNode value => value.TypeName,
                Calor.Compiler.Ast.NoneExpressionNode value => value.TypeName,
                Calor.Compiler.Ast.RecordCreationNode value => value.TypeName,
                Calor.Compiler.Ast.FieldDefinitionNode value => value.TypeName,
                Calor.Compiler.Ast.TypeConstraintNode value => value.TypeName,
                Calor.Compiler.Ast.GenericTypeNode value => value.TypeName,
                Calor.Compiler.Ast.RefinementTypeNode value => value.BaseTypeName,
                Calor.Compiler.Ast.IndexedTypeNode value => value.BaseTypeName,
                _ => null,
            })
            .Where(typeName => typeName == "WidgetException")
            .ToArray();
        Assert.Empty(spanless);
        var spanlessNewTypes = Calor.Compiler.Analysis.Dataflow.BoundNodeHelpers
            .DescendantsAndSelf(state.BoundModule!)
            .OfType<Calor.Compiler.Binding.BoundNewExpression>()
            .SelectMany(creation => DescendantTypeReferences(creation.TypeReference))
            .Where(reference => reference.ResolvedTypeSymbolId == classOccurrence.SymbolId)
            .Where(reference => reference.Span.Length == 0
                || reference.Span.Start < 0
                || reference.Span.End > source.Length)
            .ToArray();
        Assert.Empty(spanlessNewTypes);
        var incompleteSpannedPositions = DescendantsAndSelf(state.Ast!)
            .Select(node => node switch
            {
                Calor.Compiler.Ast.OutputNode value when value.TypeName == "WidgetException"
                    => (Node: node.GetType().Name, Span: value.TypeNameSpan),
                Calor.Compiler.Ast.ClassDefinitionNode value when value.BaseClass == "WidgetException"
                    => (Node: node.GetType().Name, Span: value.BaseClassSpan ?? default),
                Calor.Compiler.Ast.ArrayCreationNode value when value.ElementType == "WidgetException"
                    => (Node: node.GetType().Name, Span: value.ElementTypeSpan),
                Calor.Compiler.Ast.ListCreationNode value when value.ElementType == "WidgetException"
                    => (Node: node.GetType().Name, Span: value.ElementTypeSpan),
                Calor.Compiler.Ast.TypeOperationNode value when value.TargetType == "WidgetException"
                    => (Node: node.GetType().Name, Span: value.TargetTypeSpan),
                Calor.Compiler.Ast.CatchClauseNode value when value.ExceptionType == "WidgetException"
                    => (Node: node.GetType().Name, Span: value.ExceptionTypeSpan ?? default),
                _ => default,
            })
            .Where(position => position.Node != null && position.Span.Length == 0)
            .ToArray();
        Assert.Empty(incompleteSpannedPositions);
        Assert.DoesNotContain(classOccurrence.SymbolId, typeIndex.IncompleteSymbolIds);
        Assert.True(workspace.CanRenameSymbol(classOccurrence!.SymbolId));
        var staticCall = Assert.IsType<Calor.Compiler.Binding.BoundCallExpression>(
            SymbolFinder.FindBoundCallAtOffset(
                state.BoundModule,
                source.IndexOf("WidgetException.Create", StringComparison.Ordinal)));
        Assert.Equal(classOccurrence.SymbolId, staticCall.ReceiverTypeSymbolId);

        var classEdit = await RenameAtAsync(
            workspace,
            uri,
            source,
            "WidgetException:pub",
            "RenamedException");
        Assert.NotNull(classEdit);
        var renamedClass = ApplySingleDocumentEdit(source, classEdit!);
        Assert.DoesNotContain("WidgetException", renamedClass);
        Assert.Contains("§C{RenamedException.Create}", renamedClass);
        Assert.Contains("§CA{RenamedException:ex}", renamedClass);
        Assert.Contains("§ARR{items:RenamedException:1}", renamedClass);
        Assert.Contains("§LIST{values:RenamedException}", renamedClass);
        Assert.Contains("(cast RenamedException value)", renamedClass);
        AssertRoslynClean(renamedClass);

        workspace.Update(uri, renamedClass, version: 2);
        var interfaceEdit = await RenameAtAsync(
            workspace,
            uri,
            renamedClass,
            "IWidget}",
            "IRenamedWidget");
        Assert.NotNull(interfaceEdit);
        var renamedInterface = ApplySingleDocumentEdit(
            renamedClass,
            interfaceEdit!);
        Assert.Contains("§IFACE{i001:IRenamedWidget}", renamedInterface);
        Assert.Contains("§IMPL{IRenamedWidget}", renamedInterface);
        AssertRoslynClean(renamedInterface);
    }

    [Fact]
    public async Task ConditionalAlternativeReferencesRefuseDefinitionReferencesAndRenameAsync()
    {
        const string source = """
            §M{m001:Conditional}
              §CL{c001:Worker:pub}
                §PP{FEATURE}
                  §MT{m001:Pick:pub} () -> i32
                    §R INT:1
                §PPE
                  §MT{m002:Pick:pub} () -> i32
                    §R INT:2
                §/PP{FEATURE}
                §PP{FEATURE}
                  §FLD{i32:value:pub}
                §PPE
                  §FLD{i32:value:pub}
                §/PP{FEATURE}
                §MT{m003:Run:pub} () -> i32
                  §R (+ §C{Pick} §/C §THIS.value)
            """;
        var uri = DocumentUri.From("file:///conditional-refactor.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var callOffset = source.LastIndexOf("Pick", StringComparison.Ordinal);
        var (line, column) = LspTestHarness.GetLineColumn(source, callOffset);

        var definition = await new DefinitionHandler(workspace).Handle(
            new DefinitionParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
            },
            CancellationToken.None);
        Assert.Null(definition);

        var references = await new ReferencesHandler(workspace).Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(line - 1, column - 1),
                Context = new ReferenceContext { IncludeDeclaration = true },
            },
            CancellationToken.None);
        Assert.Null(references);

        Assert.Null(await RenameAtAsync(workspace, uri, source, "§C{Pick}", "Choose"));
        Assert.Null(await RenameAtAsync(workspace, uri, source, "Pick:pub", "Choose"));
        Assert.Null(await RenameAtAsync(workspace, uri, source, "§THIS.value", "renamed"));
    }

    [Theory]
    [InlineData("if")]
    [InlineData("class")]
    [InlineData("return")]
    [InlineData("async")]
    public async Task RenameRejectsCalorAndCSharpReservedWordsAsync(string newName)
    {
        const string source = """
            §M{m001:Keywords}
              §F{f001:Compute:pub} () -> i32
                §R INT:1
            """;
        var uri = DocumentUri.From("file:///rename-keyword.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);

        Assert.Null(await RenameAtAsync(workspace, uri, source, "Compute", newName));
    }

    [Fact]
    public async Task RenameRejectsDeclarationCollisionWithoutPartialEditsAsync()
    {
        const string source = """
            §M{m001:Collisions}
              §F{f001:First:pub} () -> i32
                §R INT:1
              §F{f002:Second:pub} () -> i32
                §R INT:2
              §F{f003:Use:pub} () -> i32
                §R §C{First} §/C
            """;
        var uri = DocumentUri.From("file:///rename-collision.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);

        Assert.Null(await RenameAtAsync(workspace, uri, source, "First:pub", "Second"));
    }

    [Fact]
    public async Task RenameAcrossDistinctModulesUsesProductionCrossModuleEmissionAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["models.calr"] = """
                    §M{m001:Models}
                      §CL{c001:Widget:pub}
                        §FLD{i32:Value:pub}
                    """,
                ["services.calr"] = """
                    §M{m002:Services}
                      §U{Models}
                      §F{f001:Make:pub} () -> Widget
                        §R §NEW{Widget} §/NEW
                    """,
                ["app.calr"] = """
                    §M{m003:App}
                      §U{Models}
                      §F{f002:Run:pub} () -> Widget
                        §R §C{Make} §/C
                    """,
            };
            foreach (var (fileName, source) in sources)
                File.WriteAllText(Path.Combine(root, fileName), source);

            var workspace = new WorkspaceState(root);
            var servicesPath = Path.Combine(root, "services.calr");
            var servicesUri = DocumentUri.FromFileSystemPath(servicesPath);
            workspace.GetOrCreate(
                servicesUri,
                sources["services.calr"],
                version: 3);

            var edit = await RenameAtAsync(
                workspace,
                servicesUri,
                sources["services.calr"],
                "Make:pub",
                "Create");

            Assert.NotNull(edit);
            var documentEdits = Assert.IsType<Container<WorkspaceEditDocumentChange>>(
                    edit.DocumentChanges)
                .Select(change => Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit))
                .ToArray();
            Assert.Equal(2, documentEdits.Length);
            Assert.Equal(2, documentEdits.Sum(documentEdit => documentEdit.Edits.Count()));
            Assert.Contains(
                documentEdits,
                documentEdit => documentEdit.TextDocument.Uri == servicesUri
                    && documentEdit.TextDocument.Version == 3);
            Assert.Contains(
                documentEdits,
                documentEdit => documentEdit.TextDocument.Uri.ToUri().LocalPath
                    == Path.Combine(root, "app.calr"));

            var updated = sources.ToDictionary(
                pair => Path.Combine(root, pair.Key),
                pair => pair.Value,
                StringComparer.Ordinal);
            ApplyDocumentEdits(updated, documentEdits);
            var generated = AssertWorkspaceRoslynCleanWithProductionMap(updated);
            Assert.Contains(
                "global::Services.ServicesModule.Create",
                generated[Path.Combine(root, "app.calr")]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RenameAllowsSplitModuleLocalCallableWithUnrelatedBaselineErrorAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            var sources = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["definition.calr"] = """
                    §M{m001:Split}
                      §CL{c001:Worker:pub:partial}
                        §MT{m001:Pick:pub} (i32:value) -> i32
                          §R value
                    """,
                ["use.calr"] = """
                    §M{m002:Split}
                      §CL{c002:Worker:pub:partial}
                        §MT{m002:Run:pub} () -> i32
                          §R §C{Pick} §A INT:1 §/C
                    """,
                ["broken.calr"] = """
                    §M{m003:Broken}
                      §CSHARP{public static class BrokenProbe
                    {
                        public static int Value => MissingValue;
                    }}§/CSHARP
                    """,
            };
            foreach (var (fileName, source) in sources)
                File.WriteAllText(Path.Combine(root, fileName), source);

            Assert.Single(GetRoslynErrors(sources["broken.calr"]));
            AssertWorkspaceRoslynCleanWithProductionMap(
                sources
                    .Where(pair => pair.Key != "broken.calr")
                    .ToDictionary(
                        pair => Path.Combine(root, pair.Key),
                        pair => pair.Value,
                        StringComparer.Ordinal));
            var workspace = new WorkspaceState(root);
            var definitionPath = Path.Combine(root, "definition.calr");
            var definitionUri = DocumentUri.FromFileSystemPath(definitionPath);
            workspace.GetOrCreate(
                definitionUri,
                sources["definition.calr"],
                version: 1);

            var edit = await RenameAtAsync(
                workspace,
                definitionUri,
                sources["definition.calr"],
                "Pick:pub",
                "Choose");

            Assert.NotNull(edit);
            var documentEdits = Assert.IsType<Container<WorkspaceEditDocumentChange>>(
                    edit.DocumentChanges)
                .Select(change => Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit))
                .ToArray();
            Assert.Equal(2, documentEdits.Length);
            Assert.DoesNotContain(
                documentEdits,
                documentEdit => documentEdit.TextDocument.Uri.ToUri().LocalPath
                    == Path.Combine(root, "broken.calr"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RenameRejectsNewCompilationErrorWhenAbsoluteCountIsUnchangedAsync()
    {
        const string source = """
            §M{m001:SwapErrors}
              §F{f001:Old:pub} () -> i32
                §R INT:1
              §CSHARP{public static class Probe
            {
                public static int Existing() => SwapErrorsModule.Old();
                public static int Missing() => SwapErrorsModule.NewName();
            }}§/CSHARP
            """;
        var candidate = source.Replace(
            "§F{f001:Old:pub}",
            "§F{f001:NewName:pub}",
            StringComparison.Ordinal);
        var baselineErrors = GetRoslynErrors(source);
        var candidateErrors = GetRoslynErrors(candidate);
        var baselineError = Assert.Single(baselineErrors);
        var candidateError = Assert.Single(candidateErrors);
        Assert.Equal(baselineError.Id, candidateError.Id);
        Assert.NotEqual(
            baselineError.Location.GetLineSpan().StartLinePosition.Line,
            candidateError.Location.GetLineSpan().StartLinePosition.Line);

        var uri = DocumentUri.From("file:///rename-error-identity.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);

        Assert.Null(await RenameAtAsync(
            workspace,
            uri,
            source,
            "Old:pub",
            "NewName"));
    }

    [Fact]
    public async Task RenameRejectsFieldToParameterCaptureThatStillCompilesAsync()
    {
        const string source = """
            §M{m001:Capture}
              §CL{c001:Box:pub}
                §FLD{i32:stored:priv}
                §MT{m001:Read:pub} (i32:value) -> i32
                  §R stored
            """;
        var candidate = source
            .Replace("stored:priv", "value:priv", StringComparison.Ordinal)
            .Replace("§R stored", "§R value", StringComparison.Ordinal);
        AssertRoslynClean(candidate);
        var uri = DocumentUri.From("file:///field-to-parameter-capture.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);

        Assert.Null(await RenameAtAsync(
            workspace,
            uri,
            source,
            "stored:priv",
            "value"));
    }

    [Fact]
    public async Task RenameRejectsParameterToFieldCaptureThatStillCompilesAsync()
    {
        const string source = """
            §M{m001:Capture}
              §CL{c001:Box:pub}
                §FLD{i32:value:priv}
                §MT{m001:Read:pub} (i32:input) -> i32
                  §R (+ input value)
            """;
        var candidate = source
            .Replace("i32:input", "i32:value", StringComparison.Ordinal)
            .Replace("(+ input value)", "(+ value value)", StringComparison.Ordinal);
        AssertRoslynClean(candidate);
        var uri = DocumentUri.From("file:///parameter-to-field-capture.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);

        Assert.Null(await RenameAtAsync(
            workspace,
            uri,
            source,
            "input)",
            "value"));
    }

    [Fact]
    public async Task RenameRejectsFieldLocalCaptureInBothDirectionsAsync()
    {
        const string fieldToLocal = """
            §M{m001:Capture}
              §CL{c001:Box:pub}
                §FLD{i32:stored:priv}
                §MT{m001:Read:pub} () -> i32
                  §B{value:i32} INT:2
                  §R (+ stored value)
            """;
        var fieldToLocalCandidate = fieldToLocal
            .Replace("stored:priv", "value:priv", StringComparison.Ordinal)
            .Replace("(+ stored value)", "(+ value value)", StringComparison.Ordinal);
        AssertRoslynClean(fieldToLocalCandidate);
        var fieldUri = DocumentUri.From("file:///field-to-local-capture.calr");
        var fieldWorkspace = new WorkspaceState();
        fieldWorkspace.GetOrCreate(fieldUri, fieldToLocal);
        Assert.Null(await RenameAtAsync(
            fieldWorkspace,
            fieldUri,
            fieldToLocal,
            "stored:priv",
            "value"));

        const string localToField = """
            §M{m001:Capture}
              §CL{c001:Box:pub}
                §FLD{i32:value:priv}
                §MT{m001:Read:pub} () -> i32
                  §B{input:i32} INT:2
                  §R (+ input value)
            """;
        var localToFieldCandidate = localToField
            .Replace("input:i32", "value:i32", StringComparison.Ordinal)
            .Replace("(+ input value)", "(+ value value)", StringComparison.Ordinal);
        AssertRoslynClean(localToFieldCandidate);
        var localUri = DocumentUri.From("file:///local-to-field-capture.calr");
        var localWorkspace = new WorkspaceState();
        localWorkspace.GetOrCreate(localUri, localToField);
        Assert.Null(await RenameAtAsync(
            localWorkspace,
            localUri,
            localToField,
            "input:i32",
            "value"));
    }

    [Fact]
    public async Task RenameAllowsExplicitFieldAccessToShadowParameterSafelyAsync()
    {
        const string source = """
            §M{m001:Capture}
              §CL{c001:Box:pub}
                §FLD{i32:stored:priv}
                §MT{m001:Read:pub} (i32:value) -> i32
                  §R (+ §THIS.stored value)
            """;
        var uri = DocumentUri.From("file:///shadow-safe-field-rename.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);

        var edit = await RenameAtAsync(
            workspace,
            uri,
            source,
            "stored:priv",
            "value");

        Assert.NotNull(edit);
        var candidate = ApplySingleDocumentEdit(source, edit!);
        Assert.Contains("§FLD{i32:value:priv}", candidate);
        Assert.Contains("§THIS.value", candidate);
        AssertRoslynClean(candidate);
    }

    [Fact]
    public async Task UnrelatedPreprocessorSymbolsDoNotExpandRenameValidationAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            var source = CreatePreprocessorRenameSource(
                "RenameTarget",
                preprocessorSymbolCount: 0);
            var unrelated = CreatePreprocessorRenameSource(
                "UnrelatedFlags",
                preprocessorSymbolCount: 8,
                includeRenameTarget: false);
            var targetPath = Path.Combine(root, "target.calr");
            File.WriteAllText(targetPath, source);
            File.WriteAllText(Path.Combine(root, "unrelated.calr"), unrelated);
            var unrelatedState = LspTestHarness.CreateDocument(unrelated);
            Assert.Equal(
                8,
                new CSharpEmitter().Emit(unrelatedState.Ast!)
                    .Split("#if FLAG_", StringSplitOptions.None)
                    .Length - 1);

            var workspace = new WorkspaceState(root);
            var uri = DocumentUri.FromFileSystemPath(targetPath);
            workspace.GetOrCreate(uri, source, version: 1);
            var targetOffset = source.IndexOf("Compute:pub", StringComparison.Ordinal);
            Assert.NotNull(workspace.ResolveOccurrence(uri, targetOffset));
            workspace.RefreshClosedDocuments();
            var target = workspace.ResolveOccurrence(uri, targetOffset);
            Assert.NotNull(target);
            Assert.True(workspace.CanRenameSymbol(target!.SymbolId));
            var targetOccurrences = workspace.FindSymbolOccurrences(
                target.SymbolId,
                includeDeclaration: true);
            Assert.Equal(2, targetOccurrences.Count);
            Assert.DoesNotContain(
                targetOccurrences,
                occurrence => occurrence.IsSplitDeclaration);
            Assert.True(workspace.AreOccurrenceSnapshotsCurrent(targetOccurrences));
            var before = workspace.RenameValidationCompilationCount;

            var edit = await RenameAtAsync(
                workspace,
                uri,
                source,
                "Compute:pub",
                "Calculate");

            Assert.NotNull(edit);
            Assert.Equal(
                2,
                workspace.RenameValidationCompilationCount - before);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RenameFailsClosedWhenRelevantConfigurationBudgetIsExceededAsync()
    {
        var source = CreatePreprocessorRenameSource(
            "TooManyFlags",
            preprocessorSymbolCount: 6);
        var uri = DocumentUri.From("file:///rename-too-many-flags.calr");
        var workspace = new WorkspaceState();
        workspace.GetOrCreate(uri, source);
        var before = workspace.RenameValidationCompilationCount;

        Assert.Null(await RenameAtAsync(
            workspace,
            uri,
            source,
            "Compute:pub",
            "Calculate"));
        Assert.Equal(
            before,
            workspace.RenameValidationCompilationCount);
    }

    [Fact]
    public async Task RenameValidationHonorsCancellationDuringCompilationAsync()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            var source = CreatePreprocessorRenameSource(
                "Cancellation",
                preprocessorSymbolCount: 5);
            var targetPath = Path.Combine(root, "target.calr");
            File.WriteAllText(targetPath, source);
            for (var index = 0; index < 20; index++)
            {
                File.WriteAllText(
                    Path.Combine(root, $"unrelated-{index}.calr"),
                    $$"""
                    §M{m100:Unrelated{{index}}}
                      §CL{c100:Type{{index}}:pub}
                        §FLD{i32:Value:pub}
                    """);
            }

            var workspace = new WorkspaceState(root);
            var uri = DocumentUri.FromFileSystemPath(targetPath);
            workspace.GetOrCreate(uri, source);
            var before = workspace.RenameValidationCompilationCount;
            using var cancellation = new CancellationTokenSource();

            var rename = RenameAtAsync(
                workspace,
                uri,
                source,
                "Compute:pub",
                "Calculate",
                cancellation.Token);
            Assert.True(SpinWait.SpinUntil(
                () => workspace.RenameValidationCompilationCount > before
                    || rename.IsCompleted,
                TimeSpan.FromSeconds(5)));
            Assert.False(rename.IsCompleted);
            await cancellation.CancelAsync();

            try
            {
                await rename;
                Assert.Fail("Rename validation should have observed cancellation.");
            }
            catch (OperationCanceledException)
            {
            }
            Assert.True(workspace.RenameValidationCompilationCount > before);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TypeRenameRefusesWhenSemanticCompletenessSignalFindsSpanlessConstraintAsync()
    {
        const string source = """
            §M{m001:IncompleteTypeSpan}
              §CL{c001:Widget:pub}
                §FLD{i32:Value:pub}
              §F{f001:Use:pub}<T> (T:value) -> T
                §WHERE T : Widget
                §R value
            """;
        var uri = DocumentUri.From("file:///rename-incomplete-type.calr");
        var workspace = new WorkspaceState();
        var state = workspace.GetOrCreate(uri, source);
        Assert.False(state.Diagnostics.HasErrors);

        var offset = source.IndexOf("Widget:pub", StringComparison.Ordinal);
        var occurrence = workspace.ResolveOccurrence(uri, offset);
        Assert.NotNull(occurrence);
        Assert.False(workspace.CanRenameSymbol(occurrence!.SymbolId));
        Assert.Null(await RenameAtAsync(workspace, uri, source, "Widget:pub", "RenamedWidget"));
    }

    [Fact]
    public void CachedProjectCallResolutionDoesNotRereadWorkspaceFiles()
    {
        var root = CreateWorkspaceDirectory();
        try
        {
            const string definitions = """
                §M{m001:Definitions}
                  §F{f001:Pick:pub} (i32:value) -> i32
                    §R value
                """;
            const string use = """
                §M{m002:Use}
                  §F{f002:Run:pub} () -> i32
                    §R §C{Pick} §A INT:1 §/C
                """;
            File.WriteAllText(Path.Combine(root, "definitions.calr"), definitions);
            var usePath = Path.Combine(root, "use.calr");
            File.WriteAllText(usePath, use);
            var workspace = new WorkspaceState(root);
            var state = workspace.GetOrCreate(
                DocumentUri.FromFileSystemPath(usePath),
                use,
                version: 1);
            var call = SymbolFinder.FindBoundCallAtOffset(
                state.BoundModule,
                use.IndexOf("Pick", StringComparison.Ordinal));
            var reads = workspace.WorkspaceFileReadCount;

            for (var iteration = 0; iteration < 10; iteration++)
                Assert.NotNull(workspace.ResolveProjectCall(state, state.Snapshot, call).Symbol);

            Assert.Equal(reads, workspace.WorkspaceFileReadCount);
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
        string newName,
        CancellationToken cancellationToken = default)
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
            cancellationToken);
    }

    private static string TextAt(string source, OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range)
    {
        var start = PositionConverter.ToOffset(range.Start, source);
        var end = PositionConverter.ToOffset(range.End, source);
        return source[start..end];
    }

    private static string ApplySingleDocumentEdit(string source, WorkspaceEdit edit)
    {
        var change = Assert.Single(Assert.IsType<Container<WorkspaceEditDocumentChange>>(
            edit.DocumentChanges));
        var documentEdit = Assert.IsType<TextDocumentEdit>(change.TextDocumentEdit);
        foreach (var textEdit in documentEdit.Edits.OrderByDescending(textEdit =>
                     PositionConverter.ToOffset(textEdit.Range.Start, source)))
        {
            var start = PositionConverter.ToOffset(textEdit.Range.Start, source);
            var end = PositionConverter.ToOffset(textEdit.Range.End, source);
            source = source[..start] + textEdit.NewText + source[end..];
        }
        return source;
    }

    private static void AssertRoslynClean(string source)
    {
        var state = LspTestHarness.CreateDocument(source);
        Assert.NotNull(state.Ast);
        Assert.NotNull(state.BoundModule);
        Assert.False(
            state.Diagnostics.HasErrors,
            string.Join(Environment.NewLine, state.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var syntaxTree = CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(state.Ast!));
        Assert.DoesNotContain(
            syntaxTree.GetDiagnostics(),
            diagnostic => diagnostic.Severity
                == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var compilation = CSharpCompilation.Create(
            "RenameAppliedCompile",
            [syntaxTree],
            GetPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity
                == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }

    private static Microsoft.CodeAnalysis.Diagnostic[] GetRoslynErrors(string source)
    {
        var state = LspTestHarness.CreateDocument(source);
        Assert.NotNull(state.Ast);
        Assert.False(
            state.Diagnostics.HasErrors,
            string.Join(Environment.NewLine, state.Diagnostics.Select(diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}")));
        var tree = CSharpSyntaxTree.ParseText(new CSharpEmitter().Emit(state.Ast!));
        var compilation = CSharpCompilation.Create(
            "RenameBaselineErrors",
            [tree],
            GetPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity
                == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();
    }

    private static Dictionary<string, string> AssertWorkspaceRoslynCleanWithProductionMap(
        IReadOnlyDictionary<string, string> sources)
    {
        var modules = sources
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var state = LspTestHarness.CreateDocument(
                    pair.Value,
                    new Uri(pair.Key).AbsoluteUri);
                Assert.NotNull(state.Ast);
                Assert.NotNull(state.BoundModule);
                Assert.False(
                    state.Diagnostics.HasErrors,
                    string.Join(Environment.NewLine, state.Diagnostics.Select(diagnostic =>
                        $"{diagnostic.Code}: {diagnostic.Message}")));
                return (pair.Key, Module: state.Ast!);
            })
            .ToArray();
        var crossModuleMap = modules.Length > 1
            ? Calor.Compiler.CompilationDriver.BuildCrossModuleFunctionMap(
                modules.Select(module => module.Module).ToArray())
            : null;
        var generated = new Dictionary<string, string>(StringComparer.Ordinal);
        var trees = modules
            .Select(module =>
            {
                var emitter = new CSharpEmitter
                {
                    CrossModuleFunctionModules = crossModuleMap,
                    LineDirectiveFilePath = module.Key,
                };
                var source = emitter.Emit(module.Module);
                generated[module.Key] = source;
                return CSharpSyntaxTree.ParseText(
                    source,
                    path: module.Key + ".g.cs");
            })
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "RenameWorkspaceCompile",
            trees,
            GetPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity
                == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        return generated;
    }

    private static void ApplyDocumentEdits(
        IDictionary<string, string> sources,
        IEnumerable<TextDocumentEdit> documentEdits)
    {
        foreach (var documentEdit in documentEdits)
        {
            var path = documentEdit.TextDocument.Uri.ToUri().LocalPath;
            var source = sources[path];
            foreach (var textEdit in documentEdit.Edits.OrderByDescending(textEdit =>
                         PositionConverter.ToOffset(textEdit.Range.Start, source)))
            {
                var start = PositionConverter.ToOffset(textEdit.Range.Start, source);
                var end = PositionConverter.ToOffset(textEdit.Range.End, source);
                source = source[..start] + textEdit.NewText + source[end..];
            }
            sources[path] = source;
        }
    }

    private static string CreatePreprocessorRenameSource(
        string moduleName,
        int preprocessorSymbolCount,
        bool includeRenameTarget = true)
    {
        var lines = new List<string>
        {
            $"§M{{m001:{moduleName}}}",
        };
        if (preprocessorSymbolCount > 0)
        {
            lines.Add("  §CL{c001:Flags:pub}");
            for (var index = 0; index < preprocessorSymbolCount; index++)
            {
                lines.Add($"    §PP{{FLAG_{index}}}");
                lines.Add($"      §FLD{{i32:Field{index}:pub}}");
                lines.Add($"    §/PP{{FLAG_{index}}}");
            }
        }
        if (includeRenameTarget)
        {
            lines.Add("  §F{f001:Compute:pub} () -> i32");
            lines.Add("    §R INT:1");
            lines.Add("  §F{f002:Use:pub} () -> i32");
            lines.Add("    §R §C{Compute} §/C");
        }
        return string.Join(Environment.NewLine, lines);
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

    private static IEnumerable<Calor.Compiler.Ast.AstNode> DescendantsAndSelf(
        Calor.Compiler.Ast.AstNode node)
    {
        yield return node;
        foreach (var child in Calor.Compiler.Analysis.RecursiveAstWalker.GetAllChildren(node))
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }

    private static IEnumerable<Calor.Compiler.Binding.BoundTypeReference> DescendantTypeReferences(
        Calor.Compiler.Binding.BoundTypeReference reference)
    {
        yield return reference;
        foreach (var argument in reference.TypeArguments)
        {
            foreach (var descendant in DescendantTypeReferences(argument))
                yield return descendant;
        }
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
