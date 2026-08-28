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
    /// test host and the child normally load byte-identical copies of
    /// <c>calor.dll</c>, so the hashes agree and nothing is needed — but under
    /// <c>--collect:"XPlat Code Coverage"</c> the collector instruments the test
    /// host's copy in place, its hash changes, and every test that builds an
    /// index in-process and then queries it through the CLI fails in the coverage
    /// lane only. Stamping is honest rather than a bypass: the value written is
    /// the hash of the very assembly that will read the index, and a genuinely
    /// stale <c>calor.dll</c> is still rejected on the other inputs (options,
    /// manifest, file list), which this does not touch.</para>
    ///
    /// <para>Call it on the index object immediately before <c>Save</c>. Outside
    /// the coverage lane it writes the same value the builder already computed.</para>
    /// </summary>
    internal static void StampForChildCli(Indexing.ProjectIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        index.CompilerHash = ChildCompilerHash();
    }

    private static string FindCalorDllCore()
    {
        foreach (var config in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(FindRepoRoot(), "src", "Calor.Compiler", "bin", config, "net10.0", "calor.dll");
            if (File.Exists(candidate)) return candidate;
        }

        throw new InvalidOperationException("calor.dll not found — build the compiler first.");
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
