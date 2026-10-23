using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: the member-level differential oracle. Given two builds of the same subject (the original, and one
/// with a single file swapped for its round-tripped C#), it drives the members <see cref="OraclePlanner"/>
/// plans on both and compares what it observes: construction state to <see cref="OracleOptions.Depth"/>;
/// return value, thrown exception type, argument state after the call (including <c>ref</c>/<c>out</c>),
/// the receiver's state, the type's public static state, console output and created files; the public
/// static state of every project type after each member; static state after type initialization; enum
/// values; and the surface (members, kind, visibility, base chain and interfaces). A member present in only
/// one build is a difference.
///
/// <para>Each build runs twice, every run in a fresh collectible load context, in the order original,
/// converted, original, converted, so each run starts from clean static state. An input is compared only
/// when both builds reproduce it on re-run; the member is NONDETERMINISTIC only when no stable input
/// differs. An input whose setup (receiver, arguments) or observation failed identically on both builds, or
/// whose observation is incomplete on both, is not counted as compared.</para>
///
/// <para>A member input that times out or crashes is handled by restarting with the member limited to the
/// inputs before it, so every run executes the same inputs in the same order; the failing input is then
/// replayed from clean state (preceding members and inputs included), twice per build. A build that fails
/// where the other finishes quickly is a difference (HANG or CRASH); a failure both builds reproduce is
/// TIMEOUT (the prefix is still compared); disagreeing replays are NONDETERMINISTIC; a replay that never
/// reached the input is INCONCLUSIVE. AGREE means no disagreement on this finite input set; it is not a proof.</para>
/// </summary>
public static partial class MemberDifferentialOracle
{
    private const string SetupFailed = "setup-failed";

    private sealed class TimeoutRestart(string key, int completed) : Exception($"{key} timed out after {completed} inputs")
    {
        public string Key { get; } = key;
        public int Completed { get; } = completed;
    }

    /// <summary>The plan both builds share, plus the surface verdicts (members, hierarchy) that differ.</summary>
    private static (List<MemberVerdict> Surface, List<PlannedMember> Plan) SharedPlan(ArmImage original, ArmImage candidate,
        IReadOnlyCollection<string> typeScope, OracleOptions options)
    {
        var verdicts = new List<MemberVerdict>();
        using var o = original.Load("plan");
        using var c = candidate.Load("plan");
        var plan = OraclePlanner.Plan(o.Assembly, typeScope, options, chooseReceivers: true);
        var candidatePlan = new Dictionary<string, PlannedMember>(StringComparer.Ordinal);
        foreach (var m in OraclePlanner.Plan(c.Assembly, typeScope, options, chooseReceivers: false))
            candidatePlan.TryAdd(m.Key, m);
        var originalKeys = plan.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var extra in candidatePlan.Values.Where(m => !originalKeys.Contains(m.Key)))
            verdicts.Add(Surface(extra, "public member present only in the converted build"));
        var shared = new List<PlannedMember>();
        foreach (var m in plan)
        {
            if (!candidatePlan.TryGetValue(m.Key, out var other))
                verdicts.Add(Surface(m, "public member present only in the original build"));
            else if (m.Kind == "hierarchy" && other.Hierarchy != m.Hierarchy)
                verdicts.Add(Surface(m, $"original {m.Hierarchy} | converted {other.Hierarchy}"));
            else
                shared.Add(m);
        }
        return (verdicts, shared);
    }

    public static List<MemberVerdict> Compare(ArmImage original, ArmImage candidate, IReadOnlyCollection<string> typeScope, OracleOptions options)
    {
        var (verdicts, plan) = SharedPlan(original, candidate, typeScope, options);
        var runnable = new List<PlannedMember>();
        foreach (var member in plan.Where(m => m.Kind != "hierarchy"))
        {
            if (member.SkipReason is not null)
                verdicts.Add(new MemberVerdict { Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Skipped, Reason = member.SkipReason });
            else
                runnable.Add(member);
        }

        // In-process mode restarts here on a timeout; child mode exits and the parent restarts the process.
        var limits = new Dictionary<string, int>(options.Limits, StringComparer.Ordinal);
        Dictionary<string, MemberRun>[] runs;
        while (true)
        {
            try
            {
                var o = options with { Limits = limits };
                runs = [Run(original, "run1", runnable, o), Run(candidate, "run1", runnable, o), Run(original, "run2", runnable, o), Run(candidate, "run2", runnable, o)];
                break;
            }
            catch (TimeoutRestart restart)
            {
                limits[restart.Key] = restart.Completed;
            }
        }

        foreach (var member in runnable)
        {
            var verdict = Judge(member, runs.Select(r => r[member.Key]).ToArray(), limits.TryGetValue(member.Key, out var k) ? k : null, options);
            if (verdict.LimitedAt is { } at && options.OnTimeout is null)
            {
                // In-process: attribute the timeout now. (Child mode leaves it to the parent, which replays in fresh processes.)
                var o = options with { Limits = limits };
                verdict = ClassifyTimeout(verdict,
                    [Probe(original, candidate, typeScope, member.Key, at, onCandidate: false, o), Probe(original, candidate, typeScope, member.Key, at, onCandidate: false, o)],
                    [Probe(original, candidate, typeScope, member.Key, at, onCandidate: true, o), Probe(original, candidate, typeScope, member.Key, at, onCandidate: true, o)],
                    options);
            }
            verdicts.Add(verdict);
        }
        return verdicts.OrderBy(v => v.Type, StringComparer.Ordinal).ThenBy(v => v.Member, StringComparer.Ordinal).ToList();
    }

    private static MemberVerdict Surface(PlannedMember m, string witness) => new()
    {
        Type = m.Type, Member = m.Member, Kind = m.Kind, Status = MemberStatus.Different, Reason = "SURFACE",
        Witnesses = [witness], Fingerprint = Hash("SURFACE\u0001" + m.Key + "\u0001" + witness),
    };

    private static bool NotEvidence(string a, string b) =>
        a == b && (a.StartsWith(SetupFailed, StringComparison.Ordinal) || StateRenderer.IsIncompleteObservation(a));

    /// <summary>The verdict for one member from its four runs (original 1, converted 1, original 2, converted 2).</summary>
    private static MemberVerdict Judge(PlannedMember member, MemberRun[] r, int? limitedAt, OracleOptions options)
    {
        var verdict = new MemberVerdict { Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Agree, LimitedAt = limitedAt };
        var n = r.Min(x => x.Completed);
        var (a1, b1, a2, b2) = (r[0].Observations, r[1].Observations, r[2].Observations, r[3].Observations);
        var stable = Enumerable.Range(0, n).Where(i => a1[i] == a2[i] && b1[i] == b2[i]).ToList();
        var executed = stable.Where(i => !NotEvidence(a1[i], b1[i])).ToList();
        var differing = executed.Where(i => a1[i] != b1[i])
            .Select(i => (Label: MemberOracleInputs.Render(member.Inputs[i]), A: a1[i], B: b1[i])).ToList();

        // Every project type's public statics after the member: a change made to another type shows here.
        var afterStable = r.All(x => x.After is not null) && r[0].After == r[2].After && r[1].After == r[3].After;
        if (afterStable && r[0].After != r[1].After && !NotEvidence(r[0].After!, r[1].After!))
            differing.Add(("<project statics after the member>", r[0].After!, r[1].After!));
        if (differing.Count > 0)
        {
            return verdict with
            {
                Status = MemberStatus.Different, Reason = "OBSERVATION", Inputs = executed.Count,
                Witnesses = differing.Take(options.MaxWitnesses).Select(d => Witness(member.Member, d.Label, d.A, d.B, "original", "converted")).ToList(),
                Fingerprint = Hash(string.Join("\u0000", differing.Select(d => $"{d.Label}\u0001{d.A}\u0002{d.B}"))),
            };
        }
        var unstable = Enumerable.Range(0, n).Except(stable).ToList();
        if (unstable.Count > 0 || (r.All(x => x.After is not null) && !afterStable))
        {
            var u = unstable.Count > 0 ? unstable[0] : -1;
            var originalUnstable = u >= 0 ? a1[u] != a2[u] : r[0].After != r[2].After;
            return verdict with
            {
                Status = MemberStatus.Nondeterministic, Inputs = executed.Count,
                Reason = $"a build disagreed with its own re-run on {unstable.Count} of {n} inputs{(afterStable ? "" : " (or on project statics after the member)")}; the {executed.Count} stable inputs agree",
                Witnesses = [u >= 0
                    ? Witness(member.Member, MemberOracleInputs.Render(member.Inputs[u]), originalUnstable ? a1[u] : b1[u], originalUnstable ? a2[u] : b2[u], "run1", "run2")
                    : Witness(member.Member, "<project statics after the member>", originalUnstable ? r[0].After! : r[1].After!, originalUnstable ? r[2].After! : r[3].After!, "run1", "run2")],
            };
        }
        var notRun = n - executed.Count;
        if (limitedAt is not null)
            return verdict with { Status = MemberStatus.Timeout, Inputs = executed.Count, Reason = $"input {limitedAt} timed out or crashed; the {executed.Count} inputs before it agree" };
        if (executed.Count == 0)
            return verdict with { Status = MemberStatus.Skipped, Reason = "no input could be compared: setup or observation failed, or the observation was incomplete, on both builds" };
        return verdict with
        {
            Inputs = executed.Count,
            Reason = notRun > 0 ? $"{notRun} of {n} inputs not compared (setup failed or observation incomplete on both builds)" : null,
        };
    }

    /// <summary>
    /// Final verdict for a member limited at input k, from two clean-state replays of input k per build. The replays are
    /// first checked for consistency: a build whose two replays disagree makes the member NONDETERMINISTIC. A build whose
    /// replays both fail (hang or crash) while the other's both finish within a quarter of the budget is a HANG or CRASH
    /// difference; replays that both finish are compared like any input. A replay that never reached the input is
    /// INCONCLUSIVE. When the prefix already differs, the replay outcome is folded into its fingerprint, so a baseline
    /// entry stops matching if the behavior at input k changes.
    /// </summary>
    public static MemberVerdict ClassifyTimeout(MemberVerdict prefix, ProbeResult[] original, ProbeResult[] converted, OracleOptions options)
    {
        if (prefix.LimitedAt is not { } k)
            return prefix;
        var (outcome, classified) = Classify(prefix, k, original, converted, options);
        if (prefix.Status == MemberStatus.Different)
            return prefix with { Fingerprint = Hash(prefix.Fingerprint + "\u0003" + outcome), Witnesses = [.. prefix.Witnesses, $"input #{k}: {outcome}"] };
        return classified;
    }

    private static (string Outcome, MemberVerdict Verdict) Classify(MemberVerdict prefix, int k, ProbeResult[] original, ProbeResult[] converted, OracleOptions options)
    {
        var budget = options.MemberTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        string Describe(ProbeResult[] p) => string.Join("/", p.Select(x => x.Outcome + (x.Finished ? $"({StateRenderer.Invariant(x.Seconds)}s)" : "")));
        var summary = $"original {Describe(original)}, converted {Describe(converted)}";
        if (original.Concat(converted).Any(p => p.Outcome == ProbeOutcome.Infrastructure))
            return ("infrastructure:" + summary, prefix with { Status = MemberStatus.Inconclusive, Reason = $"input {k}: a replay never reached the input ({summary})" });
        bool Consistent(ProbeResult[] p) => p.All(x => x.Outcome == p[0].Outcome && (!x.Finished || x.Observation == p[0].Observation));
        if (!Consistent(original) || !Consistent(converted))
            return ("nondeterministic:" + summary, prefix with { Status = MemberStatus.Nondeterministic, Reason = $"input {k} timed out or crashed, and its replays disagree ({summary})" });
        var quick = options.MemberTimeout.TotalSeconds / 4;
        bool Quick(ProbeResult[] p) => p.All(x => x.Finished && x.Seconds < quick);
        var (o, c) = (original[0], converted[0]);
        if (!c.Finished && Quick(original) || !o.Finished && Quick(converted))
        {
            var failing = !c.Finished ? c : o;
            var reason = failing.Outcome == ProbeOutcome.Crashed ? "CRASH" : "HANG";
            var arm = !c.Finished ? "converted" : "original";
            return ($"{reason}:{arm}", prefix with
            {
                Status = MemberStatus.Different, Reason = reason, Fingerprint = Hash($"{reason}\u0001{prefix.Key}\u0001{k}\u0001{arm}"),
                Witnesses = [$"{prefix.Member} input #{k} (replayed from clean state, twice per build): {summary}; budget {budget}s"],
            });
        }
        if (o.Finished && c.Finished && o.Observation != c.Observation)
            return ($"differs:{Hash(o.Observation + "\u0002" + c.Observation)}", prefix with
            {
                Status = MemberStatus.Different, Reason = "OBSERVATION",
                Witnesses = [Witness(prefix.Member, $"input #{k} (replayed from clean state)", o.Observation!, c.Observation!, "original", "converted")],
                Fingerprint = Hash($"{k}\u0001{o.Observation}\u0002{c.Observation}"),
            });
        return (o.Finished && c.Finished ? "agrees" : $"both-fail:{o.Outcome}/{c.Outcome}", prefix with
        {
            Reason = !o.Finished && !c.Finished
                ? $"input {k} {o.Outcome} on both builds (replayed from clean state); the {prefix.Inputs} inputs before it agree"
                : $"input {k} failed in the run but not when replayed ({summary}); the {prefix.Inputs} inputs before it agree",
        });
    }

    internal static string Hash(string? s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s ?? "")))[..32];

    /// <summary>The input and the region of the two observations that differs.</summary>
    private static string Witness(string member, string input, string l, string r, string leftName, string rightName)
    {
        var start = 0;
        while (start < l.Length && start < r.Length && l[start] == r[start])
            start++;
        var suffix = 0;
        while (suffix < l.Length - start && suffix < r.Length - start && l[^(suffix + 1)] == r[^(suffix + 1)])
            suffix++;
        string Excerpt(string s)
        {
            var from = Math.Max(0, start - 40);
            var to = Math.Min(s.Length, Math.Min(s.Length - suffix + 40, from + 400));
            return (from > 0 ? "…" : "") + s[from..to] + (to < s.Length ? "…" : "");
        }
        return $"{member} {input}: {leftName} {Excerpt(l)} | {rightName} {Excerpt(r)}";
    }
}
