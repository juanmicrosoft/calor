using System.CommandLine;
using Calor.Compiler.SelfTest;

namespace Calor.Compiler.Commands;

public static class SelfTestCommand
{
    public static Command Create()
    {
        var scenarioOption = new Option<string?>("--scenario", "-s")
        {
            Description = "Run only a specific scenario by name (e.g., '01_hello_world')"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed output including diffs for failures"
        };

        var command = new Command("self-test",
            "Verify compiler correctness by compiling embedded reference files and diffing output against golden files")
        {
            scenarioOption,
            verboseOption
        };

        command.SetAction(parseResult =>
        {
            return Execute(
                parseResult.GetValue(scenarioOption),
                parseResult.GetValue(verboseOption));
        });
        return command;
    }

    private static int Execute(string? scenarioFilter, bool verbose)
    {
        var scenarios = SelfTestRunner.LoadScenarios();

        if (scenarioFilter != null)
        {
            scenarios = scenarios
                .Where(s => s.Name.Equals(scenarioFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (scenarios.Count == 0)
            {
                var available = string.Join(", ", SelfTestRunner.LoadScenarios().Select(s => s.Name));
                Console.Error.WriteLine($"Unknown scenario: '{scenarioFilter}'");
                Console.Error.WriteLine($"Available scenarios: {available}");
                return 1;
            }
        }

        Console.WriteLine($"calor self-test — {scenarios.Count} scenario{(scenarios.Count != 1 ? "s" : "")}");
        Console.WriteLine();

        var results = scenarios.Select(SelfTestRunner.Run).ToList();
        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);

        foreach (var result in results)
        {
            if (result.Passed)
            {
                Console.WriteLine($"  \u2713 {result.ScenarioName}");
            }
            else
            {
                Console.WriteLine($"  \u2717 {result.ScenarioName}");

                if (result.Error != null)
                {
                    Console.WriteLine($"    Error: {result.Error}");
                }

                if (verbose && result.Diff != null)
                {
                    foreach (var line in result.Diff.Split(Environment.NewLine))
                    {
                        Console.WriteLine($"    {line}");
                    }
                }
                else if (result.Diff != null)
                {
                    var diffLines = result.Diff.Split(Environment.NewLine);
                    var changedCount = diffLines.Count(l => l.StartsWith("+") || l.StartsWith("-"));
                    // Subtract the --- / +++ header lines from count
                    changedCount = Math.Max(0, changedCount - 2);
                    Console.WriteLine($"    {changedCount} line(s) differ (use --verbose to see diff)");
                }
            }
        }

        Console.WriteLine();
        if (failed == 0)
        {
            Console.WriteLine($"Results: {passed}/{results.Count} passed");
        }
        else
        {
            Console.WriteLine($"Results: {passed}/{results.Count} passed, {failed} failed");
        }
        return failed == 0 ? 0 : 1;
    }
}
