using System.Text.Json;
using Calor.Compiler.Commands;
using Calor.Compiler.Diagnostics;
using Calor.Compiler.Mcp;
using Calor.Compiler.Mcp.Tools;
using Calor.Compiler.Verification;
using Xunit;

namespace Calor.Compiler.Tests;

public sealed class ValidationLoopTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"calor-loop-tests-{Guid.NewGuid():N}");

    public ValidationLoopTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string SourcePath => Path.Combine(_directory, "math.calr");

    private void WriteFixture(bool failing = false, string? contract = null, string? testBody = null)
    {
        File.WriteAllText(SourcePath, $$"""
            §M{m001:MathUtil}
              §F{f001:Add:pub} (i32:a, i32:b) -> i32
                {{contract ?? ""}}
                §R (+ a {{(failing ? "(+ b 1)" : "b")}})
            """);
        Directory.CreateDirectory(Path.Combine(_directory, "tests"));
        File.WriteAllText(Path.Combine(_directory, "tests", "MathTests.cs"), $$"""
            using Xunit;
            public sealed class MathTests
            {
                [Fact]
                public void Sum()
                {
                    {{testBody ?? "Assert.Equal(5, MathUtil.MathUtilModule.Add(2, 3));"}}
                }
            }
            """);
    }

    private async Task<JsonElement> RunAsync(bool mcp, int timeout = 600)
    {
        if (mcp)
        {
            var arguments = JsonSerializer.SerializeToElement(new { action = "loop", path = _directory, timeoutSeconds = timeout });
            // Go through the protocol dispatcher, not only the tool implementation.
            var response = await new McpMessageHandler().HandleRequestAsync(new JsonRpcRequest
            {
                Id = JsonSerializer.SerializeToElement(1),
                Method = "tools/call",
                Params = JsonSerializer.SerializeToElement(new { name = "calor_check", arguments })
            });
            Assert.NotNull(response);
            Assert.Null(response.Error);
            var result = Assert.IsType<McpToolResult>(response.Result);
            var data = JsonDocument.Parse(result.Content[0].Text!).RootElement.Clone();
            Assert.Equal(!data.GetProperty("success").GetBoolean(), result.IsError);
            return data;
        }

        var cli = CliTestHarness.RunCli(_directory, "check", _directory, "--format", "json", "--timeout", timeout.ToString());
        var json = JsonDocument.Parse(cli.StdOut).RootElement.Clone();
        Assert.Equal(json.GetProperty("exitCode").GetInt32(), cli.ExitCode);
        return json;
    }

    private static JsonElement Stage(JsonElement output, string name)
        => Assert.Single(output.GetProperty("stages").EnumerateArray(), s => s.GetProperty("name").GetString() == name);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Passing_BuildsTestsAndProvesContracts_WithoutSuccessNoise(bool mcp)
    {
        WriteFixture();
        File.AppendAllText(SourcePath, """

              §F{f002:Identity:pub} (i32:x) -> i32
                §S (== result x)
                §R x
            """);
        var before = File.ReadAllBytes(SourcePath);
        var result = await RunAsync(mcp);
        Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
        Assert.Empty(result.GetProperty("failures").EnumerateArray());
        Assert.Equal(1, result.GetProperty("testsRun").GetInt32());
        Assert.Equal(1, result.GetProperty("contractsChecked").GetInt32());
        Assert.All(result.GetProperty("stages").EnumerateArray(), s => Assert.Equal("passed", s.GetProperty("status").GetString()));
        Assert.Equal(before, File.ReadAllBytes(SourcePath));
        Assert.Equal(new[] { "math.calr", "tests" }, Directory.GetFileSystemEntries(_directory).Select(Path.GetFileName).Order().ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompileError_PreservesDiagnosticAndSafeFixWithOriginalPath(bool mcp)
    {
        WriteFixture();
        File.AppendAllText(SourcePath, "\n§/M\n");
        var before = File.ReadAllText(SourcePath);
        var result = await RunAsync(mcp);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("failed", Stage(result, "compile").GetProperty("status").GetString());
        Assert.Equal("skipped", Stage(result, "test").GetProperty("status").GetString());
        var diagnostic = result.GetProperty("failures").EnumerateArray()
            .Select(f => f.GetProperty("diagnostic")).First(d => d.GetProperty("code").GetString() == "Calor0830");
        Assert.Equal(SourcePath, diagnostic.GetProperty("location").GetProperty("file").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        var fix = diagnostic.GetProperty("fix");
        Assert.True(fix.GetProperty("isSafeToApply").GetBoolean());
        Assert.All(fix.GetProperty("edits").EnumerateArray(),
            e => Assert.Equal(SourcePath, e.GetProperty("filePath").GetString()));
        Assert.Equal(before, File.ReadAllText(SourcePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidGeneratedCode_IsNotTestSuccess(bool mcp)
    {
        WriteFixture();
        File.WriteAllText(SourcePath, """
            §M{m001:Broken}
              §CSHARP{
              public class Bad { public int Get() => "not an integer"; }
              }§/CSHARP
            """);
        var result = await RunAsync(mcp);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Calor1002", result.ToString());
        Assert.Equal("skipped", Stage(result, "test").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestProjectBuildError_ReportsBuildStage(bool mcp)
    {
        WriteFixture(testBody: "ThisIsNotValidCSharp;");
        var result = await RunAsync(mcp);
        Assert.Equal("failed", Stage(result, "build").GetProperty("status").GetString());
        Assert.Equal("skipped", Stage(result, "test").GetProperty("status").GetString());
        Assert.Contains("error CS", result.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingTest_ReturnsOnlyFailingTestDetails(bool mcp)
    {
        WriteFixture(failing: true);
        var result = await RunAsync(mcp);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("passed", Stage(result, "build").GetProperty("status").GetString());
        Assert.Equal("failed", Stage(result, "test").GetProperty("status").GetString());
        var failure = Assert.Single(result.GetProperty("failures").EnumerateArray());
        Assert.Equal("test", failure.GetProperty("stage").GetString());
        Assert.Contains("MathTests.Sum", failure.GetProperty("message").GetString());
        Assert.Contains("Assert.Equal", failure.GetProperty("message").GetString());
        Assert.DoesNotContain("Build succeeded", result.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoTestFiles_ReturnsDistinctExitThree(bool mcp)
    {
        WriteFixture();
        File.Delete(Path.Combine(_directory, "tests", "MathTests.cs"));
        var result = await RunAsync(mcp);
        Assert.Equal(3, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("passed", Stage(result, "build").GetProperty("status").GetString());
        Assert.Equal("no_tests", Stage(result, "test").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoDiscoveredTests_IsNotRunnerSuccess(bool mcp)
    {
        WriteFixture();
        File.WriteAllText(Path.Combine(_directory, "tests", "MathTests.cs"), "public sealed class NotATest {}");
        var result = await RunAsync(mcp);
        Assert.Equal(3, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("no_tests", Stage(result, "test").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkippedTest_IsNotReportedAsExecutedOrPassed(bool mcp)
    {
        WriteFixture();
        var testPath = Path.Combine(_directory, "tests", "MathTests.cs");
        File.WriteAllText(testPath, File.ReadAllText(testPath).Replace("[Fact]", "[Fact(Skip = \"not executed\")]"));
        var result = await RunAsync(mcp);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal(0, result.GetProperty("testsRun").GetInt32());
        Assert.Equal("failed", Stage(result, "test").GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefutedContract_ReportsCounterexampleEvenWhenTestsPass(bool mcp)
    {
        WriteFixture(contract: "§S (> result a)");
        var result = await RunAsync(mcp);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("failed", Stage(result, "verify").GetProperty("status").GetString());
        Assert.Equal("passed", Stage(result, "test").GetProperty("status").GetString());
        var verification = result.GetProperty("failures").EnumerateArray()
            .First(f => f.GetProperty("stage").GetString() == "verify").GetProperty("diagnostic").GetProperty("verification");
        Assert.Equal("refuted", verification.GetProperty("status").GetString());
        Assert.NotEmpty(verification.GetProperty("counterexample").GetProperty("bindings").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssumedContract_RemainsNonProvenWithAssumptions(bool mcp)
    {
        WriteFixture(contract: "§S (== result (+ a b))");
        var result = await RunAsync(mcp);
        Assert.False(result.GetProperty("success").GetBoolean());
        var proof = result.GetProperty("failures").EnumerateArray()
            .First(f => f.GetProperty("stage").GetString() == "verify").GetProperty("diagnostic").GetProperty("verification");
        Assert.Equal("assumed", proof.GetProperty("status").GetString());
        Assert.NotEmpty(proof.GetProperty("assumptions").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedAndVacuousContracts_RemainFailures(bool mcp)
    {
        WriteFixture();
        File.AppendAllText(SourcePath, """

              §F{f002:FloatIdentity:pub} (f64:x) -> f64
                §S (>= result FLOAT:0.0)
                §R x
              §F{f003:Vacuous:pub} (i32:x) -> i32
                §Q (> x 0)
                §Q (< x 0)
                §S (== result x)
                §R x
            """);
        var result = await RunAsync(mcp);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("passed", Stage(result, "test").GetProperty("status").GetString());
        var proofs = result.GetProperty("failures").EnumerateArray()
            .Where(f => f.GetProperty("stage").GetString() == "verify")
            .Select(f => f.GetProperty("diagnostic").GetProperty("verification")).ToList();
        Assert.Contains(proofs, p => p.GetProperty("status").GetString() == "unsupported");
        Assert.Contains(proofs, p => p.GetProperty("status").GetString() == "proven"
            && p.TryGetProperty("vacuous", out var vacuous) && vacuous.GetBoolean());
    }

    [Fact]
    public async Task MissingZ3_InIsolatedDistribution_IsUnavailableNotSuccess()
    {
        WriteFixture(contract: "§S (== result (+ a b))");
        var distribution = Path.Combine(_directory, "distribution");
        Directory.CreateDirectory(distribution);
        var originalAssembly = typeof(Program).Assembly.Location;
        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(originalAssembly)!))
        {
            if (Path.GetFileName(file).StartsWith("libz3", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(distribution, Path.GetFileName(file)));
        }
        var process = await ExecutionWorkspace.CaptureProcessAsync(ExecutionWorkspace.FindDotnet()!,
            [Path.Combine(distribution, Path.GetFileName(originalAssembly)), "check", _directory, "--format", "json"],
            _directory, TimeSpan.FromSeconds(60), CancellationToken.None);
        var result = JsonDocument.Parse(process.Output).RootElement;
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("passed", Stage(result, "test").GetProperty("status").GetString());
        Assert.Contains(result.GetProperty("failures").EnumerateArray(), f =>
            f.GetProperty("diagnostic").GetProperty("verification").GetProperty("status").GetString() == "unavailable");
    }

    [Fact]
    public async Task MultipleModules_BuildAndTestTogether()
    {
        WriteFixture();
        File.WriteAllText(Path.Combine(_directory, "other.calr"), """
            §M{m002:Other}
              §F{f002:Sum:pub} () -> i32
                §R §C{Add} §A 2 §A 3 §/C
            """);
        var result = await RunAsync(mcp: true);
        Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
    }

    [Theory]
    [InlineData("proven", false, false)]
    [InlineData("proven", true, true)]
    [InlineData("refuted", false, true)]
    [InlineData("assumed", false, true)]
    [InlineData("unknown", false, true)]
    [InlineData("timeout", false, true)]
    [InlineData("unsupported", false, true)]
    [InlineData("unavailable", false, true)]
    public void ProofPolicy_PreservesAllStatusesAndVacuity(string status, bool vacuous, bool failure)
    {
        var outcome = ProofOutcome.Rehydrate(status, null, "detail", vacuous,
            status == "assumed" ? ["callee summary"] : null);
        var proof = DiagnosticEnvelope.BuildVerification(outcome)!;
        Assert.Equal(status, proof.Status);
        Assert.Equal(failure, TestCommand.IsProofFailure(proof));
        if (status == "assumed") Assert.Equal(new[] { "callee summary" }, proof.Assumptions);
        if (vacuous) Assert.True(proof.Vacuous);
    }

    [Fact]
    public async Task PreCancelled_CLIEquivalentAndMcpPropagateCancellation()
    {
        WriteFixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestCommand.RunLoopAsync(_directory, cancellationToken: cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CheckTool().ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "loop", path = _directory }), cts.Token));
    }

    [Fact]
    public async Task RunningProcess_CancellationAndTimeoutTerminateProcessTree()
    {
        var dotnet = ExecutionWorkspace.FindDotnet()!;
        // The MCP server waits on stdin, providing a real blocking child without a shell.
        var compiler = typeof(Program).Assembly.Location;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExecutionWorkspace.CaptureProcessAsync(
            dotnet, [compiler, "mcp"], _directory, TimeSpan.FromSeconds(30), cts.Token));
        var timedOut = await ExecutionWorkspace.CaptureProcessAsync(
            dotnet, [compiler, "mcp"], _directory, TimeSpan.FromMilliseconds(300), CancellationToken.None);
        Assert.True(timedOut.TimedOut);
        Assert.Equal(2, timedOut.ExitCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HangingTest_ReturnsTimeoutWithoutClaimingTestSuccess(bool mcp)
    {
        WriteFixture(testBody: "System.Threading.Thread.Sleep(30000);");
        var result = await RunAsync(mcp, timeout: 5);
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("timeout", Stage(result, "test").GetProperty("status").GetString());
        Assert.Contains("timed out", result.ToString());
    }

    [Fact]
    public async Task ActiveLoop_CancellationReachesRunningTest()
    {
        var marker = Path.Combine(_directory, "running");
        WriteFixture(testBody: $$"""
            System.IO.File.WriteAllText({{JsonSerializer.Serialize(marker)}}, "running");
            System.Threading.Thread.Sleep(30000);
            """);
        using var cts = new CancellationTokenSource();
        var running = new CheckTool().ExecuteAsync(
            JsonSerializer.SerializeToElement(new { action = "loop", path = _directory }), cts.Token);
        for (var i = 0; i < 400 && !File.Exists(marker) && !running.IsCompleted; i++)
            await Task.Delay(50);
        cts.Cancel();
        Assert.True(File.Exists(marker), "The test did not reach execution.");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Fact]
    public async Task ProcessStartError_IsExplicit()
    {
        var result = await ExecutionWorkspace.CaptureProcessAsync(
            Path.Combine(_directory, "missing-dotnet"), [], _directory, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Failed to start", result.Output);
    }

    [Fact]
    public async Task Snapshot_IsUnaffectedBySourceMutationDuringTest()
    {
        WriteFixture(testBody: $$"""
            System.IO.File.WriteAllText({{JsonSerializer.Serialize(SourcePath)}}, "edited after compilation");
            Assert.Equal(5, MathUtil.MathUtilModule.Add(2, 3));
            """);
        var result = await RunAsync(mcp: true);
        Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
        Assert.Equal("edited after compilation", File.ReadAllText(SourcePath));
    }

    [Fact]
    public void LoopSchema_DoesNotClaimExecutionIsReadOnlyOrIdempotent()
    {
        var tool = new CheckTool();
        Assert.False(tool.Annotations!.ReadOnlyHint);
        Assert.False(tool.Annotations.IdempotentHint);
        Assert.Contains("loop", tool.GetInputSchema().GetProperty("properties").GetProperty("action").GetProperty("enum").ToString());
    }
}
