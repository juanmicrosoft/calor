using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>One file's outcome.</summary>
public sealed record OracleFileReport
{
    public required string File { get; init; }

    /// <summary>exercised, or the step that stopped the file: not-compiled, conversion-failed,
    /// calor-compile-failed, swap-build-failed, oracle-error.</summary>
    public required string Status { get; init; }
    public string? Detail { get; init; }

    /// <summary>The conversion ledger records a lossy or dropped construct (any kind but interop preservation).</summary>
    public bool LossRecorded { get; init; }
    public Dictionary<string, int> LossKinds { get; init; } = [];
    public int TypesInScope { get; init; }
    public double Seconds { get; init; }
    public List<MemberVerdict> Members { get; init; } = [];
}

/// <summary>A gate decision on one differing member.</summary>
public sealed record OracleGateItem(string File, string Member, string Decision, string Detail);

public sealed record OracleProjectReport
{
    public string Schema { get; init; } = "calor.member-oracle-report/1";
    public required string Project { get; init; }
    public string InputGenerator { get; init; } = MemberOracleInputs.GeneratorId;
    public int Depth { get; init; }
    public string Scope { get; init; } = "";
    public string? Shard { get; init; }
    public string? TargetFramework { get; init; }
    public string? Inconclusive { get; set; }
    public double WallClockSeconds { get; set; }
    public List<OracleFileReport> Files { get; init; } = [];

    /// <summary>failing (a difference on a file with no recorded loss and no matching baseline entry),
    /// loss-recorded (tolerated), baselined (tolerated), baseline-changed and baseline-stale (both fail).</summary>
    public List<OracleGateItem> Gate { get; init; } = [];

    public bool Passed => Inconclusive is null && Gate.All(g => g.Decision is "loss-recorded" or "baselined");

    public Dictionary<string, int> MemberTotals => Files.SelectMany(f => f.Members)
        .GroupBy(m => m.Status).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<string, int> SkipReasons => Files.SelectMany(f => f.Members).Where(m => m.Status != MemberStatus.Agree && m.Status != MemberStatus.Different)
        .GroupBy(m => m.Status == MemberStatus.Skipped ? m.Reason ?? "?" : m.Status)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<string, int> FileTotals => Files.GroupBy(f => f.Status)
        .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>#1587: runs the member oracle over one corpus project.</summary>
public sealed class OracleRunner(RoundTripConfig config, OracleOptions options, OracleBaseline baseline, bool includeDependents,
    int shardIndex = 0, int shardCount = 1, int jobs = 2)
{
    /// <summary>Wall-clock limit for one file's child process (conversion, swap build, four runs).</summary>
    public static readonly TimeSpan FileTimeout = TimeSpan.FromMinutes(10);

    public async Task<OracleProjectReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var report = new OracleProjectReport
        {
            Project = config.ProjectName,
            Depth = options.Depth,
            Scope = includeDependents ? "declared+dependents" : "declared",
            Shard = shardCount > 1 ? $"{shardIndex + 1}/{shardCount}" : null,
        };
        var scratch = Directory.CreateTempSubdirectory("calor-oracle-1587-").FullName;
        try
        {
            var pipeline = new RoundTripPipeline();
            var workDir = pipeline.PrepareWorkingCopy(config with { WorkingDirectory = Path.Combine(scratch, "work") }, cancellationToken);
            var projectFile = FindProject(workDir);
            Console.WriteLine($"  [{config.ProjectName}] building {Path.GetRelativePath(workDir, projectFile)}");
            var build = await SubjectBuild.CreateAsync(projectFile, config, cancellationToken);
            report = report with { TargetFramework = build.TargetFramework };
            var original = build.Emit("original", new Dictionary<string, string>(), out var originalErrors, cancellationToken);
            if (original is null)
            {
                report.Inconclusive = "the original subject does not compile in-process: " + string.Join(" | ", originalErrors.Take(3))
                    + $" (generators loaded: {build.GeneratorCount}; load failures: {string.Join("; ", build.GeneratorLoadFailures.Take(5))})";
                return report;
            }

            var libDir = Path.Combine(workDir, config.LibrarySourceRelativePath);
            var files = Directory.GetFiles(libDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExcluded(Path.GetRelativePath(workDir, f)))
                .Select(Path.GetFullPath)
                .Order(StringComparer.Ordinal)
                .Where((_, i) => i % shardCount == shardIndex)
                .ToList();
            var trees = build.Trees;
            Console.WriteLine($"  [{config.ProjectName}] {files.Count} file(s), target {build.TargetFramework}, {jobs} job(s)");

            // Each file runs in its own process: a member that never returns, overflows the stack or exits the
            // process takes down only that file, and its abandoned threads die with the process.
            var specPath = Path.Combine(scratch, "spec.json");
            var originalPath = Path.Combine(scratch, "original.dll");
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(build.BuildSpec, Json), cancellationToken);
            await File.WriteAllBytesAsync(originalPath, original.Image, cancellationToken);
            var results = new OracleFileReport[files.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, files.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    var file = files[i];
                    var relative = ReportGenerator.RelativePath(workDir, file);
                    results[i] = trees.ContainsKey(file)
                        ? await RunChildAsync(file, relative, specPath, originalPath, Path.Combine(scratch, $"file-{i}.json"), workDir, token)
                        : new OracleFileReport { File = relative, Status = "not-compiled", Detail = $"not a compile item for {build.TargetFramework}" };
                    var r = results[i];
                    var counts = string.Join(" ", r.Members.GroupBy(m => m.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
                    Console.WriteLine($"    {r.File}: {r.Status} {counts} ({r.Seconds:0.0}s)");
                });
            report.Files.AddRange(results);
            ApplyGate(config.ProjectName, baseline, report, files.Select(f => ReportGenerator.RelativePath(workDir, f)).ToHashSet(StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Inconclusive = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            report.WallClockSeconds = Math.Round(clock.Elapsed.TotalSeconds, 1);
            try { Directory.Delete(scratch, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return report;
    }

    private string FindProject(string workDir)
    {
        if (config.ParseContextProjectFile is { } explicitProject)
            return Path.GetFullPath(Path.Combine(workDir, explicitProject));
        var candidates = Directory.GetFiles(Path.Combine(workDir, config.LibrarySourceRelativePath), "*.csproj", SearchOption.TopDirectoryOnly);
        return candidates.Length == 1 ? Path.GetFullPath(candidates[0])
            : throw new InvalidOperationException($"expected one project in {config.LibrarySourceRelativePath}, found {candidates.Length}");
    }

    private bool IsExcluded(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
            return true;
        return config.ExcludePatterns.Any(p => Glob(normalized, p.Replace('\\', '/')));
    }

    private static bool Glob(string path, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(path, regex);
    }

    private async Task<OracleFileReport> RunChildAsync(string file, string relative, string specPath, string originalPath, string outPath,
        string workDir, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var host = Environment.ProcessPath ?? "dotnet";
        var prefix = Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{typeof(OracleRunner).Assembly.Location}\" " : "";
        var arguments = $"{prefix}oracle-file --spec \"{specPath}\" --original \"{originalPath}\" --file \"{file}\" --relative \"{relative}\" "
            + $"--seed \"{config.ProjectName}|{relative}\" --depth {options.Depth} --member-timeout {options.MemberTimeout.TotalSeconds} "
            + $"--scope {(includeDependents ? "dependents" : "declared")} --out \"{outPath}\"";
        var (exit, stdout, stderr) = await ProcessRunner.RunAsync(host, arguments, workDir, FileTimeout, cancellationToken: cancellationToken);
        if (exit == 0 && File.Exists(outPath))
            return JsonSerializer.Deserialize<OracleFileReport>(await File.ReadAllTextAsync(outPath, cancellationToken), Json)!;
        var tail = (stderr + stdout).Trim();
        return new OracleFileReport
        {
            File = relative,
            Status = exit == -1 ? "oracle-timeout" : "oracle-crashed",
            Detail = $"exit {exit}: " + (tail.Length > 600 ? tail[^600..] : tail),
            Seconds = Math.Round(clock.Elapsed.TotalSeconds, 2),
        };
    }

    /// <summary>The child side of <see cref="RunChildAsync"/>: one file, original image given, report written to <paramref name="outPath"/>.</summary>
    public static async Task RunFileAsync(string specPath, string originalPath, string file, string relative, OracleOptions options,
        bool includeDependents, string outPath)
    {
        var clock = Stopwatch.StartNew();
        var spec = JsonSerializer.Deserialize<SubjectBuild.Spec>(await File.ReadAllTextAsync(specPath), Json)!;
        var build = SubjectBuild.FromSpec(spec);
        var original = new ArmImage("original", await File.ReadAllBytesAsync(originalPath), spec.DependencyPaths);
        var manifestRoot = Path.Combine(Path.GetTempPath(), "calor-oracle-1587-empty-manifests-" + Environment.ProcessId);
        OracleFileReport report;
        try
        {
            report = RunFile(file, relative, build, original, options, includeDependents, manifestRoot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report = new OracleFileReport { File = relative, Status = "oracle-error", Detail = $"{ex.GetType().Name}: {ex.Message}" };
        }
        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(report with { Seconds = Math.Round(clock.Elapsed.TotalSeconds, 2) }, Json));
    }

    internal static OracleFileReport RunFile(string file, string relative, SubjectBuild build, ArmImage original, OracleOptions options,
        bool includeDependents, string manifestRoot)
    {
        OracleFileReport Stop(string status, string? detail, bool loss = false, Dictionary<string, int>? kinds = null) => new()
        {
            File = relative, Status = status, Detail = detail, LossRecorded = loss, LossKinds = kinds ?? [],
        };
        var source = File.ReadAllText(file);
        var converted = RoundTrip(source, file, manifestRoot);
        if (converted.Error is { } conversionError)
            return Stop(converted.Status, conversionError, converted.LossRecorded, converted.LossKinds);

        var candidate = build.Emit("converted", new Dictionary<string, string> { [file] = converted.CSharp! }, out var errors);
        if (candidate is null)
            return Stop("swap-build-failed", string.Join(" | ", errors.Take(3)), converted.LossRecorded, converted.LossKinds);

        var scope = build.AffectedTypes(file, converted.CSharp, includeDependents);
        var members = MemberDifferentialOracle.Compare(original, candidate, scope, options);
        return new OracleFileReport
        {
            File = relative, Status = "exercised", LossRecorded = converted.LossRecorded, LossKinds = converted.LossKinds,
            TypesInScope = scope.Count, Members = members,
        };
    }

    public sealed record RoundTripResult(string Status, string? CSharp, string? Error, bool LossRecorded, Dictionary<string, int> LossKinds);

    /// <summary>
    /// C# to Calor with exactly the options <c>calor convert</c> builds with no flags (the #1586 default leg),
    /// then Calor to C# with the harness's compile options. User-level manifests never apply: the converter
    /// gets an empty manifest root and the compile's effect resolver loads built-in manifests only.
    /// </summary>
    public static RoundTripResult RoundTrip(string source, string path, string emptyManifestRoot, CancellationToken cancellationToken = default)
    {
        var conversionOptions = RoundTripPipeline.CreateDefaultModeConversionOptions();
        conversionOptions.UserManifestRoot = emptyManifestRoot;
        ConversionResult conversion;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                conversion = new CSharpToCalorConverter(conversionOptions).Convert(source, path, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new RoundTripResult("conversion-failed", null, "conversion timed out", false, []);
            }
        }
        var lossKinds = conversion.Context.Losses.GroupBy(l => l.Kind.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
        var lossRecorded = conversion.Context.Losses.Any(l => l.Kind is not (ConversionLossKind.InteropPreserved or ConversionLossKind.EmitterFallback));
        if (!conversion.Success || string.IsNullOrWhiteSpace(conversion.CalorSource))
            return new RoundTripResult("conversion-failed",
                null, string.Join(" | ", conversion.Issues.Where(i => i.Severity == ConversionIssueSeverity.Error).Select(i => i.Message).Take(3)), lossRecorded, lossKinds);

        var resolver = new Calor.Compiler.Effects.EffectResolver(new Calor.Compiler.Effects.Manifests.ManifestLoader(loadUserLevelManifests: false));
        resolver.Initialize(projectDirectory: null, solutionDirectory: null);
        using var context = new Calor.Compiler.CompilationContext { SharedEffectResolver = resolver };
        var compile = Calor.Compiler.Program.Compile(conversion.CalorSource, path, new Calor.Compiler.CompilationOptions
        {
            EnforceEffects = false,
            ContractMode = Calor.Compiler.ContractMode.Off,
            DeferGeneratedOutputValidation = true,
            StatusWriter = TextWriter.Null,
            Context = context,
            CancellationToken = cancellationToken,
        });
        if (compile.HasErrors || string.IsNullOrWhiteSpace(compile.GeneratedCode))
            return new RoundTripResult("calor-compile-failed", null,
                string.Join(" | ", compile.Diagnostics.Errors.Take(3).Select(d => d.Code + " " + d.Message)), lossRecorded, lossKinds);
        return new RoundTripResult("converted", RoundTripPipeline.PostProcessEmittedCSharp(compile.GeneratedCode, source), null, lossRecorded, lossKinds);
    }

    /// <summary>Decides every difference, and checks this shard's baseline entries for staleness.</summary>
    internal static void ApplyGate(string project, OracleBaseline baseline, OracleProjectReport report, HashSet<string> filesInShard)
    {
        var entries = baseline.Entries.Where(e => e.Project == project && filesInShard.Contains(e.File))
            .ToDictionary(e => (e.File, e.Member));
        var used = new HashSet<(string, string)>();
        foreach (var file in report.Files)
        {
            foreach (var member in file.Members.Where(m => m.Status == MemberStatus.Different))
            {
                var witness = member.Witnesses.FirstOrDefault() ?? "";
                if (entries.TryGetValue((file.File, member.Key), out var entry))
                {
                    used.Add((file.File, member.Key));
                    report.Gate.Add(entry.Witness == witness
                        ? new OracleGateItem(file.File, member.Key, "baselined", entry.Finding)
                        : new OracleGateItem(file.File, member.Key, "baseline-changed", $"baseline witness: {entry.Witness} | observed: {witness}"));
                }
                else if (file.LossRecorded)
                {
                    report.Gate.Add(new OracleGateItem(file.File, member.Key, "loss-recorded",
                        "conversion recorded " + string.Join(", ", file.LossKinds.Select(kv => $"{kv.Key}={kv.Value}")) + "; " + witness));
                }
                else
                {
                    report.Gate.Add(new OracleGateItem(file.File, member.Key, "failing", witness));
                }
            }
        }
        // An entry is stale only when its member ran and agreed; a member skipped this run proves nothing.
        foreach (var ((file, member), entry) in entries.Where(kv => !used.Contains(kv.Key)))
        {
            var verdict = report.Files.FirstOrDefault(f => f.File == file)?.Members.FirstOrDefault(m => m.Key == member);
            if (verdict?.Status == MemberStatus.Agree)
                report.Gate.Add(new OracleGateItem(file, member, "baseline-stale", "the member now agrees; remove the entry (" + entry.Finding + ")"));
        }
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Markdown(IReadOnlyList<OracleProjectReport> reports)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Member differential oracle (#1587)").AppendLine();
        sb.AppendLine($"Inputs `{MemberOracleInputs.GeneratorId}`, state depth {reports.FirstOrDefault()?.Depth ?? StateRenderer.DefaultDepth}. ")
          .AppendLine("A member is exercised when it ran on both builds; it is skipped when it could not be driven, disagreed with its own re-run, or ran out of time.").AppendLine();
        sb.AppendLine("| Project | Verdict | Files exercised | Members agree | Different | Skipped | Wall clock |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var r in reports)
        {
            var totals = r.MemberTotals;
            var skipped = totals.Where(kv => kv.Key is not (MemberStatus.Agree or MemberStatus.Different)).Sum(kv => kv.Value);
            var verdict = r.Inconclusive is not null ? "inconclusive" : r.Passed ? "pass" : "FAIL";
            sb.AppendLine($"| {r.Project}{(r.Shard is null ? "" : $" ({r.Shard})")} | {verdict} | {r.Files.Count(f => f.Status == "exercised")}/{r.Files.Count} | "
                + $"{totals.GetValueOrDefault(MemberStatus.Agree)} | {totals.GetValueOrDefault(MemberStatus.Different)} | {skipped} | {r.WallClockSeconds:0}s |");
        }
        foreach (var r in reports)
        {
            if (r.Inconclusive is not null)
                sb.AppendLine().AppendLine($"**{r.Project} inconclusive:** {r.Inconclusive}");
            if (r.Gate.Count > 0)
            {
                sb.AppendLine().AppendLine($"### {r.Project}: differences").AppendLine();
                foreach (var g in r.Gate)
                    sb.AppendLine($"- **{g.Decision}** `{g.File}` `{g.Member}`: {Truncate(g.Detail, 400)}");
            }
            if (r.SkipReasons.Count > 0)
            {
                sb.AppendLine().AppendLine($"<details><summary>{r.Project}: skip reasons</summary>").AppendLine();
                foreach (var (reason, count) in r.SkipReasons)
                    sb.AppendLine($"- {count} × {reason}");
                sb.AppendLine().AppendLine("Files: " + string.Join(", ", r.FileTotals.Select(kv => $"{kv.Key} {kv.Value}"))).AppendLine().AppendLine("</details>");
            }
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s.Replace("\n", " ") : s[..n].Replace("\n", " ") + "…";
}
