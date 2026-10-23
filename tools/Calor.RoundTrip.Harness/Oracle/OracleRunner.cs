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

    /// <summary>
    /// exercised; or why it was not: excluded (a harness exclude pattern), not-compiled, conversion-failed,
    /// calor-compile-failed, swap-build-failed; or a failure the gate fails: oracle-crashed / oracle-timeout /
    /// oracle-error / oracle-unfinished (the file's process failed outside any member, or kept failing),
    /// candidate-build-mismatch / candidate-build-failed (the real build of the swapped file differs from the
    /// in-process candidate).
    /// </summary>
    public required string Status { get; init; }
    public string? Detail { get; init; }

    /// <summary>Where a failing process was when it failed: run label and member (and input when traced).</summary>
    public string? Checkpoint { get; init; }

    /// <summary>The conversion ledger records a lossy or dropped construct (any kind but interop preservation).</summary>
    public bool LossRecorded { get; init; }
    public Dictionary<string, int> LossKinds { get; init; } = [];
    public int TypesInScope { get; init; }
    public int Restarts { get; init; }
    public double Seconds { get; init; }
    public List<MemberVerdict> Members { get; init; } = [];
}

/// <summary>A gate decision on one differing member (or a whole file, member <c>&lt;file&gt;</c>, or coverage).</summary>
public sealed record OracleGateItem(string File, string Member, string Decision, string Detail);

public sealed record OracleProjectReport
{
    public string Schema { get; init; } = "calor.member-oracle-report/3";
    public required string Project { get; init; }
    public string InputGenerator { get; init; } = MemberOracleInputs.GeneratorId;
    public int Depth { get; init; }
    public string Scope { get; init; } = "";
    public string? Shard { get; init; }
    public string? TargetFramework { get; init; }
    public string? Inconclusive { get; set; }
    public double WallClockSeconds { get; set; }
    public List<OracleFileReport> Files { get; init; } = [];

    /// <summary>failing (a difference with no recorded loss and no matching baseline entry, a file the oracle could not
    /// finish, an inconclusive member, a candidate build mismatch, an expected file no longer exercised, or too few
    /// members compared), loss-recorded / baselined / new-coverage (tolerated), baseline-changed and baseline-stale
    /// (both fail).</summary>
    public List<OracleGateItem> Gate { get; init; } = [];

    public bool Passed => Inconclusive is null && Gate.All(g => g.Decision is "loss-recorded" or "baselined" or "new-coverage");

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
public sealed partial class OracleRunner(RoundTripConfig config, OracleOptions options, OracleBaseline baseline, OracleExpected expected, string scope,
    int shardIndex = 0, int shardCount = 1, int jobs = 2)
{
    public async Task<OracleProjectReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var report = new OracleProjectReport
        {
            Project = config.ProjectName,
            Depth = options.Depth,
            Scope = scope,
            Shard = shardCount > 1 ? $"{shardIndex + 1}/{shardCount}" : null,
        };
        var scratch = Directory.CreateTempSubdirectory("calor-oracle-1587-").FullName;
        try
        {
            var pipeline = new RoundTripPipeline();
            var workDir = pipeline.PrepareWorkingCopy(config with { WorkingDirectory = Path.Combine(scratch, "work") }, cancellationToken);
            var projectFile = FindProject(workDir);
            Console.WriteLine($"  [{config.ProjectName}] building {Path.GetRelativePath(workDir, projectFile)}");
            var build = await SubjectBuild.CreateAsync(projectFile, workDir, config, cancellationToken);
            report = report with { TargetFramework = build.TargetFramework };
            // The in-process compile is only a fast swap check and the type-scope source; both arms the oracle runs are
            // real `dotnet build` outputs, so what it compares is exactly what the project builds.
            if (build.Emit("original", new Dictionary<string, string>(), out var originalErrors, cancellationToken) is null)
            {
                report.Inconclusive = "the original subject does not compile in-process: " + string.Join(" | ", originalErrors.Take(3))
                    + $" (generators loaded: {build.GeneratorCount}; load failures: {string.Join("; ", build.GeneratorLoadFailures.Take(5))})";
                return report;
            }
            var originalImage = build.RealOriginalImage();

            var libDir = Path.Combine(workDir, config.LibrarySourceRelativePath);
            var everyFile = Directory.GetFiles(libDir, "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(f => !IsBuildOutput(Path.GetRelativePath(workDir, f)))
                .Order(StringComparer.Ordinal)
                .ToList();
            // The full inventory (before sharding), so every shard can check that expected files still exist.
            var inventory = everyFile.Select(f => ReportGenerator.RelativePath(workDir, f)).ToHashSet(StringComparer.Ordinal);
            var all = everyFile.Where((_, i) => i % shardCount == shardIndex).ToList();
            var trees = build.Trees;
            Console.WriteLine($"  [{config.ProjectName}] {all.Count} file(s), target {build.TargetFramework}, {jobs} job(s), scope {scope}");

            var specPath = Path.Combine(scratch, "spec.json");
            var originalPath = Path.Combine(scratch, "original.dll");
            await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(build.BuildSpec, Json), cancellationToken);
            await File.WriteAllBytesAsync(originalPath, originalImage, cancellationToken);

            // Phase 1: convert, check the swap in-process, then build the real candidate on a private working copy.
            var workers = System.Threading.Channels.Channel.CreateUnbounded<SubjectBuild.CandidateWorker>();
            foreach (var worker in await Task.WhenAll(Enumerable.Range(0, Math.Max(1, jobs)).Select(w =>
                         SubjectBuild.CandidateWorker.CreateAsync(build, workDir, Path.Combine(scratch, $"worker-{w}"), config.BuildTimeout, cancellationToken))))
                workers.Writer.TryWrite(worker);
            var manifestRoot = Path.Combine(scratch, "empty-manifests");
            var results = new OracleFileReport[all.Count];
            var pending = new List<int>();
            await Parallel.ForEachAsync(Enumerable.Range(0, all.Count),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    var relative = ReportGenerator.RelativePath(workDir, all[i]);
                    var prepared = await PrepareAsync(all[i], relative, workDir, build, trees, workers, manifestRoot, Path.Combine(scratch, $"file-{i}"), token);
                    if (prepared is null)
                        lock (pending) pending.Add(i);
                    else
                        results[i] = prepared;
                });
            Console.WriteLine($"  [{config.ProjectName}] {pending.Count} candidate(s) built by dotnet build");

            // Calibration: the original compared with itself over every type finds the members that time out, crash or
            // block receiver planning on the original build. Inputs depend only on the project (not the file), so every
            // file starts from this state and restarts only where the converted build fails earlier.
            var calibration = pending.Count == 0 ? new ChildState()
                : await CalibrateAsync(ReportGenerator.RelativePath(workDir, all[pending.Min()]), specPath, originalPath,
                    Path.Combine(scratch, $"file-{pending.Min()}"), Path.Combine(scratch, "calibration"), workDir, cancellationToken);
            Console.WriteLine($"  [{config.ProjectName}] calibration: {calibration.Limits.Count} member limit(s), {calibration.Bans.Count} receiver ban(s)");

            // Phase 2: one child process per file compares the two real builds.
            await Parallel.ForEachAsync(pending.Order(),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    var relative = ReportGenerator.RelativePath(workDir, all[i]);
                    results[i] = await RunChildAsync(relative, specPath, originalPath, Path.Combine(scratch, $"file-{i}"), workDir, calibration, token);
                    var r = results[i];
                    var counts = string.Join(" ", r.Members.GroupBy(m => m.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
                    Console.WriteLine($"    {r.File}: {r.Status} {counts} ({r.Seconds:0.0}s{(r.Restarts > 0 ? $", {r.Restarts} restart(s)" : "")})");
                });
            report.Files.AddRange(results);
            ApplyGate(config.ProjectName, baseline, expected, report, all.Select(f => ReportGenerator.RelativePath(workDir, f)).ToHashSet(StringComparer.Ordinal),
                inventory);
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

    /// <summary>
    /// Phase 1 for one file. Returns the file's final report when it stops here (excluded, not compiled, conversion or
    /// swap failure), or null when its candidate is ready for the oracle: <c>{stem}.converted.dll</c> (real build),
    /// <c>{stem}.converted.cs</c>, <c>{stem}.scope.json</c> and <c>{stem}.conversion.json</c>.
    /// </summary>
    private async Task<OracleFileReport?> PrepareAsync(string file, string relative, string workDir, SubjectBuild build, IReadOnlyDictionary<string, Microsoft.CodeAnalysis.SyntaxTree> trees,
        System.Threading.Channels.Channel<SubjectBuild.CandidateWorker> workers, string manifestRoot, string stem, CancellationToken cancellationToken)
    {
        if (ExcludedBy(relative) is { } pattern)
            return new OracleFileReport { File = relative, Status = "excluded", Detail = $"harness exclude pattern {pattern}" };
        if (!trees.ContainsKey(file))
            return new OracleFileReport { File = relative, Status = "not-compiled", Detail = $"not a compile item for {build.TargetFramework}" };
        var source = await File.ReadAllTextAsync(file, cancellationToken);
        var converted = RoundTrip(source, file, manifestRoot, cancellationToken);
        var conversion = new OracleFileReport { File = relative, Status = converted.Status, Detail = converted.Error, LossRecorded = converted.LossRecorded, LossKinds = converted.LossKinds };
        if (converted.Error is not null)
            return conversion;
        if (build.Emit("converted", new Dictionary<string, string> { [file] = converted.CSharp! }, out var errors, cancellationToken) is null)
            return conversion with { Status = "swap-build-failed", Detail = string.Join(" | ", errors.Take(3)) };
        var worker = await workers.Reader.ReadAsync(cancellationToken);
        (byte[]? Image, string? Error) candidate;
        try
        {
            candidate = await worker.BuildCandidateAsync(Path.GetRelativePath(workDir, file), converted.CSharp!, cancellationToken);
        }
        finally
        {
            workers.Writer.TryWrite(worker);
        }
        if (candidate.Image is null)
            return conversion with { Status = "candidate-build-failed", Detail = "compiles in-process but not with dotnet build: " + candidate.Error };
        await File.WriteAllBytesAsync(stem + ".converted.dll", candidate.Image, cancellationToken);
        await File.WriteAllTextAsync(stem + ".converted.cs", converted.CSharp!, cancellationToken);
        await File.WriteAllTextAsync(stem + ".scope.json", JsonSerializer.Serialize(build.AffectedTypes(file, converted.CSharp, scope), Json), cancellationToken);
        await File.WriteAllTextAsync(stem + ".conversion.json", JsonSerializer.Serialize(conversion, Json), cancellationToken);
        return null;
    }

    /// <summary>The working-copy root the candidate workers mirror (paths inside it are the same in every worker).</summary>
    private string WorkerRootRelative(SubjectBuild build)
    {
        var dir = Path.GetDirectoryName(build.ProjectFile)!;
        var depth = config.LibrarySourceRelativePath.Count(c => c is '/' or '\\') + 1;
        for (var i = 0; i < depth && config.ParseContextProjectFile is null; i++)
            dir = Path.GetDirectoryName(dir)!;
        return config.ParseContextProjectFile is { } p
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(build.ProjectFile)!, string.Concat(Enumerable.Repeat("../", p.Count(c => c is '/' or '\\')))))
            : dir;
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

    /// <summary>
    /// File statuses the gate fails whatever the conversion recorded: the oracle could not finish or trust the file, or
    /// a conversion that succeeded produced code that does not compile (Calor to C#, the in-process swap, or the real
    /// build). Only a conversion the converter itself refused is not a failure.
    /// </summary>
    public static bool IsFailureStatus(string status) =>
        status.StartsWith("oracle-", StringComparison.Ordinal) || status.StartsWith("candidate-build-", StringComparison.Ordinal)
        || status is "swap-build-failed" or "calor-compile-failed";

    /// <summary>
    /// A failure's identity for baselines: status, checkpoint, exit code (<c>exit N</c> or <c>dotnet build failed (N)</c>),
    /// and the failure detail with only clearly volatile tokens removed (see <see cref="NormalizeFailure"/>), so the same
    /// failure keeps its fingerprint across machines while two different errors with the same code do not alias.
    /// </summary>
    internal static string FailureFingerprint(OracleFileReport file)
    {
        var detail = file.Detail ?? "";
        var exit = System.Text.RegularExpressions.Regex.Match(detail, @"\bexit -?\d+|dotnet build failed \(-?\d+\)").Value;
        return MemberDifferentialOracle.Hash(string.Join("\u0001", file.Status, file.Checkpoint ?? "", exit, NormalizeFailure(detail)));
    }

    /// <summary>
    /// Removes only tokens that are clearly volatile: absolute paths (which carry temp-directory names), line and column
    /// positions, canonical 8-4-4-4-12 GUIDs, <c>0x</c>-prefixed addresses, and the oracle's own temp-directory names.
    /// Identifiers, including ones that look like hex (<c>deadbeefdead</c>), are kept.
    /// </summary>
    internal static string NormalizeFailure(string detail)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(detail, @"(?:[A-Za-z]:)?(?:[/\\][^\s:()'""\[\]]+)+", "<path>");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\(\d+,\d+(?:,\d+,\d+)?\)", "");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", "<guid>");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b0x[0-9a-fA-F]+\b", "<address>");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\bcalor-(?:oracle-1587|roundtrip|t1)-[A-Za-z0-9_]+\b", "<temp>");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    /// <summary>Member comparisons a file made: AGREE, DIFFERENT, and TIMEOUT / NONDETERMINISTIC with a compared prefix.</summary>
    public static int Compared(OracleFileReport file) => file.Members.Count(m => m.Status is MemberStatus.Agree or MemberStatus.Different
        || (m.Status is MemberStatus.Timeout or MemberStatus.Nondeterministic && m.Inputs > 0));

    /// <summary>
    /// Decides every difference and file failure, checks this shard's baseline entries for staleness, checks that every
    /// file <c>eng/member-oracle-expected.json</c> lists for this shard was exercised, and (unsharded) the member floor.
    /// </summary>
    internal static void ApplyGate(string project, OracleBaseline baseline, OracleExpected expected, OracleProjectReport report,
        HashSet<string> filesInShard, IReadOnlySet<string> inventory)
    {
        var entries = baseline.Entries.Where(e => e.Project == project && filesInShard.Contains(e.File))
            .ToDictionary(e => (e.File, e.Member));
        var used = new HashSet<(string, string)>();
        void Decide(OracleFileReport file, string member, string? fingerprint, string witness, bool lossTolerated)
        {
            if (entries.TryGetValue((file.File, member), out var entry))
            {
                used.Add((file.File, member));
                report.Gate.Add(entry.Fingerprint == fingerprint
                    ? new OracleGateItem(file.File, member, "baselined", entry.Finding)
                    : new OracleGateItem(file.File, member, "baseline-changed", $"baseline fingerprint {entry.Fingerprint}, observed {fingerprint}: {witness}"));
            }
            else if (lossTolerated && file.LossRecorded)
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
            if (IsFailureStatus(file.Status))
                Decide(file, "<file>", FailureFingerprint(file), $"{file.Status} at {file.Checkpoint ?? "?"}: {file.Detail}", lossTolerated: false);
            foreach (var member in file.Members.Where(m => m.Status == MemberStatus.Different))
                Decide(file, member.Key, member.Fingerprint, member.Witnesses.FirstOrDefault() ?? "", lossTolerated: true);
            foreach (var member in file.Members.Where(m => m.Status == MemberStatus.Inconclusive))
                Decide(file, member.Key, MemberDifferentialOracle.Hash("INCONCLUSIVE\u0001" + member.Reason), member.Reason ?? "inconclusive", lossTolerated: false);
        }
        // An entry is stale only when its member ran and agreed; a member skipped this run proves nothing.
        foreach (var ((file, member), entry) in entries.Where(kv => !used.Contains(kv.Key)))
        {
            var fileReport = report.Files.FirstOrDefault(f => f.File == file);
            var verdict = fileReport?.Members.FirstOrDefault(m => m.Key == member);
            if (verdict?.Status == MemberStatus.Agree || (member == "<file>" && fileReport?.Status == "exercised"))
                report.Gate.Add(new OracleGateItem(file, member, "baseline-stale", "the member now agrees; remove the entry (" + entry.Finding + ")"));
        }
        if (report.Inconclusive is not null)
            return;
        // Coverage by file identity: every expected file of this shard must be exercised with at least 95% of the member
        // comparisons it made when listed (a slower runner can time a member out before its first input); every expected
        // file must still exist (checked against the full inventory, so it holds in every shard); new ones are reported.
        var expectedFiles = expected.For(project);
        foreach (var file in report.Files.Where(f => filesInShard.Contains(f.File)))
        {
            if (expectedFiles.TryGetValue(file.File, out var listed))
            {
                var floor = (int)Math.Floor(listed * 0.95);
                if (file.Status != "exercised" && !IsFailureStatus(file.Status))
                    report.Gate.Add(new OracleGateItem(file.File, "<coverage>", "failing",
                        $"expected to be exercised (eng/member-oracle-expected.json); now {file.Status}: {file.Detail}"));
                else if (file.Status == "exercised" && Compared(file) < floor)
                    report.Gate.Add(new OracleGateItem(file.File, "<coverage>", "failing",
                        $"{Compared(file)} member comparisons, floor {floor} (95% of the {listed} listed in eng/member-oracle-expected.json)"));
            }
            else if (file.Status == "exercised")
            {
                report.Gate.Add(new OracleGateItem(file.File, "<coverage>", "new-coverage", "exercised but not listed in eng/member-oracle-expected.json; add it"));
            }
        }
        foreach (var missing in expectedFiles.Keys.Where(f => !inventory.Contains(f)))
            report.Gate.Add(new OracleGateItem(missing, "<coverage>", "failing", "listed in eng/member-oracle-expected.json but no longer a library file"));
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
        sb.AppendLine("| Project | Verdict | Files exercised | Member comparisons | Agree | Different | Skipped | Wall clock |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in reports)
        {
            var totals = r.MemberTotals;
            var skipped = totals.Where(kv => kv.Key is not (MemberStatus.Agree or MemberStatus.Different)).Sum(kv => kv.Value);
            var verdict = r.Inconclusive is not null ? "inconclusive" : r.Passed ? "pass" : "FAIL";
            sb.AppendLine($"| {r.Project}{(r.Shard is null ? "" : $" ({r.Shard})")} | {verdict} | {r.ExercisedFiles}/{r.Files.Count} | "
                + $"{r.ComparedMembers} | {totals.GetValueOrDefault(MemberStatus.Agree)} | {totals.GetValueOrDefault(MemberStatus.Different)} | {skipped} | {r.WallClockSeconds:0}s |");
        }
        foreach (var r in reports)
        {
            if (r.Inconclusive is not null)
                sb.AppendLine().AppendLine($"**{r.Project} inconclusive:** {r.Inconclusive}");
            if (r.Gate.Count > 0)
            {
                sb.AppendLine().AppendLine($"### {r.Project}: gate").AppendLine();
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

/// <summary>#1587: the files each project must exercise (<c>eng/member-oracle-expected.json</c>), checked by identity, each
/// with the member comparisons it made when listed.</summary>
public sealed record OracleExpected
{
    public const string Schema = "calor.member-oracle-expected/2";

    [JsonPropertyName("schema")] public string SchemaId { get; init; } = Schema;

    [JsonPropertyName("projects")] public Dictionary<string, Dictionary<string, int>> Projects { get; init; } = [];

    public IReadOnlyDictionary<string, int> For(string project) =>
        Projects.TryGetValue(project, out var files) ? files : new Dictionary<string, int>();

    public static string DefaultPath(string repoRoot) => Path.Combine(repoRoot, "eng", "member-oracle-expected.json");

    public static OracleExpected Load(string path)
    {
        if (!File.Exists(path))
            return new OracleExpected();
        var expected = JsonSerializer.Deserialize<OracleExpected>(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} is empty");
        return expected.SchemaId == Schema ? expected : throw new InvalidOperationException($"{path}: schema '{expected.SchemaId}', expected '{Schema}'");
    }
}
