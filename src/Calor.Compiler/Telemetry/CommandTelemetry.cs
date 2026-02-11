using System.Diagnostics;

namespace Calor.Compiler.Telemetry;

/// <summary>
/// Wraps command execution with telemetry tracking and issue reporting.
/// </summary>
public static class CommandTelemetry
{
    /// <summary>
    /// Wraps an async command handler with telemetry tracking.
    /// Tracks command start, duration, success/failure, exceptions, and prompts for issue reporting on failure.
    /// </summary>
    public static Func<T1, Task> Wrap<T1>(string commandName, Func<T1, Task> handler)
    {
        return async (a1) => await ExecuteWithTelemetry(commandName, () => handler(a1));
    }

    public static Func<T1, T2, Task> Wrap<T1, T2>(string commandName, Func<T1, T2, Task> handler)
    {
        return async (a1, a2) => await ExecuteWithTelemetry(commandName, () => handler(a1, a2));
    }

    public static Func<T1, T2, T3, Task> Wrap<T1, T2, T3>(string commandName, Func<T1, T2, T3, Task> handler)
    {
        return async (a1, a2, a3) => await ExecuteWithTelemetry(commandName, () => handler(a1, a2, a3));
    }

    public static Func<T1, T2, T3, T4, Task> Wrap<T1, T2, T3, T4>(string commandName, Func<T1, T2, T3, T4, Task> handler)
    {
        return async (a1, a2, a3, a4) => await ExecuteWithTelemetry(commandName, () => handler(a1, a2, a3, a4));
    }

    public static Func<T1, T2, T3, T4, T5, Task> Wrap<T1, T2, T3, T4, T5>(string commandName, Func<T1, T2, T3, T4, T5, Task> handler)
    {
        return async (a1, a2, a3, a4, a5) => await ExecuteWithTelemetry(commandName, () => handler(a1, a2, a3, a4, a5));
    }

    public static Func<T1, T2, T3, T4, T5, T6, Task> Wrap<T1, T2, T3, T4, T5, T6>(string commandName, Func<T1, T2, T3, T4, T5, T6, Task> handler)
    {
        return async (a1, a2, a3, a4, a5, a6) => await ExecuteWithTelemetry(commandName, () => handler(a1, a2, a3, a4, a5, a6));
    }

    public static Func<T1, T2, T3, T4, T5, T6, T7, Task> Wrap<T1, T2, T3, T4, T5, T6, T7>(string commandName, Func<T1, T2, T3, T4, T5, T6, T7, Task> handler)
    {
        return async (a1, a2, a3, a4, a5, a6, a7) => await ExecuteWithTelemetry(commandName, () => handler(a1, a2, a3, a4, a5, a6, a7));
    }

    public static Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> Wrap<T1, T2, T3, T4, T5, T6, T7, T8>(string commandName, Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> handler)
    {
        return async (a1, a2, a3, a4, a5, a6, a7, a8) => await ExecuteWithTelemetry(commandName, () => handler(a1, a2, a3, a4, a5, a6, a7, a8));
    }

    private static async Task ExecuteWithTelemetry(string commandName, Func<Task> action)
    {
        var telemetry = CalorTelemetry.Instance;
        telemetry.SetCommand(commandName);

        var sw = Stopwatch.StartNew();
        string? errorMessage = null;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            telemetry.TrackException(ex, new Dictionary<string, string>
            {
                ["phase"] = "command_execution",
                ["exceptionType"] = ex.GetType().Name
            });
            throw;
        }
        finally
        {
            sw.Stop();
            var exitCode = Environment.ExitCode;
            telemetry.TrackCommand(commandName, exitCode, new Dictionary<string, string>
            {
                ["durationMs"] = sw.ElapsedMilliseconds.ToString()
            });

            if (exitCode != 0)
            {
                IssueReporter.PromptForIssue(
                    telemetry.OperationId,
                    commandName,
                    errorMessage ?? "Command failed with errors (see diagnostic codes)",
                    diagnosticCodes: null);
            }

            telemetry.Flush();
        }
    }
}
