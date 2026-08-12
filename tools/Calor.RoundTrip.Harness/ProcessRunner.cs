using System.Diagnostics;
using System.Text;

namespace Calor.RoundTrip.Harness;

/// <summary>
/// Runs external processes with timeout support.
/// </summary>
public static class ProcessRunner
{
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        TimeSpan timeout,
        Dictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (environmentVariables != null)
        {
            foreach (var (key, value) in environmentVariables)
            {
                process.StartInfo.EnvironmentVariables[key] = value;
            }
        }

        // Ensure .NET root is set for SDK resolution (see ResolveDotnetRoot). If we
        // cannot resolve a real path — including the common `--dotnet dotnet` bare-name
        // case — we simply leave DOTNET_ROOT unset and let the host resolve it, rather
        // than crashing.
        var dotnetRoot = ResolveDotnetRoot(fileName);
        if (dotnetRoot != null)
        {
            process.StartInfo.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
            // Prepend to PATH so child processes also find this dotnet
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            process.StartInfo.EnvironmentVariables["PATH"] = $"{dotnetRoot}{Path.PathSeparator}{existingPath}";
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            cancellationToken);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            cancellationToken.ThrowIfCancellationRequested();
            return (-1, stdout.ToString(), $"Process timed out after {timeout.TotalSeconds}s\n{stderr}");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Resolve the directory to point <c>DOTNET_ROOT</c> at for a given dotnet
    /// executable reference, following symlinks. A common install symlinks
    /// <c>/opt/homebrew/bin/dotnet</c> → the real <c>/usr/local/share/dotnet/dotnet</c>,
    /// and DOTNET_ROOT must point at the directory that actually holds <c>sdk/</c> and
    /// <c>shared/</c> — the symlink target's directory, not the symlink's directory.
    ///
    /// Handles a BARE name (<c>--dotnet dotnet</c>, the documented + CI form) by
    /// searching PATH, and NEVER throws: a non-existent path (e.g. a bare name that is
    /// not on PATH) returns <c>null</c> so the caller leaves DOTNET_ROOT unset rather
    /// than crashing on <see cref="FileInfo.ResolveLinkTarget"/>.
    /// </summary>
    internal static string? ResolveDotnetRoot(string fileName)
    {
        if (!fileName.EndsWith("dotnet", StringComparison.Ordinal))
            return null;

        // Resolve the reference to an absolute path to an EXISTING file first.
        string? full = null;
        if (fileName.Contains('/') || fileName.Contains('\\') || Path.IsPathRooted(fileName))
        {
            var candidate = Path.GetFullPath(fileName);
            if (File.Exists(candidate))
                full = candidate;
        }
        else
        {
            full = FindOnPath(fileName); // bare name — search PATH
        }

        if (full == null)
            return null;

        try
        {
            var resolved = new FileInfo(full).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? full;
            return Path.GetDirectoryName(resolved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.GetDirectoryName(full);
        }
    }

    /// <summary>Find an executable by bare name on PATH, expanding a leading <c>~</c>.</summary>
    private static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var rawDir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(rawDir))
                continue;
            var dir = rawDir.StartsWith('~') ? home + rawDir[1..] : rawDir;
            try
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Skip malformed PATH entries (invalid path chars) rather than throw.
            }
        }
        return null;
    }
}
