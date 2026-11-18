using Calor.Compiler;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Effects;
using Calor.Compiler.Incremental;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Calor.Tasks.Tests;

/// <summary>
/// #1732: the CLI driver (<see cref="CompilationDriver.CompileAll"/>) and the
/// MSBuild task (<see cref="CompileCalor"/>) share one warm-skip decision,
/// <see cref="CrossModuleCacheReuse"/>. These tests drive both surfaces over the
/// same multi-file workspace (each in its own copy, so neither sees the other's
/// state) and check, file by file, that they skip the same files (criterion 4),
/// and that each skip is one the independence proof can justify (criterion 3):
/// <list type="bullet">
///   <item>a body-only edit recompiles only the edited module and its callers;</item>
///   <item>renaming a public function, changing a signature or changing an effect
///     row invalidates the callers;</item>
///   <item>a corrupted or missing output is a miss;</item>
///   <item>a cross-module Calor0410 appears and disappears with the callee's row;</item>
///   <item>an output that shares a namespaced partial module wrapper with an
///     edited module is not spared.</item>
/// </list>
/// Every module uses a namespace header (<c>module X X block</c>), the shape that
/// emits <c>namespace X { public static partial class XModule }</c>.
/// </summary>
public sealed class IncrementalSurfaceAgreementTests : IDisposable
{
    private readonly List<string> _roots = [];
    private readonly ITestOutputHelper _output;

    public IncrementalSurfaceAgreementTests(ITestOutputHelper output) => _output = output;

    private const string Library = """
        module Library Library block
          func Value public () → int
            !
            return 1

          func Save public () → void
            ! db:w
            return
        """;

    private const string Caller = """
        module App App block
          func Main public () → void
            ! db:w
            Library.Save()
        """;

    private const string Bystander = """
        module Bystander Bystander block
          func Answer public () → int
            !
            return 42
        """;

    // A second module in the Library namespace: it emits into the same
    // `namespace Library { public static partial class LibraryModule }`.
    private const string Sibling = """
        module Extra Library block
          func Other public () → int
            !
            return 7
        """;

    private static readonly Dictionary<string, string> Baseline = new()
    {
        ["library"] = Library,
        ["caller"] = Caller,
        ["bystander"] = Bystander,
    };

    public static TheoryData<string, string[], string[]> Edits() => new()
    {
        // edit, files expected to compile, files expected to skip.
        // The proof checks a reused output against the names of EVERY other
        // module, edited or not, so an output that calls any companion is
        // recompiled whenever any source changes: the caller here, even for an
        // edit to the bystander it never calls. Conservative, not a soundness gap.
        { "bystander-body", ["bystander", "caller"], ["library"] },
        { "caller-body", ["caller"], ["bystander", "library"] },
        { "library-body", ["caller", "library"], ["bystander"] },
        { "library-signature", ["caller", "library"], ["bystander"] },
        { "library-row-unused", ["caller", "library"], ["bystander"] },
        { "library-rename", ["bystander", "caller", "library"], [] },
        { "corrupt-output", ["bystander"], ["caller", "library"] },
        { "missing-output", ["bystander"], ["caller", "library"] },
        { "corrupt-output-and-edit", ["bystander", "caller", "library"], [] },
        // The sibling shares Library's partial wrapper, so library is not spared;
        // the caller names that wrapper (global::Library.LibraryModule.Save).
        { "sibling-body", ["caller", "library", "sibling"], ["bystander"] },
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public void CliAndTaskSkipTheSameFiles_AndOnlyProvenIndependentOnes(
        string edit, string[] compiled, string[] skipped)
    {
        var files = new Dictionary<string, string>(Baseline);
        if (edit.StartsWith("sibling", StringComparison.Ordinal))
            files["sibling"] = Sibling;

        var cli = NewWorkspace(files);
        var task = NewWorkspace(files);
        foreach (var (root, surface) in new[] { (cli, Surface.Cli), (task, Surface.Task) })
        {
            var cold = Build(root, surface);
            Assert.True(cold.Success, cold.Describe());
            Assert.Equal(files.Keys.Order(StringComparer.Ordinal), cold.Compiled);
            var warm = Build(root, surface);
            Assert.True(warm.Success, warm.Describe());
            Assert.Empty(warm.Compiled);
            Assert.Equal(files.Keys.Order(StringComparer.Ordinal), warm.Skipped);
            Apply(root, surface, edit);
        }

        var cliRun = Build(cli, Surface.Cli);
        var taskRun = Build(task, Surface.Task);
        _output.WriteLine("cli:  " + cliRun.Describe());
        _output.WriteLine("task: " + taskRun.Describe());
        Assert.True(cliRun.Success, cliRun.Describe());
        Assert.True(taskRun.Success, taskRun.Describe());

        // Criterion 4: file-by-file agreement.
        Assert.Equal(cliRun.Compiled, taskRun.Compiled);
        Assert.Equal(cliRun.Skipped, taskRun.Skipped);
        // Criterion 3: the agreed set is the sound one.
        Assert.Equal(compiled, cliRun.Compiled);
        Assert.Equal(skipped, cliRun.Skipped);

        var reason = taskRun.Messages.Where(message =>
            message.Contains("global invalidation [", StringComparison.Ordinal)
            || message.Contains("[cross-module-sources-changed]", StringComparison.Ordinal)).ToArray();
        if (edit is "library-rename" or "corrupt-output-and-edit")
            Assert.Contains(reason, message => message.Contains("[cross-module-map-changed]", StringComparison.Ordinal));
        else if (edit is "corrupt-output" or "missing-output")
            Assert.Empty(reason);
        else
            Assert.Equal("Calor: cross-module sources changed [cross-module-sources-changed] — "
                + "reusing only outputs proven independent of the changed modules.", Assert.Single(reason));

        // Every warm result equals a cold build of the same inputs.
        foreach (var (root, surface, warmRun) in new[] { (cli, Surface.Cli, cliRun), (task, Surface.Task, taskRun) })
        {
            var outputs = Outputs(root, surface);
            var fresh = Build(root, surface, clear: true);
            Assert.Equal(warmRun.ErrorCodes, fresh.ErrorCodes);
            Assert.Equal(outputs, Outputs(root, surface));
        }
    }

    [Fact]
    public void CrossModuleViolation_AppearsAndDisappearsWithTheCalleeRow_OnBothSurfaces()
    {
        // ES-04 / ES-08 shape: the callee's row changes, the unedited caller's
        // Calor0410 must appear on the warm build and disappear again when the
        // edit is reverted. The bystander is the only file the proof may spare.
        foreach (var surface in new[] { Surface.Cli, Surface.Task })
        {
            var root = NewWorkspace(Baseline);
            Assert.True(Build(root, surface).Success);
            Assert.Equal(new[] { "bystander", "caller", "library" }, Build(root, surface).Skipped);

            Write(root, "library", Library.Replace("! db:w", "! db:w, net", StringComparison.Ordinal));
            var gained = Build(root, surface);
            _output.WriteLine($"{surface} gained: " + gained.Describe());
            Assert.False(gained.Success);
            Assert.Contains("Calor0410", gained.ErrorCodes);
            Assert.Contains("caller", gained.Compiled);
            Assert.Equal(new[] { "bystander" }, gained.Skipped);
            var fresh = Build(NewWorkspace(Snapshot(root)), surface);
            Assert.Equal(fresh.ErrorCodes, gained.ErrorCodes);

            Write(root, "library", Library);
            var restored = Build(root, surface);
            _output.WriteLine($"{surface} restored: " + restored.Describe());
            Assert.True(restored.Success, restored.Describe());
            Assert.DoesNotContain("Calor0410", restored.ErrorCodes);
            // The surfaces start this step from different states, by design: the
            // CLI never saves a build that failed cross-module enforcement, so its
            // prior state is the clean one and every source matches it again; the
            // task saved the failed build's per-file entries, so the library is an
            // edit against them and its caller must be proven again (it cannot be).
            if (surface == Surface.Cli)
                Assert.Equal(new[] { "bystander", "caller", "library" }, restored.Skipped);
            else
            {
                Assert.Equal(new[] { "caller", "library" }, restored.Compiled);
                Assert.Equal(new[] { "bystander" }, restored.Skipped);
            }
        }
    }

    [Fact]
    public void DependentRecompilesWhenTheCalleeSignatureBreaksIt_OnBothSurfaces()
    {
        // A signature change the caller can no longer satisfy must surface as the
        // caller's own error on the warm build, exactly as on a cold one.
        foreach (var surface in new[] { Surface.Cli, Surface.Task })
        {
            var root = NewWorkspace(Baseline);
            Assert.True(Build(root, surface).Success);
            Write(root, "library", Library.Replace("func Save public ()", "func Save public (int:n)",
                StringComparison.Ordinal));
            var warm = Build(root, surface);
            _output.WriteLine($"{surface}: " + warm.Describe());
            Assert.False(warm.Success);
            Assert.Contains("caller", warm.Compiled);
            Assert.DoesNotContain("caller", warm.Skipped);
            var fresh = Build(NewWorkspace(Snapshot(root)), surface);
            Assert.Equal(fresh.ErrorCodes, warm.ErrorCodes);
        }
    }

    [Fact]
    public void QualifiedCrossModuleCall_IsInTheCachedSummary_OnBothSurfaces()
    {
        // Warm builds enforce Calor0410 from the CACHED effect summaries of
        // skipped files, so both surfaces must cache the same summary. The task
        // used to build it without the cross-module function map. A call written
        // with the callee's namespace (`Library.Save()` into `module Store
        // Library block`) then stayed `Library.Save`, which the cross-module
        // registry (keyed by module name) does not know; with the map it is
        // `Store.Save`, as on the CLI.
        var files = new Dictionary<string, string>(Baseline)
        {
            ["library"] = Library.Replace("module Library Library block", "module Store Library block",
                StringComparison.Ordinal)
        };
        var cli = NewWorkspace(files);
        var task = NewWorkspace(files);
        Assert.True(BuildCli(cli, clear: false).Success);
        Assert.True(BuildTask(task, clear: false).Success);
        var cliState = BuildStateCache.Load(cli)!;
        var taskState = BuildStateCache.Load(Path.Combine(task, "obj"))!;

        var callerCalls = cliState.Files["caller.calr"].EffectSummary!.Callers
            .SelectMany(caller => caller.Calls).Select(call => call.Target).ToArray();
        _output.WriteLine("cli caller calls: " + string.Join(", ", callerCalls));
        Assert.Contains("Store.Save", callerCalls);
        Assert.Equal(cliState.Files.Keys.Order(StringComparer.Ordinal), taskState.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var key in cliState.Files.Keys)
            Assert.Equal(
                System.Text.Json.JsonSerializer.Serialize(cliState.Files[key].EffectSummary),
                System.Text.Json.JsonSerializer.Serialize(taskState.Files[key].EffectSummary));
    }

    [Fact]
    public void HandWrittenPartialOfASkippedWrapper_StillFailsTheBuild()
    {
        // The soundness argument for namespaced partial wrappers (#1732) does not
        // fingerprint hand-written C#: it cannot change Calor emission, and every
        // output, skipped or fresh, is validated with the project's .cs files on
        // every build. So a hand-written partial that collides with a SKIPPED
        // wrapper still fails the build that introduces it.
        var root = NewWorkspace(Baseline);
        Assert.True(BuildTask(root, clear: false).Success);
        var handWritten = Path.Combine(root, "Handwritten.cs");
        File.WriteAllText(handWritten, """
            namespace Bystander
            {
                public static partial class BystanderModule
                {
                    public static int Answer() => 0;
                }
            }
            """);
        Write(root, "library", Library.Replace("return 1", "return 2", StringComparison.Ordinal));
        var run = BuildTask(root, clear: false, projectSources: [handWritten]);
        _output.WriteLine(run.Describe());
        Assert.Equal(new[] { "bystander" }, run.Skipped);
        Assert.False(run.Success);
        Assert.NotEmpty(run.ErrorCodes);

        File.Delete(handWritten);
        var repaired = BuildTask(root, clear: false);
        Assert.True(repaired.Success, repaired.Describe());
    }

    private enum Surface { Cli, Task }

    private sealed record BuildRun(
        bool Success, string[] Compiled, string[] Skipped, string[] ErrorCodes, IReadOnlyList<string> Messages)
    {
        public string Describe() =>
            $"success={Success} compiled=[{string.Join(",", Compiled)}] skipped=[{string.Join(",", Skipped)}] "
            + $"errors=[{string.Join(",", ErrorCodes)}]\n" + string.Join("\n", Messages);
    }

    private string NewWorkspace(IReadOnlyDictionary<string, string> files)
    {
        var root = Path.Combine(Path.GetTempPath(), "calor-surface-agree-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(root);
        _roots.Add(root);
        foreach (var (name, source) in files)
            Write(root, name, source);
        return root;
    }

    private static void Write(string root, string name, string source) =>
        File.WriteAllText(Path.Combine(root, name + ".calr"), source);

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.GetFiles(root, "*.calr").ToDictionary(
            path => Path.GetFileNameWithoutExtension(path), File.ReadAllText);

    private static string OutputPath(string root, Surface surface, string name) => surface == Surface.Cli
        ? Path.Combine(root, name + ".g.cs")
        : Path.Combine(root, "obj", name + ".g.cs");

    private static Dictionary<string, string> Outputs(string root, Surface surface) =>
        Directory.GetFiles(surface == Surface.Cli ? root : Path.Combine(root, "obj"), "*.g.cs")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText);

    private static void Apply(string root, Surface surface, string edit)
    {
        switch (edit)
        {
            case "bystander-body":
                Write(root, "bystander", Bystander.Replace("return 42", "return 43", StringComparison.Ordinal));
                break;
            case "caller-body":
                Write(root, "caller", Caller.Replace("    Library.Save()", "    Library.Save()\n    Library.Save()",
                    StringComparison.Ordinal));
                break;
            case "library-body":
                Write(root, "library", Library.Replace("return 1", "return 2", StringComparison.Ordinal));
                break;
            case "library-signature":
                // Same names, so the function map (and its fingerprint) is unchanged:
                // only the proof can send the caller back to the compiler.
                Write(root, "library", Library.Replace("func Save public () → void\n    ! db:w\n    return",
                    "func Save public () → int\n    ! db:w\n    return 0", StringComparison.Ordinal));
                break;
            case "library-row-unused":
                Write(root, "library", Library.Replace("    !\n    return 1", "    ! cw\n    return 1",
                    StringComparison.Ordinal));
                break;
            case "library-rename":
                Write(root, "library", Library.Replace("func Value", "func Number", StringComparison.Ordinal));
                break;
            case "corrupt-output":
                File.AppendAllText(OutputPath(root, surface, "bystander"), "\n// edited by hand\n");
                break;
            case "missing-output":
                File.Delete(OutputPath(root, surface, "bystander"));
                break;
            case "corrupt-output-and-edit":
                File.AppendAllText(OutputPath(root, surface, "bystander"), "\n// edited by hand\n");
                Write(root, "library", Library.Replace("return 1", "return 2", StringComparison.Ordinal));
                break;
            case "sibling-body":
                Write(root, "sibling", Sibling.Replace("return 7", "return 8", StringComparison.Ordinal));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit));
        }
    }

    private static BuildRun Build(string root, Surface surface, bool clear = false) =>
        surface == Surface.Cli ? BuildCli(root, clear) : BuildTask(root, clear);

    private static string[] Sources(string root) =>
        Directory.GetFiles(root, "*.calr").Order(StringComparer.Ordinal).ToArray();

    private static BuildRun BuildCli(string root, bool clear)
    {
        var diagnostics = new DiagnosticBag();
        var attempted = new List<string>();
        var result = CompilationDriver.CompileAll(
            Sources(root).Select(path => new FileInfo(path)).ToArray(),
            _ => new CompilationOptions
            {
                EnforceEffects = true,
                UnknownCallPolicy = UnknownCallPolicy.Strict,
                StatusWriter = TextWriter.Null
            },
            crossModuleEnforcement: true,
            crossModulePolicy: UnknownCallPolicy.Strict,
            onCompiled: (file, compiled) =>
                File.WriteAllText(Path.ChangeExtension(file.FullName, ".g.cs"), compiled.GeneratedCode),
            diagnosticSink: diagnostics,
            cache: new CompilationDriver.DriverCacheSettings(
                root, "surface-agreement", clear, file => Path.ChangeExtension(file.FullName, ".g.cs")),
            onFileResult: (file, _) => attempted.Add(Path.GetFileNameWithoutExtension(file.Name)));
        return new BuildRun(
            !result.AnyErrors,
            attempted.Order(StringComparer.Ordinal).ToArray(),
            result.Skipped.Select(file => Path.GetFileNameWithoutExtension(file.Name))
                .Order(StringComparer.Ordinal).ToArray(),
            diagnostics.Where(diagnostic => diagnostic.IsError).Select(diagnostic => diagnostic.Code)
                .Order(StringComparer.Ordinal).ToArray(),
            diagnostics.Select(diagnostic => diagnostic.ToString()).ToArray());
    }

    private static BuildRun BuildTask(string root, bool clear, string[]? projectSources = null)
    {
        var outputDirectory = Path.Combine(root, "obj");
        if (clear && Directory.Exists(outputDirectory))
            Directory.Delete(outputDirectory, recursive: true);
        var engine = new TestBuildEngine();
        var task = new CompileCalor
        {
            BuildEngine = engine,
            SourceFiles = Sources(root).Select(path => (ITaskItem)new TaskItem(path)).ToArray(),
            OutputDirectory = outputDirectory,
            ProjectDirectory = root,
            ImplicitUsings = "enable",
            EnforceEffects = true,
            Verbose = true,
            ProjectSourceFiles = (projectSources ?? []).Select(path => (ITaskItem)new TaskItem(path)).ToArray()
        };
        var success = task.Execute();
        string[] Files(string prefix) => engine.Messages
            .Where(message => message.StartsWith(prefix, StringComparison.Ordinal))
            .Select(message => Path.GetFileNameWithoutExtension(
                message[prefix.Length..].Split(" -> ")[0].Trim()))
            .Order(StringComparer.Ordinal).ToArray();
        return new BuildRun(
            success,
            Files("Compiling Calor: "),
            Files("Calor: skipping (up-to-date): "),
            engine.ErrorCodes.Order(StringComparer.Ordinal).ToArray(),
            engine.Messages.Concat(engine.Errors).ToArray());
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
