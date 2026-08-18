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
    case "mine":
        return MineCommand(cliArgs.Skip(1).ToArray());
    case "enumerate-supply":
        return await EnumerateSupplyCommand(cliArgs.Skip(1).ToArray());
    case "screen-determinism":
        return await ScreenDeterminismCommand(cliArgs.Skip(1).ToArray());
    case "regen-bundle-readmes":
        return await RegenBundleReadmesCommand(cliArgs.Skip(1).ToArray());
    case "failure-census":
        return await FailureCensusCommand(cliArgs.Skip(1).ToArray());
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
    var minimumCoverage = ParseFractionOption(runArgs, "--min-coverage");
    var minimumNative = ParseFractionOption(runArgs, "--min-native");

    // Resolve paths
    projectsDir = Path.GetFullPath(projectsDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    outputDir = Path.GetFullPath(outputDir);
    // A bare `dotnet` (the documented/CI form) must stay bare so it resolves via PATH;
    // only expand a real path (with a separator or leading ~).
    if (dotnetPath.Contains('/') || dotnetPath.Contains('\\'))
        dotnetPath = Path.GetFullPath(dotnetPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    Directory.CreateDirectory(outputDir);

    // Collect project names: skip flags and their values
    var optionsWithValues = new HashSet<string>
    {
        "--projects-dir", "--output", "--dotnet", "--build-timeout",
        "--min-coverage", "--min-native"
    };
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
            anyFailure = true;
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
            ParseContextProjectFile = config.ParseContextProjectFile,
            DotnetPath = config.DotnetPath,
            TargetFramework = config.TargetFramework,
            Configuration = config.Configuration,
            ExtraBuildProperties = config.ExtraBuildProperties,
            LooseDirectoryMode = config.LooseDirectoryMode,
            EnableBisect = enableBisect,
            ExcludePatterns = config.ExcludePatterns,
            TestTimeout = config.TestTimeout,
            BuildTimeout = buildTimeout ?? config.BuildTimeout,
            ConversionTimeout = config.ConversionTimeout,
            TestFilter = config.TestFilter,
            MinimumCoverageFraction = minimumCoverage ?? config.MinimumCoverageFraction,
            MinimumNativeFraction = minimumNative ?? config.MinimumNativeFraction,
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
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        RoundTripReport report;
        try
        {
            report = await pipeline.RunAsync(config, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        // Write reports
        var mdPath = Path.Combine(outputDir, $"{config.ProjectName}-roundtrip.md");
        var jsonPath = Path.Combine(outputDir, $"{config.ProjectName}-roundtrip.json");
        await File.WriteAllTextAsync(mdPath, ReportGenerator.GenerateMarkdown(report));
        await File.WriteAllTextAsync(jsonPath, ReportGenerator.GenerateJson(report));

        // Print summary
        var gateFailures = RoundTripExitPolicy.GetFailureReasons(report);
        var verdictEmoji = gateFailures.Count == 0 ? "PASS" : "FAIL";

        Console.WriteLine($"\n{verdictEmoji} {config.ProjectName}: {report.Comparison?.Status}");
        Console.WriteLine($"   Baseline: {report.Baseline?.Passed}/{report.Baseline?.TotalTests} passing");
        Console.WriteLine($"   Round-trip: {report.RoundTripTests?.Passed ?? 0}/{report.RoundTripTests?.TotalTests ?? 0} passing");
        Console.WriteLine($"   Regressions: {report.Comparison?.Regressions.Count ?? -1}");
        var sourceCandidateCount = report.Fidelity?.Coverage.TotalConvertibleFiles
            ?? report.FileResults.Count + report.ExcludedFileCount;
        Console.WriteLine(
            $"   Files converted: {report.FileResults.Count(f => f.Status == FileStatus.Replaced)}/{sourceCandidateCount}");
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
        foreach (var reason in gateFailures)
            Console.WriteLine($"   BLOCKED: {reason}");

        if (gateFailures.Count > 0)
            anyFailure = true;
    }

    return anyFailure ? 1 : 0;
}

double? ParseFractionOption(string[] args, string name)
{
    var value = GetOption(args, name);
    if (value == null)
        return null;
    if (!double.TryParse(value, out var parsed) || parsed < 0 || parsed > 1)
        throw new ArgumentException($"{name} must be a number between 0 and 1.");
    return parsed;
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
    var scanCommits = int.TryParse(GetOption(genArgs, "--scan-commits"), out var sc) ? sc : 2000;

    // Defect source: injected mutations (supplement), revert-upstream-bugfix (gold standard), or both.
    var sourceArg = (GetOption(genArgs, "--source") ?? "injected").ToLowerInvariant();
    var sources = sourceArg switch
    {
        "revert" => TaskSourceSelection.Revert,
        "both" => TaskSourceSelection.Both,
        "injected" => TaskSourceSelection.Injected,
        _ => (TaskSourceSelection?)null,
    };
    if (sources == null)
    {
        Console.Error.WriteLine($"Unknown --source '{sourceArg}'. Use one of: injected, revert, both.");
        return 1;
    }

    // Defect stratum: logic (conversion-penalty / PP-A2), expressible (verification-addressable), or both.
    var stratumArg = (GetOption(genArgs, "--stratum") ?? "both").ToLowerInvariant();
    var strata = stratumArg switch
    {
        "logic" => StratumSelection.Logic,
        "expressible" => StratumSelection.Expressible,
        "both" => StratumSelection.Both,
        _ => (StratumSelection?)null,
    };
    if (strata == null)
    {
        Console.Error.WriteLine($"Unknown --stratum '{stratumArg}'. Use one of: logic, expressible, both.");
        return 1;
    }

    var optionsWithValues = new HashSet<string>
        { "--projects-dir", "--output", "--dotnet", "--native-bar", "--max-candidates", "--target", "--source", "--stratum", "--scan-commits" };
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
        Sources = sources.Value,
        Strata = strata.Value,
        RevertScanCommits = scanCommits,
        Fidelity = new FidelityGateConfig { NativeFractionBar = nativeBar, BarIsProvisional = true },
    };
    Console.WriteLine($"Defect stratum/strata: {strata.Value}");
    Console.WriteLine($"Defect source(s): {sources.Value}" +
        (sources.Value.HasFlag(TaskSourceSelection.Revert) ? $" (scanning {scanCommits} commits of upstream history)" : ""));

    var run = await TaskGenRunner.RunAsync(configs, options);
    return run.TotalEligible > 0 ? 0 : 1;
}

// WS-S1 failure-cause census over conversion reports. Decides the pre-committed replacement gate
// (gates A-1.6(b) / substrate plan §10): top-3 causes >= 50% -> continue WS-S1; otherwise PP-S1 = miss.
async Task<int> FailureCensusCommand(string[] args)
{
    var reportsDir = GetOption(args, "--reports");
    if (reportsDir == null) { Console.Error.WriteLine("--reports <dir> (containing *-roundtrip.json) is required."); return 2; }
    reportsDir = Path.GetFullPath(reportsDir);
    var outDir = Path.GetFullPath(GetOption(args, "--output") ?? reportsDir);

    var files = Directory.GetFiles(reportsDir, "*-roundtrip.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
    if (files.Count == 0) { Console.Error.WriteLine($"No *-roundtrip.json under {reportsDir}."); return 2; }

    var failures = new List<(string Status, string Path, IReadOnlyList<string> Errors)>();
    var perProject = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var f in files)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(f));
        var root = doc.RootElement;
        var project = root.TryGetProperty("project", out var p) ? p.GetString() ?? "?" : "?";
        if (!root.TryGetProperty("file_detail", out var detail)) continue;

        foreach (var fd in detail.EnumerateArray())
        {
            var status = fd.GetProperty("status").GetString() ?? "?";
            // The attempted-file cause census excludes configured source exclusions;
            // coverage reports disclose them separately as denominator failures.
            if (status is "Replaced" or "Excluded") continue;

            var path = fd.GetProperty("path").GetString() ?? "?";
            var errors = fd.TryGetProperty("errors", out var errs) && errs.ValueKind == System.Text.Json.JsonValueKind.Array
                ? errs.EnumerateArray().Select(e => e.GetString() ?? "").Where(e => e.Length > 0).ToList()
                : [];
            failures.Add((status, $"{project}/{path}", errors));
            perProject[project] = perProject.GetValueOrDefault(project) + 1;
        }
    }

    var result = FailureCensus.Analyse(failures);
    var md = FailureCensusReport.Render(result, perProject);
    Directory.CreateDirectory(outDir);
    await File.WriteAllTextAsync(Path.Combine(outDir, "failure-census.md"), md);
    await File.WriteAllTextAsync(Path.Combine(outDir, "failure-census.json"),
        System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine();
    Console.WriteLine(md);
    return 0;
}

// Regenerate each bundle's README from its provenance.json THROUGH THE GENERATOR. provenance.json is
// a serialized TaskBundle, so this is deserialize + BundleReadme — the only way to guarantee a shipped
// artifact matches the template. Hand-patching them (as was tried once) leaves the retracted sentences
// in place while the inserted correction contradicts them.
async Task<int> RegenBundleReadmesCommand(string[] args)
{
    var epochDir = GetOption(args, "--epoch");
    if (epochDir == null) { Console.Error.WriteLine("--epoch <dir> is required."); return 2; }
    var bundlesDir = Path.Combine(Path.GetFullPath(epochDir), "bundles");
    if (!Directory.Exists(bundlesDir)) { Console.Error.WriteLine($"No bundles/ under {epochDir}."); return 2; }

    var opts = new System.Text.Json.JsonSerializerOptions
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    var n = 0; var failed = 0;
    foreach (var bundle in Directory.GetDirectories(bundlesDir).OrderBy(d => d, StringComparer.Ordinal))
    {
        var prov = Path.Combine(bundle, "provenance.json");
        if (!File.Exists(prov)) continue;
        try
        {
            var b = System.Text.Json.JsonSerializer.Deserialize<TaskBundle>(await File.ReadAllTextAsync(prov), opts);
            if (b == null) { Console.Error.WriteLine($"  {Path.GetFileName(bundle)}: deserialized to null"); failed++; continue; }
            await File.WriteAllTextAsync(Path.Combine(bundle, "README.md"), TaskGenReportWriter.BundleReadme(b));
            n++;
        }
        catch (Exception ex) { Console.Error.WriteLine($"  {Path.GetFileName(bundle)}: {ex.GetType().Name}: {ex.Message}"); failed++; }
    }
    Console.WriteLine($"Regenerated {n} bundle README(s); {failed} failed.");
    return failed == 0 && n > 0 ? 0 : 1;
}

// D-S5.1 determinism screen: gates §0.2's 5-consecutive-green rule over an epoch's ELIGIBLE tasks.
// M-S3 counts only screened tasks, so this is the gate between "the generator produced N eligible"
// and "the epoch has N tasks".
async Task<int> ScreenDeterminismCommand(string[] args)
{
    var epochDir = GetOption(args, "--epoch");
    if (epochDir == null) { Console.Error.WriteLine("--epoch <dir> is required."); return 2; }
    epochDir = Path.GetFullPath(epochDir);
    if (!File.Exists(Path.Combine(epochDir, "task-generation-report.json")))
    { Console.Error.WriteLine($"No task-generation-report.json under {epochDir}."); return 2; }

    var projectsDir = GetOption(args, "--projects-dir") ?? ProjectConfigs.DefaultCorpusDir;
    projectsDir = Path.GetFullPath(projectsDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    var dotnet = GetOption(args, "--dotnet") ?? "dotnet";

    var bundlesDir = Path.Combine(epochDir, "bundles");
    if (!Directory.Exists(bundlesDir)) { Console.Error.WriteLine($"No bundles/ under {epochDir}."); return 2; }

    var pipeline = new RoundTripPipeline();
    var workRoot = Path.Combine(Path.GetTempPath(), "calor-screen-" + Guid.NewGuid().ToString("N")[..8]);
    var screens = new List<DeterminismScreen.TaskScreen>();
    // Per project: the C# reference (pristine) and the Calor reference (converted, UNMUTATED).
    var csRef = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    var calorRef = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    async Task<DeterminismScreen.RunRecord?> RunOnce(string dir, RoundTripConfig cfg)
    {
        // Every other RunTestsAsync caller cleans TRX first; without it a run that could not execute
        // (testhost crash, timeout) is scored from the PREVIOUS run's results on disk — i.e. "5 runs"
        // silently becomes one run counted five times, in the one instrument whose job is to detect
        // exactly that.
        TrxParser.CleanTrxFiles(dir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = await pipeline.RunTestsAsync(dir, cfg, noBuild: true);
        sw.Stop();
        if (run.ExitCode < 0 || run.TotalTests == 0) return null;   // timeout / nothing matched → inconclusive
        return new DeterminismScreen.RunRecord
        {
            TotalTests = run.TotalTests, Passed = run.Passed, Failed = run.Failed, DurationMs = sw.ElapsedMilliseconds,
        };
    }

    async Task<(List<DeterminismScreen.RunRecord> Runs, string? Bad)> FiveRuns(string label, string dir, RoundTripConfig cfg)
    {
        var runs = new List<DeterminismScreen.RunRecord>();
        for (var i = 0; i < DeterminismScreen.RequiredConsecutiveGreenRuns; i++)
        {
            var r = await RunOnce(dir, cfg);
            if (r == null) return (runs, $"{label} run {i + 1} did not execute (timeout, or the filter matched no tests)");
            runs.Add(r);
            Console.WriteLine($"    {label} run {i + 1}: {(r.Green ? "green" : $"RED ({r.Failed} failed)")} [{r.TotalTests} tests, {r.DurationMs} ms]");
        }
        return (runs, null);
    }

    try
    {
        foreach (var bundle in Directory.GetDirectories(bundlesDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(bundle);
            var provPath = Path.Combine(bundle, "provenance.json");
            // Every bundle directory produces a row. Silently skipping one would compute "N of N pass"
            // over a self-selected population — the arithmetic a supply gate must never permit.
            if (!File.Exists(provPath)) { screens.Add(Blank(name, "?", "", "no provenance.json")); continue; }

            string taskId = name, projectName = "?";
            string filter = "";
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(provPath));
                var root = doc.RootElement;
                taskId = root.GetProperty("TaskId").GetString() ?? name;
                projectName = root.GetProperty("ProjectName").GetString() ?? "?";
                var heldOut = root.GetProperty("HeldOut").EnumerateArray().Select(h => new HeldOutTest
                {
                    TestName = h.GetProperty("TestName").GetString() ?? "",
                    ClassName = h.GetProperty("ClassName").GetString() ?? "",
                    FilterName = h.TryGetProperty("FilterName", out var fn) ? fn.GetString() ?? "" : "",
                }).ToList();
                // The canonical builder — it escapes filter metacharacters, so the screen cannot
                // silently select a different set than the epoch graded.
                filter = HeldOutExtraction.BuildHeldOutFilter(heldOut);
            }
            catch (Exception ex) { screens.Add(Blank(name, projectName, "", $"unreadable provenance: {ex.GetType().Name}")); continue; }

            // An empty filter becomes `--filter ""`, which runs the ENTIRE suite — green on a pristine
            // reference, so the task would pass on evidence unrelated to its held-out set.
            if (string.IsNullOrWhiteSpace(filter)) { screens.Add(Blank(taskId, projectName, "", "no held-out filter")); continue; }

            var cfg = ProjectConfigs.Get(projectName, projectsDir, dotnet);
            if (cfg == null || !Directory.Exists(cfg.OriginalProjectPath))
            { screens.Add(Blank(taskId, projectName, filter, $"project '{projectName}' unavailable")); continue; }

            Console.WriteLine($"[{taskId}] screening both arms, {DeterminismScreen.RequiredConsecutiveGreenRuns} runs each…");

            // --- C# arm reference: the pristine corpus, prepared once per project. ---
            if (!csRef.TryGetValue(projectName, out var csDir))
            {
                var dir = pipeline.PrepareWorkingCopy(CloneForScreen(cfg, Path.Combine(workRoot, projectName + "-cs-ref"), null));
                csDir = (await pipeline.BuildProjectAsync(dir, cfg)).Succeeded ? dir : null;
                csRef[projectName] = csDir;
            }
            if (csDir == null) { screens.Add(Blank(taskId, projectName, filter, "pristine C# reference did not build")); continue; }

            // --- Calor arm reference: the CONVERTED, UNMUTATED program, prepared once per project. ---
            if (!calorRef.TryGetValue(projectName, out var calDir))
            {
                var dir = pipeline.PrepareWorkingCopy(CloneForScreen(cfg, Path.Combine(workRoot, projectName + "-calor-ref"), null));
                var report = new RoundTripReport { ProjectName = projectName };
                var results = await pipeline.ConvertAndReplaceAsync(dir, cfg, report);
                var built = await pipeline.BuildProjectAsync(dir, cfg);
                if (!built.Succeeded) { await pipeline.RecoverBuildAsync(dir, cfg, results); built = await pipeline.BuildProjectAsync(dir, cfg); }
                calDir = built.Succeeded ? dir : null;
                calorRef[projectName] = calDir;
            }
            if (calDir == null) { screens.Add(Blank(taskId, projectName, filter, "converted-unmutated Calor reference did not build")); continue; }

            var (csRuns, csBad) = await FiveRuns("C# reference", csDir, CloneForScreen(cfg, csDir, filter));
            var (calRuns, calBad) = await FiveRuns("Calor reference", calDir, CloneForScreen(cfg, calDir, filter));

            // --- Mutated arm (reported only). Absence is recorded as absence, never as intermittency. ---
            var mutRuns = new List<DeterminismScreen.RunRecord>();
            var armStatus = DeterminismScreen.ArmStatus.NotAttempted;
            var armDir = Path.Combine(bundle, "csharp-arm");
            if (csBad == null && calBad == null)
            {
                if (!Directory.Exists(armDir)) armStatus = DeterminismScreen.ArmStatus.NotPresent;
                else
                {
                    var armCfg = CloneForScreen(cfg, armDir, filter);
                    if (!(await pipeline.BuildProjectAsync(armDir, armCfg)).Succeeded)
                        armStatus = DeterminismScreen.ArmStatus.BuildFailed;
                    else
                    {
                        var (mr, _) = await FiveRuns("mutated", armDir, armCfg);
                        mutRuns = mr;
                        armStatus = DeterminismScreen.ArmStatus.Ran;
                    }
                }
            }

            screens.Add(new DeterminismScreen.TaskScreen
            {
                TaskId = taskId, ProjectName = projectName, HeldOutFilter = filter,
                CSharpReferenceRuns = csRuns, CalorReferenceRuns = calRuns,
                MutatedRuns = mutRuns, MutatedArm = armStatus,
                Inconclusive = csBad ?? calBad,
            });
        }
    }
    finally
    {
        try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, recursive: true); } catch { /* best effort */ }
    }

    var result = new DeterminismScreen.Result
    {
        Tasks = screens,
        CorpusPins = ProjectConfigs.KnownProjects
            .Where(p => !ProjectConfigs.SyntheticProjects.Contains(p))
            .ToDictionary(p => p, p => SubmoduleSha(Path.Combine(projectsDir, p)), StringComparer.OrdinalIgnoreCase),
        HarnessCommit = SubmoduleSha("."),
    };

    var md = DeterminismScreenReport.Render(result);
    await File.WriteAllTextAsync(Path.Combine(epochDir, "determinism-screen.md"), md);
    await File.WriteAllTextAsync(Path.Combine(epochDir, "determinism-screen.json"),
        System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine();
    Console.WriteLine(md);

    // A gate command must gate: a vacuous screen and a total-failure screen are not successes.
    return result.Tasks.Count > 0 && result.Rejected == 0 ? 0 : 1;
}

DeterminismScreen.TaskScreen Blank(string id, string proj, string filter, string why) => new()
{
    TaskId = id, ProjectName = proj, HeldOutFilter = filter,
    CSharpReferenceRuns = [], CalorReferenceRuns = [], MutatedRuns = [],
    MutatedArm = DeterminismScreen.ArmStatus.NotAttempted, Inconclusive = why,
};

string SubmoduleSha(string dir)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
        { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p == null) return "unknown";
        var sha = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit(5000);
        return string.IsNullOrWhiteSpace(sha) ? "unknown" : sha;
    }
    catch { return "unknown"; }
}

RoundTripConfig CloneForScreen(
    RoundTripConfig c,
    string workDir,
    string? testFilter) =>
    TaskGenerator.Clone(c, workDir, testFilter);

// v0.12 S1 pre-pass (kickoff D-1/D-5): expressible-stratum SUPPLY per project, conversion-only.
// No builds, no tests, no recovery, no bundles — and, critically, NO ELIGIBILITY EVALUATION, which
// is what lets it run before the A-1.5 freeze (plan §3 constraint (a)).
async Task<int> EnumerateSupplyCommand(string[] args)
{
    var projectsDir = GetOption(args, "--projects-dir") ?? ProjectConfigs.DefaultCorpusDir;
    projectsDir = Path.GetFullPath(projectsDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    var outputDir = Path.GetFullPath(GetOption(args, "--output") ?? "supply-enumeration");
    // No --dotnet option: this command runs no builds and no tests, so a dotnet path would be dead
    // configuration that implies otherwise.

    var optionsWithValues = new HashSet<string> { "--projects-dir", "--output" };
    var knownFlags = new HashSet<string>(optionsWithValues) { "--all", "--include-synthetic" };
    var projectNames = new List<string>();
    if (args.Contains("--all"))
    {
        // --all means the OSS corpus. The synthetic subjects are purpose-built to CONTAIN
        // expressible candidates, so including them would inflate the very ceiling the plan §4
        // go/no-go consumes. Opt in explicitly if you want them.
        projectNames = args.Contains("--include-synthetic")
            ? ProjectConfigs.KnownProjects.ToList()
            : ProjectConfigs.KnownProjects.Where(p => !ProjectConfigs.SyntheticProjects.Contains(p)).ToList();
    }
    else
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                // Reject unknown flags and the --opt=value form outright. GetOption only understands
                // space-separated values, so `--projects-dir=/x` would otherwise be silently dropped
                // and the DEFAULT corpus used — a silent wrong-corpus run producing a number that
                // feeds a venue-retirement decision.
                if (!knownFlags.Contains(args[i]))
                {
                    Console.Error.WriteLine($"Unrecognized option '{args[i]}'. " +
                        "Use the space-separated form (e.g. --projects-dir <path>); '--opt=value' is not supported.");
                    return 2;
                }
                if (optionsWithValues.Contains(args[i])) i++;
                continue;
            }
            projectNames.Add(args[i]);
        }
    }
    if (projectNames.Count == 0)
    {
        Console.Error.WriteLine("Specify at least one project, or --all.");
        return 2;
    }

    var configs = new List<RoundTripConfig>();
    foreach (var name in projectNames)
    {
        var cfg = ProjectConfigs.Get(name, projectsDir, "dotnet");
        if (cfg == null) { Console.Error.WriteLine($"Unknown project '{name}'."); return 2; }
        if (!Directory.Exists(cfg.OriginalProjectPath))
        {
            // D-6: a missing corpus subject must change the denominator LOUDLY, never silently.
            Console.Error.WriteLine(
                $"ERROR: project '{name}' is not present at {cfg.OriginalProjectPath} " +
                "(uninitialized submodule?). Refusing to run: a silently reduced denominator is the " +
                "failure mode this pre-pass exists to avoid. Initialize it, or drop it from the argument " +
                "list deliberately so the reduced corpus is explicit.");
            return 2;
        }
        configs.Add(cfg);
    }

    var pipeline = new RoundTripPipeline();
    var workRoot = Path.Combine(Path.GetTempPath(), "calor-supply-" + Guid.NewGuid().ToString("N")[..8]);

    // Fail on an unusable output directory BEFORE doing three full conversions, not after.
    Directory.CreateDirectory(outputDir);

    SupplyEnumeration.Result result;
    var supplyParseOptions = new Dictionary<
        string,
        Dictionary<string, IReadOnlyList<
            Microsoft.CodeAnalysis.CSharp.CSharpParseOptions>>>(
        StringComparer.Ordinal);
    try
    {
        result = await SupplyEnumeration.RunAsync(
            configs,
            async project =>
            {
                Console.WriteLine($"[{project.ProjectName}] converting (no build, no tests)…");
                var dir = pipeline.PrepareWorkingCopy(CloneForSupply(project, Path.Combine(workRoot, project.ProjectName)));
                var report = new RoundTripReport { ProjectName = project.ProjectName };
                var files = await pipeline.ConvertAndReplaceAsync(dir, project, report);
                supplyParseOptions[project.ProjectName] = files
                    .Select(file => (
                        file.FilePath,
                        Contexts: report.EvaluatedParseResolutions.TryGetValue(
                            ProjectParseContextResolver.Canonicalize(
                                Path.Combine(dir, file.FilePath)),
                            out var resolution)
                            ? resolution switch
                            {
                                ResolvedProjectFileParseContext resolved =>
                                    new[] { resolved.Context.ParseOptions },
                                AmbiguousProjectFileParseContext ambiguous =>
                                    ambiguous.Contexts
                                        .Select(context => context.ParseOptions)
                                        .ToArray(),
                                _ => []
                            }
                            : []))
                    .Where(entry => entry.Contexts.Length > 0)
                    .ToDictionary(
                        entry => entry.FilePath,
                        entry => (IReadOnlyList<
                            Microsoft.CodeAnalysis.CSharp.CSharpParseOptions>)
                            entry.Contexts,
                        StringComparer.Ordinal);
                return files;
            },
            async (project, rel) =>
            {
                var abs = Path.Combine(project.OriginalProjectPath, rel);
                return File.Exists(abs) ? await File.ReadAllTextAsync(abs) : null;
            },
            (project, rel) =>
                supplyParseOptions.TryGetValue(
                    project.ProjectName,
                    out var contexts)
                && contexts.TryGetValue(rel, out var options)
                    ? options
                    : throw new InvalidOperationException(
                        $"Supply candidate '{rel}' has no evaluated parse context."));
    }
    finally
    {
        // workRoot holds full recursive copies of every subject; leaking it on the exception path
        // silently strands hundreds of MB.
        try { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, recursive: true); } catch { /* best effort */ }
    }

    var rendered = SupplyEnumerationReport.Render(result);
    var reportPath = Path.Combine(outputDir, "supply-enumeration.md");
    await File.WriteAllTextAsync(reportPath, rendered);
    var jsonPath = Path.Combine(outputDir, "supply-enumeration.json");
    await File.WriteAllTextAsync(jsonPath, System.Text.Json.JsonSerializer.Serialize(result,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine();
    Console.WriteLine(rendered);
    Console.WriteLine($"Written: {reportPath}");

    // Exit code carries NO supply signal on purpose: this runs before the A-1.5 freeze, and an
    // exit code that encoded "supply > 0" would be exactly the kind of side-channel that made the
    // superseded seal worthless (plan §10, Draft v2.2). 0 = the pass completed.
    return 0;
}

RoundTripConfig CloneForSupply(RoundTripConfig c, string workDir) => new()
{
    ProjectName = c.ProjectName,
    OriginalProjectPath = c.OriginalProjectPath,
    LibrarySourceRelativePath = c.LibrarySourceRelativePath,
    SolutionOrProjectFile = c.SolutionOrProjectFile,
    ParseContextProjectFile = c.ParseContextProjectFile,
    WorkingDirectory = workDir,
    ExcludePatterns = c.ExcludePatterns,
    DotnetPath = c.DotnetPath,
    TargetFramework = c.TargetFramework,
    ExtraBuildProperties = c.ExtraBuildProperties,
    LooseDirectoryMode = c.LooseDirectoryMode,
    TestTimeout = c.TestTimeout,
    BuildTimeout = c.BuildTimeout,
};

// Cheap diagnostic: mine the revert-upstream-bugfix SUPPLY per project (git-only, no build/test).
// Reports the raw fix-shaped supply, the source+covering-test subset (the "candidates" number), and
// how many yield a clean source-hunk-only revert vs. are excluded (multi-source / inseparable).
int MineCommand(string[] mineArgs)
{
    var projectsDir = GetOption(mineArgs, "--projects-dir") ?? ProjectConfigs.DefaultCorpusDir;
    projectsDir = Path.GetFullPath(projectsDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    var scanCommits = int.TryParse(GetOption(mineArgs, "--scan-commits"), out var sc) ? sc : 5000;
    var samples = int.TryParse(GetOption(mineArgs, "--samples"), out var sm) ? sm : 3;

    var optionsWithValues = new HashSet<string> { "--projects-dir", "--scan-commits", "--samples" };
    var projectNames = new List<string>();
    if (mineArgs.Contains("--all")) projectNames = ProjectConfigs.KnownProjects.ToList();
    else
    {
        for (int i = 0; i < mineArgs.Length; i++)
        {
            if (mineArgs[i].StartsWith("--"))
            {
                if (optionsWithValues.Contains(mineArgs[i]) && i + 1 < mineArgs.Length) i++;
                continue;
            }
            projectNames.Add(mineArgs[i]);
        }
    }
    if (projectNames.Count == 0)
    {
        Console.Error.WriteLine("No projects specified. Use --all or provide project names.");
        return 1;
    }

    int grandFix = 0, grandCand = 0, grandBuildable = 0, grandMulti = 0, grandInsep = 0;
    foreach (var name in projectNames)
    {
        var config = ProjectConfigs.Get(name, projectsDir, "dotnet");
        if (config == null) { Console.Error.WriteLine($"Unknown project: {name}"); continue; }
        if (!Directory.Exists(config.OriginalProjectPath))
        {
            Console.Error.WriteLine($"{name}: submodule not checked out at {config.OriginalProjectPath} (run: git submodule update --init).");
            continue;
        }

        var fixCommits = BugfixMiner.MineFixCommits(config.OriginalProjectPath, config.LibrarySourceRelativePath, scanCommits);
        var candidates = BugfixMiner.SelectRevertCandidates(fixCommits);

        var verbose = mineArgs.Contains("--verbose");
        int buildable = 0, multi = 0, insep = 0, shown = 0, insepShown = 0;
        var samplesList = new List<MutationCandidate>();
        foreach (var fc in candidates)
        {
            var res = BugfixMiner.TryBuildRevertCandidate(config.OriginalProjectPath, fc);
            if (res.Succeeded) { buildable++; if (shown < samples) { samplesList.Add(res.Candidate!); shown++; } }
            else if (res.Exclusion == ExclusionReason.MultipleSourceFiles) multi++;
            else
            {
                insep++;
                if (verbose && insepShown < 5)
                {
                    Console.Error.WriteLine($"    [insep] {fc.Sha[..8]} {fc.SourceFiles.FirstOrDefault()} :: {res.ExclusionDetail}");
                    insepShown++;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 64));
        Console.WriteLine($"  {name} — revert-bugfix supply (scanned {scanCommits} commits, prefix {config.LibrarySourceRelativePath})");
        Console.WriteLine(new string('=', 64));
        Console.WriteLine($"  fix-shaped commits           : {fixCommits.Count}");
        Console.WriteLine($"  with source + covering test  : {candidates.Count}   <-- revert candidates (SelectRevertCandidates fires)");
        Console.WriteLine($"    clean source-hunk revert   : {buildable}");
        Console.WriteLine($"    excluded multi-source-file : {multi}");
        Console.WriteLine($"    excluded inseparable       : {insep}");
        foreach (var s in samplesList)
            Console.WriteLine($"    e.g. revert {s.RevertedCommit?[..8]} in {s.FileRelPath}:{s.Line} — \"{s.RevertedCommitSubject}\"");

        grandFix += fixCommits.Count; grandCand += candidates.Count;
        grandBuildable += buildable; grandMulti += multi; grandInsep += insep;
    }

    Console.WriteLine();
    Console.WriteLine($"TOTAL: fix-shaped={grandFix}, revert-candidates={grandCand}, clean-revert={grandBuildable}, multi-source={grandMulti}, inseparable={grandInsep}");
    return grandBuildable > 0 ? 0 : 1;
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
          calor-roundtrip mine <project...> [options]     Report revert-bugfix supply per project (git-only, no build)
          calor-roundtrip enumerate-supply <project...>   Expressible-stratum supply per project (conversion-only;
                                                          no builds/tests, and no eligibility evaluated)
          calor-roundtrip screen-determinism --epoch <dir> D-S5.1 screen: gates 0.2's 5-consecutive-green rule
                                                          over an epoch's eligible tasks
          calor-roundtrip list                            List known projects

        Options (run):
          --projects-dir <path>    Directory containing target project clones
          --output <path>          Output directory for reports (default: conversion-reports)
          --dotnet <path>          Path to dotnet executable (or a bare 'dotnet' on PATH)
          --build-timeout <min>    Per-build timeout in minutes (default 15)
          --min-coverage <0..1>    Override the project's minimum total coverage
          --min-native <0..1>      Override the project's minimum native coverage
          --bisect                 Enable regression bisection

        Options (gen-tasks):
          --output <path>          Output directory for task bundles (default: task-bundles)
          --source <sel>           Defect source: injected | revert | both (default injected).
                                   'revert' = gold-standard revert-upstream-bugfix; 'injected' = mutation supplement.
          --stratum <sel>          Defect stratum: logic | expressible | both (default both).
                                   'logic' = conversion-penalty/PP-A2 operators (no Calor signal);
                                   'expressible' = verification-addressable operators (effect/div0/index/null),
                                   each gated by the differential addressability check.
          --scan-commits <n>       Upstream commits to scan for fix commits (revert source; default 2000)
          --native-bar <frac>      Fidelity-gate NativeFraction bar (default provisional 0.70)
          --max-candidates <n>     Max candidates evaluated per project (default 8; 0 = unbounded)
          --target <n>             Stop after this many eligible bundles per project (default 3)
        """);
}
