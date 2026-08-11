using System.Text;
using System.Runtime.CompilerServices;
using Calor.Compiler.Verification;
using Calor.Compiler.Verification.Z3;
using Microsoft.Z3;
using Xunit;

namespace Calor.Verification.Tests.VerifierRuntimeDifferential;

public sealed class VerifierRuntimeDifferentialTests
{
    private const string UpdateReportsVariable =
        "CALOR_UPDATE_VERIFIER_RUNTIME_DIFFERENTIAL_REPORTS";

    [Fact]
    public void CommittedReportsMatchGeneratedOracle()
    {
        Assert.True(
            Z3ContextFactory.IsAvailable,
            "F-4 differential gate cannot run: Z3 is unavailable. This is a blocking failure, not a skip.");

        var repositoryRoot = FindRepositoryRoot();
        var report = DifferentialGate.Run(repositoryRoot);

        Assert.True(report.Passed, ReportWriter.ToJson(report));
        Assert.Equal(65, report.Coverage.FormsWhitelisted);
        Assert.Equal(65, report.Coverage.FormsCovered);
        Assert.Equal(1_170, report.Coverage.MatrixCellsRegistered);
        Assert.Equal(1_170, report.Coverage.MatrixCellsApplicable);
        Assert.Equal(1_170, report.Coverage.MatrixCellsCovered);
        Assert.Equal(1_170, report.Coverage.CasesGenerated);
        Assert.Equal(0, report.Coverage.Mismatches);
        Assert.Equal(10, report.FailSafeControls.Count);
        Assert.All(report.FailSafeControls, control => Assert.True(control.Passed));
        var timeoutControls = report.FailSafeControls
            .Where(control => control.Scenario == "timeout")
            .ToList();
        var solverErrorControls = report.FailSafeControls
            .Where(control => control.Scenario == "solver-error")
            .ToList();
        Assert.Equal(2, timeoutControls.Count);
        Assert.Equal(2, solverErrorControls.Count);
        Assert.All(timeoutControls, control => Assert.Equal("timeout", control.Status));
        Assert.All(solverErrorControls, control => Assert.Equal("unknown", control.Status));
        Assert.All(
            report.Forms.Where(form => form.Applicable),
            form => Assert.True(form.SolverHandled, form.Id));

        var fieldAccess = report.Forms.Single(
            form => form.Id == "expression-kind:FieldAccessNode");
        Assert.Equal(18, fieldAccess.Cases);
        Assert.Equal(3, fieldAccess.Statuses["proven"]);
        Assert.Equal(6, fieldAccess.Statuses["assumed"]);
        Assert.Equal(9, fieldAccess.Statuses["refuted"]);
        Assert.False(fieldAccess.Statuses.ContainsKey("unsupported"));

        var reports = new[]
        {
            (
                Path.Combine(
                    repositoryRoot,
                    "bench",
                    "phase0-agent-native",
                    "verifier-runtime-differential.json"),
                ReportWriter.ToJson(report)),
            (
                Path.Combine(
                    repositoryRoot,
                    "bench",
                    "phase0-agent-native",
                    "verifier-runtime-differential.md"),
                ReportWriter.ToMarkdown(report))
        };

        foreach (var (path, generated) in reports)
        {
            var generatedBytes = Encoding.UTF8.GetBytes(generated);
            Assert.DoesNotContain((byte)'\r', generatedBytes);
            if (Environment.GetEnvironmentVariable(UpdateReportsVariable) == "1")
                File.WriteAllBytes(path, generatedBytes);

            Assert.True(File.Exists(path), $"Committed report is missing: {path}");
            var committedBytes = File.ReadAllBytes(path);
            Assert.DoesNotContain((byte)'\r', committedBytes);
            Assert.True(
                committedBytes.AsSpan().SequenceEqual(generatedBytes),
                $"Committed report is stale: {path}. Set {UpdateReportsVariable}=1 and rerun this test.");
        }
    }

    [Fact]
    public void SolverClassificationSeamDistinguishesTimeoutFromSolverError()
    {
        var timeout = ProofOutcome.ClassifySolverStatus(
            Status.UNKNOWN,
            SatPolarity.SatIsRefutation,
            reasonUnknown: "timeout: deterministic regression");
        var solverError = ProofOutcome.ClassifySolverException(
            new InvalidOperationException("deterministic regression"));

        Assert.Equal(ProofStatus.Timeout, timeout.Status);
        Assert.Equal(ProofStatus.Unknown, solverError.Status);
        Assert.Contains("Z3 solver error", solverError.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryRootDiscoveryAcceptsGitFileWorktrees()
    {
        const string candidate = "/worktree";
        Assert.True(IsRepositoryRootCandidate(
            candidate,
            path => path == Path.Combine(candidate, "Directory.Build.props")
                || path == Path.Combine(candidate, ".git"),
            _ => false));
    }

    [Fact]
    public void GeneratedAssembliesAreCollectibleAndUnload()
    {
        var loadContext = CompileInvokeAndUnload();

        for (var attempt = 0; attempt < 10 && loadContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(loadContext.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompileInvokeAndUnload()
    {
        const string code = """
            namespace CollectibleProbe
            {
                internal static class CollectibleProbeModule
                {
                    internal static void Execute()
                    {
                    }
                }
            }
            """;
        using var runtime = GeneratedRuntime.Compile(
            "CalorVerifierDifferentialCollectible",
            code,
            "CollectibleProbe");
        Assert.True(runtime.IsCollectible);
        Assert.Equal(
            RuntimeVerdict.Completed,
            runtime.Invoke("Execute", Array.Empty<string>(), out _));
        return runtime.LoadContextReference;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (IsRepositoryRootCandidate(directory.FullName, File.Exists, Directory.Exists))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Calor repository root.");
    }

    private static bool IsRepositoryRootCandidate(
        string directory,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists)
    {
        return fileExists(Path.Combine(directory, "Directory.Build.props"))
            && (directoryExists(Path.Combine(directory, ".git"))
                || fileExists(Path.Combine(directory, ".git")));
    }
}
