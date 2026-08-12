using System.Xml.Linq;

namespace Calor.RoundTrip.Harness;

/// <summary>
/// Parses Visual Studio TRX (Test Results XML) files.
/// </summary>
public static class TrxParser
{
    /// <summary>
    /// Parse a single TRX file, joining each UnitTestResult to its TestDefinitions
    /// entry so results carry assembly, class, and executor identity — display names
    /// alone collide across assemblies.
    /// </summary>
    public static List<TestResult> Parse(string trxPath)
    {
        var doc = XDocument.Load(trxPath);
        var root = doc.Root ?? throw new InvalidDataException("TRX has no document element.");
        if (root.Name.LocalName != "TestRun")
            throw new InvalidDataException("TRX root element must be TestRun.");

        var ns = root.GetDefaultNamespace();
        var resultsElement = root.Element(ns + "Results")
            ?? throw new InvalidDataException("TRX is missing Results.");
        var definitionsElement = root.Element(ns + "TestDefinitions")
            ?? throw new InvalidDataException("TRX is missing TestDefinitions.");
        var counters = root.Element(ns + "ResultSummary")?.Element(ns + "Counters")
            ?? throw new InvalidDataException("TRX is missing ResultSummary/Counters.");

        // Index TestDefinitions by test id for identity joining.
        var definitions = new Dictionary<string, (string Assembly, string ClassName, string ExecutorUri)>();
        foreach (var unitTest in definitionsElement.Elements(ns + "UnitTest"))
        {
            var id = unitTest.Attribute("id")?.Value;
            if (id == null) continue;

            var storage = unitTest.Attribute("storage")?.Value ?? "";
            var assembly = storage.Length > 0 ? Path.GetFileName(storage) : "";
            var testMethod = unitTest.Element(ns + "TestMethod");
            var className = testMethod?.Attribute("className")?.Value ?? "";
            // The adapter type is the closest thing TRX carries to an executor URI.
            var adapter = testMethod?.Attribute("adapterTypeName")?.Value ?? "";

            definitions[id] = (assembly, className, adapter);
        }

        var results = resultsElement.Elements(ns + "UnitTestResult")
            .Select(e =>
            {
                var testId = e.Attribute("testId")?.Value
                    ?? throw new InvalidDataException("TRX result is missing testId.");
                if (!definitions.TryGetValue(testId, out var def))
                    throw new InvalidDataException(
                        $"TRX result references missing test definition '{testId}'.");
                var outcome = e.Attribute("outcome")?.Value
                    ?? throw new InvalidDataException("TRX result is missing outcome.");
                if (outcome is not ("Passed" or "Failed" or "NotExecuted" or "Skipped"))
                    throw new InvalidDataException($"TRX result has invalid outcome '{outcome}'.");

                return new TestResult
                {
                    TestName = e.Attribute("testName")?.Value ?? "",
                    Assembly = def.Item1,
                    ClassName = def.Item2,
                    ExecutorUri = def.Item3,
                    Outcome = outcome,
                    Duration = TimeSpan.TryParse(e.Attribute("duration")?.Value, out var dur) ? dur : TimeSpan.Zero,
                    ErrorMessage = e.Descendants(ns + "Message").FirstOrDefault()?.Value,
                    StackTrace = e.Descendants(ns + "StackTrace").FirstOrDefault()?.Value,
                };
            })
            .ToList();

        var expectedTotal = ParseCounter(counters, "total");
        var expectedExecuted = ParseCounter(counters, "executed");
        var expectedPassed = ParseCounter(counters, "passed");
        var expectedFailed = ParseCounter(counters, "failed");
        if (expectedTotal != results.Count)
            throw new InvalidDataException(
                $"TRX counter total {expectedTotal} does not match {results.Count} results.");

        var actualNotExecuted = results.Count(result =>
            result.Outcome is "NotExecuted" or "Skipped");
        var actualPassed = results.Count(result => result.Outcome == "Passed");
        var actualFailed = results.Count(result => result.Outcome == "Failed");
        if (expectedExecuted != results.Count - actualNotExecuted)
            throw new InvalidDataException(
                $"TRX counter executed {expectedExecuted} does not match result outcomes.");
        // VSTest/xUnit emits notExecuted="0" even when concrete results contain
        // NotExecuted entries, so derive skipped counts from total - executed.
        if (expectedPassed != actualPassed || expectedFailed != actualFailed)
            throw new InvalidDataException("TRX outcome counters do not match result outcomes.");

        return results;
    }

    private static int ParseCounter(XElement counters, string name)
    {
        var value = counters.Attribute(name)?.Value;
        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidDataException($"TRX counter '{name}' is missing or invalid.");
    }

    /// <summary>
    /// Find ALL TRX files under the working directory (a solution test run produces
    /// one per test assembly — parsing only the newest silently drops results).
    /// </summary>
    public static List<string> FindTrxFiles(string workDir)
    {
        return Directory.GetFiles(workDir, "*.trx", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Parse and aggregate every TRX file under the working directory.
    /// Returns the combined results, parsed files, and any parse failures.
    /// </summary>
    public static (
        List<TestResult> Results,
        List<string> TrxFiles,
        List<string> ParseErrors) ParseAll(string workDir)
    {
        var trxFiles = FindTrxFiles(workDir);
        var results = new List<TestResult>();
        var parsed = new List<string>();
        var parseErrors = new List<string>();

        foreach (var trx in trxFiles)
        {
            try
            {
                results.AddRange(Parse(trx));
                parsed.Add(trx);
            }
            catch (Exception ex)
            {
                var error = $"failed to parse TRX file {trx}: {ex.Message}";
                Console.Error.WriteLine($"  ERROR: {error}");
                parseErrors.Add(error);
            }
        }

        return (results, parsed, parseErrors);
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
