using Calor.RoundTrip.Harness;
using Xunit;

namespace Calor.RoundTrip.Harness.Tests;

public class TrxParserTests
{
    [Fact]
    public void Parse_ValidTrxFile_ReturnsResults()
    {
        var trxContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="id-1" testName="Test1" outcome="Passed" duration="00:00:00.001" />
                <UnitTestResult testId="id-2" testName="Test2" outcome="Failed" duration="00:00:00.050">
                  <Output>
                    <ErrorInfo>
                      <Message>Assert.Equal failed</Message>
                      <StackTrace>at Tests.Test2() in Test.cs:line 10</StackTrace>
                    </ErrorInfo>
                  </Output>
                </UnitTestResult>
                <UnitTestResult testId="id-3" testName="Test3" outcome="NotExecuted" duration="00:00:00.000" />
              </Results>
              <TestDefinitions>
                <UnitTest id="id-1"><TestMethod /></UnitTest>
                <UnitTest id="id-2"><TestMethod /></UnitTest>
                <UnitTest id="id-3"><TestMethod /></UnitTest>
              </TestDefinitions>
              <ResultSummary outcome="Failed">
                <Counters total="3" executed="2" passed="1" failed="1" notExecuted="0" />
              </ResultSummary>
            </TestRun>
            """;

        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, trxContent);
            var results = TrxParser.Parse(tmpFile);

            Assert.Equal(3, results.Count);

            Assert.Equal("Test1", results[0].TestName);
            Assert.Equal("id-1", results[0].TestCaseId);
            Assert.Equal("Passed", results[0].Outcome);

            Assert.Equal("Test2", results[1].TestName);
            Assert.Equal("Failed", results[1].Outcome);
            Assert.Equal("Assert.Equal failed", results[1].ErrorMessage);
            Assert.Contains("line 10", results[1].StackTrace);

            Assert.Equal("Test3", results[2].TestName);
            Assert.Equal("NotExecuted", results[2].Outcome);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void Parse_EmptyResults_ReturnsEmptyList()
    {
        var trxContent = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results />
              <TestDefinitions />
              <ResultSummary outcome="Completed">
                <Counters total="0" executed="0" passed="0" failed="0" notExecuted="0" />
              </ResultSummary>
            </TestRun>
            """;

        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, trxContent);
            var results = TrxParser.Parse(tmpFile);
            Assert.Empty(results);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void FindTrxFiles_FindsAll_NotJustNewest()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "trx-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        Directory.CreateDirectory(Path.Combine(tmpDir, "nested"));

        try
        {
            var older = Path.Combine(tmpDir, "old.trx");
            File.WriteAllText(older, "<TestRun/>");
            Thread.Sleep(50);
            var newer = Path.Combine(tmpDir, "nested", "new.trx");
            File.WriteAllText(newer, "<TestRun/>");

            var found = TrxParser.FindTrxFiles(tmpDir);
            Assert.Equal(2, found.Count);
            Assert.Contains(older, found);
            Assert.Contains(newer, found);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Parse_JoinsTestDefinitions_ForAssemblyClassAndExecutor()
    {
        var trxContent = MakeTrx("alpha.tests.dll", "Alpha.Tests.Suite", ("TestA", "Passed"));

        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, trxContent);
            var results = TrxParser.Parse(tmpFile);

            var r = Assert.Single(results);
            Assert.Equal("TestA", r.TestName);
            Assert.Equal("alpha.tests.dll", r.Assembly);
            Assert.Equal("Alpha.Tests.Suite", r.ClassName);
            Assert.Equal("executor://xunit/VsTestRunner2/netcoreapp", r.ExecutorUri);
            Assert.Equal("Alpha.Tests.Suite.TestA", r.FullyQualifiedName);
            Assert.Equal("id-0", r.TestCaseId);
            Assert.Contains("alpha.tests.dll", r.Identity);
            Assert.Contains("Alpha.Tests.Suite", r.Identity);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void ParseAll_AggregatesEveryTrxFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "trx-agg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(tmpDir, "A", "TestResults"));
        Directory.CreateDirectory(Path.Combine(tmpDir, "B", "TestResults"));

        try
        {
            // Two projects with the same assembly/class/display identity. Project
            // provenance from the TRX path must keep them distinct.
            File.WriteAllText(
                Path.Combine(tmpDir, "A", "TestResults", "results.trx"),
                MakeTrx("shared.tests.dll", "Shared.Tests.Suite", ("SharedName", "Passed"), ("OnlyInA", "Passed")));
            File.WriteAllText(
                Path.Combine(tmpDir, "B", "TestResults", "results.trx"),
                MakeTrx("shared.tests.dll", "Shared.Tests.Suite", ("SharedName", "Failed"), ("OnlyInB", "Passed")));

            var (results, trxFiles, parseErrors) = TrxParser.ParseAll(tmpDir);

            Assert.Equal(2, trxFiles.Count);
            Assert.Empty(parseErrors);
            Assert.Equal(4, results.Count);
            Assert.Equal(3, results.Count(r => r.Outcome == "Passed"));
            Assert.Equal(1, results.Count(r => r.Outcome == "Failed"));

            // Duplicate display names across assemblies keep distinct identities.
            var shared = results.Where(r => r.TestName == "SharedName").ToList();
            Assert.Equal(2, shared.Count);
            Assert.NotEqual(shared[0].Identity, shared[1].Identity);
            Assert.Equal(["A", "B"], shared.Select(result => result.Project).Order().ToArray());
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void Identity_IncludesFullyQualifiedNameAndTheoryDataRow()
    {
        var first = new TestResult
        {
            Project = "Tests",
            Assembly = "tests.dll",
            ExecutorUri = "executor://xunit",
            FullyQualifiedName = "Suite.Theory",
            TestCaseId = "row-1",
            TestName = "Suite.Theory(value: 1)",
        };
        var second = new TestResult
        {
            Project = first.Project,
            Assembly = first.Assembly,
            ExecutorUri = first.ExecutorUri,
            FullyQualifiedName = first.FullyQualifiedName,
            TestCaseId = "row-2",
            TestName = "Suite.Theory(value: 2)",
        };

        Assert.NotEqual(first.Identity, second.Identity);
    }

    [Fact]
    public void ParseAll_ReportsUnparseableTrx()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "trx-bad-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);

        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "bad.trx"), "not xml at all <<<");
            File.WriteAllText(
                Path.Combine(tmpDir, "good.trx"),
                MakeTrx("alpha.tests.dll", "Alpha.Tests.Suite", ("TestA", "Passed")));

            var (results, trxFiles, parseErrors) = TrxParser.ParseAll(tmpDir);

            Assert.Single(trxFiles);
            Assert.Single(results);
            Assert.Single(parseErrors);
            Assert.Contains("bad.trx", parseErrors[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ParseAll_ReportsStructurallyIncompleteTrx()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "trx-incomplete-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);

        try
        {
            File.WriteAllText(
                Path.Combine(tmpDir, "partial.trx"),
                """
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Results />
                  <TestDefinitions />
                </TestRun>
                """);

            var (results, trxFiles, parseErrors) = TrxParser.ParseAll(tmpDir);

            Assert.Empty(trxFiles);
            Assert.Empty(results);
            Assert.Single(parseErrors);
            Assert.Contains("ResultSummary/Counters", parseErrors[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ParseAll_ReportsResultWithInvalidOutcome()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "trx-outcome-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);

        try
        {
            var malformed = MakeTrx("alpha.tests.dll", "Alpha.Tests.Suite", ("TestA", "Unknown"));
            File.WriteAllText(Path.Combine(tmpDir, "invalid-outcome.trx"), malformed);

            var (results, trxFiles, parseErrors) = TrxParser.ParseAll(tmpDir);

            Assert.Empty(trxFiles);
            Assert.Empty(results);
            Assert.Single(parseErrors);
            Assert.Contains("invalid outcome", parseErrors[0], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    /// <summary>Builds a minimal TRX with Results joined to TestDefinitions.</summary>
    internal static string MakeTrx(string assembly, string className, params (string Name, string Outcome)[] tests)
    {
        var results = string.Join("\n", tests.Select((t, i) =>
            $"""    <UnitTestResult testId="id-{i}" testName="{t.Name}" outcome="{t.Outcome}" duration="00:00:00.001" />"""));
        var definitions = string.Join("\n", tests.Select((t, i) =>
            $"""
                <UnitTest id="id-{i}" name="{t.Name}" storage="/work/bin/Debug/{assembly}">
                  <TestMethod codeBase="/work/bin/Debug/{assembly}" adapterTypeName="executor://xunit/VsTestRunner2/netcoreapp" className="{className}" name="{t.Name}" />
                </UnitTest>
            """));
        var passed = tests.Count(test => test.Outcome == "Passed");
        var failed = tests.Count(test => test.Outcome == "Failed");
        var notExecuted = tests.Length - passed - failed;

        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
            {results}
              </Results>
              <TestDefinitions>
            {definitions}
              </TestDefinitions>
              <ResultSummary outcome="{(failed > 0 ? "Failed" : "Completed")}">
                <Counters total="{tests.Length}" executed="{passed + failed}" passed="{passed}" failed="{failed}" notExecuted="{notExecuted}" />
              </ResultSummary>
            </TestRun>
            """;
    }

    [Fact]
    public void CleanTrxFiles_DeletesAllTrxFiles()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "trx-clean-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);

        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "a.trx"), "");
            File.WriteAllText(Path.Combine(tmpDir, "b.trx"), "");
            File.WriteAllText(Path.Combine(tmpDir, "keep.txt"), "");

            TrxParser.CleanTrxFiles(tmpDir);

            Assert.Empty(Directory.GetFiles(tmpDir, "*.trx"));
            Assert.Single(Directory.GetFiles(tmpDir, "*.txt"));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }
}
