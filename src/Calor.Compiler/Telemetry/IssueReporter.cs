using System.Diagnostics;
using System.Text;
using System.Web;

namespace Calor.Compiler.Telemetry;

/// <summary>
/// Prompts the user to report an issue on GitHub when a command fails.
/// Opens the browser with a pre-filled issue URL.
/// </summary>
public static class IssueReporter
{
    private const string RepoUrl = "https://github.com/juanmicrosoft/calor";

    /// <summary>
    /// Prompts the user to report an issue after a command failure.
    /// </summary>
    public static void PromptForIssue(
        string operationId,
        string command,
        string errorSummary,
        IReadOnlyList<string>? diagnosticCodes = null)
    {
        try
        {
            // Don't prompt if not interactive (piped output, CI, etc.)
            if (!IsInteractive())
                return;

            Console.Error.WriteLine();
            Console.Error.WriteLine($"  Diagnostic ID: {operationId}");
            Console.Error.WriteLine();
            Console.Error.Write("  Would you like to report this issue? [Y/n] ");

            var key = Console.ReadKey(intercept: true);
            Console.Error.WriteLine();

            if (key.Key == ConsoleKey.N)
                return;

            var url = BuildIssueUrl(operationId, command, errorSummary, diagnosticCodes);
            OpenBrowser(url);
            Console.Error.WriteLine("  Opening browser to create issue...");
        }
        catch
        {
            // Never crash the CLI due to issue reporting
        }
    }

    internal static string BuildIssueUrl(
        string operationId,
        string command,
        string errorSummary,
        IReadOnlyList<string>? diagnosticCodes = null)
    {
        // Truncate error summary for URL
        var shortError = errorSummary.Length > 80
            ? errorSummary[..80] + "..."
            : errorSummary;

        var title = $"[auto-report] `calor {command}`: {shortError}";

        var body = new StringBuilder();
        body.AppendLine("## Auto-reported issue");
        body.AppendLine();
        body.AppendLine($"**Diagnostic ID:** `{operationId}`");
        body.AppendLine($"**Command:** `calor {command}`");
        body.AppendLine();
        body.AppendLine("### Error");
        body.AppendLine("```");
        body.AppendLine(errorSummary.Length > 500 ? errorSummary[..500] + "..." : errorSummary);
        body.AppendLine("```");

        if (diagnosticCodes is { Count: > 0 })
        {
            body.AppendLine();
            body.AppendLine("### Diagnostic codes");
            foreach (var code in diagnosticCodes.Take(10))
            {
                body.AppendLine($"- `{code}`");
            }
        }

        body.AppendLine();
        body.AppendLine("### Environment");
        body.AppendLine($"- **OS:** {Environment.OSVersion}");
        body.AppendLine($"- **Arch:** {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        body.AppendLine($"- **.NET:** {Environment.Version}");
        body.AppendLine($"- **Calor:** {GetCalorVersion()}");
        body.AppendLine();
        body.AppendLine("### Additional context");
        body.AppendLine("<!-- Please add any additional context, steps to reproduce, or sample files below -->");

        var encodedTitle = HttpUtility.UrlEncode(title);
        var encodedBody = HttpUtility.UrlEncode(body.ToString());

        return $"{RepoUrl}/issues/new?title={encodedTitle}&body={encodedBody}&labels=bug,auto-reported";
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            Console.Error.WriteLine($"  Could not open browser. Please visit:");
            Console.Error.WriteLine($"  {url}");
        }
    }

    private static bool IsInteractive()
    {
        try
        {
            return !Console.IsInputRedirected && !Console.IsOutputRedirected && Environment.UserInteractive;
        }
        catch
        {
            return false;
        }
    }

    private static string GetCalorVersion()
    {
        try
        {
            return System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()
                ?.InformationalVersion ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
