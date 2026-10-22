using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calor.Compiler.Migration;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>One file's outcome.</summary>
public sealed record OracleFileReport
{
    public required string File { get; init; }

    /// <summary>
    /// exercised; or why it was not: excluded (a harness exclude pattern), not-compiled, conversion-failed,
    /// calor-compile-failed, swap-build-failed; or a failure of the file's process: converted-crashed /
    /// converted-timeout / converted-error (the original compared with itself completes, so the converted build is
    /// at fault; the gate fails these), oracle-crashed / oracle-timeout / oracle-error (the original fails too, or
    /// the failure came before the converted build existed).
    /// </summary>
    public required string Status { get; init; }
    public string? Detail { get; init; }

    /// <summary>The conversion ledger records a lossy or dropped construct (any kind but interop preservation).</summary>
    public bool LossRecorded { get; init; }
    public Dictionary<string, int> LossKinds { get; init; } = [];
    public int TypesInScope { get; init; }
    public int Restarts { get; init; }
    public double Seconds { get; init; }
    public List<MemberVerdict> Members { get; init; } = [];
}

/// <summary>A gate decision on one differing member (or a whole file, member <c>&lt;file&gt;</c>, or the project).</summary>
public sealed record OracleGateItem(string File, string Member, string Decision, string Detail);

public sealed record OracleProjectReport
{
    public string Schema { get; init; } = "calor.member-oracle-report/2";
    public required string Project { get; init; }
    public string InputGenerator { get; init; } = MemberOracleInputs.GeneratorId;
    public int Depth { get; init; }
    public string Scope { get; init; } = "";
    public string? Shard { get; init; }
    public string? TargetFramework { get; init; }
    public string? Inconclusive { get; set; }
    public double WallClockSeconds { get; set; }
    public int MinimumExercisedFiles { get; init; }
    public int MinimumComparedMembers { get; init; }
    public List<OracleFileReport> Files { get; init; } = [];

    /// <summary>failing (a difference with no recorded loss and no matching baseline entry, a converted-build crash
    /// or hang, or coverage below the floor), loss-recorded and baselined (tolerated), baseline-changed and
    /// baseline-stale (both fail).</summary>
    public List<OracleGateItem> Gate { get; init; } = [];

    public bool Passed => Inconclusive is null && Gate.All(g => g.Decision is "loss-recorded" or "baselined");

    public int ExercisedFiles => Files.Count(f => f.Status == "exercised");

    /// <summary>Members whose inputs were compared: AGREE, DIFFERENT, and TIMEOUT / NONDETERMINISTIC members with a compared prefix.</summary>
    public int ComparedMembers => Files.SelectMany(f => f.Members).Count(m => m.Status is MemberStatus.Agree or MemberStatus.Different
        || (m.Status is MemberStatus.Timeout or MemberStatus.Nondeterministic && m.Inputs > 0));

    public Dictionary<string, int> MemberTotals => Files.SelectMany(f => f.Members)
        .GroupBy(m => m.Status).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<string, int> SkipReasons => Files.SelectMany(f => f.Members).Where(m => m.Status != MemberStatus.Agree && m.Status != MemberStatus.Different)
        .GroupBy(m => m.Status == MemberStatus.Skipped ? m.Reason ?? "?" : m.Status)
        .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());

    public Dictionary<string, int> FileTotals => Files.GroupBy(f => f.Status)
        .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>#1587: runs the member oracle over one corpus project.</summary>
public sealed class OracleRunner(RoundTripConfig config, OracleOptions options, OracleBaseline baseline, string scope,
    int shardIndex = 0, int shardCount = 1, int jobs = 2)
{
    /// <summary>Wall-clock limit for one child process (conversion, swap build, four runs).</summary>
    public static readonly TimeSpan FileTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Restarts of one file after member timeouts before the file is reported unfinished (and fails).</summary>
    public const int MaxRestarts = 40;

    /// <summary>Exit code of a child that hit a member timeout and wrote <c>.timeout.json</c>.</summary>
    public const int TimeoutExitCode = 3;

    public async Task<OracleProjectReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var report = new OracleProjectReport
        {
            Project = config.ProjectName,
            Depth = options.Depth,
            Scope = scope,
            Shard = shardCount > 1 ? $"{shardIndex + 1}/{shardCount}" : null,
            MinimumExercisedFiles = config.OracleMinimumExercisedFiles,
            MinimumComparedMembers = config.OracleMinimumComparedMembers,
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
            if (build.VerifyAgainstRealBuild(original) is { } mismatch)
            {
                report.Inconclusive = "the in-process build does not match the dotnet build: " + mismatch;
                return report;
            }

            var libDir = Path.Combine(workDir, config.LibrarySourceRelativePath);
            var all = Directory.GetFiles(libDir, "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(f => !IsBuildOutput(Path.GetRelativePath(workDir, f)))
                .Order(StringComparer.Ordinal)
                .Where((_, i) => i % shardCount == shardIndex)
                .ToList();
            var trees = build.Trees;
            Console.WriteLine($"  [{config.ProjectName}] {all.Count} file(s), target {build.TargetFramework}, {jobs} job(s), scope {scope}");

            // Each file runs in its own process: a member that never returns, overflows the stack or exits the
            // process takes down only that process, and abandoned threads die with it.
            var specPath = Path.Combine(scratch, "spec.json");
            var originalPath = Path.Combine(scratch, "original.dll");
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(build.BuildSpec, Json), cancellationToken);
            await File.WriteAllBytesAsync(originalPath, original.Image, cancellationToken);

            // Calibration: the original compared with itself over every type finds the members that time out on the
            // original build. Inputs depend only on the project (not the file), so every file starts from these limits
            // and restarts only where the converted build hangs earlier.
            var calibrationFile = all.FirstOrDefault(f => trees.ContainsKey(f) && ExcludedBy(ReportGenerator.RelativePath(workDir, f)) is null);
            var baseLimits = calibrationFile is null ? new Dictionary<string, int>(StringComparer.Ordinal)
                : await CalibrateAsync(calibrationFile, ReportGenerator.RelativePath(workDir, calibrationFile), specPath, originalPath,
                    Path.Combine(scratch, "calibration"), workDir, cancellationToken);
            Console.WriteLine($"  [{config.ProjectName}] calibration: {baseLimits.Count} member(s) time out on the original build");
            var results = new OracleFileReport[all.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, all.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    var file = all[i];
                    var relative = ReportGenerator.RelativePath(workDir, file);
                    results[i] = ExcludedBy(relative) is { } pattern
                        ? new OracleFileReport { File = relative, Status = "excluded", Detail = $"harness exclude pattern {pattern}" }
                        : !trees.ContainsKey(file)
                            ? new OracleFileReport { File = relative, Status = "not-compiled", Detail = $"not a compile item for {build.TargetFramework}" }
                            : await RunChildAsync(file, relative, specPath, originalPath, Path.Combine(scratch, $"file-{i}"), workDir, baseLimits, token);
                    var r = results[i];
                    var counts = string.Join(" ", r.Members.GroupBy(m => m.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
                    Console.WriteLine($"    {r.File}: {r.Status} {counts} ({r.Seconds:0.0}s{(r.Restarts > 0 ? $", {r.Restarts} restart(s)" : "")})");
                });
            report.Files.AddRange(results);
            ApplyGate(config.ProjectName, baseline, report, all.Select(f => ReportGenerator.RelativePath(workDir, f)).ToHashSet(StringComparer.Ordinal),
                checkFloors: shardCount == 1);
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

    private static bool IsBuildOutput(string relativePath)
    {
        var normalized = "/" + relativePath.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal);
    }

    /// <summary>The harness exclude pattern that matches the file, if any (reported, never silently dropped).</summary>
    private string? ExcludedBy(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return config.ExcludePatterns.FirstOrDefault(p => Glob(normalized, p.Replace('\\', '/')));
    }

    private static bool Glob(string path, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(path, regex);
    }

    // ---- Parent side: one file -----------------------------------------------------------------

    private sealed record TimeoutNote(string Member, int Completed);

    /// <summary>
    /// Runs one file in child processes. A member timeout ends the child (its thread cannot be stopped); the file is
    /// restarted with that member limited to the inputs before the one that hung, so every run drives the same inputs.
    /// Each hanging input is then probed from clean state in fresh processes (twice per build) to tell a converted-build
    /// hang (a difference) from a hang both builds share. A crash or error is attributed by re-running the original
    /// compared with itself: if that completes, the converted build is at fault.
    /// </summary>
    private async Task<Dictionary<string, int>> CalibrateAsync(string file, string relative, string specPath, string originalPath, string stem,
        string workDir, CancellationToken cancellationToken)
    {
        var limits = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var attempt = 0; attempt <= MaxRestarts; attempt++)
        {
            var (status, _, _) = await SpawnAsync("oracle-file", stem, file, relative, specPath, originalPath, limits, self: true, workDir, cancellationToken, scopeOverride: "all");
            if (status != "timeout-restart")
                break;
            var note = JsonSerializer.Deserialize<TimeoutNote>(await File.ReadAllTextAsync(stem + ".timeout.json", cancellationToken), Json)!;
            limits[note.Member] = Math.Min(note.Completed, limits.GetValueOrDefault(note.Member, int.MaxValue));
        }
        return limits;
    }

    /// <summary>Clean-state probes of the original build, shared by every file of the project (the original never changes).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), Task<ProbeResult[]>> _originalProbes = new();

    private async Task<OracleFileReport> RunChildAsync(string file, string relative, string specPath, string originalPath, string stem,
        string workDir, IReadOnlyDictionary<string, int> baseLimits, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var limits = new Dictionary<string, int>(baseLimits, StringComparer.Ordinal);
        var restarts = 0;
        string status;
        OracleFileReport? report;
        string detail;
        while (true)
        {
            (status, report, detail) = await SpawnAsync("oracle-file", stem, file, relative, specPath, originalPath, limits, self: false, workDir, cancellationToken);
            if (status != "timeout-restart")
                break;
            var note = JsonSerializer.Deserialize<TimeoutNote>(await File.ReadAllTextAsync(stem + ".timeout.json", cancellationToken), Json)!;
            limits[note.Member] = Math.Min(note.Completed, limits.GetValueOrDefault(note.Member, int.MaxValue));
            if (++restarts > MaxRestarts)
            {
                return new OracleFileReport
                {
                    File = relative, Status = "converted-unfinished", Restarts = restarts, Seconds = Seconds(clock),
                    Detail = $"more than {MaxRestarts} member timeouts; the file was never compared to the end",
                };
            }
        }

        if (status == "ok" && report!.Status != "oracle-error")
        {
            if (report.Status != "exercised")
                return report with { Restarts = restarts, Seconds = Seconds(clock) };
            var members = new List<MemberVerdict>();
            foreach (var verdict in report.Members)
                members.Add(verdict.LimitedAt is { } k && verdict.Status != MemberStatus.Different
                    ? MemberDifferentialOracle.ClassifyTimeout(verdict,
                        await _originalProbes.GetOrAdd((verdict.Key, k), async _ =>
                            [await ProbeAsync(stem + ".o", specPath, originalPath, originalPath, relative, verdict.Key, k, workDir, cancellationToken),
                             await ProbeAsync(stem + ".o", specPath, originalPath, originalPath, relative, verdict.Key, k, workDir, cancellationToken)]),
                        [await ProbeAsync(stem, specPath, originalPath, stem + ".converted.dll", relative, verdict.Key, k, workDir, cancellationToken),
                         await ProbeAsync(stem, specPath, originalPath, stem + ".converted.dll", relative, verdict.Key, k, workDir, cancellationToken)],
                        options)
                    : verdict);
            return report with { Members = members, Restarts = restarts, Seconds = Seconds(clock) };
        }

        if (status == "ok")
            detail = report!.Detail ?? "";
        var conversion = File.Exists(stem + ".conversion.json")
            ? JsonSerializer.Deserialize<OracleFileReport>(await File.ReadAllTextAsync(stem + ".conversion.json", cancellationToken), Json)
            : null;
        var failure = status == "ok" ? "error" : status;
        if (conversion is null)
        {
            // The child failed before the converted build existed (in conversion or the swap build): nothing ran.
            return new OracleFileReport { File = relative, Status = "oracle-" + failure, Detail = detail, Restarts = restarts, Seconds = Seconds(clock) };
        }
        var (selfStatus, selfReport, selfDetail) = await SpawnAsync("oracle-file", stem + ".self", file, relative, specPath, originalPath, limits, self: true, workDir, cancellationToken);
        var originalIsClean = selfStatus == "ok" && selfReport!.Status == "exercised";
        return conversion with
        {
            Status = (originalIsClean ? "converted-" : "oracle-") + failure,
            Detail = originalIsClean
                ? $"the converted build failed ({failure}) where the original compared with itself completes: {detail}"
                : $"the original also fails when compared with itself ({selfStatus}: {selfReport?.Detail ?? selfDetail}): {detail}",
            Restarts = restarts,
            Seconds = Seconds(clock),
        };
    }

    private static double Seconds(Stopwatch clock) => Math.Round(clock.Elapsed.TotalSeconds, 2);

    private string HostPrefix(out string host)
    {
        host = Environment.ProcessPath ?? "dotnet";
        return Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{typeof(OracleRunner).Assembly.Location}\" " : "";
    }

    /// <summary>The input seed is the project name, so every file of a project drives the same inputs (calibrated
    /// limits and original-build probes then apply to every file).</summary>
    private string CommonArguments(string? scopeOverride = null) =>
        $"--seed \"{config.ProjectName}\" --depth {options.Depth} "
        + $"--member-timeout {options.MemberTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} --scope {scopeOverride ?? scope}";

    private async Task<(string Status, OracleFileReport? Report, string Detail)> SpawnAsync(string command, string stem, string file, string relative,
        string specPath, string originalPath, Dictionary<string, int> limits, bool self, string workDir, CancellationToken cancellationToken,
        string? scopeOverride = null)
    {
        var prefix = HostPrefix(out var host);
        var limitsPath = stem + ".limits.json";
        await File.WriteAllTextAsync(limitsPath, JsonSerializer.Serialize(limits, Json), cancellationToken);
        File.Delete(stem + ".timeout.json");
        File.Delete(stem + ".json");
        var arguments = $"{prefix}{command} --spec \"{specPath}\" --original \"{originalPath}\" --file \"{file}\" --relative \"{relative}\" "
            + CommonArguments(scopeOverride) + $" --limits \"{limitsPath}\" --stem \"{stem}\"" + (self ? " --self" : "");
        var (exit, stdout, stderr) = await ProcessRunner.RunAsync(host, arguments, workDir, FileTimeout, cancellationToken: cancellationToken);
        var tail = (stderr + stdout).Trim();
        var detail = $"exit {exit}: " + (tail.Length > 600 ? tail[^600..] : tail);
        if (exit == TimeoutExitCode && File.Exists(stem + ".timeout.json"))
            return ("timeout-restart", null, detail);
        if (exit == 0 && File.Exists(stem + ".json"))
            return ("ok", JsonSerializer.Deserialize<OracleFileReport>(await File.ReadAllTextAsync(stem + ".json", cancellationToken), Json)!, detail);
        return (exit == -1 ? "timeout" : "crashed", null, detail);
    }

    /// <summary>One clean-state probe in its own process. A probe process that crashes reports that as its observation.</summary>
    private async Task<ProbeResult> ProbeAsync(string stem, string specPath, string planImage, string runImage, string relative, string member, int input,
        string workDir, CancellationToken cancellationToken)
    {
        var prefix = HostPrefix(out var host);
        // The member's scope file is written by the file's own child; ".o" probes share the file's stem.
        var scopeFile = (stem.EndsWith(".o", StringComparison.Ordinal) ? stem[..^2] : stem) + ".scope.json";
        stem += "." + Guid.NewGuid().ToString("N")[..8];
        var output = stem + ".probe.json";
        var memberPath = stem + ".probe-member.txt";
        await File.WriteAllTextAsync(memberPath, member, cancellationToken);
        File.Delete(output);
        var arguments = $"{prefix}oracle-probe --spec \"{specPath}\" --plan-image \"{planImage}\" --run-image \"{runImage}\" --scope-file \"{scopeFile}\" "
            + $"--member-file \"{memberPath}\" --input {input} {CommonArguments()} --out \"{output}\"";
        var (exit, _, stderr) = await ProcessRunner.RunAsync(host, arguments, workDir, FileTimeout, cancellationToken: cancellationToken);
        return exit == 0 && File.Exists(output)
            ? JsonSerializer.Deserialize<ProbeResult>(await File.ReadAllTextAsync(output, cancellationToken), Json)!
            : new ProbeResult(true, 0, $"<probe process exit {exit}: {(stderr.Length > 200 ? stderr[^200..] : stderr).Trim()}>");
    }

    // ---- Child side ------------------------------------------------------------------------------

    /// <summary>
    /// The child side of <see cref="RunChildAsync"/>: one file, report written to <c>{stem}.json</c>. A member timeout
    /// writes <c>{stem}.timeout.json</c> and exits with <see cref="TimeoutExitCode"/>. With <paramref name="self"/>, the
    /// original is compared with itself (no conversion), to attribute a failure.
    /// </summary>
    public static async Task RunFileAsync(string specPath, string originalPath, string file, string relative, OracleOptions options,
        string scope, string stem, bool self)
    {
        var clock = Stopwatch.StartNew();
        var spec = JsonSerializer.Deserialize<SubjectBuild.Spec>(await File.ReadAllTextAsync(specPath), Json)!;
        var build = SubjectBuild.FromSpec(spec);
        var original = new ArmImage("original", await File.ReadAllBytesAsync(originalPath), spec.DependencyPaths);
        var manifestRoot = Path.Combine(Path.GetTempPath(), "calor-oracle-1587-empty-manifests-" + Environment.ProcessId);
        options = options with
        {
            OnTimeout = (member, completed) =>
            {
                File.WriteAllText(stem + ".timeout.json", JsonSerializer.Serialize(new TimeoutNote(member, completed), Json));
                Console.Out.Flush();
                Environment.Exit(TimeoutExitCode);
            },
        };
        OracleFileReport report;
        try
        {
            report = self
                ? new OracleFileReport
                {
                    File = relative, Status = "exercised",
                    Members = MemberDifferentialOracle.Compare(original, original, build.AffectedTypes(file, null, scope), options),
                }
                : RunFile(file, relative, build, original, options, scope, manifestRoot, (conversion, candidate, types) =>
                {
                    File.WriteAllBytes(stem + ".converted.dll", candidate.Image);
                    File.WriteAllText(stem + ".scope.json", JsonSerializer.Serialize(types, Json));
                    File.WriteAllText(stem + ".conversion.json", JsonSerializer.Serialize(conversion, Json));
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report = new OracleFileReport { File = relative, Status = "oracle-error", Detail = $"{ex.GetType().Name}: {ex.Message}" };
        }
        await File.WriteAllTextAsync(stem + ".json", JsonSerializer.Serialize(report with { Seconds = Math.Round(clock.Elapsed.TotalSeconds, 2) }, Json));
    }

    /// <summary>The child side of <see cref="ProbeAsync"/>.</summary>
    public static async Task ProbeFileAsync(string specPath, string planImage, string runImage, string scopePath, string member, int input,
        OracleOptions options, string output)
    {
        var spec = JsonSerializer.Deserialize<SubjectBuild.Spec>(await File.ReadAllTextAsync(specPath), Json)!;
        var scope = JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(scopePath), Json)!;
        var result = MemberDifferentialOracle.Probe(new ArmImage("plan", await File.ReadAllBytesAsync(planImage), spec.DependencyPaths),
            new ArmImage("probe", await File.ReadAllBytesAsync(runImage), spec.DependencyPaths), scope, member, input, options);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, Json));
    }

    internal static OracleFileReport RunFile(string file, string relative, SubjectBuild build, ArmImage original, OracleOptions options,
        string scope, string manifestRoot, Action<OracleFileReport, ArmImage, IReadOnlyList<string>>? onConverted = null)
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

        var types = build.AffectedTypes(file, converted.CSharp, scope);
        // Recorded before any member runs, so a crash while driving members still knows the conversion outcome.
        onConverted?.Invoke(Stop("converted", null, converted.LossRecorded, converted.LossKinds) with { TypesInScope = types.Count }, candidate, types);
        var members = MemberDifferentialOracle.Compare(original, candidate, types, options);
        return new OracleFileReport
        {
            File = relative, Status = "exercised", LossRecorded = converted.LossRecorded, LossKinds = converted.LossKinds,
            TypesInScope = types.Count, Members = members,
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

    // ---- Gate ------------------------------------------------------------------------------------

    /// <summary>Decides every difference, checks this shard's baseline entries for staleness, and (unsharded) the coverage floors.</summary>
    internal static void ApplyGate(string project, OracleBaseline baseline, OracleProjectReport report, HashSet<string> filesInShard, bool checkFloors = true)
    {
        var entries = baseline.Entries.Where(e => e.Project == project && filesInShard.Contains(e.File))
            .ToDictionary(e => (e.File, e.Member));
        var used = new HashSet<(string, string)>();
        void Decide(OracleFileReport file, string member, string? fingerprint, string witness)
        {
            if (entries.TryGetValue((file.File, member), out var entry))
            {
                used.Add((file.File, member));
                report.Gate.Add(entry.Fingerprint == fingerprint
                    ? new OracleGateItem(file.File, member, "baselined", entry.Finding)
                    : new OracleGateItem(file.File, member, "baseline-changed", $"baseline fingerprint {entry.Fingerprint}, observed {fingerprint}: {witness}"));
            }
            else if (file.LossRecorded)
            {
                report.Gate.Add(new OracleGateItem(file.File, member, "loss-recorded",
                    "conversion recorded " + string.Join(", ", file.LossKinds.Select(kv => $"{kv.Key}={kv.Value}")) + "; " + witness));
            }
            else
            {
                report.Gate.Add(new OracleGateItem(file.File, member, "failing", witness));
            }
        }
        foreach (var file in report.Files)
        {
            // The converted build crashed, hung or broke the oracle where the original did not: a difference for the whole file.
            if (file.Status.StartsWith("converted-", StringComparison.Ordinal))
                Decide(file, "<file>", MemberDifferentialOracle.Hash(file.Status), file.Detail ?? file.Status);
            foreach (var member in file.Members.Where(m => m.Status == MemberStatus.Different))
                Decide(file, member.Key, member.Fingerprint, member.Witnesses.FirstOrDefault() ?? "");
        }
        // An entry is stale only when its member ran and agreed; a member skipped this run proves nothing.
        foreach (var ((file, member), entry) in entries.Where(kv => !used.Contains(kv.Key)))
        {
            var fileReport = report.Files.FirstOrDefault(f => f.File == file);
            var verdict = fileReport?.Members.FirstOrDefault(m => m.Key == member);
            if (verdict?.Status == MemberStatus.Agree || (member == "<file>" && fileReport?.Status == "exercised"))
                report.Gate.Add(new OracleGateItem(file, member, "baseline-stale", "the member now agrees; remove the entry (" + entry.Finding + ")"));
        }
        if (checkFloors && report.Inconclusive is null)
        {
            if (report.ExercisedFiles < report.MinimumExercisedFiles)
                report.Gate.Add(new OracleGateItem("*", "<coverage>", "failing",
                    $"{report.ExercisedFiles} files exercised, floor {report.MinimumExercisedFiles} (ProjectConfigs.OracleMinimumExercisedFiles)"));
            if (report.ComparedMembers < report.MinimumComparedMembers)
                report.Gate.Add(new OracleGateItem("*", "<coverage>", "failing",
                    $"{report.ComparedMembers} members compared, floor {report.MinimumComparedMembers} (ProjectConfigs.OracleMinimumComparedMembers)"));
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
        sb.AppendLine($"Inputs `{MemberOracleInputs.GeneratorId}`, state depth {reports.FirstOrDefault()?.Depth ?? StateRenderer.DefaultDepth}, scope `{reports.FirstOrDefault()?.Scope}`. ")
          .AppendLine("A member is compared when its inputs ran on both builds; it is skipped when it could not be driven, disagreed with its own re-run, or ran out of time.").AppendLine();
        sb.AppendLine("| Project | Verdict | Files exercised | Members compared | Agree | Different | Skipped | Wall clock |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in reports)
        {
            var totals = r.MemberTotals;
            var skipped = totals.Where(kv => kv.Key is not (MemberStatus.Agree or MemberStatus.Different)).Sum(kv => kv.Value);
            var verdict = r.Inconclusive is not null ? "inconclusive" : r.Passed ? "pass" : "FAIL";
            sb.AppendLine($"| {r.Project}{(r.Shard is null ? "" : $" ({r.Shard})")} | {verdict} | {r.ExercisedFiles}/{r.Files.Count} (floor {r.MinimumExercisedFiles}) | "
                + $"{r.ComparedMembers} (floor {r.MinimumComparedMembers}) | {totals.GetValueOrDefault(MemberStatus.Agree)} | {totals.GetValueOrDefault(MemberStatus.Different)} | {skipped} | {r.WallClockSeconds:0}s |");
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
            sb.AppendLine().AppendLine($"<details><summary>{r.Project}: files and skip reasons</summary>").AppendLine();
            sb.AppendLine("Files: " + string.Join(", ", r.FileTotals.Select(kv => $"{kv.Key} {kv.Value}"))).AppendLine();
            foreach (var (reason, count) in r.SkipReasons)
                sb.AppendLine($"- {count} × {reason}");
            sb.AppendLine().AppendLine("</details>");
        }
        return sb.ToString();
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s.Replace("\n", " ") : s[..n].Replace("\n", " ") + "…";
}
