using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Ids.Tests;

public class HookIdValidationTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), $"calor-id-hook-test-{Guid.NewGuid():N}");

    public HookIdValidationTests() => Directory.CreateDirectory(_testDirectory);

    public void Dispose() => Directory.Delete(_testDirectory, recursive: true);

    [Fact]
    public void ValidateIds_AllowsValidCurrentContentWithoutChangingSidecar()
    {
        const string content = "§M{TestModule}\n  §F{TestFunc:pub} () -> void\n";
        var (file, store) = Register(content);
        var before = File.ReadAllText(store.SidecarPath);

        var (exitCode, reason) = HookCommand.ValidateIds(CreateToolInput(file, content));

        Assert.Equal(0, exitCode);
        Assert.Null(reason);
        Assert.Equal(before, File.ReadAllText(store.SidecarPath));
        Assert.Equal(content, File.ReadAllText(file));
    }

    [Fact]
    public void ValidateIds_AllowsNonCalorFiles()
    {
        var (exitCode, _) = HookCommand.ValidateIds(CreateToolInput(
            Path.Combine(_testDirectory, "test.cs"), "public class Test {}"));
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_AllowsIdShapedSemanticNames()
    {
        const string content = "§M{m001}\n  §F{f001:pub} () -> void\n";
        var (file, _) = Register(content);
        var (exitCode, reason) = HookCommand.ValidateIds(CreateToolInput(file, content));
        Assert.Equal(0, exitCode);
        Assert.Null(reason);
    }

    [Fact]
    public void ValidateIds_BlocksDuplicatePersistedIdsWithoutWritingAnything()
    {
        const string content = "§M{TestModule}\n  §F{First:pub} () -> void\n  §F{Second:pub} () -> void\n";
        var (file, store) = Register(content);
        var functions = store.Sync().Declarations.Where(d => d.Kind == IdKind.Function).ToArray();
        var corrupt = File.ReadAllText(store.SidecarPath).Replace(functions[1].Id, functions[0].Id);
        File.WriteAllText(store.SidecarPath, corrupt);

        var (exitCode, reason) = HookCommand.ValidateIds(CreateToolInput(file, content));

        Assert.Equal(1, exitCode);
        Assert.Contains("duplicate or invalid declarations", reason);
        var compilation = Calor.Compiler.Program.Compile(content, file);
        Assert.Contains(compilation.Diagnostics, d => d.Code == DiagnosticCode.DeclarationIdentityUnavailable);
        Assert.Empty(compilation.GeneratedCode);
        Assert.Equal(corrupt, File.ReadAllText(store.SidecarPath));
        Assert.Equal(content, File.ReadAllText(file));
    }

    [Fact]
    public void ValidateIds_BlocksMalformedIdentityStorage()
    {
        const string content = "§M{TestModule}\n";
        var (file, store) = Register(content);
        File.WriteAllText(store.SidecarPath, "{ malformed");

        var (exitCode, reason) = HookCommand.ValidateIds(CreateToolInput(file, content));

        Assert.Equal(1, exitCode);
        Assert.Contains("identity", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("{ malformed", File.ReadAllText(store.SidecarPath));
    }

    [Fact]
    public void ValidateIds_BlocksAmbiguousContinuityWithoutWritingAnything()
    {
        const string content = "§M{TestModule}\n  §F{First:pub} () -> void\n  §F{Second:pub} () -> void\n";
        var (file, store) = Register(content);
        var before = File.ReadAllText(store.SidecarPath);
        var proposed = content.Replace("First", "RenamedFirst").Replace("Second", "RenamedSecond");

        var (exitCode, reason) = HookCommand.ValidateIds(CreateToolInput(file, proposed));

        Assert.Equal(1, exitCode);
        Assert.Contains("ambiguous", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(store.SidecarPath));
        Assert.Equal(content, File.ReadAllText(file));
    }

    [Fact]
    public void ValidateIds_AllowsNewFunctionsWithoutMintingPersistedIds()
    {
        const string content = "§M{TestModule}\n  §F{TestFunc:pub} () -> void\n";
        var (file, store) = Register(content);
        var before = File.ReadAllText(store.SidecarPath);
        var (exitCode, _) = HookCommand.ValidateIds(CreateToolInput(file,
            content + "  §F{NewFunc:pub} () -> void\n"));
        Assert.Equal(0, exitCode);
        Assert.Equal(before, File.ReadAllText(store.SidecarPath));
        Assert.Equal(content, File.ReadAllText(file));
    }

    [Fact]
    public void ValidateIds_AllowsEmptyContent()
    {
        var (exitCode, _) = HookCommand.ValidateIds(CreateToolInput(
            Path.Combine(_testDirectory, "test.calr"), ""));
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ValidateIds_AllowsInvalidToolInputJson()
    {
        var (exitCode, _) = HookCommand.ValidateIds("not valid json");
        Assert.Equal(0, exitCode);
    }

    private (string File, DeclarationIdentityStore Store) Register(string source)
    {
        var file = Path.Combine(_testDirectory, "test.calr");
        File.WriteAllText(file, source);
        var store = new DeclarationIdentityStore(_testDirectory);
        store.Sync([file]);
        return (file, store);
    }

    private static string CreateToolInput(string filePath, string content) =>
        System.Text.Json.JsonSerializer.Serialize(new { file_path = filePath, content });
}
