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
                <UnitTestResult testName="Test1" outcome="Passed" duration="00:00:00.001" />
                <UnitTestResult testName="Test2" outcome="Failed" duration="00:00:00.050">
                  <Output>
                    <ErrorInfo>
                      <Message>Assert.Equal failed</Message>
                      <StackTrace>at Tests.Test2() in Test.cs:line 10</StackTrace>
                    </ErrorInfo>
                  </Output>
                </UnitTestResult>
                <UnitTestResult testName="Test3" outcome="NotExecuted" duration="00:00:00.000" />
              </Results>
            </TestRun>
            """;

        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, trxContent);
            var results = TrxParser.Parse(tmpFile);

            Assert.Equal(3, results.Count);

            Assert.Equal("Test1", results[0].TestName);
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
    public void FindTrxFile_FindsMostRecent()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "trx-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);

        try
        {
            var older = Path.Combine(tmpDir, "old.trx");
            File.WriteAllText(older, "<TestRun/>");
            Thread.Sleep(100);
            var newer = Path.Combine(tmpDir, "new.trx");
            File.WriteAllText(newer, "<TestRun/>");

            var found = TrxParser.FindTrxFile(tmpDir);
            Assert.NotNull(found);
            Assert.Equal(newer, found);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
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
