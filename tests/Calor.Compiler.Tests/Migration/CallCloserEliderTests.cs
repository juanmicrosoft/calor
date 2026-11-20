using Calor.Compiler.Commands;
using Calor.Compiler.Migration;
using Xunit;

namespace Calor.Compiler.Tests;

public class CallCloserEliderTests
{
    [Fact]
    [Trait("Category", "HistoricalReplay")]
    public async Task ArchivedMigrations_ReplayAllOriginalElisionAndHealingCases()
    {
        var root = CliTestHarness.FindRepoRoot();
        var result = await ExecutionWorkspace.CaptureProcessAsync(
            "python3", [Path.Combine(root, "scripts", "registered_ledger_historical_replay.py"), "archived-closers"],
            root, TimeSpan.FromMinutes(20), CancellationToken.None);
        Assert.True(result.ExitCode == 0 && !result.TimedOut,
            $"Historical closer replay failed (exit {result.ExitCode}).\n{result.Output}");
    }

}
