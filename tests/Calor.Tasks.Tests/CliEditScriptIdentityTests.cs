using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
using Calor.Compiler.Indexing;
using Calor.Compiler.Verification.Z3;
using Xunit;
using static Calor.Tests.Shared.EditScriptCorpus;

namespace Calor.Tasks.Tests;

/// <summary>Gate 3's process leg. Kept out of the compiler shard (#1116).</summary>
public sealed class CliEditScriptIdentityTests : IDisposable
{
    private readonly List<string> _workspaces = [];
    private sealed record Outcome(IReadOnlyList<string> Diagnostics, bool AnyErrors, int Skipped);
    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);

    public static TheoryData<string> RegisteredScripts()
    {
        var data = new TheoryData<string>();
        foreach (var directory in EnumerateScriptDirectories())
            data.Add(Path.GetFileName(directory));
        return data;
    }

    [Fact]
    public void AllEightScriptsIncludingDocsRequiredAreRegistered() =>
        Assert.Equal(new[]
        {
            "ES-01-local-edit", "ES-02-add-file", "ES-03-delete-file",
            "ES-04-cross-module-effect", "ES-05-options-flip", "ES-06-touch-noop",
            "ES-07-persistent-finding", "ES-08-effect-row-edit",
        }, EnumerateScriptDirectories().Select(Path.GetFileName).ToArray());

    [Theory]
    [MemberData(nameof(RegisteredScripts))]
    public void CleanAndIncrementalCliMatchTheInProcessOracle(string scriptName)
    {
        var script = LoadScript(scriptName);
        var workspace = CreateWorkspace();
        var cachePath = BuildStateCache.GetCachePath(workspace);
        var reused = 0;
        var sourceCount = 0;

        foreach (var step in script.Steps)
        {
            SyncWorkspace(workspace, step.SourceDirectory);
            var priorCache = ReadCache(cachePath);
            var priorOutputs = ReadOutputs(workspace);
            var incremental = CompileCli(workspace, step.Options, clear: false);
            reused += incremental.Skipped;
            if (scriptName == "ES-06-touch-noop" && sourceCount != 0)
                Assert.Equal(sourceCount, incremental.Skipped);
            sourceCount = Directory.GetFiles(workspace, "*.calr").Length;

            var incrementalCache = ReadCache(cachePath);
            var incrementalOutputs = ReadOutputs(workspace);
            var index = RunProcess(workspace, ["index", "build", workspace]);
            Assert.True(index.ExitCode == 0, index.Stderr);
            var indexDirectory = Path.Combine(workspace, "obj", "calor");
            var indexPath = ProjectIndex.PathFor(indexDirectory);
            var cliIndexBytes = File.ReadAllBytes(indexPath);
            var (cliIndex, status) = ProjectIndex.Load(indexDirectory);
            Assert.Equal(ProjectIndex.Freshness.Fresh, status);
            Assert.NotNull(cliIndex);
            Assert.Equal(SerializeIndex(workspace, step), CanonicalizeIndex(cliIndex));

            RestoreCache(cachePath, priorCache);
            RestoreOutputs(workspace, priorOutputs);
            var incrementalOracle = CompileInProcess(workspace, step.Options, clear: false);
            Assert.Equal(incrementalOracle.Diagnostics, incremental.Diagnostics);
            Assert.Equal(incrementalOracle.AnyErrors, incremental.AnyErrors);
            Assert.Equal(incrementalOracle.Skipped, incremental.Skipped);
            Assert.Equal(ReadCache(cachePath), incrementalCache);

            var clean = CompileCli(workspace, step.Options, clear: true);
            Assert.Equal(0, clean.Skipped);
            Assert.Equal(clean.Diagnostics, incremental.Diagnostics);
            Assert.Equal(clean.AnyErrors, incremental.AnyErrors);
            var cleanCache = ReadCache(cachePath);
            if (cleanCache == null)
            {
                // Failed publication retains the OLD cache on a warm build, but
                // leaves no cache after --clear-cache. It must not publish partial state.
                Assert.Equal(priorCache, incrementalCache);
            }
            else
                Assert.Equal(CanonicalCache(cleanCache), CanonicalCache(incrementalCache));

            var oracle = CompileInProcess(workspace, step.Options);
            Assert.Equal(oracle.Diagnostics, incremental.Diagnostics);
            Assert.Equal(oracle.AnyErrors, incremental.AnyErrors);
            Assert.Equal(ReadCache(cachePath), cleanCache);
            ProjectIndexBuilder.Build(new ProjectIndexBuilder.Options(
                workspace, "index-v1", ProjectIndexBuilder.DiscoverSources(workspace))).Save(indexDirectory);
            Assert.Equal(File.ReadAllBytes(indexPath), cliIndexBytes);

            // Oracle/clean runs must not become the next step's incremental history.
            RestoreCache(cachePath, incrementalCache);
            RestoreOutputs(workspace, incrementalOutputs);
        }

        if (script.ExpectsReuse)
            Assert.True(reused > 0, $"{scriptName}: no CLI cache hit; identity is vacuous.");
        else
            Assert.True(reused == 0, $"{scriptName}: unexpected reuse ({script.ReuseNote}).");
    }

    [Fact]
    public void DefaultUnknownCallPolicyIsDiscriminatedByTheCorpus()
    {
        var step = LoadScript("ES-08-effect-row-edit").Steps[2];
        var workspace = CreateWorkspace();
        SyncWorkspace(workspace, step.SourceDirectory);
        var cli = CompileCli(workspace, step.Options, clear: true);
        var oracle = CompileInProcess(workspace, step.Options);
        Assert.Equal(oracle.Diagnostics, cli.Diagnostics);
        Assert.Contains(cli.Diagnostics, d => d.Contains("|Calor0410|error|", StringComparison.Ordinal));

        var permissiveCli = CompileCli(workspace, step.Options, clear: true, permissive: true);
        var permissiveOracle = CompileInProcess(workspace, step.Options, permissive: true);
        Assert.Equal(permissiveOracle.Diagnostics, permissiveCli.Diagnostics);
        Assert.False(cli.Diagnostics.SequenceEqual(permissiveCli.Diagnostics));
        Assert.False(oracle.Diagnostics.SequenceEqual(permissiveOracle.Diagnostics));
    }

    private static Outcome CompileInProcess(
        string workspace, string profile, bool permissive = false, bool clear = true)
    {
        var diagnostics = new DiagnosticBag();
        var defaults = OptionsFor(profile);
        var options = permissive ? new CompilationOptions
        {
            EnforceEffects = defaults.EnforceEffects,
            RequireDocs = defaults.RequireDocs,
            UnknownCallPolicy = UnknownCallPolicy.Permissive,
        } : defaults;
        var token = Program.BuildOptionsToken(
            strictApi: false, requireDocs: options.RequireDocs, enforceEffects: options.EnforceEffects,
            enableTypeChecking: options.EnableTypeChecking, strictEffects: false,
            permissiveEffects: permissive, contractMode: "debug", verify: false,
            verificationTimeout: (int)VerificationOptions.DefaultTimeoutMs,
            analyze: false, allFindings: false, strictBindInference: options.StrictBindInference,
            experimentalFlags: null, elideProvenGuards: options.ElideProvenGuards);
        var result = CompilationDriver.CompileAll(
            Directory.GetFiles(workspace, "*.calr").Order(StringComparer.Ordinal)
                .Select(path => new FileInfo(path)).ToArray(),
            _ => options, crossModuleEnforcement: true, crossModulePolicy: options.UnknownCallPolicy,
            onCompiled: (file, compiled) =>
                File.WriteAllText(Path.ChangeExtension(file.FullName, ".g.cs"), compiled.GeneratedCode),
            diagnosticSink: diagnostics,
            cache: new CompilationDriver.DriverCacheSettings(workspace, token, ClearFirst: clear,
                OutputPathFor: file => Path.ChangeExtension(file.FullName, ".g.cs")),
            onFailed: file => File.Delete(Path.ChangeExtension(file.FullName, ".g.cs")));
        if (clear)
            Assert.Empty(result.Skipped);
        return new Outcome(Canonicalize(diagnostics, workspace), result.AnyErrors, result.Skipped.Count);
    }

    private static Outcome CompileCli(string workspace, string profile, bool clear, bool permissive = false)
    {
        var arguments = new List<string> { "--cache", "--format", "json" };
        foreach (var source in Directory.GetFiles(workspace, "*.calr").Order(StringComparer.Ordinal))
        {
            arguments.Add("-i");
            arguments.Add(source);
        }
        if (clear)
            arguments.Add("--clear-cache");
        if (profile == "effects-off")
            arguments.Add("--no-enforce-effects");
        if (profile == "docs-required")
            arguments.Add("--require-docs");
        if (permissive)
            arguments.Add("--permissive-effects");
        var result = RunProcess(workspace, arguments);
        Assert.True(result.ExitCode is 0 or 1, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        var diagnostics = json.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(d =>
            {
                var location = d.GetProperty("location");
                return DiagnosticLine(workspace,
                    location.TryGetProperty("file", out var file) ? file.GetString() : null,
                    d.GetProperty("code").GetString()!, d.GetProperty("severity").GetString()!,
                    location.GetProperty("line").GetInt32(), location.GetProperty("column").GetInt32(),
                    d.GetProperty("message").GetString()!);
            }).Order(StringComparer.Ordinal).ToArray();
        Assert.DoesNotContain(diagnostics,
            d => d.Contains($"|{DiagnosticCode.CliInternalError}|", StringComparison.Ordinal));
        var anyErrors = diagnostics.Any(d => d.Contains("|error|", StringComparison.Ordinal));
        Assert.Equal(anyErrors ? 1 : 0, result.ExitCode);
        var skipped = result.Stderr.Split('\n')
            .Count(line => line.StartsWith("Up-to-date (cached): ", StringComparison.Ordinal));
        return new Outcome(diagnostics, anyErrors, skipped);
    }

    private static ProcessResult RunProcess(string workspace, IEnumerable<string> arguments)
    {
        var dll = SdkSurfaceAgreementTests.FindCalorDll();
        Assert.Equal(
            BuildStateCache.ComputeCompilerHash([typeof(Program).Assembly.Location]),
            BuildStateCache.ComputeCompilerHash([dll]));
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workspace, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--no-telemetry");
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start calor.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("calor process exceeded 120 seconds.");
        }
        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "calor-cli-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        _workspaces.Add(workspace);
        return workspace;
    }

    private static byte[]? ReadCache(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static void RestoreCache(string path, byte[]? bytes)
    {
        if (bytes == null)
            File.Delete(path);
        else
            File.WriteAllBytes(path, bytes);
    }

    private static Dictionary<string, byte[]> ReadOutputs(string workspace) =>
        Directory.GetFiles(workspace, "*.g.cs")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes);

    private static void RestoreOutputs(string workspace, Dictionary<string, byte[]> outputs)
    {
        foreach (var file in Directory.GetFiles(workspace, "*.g.cs"))
            File.Delete(file);
        foreach (var (name, bytes) in outputs)
            File.WriteAllBytes(Path.Combine(workspace, name), bytes);
    }

    private static byte[]? CanonicalCache(byte[]? bytes)
    {
        if (bytes == null)
            return null;
        var state = JsonNode.Parse(bytes)!.AsObject();
        foreach (var file in state["files"]!.AsObject())
            file.Value!.AsObject().Remove("lastModified");
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteSorted(writer, state);
        return buffer.ToArray();
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            writer.WriteStartObject();
            foreach (var (key, value) in obj.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key);
                WriteSorted(writer, value);
            }
            writer.WriteEndObject();
        }
        else if (node is JsonArray array)
        {
            writer.WriteStartArray();
            foreach (var value in array)
                WriteSorted(writer, value);
            writer.WriteEndArray();
        }
        else if (node == null)
            writer.WriteNullValue();
        else
            node.WriteTo(writer);
    }

    public void Dispose()
    {
        foreach (var workspace in _workspaces)
            Directory.Delete(workspace, recursive: true);
    }
}
