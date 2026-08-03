namespace Calor.RoundTrip.Harness.TaskGen;

/// <summary>
/// Drives task generation across a set of projects, applies the D-W4.3 fidelity gate, and writes
/// the eligible bundles + the exclusion-accounting report to disk.
/// </summary>
public static class TaskGenRunner
{
    public static async Task<TaskGenRunResult> RunAsync(IReadOnlyList<RoundTripConfig> projects, TaskGenOptions options)
    {
        Directory.CreateDirectory(options.OutputDir);
        var generator = new TaskGenerator();
        var projectResults = new List<TaskGenProjectResult>();

        foreach (var project in projects)
        {
            Console.WriteLine();
            Console.WriteLine(new string('=', 64));
            Console.WriteLine($"  Task generation: {project.ProjectName}");
            Console.WriteLine(new string('=', 64));
            var result = await generator.GenerateForProjectAsync(project, options);
            projectResults.Add(result);
        }

        var gate = FidelityGate.Evaluate(projectResults.Select(p => p.ToFidelityInput()), options.Fidelity);
        var run = new TaskGenRunResult { Projects = projectResults, FidelityGate = gate };

        var reportPath = Path.Combine(options.OutputDir, "task-generation-report.md");
        await File.WriteAllTextAsync(reportPath, TaskGenReportWriter.RunReport(run));
        var jsonPath = Path.Combine(options.OutputDir, "task-generation-report.json");
        await File.WriteAllTextAsync(jsonPath, TaskGenReportWriter.Serialize(new
        {
            run.TotalConsidered,
            run.TotalEligible,
            run.ProjectsWithEligibleTasks,
            run.MeetsDefinitionOfDone,
            FidelityBar = gate.Config.BarLabel,
            gate.Signal,
            Projects = run.Projects.Select(p => new
            {
                p.ProjectName,
                p.NativeFraction,
                p.Inconclusive,
                p.NativeSourceFiles,
                p.TotalConvertibleFiles,
                p.TotalEnumeratedCandidates,
                p.BaselineTestsPassed,
                Considered = p.Accounting.Considered,
                Eligible = p.Accounting.Eligible,
                ExcludedClauseA = p.Accounting.ExcludedClauseA,
                ExcludedClauseB = p.Accounting.ExcludedClauseB,
                ExcludedAttribution = p.Accounting.ExcludedAttribution,
                ExcludedNoCoveringTest = p.Accounting.ExcludedNoCoveringTest,
                ExcludedDidNotCompile = p.Accounting.ExcludedDidNotCompile,
                ExcludedHeldOutFilterLeak = p.Accounting.ExcludedHeldOutFilterLeak,
                EligibilityRate = p.Accounting.EligibilityRate,
                Bundles = p.Bundles.Select(b => b.TaskId).ToList(),
                Dispositions = p.Accounting.Dispositions,
            }),
        }));

        Console.WriteLine();
        Console.WriteLine(new string('=', 64));
        Console.WriteLine($"  Eligible bundles: {run.TotalEligible} across {run.ProjectsWithEligibleTasks} project(s)");
        Console.WriteLine($"  {gate.Signal}");
        Console.WriteLine($"  DoD met (≥3 eligible across ≥2 fidelity-passing projects): {run.MeetsDefinitionOfDone}");
        Console.WriteLine($"  Report: {reportPath}");
        Console.WriteLine(new string('=', 64));
        return run;
    }
}
