using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: the per-file process protocol. Each file runs in a child process; the parent never trusts a child that
/// did not finish. Exit codes: 0 report written; <see cref="TimeoutExitCode"/> a member input timed out (the parent
/// limits that member to the inputs before it and restarts); <see cref="PlanTimeoutExitCode"/> receiver construction
/// timed out while planning (the parent bans that receiver and restarts); anything else is a crash, located from the
/// child's checkpoint (run, member, and on a traced re-run the input) and handled like a timeout once it reproduces.
/// Limited members are then replayed from clean state in probe processes, twice per build.
/// </summary>
public sealed partial class OracleRunner
{
    /// <summary>Wall-clock limit for one child process (conversion, swap build, four runs).</summary>
    public static readonly TimeSpan FileTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Restarts of one file before it is reported unfinished (which fails the gate).</summary>
    public const int MaxRestarts = 40;

    public const int TimeoutExitCode = 3;
    public const int PlanTimeoutExitCode = 4;

    /// <summary>What restarts carry: member input limits (with why), and banned receivers.</summary>
    internal sealed class ChildState
    {
        public Dictionary<string, int> Limits { get; init; } = new(StringComparer.Ordinal);
        public HashSet<string> Bans { get; init; } = new(StringComparer.Ordinal);

        public ChildState Copy() => new() { Limits = new(Limits, StringComparer.Ordinal), Bans = new(Bans, StringComparer.Ordinal) };
    }

    private sealed record TimeoutNote(string Member, int Completed);

    private sealed record Checkpoint(string Label, string Member, int? Input)
    {
        public override string ToString() => $"{Label} {Member}{(Input is { } i ? $" input #{i}" : "")}";
    }

    private sealed record Attempt(string Status, OracleFileReport? Report, string Detail, Checkpoint? At);

    /// <summary>The original compared with itself over every type (any prepared file's stem supplies the plan inputs).</summary>
    private async Task<ChildState> CalibrateAsync(string relative, string specPath, string originalPath, string fileStem, string stem,
        string workDir, CancellationToken cancellationToken)
    {
        var (_, state, _, _) = await RunProtocolAsync(relative, specPath, originalPath, fileStem, stem, workDir, new ChildState(), self: true, cancellationToken);
        return state;
    }

    private async Task<OracleFileReport> RunChildAsync(string relative, string specPath, string originalPath, string stem,
        string workDir, ChildState calibration, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var (attempt, state, restarts, unreproduced) = await RunProtocolAsync(relative, specPath, originalPath, stem, stem, workDir, calibration.Copy(),
            self: false, cancellationToken);
        if (attempt.Status != "ok" || attempt.Report!.Status == "oracle-error")
        {
            var status = attempt.Status == "ok" ? "oracle-error" : attempt.Status.StartsWith("oracle-", StringComparison.Ordinal) ? attempt.Status : "oracle-" + attempt.Status;
            return new OracleFileReport
            {
                File = relative, Status = status, Detail = attempt.Report?.Detail ?? attempt.Detail,
                Checkpoint = (attempt.At ?? ReadCheckpoint(stem + ".progress"))?.ToString(),
                Restarts = restarts, Seconds = Seconds(clock),
            };
        }
        var report = attempt.Report;
        if (report.Status != "exercised")
            return report with { Restarts = restarts, Seconds = Seconds(clock) };

        var members = new List<MemberVerdict>();
        foreach (var verdict in report.Members)
        {
            var v = verdict;
            if (v.LimitedAt is { } k)
            {
                // Replays use this file's plan, limits and bans: the original's replay is not shared across files.
                var original = await ProbeTwiceAsync(stem, specPath, originalPath, v.Key, k, onCandidate: false, state, workDir, cancellationToken);
                var converted = await ProbeTwiceAsync(stem, specPath, originalPath, v.Key, k, onCandidate: true, state, workDir, cancellationToken);
                v = MemberDifferentialOracle.ClassifyTimeout(v, original, converted, options);
            }
            if (unreproduced.Contains(v.Key) && v.Status is MemberStatus.Agree)
                v = v with { Status = MemberStatus.Nondeterministic, Reason = "the file's process crashed in this member once, and the crash did not reproduce" };
            members.Add(v);
        }
        return report with { Members = members, Restarts = restarts, Seconds = Seconds(clock) };
    }

    /// <summary>Spawns the file's child until it finishes, folding timeouts, receiver bans and reproduced crashes into the state.</summary>
    private async Task<(Attempt Attempt, ChildState State, int Restarts, HashSet<string> Unreproduced)> RunProtocolAsync(string relative,
        string specPath, string originalPath, string fileStem, string stem, string workDir, ChildState state, bool self, CancellationToken cancellationToken)
    {
        var restarts = 0;
        var unreproduced = new HashSet<string>(StringComparer.Ordinal);
        string? trace = null;
        while (true)
        {
            var attempt = await SpawnAsync(relative, specPath, originalPath, fileStem, stem, workDir, state, trace, self, cancellationToken);
            switch (attempt.Status)
            {
                case "ok":
                    if (trace is not null)
                        unreproduced.Add(trace);
                    return (attempt, state, restarts, unreproduced);
                case "member-timeout":
                {
                    var note = JsonSerializer.Deserialize<TimeoutNote>(await File.ReadAllTextAsync(stem + ".timeout.json", cancellationToken), Json)!;
                    state.Limits[note.Member] = Math.Min(note.Completed, state.Limits.GetValueOrDefault(note.Member, int.MaxValue));
                    trace = null;
                    break;
                }
                case "plan-timeout":
                    state.Bans.Add((await File.ReadAllTextAsync(stem + ".ban.txt", cancellationToken)).Trim());
                    break;
                default:
                    // A crash (or a process-level hang). Inside a member it is located, reproduced and limited like a timeout.
                    if (attempt.At is not { Member.Length: > 0 } at || !at.Label.StartsWith("run", StringComparison.Ordinal))
                        return (attempt with { Status = "oracle-" + attempt.Status }, state, restarts, unreproduced);
                    if (at.Input is { } input && trace == at.Member)
                    {
                        state.Limits[at.Member] = Math.Min(input, state.Limits.GetValueOrDefault(at.Member, int.MaxValue));
                        trace = null;
                    }
                    else if (trace == at.Member)
                    {
                        // Traced, and it still failed before reaching any input of the member: outside the member's inputs.
                        return (attempt with { Status = "oracle-" + attempt.Status }, state, restarts, unreproduced);
                    }
                    else
                    {
                        trace = at.Member;
                    }
                    break;
            }
            if (++restarts > MaxRestarts)
                return (new Attempt("oracle-unfinished", null, $"more than {MaxRestarts} restarts; the file was never compared to the end", null), state, restarts, unreproduced);
        }
    }

    private static double Seconds(Stopwatch clock) => Math.Round(clock.Elapsed.TotalSeconds, 2);

    private string HostPrefix(out string host)
    {
        host = Environment.ProcessPath ?? "dotnet";
        return Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{typeof(OracleRunner).Assembly.Location}\" " : "";
    }

    /// <summary>The input seed is the project name, so every file of a project drives the same inputs (calibrated
    /// limits and original-build replays then apply to every file).</summary>
    private string CommonArguments(string scopeArgument) =>
        $"--seed \"{config.ProjectName}\" --depth {options.Depth} "
        + $"--member-timeout {options.MemberTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} --scope {scopeArgument}";

    private async Task<string> StateArgumentsAsync(string stem, ChildState state, string? trace, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(stem + ".limits.json", JsonSerializer.Serialize(state.Limits, Json), cancellationToken);
        await File.WriteAllTextAsync(stem + ".bans.json", JsonSerializer.Serialize(state.Bans.Order(StringComparer.Ordinal).ToList(), Json), cancellationToken);
        await File.WriteAllTextAsync(stem + ".trace.txt", trace ?? "", cancellationToken);
        return $"--limits \"{stem}.limits.json\" --bans \"{stem}.bans.json\" --trace-file \"{stem}.trace.txt\"";
    }

    private async Task<Attempt> SpawnAsync(string relative, string specPath, string originalPath, string fileStem, string stem, string workDir,
        ChildState state, string? trace, bool self, CancellationToken cancellationToken)
    {
        var prefix = HostPrefix(out var host);
        foreach (var suffix in new[] { ".timeout.json", ".ban.txt", ".json", ".progress" })
            File.Delete(stem + suffix);
        var arguments = $"{prefix}oracle-file --spec \"{specPath}\" --original \"{originalPath}\" --candidate \"{(self ? originalPath : fileStem + ".converted.dll")}\" "
            + $"--scope-file \"{fileStem}.scope.json\" --conversion \"{fileStem}.conversion.json\" --relative \"{relative}\" "
            + CommonArguments(self ? "all" : scope) + " " + await StateArgumentsAsync(stem, state, trace, cancellationToken)
            + $" --stem \"{stem}\"" + (self ? " --self" : "");
        var (exit, stdout, stderr) = await ProcessRunner.RunAsync(host, arguments, workDir, FileTimeout, cancellationToken: cancellationToken);
        var tail = (stderr + stdout).Trim();
        var detail = $"exit {exit}: " + (tail.Length > 600 ? tail[^600..] : tail);
        if (exit == TimeoutExitCode && File.Exists(stem + ".timeout.json"))
            return new Attempt("member-timeout", null, detail, null);
        if (exit == PlanTimeoutExitCode && File.Exists(stem + ".ban.txt"))
            return new Attempt("plan-timeout", null, detail, null);
        if (exit == 0 && File.Exists(stem + ".json"))
            return new Attempt("ok", JsonSerializer.Deserialize<OracleFileReport>(await File.ReadAllTextAsync(stem + ".json", cancellationToken), Json)!, detail, null);
        return new Attempt(exit == -1 ? "timeout" : "crashed", null, detail, ReadCheckpoint(stem + ".progress"));
    }

    private static Checkpoint? ReadCheckpoint(string path)
    {
        if (!File.Exists(path))
            return null;
        var parts = File.ReadAllText(path).TrimEnd('\n').Split('\t');
        return parts.Length < 2 ? null
            : new Checkpoint(parts[0], parts[1], parts.Length > 2 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out var i) ? i : null);
    }

    private async Task<ProbeResult[]> ProbeTwiceAsync(string stem, string specPath, string originalPath, string member, int input, bool onCandidate,
        ChildState state, string workDir, CancellationToken cancellationToken) =>
        [await ProbeAsync(stem, specPath, originalPath, member, input, onCandidate, state, workDir, cancellationToken),
         await ProbeAsync(stem, specPath, originalPath, member, input, onCandidate, state, workDir, cancellationToken)];

    /// <summary>
    /// One clean-state replay in its own process. The child writes a marker right before the replayed input runs: a
    /// process that dies after the marker crashed in the subject; one that dies before it never reached the input
    /// (infrastructure, never a behavioral observation).
    /// </summary>
    private async Task<ProbeResult> ProbeAsync(string stem, string specPath, string originalPath, string member, int input, bool onCandidate,
        ChildState state, string workDir, CancellationToken cancellationToken)
    {
        var prefix = HostPrefix(out var host);
        var probe = stem + ".probe-" + Guid.NewGuid().ToString("N")[..8];
        await File.WriteAllTextAsync(probe + ".member.txt", member, cancellationToken);
        var arguments = $"{prefix}oracle-probe --spec \"{specPath}\" --original \"{originalPath}\" --candidate \"{stem}.converted.dll\" "
            + $"--scope-file \"{stem}.scope.json\" --member-file \"{probe}.member.txt\" --input {input} {(onCandidate ? "--on-candidate " : "")}"
            + CommonArguments(scope) + " " + await StateArgumentsAsync(probe, state, null, cancellationToken) + $" --out \"{probe}.json\" --marker \"{probe}.started\"";
        var (exit, _, stderr) = await ProcessRunner.RunAsync(host, arguments, workDir, FileTimeout, cancellationToken: cancellationToken);
        if (exit == 0 && File.Exists(probe + ".json"))
            return JsonSerializer.Deserialize<ProbeResult>(await File.ReadAllTextAsync(probe + ".json", cancellationToken), Json)!;
        var reached = File.Exists(probe + ".started");
        var why = $"probe process exit {exit}: {(stderr.Length > 200 ? stderr[^200..] : stderr).Trim()}";
        return new ProbeResult(!reached ? ProbeOutcome.Infrastructure : exit == -1 ? ProbeOutcome.Hung : ProbeOutcome.Crashed, 0, why);
    }

    // ---- Child side ------------------------------------------------------------------------------

    /// <summary>Overwrites one line in place (the crash checkpoint); flushed to the OS so it survives the process dying.</summary>
    private sealed class ProgressFile(string path) : IDisposable
    {
        private readonly FileStream _stream = new(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

        public void Write(string label, string member, int? input)
        {
            var bytes = Encoding.UTF8.GetBytes($"{label}\t{member}\t{(input is { } i ? i.ToString(CultureInfo.InvariantCulture) : "")}\n");
            _stream.SetLength(0);
            _stream.Position = 0;
            _stream.Write(bytes);
            _stream.Flush();
        }

        public void Dispose() => _stream.Dispose();
    }

    /// <summary>
    /// The child side of the protocol: compares the two real builds for one file, report written to <c>{stem}.json</c>.
    /// With <paramref name="self"/>, the original is compared with itself over every type (calibration).
    /// </summary>
    public static async Task RunFileAsync(string specPath, string originalPath, string candidatePath, string scopePath, string conversionPath,
        string relative, OracleOptions options, string stem, bool self)
    {
        var clock = Stopwatch.StartNew();
        var spec = JsonSerializer.Deserialize<SubjectBuild.Spec>(await File.ReadAllTextAsync(specPath), Json)!;
        var original = new ArmImage("original", await File.ReadAllBytesAsync(originalPath), spec.DependencyPaths);
        var candidate = self ? original : new ArmImage("converted", await File.ReadAllBytesAsync(candidatePath), spec.DependencyPaths);
        var scope = self ? [OraclePlanner.AllTypes] : JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(scopePath), Json)!;
        var conversion = self ? new OracleFileReport { File = relative, Status = "exercised" }
            : JsonSerializer.Deserialize<OracleFileReport>(await File.ReadAllTextAsync(conversionPath), Json)!;
        using var progress = new ProgressFile(stem + ".progress");
        options = options with
        {
            OnTimeout = (member, completed) =>
            {
                File.WriteAllText(stem + ".timeout.json", JsonSerializer.Serialize(new TimeoutNote(member, completed), Json));
                Environment.Exit(TimeoutExitCode);
            },
            OnPlanTimeout = type =>
            {
                File.WriteAllText(stem + ".ban.txt", type);
                Environment.Exit(PlanTimeoutExitCode);
            },
            OnMemberStart = (label, member) => progress.Write(label, member, null),
            OnInputStart = (label, input) => progress.Write(label, options.TraceMember!, input),
        };
        progress.Write("plan", "", null);
        OracleFileReport report;
        try
        {
            report = conversion with
            {
                Status = "exercised", Detail = null, TypesInScope = scope.Count,
                Members = MemberDifferentialOracle.Compare(original, candidate, scope, options),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report = new OracleFileReport { File = relative, Status = "oracle-error", Detail = $"{ex.GetType().Name}: {ex.Message}" };
        }
        await File.WriteAllTextAsync(stem + ".json", JsonSerializer.Serialize(report with { Seconds = Math.Round(clock.Elapsed.TotalSeconds, 2) }, Json));
    }

    /// <summary>The child side of <see cref="ProbeAsync"/>.</summary>
    public static async Task ProbeFileAsync(string specPath, string originalPath, string candidatePath, string scopePath, string member, int input,
        bool onCandidate, OracleOptions options, string output, string marker)
    {
        var spec = JsonSerializer.Deserialize<SubjectBuild.Spec>(await File.ReadAllTextAsync(specPath), Json)!;
        var scope = JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(scopePath), Json)!;
        options = options with
        {
            OnProbeInput = () => File.WriteAllText(marker, ""),
            // A probe that would need a new restart did not reproduce the main run's state: it is infrastructure, not behavior.
            OnTimeout = (_, _) => Environment.Exit(5),
            OnPlanTimeout = _ => Environment.Exit(5),
        };
        var result = MemberDifferentialOracle.Probe(new ArmImage("original", await File.ReadAllBytesAsync(originalPath), spec.DependencyPaths),
            new ArmImage("converted", await File.ReadAllBytesAsync(candidatePath), spec.DependencyPaths), scope, member, input, onCandidate, options);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, Json));
    }
}
