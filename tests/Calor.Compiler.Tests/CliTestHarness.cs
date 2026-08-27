using System.Diagnostics;

namespace Calor.Compiler.Tests;

/// <summary>
/// Shared helpers for CLI-level subprocess tests: locates the built calor.dll
/// (Release preferred over Debug) and invokes it with captured output.
/// </summary>
internal static class CliTestHarness
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRootCore);
    private static readonly Lazy<string> CalorDll = new(FindCalorDllCore);

    /// <summary>Walks up from the test working directory to the repository root.</summary>
    internal static string FindRepoRoot() => RepoRoot.Value;

    /// <summary>
    /// Locates the built calor.dll, probing Release before Debug (matching the
    /// benchmark harness, which runs against Release builds).
    /// </summary>
    internal static string FindCalorDll() => CalorDll.Value;

    private static string FindRepoRootCore()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Calor.sln")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        throw new InvalidOperationException("Repository root (Calor.sln) not found from " + Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// The compiler-identity hash of the <c>calor.dll</c> a <see cref="RunCli"/>
    /// child will load — <see cref="Incremental.BuildStateCache.ComputeCompilerHash"/>
    /// over that file, which is exactly what the child computes for itself.
    /// </summary>
    internal static string ChildCompilerHash() =>
        Incremental.BuildStateCache.ComputeCompilerHash([FindCalorDll()]);

    /// <summary>
    /// Makes an index built <b>in this process</b> acceptable to a
    /// <see cref="RunCli"/> child, by stamping it with the child's compiler hash
    /// before it is saved.
    ///
    /// <para><b>Why this is needed.</b> A project index records the identity hash
    /// of the compiler that produced it, and a reader refuses an index whose hash
    /// is not its own (<c>Error: index unusable — the compiler changed</c>). The
    /// test host and the child do not always load the same <c>calor.dll</c>: under
    /// <c>--collect:"XPlat Code Coverage"</c> the collector instruments the host's
    /// copy in place, and when both build configurations are present a Debug test
    /// host loads the Debug build while <see cref="FindCalorDll"/> hands the child
    /// the Release one. Either way an index built in-process is then refused.</para>
    ///
    /// <para><b>What this suppresses — read before copying it.</b> It suppresses
    /// the compiler-identity check <i>in full</i>. <c>CompilerHash</c> is the only
    /// input in <c>ProjectIndex.CheckFreshness</c> that distinguishes one
    /// <c>calor.dll</c> from another: <c>OptionsHash</c>, <c>ManifestHash</c> and
    /// the file list are workspace inputs, identical whichever binary runs, and
    /// <c>FormatVersion</c>/<c>CompilerSemanticsVersion</c> move only on a
    /// deliberate bump. Overwriting <c>CompilerHash</c> therefore makes the child
    /// accept an index produced by a <i>genuinely different</i> compiler, not only
    /// by an instrumented copy of the same one.</para>
    ///
    /// <para><b>Precondition for that to be sound:</b> the host builds the index,
    /// the child only reads it, and both binaries come from the same working tree
    /// and the same build configuration — so they differ at most by instrumentation.
    /// <b>If that breaks</b> — a stale <c>bin/Release</c>, or a mixed Debug/Release
    /// tree — the child will silently answer from an index a different compiler
    /// produced, and the test will report whatever that older compiler recorded
    /// instead of failing. Use this only where the index genuinely must be built
    /// in-process (an injected knob with no CLI surface). Where the child can
    /// build the index itself (<c>calor index build</c>), prefer that: it keeps
    /// the identity check in force.</para>
    ///
    /// <para>Call it on the index object immediately before <c>Save</c>.</para>
    /// </summary>
    internal static void StampForChildCli(Indexing.ProjectIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        index.CompilerHash = ChildCompilerHash();
    }

    private static string FindCalorDllCore()
    {
        var candidates = new[] { "Release", "Debug" }
            .Select(config => Path.Combine(FindRepoRoot(), "src", "Calor.Compiler", "bin", config, "net10.0", "calor.dll"))
            .Where(File.Exists)
            .ToArray();
        if (candidates.Length == 0)
            throw new InvalidOperationException("calor.dll not found — build the compiler first.");

        // Prefer the build that IS the compiler this test process loaded. When
        // both configurations exist on disk, a CLI child running the other one
        // is a different compiler: its index headers and build-state hashes do
        // not match ours, so a `--no-build` query refuses ("the compiler
        // changed") for a reason that has nothing to do with the test.
        var loaded = typeof(Program).Assembly.Location;
        if (!string.IsNullOrEmpty(loaded) && File.Exists(loaded))
        {
            var loadedHash = Incremental.BuildStateCache.ComputeCompilerHash([loaded]);
            var matching = candidates.FirstOrDefault(candidate =>
                Incremental.BuildStateCache.ComputeCompilerHash([candidate]) == loadedHash);
            if (matching != null)
                return matching;
        }

        return candidates[0];
    }

    /// <summary>
    /// Runs <c>dotnet calor.dll --no-telemetry [args]</c> from
    /// <paramref name="workingDirectory"/> and returns exit code plus captured
    /// stdout/stderr. Kills the process tree on timeout.
    /// </summary>
    internal static (int ExitCode, string StdOut, string StdErr) RunCli(
        string workingDirectory, params string[] args)
        => RunCli(workingDirectory, environment: null, args);

    /// <summary>
    /// As above, with extra environment variables for the child process. Needed by gates that are
    /// acknowledged through the environment (e.g. <c>CALOR_EXPERIMENTAL_FORMAT_WRITE</c>): setting
    /// them via <c>Environment.SetEnvironmentVariable</c> in-test would leak across xUnit's
    /// parallel collections, so they are scoped to the child instead.
    /// </summary>
    internal static (int ExitCode, string StdOut, string StdErr) RunCli(
        string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        if (environment != null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        psi.ArgumentList.Add(FindCalorDll());
        psi.ArgumentList.Add("--no-telemetry");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start calor CLI process.");

        // Read both streams concurrently to avoid pipe-buffer deadlocks.
        var stdOutTask = proc.StandardOutput.ReadToEndAsync();
        var stdErrTask = proc.StandardError.ReadToEndAsync();

        // Generous timeout: some of these tests restore/build/run real dotnet projects.
        if (!proc.WaitForExit(300_000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException("calor CLI did not exit within 5 minutes: " + string.Join(" ", args));
        }

        return (proc.ExitCode, stdOutTask.Result, stdErrTask.Result);
    }
}
