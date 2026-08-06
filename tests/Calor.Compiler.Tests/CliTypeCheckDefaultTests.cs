using Xunit;

namespace Calor.Compiler.Tests;

/// <summary>
/// PP-A1 item 9 regression pins: the type checker defaults ON as of v0.12, with
/// <c>--no-type-check</c> and <c>CALOR_NO_TYPE_CHECK=1</c> as the explicit opt-outs.
///
/// These run the real CLI on purpose. The release review of v0.12 caught a pin that set
/// <c>CompilationOptions.EnableTypeChecking = false</c> directly and therefore passed with the
/// entire opt-out removed — that property was init-settable long before the flag existed, so it
/// pinned nothing. Only a subprocess can tell whether the flag is actually wired.
///
/// The env-var cases exist because the opt-out is read at the <c>CompilationOptions</c> default
/// rather than threaded through the ~30 sites that construct it: <c>run</c>, <c>test</c>,
/// <c>watch</c>, <c>verify</c>, the MCP tools and the MSBuild task inside the published SDK all
/// type-check, and hand-threading a flag would have covered <c>build</c> only.
/// </summary>
public class CliTypeCheckDefaultTests : IDisposable
{
    private readonly string _tempDir;

    public CliTypeCheckDefaultTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "calor-cli-tc-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>A type error the checker catches and nothing else on the build path does.</summary>
    private string WriteTypeViolation()
    {
        var path = Path.Combine(_tempDir, "violation.calr");
        File.WriteAllText(path, """
            §M{m001:Test}
              §F{f001:Main:pub} () -> void
                §E{}
                §B{x:i32} STR:"not an int"
                §R
            """);
        return path;
    }

    [Fact]
    public void Cli_Default_TypeChecks()
    {
        var path = WriteTypeViolation();

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(_tempDir, "--input", path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Calor0202", stdOut + stdErr);
    }

    [Fact]
    public void Cli_NoTypeCheckFlag_OptsOut()
    {
        var path = WriteTypeViolation();

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir, "--input", path, "--no-type-check");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Calor0202", stdOut + stdErr);
    }

    /// <summary>
    /// `calor run` type-checks too, and does NOT accept `--no-type-check` (the flag is scoped to
    /// the root/build command, as `--no-enforce-effects` is). The environment variable is what
    /// makes the opt-out reachable there — without it, a program that ran under v0.11 has no way
    /// to run under v0.12.
    /// </summary>
    [Fact]
    public void Run_TypeChecks_AndEnvVarOptsOut()
    {
        var path = WriteTypeViolation();

        var on = CliTestHarness.RunCli(_tempDir, "run", path);
        Assert.NotEqual(0, on.ExitCode);
        Assert.Contains("Calor0202", on.StdOut + on.StdErr);

        var off = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = "1" },
            "run", path);
        Assert.DoesNotContain("Calor0202", off.StdOut + off.StdErr);
    }

    [Fact]
    public void EnvVar_OptsOut_OnBuildToo()
    {
        var path = WriteTypeViolation();

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = "1" },
            "--input", path);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Calor0202", stdOut + stdErr);
    }

    /// <summary>
    /// The variable is opt-OUT only, and only on an explicit truthy value: an unrelated or empty
    /// setting must not silently disable checking.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("no")]
    public void EnvVar_NonTruthyValue_LeavesCheckingOn(string value)
    {
        var path = WriteTypeViolation();

        var (exitCode, stdOut, stdErr) = CliTestHarness.RunCli(
            _tempDir,
            new Dictionary<string, string> { ["CALOR_NO_TYPE_CHECK"] = value },
            "--input", path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Calor0202", stdOut + stdErr);
    }
}
