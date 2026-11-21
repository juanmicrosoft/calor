using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.CodeGen;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Calor.Compiler.Mcp.Sessions;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Migration;
using Calor.Compiler.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class EnumExtensionIdentityMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-enum-migration-" + Guid.NewGuid().ToString("N"));
    private DeclarationIdentityStore Store => new(_root);
    private string FilePath => Path.Combine(_root, "Extensions.calr");

    private const string Before = """
        §M{Extensions}
          §EN{Mode}
            First = 1
          §EEXT{Mode}
            §F{Read:pub} (Mode:self, i32:delta) → i32
              §E{}
              §R delta
        """;

    [Theory]
    [InlineData("")]
    [InlineData(" Native")]
    public void MigrationPreservesAllocatedIdsReloadsAndMatchesIndependentClrOracle(string namespaceHeader)
    {
        var original = Original(namespaceHeader);
        var initial = Seed(original);
        var oldCode = new CSharpEmitter().Emit(original);
        var migrated = Store.MigrateEnumExtensions(FilePath, Before, original);
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            migrated.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        var oldOwner = initial.Declarations.Single(declaration => declaration.Kind == IdKind.EnumExtension);
        var newOwner = migrated.Declarations.Single(declaration => declaration.Id == oldOwner.Id);
        Assert.Equal(IdKind.Module, newOwner.Kind);
        Assert.Equal(IdKind.EnumExtension, newOwner.AllocationKind);
        var oldMethod = initial.Declarations.Single(declaration => declaration.Kind == IdKind.Function);
        var newMethod = migrated.Declarations.Single(declaration => declaration.Id == oldMethod.Id);
        Assert.Equal(IdKind.Method, newMethod.Kind);
        Assert.Equal(IdKind.Function, newMethod.AllocationKind);
        var source = File.ReadAllText(FilePath);
        Assert.DoesNotContain('§', source);
        var attached = CalorSourceHelper.Parse(source, FilePath).Ast!;
        Assert.Equal(oldOwner.Id, attached.Classes.Single().Id);
        Assert.Equal(oldMethod.Id, attached.Classes.Single().Methods.Single().Id);
        Assert.True(IdChecker.Check(new IdScanner().Scan(attached, FilePath)).IsValid);
        Assert.Equal(migrated.Declarations, new DeclarationIdentityStore(_root).Sync().Declarations);
        var compiled = Program.Compile(source);
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var ownerName = namespaceHeader.Length == 0 ? "ModeExtensions" : "Native.ModeExtensions";
        Assert.Equal(Observe(oldCode, ownerName), Observe(compiled.GeneratedCode, ownerName));

        File.WriteAllText(FilePath, source.Replace("Read", "Load", StringComparison.Ordinal));
        var renamed = Store.Sync();
        Assert.Equal(oldMethod.Id, renamed.Declarations.Single(declaration => declaration.Name == "Load").Id);
        var destination = Path.Combine(_root, "Moved.calr");
        File.Move(FilePath, destination);
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            Store.Sync([destination]).Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ArbitraryFunctionToMethodRelocationIsStillRejected()
    {
        const string before = "module Native\n  func Read () → int\n    !\n    return 7";
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, before);
        var initial = Store.Sync([FilePath]);
        const string after = "module Native\n  module Owner\n    func Read () → int\n      !\n      return 7";
        var target = DeclarationIdentityStore.Describe("Extensions.calr", after)
            .Single(declaration => declaration.Kind == IdKind.Method);
        var method = initial.Declarations.Single(declaration => declaration.Kind == IdKind.Function);
        var sidecar = File.ReadAllText(Store.SidecarPath);
        Assert.Throws<InvalidOperationException>(() => Store.Apply(
            [new(FilePath, before, after)], [new(method.Id, target.Key, AllowKindMigration: true)]));
        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Equal(sidecar, File.ReadAllText(Store.SidecarPath));
    }

    [Fact]
    public void SuffixCollisionCannotSelectOrReplaceAnExistingOwner()
    {
        var original = Original("");
        var owner = Parse("module Extensions\n  module ModeExtensions").Classes.Single();
        original = original.With(update =>
        {
            update.Classes = [owner];
            update.Items = original.Items.Append(owner).ToArray();
        });
        Seed(original);
        var sidecar = File.ReadAllText(Store.SidecarPath);
        Assert.Throws<DeclarationIdentityParseException>(() => Store.MigrateEnumExtensions(FilePath, Before, original));
        Assert.Equal(Before, File.ReadAllText(FilePath));
        Assert.Equal(sidecar, File.ReadAllText(Store.SidecarPath));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MigrationRequiresExplicitPermissionAndTheMigratedOwner(bool permission, bool wrongOwner)
    {
        var original = Original("");
        var previous = Seed(original);
        var after = new CalorEmitter().Emit(original) + """

              module Other
                func Read (Mode:self, int:delta) → int
                  !
                  return delta
            """;
        var destinations = DeclarationIdentityStore.Describe("Extensions.calr", after);
        var owner = destinations.Single(declaration => declaration.Kind == IdKind.Module && declaration.Name == "ModeExtensions");
        var method = destinations.Single(declaration => declaration.Kind == IdKind.Method
            && declaration.Container.Contains(wrongOwner ? ".Other`" : ".ModeExtensions`", StringComparison.Ordinal));
        var oldOwner = previous.Declarations.Single(declaration => declaration.Kind == IdKind.EnumExtension);
        var oldMethod = previous.Declarations.Single(declaration => declaration.Kind == IdKind.Function);
        var sidecar = File.ReadAllText(Store.SidecarPath);
        Assert.Throws<InvalidOperationException>(() => Store.Apply([new(FilePath, Before, after)],
            [new(oldOwner.Id, owner.Key, permission), new(oldMethod.Id, method.Key, permission)]));
        Assert.Equal(Before, File.ReadAllText(FilePath));
        Assert.Equal(sidecar, File.ReadAllText(Store.SidecarPath));
    }

    [Fact]
    public void UnsupportedAllocationKindCannotBypassPersistedPrefixValidation()
    {
        const string source = "module Native\n  class Owner";
        Directory.CreateDirectory(_root);
        File.WriteAllText(FilePath, source);
        var previous = Store.Sync([FilePath]);
        var corrupt = previous with
        {
            Declarations = previous.Declarations.Select(declaration => declaration.Kind == IdKind.Class
                ? declaration with { AllocationKind = IdKind.Function }
                : declaration).ToArray()
        };
        File.WriteAllText(Store.SidecarPath, JsonSerializer.Serialize(corrupt));
        Assert.Throws<InvalidDataException>(() => Store.Sync());
        Assert.Equal(source, File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryRetainsMigrationProvenanceAndRefusesExternalWrites(bool externalWrite)
    {
        var original = Original("");
        Seed(original);
        var beforeSidecar = File.ReadAllText(Store.SidecarPath);
        var migrated = Store.MigrateEnumExtensions(FilePath, Before, original);
        var afterSidecar = File.ReadAllText(Store.SidecarPath);
        var afterSource = File.ReadAllText(FilePath);
        File.WriteAllText(Store.SidecarPath, beforeSidecar);
        var journal = Path.Combine(_root, ".calor", "ids.transaction.json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            BeforeSidecar = beforeSidecar,
            AfterSidecar = afterSidecar,
            Changes = new[] { new DeclarationIdentityStore.SourceChange("Extensions.calr", Before, afterSource) }
        }));
        if (externalWrite)
        {
            var external = afterSource + "\n// external";
            File.WriteAllText(FilePath, external);
            Assert.Throws<IOException>(() => Store.Recover());
            Assert.Equal(external, File.ReadAllText(FilePath));
            Assert.Equal(beforeSidecar, File.ReadAllText(Store.SidecarPath));
            Assert.True(File.Exists(journal));
        }
        else
        {
            Store.Recover();
            Assert.False(File.Exists(journal));
            Assert.Equal(afterSource, File.ReadAllText(FilePath));
            Assert.Equal(migrated.Declarations, Store.Sync().Declarations);
        }
    }

    [Fact]
    public void MigrationPreservesSelectedMethodMetadataWithoutAllocatingReplacementIds()
    {
        var original = Original("", """
              /// Explains the helper.
              /// since 1.2.0
              /// deprecated 1.4.0 ReadNext
              /// breaking 2.0.0 "Use \"options\"\nNext line"
            """);
        var initial = Seed(original);
        var previous = original.EnumExtensions.Single().Methods.Single();
        var migrated = Store.MigrateEnumExtensions(FilePath, Before, original);
        var source = File.ReadAllText(FilePath);
        var method = CalorSourceHelper.Parse(source, FilePath).Ast!.Classes.Single().Methods.Single();
        Assert.Equal(previous.Since!.Version, method.Since!.Version);
        Assert.Equal(previous.Deprecated!.Replacement, method.Deprecated!.Replacement);
        Assert.Equal(previous.BreakingChanges.Single().Description, method.BreakingChanges.Single().Description);
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            migrated.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        Assert.Equal(migrated.Declarations, Store.Sync().Declarations);
        var reemitted = new CalorEmitter().Emit(CalorSourceHelper.Parse(source, FilePath).Ast!);
        Store.Apply([new(FilePath, source, reemitted)]);
        Assert.Equal(migrated.Declarations, Store.Sync().Declarations);
        Assert.Contains("[System.Obsolete(", Program.Compile(reemitted).GeneratedCode);
    }

    [Fact]
    public void LoweringRetainsRetiredGuidanceAsOrdinaryDocumentation()
    {
        var original = Original("");
        var old = original.EnumExtensions.Single();
        var function = old.Methods.Single();
        var issue = new IssueNode(function.Span, IssueKind.Todo, null, "perf", IssuePriority.High,
            "Optimize allocation.\nKeep receiver order.", new AttributeCollection());
        var annotated = new FunctionNode(function.Span, function.LocalId, function.Name, function.Visibility,
            function.TypeParameters, function.Parameters, function.Output, function.Effects,
            function.Preconditions, function.Postconditions, function.Body, function.Attributes,
            [], [issue], null, null, [], null, null, null, [], [], null, null, null)
        {
            DocComment = "Explains the helper."
        };
        var extension = new EnumExtensionNode(old.Span, old.LocalId, old.EnumName, [annotated], old.Attributes);
        var lowered = CalorEmitter.LowerEnumExtension(extension);
        Assert.Contains("Explains the helper.", lowered.Methods.Single().DocComment);
        var module = original.With(update =>
        {
            update.EnumExtensions = [];
            update.Classes = [lowered];
            update.Items = [.. original.Enums, lowered];
        });
        var source = new CalorEmitter().Emit(module);
        Assert.Contains("/// Todo perf (High): Optimize allocation.", source);
        Assert.Contains("/// Keep receiver order.", source);
        Assert.DoesNotContain('§', source);
        Assert.Contains("Optimize allocation.", Parse(source).Classes.Single().Methods.Single().DocComment);
    }

    [Fact]
    public async Task McpPreviewRenameAndFileMoveRetainMigratedAllocationKinds()
    {
        var original = Original("");
        var initial = Seed(original);
        Store.MigrateEnumExtensions(FilePath, Before, original);
        var source = File.ReadAllText(FilePath);
        var offset = source.IndexOf("func Read", StringComparison.Ordinal) + "func ".Length;
        var line = source[..offset].Count(character => character == '\n') + 1;
        var column = offset - source.LastIndexOf('\n', offset - 1);
        var sessions = new ProjectSessionManager();
        var tool = new FileWriteTool(sessions, _root);
        var sidecar = File.ReadAllText(Store.SidecarPath);
        foreach (var preview in new[] { true, false })
        {
            var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
            {
                action = "rename", projectDirectory = _root, filePath = FilePath,
                line, column, newName = "Load", preview
            }));
            Assert.False(result.IsError, result.Content.Single().Text);
            if (preview)
            {
                Assert.Equal(source, File.ReadAllText(FilePath));
                Assert.Equal(sidecar, File.ReadAllText(Store.SidecarPath));
            }
        }
        var renamed = File.ReadAllText(FilePath);
        Assert.Contains("func Load", renamed);
        var destination = Path.Combine(_root, "Moved.calr");
        var moved = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "move", projectDirectory = _root, filePath = FilePath, line, column,
            destinationFile = destination, destinationLine = line, destinationColumn = column,
            changes = new[]
            {
                new { path = FilePath, originalSource = (string?)renamed, modifiedSource = (string?)null },
                new { path = destination, originalSource = (string?)null, modifiedSource = (string?)renamed }
            }
        }));
        Assert.False(moved.IsError, moved.Content.Single().Text);
        Assert.False(File.Exists(FilePath));
        var final = Store.Sync();
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            final.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        var method = final.Declarations.Single(declaration => declaration.Name == "Load");
        Assert.Equal(IdKind.Method, method.Kind);
        Assert.Equal(IdKind.Function, method.AllocationKind);
        Assert.Equal(IdKind.EnumExtension, final.Declarations.Single(declaration =>
            declaration.Name == "ModeExtensions").AllocationKind);
        var attached = CalorSourceHelper.Parse(File.ReadAllText(destination), destination).Ast!;
        Assert.True(IdChecker.Check(new IdScanner().Scan(attached, destination)).IsValid);
    }

    [Theory]
    [InlineData("class Owner\n    module Nested\n      func Read (this Mode:self) → int\n        !\n        return 7")]
    [InlineData("module Owner<T>\n    func Read (this Mode:self) → int\n      !\n      return 7")]
    public void NativeExtensionOwnersStillRejectNestedAndGenericContainers(string declaration)
    {
        var compiled = Program.Compile("module Native\n  enum Mode\n    First\n  " + declaration);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCode.InvalidReference);
    }

    private DeclarationIdentityStore.State Seed(ModuleNode original)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, ".calor"));
        File.WriteAllText(FilePath, Before);
        var declarations = DeclarationIdentityStore.DescribeAst("Extensions.calr", original)
            .Select(declaration => declaration with { Id = CompactIdGenerator.Generate(declaration.Kind) }).ToArray();
        var state = new DeclarationIdentityStore.State(DeclarationIdentityStore.SchemaVersion, ["Extensions.calr"], declarations);
        File.WriteAllText(Store.SidecarPath, JsonSerializer.Serialize(state));
        return state;
    }

    private static ModuleNode Original(string namespaceHeader, string? metadata = null)
    {
        var source = $"""
            module Extensions{namespaceHeader}
              enum Mode
                First = 1
              func Read (Mode:self, int:delta) → int
                !
                return delta
            """;
        if (metadata != null)
            source = source.Replace("  func Read", metadata + "\n  func Read", StringComparison.Ordinal);
        var module = Parse(source);
        var function = module.Functions.Single();
        var extension = new EnumExtensionNode(function.Span, "legacy-extension", "Mode", [function], new AttributeCollection())
        {
            NamespaceIdentity = function.NamespaceIdentity,
            NamespaceScopeId = function.NamespaceScopeId
        };
        return module.With(update =>
        {
            update.Functions = [];
            update.EnumExtensions = [extension];
            update.Items = module.Items.Select(item => ReferenceEquals(item, function) ? extension : item).ToArray();
        });
    }

    private static ModuleNode Parse(string source)
    {
        var diagnostics = new DiagnosticBag();
        var module = new Parser(new Lexer(source, diagnostics).TokenizeAllForParser(), diagnostics).Parse();
        Assert.False(diagnostics.HasErrors, string.Join("\n", diagnostics));
        return module;
    }

    private static string Observe(string source, string ownerName)
    {
        var compilation = CSharpCompilation.Create("ExtensionOracle",
            [CSharpSyntaxTree.ParseText(source)],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext("ExtensionOracle", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var owner = assembly.GetType(ownerName, throwOnError: true)!;
            var method = owner.GetMethod("Read")!;
            var parameters = method.GetParameters();
            Assert.True(method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute)));
            Assert.Equal(["self", "delta"], parameters.Select(parameter => parameter.Name));
            Assert.Equal(42, method.Invoke(null, [Enum.ToObject(parameters[0].ParameterType, 1), 42]));
            return owner.FullName + ":" + method.Name + ":" + method.ReturnType.FullName + ":"
                + string.Join(",", parameters.Select(parameter => parameter.ParameterType.FullName));
        }
        finally { context.Unload(); }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
