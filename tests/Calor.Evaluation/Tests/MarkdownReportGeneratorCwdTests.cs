using Calor.Evaluation.Core;
using Calor.Evaluation.Reports;
using Xunit;

namespace Calor.Evaluation.Tests;

/// <summary>
/// Regression test for the flaky "Source-Declared Compiler Version" assertion: the report
/// generator must find the repository without relying on Environment.CurrentDirectory, which the
/// pair oracle changes to a temp sandbox while it runs. This class changes the current directory
/// itself, so it runs in the non-parallel process-global-state collection.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
public class MarkdownReportGeneratorCwdTests
{
    [Fact]
    public void Generate_ResolvesSourceVersion_WhenCurrentDirectoryIsOutsideRepository()
    {
        var result = new EvaluationResult { BenchmarkCount = 1, CommitHash = "HEAD", Summary = new EvaluationSummary() };
        var generator = new MarkdownReportGenerator();
        var sandbox = Directory.CreateTempSubdirectory("md-report-cwd-");
        var originalDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = sandbox.FullName;
            var markdown = generator.Generate(result);
            Assert.Contains("Source-Declared Compiler Version", markdown);
        }
        finally
        {
            Environment.CurrentDirectory = originalDir;
            sandbox.Delete(recursive: true);
        }
    }
}
