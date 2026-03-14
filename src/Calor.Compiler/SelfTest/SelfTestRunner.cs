using System.Reflection;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
namespace Calor.Compiler.SelfTest;

public sealed class SelfTestRunner
{
    public record SelfTestScenario(string Name, string Input, string ExpectedOutput);

    public record SelfTestResult(
        string ScenarioName,
        bool Passed,
        string? ActualOutput,
        string? Diff,
        string? Error);

    private static readonly Assembly Assembly = typeof(SelfTestRunner).Assembly;

    /// <summary>
    /// Load all scenarios from embedded resources.
    /// </summary>
    public static List<SelfTestScenario> LoadScenarios()
    {
        const string prefix = "Calor.Compiler.Resources.SelfTest.";
        const string calrSuffix = ".calr";

        var resourceNames = Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(calrSuffix, StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var scenarios = new List<SelfTestScenario>();

        foreach (var calrResource in resourceNames)
        {
            // Extract scenario name: "Calor.Compiler.Resources.SelfTest.01_hello_world.calr" → "01_hello_world"
            var name = calrResource[prefix.Length..^calrSuffix.Length];

            var goldenResource = $"{prefix}{name}.g.cs";

            var input = ReadResource(calrResource);
            var expectedOutput = ReadResource(goldenResource);

            scenarios.Add(new SelfTestScenario(name, input, expectedOutput));
        }

        return scenarios;
    }

    /// <summary>
    /// Run all scenarios, return results.
    /// </summary>
    public static List<SelfTestResult> RunAll()
    {
        var scenarios = LoadScenarios();
        return scenarios.Select(Run).ToList();
    }

    /// <summary>
    /// Run a single scenario.
    /// </summary>
    public static SelfTestResult Run(SelfTestScenario scenario)
    {
        try
        {
            var options = new CompilationOptions
            {
                EnforceEffects = true,
                ContractMode = ContractMode.Debug,
                VerifyContracts = false
            };

            // Normalize input line endings so span offsets are consistent across platforms
            var normalizedInput = scenario.Input.Replace("\r\n", "\n");
            var result = Program.Compile(normalizedInput, scenario.Name + ".calr", options);

            if (result.HasErrors)
            {
                var errors = string.Join(Environment.NewLine,
                    result.Diagnostics.Where(d => d.IsError).Select(d => $"  {d.Code}: {d.Message}"));
                return new SelfTestResult(scenario.Name, false, null, null,
                    $"Compilation failed:{Environment.NewLine}{errors}");
            }

            var actual = NormalizeLineEndings(result.GeneratedCode);
            var expected = NormalizeLineEndings(scenario.ExpectedOutput);

            if (actual == expected)
            {
                return new SelfTestResult(scenario.Name, true, actual, null, null);
            }

            var diff = GenerateDiff(expected, actual);
            return new SelfTestResult(scenario.Name, false, actual, diff, null);
        }
        catch (Exception ex)
        {
            return new SelfTestResult(scenario.Name, false, null, null,
                $"Exception: {ex.Message}");
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            var available = string.Join(", ", Assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded resource '{name}' not found. Available: {available}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").TrimEnd('\n', ' ');
    }

    internal static string GenerateDiff(string expected, string actual, int contextLines = 3)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(expected, actual);
        var allLines = diff.Lines.ToList();

        // Find which lines are changed (inserted, deleted, modified)
        var changedIndices = new HashSet<int>();
        for (int i = 0; i < allLines.Count; i++)
        {
            if (allLines[i].Type != ChangeType.Unchanged && allLines[i].Type != ChangeType.Imaginary)
                changedIndices.Add(i);
        }

        if (changedIndices.Count == 0)
            return string.Empty;

        // Determine which lines to show (changed + context)
        var visibleIndices = new HashSet<int>();
        foreach (var idx in changedIndices)
        {
            for (int c = Math.Max(0, idx - contextLines); c <= Math.Min(allLines.Count - 1, idx + contextLines); c++)
                visibleIndices.Add(c);
        }

        // Build hunks: contiguous ranges of visible lines
        var sortedVisible = visibleIndices.OrderBy(i => i).ToList();
        var output = new List<string>();
        output.Add("--- expected");
        output.Add("+++ actual");

        int hunkStart = 0;
        while (hunkStart < sortedVisible.Count)
        {
            // Find contiguous range
            int hunkEnd = hunkStart;
            while (hunkEnd + 1 < sortedVisible.Count && sortedVisible[hunkEnd + 1] == sortedVisible[hunkEnd] + 1)
                hunkEnd++;

            int startIdx = sortedVisible[hunkStart];
            int endIdx = sortedVisible[hunkEnd];

            // Count expected/actual line numbers for hunk header
            int expectedLine = 1, actualLine = 1;
            for (int i = 0; i < startIdx; i++)
            {
                if (allLines[i].Type != ChangeType.Inserted)
                    expectedLine++;
                if (allLines[i].Type != ChangeType.Deleted)
                    actualLine++;
            }

            int expectedCount = 0, actualCount = 0;
            for (int i = startIdx; i <= endIdx; i++)
            {
                if (allLines[i].Type != ChangeType.Inserted)
                    expectedCount++;
                if (allLines[i].Type != ChangeType.Deleted)
                    actualCount++;
            }

            output.Add($"@@ -{expectedLine},{expectedCount} +{actualLine},{actualCount} @@");

            for (int i = startIdx; i <= endIdx; i++)
            {
                var line = allLines[i];
                var prefix = line.Type switch
                {
                    ChangeType.Inserted => "+",
                    ChangeType.Deleted => "-",
                    ChangeType.Modified => "~",
                    _ => " "
                };
                output.Add($"{prefix}{line.Text}");
            }

            hunkStart = hunkEnd + 1;
        }

        return string.Join(Environment.NewLine, output);
    }
}
