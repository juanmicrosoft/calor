using System.Text.Json;
using Calor.Compiler.Ast;
using Calor.Compiler.Ids;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Mcp.Sessions;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class CanonicalDeclarationIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "calor-native-identity-" + Guid.NewGuid().ToString("N"));
    private DeclarationIdentityStore Store => new(_root);

    private string Write(string file, string source)
    {
        var path = Path.Combine(_root, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    private const string Source = """
        module ContractExamples
          func Sqrt (int:x) → int
            return x
        """;

    private void Persist(DeclarationIdentityStore.State state)
        => File.WriteAllText(Store.SidecarPath, JsonSerializer.Serialize(state));

    private static DeclarationIdentityStore.State WithTestIds(DeclarationIdentityStore.State state)
        => state with
        {
            Declarations = state.Declarations.Select(declaration => declaration with
            {
                Id = declaration.Kind == IdKind.Module ? "m001" : "f001"
            }).ToArray()
        };

    [Fact]
    public void EnumsDelegatesAndEventsKeepIdentityAcrossEmissionAndMove()
    {
        const string source = """
            module Native
              enum Mode byte
                @event = 7
              delegate Handler (int:value) → void
              class Source
                event Handler:Changed
            """;
        var file = Write("src/Events.calr", source);
        var initial = Store.Sync([file]);
        Assert.Single(initial.Declarations.Where(declaration => declaration.Kind == IdKind.Enum));
        Assert.Single(initial.Declarations.Where(declaration => declaration.Kind == IdKind.Delegate));
        Assert.Single(initial.Declarations.Where(declaration => declaration.Kind == IdKind.Event));
        var parsed = CalorSourceHelper.Parse(source, file).Ast!;
        File.WriteAllText(file, new Calor.Compiler.Migration.CalorEmitter().Emit(parsed));
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            Store.Sync().Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        var moved = Path.Combine(_root, "src", "Moved.calr");
        File.Move(file, moved);
        var reconciled = Store.Sync([moved]);
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            reconciled.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        Assert.All(reconciled.Declarations, declaration => Assert.Equal("src/Moved.calr", declaration.File));
    }

    [Fact]
    public void OperatorsIndexersAndConditionalMembersKeepIdentityAcrossEmissionAndMove()
    {
        const string source = """
            module Native
              class Counter
                op + (Counter:a, Counter:b) → Counter
                  return a
                this[int:i] → int
                  get
                    return i
                pp DEBUG
                  func Probe () → int
                    return 1
                pp else
                  func Probe () → int
                    return 2
            """;
        var file = Write("src/Counter.calr", source);
        var initial = Store.Sync([file]);
        Assert.Single(initial.Declarations.Where(declaration => declaration.Kind == IdKind.OperatorOverload));
        Assert.Single(initial.Declarations.Where(declaration => declaration.Kind == IdKind.Indexer));
        var methods = initial.Declarations.Where(declaration => declaration.Name == "Probe").ToArray();
        Assert.Equal(2, methods.Length);
        Assert.NotEqual(methods[0].Id, methods[1].Id);
        var parsed = CalorSourceHelper.Parse(source, file).Ast!;
        File.WriteAllText(file, new Calor.Compiler.Migration.CalorEmitter().Emit(parsed));
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            Store.Sync().Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        var moved = Path.Combine(_root, "src", "Moved.calr");
        File.Move(file, moved);
        var reconciled = Store.Sync([moved]);
        Assert.Equal(initial.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal),
            reconciled.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        Assert.All(reconciled.Declarations, declaration => Assert.Equal("src/Moved.calr", declaration.File));
    }

    [Fact]
    public void NativeSyncAndAttachmentPreserveLiteralTestIdentitiesInTestInputs()
    {
        var file = Write("tests/Contract.calr", Source);
        var initial = Store.Sync([file]);
        Persist(WithTestIds(initial));
        var reconciled = Store.Sync();
        Assert.Equal(["f001", "m001"], reconciled.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        var ast = CalorSourceHelper.Parse(Source, file).Ast!;
        Assert.Equal("m001", ast.Id);
        Assert.Equal("f001", ast.Functions.Single().Id);
        Assert.Equal(Source, File.ReadAllText(file));
        Assert.Equal(reconciled.Declarations, Store.Sync().Declarations);
    }

    [Theory]
    [InlineData("src/Contract.calr", false)]
    [InlineData("tests/Contract.calr", true)]
    public void TestIdentityCompatibilityCannotBypassProductionOrKindValidation(string file, bool wrongKind)
    {
        var path = Write(file, Source);
        var initial = Store.Sync([path]);
        var seeded = WithTestIds(initial);
        if (wrongKind)
            seeded = seeded with
            {
                Declarations = seeded.Declarations.Select(declaration => declaration.Kind == IdKind.Module
                    ? declaration with { Id = "f002" } : declaration).ToArray()
            };
        Persist(seeded);
        var before = File.ReadAllText(Store.SidecarPath);
        Assert.Throws<InvalidDataException>(() => Store.Sync());
        Assert.Equal(before, File.ReadAllText(Store.SidecarPath));
        Assert.Equal(Source, File.ReadAllText(path));
    }

    [Fact]
    public void NativeTextRenameAndMovePreserveExistingLiteralIds()
    {
        var file = Write("tests/Contract.calr", Source);
        Persist(WithTestIds(Store.Sync([file])));
        File.WriteAllText(file, Source.Replace("Sqrt", "Root", StringComparison.Ordinal));
        Assert.Equal("f001", Store.Sync().Declarations.Single(declaration => declaration.Kind == IdKind.Function).Id);
        var moved = Path.Combine(_root, "tests", "Moved.calr");
        File.Move(file, moved);
        var state = Store.Sync([moved]);
        Assert.Equal(["f001", "m001"], state.Declarations.Select(declaration => declaration.Id).Order(StringComparer.Ordinal));
        Assert.All(state.Declarations, declaration => Assert.Equal("tests/Moved.calr", declaration.File));
    }

    [Fact]
    public void NativeAmbiguousTextRenamesDoNotRewriteTheSidecar()
    {
        var source = Source + "\n  func Other (int:x) → int\n    return x";
        var file = Write("tests/Contract.calr", source);
        var initial = Store.Sync([file]);
        Persist(initial with
        {
            Declarations = initial.Declarations.Select(declaration => declaration with
            {
                Id = declaration.Kind == IdKind.Module ? "m001" : declaration.Name == "Sqrt" ? "f001" : "f002"
            }).ToArray()
        });
        var before = File.ReadAllText(Store.SidecarPath);
        var changed = source.Replace("Sqrt", "Root", StringComparison.Ordinal)
            .Replace("Other", "Again", StringComparison.Ordinal);
        File.WriteAllText(file, changed);
        Assert.Throws<InvalidOperationException>(() => Store.Sync());
        Assert.Equal(before, File.ReadAllText(Store.SidecarPath));
        Assert.Equal(changed, File.ReadAllText(file));
    }

    [Fact]
    public async Task NativeMcpRenamePreservesSeededIdentityAndReferenceNavigation()
    {
        var source = Source + "\n  func Use () → int\n    return Sqrt(1)";
        var file = Write("tests/Contract.calr", source);
        var initial = Store.Sync([file]);
        Persist(initial with
        {
            Declarations = initial.Declarations.Select(declaration => declaration with
            {
                Id = declaration.Kind == IdKind.Module ? "m001" : declaration.Name == "Sqrt" ? "f001" : "f002"
            }).ToArray()
        });
        var tool = new FileWriteTool(new ProjectSessionManager(), _root);
        var result = await tool.ExecuteAsync(JsonSerializer.SerializeToElement(new
        {
            action = "rename", projectDirectory = _root, filePath = file,
            line = 2, column = 8, newName = "Root"
        }));
        Assert.False(result.IsError, result.Content.Single().Text);
        var updated = File.ReadAllText(file);
        Assert.Contains("func Root", updated);
        Assert.Contains("return Root(1)", updated);
        var parsed = CalorSourceHelper.Parse(updated, file).Ast!;
        Assert.Equal("f001", parsed.Functions.Single(function => function.Name == "Root").Id);
        Assert.Equal("f002", parsed.Functions.Single(function => function.Name == "Use").Id);
    }

    [Fact]
    public void NamedStaticModuleKindsAndNestedLineageSurviveCanonicalRename()
    {
        const string source = """
            module Unit
              module Tools
                class Nested
                  field int:Value
                func Read () → int
                  !
                  return 1
            """;
        var file = Write("tests/Modules.calr", source);
        var before = Store.Sync([file]);
        var ast = CalorSourceHelper.Parse(source, file).Ast!;
        var namedModule = ast.Classes.Single();
        Assert.True(namedModule.IsModule);
        Assert.True(namedModule.IsStatic);
        Assert.False(namedModule.NestedClasses.Single().IsModule);
        Assert.True(namedModule.Methods.Single().IsStatic);
        Assert.Equal(2, before.Declarations.Count(declaration => declaration.Kind == IdKind.Module));
        File.WriteAllText(file, source.Replace("Tools", "Utilities", StringComparison.Ordinal));
        var after = Store.Sync();
        Assert.Equal(before.Declarations.Select(declaration => declaration.Id).Order(),
            after.Declarations.Select(declaration => declaration.Id).Order());
        Assert.Equal(before.Declarations.Single(declaration => declaration.Name == "Tools").Id,
            after.Declarations.Single(declaration => declaration.Name == "Utilities").Id);
    }

    [Fact]
    public void FreshNativeDeclarationsStillReceiveCanonicalIds()
    {
        var path = Write("tests/Contract.calr", Source);
        var state = Store.Sync([path]);
        Assert.All(state.Declarations, declaration => Assert.True(IdValidator.IsCanonicalId(declaration.Id)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
