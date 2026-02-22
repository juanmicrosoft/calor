using Calor.RoundTrip.Harness;

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
    var projectsDir = GetOption(runArgs, "--projects-dir")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "sources/repos/experimental/github-top10");
    var outputDir = GetOption(runArgs, "--output") ?? "conversion-reports";
    var dotnetPath = GetOption(runArgs, "--dotnet")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet/dotnet");
    var runAll = runArgs.Contains("--all");
    var enableBisect = runArgs.Contains("--bisect");

    // Resolve paths
    projectsDir = Path.GetFullPath(projectsDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    outputDir = Path.GetFullPath(outputDir);
    dotnetPath = Path.GetFullPath(dotnetPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    Directory.CreateDirectory(outputDir);

    // Collect project names: skip flags and their values
    var optionsWithValues = new HashSet<string> { "--projects-dir", "--output", "--dotnet" };
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
            EnableBisect = enableBisect,
            ExcludePatterns = config.ExcludePatterns,
            TestTimeout = config.TestTimeout,
            TestFilter = config.TestFilter,
        };

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"  Round-Trip Verification: {config.ProjectName}");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine();

        // First, restore the project
        Console.WriteLine("Restoring project dependencies...");
        var (restoreExit, _, restoreErr) = await ProcessRunner.RunAsync(
            config.DotnetPath,
            $"restore \"{Path.Combine(config.OriginalProjectPath, config.SolutionOrProjectFile)}\"",
            config.OriginalProjectPath,
            TimeSpan.FromMinutes(5));

        if (restoreExit != 0)
        {
            Console.Error.WriteLine($"Failed to restore {config.ProjectName}: {restoreErr}");
            anyFailure = true;
            continue;
        }

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
        Console.WriteLine($"   Report: {mdPath}");

        if (report.Comparison?.Regressions.Count > 0)
            anyFailure = true;
    }

    return anyFailure ? 1 : 0;
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
          calor-roundtrip run <project> [options]    Run round-trip for a project
          calor-roundtrip run --all [options]         Run for all known projects
          calor-roundtrip list                        List known projects

        Options:
          --projects-dir <path>    Directory containing target project clones
          --output <path>          Output directory for reports (default: conversion-reports)
          --dotnet <path>          Path to dotnet executable
          --bisect                 Enable regression bisection
        """);
}
