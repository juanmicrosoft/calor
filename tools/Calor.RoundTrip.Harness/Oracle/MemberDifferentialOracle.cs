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
/// static state of every project type after each input; static state after type initialization; enum
/// values; and the surface (members, kind, visibility, base chain and interfaces). A member present in only
/// one build is a difference.
///
/// <para>Each build runs twice, every run in a fresh collectible load context, in the order original,
/// converted, original, converted, so each run starts from clean static state. Each observation has named
/// parts (result, args, this, statics, project, stdout, files), compared one by one: a part counts only when
/// both builds reproduce it on re-run, and a nondeterministic part is set aside alone, so it never hides a
/// stable difference in another part. The member is NONDETERMINISTIC only when no part of any input was
/// reproducible. A setup (receiver, arguments) or observation failure identical on both builds, or a part
/// rendered incompletely on both, is not counted as compared.</para>
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

    /// <summary>Separates an observation's parts; a part name never contains the separators.</summary>
    internal const char PartSeparator = '\u001e', NameSeparator = '\u001f';

    /// <summary>The named parts of one observation: result, args, this, statics, project, stdout, files (or setup).</summary>
    internal static Dictionary<string, string> Parts(string observation)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in observation.Split(PartSeparator))
        {
            var cut = part.IndexOf(NameSeparator);
            parts[cut < 0 ? "observation" : part[..cut]] = cut < 0 ? part : part[(cut + 1)..];
        }
        return parts;
    }

    /// <summary>
    /// Compares one input's observations part by part. A part counts when it is stable (each build reproduces it on
    /// re-run); a stable part that differs between the builds is a difference, whatever the other parts do. An unstable
    /// part is set aside alone, so nondeterminism in one part (a <c>Guid.NewGuid()</c> static, say) never hides a stable
    /// difference in another. An input counts as compared when its primary part (the member's result, or a type's
    /// statics) is reproducible and complete on both builds; a setup failure identical on both builds is not evidence.
    /// A part rendered identically but incompletely is reported, because it can hide a difference.
    /// </summary>
    internal static (List<(string Part, string A, string B)> Differing, List<string> Unstable, bool Compared, List<string> Incomplete) CompareParts(
        string a1, string a2, string b1, string b2)
    {
        var (pa1, pa2, pb1, pb2) = (Parts(a1), Parts(a2), Parts(b1), Parts(b2));
        var differing = new List<(string, string, string)>();
        var unstable = new List<string>();
        var incomplete = new List<string>();
        var compared = false;
        // The member's own outcome: it must be reproducible and complete for the input to count as compared (other parts,
        // such as an empty stdout, are always stable and prove nothing on their own).
        var all = new[] { pa1, pa2, pb1, pb2 };
        var primary = all.Any(d => d.ContainsKey("result")) ? "result" : all.Any(d => d.ContainsKey("statics")) ? "statics" : null;
        foreach (var name in all.SelectMany(d => d.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            string V(Dictionary<string, string> d) => d.TryGetValue(name, out var v) ? v : "<absent>";
            var (x1, x2, y1, y2) = (V(pa1), V(pa2), V(pb1), V(pb2));
            if (x1 != x2 || y1 != y2)
            {
                unstable.Add(name);
                continue;
            }
            if (x1 != y1)
                differing.Add((name, x1, y1));
            else if (StateRenderer.IsIncompleteObservation(x1))
                incomplete.Add(name);
            else if (name == primary)
                compared = true;
        }
        return (differing, unstable, compared || differing.Count > 0, incomplete);
    }

    /// <summary>The verdict for one member from its four runs (original 1, converted 1, original 2, converted 2).</summary>
    private static MemberVerdict Judge(PlannedMember member, MemberRun[] r, int? limitedAt, OracleOptions options)
    {
        var verdict = new MemberVerdict { Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Agree, LimitedAt = limitedAt };
        var n = r.Min(x => x.Completed);
        var (a1, b1, a2, b2) = (r[0].Observations, r[1].Observations, r[2].Observations, r[3].Observations);
        var differing = new List<(string Label, string A, string B)>();
        var unstableParts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        (string Label, string Part, string X, string Y)? firstUnstable = null;
        var executed = 0;
        var incompleteParts = new SortedSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            var label = MemberOracleInputs.Render(member.Inputs[i]);
            var (diff, unstable, compared, incomplete) = CompareParts(a1[i], a2[i], b1[i], b2[i]);
            incompleteParts.UnionWith(incomplete);
            differing.AddRange(diff.Select(d => ($"{label} [{d.Part}]", d.A, d.B)));
            foreach (var part in unstable)
            {
                unstableParts[part] = unstableParts.GetValueOrDefault(part) + 1;
                if (firstUnstable is null)
                {
                    var (x1, x2) = (Parts(a1[i]).GetValueOrDefault(part, "<absent>"), Parts(a2[i]).GetValueOrDefault(part, "<absent>"));
                    firstUnstable = x1 != x2 ? ($"{label} [{part}]", part, x1, x2)
                        : ($"{label} [{part}]", part, Parts(b1[i]).GetValueOrDefault(part, "<absent>"), Parts(b2[i]).GetValueOrDefault(part, "<absent>"));
                }
            }
            if (compared)
                executed++;
        }
        var unstableNote = unstableParts.Count == 0 ? null
            : "nondeterministic parts set aside: " + string.Join(", ", unstableParts.Select(kv => $"{kv.Key} on {kv.Value} input(s)"));
        if (differing.Count > 0)
        {
            return verdict with
            {
                Status = MemberStatus.Different, Reason = "OBSERVATION" + (unstableNote is null ? "" : "; " + unstableNote), Inputs = executed,
                Witnesses = differing.Take(options.MaxWitnesses).Select(d => Witness(member.Member, d.Label, d.A, d.B, "original", "converted")).ToList(),
                Fingerprint = Hash(string.Join("\u0000", differing.Select(d => $"{d.Label}\u0001{d.A}\u0002{d.B}"))),
            };
        }
        // A part that could not be rendered in full may hide a difference: the member is never AGREE and never counts as
        // compared coverage (any stable difference elsewhere has already failed above).
        if (incompleteParts.Count > 0)
        {
            return verdict with
            {
                Status = MemberStatus.Skipped, Inputs = 0,
                Reason = "incomplete observation: the " + string.Join(", ", incompleteParts) + " part could not be rendered in full on either build",
            };
        }
        if (executed == 0 && firstUnstable is { } u)
        {
            return verdict with
            {
                Status = MemberStatus.Nondeterministic, Inputs = 0,
                Reason = $"no part of any input was reproducible on both builds ({unstableNote})",
                Witnesses = [Witness(member.Member, u.Label, u.X, u.Y, "run1", "run2")],
            };
        }
        if (limitedAt is not null)
            return verdict with { Status = MemberStatus.Timeout, Inputs = executed, Reason = $"input {limitedAt} timed out or crashed; the {executed} inputs before it agree" + (unstableNote is null ? "" : $" ({unstableNote})") };
        if (executed == 0)
            return verdict with { Status = MemberStatus.Skipped, Reason = "no input could be compared: setup or observation failed, or the observation was incomplete, on both builds" };
        var notRun = n - executed;
        var notes = new[] { notRun > 0 ? $"{notRun} of {n} inputs not compared (setup failed or observation incomplete on both builds)" : null, unstableNote }
            .Where(x => x is not null).ToList();
        return verdict with { Inputs = executed, Reason = notes.Count == 0 ? null : string.Join("; ", notes) };
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
        // A replay that never reached the input is inconclusive even when the prefix already differs: the gate fails it
        // whatever the conversion recorded.
        if (classified.Status == MemberStatus.Inconclusive)
            return classified with { Witnesses = [.. prefix.Witnesses, $"input #{k}: {outcome}"] };
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
        // All four replays finished: compare part by part first. A nondeterministic part (a Guid return, say) is set aside
        // alone; a stable difference in any other part (arguments, receiver, statics, console, files) fails.
        if (original.Concat(converted).All(p => p.Finished)
            && CompareParts(original[0].Observation!, original[1].Observation!, converted[0].Observation!, converted[1].Observation!) is
                { Differing: { Count: > 0 } stableDifferences })
        {
            var evidence = string.Join("\u0000", stableDifferences.Select(d => $"{d.Part}\u0001{d.A}\u0002{d.B}"));
            return ($"differs:{Hash(evidence)}", prefix with
            {
                Status = MemberStatus.Different, Reason = "OBSERVATION",
                Witnesses = [.. stableDifferences.Take(options.MaxWitnesses).Select(d => Witness(prefix.Member, $"input #{k} [{d.Part}] (replayed from clean state)", d.A, d.B, "original", "converted"))],
                Fingerprint = Hash($"{k}\u0001" + evidence),
            });
        }
        // A build's replays must reproduce their termination and their primary part (the member's result); other parts
        // may be noisy (a Guid static) and are compared part by part below, so they neither block HANG/CRASH attribution
        // nor hide a stable difference.
        static string Primary(string? observation)
        {
            if (observation is null)
                return "";
            var parts = Parts(observation);
            return parts.TryGetValue("result", out var r) ? r : parts.TryGetValue("statics", out var st) ? st : observation;
        }
        bool Consistent(ProbeResult[] p) => p.All(x => x.Outcome == p[0].Outcome && (!x.Finished || Primary(x.Observation) == Primary(p[0].Observation)));
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
