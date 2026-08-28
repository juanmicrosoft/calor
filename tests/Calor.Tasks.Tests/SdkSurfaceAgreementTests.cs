using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Calor.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace Calor.Tasks.Tests;

/// <summary>
/// v0.16 gate 3, <c>Calor.Sdk</c> leg (roadmap-v0.16 §6 "SDK leg with E7";
/// R:889-905). <c>Calor.Sdk</c> is MSBuild props/targets with no API of its
/// own: a project built through it compiles by running the
/// <see cref="CompileCalor"/> task. So the SDK path IS the task, and this leg
/// drives the task in-process (a capturing build engine, the way
/// <c>CompileCalorIntegrationTests</c> does) over the edit-script corpus and
/// compares its canonical diagnostics — <c>file|code|severity|line|column|message</c>,
/// sorted — with the <c>calor</c> PROCESS run as <c>calor -i … --format json</c>.
///
/// Denominator: every step whose option profile the task can express.
/// <c>effects-on</c> / <c>effects-off</c> map to <see cref="CompileCalor.EnforceEffects"/>;
/// <c>docs-required</c> has no task input (there is no <c>RequireDocs</c>
/// property), so ES-07 is excluded and named as such in
/// <see cref="ExcludedScripts"/> — a hole the leg reports, not one it hides.
/// </summary>
public sealed class SdkSurfaceAgreementTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>Scripts the task cannot express, with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> ExcludedScripts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ES-07-persistent-finding"] =
                "every step uses the docs-required profile; CompileCalor has no RequireDocs input",
        };

    public static TheoryData<string> ExpressibleScripts()
    {
        var data = new TheoryData<string>();
        foreach (var directory in EnumerateScriptDirectories())
        {
            var name = Path.GetFileName(directory);
            if (!ExcludedScripts.ContainsKey(name))
                data.Add(name);
        }
        return data;
    }

    [Fact]
    public void TheDenominatorIsRegistered()
    {
        // Seven scripts in the corpus; six are expressible. A script that
        // appears or disappears has to edit this test in the diff.
        Assert.Equal(
            new[]
            {
                "ES-01-local-edit",
                "ES-02-add-file",
                "ES-03-delete-file",
                "ES-04-cross-module-effect",
                "ES-05-options-flip",
                "ES-06-touch-noop",
                "ES-07-persistent-finding",
            },
            EnumerateScriptDirectories().Select(Path.GetFileName).ToArray());
        Assert.Equal(6, ExpressibleScripts().Count());

        // The exclusion is real: the profile is in use, and the task has no
        // property to express it. If either changes, ES-07 joins the leg.
        var es07 = LoadScript("ES-07-persistent-finding");
        Assert.All(es07.Steps, step => Assert.Equal("docs-required", step.Options));
        Assert.Null(typeof(CompileCalor).GetProperty("RequireDocs"));
    }

    [Theory]
    [MemberData(nameof(ExpressibleScripts))]
    public void DiagnosticsThroughTheSdkTaskMatchTheCliProcess(string scriptName)
    {
        var script = LoadScript(scriptName);
        var anyDiagnostics = false;
        foreach (var step in script.Steps)
        {
            var workspace = CreateTempDir();
            foreach (var source in Directory.GetFiles(step.SourceDirectory, "*.calr"))
                File.Copy(source, Path.Combine(workspace, Path.GetFileName(source)), overwrite: true);
            var sources = Directory.GetFiles(workspace, "*.calr")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            // Task first: the CLI writes .g.cs beside the sources.
            var sdk = CompileThroughTask(workspace, sources, step.Options);
            var cli = CompileThroughCliProcess(workspace, sources, step.Options);

            Assert.Equal(cli, sdk);
            anyDiagnostics |= cli.Count > 0;
        }

        if (scriptName != "ES-06-touch-noop")
            Assert.True(anyDiagnostics, $"{scriptName}: no step produced a diagnostic on either path; the comparison is vacuous");
    }

    /// <summary>
    /// The edit-script corpus's findings are errors (Calor0410) and, on the
    /// excluded ES-07, warnings the task cannot request. So warning parity gets
    /// its own subject: the query corpus, whose <c>app.calr</c> carries both an
    /// error and a warning under default options. Without this the leg would
    /// pin errors only, and a severity that drifted between the two paths would
    /// go unobserved.
    /// </summary>
    [Fact]
    public void WarningsAgreeToo_NotJustErrors()
    {
        var workspace = CreateTempDir();
        var corpus = Path.Combine(RepoRoot, "tests", "TestData", "QueryCorpus", "project");
        foreach (var source in Directory.GetFiles(corpus, "*.calr"))
            File.Copy(source, Path.Combine(workspace, Path.GetFileName(source)));
        var sources = Directory.GetFiles(workspace, "*.calr")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var sdk = CompileThroughTask(workspace, sources, "effects-on");
        var cli = CompileThroughCliProcess(workspace, sources, "effects-on");

        Assert.Equal(cli, sdk);
        Assert.Contains(cli, line => line.Contains("|warning|", StringComparison.Ordinal));
        Assert.Contains(cli, line => line.Contains("|error|", StringComparison.Ordinal));
    }

    // --- the SDK path -------------------------------------------------------

    /// <summary>Records every logged event that carries a Calor diagnostic code.</summary>
    private sealed class CapturingBuildEngine : IBuildEngine
    {
        private static readonly Regex CalorCode = new(@"^Calor\d{4}$", RegexOptions.Compiled);

        public List<(string Severity, string Code, string File, int Line, int Column, string Message)> Diagnostics { get; } = [];

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "surface-agreement.csproj";

        public bool BuildProjectFile(string projectFileName, string[] targetNames,
            System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs) => true;

        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public void LogErrorEvent(BuildErrorEventArgs e) =>
            Record("error", e.Code, e.File, e.LineNumber, e.ColumnNumber, e.Message);

        public void LogWarningEvent(BuildWarningEventArgs e) =>
            Record("warning", e.Code, e.File, e.LineNumber, e.ColumnNumber, e.Message);

        public void LogMessageEvent(BuildMessageEventArgs e) =>
            Record("info", e.Code, e.File, e.LineNumber, e.ColumnNumber, e.Message);

        private void Record(string severity, string? code, string? file, int line, int column, string? message)
        {
            if (code == null || !CalorCode.IsMatch(code))
                return;
            Diagnostics.Add((severity, code, file ?? "", line, column, message ?? ""));
        }
    }

    private IReadOnlyList<string> CompileThroughTask(string workspace, string[] sources, string profile)
    {
        var engine = new CapturingBuildEngine();
        var task = new CompileCalor
        {
            BuildEngine = engine,
            SourceFiles = sources.Select(path => (ITaskItem)new TaskItem(path)).ToArray(),
            OutputDirectory = Path.Combine(workspace, "obj", "calor-sdk"),
            ProjectDirectory = workspace,
            EnforceEffects = profile != "effects-off",
            ImplicitUsings = "enable",
        };
        task.Execute();

        return engine.Diagnostics
            .Select(d => string.Join(
                "|",
                d.File.Length == 0 ? "" : Path.GetRelativePath(workspace, d.File).Replace('\\', '/'),
                d.Code,
                d.Severity,
                d.Line,
                d.Column,
                d.Message))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
    }

    // --- the CLI path -------------------------------------------------------

    private static IReadOnlyList<string> CompileThroughCliProcess(string workspace, string[] sources, string profile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workspace,
        };
        psi.ArgumentList.Add(FindCalorDll());
        psi.ArgumentList.Add("--no-telemetry");
        foreach (var source in sources)
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(source);
        }
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("json");
        if (profile == "effects-off")
            psi.ArgumentList.Add("--no-enforce-effects");
        if (profile == "docs-required")
            psi.ArgumentList.Add("--require-docs");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the calor CLI process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("calor CLI timed out");
        }

        using var output = JsonDocument.Parse(stdout.Result);
        return output.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(entry =>
            {
                var location = entry.GetProperty("location");
                var file = location.TryGetProperty("file", out var fileElement) && fileElement.ValueKind == JsonValueKind.String
                    ? Path.GetRelativePath(workspace, fileElement.GetString()!).Replace('\\', '/')
                    : "";
                return string.Join(
                    "|",
                    file,
                    entry.GetProperty("code").GetString(),
                    entry.GetProperty("severity").GetString(),
                    location.GetProperty("line").GetInt32(),
                    location.GetProperty("column").GetInt32(),
                    entry.GetProperty("message").GetString());
            })
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToArray();
    }

    // --- corpus -------------------------------------------------------------

    private sealed record ScriptStep(string SourceDirectory, string Options);

    private sealed record EditScript(string Id, IReadOnlyList<ScriptStep> Steps);

    private static EditScript LoadScript(string scriptName)
    {
        var directory = Path.Combine(CorpusRoot, scriptName);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "script.json")));
        var root = json.RootElement;
        var steps = root.GetProperty("steps")
            .EnumerateArray()
            .Select(step => new ScriptStep(
                Path.Combine(directory, step.GetProperty("dir").GetString()!),
                step.GetProperty("options").GetString()!))
            .ToArray();
        Assert.NotEmpty(steps);
        return new EditScript(root.GetProperty("id").GetString()!, steps);
    }

    private static string[] EnumerateScriptDirectories() =>
        Directory.GetDirectories(CorpusRoot)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string RepoRoot
    {
        get
        {
            var directory = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(directory, "Calor.sln")))
                directory = Directory.GetParent(directory)!.FullName;
            return directory;
        }
    }

    private static string CorpusRoot => Path.Combine(RepoRoot, "tests", "TestData", "EditScripts");

    private static string FindCalorDll()
    {
        var candidates = new[] { "Release", "Debug" }
            .Select(config => Path.Combine(RepoRoot, "src", "Calor.Compiler", "bin", config, "net10.0", "calor.dll"))
            .Where(File.Exists)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException("calor.dll not found — build the compiler first.");

        // The CLI process should be THE compiler the task in this process uses:
        // when both configurations exist on disk, prefer the one whose bytes
        // match the loaded Calor.Compiler assembly. Under a profiler (the
        // coverage lane) coverlet rewrites the loaded assembly, so no candidate
        // can match; this leg compares DIAGNOSTICS, which do not depend on the
        // compiler hash, so falling back is safe here.
        var loaded = typeof(Calor.Compiler.Program).Assembly.Location;
        if (!string.IsNullOrEmpty(loaded) && File.Exists(loaded))
        {
            var loadedHash = Calor.Compiler.Incremental.BuildStateCache.ComputeCompilerHash([loaded]);
            var matching = candidates.FirstOrDefault(candidate =>
                Calor.Compiler.Incremental.BuildStateCache.ComputeCompilerHash([candidate]) == loadedHash);
            if (matching != null)
                return matching;
        }

        return candidates[0];
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "calor-sdkgate-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }
}
