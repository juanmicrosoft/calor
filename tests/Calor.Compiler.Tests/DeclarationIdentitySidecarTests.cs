using System.Text.Json;
using Calor.Compiler.Binding;
using Calor.Compiler.Ast;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Mcp.Sessions;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Calor.Compiler.Refactoring;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class DeclarationIdentitySidecarTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-identity-" + Guid.NewGuid().ToString("N"));
    private DeclarationIdentityStore Store => new(_root);
    private string PathFor(string file) => Path.Combine(_root, file);
    private string Write(string file, string source)
    {
        var path = PathFor(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private const string Original = """
        §M{Demo}
          §F{Add:pub} (i32:x) -> i32
            §R x
        """;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void NarrowActiveInputSyncRetainsAuthoritativeAncestorStore()
    {
        var file = Write("src/A.calr", Original);
        var before = Store.Sync([file]);
        DeclarationIdentityStore.SyncActiveInputs([file]);
        Assert.False(File.Exists(PathFor("src/.calor/ids.json")));
        Assert.Equal(before.Declarations, Store.Sync().Declarations);
        var module = CalorSourceHelper.Parse(File.ReadAllText(file), file).Ast!;
        Assert.Equal(before.Declarations.Single(d => d.Kind == IdKind.Function).Id, module.Functions.Single().Id);
    }

    [Fact]
    public void MigrationOutputUsesExistingProjectStoreWithoutShadowingRegisteredSiblings()
    {
        var sibling = Write("src/A.calr", Original);
        var before = Store.Sync([sibling]);
        var output = PathFor("src/B.calr");
        ConversionFileWriter.WriteAtomic(output, Original.Replace("Demo", "Added"));
        Assert.False(File.Exists(PathFor("src/.calor/ids.json")));
        var after = Store.Sync();
        Assert.Contains("src/B.calr", after.Files);
        Assert.Equal(before.Declarations.Single(d => d.Kind == IdKind.Function).Id,
            CalorSourceHelper.Parse(File.ReadAllText(sibling), sibling).Ast!.Functions.Single().Id);
    }

    [Fact]
    public void NewProjectInputsDoNotAdoptUnrelatedAncestorStore()
    {
        Store.Sync([Write("unrelated.calr", Original)]);
        var ancestor = File.ReadAllText(Store.SidecarPath);
        Write("project/project.csproj", "<Project />");
        var active = Write("project/A.calr", Original.Replace("Demo", "NewProject"));
        DeclarationIdentityStore.SyncActiveInputs([active]);
        ConversionFileWriter.WriteAtomic(PathFor("project/B.calr"), Original.Replace("Demo", "Converted"));
        Assert.Equal(ancestor, File.ReadAllText(Store.SidecarPath));
        var project = new DeclarationIdentityStore(PathFor("project"));
        Assert.Equal(["A.calr", "B.calr"], project.Sync().Files);
    }

    [Fact]
    public void NewProjectInputsDoNotRecoverUnrelatedAncestorTransactions()
    {
        var unrelated = Write("unrelated.calr", Original);
        Store.Sync([unrelated]);
        var ancestor = File.ReadAllText(Store.SidecarPath);
        var journal = JsonSerializer.Serialize(new
        {
            BeforeSidecar = ancestor, AfterSidecar = ancestor,
            Changes = new[] { new DeclarationIdentityStore.SourceChange("unrelated.calr", Original, Original + "\n") }
        });
        File.WriteAllText(PathFor(".calor/ids.transaction.json"), journal);
        Write("project/project.csproj", "<Project />");
        var active = Write("project/A.calr", Original.Replace("Demo", "NewProject"));
        DeclarationIdentityStore.SyncActiveInputs([active]);
        ConversionFileWriter.WriteAtomic(PathFor("project/B.calr"), Original.Replace("Demo", "Converted"));
        Assert.Equal(Original, File.ReadAllText(unrelated));
        Assert.Equal(ancestor, File.ReadAllText(Store.SidecarPath));
        Assert.Equal(journal, File.ReadAllText(PathFor(".calor/ids.transaction.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TextClassRenamePreservesSelfTypedMemberIdentity(bool canonicalSourceId)
    {
        var methodId = canonicalSourceId ? CompactIdGenerator.Generate(IdKind.Method) + ":" : "";
        var source = "§M{Demo}\n  §CL{A:pub}\n    §MT{" + methodId
            + "Echo:pub} (A:value) -> A\n      §R value\n";
        var file = Write("active.calr", source);
        var before = Store.Sync([file]);
        var after = source.Replace("§CL{A:", "§CL{B:").Replace("(A:value)", "(B:value)").Replace("-> A", "-> B");
        var result = Store.Apply([new(file, source, after)]);
        Assert.Equal(before.Declarations.Single(d => d.Name == "Echo").Id,
            result.Declarations.Single(d => d.Name == "Echo").Id);
        Assert.Equal(before.Declarations.Single(d => d.Name == "A").Id,
            result.Declarations.Single(d => d.Name == "B").Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task McpClassRenameReportsExistingTypeIndexLimitationWithoutEdits(bool canonicalSourceId)
    {
        var methodId = canonicalSourceId ? CompactIdGenerator.Generate(IdKind.Method) + ":" : "";
        var source = "§M{Demo}\n  §CL{A:pub}\n    §MT{" + methodId
            + "Echo:pub} (A:value) -> A\n      §R value\n"
            + "    §MT{Echo:pub} (i32:value) -> i32\n      §R value\n";
        var file = Write("active.calr", source);
        Store.Sync([file]);
        var sidecar = File.ReadAllText(Store.SidecarPath);
        var result = await new FileWriteTool(new ProjectSessionManager(), _root).ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                action = "rename", projectDirectory = _root, filePath = file,
                line = 2, column = 7, newName = "B"
            }));
        Assert.True(result.IsError);
        Assert.Contains("TypeReferencesNotIndexed", result.Content.Single().Text);
        Assert.Equal(source, File.ReadAllText(file));
        Assert.Equal(sidecar, File.ReadAllText(Store.SidecarPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClassRenamePreservesSelfTypedOverloadsWithOrWithoutExplicitRelocation(bool explicitRelocation)
    {
        const string source = """
            §M{Demo}
              §CL{A:pub}
                §MT{Echo:pub} (A:value) -> A
                  §R value
                §MT{Echo:pub} (i32:value) -> i32
                  §R value
            """;
        var file = Write("active.calr", source);
        var before = Store.Sync([file]);
        var renamed = source.Replace("§CL{A:", "§CL{B:").Replace("(A:value)", "(B:value)").Replace("-> A", "-> B");
        var relocation = new DeclarationRelocation(before.Declarations.Single(d => d.Name == "A").Id,
            DeclarationIdentityStore.Describe("active.calr", renamed).Single(d => d.Name == "B").Key);
        Store.Apply([new(file, source, renamed)], explicitRelocation ? [relocation] : []);
        var after = Store.Sync();
        foreach (var method in before.Declarations.Where(d => d.Kind == IdKind.Method))
            Assert.Equal(method.Id, after.Declarations.Single(d => d.Kind == IdKind.Method
                && d.Signature == method.Signature.Replace("\"A\"", "\"B\"")).Id);
    }

    [Fact]
    public void AmbiguousSelfTypedOverloadEditsAfterClassRenameWriteNothing()
    {
        const string source = """
            §M{Demo}
              §CL{A:pub}
                §MT{Echo:pub} (A:value) -> A
                  §R value
                §MT{Echo:pub} (A:value, i32:count) -> A
                  §R value
            """;
        var file = Write("active.calr", source);
        Store.Sync([file]);
        var sidecar = File.ReadAllText(Store.SidecarPath);
        var renamed = source.Replace("§CL{A:", "§CL{B:").Replace("(A:value", "(B:value").Replace("-> A", "-> B");
        Assert.Contains("Ambiguous", Assert.Throws<InvalidOperationException>(() =>
            Store.Apply([new(file, source, renamed)])).Message);
        Assert.Equal(source, File.ReadAllText(file));
        Assert.Equal(sidecar, File.ReadAllText(Store.SidecarPath));
        Assert.False(File.Exists(PathFor(".calor/ids.transaction.json")));
    }

    [Theory]
    [InlineData(false, "property")]
    [InlineData(true, "property")]
    [InlineData(false, "indexer")]
    [InlineData(true, "indexer")]
    [InlineData(false, "init")]
    [InlineData(true, "init")]
    [InlineData(false, "event")]
    [InlineData(true, "event")]
    public void AccessorLocalDeclarationsHaveDistinctStableScopes(bool sourceIds, string memberKind)
    {
        var member = memberKind switch
        {
            "property" => "§PROP{" + (sourceIds ? "p001:" : "") + "P:i32:pub}",
            "init" => "§PROP{" + (sourceIds ? "p001:" : "") + "P:i32:pub}",
            "indexer" => "§IXER{ix001:i32:pub}\n      §I{i32:index}",
            _ => "§EVT{" + (sourceIds ? "e001:" : "") + "Changed:pub:EventHandler}"
        };
        var firstAccessor = memberKind == "event" ? "§EADD" : "§GET";
        var secondAccessor = memberKind == "event" ? "§EREM" : memberKind == "init" ? "§INIT" : "§SET";
        var source = "§M{Demo}\n  §CL{A:pub}\n    " + member + "\n"
            + "      " + firstAccessor + "\n        §F{" + (sourceIds ? "f001:" : "") + "Helper:priv} () -> i32\n          §R 1\n"
            + "      " + secondAccessor + "\n        §F{" + (sourceIds ? "f002:" : "") + "Helper:priv} () -> i32\n          §R 2\n";
        var file = Write("active.calr", source);
        var before = Store.Sync([file]).Declarations.Where(d => d.Name == "Helper").ToArray();
        Assert.Equal(2, before.Length);
        Assert.NotEqual(before[0].Container, before[1].Container);
        Assert.NotEqual(before[0].Id, before[1].Id);
        File.WriteAllText(file, source.Replace("§R 1", "§R 3").Replace("§R 2", "§R 4"));
        Assert.Equal(before, Store.Sync().Declarations.Where(d => d.Name == "Helper"));
        var renamed = File.ReadAllText(file).Replace("§CL{A:", "§CL{B:");
        Store.Apply([new(file, File.ReadAllText(file), renamed)]);
        Assert.Equal(before.Select(d => d.Id).Order(), Store.Sync().Declarations.Where(d => d.Name == "Helper").Select(d => d.Id).Order());
    }

    [Fact]
    public void SyncRegistersExplicitInputsWithoutChangingAnySourceBytes()
    {
        var file = Write("active.calr", Original);
        var historical = Write("historical.calr", "frozen syntax, not parseable");
        var bytes = File.ReadAllBytes(file);
        var first = Store.Sync([file]);
        var sidecarBytes = File.ReadAllBytes(Store.SidecarPath);
        var second = Store.Sync();
        Assert.Equal(bytes, File.ReadAllBytes(file));
        Assert.Equal("frozen syntax, not parseable", File.ReadAllText(historical));
        Assert.Equal(["active.calr"], first.Files);
        Assert.Equal(2, first.Declarations.Count);
        Assert.All(first.Declarations, d => Assert.True(IdValidator.IsCompactId(d.Id)));
        Assert.Equal(first.Declarations, second.Declarations);
        Assert.Equal(sidecarBytes, File.ReadAllBytes(Store.SidecarPath));
    }

    [Fact]
    public void TextRenameAndFileMovePreserveIdentityAfterSync()
    {
        var file = Write("before.calr", Original);
        var first = Store.Sync([file]);
        var renamed = Original.Replace("Add", "Sum");
        var destination = Write("after.calr", renamed);
        File.Delete(file);
        var second = Store.Sync([destination]);
        Assert.Equal(first.Declarations.Single(d => d.Kind == IdKind.Function).Id,
            second.Declarations.Single(d => d.Kind == IdKind.Function).Id);
        Assert.Equal("Sum", second.Declarations.Single(d => d.Kind == IdKind.Function).Name);
        Assert.Equal(["after.calr"], second.Files);
    }

    [Fact]
    public void AmbiguousTextRenamesWriteNothing()
    {
        var before = Original + "\n  §F{Other:pub} (i32:x) -> i32\n    §R x";
        var file = Write("active.calr", before);
        Store.Sync([file]);
        var bytes = File.ReadAllBytes(Store.SidecarPath);
        File.WriteAllText(file, before.Replace("Add", "Sum").Replace("Other", "Again"));
        Assert.Contains("Ambiguous", Assert.Throws<InvalidOperationException>(() => Store.Sync()).Message);
        Assert.Equal(bytes, File.ReadAllBytes(Store.SidecarPath));
        Assert.False(File.Exists(PathFor(".calor/ids.transaction.json")));
    }

    [Fact]
    public void ExplicitRelocationsDisambiguateTwoRenamesAtomically()
    {
        var before = Original + "\n  §F{Other:pub} (i32:x) -> i32\n    §R x";
        var file = Write("active.calr", before);
        var state = Store.Sync([file]);
        var after = before.Replace("Add", "Sum").Replace("Other", "Again");
        var destinations = DeclarationIdentityStore.Describe("active.calr", after);
        var relocations = new[]
        {
            new DeclarationRelocation(state.Declarations.Single(d => d.Name == "Add").Id, destinations.Single(d => d.Name == "Sum").Key),
            new DeclarationRelocation(state.Declarations.Single(d => d.Name == "Other").Id, destinations.Single(d => d.Name == "Again").Key)
        };
        var result = Store.Apply([new(file, before, after)], relocations);
        Assert.Equal(after, File.ReadAllText(file));
        Assert.Equal(relocations[0].Id, result.Declarations.Single(d => d.Name == "Sum").Id);
        Assert.Equal(relocations[1].Id, result.Declarations.Single(d => d.Name == "Again").Id);
        Assert.False(File.Exists(PathFor(".calor/ids.transaction.json")));
    }

    [Fact]
    public void StalePreparedSidecarCannotOverwriteConcurrentIdentityChanges()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var expected = File.ReadAllText(Store.SidecarPath);
        var added = Write("added.calr", Original.Replace("Demo", "Added"));
        Store.Sync([added]);
        var current = File.ReadAllText(Store.SidecarPath);
        Assert.Contains("Sidecar changed concurrently", Assert.Throws<IOException>(() =>
            Store.Apply([new(file, Original, Original.Replace("Add", "Sum"))], expectedSidecar: expected)).Message);
        Assert.Equal(Original, File.ReadAllText(file));
        Assert.Equal(current, File.ReadAllText(Store.SidecarPath));
    }

    [Fact]
    public void MismatchedCanonicalSourceKindCannotLeaveAnInvalidJournal()
    {
        var source = Original.Replace("§F{Add", "§F{" + CompactIdGenerator.Generate(IdKind.Module) + ":Add");
        var file = Write("active.calr", source);
        Assert.Contains("kind", Assert.Throws<InvalidOperationException>(() => Store.Sync([file])).Message);
        Assert.False(File.Exists(Store.SidecarPath));
        Assert.False(File.Exists(PathFor(".calor/ids.transaction.json")));
        Assert.Equal(source, File.ReadAllText(file));
    }

    [Fact]
    public void AtomicSourceWriteEnrollsAnExistingNewActiveFile()
    {
        Store.Sync([Write("original.calr", Original)]);
        var source = Original.Replace("Demo", "Added");
        var file = Write("added.calr", source);
        var updated = source.Replace("Add", "Sum");
        var state = Store.Apply([new(file, source, updated)]);
        Assert.Contains("added.calr", state.Files);
        Assert.Equal(updated, File.ReadAllText(file));
        Assert.Contains(state.Declarations, declaration => declaration.File == "added.calr" && declaration.Name == "Sum");
    }

    [Fact]
    public void SameFilePartialTypeFragmentsShareIdentityWithoutSelectingALegacyFragmentId()
    {
        const string source = """
            §M{Demo}
              §CL{c001:Parts:pub:partial}
                §MT{mt001:First:pub} () -> i32
                  §R 1
              §CL{c002:Parts:pub:partial}
                §MT{mt002:Second:pub} () -> i32
                  §R 2
            """;
        var file = Write("active.calr", source);
        var before = Store.Sync([file]);
        var expected = Assert.Single(before.Declarations.Where(d => d.Kind == IdKind.Class)).Id;
        var ast = CalorSourceHelper.Parse(source, file).Ast!;
        Assert.All(ast.Classes, node => Assert.Equal(expected, node.Id));
        Assert.Single(new IdScanner().Scan(ast, file).Where(d => d.Kind == IdKind.Class));
        File.WriteAllText(file, source.Replace("Parts", "Renamed"));
        var after = Store.Sync();
        Assert.Equal(before.Declarations.Select(d => d.Id).Order(), after.Declarations.Select(d => d.Id).Order());
        Assert.Equal(expected, after.Declarations.Single(d => d.Kind == IdKind.Class).Id);
    }

    [Fact]
    public void IndexEnrollsItsActiveInputsAndProjectsNonBinderDeclarations()
    {
        const string source = """
            §M{Demo}
              §CL{Container:pub}
                §FLD{i32:count:pub}
                §EVT{ev001:Changed:pub:EventHandler}
              §F{Main:pub} () -> i32
                §PROOF{po001:positive} (> 1 0)
                §R 1
            """;
        var file = Write("active.calr", source);
        Write("historical.calr", "frozen invalid syntax");
        var options = new Calor.Compiler.Indexing.ProjectIndexBuilder.Options(_root, "identity", [file]);
        var index = Calor.Compiler.Indexing.ProjectIndexBuilder.Build(options);
        var state = Store.Preview([]);
        Assert.Equal(["active.calr"], state.Files);
        foreach (var declaration in state.Declarations)
            Assert.Contains(index.Declarations, entry => entry.SymbolId == SymbolId.Create("declaration", declaration.Id).Value);
        var field = state.Declarations.Single(d => d.Kind == IdKind.Field);
        var parsed = CalorSourceHelper.Parse(source, file);
        var bound = new Binder(new DiagnosticBag(), file).Bind(parsed.Ast!);
        Assert.Contains(bound.SymbolsById.Keys, id => id == SymbolId.Create("declaration", field.Id));
    }

    [Theory]
    [InlineData("write", "changes")]
    [InlineData("rename", "content")]
    [InlineData("rename", "destinationFile")]
    [InlineData("move", "newName")]
    public async Task McpActionsRejectIncompatibleParametersWithoutWriting(string action, string parameter)
    {
        var tool = new FileWriteTool(new ProjectSessionManager(), _root);
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["action"] = action, [parameter] = "invalid" }));
        Assert.True(result.IsError);
        Assert.Contains("does not apply", result.Content.Single().Text);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task McpSchemaExtendsFileWriteWithoutAddingATopLevelTool()
    {
        var response = await new McpMessageHandler().HandleRequestAsync(new JsonRpcRequest
        {
            Id = JsonSerializer.SerializeToElement(1),
            Method = "tools/list"
        });
        Assert.NotNull(response);
        Assert.Null(response.Error);
        var result = JsonSerializer.SerializeToElement(response.Result, McpJsonOptions.Default);
        var tools = result.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(19, tools.Length);
        Assert.DoesNotContain(tools, tool => tool.GetProperty("name").GetString() == "calor_refactor");
        var schema = Assert.Single(tools, tool => tool.GetProperty("name").GetString() == "calor_file_write")
            .GetProperty("inputSchema");
        var properties = schema.GetProperty("properties");
        foreach (var legacy in new[] { "path", "content", "sessionId", "checks", "heal" })
            Assert.True(properties.TryGetProperty(legacy, out _));
        Assert.Equal(new[] { "write", "rename", "move" },
            properties.GetProperty("action").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("write", properties.GetProperty("action").GetProperty("default").GetString());
        Assert.Equal(3, schema.GetProperty("allOf").GetArrayLength());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void OverloadsUseTypesAndRefModifiersRatherThanParameterNames()
    {
        var before = Original + "\n  §F{Add:pub} (str:value) -> str\n    §R value";
        var file = Write("active.calr", before);
        var initial = Store.Sync([file]);
        File.WriteAllText(file, before.Replace("Add", "Sum").Replace("i32:x", "i32:input").Replace("§R x", "§R input"));
        var result = Store.Sync();
        Assert.Equal(initial.Declarations.Where(d => d.Kind == IdKind.Function).Select(d => d.Id).Order(),
            result.Declarations.Where(d => d.Kind == IdKind.Function).Select(d => d.Id).Order());
        var refVersion = DeclarationIdentityStore.Describe("ref.calr", Original.Replace("i32:x", "i32:x:ref"));
        Assert.NotEqual(initial.Declarations.Single(d => d.Name == "Add" && d.Signature.Contains("i32")).Signature,
            refVersion.Single(d => d.Name == "Add").Signature);
    }

    [Fact]
    public void MoveBetweenClassesAndFilesKeepsBoundSymbolIdentity()
    {
        const string before = """
            §M{Demo}
              §CL{A:pub}
                §MT{Work:pub} (i32:x) -> i32
                  §R x
              §CL{B:pub}
            """;
        const string after = """
            §M{Demo}
              §CL{A:pub}
              §CL{B:pub}
                §MT{Work:pub} (i32:x) -> i32
                  §R x
            """;
        var file = Write("before.calr", before);
        var initial = Store.Sync([file]);
        var oldModule = CalorSourceHelper.Parse(before, file).Ast!;
        var oldBound = new Binder(new DiagnosticBag(), file).Bind(oldModule);
        var oldSymbol = oldBound.SymbolsById.Values.Single(s => s.Name.EndsWith(".Work", StringComparison.Ordinal)).Id;
        var method = initial.Declarations.Single(d => d.Name == "Work");
        var destination = PathFor("after.calr");
        var key = DeclarationIdentityStore.Describe("after.calr", after).Single(d => d.Name == "Work").Key;
        var result = Store.Apply([new(file, before, null), new(destination, null, after)], [new(method.Id, key)]);
        var newModule = CalorSourceHelper.Parse(after, destination).Ast!;
        var newBound = new Binder(new DiagnosticBag(), destination).Bind(newModule);
        Assert.Equal(method.Id, result.Declarations.Single(d => d.Name == "Work").Id);
        Assert.Equal(oldSymbol, newBound.SymbolsById.Values.Single(s => s.Name.EndsWith(".Work", StringComparison.Ordinal)).Id);
    }

    [Fact]
    public void NavigationAndDiagnosticResolverUseSidecarIdentityForUnsavedRename()
    {
        var file = Write("active.calr", Original);
        var state = Store.Sync([file]);
        var expected = state.Declarations.Single(d => d.Kind == IdKind.Function).Id;
        var renamed = Original.Replace("Add", "Sum");
        var parsed = CalorSourceHelper.Parse(renamed, file);
        Assert.True(parsed.IsSuccess);
        Assert.Equal(expected, parsed.Ast!.Functions.Single().Id);
        var resolver = new DeclarationIdResolver();
        resolver.AddFile(file, renamed, parsed.Ast);
        Assert.Equal(expected, resolver.Resolve(file, 0, 0, 3, 5));
        Assert.Equal(Original, File.ReadAllText(file));
    }

    [Fact]
    public void SourceEmitterNeverLeaksToolingIds()
    {
        var file = Write("active.calr", Original);
        var state = Store.Sync([file]);
        var parsed = CalorSourceHelper.Parse(Original, file);
        var emitted = new CalorEmitter().Emit(parsed.Ast!);
        foreach (var declaration in state.Declarations)
            Assert.DoesNotContain(declaration.Id, emitted);
        Assert.Contains(parsed.Ast!.Functions.Single().SourceId, emitted);
    }

    [Fact]
    public void NamedModulesUseExactModuleKindAndRetainNestedIdentityAcrossRename()
    {
        const string source = """
            §M{Demo}
              §M{Tools:pub}
                §F{Work:pub} () -> i32
                  §R 1
                §M{Nested:pub}
                  §F{Inner:pub} () -> i32
                    §R 2
            """;
        var file = Write("active.calr", source);
        var before = Store.Sync([file]);
        Assert.All(before.Declarations.Where(d => d.Name is "Demo" or "Tools" or "Nested"),
            declaration => Assert.Equal(IdKind.Module, declaration.Kind));
        var updated = source.Replace("Tools", "Helpers");
        var after = Store.Apply([new(file, source, updated)]);
        Assert.Equal(before.Declarations.Select(d => d.Id).Order(), after.Declarations.Select(d => d.Id).Order());
        var ast = CalorSourceHelper.Parse(updated, file).Ast!;
        Assert.True(ast.Classes.Single(node => node.Name == "Helpers").IsModule);
        Assert.Equal(after.Declarations.Select(d => d.Id).Order(),
            new IdScanner().Scan(ast, file).Select(d => d.Id).Order());
        var bound = new Binder(new DiagnosticBag(), file).Bind(ast);
        Assert.Contains(bound.SymbolsById.Keys,
            id => id == SymbolId.Create("declaration", after.Declarations.Single(d => d.Name == "Helpers").Id));
        var emitted = new CalorEmitter().Emit(ast);
        Assert.Contains("§M{", emitted);
        foreach (var declaration in after.Declarations)
            Assert.DoesNotContain(declaration.Id, emitted);
    }

    [Fact]
    public void LegacyStaticAstRemainsAClassIdentityWithoutRequiringRetiredSourceSyntax()
    {
        var legacy = new ClassDefinitionNode(TextSpan.Empty, IdGenerator.Generate(IdKind.Class), "Legacy",
            isAbstract: false, isSealed: false, isPartial: false, isStatic: true, baseClass: null,
            implementedInterfaces: [], typeParameters: [], fields: [], properties: [], constructors: [],
            methods: [], attributes: new AttributeCollection(), csharpAttributes: []);
        var module = CalorSourceHelper.Parse("§M{Demo}").Ast!.With(update => update.Items = [legacy]);
        Assert.True(legacy.IsStatic);
        Assert.False(legacy.IsModule);
        var identity = Assert.Single(new IdScanner().Scan(module, "legacy-ast.calr").Where(d => d.Name == "Legacy"));
        Assert.Equal(IdKind.Class, identity.Kind);
        Assert.Equal(legacy.SourceId, identity.Id);
    }

    [Fact]
    public void ConvertedStaticClassesUseModuleIdentityWithoutReclassifyingLegacyStaticClasses()
    {
        var result = new CSharpToCalorConverter().Convert(
            "public static class Tools { public static int Work() => 1; }");
        Assert.True(result.Success);
        Assert.True(result.Ast!.Classes.Single().IsModule);
        var file = Write("converted.calr", result.CalorSource!);
        var state = Store.Sync([file]);
        Assert.Equal(IdKind.Module, state.Declarations.Single(d => d.Name == "Tools").Kind);
    }

    [Fact]
    public void ContractReconstructionPreservesBothSourceAndToolingIdentity()
    {
        var source = Original.Replace("§R x", "§Q (== INT:1 INT:1)\n    §R x");
        var file = Write("active.calr", source);
        var state = Store.Sync([file]);
        var ast = CalorSourceHelper.Parse(source, file).Ast!;
        var simplified = new Calor.Compiler.Verification.ContractSimplificationPass(new DiagnosticBag()).Simplify(ast);
        Assert.NotSame(ast.Functions.Single(), simplified.Functions.Single());
        Assert.Equal(ast.Functions.Single().SourceId, simplified.Functions.Single().SourceId);
        Assert.Equal(ast.Functions.Single().Id, simplified.Functions.Single().Id);
        var emitted = new CalorEmitter().Emit(simplified);
        foreach (var declaration in state.Declarations)
            Assert.DoesNotContain(declaration.Id, emitted);
    }

    [Fact]
    public void ApplyRefusesStaleSourcesWithoutChangingSidecar()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var bytes = File.ReadAllBytes(Store.SidecarPath);
        File.AppendAllText(file, "\n");
        Assert.Throws<IOException>(() => Store.Apply([new(file, Original, Original.Replace("Add", "Sum"))]));
        Assert.Equal(bytes, File.ReadAllBytes(Store.SidecarPath));
        Assert.Equal(Original + "\n", File.ReadAllText(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedTransactionsRecoverForwardWithoutChangingIdentity(bool sourceAlreadyWritten)
    {
        var file = Write("active.calr", Original);
        var initial = Store.Sync([file]);
        var beforeSidecar = File.ReadAllText(Store.SidecarPath);
        var after = Original.Replace("Add", "Sum");
        var next = initial with { Declarations = initial.Declarations.Select(d => d.Name == "Add" ? d with { Name = "Sum" } : d).ToArray() };
        var journal = new
        {
            BeforeSidecar = beforeSidecar,
            AfterSidecar = JsonSerializer.Serialize(next),
            Changes = new[] { new DeclarationIdentityStore.SourceChange("active.calr", Original, after) }
        };
        File.WriteAllText(PathFor(".calor/ids.transaction.json"), JsonSerializer.Serialize(journal));
        if (sourceAlreadyWritten) File.WriteAllText(file, after);
        Store.Recover();
        Assert.Equal(after, File.ReadAllText(file));
        Assert.Equal(initial.Declarations.Single(d => d.Name == "Add").Id,
            Store.Sync().Declarations.Single(d => d.Name == "Sum").Id);
        Assert.False(File.Exists(PathFor(".calor/ids.transaction.json")));
    }

    [Fact]
    public void RecoveryRefusesToClobberExternalWrites()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var sidecar = File.ReadAllText(Store.SidecarPath);
        var after = Original.Replace("Add", "Sum");
        File.WriteAllText(PathFor(".calor/ids.transaction.json"), JsonSerializer.Serialize(new
        {
            BeforeSidecar = sidecar, AfterSidecar = sidecar,
            Changes = new[] { new DeclarationIdentityStore.SourceChange("active.calr", Original, after) }
        }));
        File.WriteAllText(file, "external edit");
        Assert.Throws<IOException>(() => Store.Recover());
        Assert.Equal("external edit", File.ReadAllText(file));
        Assert.Equal(sidecar, File.ReadAllText(Store.SidecarPath));
        Assert.True(File.Exists(PathFor(".calor/ids.transaction.json")));
    }

    [Fact]
    public void IndependentBranchAdditionsResolveFromConflictMarkers()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var common = File.ReadAllText(Store.SidecarPath);
        var left = Write("left.calr", Original.Replace("Demo", "Left"));
        var leftState = Store.Sync([left]);
        var leftText = File.ReadAllText(Store.SidecarPath);
        File.WriteAllText(Store.SidecarPath, common);
        var right = Write("right.calr", Original.Replace("Demo", "Right"));
        var rightState = Store.Sync([right]);
        var rightText = File.ReadAllText(Store.SidecarPath);
        File.WriteAllText(Store.SidecarPath, "<<<<<<< ours\n" + leftText + "\n=======\n" + rightText + "\n>>>>>>> theirs\n");
        var merged = Store.Sync();
        Assert.Equal(6, merged.Declarations.Count);
        Assert.Equal(leftState.Declarations.Single(d => d.File == "left.calr" && d.Kind == IdKind.Function).Id,
            merged.Declarations.Single(d => d.File == "left.calr" && d.Kind == IdKind.Function).Id);
        Assert.Equal(rightState.Declarations.Single(d => d.File == "right.calr" && d.Kind == IdKind.Function).Id,
            merged.Declarations.Single(d => d.File == "right.calr" && d.Kind == IdKind.Function).Id);
    }

    [Fact]
    public void SyncRejectsMalformedInputsAndPathEscapes()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var bytes = File.ReadAllBytes(Store.SidecarPath);
        File.WriteAllText(file, "not Calor");
        Assert.Throws<DeclarationIdentityParseException>(() => Store.Sync());
        Assert.Equal(bytes, File.ReadAllBytes(Store.SidecarPath));
        Assert.Throws<InvalidOperationException>(() => Store.Sync([Path.Combine(_root, "..", "outside.calr")]));
    }

    [Fact]
    public void ProjectIndexUsesStableSidecarIdsAfterTextRename()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var first = ProjectSymbolIndex.Build([file], out var firstSkipped);
        var initial = first.Documents.Single().BoundModule.SymbolsById.Values.Single(s => s.Name == "Add").Id;
        File.WriteAllText(file, Original.Replace("Add", "Sum"));
        var second = ProjectSymbolIndex.Build([file], out var secondSkipped);
        Assert.Empty(firstSkipped);
        Assert.Empty(secondSkipped);
        Assert.Equal(initial, second.Documents.Single().BoundModule.SymbolsById.Values.Single(s => s.Name == "Sum").Id);
    }

    [Fact]
    public void DefaultActiveInputSyncPicksUpNewFilesWithoutManualRegistration()
    {
        var first = Write("first.calr", Original);
        DeclarationIdentityStore.SyncActiveInputs([first], _root);
        var originalState = Store.Sync();
        var second = Write("sub/second.calr", Original.Replace("Demo", "Other"));
        DeclarationIdentityStore.SyncActiveInputs([first, second], _root);
        var state = Store.Sync();
        Assert.Equal(2, state.Files.Count);
        Assert.Equal(originalState.Declarations.Single(d => d.Kind == IdKind.Function).Id,
            state.Declarations.Single(d => d.File == "first.calr" && d.Kind == IdKind.Function).Id);
    }

    [Fact]
    public void SnapshotCheckUsesOriginalProjectIdentity()
    {
        var file = Write("active.calr", Original);
        var initial = Store.Sync([file]);
        var copy = Write("snapshot/active.calr", Original);
        DeclarationIdentityStore.CopyToSnapshot(new Dictionary<string, string> { [file] = copy });
        var parsed = CalorSourceHelper.Parse(Original, copy);
        Assert.Equal(initial.Declarations.Single(d => d.Name == "Add").Id, parsed.Ast!.Functions.Single().Id);
    }

    [Fact]
    public async Task McpFileWriteCommitsSourceAndSidecarTogether()
    {
        var file = Write("active.calr", Original);
        var initial = Store.Sync([file]);
        var after = Original.Replace("Add", "Sum");
        var tool = new FileWriteTool(new ProjectSessionManager(), _root);
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new { path = file, content = after }));
        Assert.False(result.IsError, result.Content.Single().Text);
        Assert.Equal(after, File.ReadAllText(file));
        Assert.Equal(initial.Declarations.Single(d => d.Name == "Add").Id,
            Store.Sync().Declarations.Single(d => d.Name == "Sum").Id);
        Assert.False(File.Exists(PathFor(".calor/ids.transaction.json")));
    }

    [Fact]
    public void MigrationWriterPreservesIdentityWithoutManualRegistration()
    {
        var file = PathFor("active.calr");
        ConversionFileWriter.WriteAtomic(file, Original);
        var before = Store.Sync().Declarations.Single(d => d.Name == "Add").Id;
        ConversionFileWriter.WriteAtomic(file, Original.Replace("Add", "Sum"));
        Assert.Equal(before, Store.Sync().Declarations.Single(d => d.Name == "Sum").Id);
    }

    [Fact]
    public void CanonicalSourceIdentityIsImportedWithoutRepointingIt()
    {
        var functionId = CompactIdGenerator.Generate(IdKind.Function);
        var source = Original.Replace("§F{Add", "§F{" + functionId + ":Add");
        var file = Write("active.calr", source);
        var state = Store.Sync([file]);
        Assert.Equal(functionId, state.Declarations.Single(d => d.Name == "Add").Id);
        Assert.Equal(source, File.ReadAllText(file));
    }

    [Fact]
    public void ConcurrentToolingWriterIsRejectedRatherThanOverwritingState()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var bytes = File.ReadAllBytes(Store.SidecarPath);
        using var locked = new FileStream(PathFor(".calor/ids.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => Store.Sync());
        Assert.Equal(bytes, File.ReadAllBytes(Store.SidecarPath));
    }

    [Fact]
    public void CliBuildCreatesIdentityFromItsActualInputs()
    {
        var file = Write("active.calr", Original);
        Write("historical.calr", "frozen input deliberately invalid");
        var result = CliTestHarness.RunCli(_root, "--input", file);
        Assert.True(result.ExitCode == 0, result.StdErr);
        Assert.True(File.Exists(Store.SidecarPath));
        Assert.Equal(["active.calr"], Store.Sync().Files);
        Assert.Equal("frozen input deliberately invalid", File.ReadAllText(PathFor("historical.calr")));
    }

    [Fact]
    public async Task McpRenameUsesPositionsAndKeepsNavigationIdentity()
    {
        var source = Original + "\n  §F{Use:pub} () -> i32\n    §R §C{Add} §A 1 §/C";
        var file = Write("active.calr", source);
        var initial = Store.Sync([file]);
        var sessions = new ProjectSessionManager();
        var tool = new FileWriteTool(sessions, _root);
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "rename", projectDirectory = _root, filePath = file,
            line = 2, column = 7, newName = "Sum"
        }));
        Assert.False(result.IsError, result.Content.Single().Text);
        var updated = File.ReadAllText(file);
        Assert.Contains("§F{Sum:pub}", updated);
        Assert.Contains("§C{Sum}", updated);
        var parsed = CalorSourceHelper.Parse(updated, file);
        Assert.Equal(initial.Declarations.Single(d => d.Name == "Add").Id, parsed.Ast!.Functions.Single(f => f.Name == "Sum").Id);
    }

    [Fact]
    public async Task McpMoveAcrossClassesAndFilesRetainsExactIdentity()
    {
        const string before = """
            §M{Demo}
              §CL{A:pub}
                §MT{Work:pub} (i32:x) -> i32
                  §R x
              §CL{B:pub}
            """;
        const string after = """
            §M{Demo}
              §CL{A:pub}
              §CL{B:pub}
                §MT{Work:pub} (i32:x) -> i32
                  §R x
            """;
        var file = Write("before.calr", before);
        var initial = Store.Sync([file]);
        var destination = PathFor("after.calr");
        var sessions = new ProjectSessionManager();
        var tool = new FileWriteTool(sessions, _root);
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "move", projectDirectory = _root, filePath = file, line = 3, column = 9,
            destinationFile = destination, destinationLine = 4, destinationColumn = 9,
            changes = new[]
            {
                new { path = file, originalSource = (string?)before, modifiedSource = (string?)null },
                new { path = destination, originalSource = (string?)null, modifiedSource = (string?)after }
            }
        }));
        Assert.False(result.IsError, result.Content.Single().Text);
        Assert.False(File.Exists(file));
        Assert.Equal(after, File.ReadAllText(destination));
        Assert.Equal(initial.Declarations.Single(d => d.Name == "Work").Id,
            Store.Sync().Declarations.Single(d => d.Name == "Work").Id);
    }

    [Fact]
    public async Task McpRefactorPreviewDoesNotWriteSourceOrSidecar()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var bytes = File.ReadAllBytes(Store.SidecarPath);
        var sessions = new ProjectSessionManager();
        var tool = new FileWriteTool(sessions, _root);
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "rename", projectDirectory = _root, filePath = file,
            line = 2, column = 7, newName = "Sum", preview = true
        }));
        Assert.False(result.IsError, result.Content.Single().Text);
        Assert.Equal(Original, File.ReadAllText(file));
        Assert.Equal(bytes, File.ReadAllBytes(Store.SidecarPath));
        Assert.False(File.Exists(PathFor(".calor/ids.transaction.json")));
    }

    [Fact]
    public void SignatureEditPreservesIdentityAndAmbiguousOverloadEditsFail()
    {
        var file = Write("active.calr", Original);
        var before = Store.Sync([file]);
        var after = Store.Apply([new(file, Original, Original.Replace("(i32:x)", "(i64:x)"))]);
        Assert.Equal(before.Declarations.Single(d => d.Name == "Add").Id,
            after.Declarations.Single(d => d.Name == "Add").Id);
        Store.Apply([new(file, Original.Replace("(i32:x)", "(i64:x)"), Original)]);
        var overloaded = Original + "\n  §F{Add:pub} (str:x) -> str\n    §R x";
        File.WriteAllText(file, overloaded);
        Store.Sync();
        var sidecar = File.ReadAllBytes(Store.SidecarPath);
        var changed = overloaded.Replace("(i32:x)", "(i64:x)").Replace("(str:x)", "(bool:x)");
        Assert.Throws<InvalidOperationException>(() => Store.Preview([new(file, overloaded, changed)]));
        Assert.Equal(sidecar, File.ReadAllBytes(Store.SidecarPath));
    }

    [Fact]
    public void RenamingAClassPreservesItsOverloadedMembers()
    {
        const string source = """
            §M{Demo}
              §CL{A:pub}
                §MT{One:pub} () -> i32
                  §R 1
                §MT{Two:pub} () -> i32
                  §R 2
            """;
        var file = Write("active.calr", source);
        var before = Store.Sync([file]);
        var after = Store.Apply([new(file, source, source.Replace("§CL{A:pub}", "§CL{B:pub}"))]);
        Assert.Equal(before.Declarations.Select(d => d.Id).Order(), after.Declarations.Select(d => d.Id).Order());
    }

    [Fact]
    public async Task InvalidActiveInputKeepsParserDiagnosticsAndSidecarUntouched()
    {
        var file = Write("active.calr", Original);
        Store.Sync([file]);
        var sidecar = File.ReadAllBytes(Store.SidecarPath);
        File.WriteAllText(file, "§M{Demo}\n  §F{Broken:pub} () -> i32\n    §R (");
        var failure = Assert.Throws<DeclarationIdentityParseException>(() => Store.Sync());
        Assert.All(failure.Diagnostics.Errors, diagnostic => Assert.Equal(file, diagnostic.FilePath));
        Assert.Equal(sidecar, File.ReadAllBytes(Store.SidecarPath));
        var result = await Calor.Compiler.Commands.TestCommand.RunLoopAsync(file);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains(result.Failures, item => item.Diagnostic != null && item.Diagnostic.Code != DiagnosticCode.CliInternalError);
    }

    [Fact]
    public void ConditionalDeclarationsHaveDistinctStableIdentities()
    {
        const string source = """
            §M{Demo}
              §CL{A:pub}
                §PP{DEBUG}
                  §MT{Work:pub} () -> i32
                    §R 1
                §PPE
                  §MT{Work:pub} () -> i32
                    §R 2
            """;
        var file = Write("active.calr", source);
        var before = Store.Sync([file]);
        var methods = before.Declarations.Where(d => d.Kind == IdKind.Method).ToArray();
        Assert.Equal(2, methods.Length);
        Assert.NotEqual(methods[0].Id, methods[1].Id);
        var updated = source.Replace("§MT{Work:pub}", "§MT{Run:pub}");
        var after = Store.Apply([new(file, source, updated)]);
        Assert.Equal(methods.Select(d => d.Id).Order(), after.Declarations.Where(d => d.Kind == IdKind.Method).Select(d => d.Id).Order());
        var parse = CalorSourceHelper.Parse(updated, file);
        Assert.True(parse.IsSuccess);
        Assert.Equal(2, new IdScanner().Scan(parse.Ast!, file).Count(d => d.Kind == IdKind.Method));
    }

    [Theory]
    [InlineData(IdKind.Delegate)]
    [InlineData(IdKind.Event)]
    [InlineData(IdKind.Record)]
    [InlineData(IdKind.Union)]
    [InlineData(IdKind.Field)]
    [InlineData(IdKind.Variant)]
    public void AdditionalDeclarationKindsHaveCanonicalDistinctPrefixes(IdKind kind)
    {
        var id = IdGenerator.Generate(kind);
        Assert.True(IdValidator.IsCanonicalId(id));
        Assert.Equal(kind, IdGenerator.GetKindFromId(id));
        Assert.Equal(IdValidationResult.Valid, IdValidator.Validate(id, kind, isTestPath: false));
    }

    [Fact]
    public void DelegateEventFieldLocalFunctionAndProofIdentitiesSurviveEdits()
    {
        const string source = """
            §M{Demo}
              §DEL{d001:Transform}
                §I{i32:x}
                §O{i32}
              §CL{A:pub}
                §EVT{ev001:Changed:pub:EventHandler}
                §FLD{i32:value:pub}
              §F{Main:pub} () -> i32
                §F{Local:priv} () -> i32
                  §IF{check} true
                    §PROOF{po001:positive} (> 1 0)
                  §R 1
                §R 0
            """;
        var initialParse = CalorSourceHelper.Parse(source);
        Assert.True(initialParse.IsSuccess, string.Join("; ", initialParse.Errors));
        var file = Write("active.calr", source);
        var before = Store.Sync([file]);
        Assert.Contains(before.Declarations, d => d.Kind == IdKind.Delegate);
        Assert.Contains(before.Declarations, d => d.Kind == IdKind.Event);
        Assert.Contains(before.Declarations, d => d.Kind == IdKind.Field);
        Assert.Contains(before.Declarations, d => d.Kind == IdKind.ProofObligation);
        Assert.Contains(before.Declarations, d => d.Name == "Local" && d.Container.Contains("Main"));
        var updated = source.Replace("Transform", "Convert").Replace("Changed", "Updated")
            .Replace("Local", "Inner").Replace("positive", "valid");
        var after = Store.Apply([new(file, source, updated)]);
        Assert.Equal(before.Declarations.Select(d => d.Id).Order(), after.Declarations.Select(d => d.Id).Order());
        var parsed = CalorSourceHelper.Parse(updated, file);
        Assert.True(parsed.IsSuccess, string.Join("; ", parsed.Errors));
        var entries = new IdScanner().Scan(parsed.Ast!, file);
        Assert.Equal(after.Declarations.Select(d => d.Id).Order(), entries.Select(d => d.Id).Order());
        var emitted = new CalorEmitter().Emit(parsed.Ast!);
        Assert.DoesNotContain(after.Declarations.Single(d => d.Kind == IdKind.Event).Id, emitted);
        Assert.DoesNotContain(after.Declarations.Single(d => d.Kind == IdKind.Delegate).Id, emitted);
    }

    [Fact]
    public void ScannerCoversRecordAndUnionAstDeclarationsWithoutInventingSourceSyntax()
    {
        var module = CalorSourceHelper.Parse(Original).Ast!;
        var record = new RecordDefinitionNode(TextSpan.Empty, IdGenerator.Generate(IdKind.Record), "Point",
            [new FieldDefinitionNode(TextSpan.Empty, "X", "i32", null, new AttributeCollection())], new AttributeCollection());
        var union = new UnionTypeDefinitionNode(TextSpan.Empty, IdGenerator.Generate(IdKind.Union), "Shape",
            [new VariantDefinitionNode(TextSpan.Empty, "Circle", [], new AttributeCollection())], new AttributeCollection());
        var ast = module.With(update => update.Items = [record, union]);
        var entries = new IdScanner().Scan(ast, "ast.calr");
        Assert.Contains(entries, e => e.Kind == IdKind.Record && e.Name == "Point");
        Assert.Contains(entries, e => e.Kind == IdKind.Union && e.Name == "Shape");
    }
}
