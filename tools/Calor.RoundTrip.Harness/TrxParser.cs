using System.Xml.Linq;

namespace Calor.RoundTrip.Harness;

/// <summary>
/// Parses Visual Studio TRX (Test Results XML) files.
/// </summary>
public static class TrxParser
{
    public static List<TestResult> Parse(string trxPath)
    {
        var doc = XDocument.Load(trxPath);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        return doc.Descendants(ns + "UnitTestResult")
            .Select(e => new TestResult
            {
                TestName = e.Attribute("testName")?.Value ?? "",
                Outcome = e.Attribute("outcome")?.Value ?? "Unknown",
                Duration = TimeSpan.TryParse(e.Attribute("duration")?.Value, out var d) ? d : TimeSpan.Zero,
                ErrorMessage = e.Descendants(ns + "Message").FirstOrDefault()?.Value,
                StackTrace = e.Descendants(ns + "StackTrace").FirstOrDefault()?.Value,
            })
            .ToList();
    }

    /// <summary>
    /// Find the most recent TRX file in the test results directories.
    /// </summary>
    public static string? FindTrxFile(string workDir)
    {
        return Directory.GetFiles(workDir, "*.trx", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Delete all TRX files under the working directory to avoid stale results.
    /// </summary>
    public static void CleanTrxFiles(string workDir)
    {
        foreach (var trx in Directory.GetFiles(workDir, "*.trx", SearchOption.AllDirectories))
        {
            try { File.Delete(trx); } catch { }
        }
    }
}
