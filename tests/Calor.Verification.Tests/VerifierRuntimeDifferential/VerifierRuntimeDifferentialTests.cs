using System.Text;
using Calor.Compiler.Verification.Z3;
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
        Assert.Equal(1_170, report.Coverage.MatrixCellsCovered);
        Assert.Equal(1_170, report.Coverage.CasesGenerated);
        Assert.Equal(0, report.Coverage.Mismatches);
        Assert.Equal(10, report.FailSafeControls.Count);
        Assert.All(report.FailSafeControls, control => Assert.True(control.Passed));

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
            if (Environment.GetEnvironmentVariable(UpdateReportsVariable) == "1")
                File.WriteAllBytes(path, generatedBytes);

            Assert.True(File.Exists(path), $"Committed report is missing: {path}");
            Assert.True(
                File.ReadAllBytes(path).AsSpan().SequenceEqual(generatedBytes),
                $"Committed report is stale: {path}. Set {UpdateReportsVariable}=1 and rerun this test.");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))
                && Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Calor repository root.");
    }
}
