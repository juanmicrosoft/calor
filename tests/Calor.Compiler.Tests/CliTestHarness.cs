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
