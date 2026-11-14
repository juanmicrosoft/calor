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

    [Theory]
    [InlineData("  §C{Doit} §/C\n")]
    [InlineData("  §C{Doit} §A x §/C\n")]
    [InlineData("  §B{r:i32} §C{Add} §A 1 §/C\n")]
    [InlineData("  §C{Doit}\n    §A x\n  §/C\n")]
    [InlineData("  §C{Doit} §A[name] x §/C\n")]
    [InlineData("  §C{Add} §A 1 §A 2 §/C\n")]
    [InlineData("  §C{Outer} §A §C{Inner} §A x §/C §/C\n")]
    [InlineData("  §C{Doit} x\n")]
    [InlineData("  §C{Doit} §A x §/C\n  §C{Another} §/C\n")]
    public void CurrentCompiler_RejectsArchivedCallsWithoutChangingSource(string body)
    {
        var source = "§M{Calc}\n§F{run:pub}\n" + body;
        var result = new CallCloserElider().Process(source, "archive.calr");

        Assert.True(result.Skipped);
        Assert.Equal("lexer errors", result.SkipReason);
        Assert.Equal(source, result.MigratedSource);
        Assert.Equal(0, result.CallsElided);
        Assert.Empty(result.Removals);
    }

    [Fact]
    public void CurrentCompiler_NativeSourceNeedsNoLegacyElision()
    {
        const string source = """
            module Native
              func Abs (int:value) → int
                !
                return System.Math.Abs(value)
            """;
        Assert.False(Program.Compile(source).HasErrors);
        var result = new CallCloserElider().Process(source, "native.calr");

        Assert.False(result.Skipped, result.SkipReason);
        Assert.Equal(source, result.MigratedSource);
        Assert.Equal(0, result.CallsElided);
        Assert.Empty(result.Removals);
    }

    [Fact]
    public void CurrentCompiler_LexerErrorsDoNotProduceEdits()
    {
        const string source = "§§§INVALID";
        var result = new CallCloserElider().Process(source, "invalid.calr");

        Assert.True(result.Skipped);
        Assert.Equal("lexer errors", result.SkipReason);
        Assert.Equal(source, result.MigratedSource);
        Assert.Equal(0, result.CallsElided);
        Assert.Empty(result.Removals);
    }
}
