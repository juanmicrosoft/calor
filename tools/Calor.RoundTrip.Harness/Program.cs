using Calor.RoundTrip.Harness;
using Calor.RoundTrip.Harness.TaskGen;

// Parse command-line arguments
// Usage:
//   calor-roundtrip run MediatR --projects-dir ~/target-projects --output ./conversion-reports/
//   calor-roundtrip run --all --projects-dir ~/target-projects --output ./conversion-reports/
//   calor-roundtrip list

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

if (cliArgs.Length == 0 || cliArgs[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

var command = cliArgs[0];

switch (command)
{
    case "run":
        return await RunCommand(cliArgs.Skip(1).ToArray());
    case "gen-tasks":
        return await GenTasksCommand(cliArgs.Skip(1).ToArray());
    case "list":
        Console.WriteLine("Known projects:");
        foreach (var p in ProjectConfigs.KnownProjects)
            Console.WriteLine($"  - {p}");
        return 0;
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
}

async Task<int> RunCommand(string[] runArgs)
{
    // Default to the vendored, SHA-pinned corpus (bench/corpus, D-W4.2). The former
    // un-pinned external default (~/sources/repos/experimental/github-top10) is gone.
    var projectsDir = GetOption(runArgs, "--projects-dir")
        ?? ProjectConfigs.DefaultCorpusDir;
    var outputDir = GetOption(runArgs, "--output") ?? "conversion-reports";
    var dotnetPath = GetOption(runArgs, "--dotnet")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet/dotnet");
    var runAll = runArgs.Contains("--all");
    var enableBisect = runArgs.Contains("--bisect");
    // Optional build-timeout override (minutes) — mainly for CI/large cold-cache runs
    // and for exercising the inconclusive guard. Falls back to the config default (15m).
    var buildTimeout = double.TryParse(GetOption(runArgs, "--build-timeout"), out var bt) && bt > 0
        ? TimeSpan.FromMinutes(bt)
        : (TimeSpan?)null;

    // Resolve paths
    projectsDir = Path.GetFullPath(projectsDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    outputDir = Path.GetFullPath(outputDir);
    // A bare `dotnet` (the documented/CI form) must stay bare so it resolves via PATH;
    // only expand a real path (with a separator or leading ~).
    if (dotnetPath.Contains('/') || dotnetPath.Contains('\\'))
        dotnetPath = Path.GetFullPath(dotnetPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    Directory.CreateDirectory(outputDir);

    // Collect project names: skip flags and their values
    var optionsWithValues = new HashSet<string> { "--projects-dir", "--output", "--dotnet", "--build-timeout" };
    var projectNames = new List<string>();
    if (runAll)
    {
        projectNames = ProjectConfigs.KnownProjects.ToList();
    }
    else
    {
        for (int i = 0; i < runArgs.Length; i++)
        {
            if (runArgs[i].StartsWith("--"))
            {
                if (optionsWithValues.Contains(runArgs[i]) && i + 1 < runArgs.Length)
                    i++; // skip the value
                continue;
            }
            projectNames.Add(runArgs[i]);
        }
    }

    if (projectNames.Count == 0)
    {
        Console.Error.WriteLine("No projects specified. Use --all or provide project names.");
        return 1;
    }

    var pipeline = new RoundTripPipeline();
    var anyFailure = false;

    foreach (var projectName in projectNames)
    {
        var config = ProjectConfigs.Get(projectName, projectsDir, dotnetPath);
        if (config == null)
        {
            Console.Error.WriteLine($"Unknown project: {projectName}. Use 'list' to see known projects.");
            continue;
        }

        // Set bisect on the config (EnableBisect has init accessor from Get())
        // Create a new config manually
        config = new RoundTripConfig
        {
            ProjectName = config.ProjectName,
            OriginalProjectPath = config.OriginalProjectPath,
            LibrarySourceRelativePath = config.LibrarySourceRelativePath,
            SolutionOrProjectFile = config.SolutionOrProjectFile,
            DotnetPath = config.DotnetPath,
            TargetFramework = config.TargetFramework,
            ExtraBuildProperties = config.ExtraBuildProperties,
            EnableBisect = enableBisect,
            ExcludePatterns = config.ExcludePatterns,
            TestTimeout = config.TestTimeout,
            BuildTimeout = buildTimeout ?? config.BuildTimeout,
            TestFilter = config.TestFilter,
        };

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"  Round-Trip Verification: {config.ProjectName}");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        // Note: the pipeline restores the working copy itself (RestoreProjectAsync).
        // We do NOT pre-restore OriginalProjectPath here — a vendored corpus project
        // may pin its own SDK via global.json (e.g. FluentValidation pins 9.0.0),
        // which would make `dotnet` refuse to start in that directory. The working
        // copy neutralizes corpus global.json so every subject builds on Calor's
        // pinned .NET 10 SDK.
        var report = await pipeline.RunAsync(config);

        // Write reports
        var mdPath = Path.Combine(outputDir, $"{config.ProjectName}-roundtrip.md");
        var jsonPath = Path.Combine(outputDir, $"{config.ProjectName}-roundtrip.json");
        await File.WriteAllTextAsync(mdPath, ReportGenerator.GenerateMarkdown(report));
        await File.WriteAllTextAsync(jsonPath, ReportGenerator.GenerateJson(report));

        // Print summary
        var verdictEmoji = report.Comparison?.Status switch
        {
            ComparisonStatus.Pass => "PASS",
            ComparisonStatus.MinorRegressions => "WARN",
            ComparisonStatus.MajorRegressions => "FAIL",
            ComparisonStatus.BuildFailed => "FAIL",
            _ => "???",
        };

        Console.WriteLine($"\n{verdictEmoji} {config.ProjectName}: {report.Comparison?.Status}");
        Console.WriteLine($"   Baseline: {report.Baseline?.Passed}/{report.Baseline?.TotalTests} passing");
        Console.WriteLine($"   Round-trip: {report.RoundTripTests?.Passed ?? 0}/{report.RoundTripTests?.TotalTests ?? 0} passing");
        Console.WriteLine($"   Regressions: {report.Comparison?.Regressions.Count ?? -1}");
        Console.WriteLine($"   Files converted: {report.FileResults.Count(f => f.Status == FileStatus.Replaced)}/{report.FileResults.Count}");
        if (report.Inconclusive)
        {
            Console.WriteLine($"   Coverage: INCONCLUSIVE — {report.InconclusiveReason}");
        }
        else if (report.Fidelity != null)
        {
            var cov = report.Fidelity.Coverage;
            Console.WriteLine($"   Coverage: {cov.CoverageFraction:P1} ({cov.ConvertedNative} native, {cov.ConvertedWithLosses} with-losses, {cov.Reverted} reverted, {cov.FailedConversion} failed of {cov.TotalConvertibleFiles})");
            Console.WriteLine($"   Interop blocks: {cov.TotalInteropBlocks}; distinct gaps: {cov.DistinctGaps.Count}");
        }
        Console.WriteLine($"   Report: {mdPath}");

        if (report.Comparison?.Regressions.Count > 0)
            anyFailure = true;
    }

    return anyFailure ? 1 : 0;
}

async Task<int> GenTasksCommand(string[] genArgs)
{
    // WS-W4 Slice C: mutate-then-convert task generation + D-W4.1 eligibility predicate.
    var projectsDir = GetOption(genArgs, "--projects-dir") ?? ProjectConfigs.DefaultCorpusDir;
    var outputDir = GetOption(genArgs, "--output") ?? "task-bundles";
    var dotnetPath = GetOption(genArgs, "--dotnet")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet/dotnet");

    projectsDir = Path.GetFullPath(projectsDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    outputDir = Path.GetFullPath(outputDir);
    if (dotnetPath.Contains('/') || dotnetPath.Contains('\\'))
        dotnetPath = Path.GetFullPath(dotnetPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    if (!File.Exists(dotnetPath) && dotnetPath.Contains('/'))
        dotnetPath = "dotnet"; // fall back to PATH when the ~/.dotnet default is absent

    // Configurable fidelity bar (D-W4.3) — NOT frozen; defaults to provisional 0.70.
    var nativeBar = double.TryParse(GetOption(genArgs, "--native-bar"), out var nb) ? nb : 0.70;
    var maxCandidates = int.TryParse(GetOption(genArgs, "--max-candidates"), out var mc) ? mc : 8;
    var target = int.TryParse(GetOption(genArgs, "--target"), out var tg) ? tg : 3;

    var optionsWithValues = new HashSet<string>
        { "--projects-dir", "--output", "--dotnet", "--native-bar", "--max-candidates", "--target" };
    var projectNames = new List<string>();
    if (genArgs.Contains("--synthetic"))
        projectNames = ProjectConfigs.SyntheticProjects.ToList();
    else if (genArgs.Contains("--all"))
        projectNames = ProjectConfigs.KnownProjects.ToList();
    else
    {
        for (int i = 0; i < genArgs.Length; i++)
        {
            if (genArgs[i].StartsWith("--"))
            {
                if (optionsWithValues.Contains(genArgs[i]) && i + 1 < genArgs.Length) i++;
                continue;
            }
            projectNames.Add(genArgs[i]);
        }
    }
    if (projectNames.Count == 0)
    {
        Console.Error.WriteLine("No projects specified. Use --synthetic, --all, or provide project names.");
        return 1;
    }

    var configs = new List<RoundTripConfig>();
    foreach (var name in projectNames)
    {
        var config = ProjectConfigs.Get(name, projectsDir, dotnetPath);
        if (config == null)
        {
            Console.Error.WriteLine($"Unknown project: {name}. Use 'list' to see known projects.");
            continue;
        }
        configs.Add(config);
    }
    if (configs.Count == 0) return 1;

    var options = new TaskGenOptions
    {
        OutputDir = outputDir,
        MaxCandidatesPerProject = maxCandidates,
        TargetEligiblePerProject = target,
        Fidelity = new FidelityGateConfig { NativeFractionBar = nativeBar, BarIsProvisional = true },
    };

    var run = await TaskGenRunner.RunAsync(configs, options);
    return run.TotalEligible > 0 ? 0 : 1;
}

static string? GetOption(string[] args, string flag)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == flag)
            return args[i + 1];
    }
    return null;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Calor Round-Trip Verification Harness

        Usage:
          calor-roundtrip run <project> [options]        Run round-trip for a project
          calor-roundtrip run --all [options]             Run for all known projects
          calor-roundtrip gen-tasks <project...> [options] Generate real-scale task bundles (WS-W4 Slice C)
          calor-roundtrip gen-tasks --synthetic [options]  Generate against the in-repo synthetic subjects
          calor-roundtrip list                            List known projects

        Options (run):
          --projects-dir <path>    Directory containing target project clones
          --output <path>          Output directory for reports (default: conversion-reports)
          --dotnet <path>          Path to dotnet executable (or a bare 'dotnet' on PATH)
          --build-timeout <min>    Per-build timeout in minutes (default 15)
          --bisect                 Enable regression bisection

        Options (gen-tasks):
          --output <path>          Output directory for task bundles (default: task-bundles)
          --native-bar <frac>      Fidelity-gate NativeFraction bar (default provisional 0.70)
          --max-candidates <n>     Max sited candidates considered per project (default 8)
          --target <n>             Stop after this many eligible bundles per project (default 3)
        """);
}
