using System.Diagnostics;
using System.Text.Json;
using Calor.Compiler.Commands;
using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// End-to-end contract tests that exercise the <c>calor hook validate-write</c>
/// binary the way a Claude Code <c>PreToolUse</c> hook would invoke it.
///
/// Unlike <see cref="HookCommandTests"/>, which drives the public methods
/// directly, these tests spawn the compiled CLI as a subprocess so the whole
/// argument-parsing + exit-code + stderr contract is exercised as an agent
/// would hit it. This is the enforcement contract installed by
/// <c>calor init --ai claude</c> (see <c>ClaudeInitializer.ConfigureHooksAsync</c>,
/// which registers the command <c>calor hook validate-write $TOOL_INPUT</c>).
///
/// Claude Code delivers the outer PreToolUse envelope on stdin, but the
/// installed hook only passes the <c>$TOOL_INPUT</c> shell variable (the
/// inner <c>tool_input</c> object) as a CLI argument. These tests therefore
/// pass that inner object as an argument — matching the deployed contract.
///
/// Note: Codex uses a different envelope (Codex ships the whole payload on
/// stdin, containing an <c>apply_patch</c> command); that path is covered
/// by <c>CodexWriteHook_*</c> tests in <see cref="HookCommandTests"/>.
/// </summary>
public class HookCommandAgentInvocationTests
{
    // Shape of the inner tool_input object Claude Code sends for a `Write` tool
    // call. This is the exact JSON that expands into `$TOOL_INPUT` at hook
    // invocation time.
    private static string BuildWriteToolInput(string filePath, string? content = null)
    {
        return JsonSerializer.Serialize(new
        {
            file_path = filePath,
            content = content ?? "// example content"
        });
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunHookAsync(
        string subcommand,
        string? argument,
        string? stdin = null)
    {
        var compilerDll = typeof(HookCommand).Assembly.Location;
        var args = argument is null
            ? $"\"{compilerDll}\" hook {subcommand}"
            : $"\"{compilerDll}\" hook {subcommand} {EscapeArg(argument)}";

        var startInfo = new ProcessStartInfo("dotnet")
        {
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start calor hook subprocess.");

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }
        process.StandardInput.Close();

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;
        return (process.ExitCode, stdOut, stdErr);
    }

    // Quote the argument for the process arg string. The JSON payload contains
    // spaces and double quotes so we wrap it in double quotes and escape any
    // embedded ones.
    private static string EscapeArg(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    [Fact]
    public async Task PreToolUseHook_BlocksCsWrite_ReturnsExpectedDiagnostic()
    {
        // Synthetic Claude Code PreToolUse tool_input for a `Write` targeting a .cs file.
        var toolInput = BuildWriteToolInput("/tmp/whatever.cs", "public class Whatever {}");

        var (exitCode, _, stdErr) = await RunHookAsync("validate-write", toolInput);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("BLOCKED", stdErr);
        Assert.Contains("Calor-first", stdErr);
        Assert.Contains("whatever.cs", stdErr);
        // The suggestion should nudge the agent toward the .calr equivalent.
        Assert.Contains(".calr", stdErr);
    }

    [Fact]
    public async Task PreToolUseHook_AllowsCalrWrite_ReturnsOk()
    {
        var toolInput = BuildWriteToolInput("/tmp/whatever.calr", "§M{m001:Whatever}\n");

        var (exitCode, stdOut, stdErr) = await RunHookAsync("validate-write", toolInput);

        Assert.Equal(0, exitCode);
        // No block reason should be printed on the happy path.
        Assert.DoesNotContain("BLOCKED", stdErr);
        // The allow path is silent — no advisory chatter on either stream.
        // A regression that emitted a stray warning on the happy path would
        // pollute agent output on every legitimate .calr write; asserting
        // both streams are empty pins the "hook shuts up on allow" contract.
        Assert.Equal(string.Empty, stdOut.Trim());
        Assert.Equal(string.Empty, stdErr.Trim());
    }

    [Fact]
    public async Task PreToolUseHook_MalformedJsonPayload_FailsOpen()
    {
        // Documents the CURRENT Claude hook behavior for malformed JSON:
        // ValidateWriteWithReason (src/Calor.Compiler/Commands/HookCommand.cs)
        // catches JsonException and returns (0, null, null) — i.e., garbage
        // input allows the write. This is a fail-OPEN posture that is
        // deliberately inconsistent with the Codex path
        // (CodexWriteHook_FailsClosedForMalformedEnvelope pins Codex at
        // exit 2 on the same shape). Both cannot be right; this test pins
        // the current state so a future change is a visible flip, not a
        // silent semantic drift. If the fail-open/fail-closed
        // inconsistency between Claude and Codex is resolved by making
        // Claude also fail closed, this test's assertion must invert.
        var malformed = "not-json-at-all";

        var (exitCode, _, _) = await RunHookAsync("validate-write", malformed);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task PreToolUseHook_MissingPayload_FailsWithNonZeroExit()
    {
        // Simulate an agent (or misconfigured integration) invoking the hook
        // without passing the tool_input argument. The point is a controlled
        // non-zero exit, not any specific message text (System.CommandLine's
        // "Required argument missing" template is framework-owned and will
        // shift on beta bumps). Any UnhandledException surfacing on stderr
        // is a controlled-diagnostic contract violation — that IS a
        // Calor-owned invariant and worth asserting.
        var (exitCode, _, stdErr) = await RunHookAsync("validate-write", argument: null);

        Assert.NotEqual(0, exitCode);
        // The hook must not surface an unhandled exception to the agent —
        // that's the specific class of "controlled diagnostic" contract we
        // own. The literal "Unhandled exception" prefix is the .NET runtime's
        // guaranteed output when a Task or Main throws unhandled.
        Assert.DoesNotContain("Unhandled exception", stdErr);
    }
}
