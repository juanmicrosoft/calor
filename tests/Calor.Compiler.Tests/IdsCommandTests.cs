using System.Text.Json;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Ids;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class IdsCommandTests : IDisposable
{
    private readonly string _directory = Path.GetFullPath(Path.Combine(
        ".calor-test-workspaces", $"ids-command-{Guid.NewGuid():N}"));
    private const string Source = "§M{Test}\n  §F{Foo:pub} () -> void\n";

    public IdsCommandTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "Directory.Build.props"), "<Project />");
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteSource()
    {
        var file = Path.Combine(_directory, "active.calr");
        File.WriteAllText(file, Source);
        return file;
    }

    [Fact]
    public void Assign_IsNotACommand()
    {
        var file = WriteSource();
        var (exit, _, stderr) = CliTestHarness.RunCli(_directory, "ids", "assign", file);
        Assert.NotEqual(0, exit);
        Assert.Contains("calor ids sync", stderr);
        Assert.Equal(Source, File.ReadAllText(file));
        Assert.False(File.Exists(Path.Combine(_directory, DeclarationIdentityStore.RelativePath)));
    }

    [Fact]
    public void Check_UnknownOption_RemainsAUsageError()
    {
        var file = WriteSource();
        var (exit, _, stderr) = CliTestHarness.RunCli(_directory, "ids", "check", file, "--retired-option");
        Assert.NotEqual(0, exit);
        Assert.Contains("--retired-option", stderr);
        Assert.False(File.Exists(new DeclarationIdentityStore(_directory).SidecarPath));
    }

    [Fact]
    public void Check_ExplicitActiveFile_UsesStableSidecarIdsWithoutRewritingSource()
    {
        var file = WriteSource();
        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "ids", "check", file, "--format", "json");
        Assert.Equal(0, exit);
        using var output = JsonDocument.Parse(stdout);
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(output.RootElement);
        Assert.Equal(2, output.RootElement.GetProperty("data").GetProperty("totalIds").GetInt32());
        Assert.Empty(output.RootElement.GetProperty("diagnostics").EnumerateArray());
        var store = new DeclarationIdentityStore(_directory);
        var before = File.ReadAllText(store.SidecarPath);

        var (secondExit, _, _) = CliTestHarness.RunCli(_directory, "ids", "check", file);
        Assert.Equal(0, secondExit);
        Assert.Equal(before, File.ReadAllText(store.SidecarPath));
        Assert.Equal(Source, File.ReadAllText(file));
    }

    [Fact]
    public void Check_Directory_DoesNotEnrollUnregisteredHistoricalFiles()
    {
        var file = WriteSource();
        var store = new DeclarationIdentityStore(_directory);
        store.Sync([file]);
        var historical = Path.Combine(_directory, "frozen.calr");
        const string frozen = "original historical bytes, not current syntax";
        File.WriteAllText(historical, frozen);

        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "ids", "check", _directory, "--format", "json");
        Assert.Equal(0, exit);
        using var output = JsonDocument.Parse(stdout);
        Assert.Equal(2, output.RootElement.GetProperty("data").GetProperty("totalIds").GetInt32());
        Assert.Equal(["active.calr"], store.Sync().Files);
        Assert.Equal(frozen, File.ReadAllText(historical));
    }

    [Theory]
    [InlineData("check", "registered")]
    [InlineData("check", "empty")]
    [InlineData("check", "missing")]
    [InlineData("index", "registered")]
    [InlineData("index", "empty")]
    [InlineData("index", "missing")]
    public void Directory_RecoversMatchingCurrentAndPendingRegistrations(string command, string registration)
    {
        var file = WriteSource();
        var store = new DeclarationIdentityStore(_directory);
        store.Sync(registration == "registered" ? [file] : []);
        string? beforeSidecar = File.ReadAllText(store.SidecarPath);
        const string updated = "§M{Test}\n  §F{Bar:pub} () -> void\n";
        if (registration == "missing") File.Delete(file);
        var next = store.Preview(
            [new(file, registration == "missing" ? null : Source, updated)]);
        if (registration == "missing")
        {
            File.Delete(store.SidecarPath);
            beforeSidecar = null;
        }
        var journal = Path.Combine(_directory, ".calor", "ids.transaction.json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new
        {
            BeforeSidecar = beforeSidecar,
            AfterSidecar = JsonSerializer.Serialize(next),
            Changes = new[]
            {
                new DeclarationIdentityStore.SourceChange("active.calr",
                    registration == "missing" ? null : Source, updated)
            }
        }));

        var args = command == "check"
            ? new[] { "ids", command, _directory, "--format", "json" }
            : ["ids", command, _directory, "--output", Path.Combine(_directory, "index.json")];
        var (exit, stdout, stderr) = CliTestHarness.RunCli(_directory, args);
        Assert.True(exit == 0, $"Exit {exit}; stdout: {stdout}; stderr: {stderr}");
        Assert.Equal(updated, File.ReadAllText(file));
        Assert.False(File.Exists(journal));
        Assert.Equal(next.Declarations.ToArray(), store.Sync().Declarations.ToArray());
        if (command == "check")
        {
            using var output = JsonDocument.Parse(stdout);
            EnvelopeSchemaValidator.ValidateEnvelopeDocument(output.RootElement);
            Assert.Equal(2, output.RootElement.GetProperty("data").GetProperty("totalIds").GetInt32());
        }
        else
        {
            using var output = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "index.json")));
            Assert.Equal(next.Declarations.Select(d => d.Id).Order().ToArray(),
                output.RootElement.EnumerateArray().Select(d => d.GetProperty("id").GetString()!).Order().ToArray());
        }
    }

    [Fact]
    public void ReadRegisteredInputs_ReturnsCurrentAndMissingPendingFilesWithoutReplaying()
    {
        var current = WriteSource();
        var store = new DeclarationIdentityStore(_directory);
        store.Sync([current]);
        var pendingFile = Path.Combine(_directory, "pending.calr");
        var beforeSidecar = File.ReadAllText(store.SidecarPath);
        var next = store.Preview([new(pendingFile, null, Source.Replace("{Test}", "{Pending}", StringComparison.Ordinal))]);
        var journal = Path.Combine(_directory, ".calor", "ids.transaction.json");
        var transaction = JsonSerializer.Serialize(new
        {
            BeforeSidecar = beforeSidecar,
            AfterSidecar = JsonSerializer.Serialize(next),
            Changes = new[]
            {
                new DeclarationIdentityStore.SourceChange("pending.calr", null,
                    Source.Replace("{Test}", "{Pending}", StringComparison.Ordinal))
            }
        });
        File.WriteAllText(journal, transaction);

        Assert.Equal(new[] { current, pendingFile }.Order(StringComparer.Ordinal).ToArray(),
            store.ReadRegisteredInputs().ToArray());
        Assert.False(File.Exists(pendingFile));
        Assert.Equal(Source, File.ReadAllText(current));
        Assert.Equal(beforeSidecar, File.ReadAllText(store.SidecarPath));
        Assert.Equal(transaction, File.ReadAllText(journal));
    }

    [Theory]
    [InlineData("check", "sidecar")]
    [InlineData("check", "journal")]
    [InlineData("index", "sidecar")]
    [InlineData("index", "journal")]
    public void Directory_MalformedIdentityArtifacts_ReportFailureWithoutWriting(string command, string artifact)
    {
        var file = WriteSource();
        var store = new DeclarationIdentityStore(_directory);
        store.Sync([file]);
        var journal = Path.Combine(_directory, ".calor", "ids.transaction.json");
        File.WriteAllText(artifact == "sidecar" ? store.SidecarPath : journal, "{}");
        var sidecar = File.ReadAllText(store.SidecarPath);
        var transaction = File.Exists(journal) ? File.ReadAllText(journal) : null;
        var index = Path.Combine(_directory, "index.json");
        var args = command == "check"
            ? new[] { "ids", command, _directory, "--format", "json" }
            : ["ids", command, _directory, "--output", index];

        var (exit, stdout, stderr) = CliTestHarness.RunCli(_directory, args);
        Assert.Equal(1, exit);
        if (command == "check")
        {
            using var output = JsonDocument.Parse(stdout);
            EnvelopeSchemaValidator.ValidateEnvelopeDocument(output.RootElement);
            Assert.Contains(output.RootElement.GetProperty("diagnostics").EnumerateArray(),
                d => d.GetProperty("code").GetString() == DiagnosticCode.DeclarationIdentityUnavailable);
        }
        else
        {
            Assert.Contains(DiagnosticCode.DeclarationIdentityUnavailable, stderr);
        }
        Assert.False(File.Exists(index));
        Assert.Equal(Source, File.ReadAllText(file));
        Assert.Equal(sidecar, File.ReadAllText(store.SidecarPath));
        Assert.Equal(transaction, File.Exists(journal) ? File.ReadAllText(journal) : null);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("index")]
    public void Directory_UnregisteredSubdirectory_DoesNotRecoverAncestorJournal(string command)
    {
        var file = WriteSource();
        var store = new DeclarationIdentityStore(_directory);
        store.Sync([file]);
        var journal = Path.Combine(_directory, ".calor", "ids.transaction.json");
        var sidecar = File.ReadAllText(store.SidecarPath);
        var pending = JsonSerializer.Serialize(new
        {
            BeforeSidecar = sidecar,
            AfterSidecar = sidecar,
            Changes = new[] { new DeclarationIdentityStore.SourceChange("active.calr", Source, Source + "\n") }
        });
        File.WriteAllText(journal, pending);
        var child = Path.Combine(_directory, "unregistered");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "frozen.calr"), "frozen bytes");

        var args = command == "check"
            ? new[] { "ids", command, child, "--format", "json" }
            : ["ids", command, child, "--output", Path.Combine(_directory, "index.json")];
        var (exit, stdout, stderr) = CliTestHarness.RunCli(_directory, args);
        Assert.Equal(1, exit);
        if (command == "check")
        {
            using var output = JsonDocument.Parse(stdout);
            Assert.Contains(output.RootElement.GetProperty("diagnostics").EnumerateArray(),
                d => d.GetProperty("code").GetString() == DiagnosticCode.CliInputNotFound);
        }
        else
        {
            Assert.Contains("No active .calr files found", stderr);
            Assert.False(File.Exists(Path.Combine(_directory, "index.json")));
        }
        Assert.Equal(Source, File.ReadAllText(file));
        Assert.Equal(sidecar, File.ReadAllText(store.SidecarPath));
        Assert.Equal(pending, File.ReadAllText(journal));
    }

    [Fact]
    public void Check_ParseFailure_RetainsParserEnvelopeAndDoesNotCreateSidecar()
    {
        var file = WriteSource();
        File.WriteAllText(file, "§M{Test}\n§/M\n");
        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "ids", "check", file, "--format", "json");
        Assert.Equal(1, exit);
        using var output = JsonDocument.Parse(stdout);
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(output.RootElement);
        Assert.Contains(output.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == DiagnosticCode.LegacyCloserForm);
        Assert.False(File.Exists(new DeclarationIdentityStore(_directory).SidecarPath));
    }

    [Fact]
    public void Check_AmbiguousRename_ReportsIdentityFailureAndLeavesSnapshotsUntouched()
    {
        var file = WriteSource();
        const string source = "§M{Test}\n  §F{A:pub} () -> void\n  §F{B:pub} () -> void\n";
        File.WriteAllText(file, source);
        var store = new DeclarationIdentityStore(_directory);
        store.Sync([file]);
        var sidecar = File.ReadAllText(store.SidecarPath);
        var changed = source.Replace("{A:", "{C:", StringComparison.Ordinal)
            .Replace("{B:", "{D:", StringComparison.Ordinal);
        File.WriteAllText(file, changed);

        var (exit, stdout, _) = CliTestHarness.RunCli(_directory, "ids", "check", file, "--format", "json");
        Assert.Equal(1, exit);
        using var output = JsonDocument.Parse(stdout);
        EnvelopeSchemaValidator.ValidateEnvelopeDocument(output.RootElement);
        Assert.Contains(output.RootElement.GetProperty("diagnostics").EnumerateArray(),
            d => d.GetProperty("code").GetString() == DiagnosticCode.DeclarationIdentityUnavailable);
        Assert.Equal(sidecar, File.ReadAllText(store.SidecarPath));
        Assert.Equal(changed, File.ReadAllText(file));
    }

    [Fact]
    public void Index_ExplicitActiveFile_ExportsPersistedIds()
    {
        var file = WriteSource();
        var index = Path.Combine(_directory, "index.json");
        var (exit, _, _) = CliTestHarness.RunCli(_directory, "ids", "index", file, "--output", index);
        Assert.Equal(0, exit);
        using var output = JsonDocument.Parse(File.ReadAllText(index));
        var persisted = new DeclarationIdentityStore(_directory).Sync().Declarations;
        Assert.Equal(persisted.Select(d => d.Id).Order().ToArray(),
            output.RootElement.EnumerateArray().Select(d => d.GetProperty("id").GetString()!).Order().ToArray());
        Assert.Equal(Source, File.ReadAllText(file));
    }

    [Theory]
    [InlineData("active.calr")]
    [InlineData(".calor/ids.json")]
    [InlineData(".calor/ids.transaction.json")]
    public void Index_RejectsSourceAndIdentityStoreOutput(string relative)
    {
        var file = WriteSource();
        var store = new DeclarationIdentityStore(_directory);
        store.Sync([file]);
        var sidecar = File.ReadAllText(store.SidecarPath);

        var (exit, _, _) = CliTestHarness.RunCli(_directory, "ids", "index", file,
            "--output", Path.Combine(_directory, relative));
        Assert.Equal(1, exit);
        Assert.Equal(Source, File.ReadAllText(file));
        Assert.Equal(sidecar, File.ReadAllText(store.SidecarPath));
    }
}
