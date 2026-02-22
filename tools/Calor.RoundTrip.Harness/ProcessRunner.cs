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
        Dictionary<string, string>? environmentVariables = null)
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

        // Ensure .NET root is set for SDK resolution
        var dotnetRoot = Path.GetDirectoryName(fileName);
        if (dotnetRoot != null && fileName.EndsWith("dotnet"))
        {
            process.StartInfo.EnvironmentVariables["DOTNET_ROOT"] = dotnetRoot;
            // Prepend to PATH so child processes also find this dotnet
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            process.StartInfo.EnvironmentVariables["PATH"] = $"{dotnetRoot}:{existingPath}";
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

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, stdout.ToString(), $"Process timed out after {timeout.TotalSeconds}s\n{stderr}");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
