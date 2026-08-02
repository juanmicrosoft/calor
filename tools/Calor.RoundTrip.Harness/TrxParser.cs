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
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        // Index TestDefinitions by test id for identity joining.
        var definitions = new Dictionary<string, (string Assembly, string ClassName, string ExecutorUri)>();
        foreach (var unitTest in doc.Descendants(ns + "UnitTest"))
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

        return doc.Descendants(ns + "UnitTestResult")
            .Select(e =>
            {
                var testId = e.Attribute("testId")?.Value;
                var def = testId != null && definitions.TryGetValue(testId, out var d)
                    ? d
                    : ("", "", "");

                return new TestResult
                {
                    TestName = e.Attribute("testName")?.Value ?? "",
                    Assembly = def.Item1,
                    ClassName = def.Item2,
                    ExecutorUri = def.Item3,
                    Outcome = e.Attribute("outcome")?.Value ?? "Unknown",
                    Duration = TimeSpan.TryParse(e.Attribute("duration")?.Value, out var dur) ? dur : TimeSpan.Zero,
                    ErrorMessage = e.Descendants(ns + "Message").FirstOrDefault()?.Value,
                    StackTrace = e.Descendants(ns + "StackTrace").FirstOrDefault()?.Value,
                };
            })
            .ToList();
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
    /// Returns the combined results plus the list of files parsed.
    /// </summary>
    public static (List<TestResult> Results, List<string> TrxFiles) ParseAll(string workDir)
    {
        var trxFiles = FindTrxFiles(workDir);
        var results = new List<TestResult>();
        var parsed = new List<string>();

        foreach (var trx in trxFiles)
        {
            try
            {
                results.AddRange(Parse(trx));
                parsed.Add(trx);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  WARNING: failed to parse TRX file {trx}: {ex.Message}");
            }
        }

        return (results, parsed);
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
