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
    public void NativeSyncAndAttachmentPreserveLiteralTestIdentitiesInTestInputs()
    {
        var file = Write("tests/Contract.calr", Source);
        var initial = Store.Sync([file]);
        Persist(WithTestIds(initial));
        var reconciled = Store.Sync();
        Assert.Equal(["m001", "f001"], reconciled.Declarations.Select(declaration => declaration.Id));
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
        Assert.Equal(["m001", "f001"], state.Declarations.Select(declaration => declaration.Id));
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
