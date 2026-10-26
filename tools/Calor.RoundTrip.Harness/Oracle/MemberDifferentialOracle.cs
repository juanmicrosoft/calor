using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: the member-level differential oracle. Given two builds of the same subject (the original, and one
/// with a single file swapped for its round-tripped C#), it drives the members <see cref="OraclePlanner"/>
/// plans on both and compares what it observes: construction state to <see cref="OracleOptions.Depth"/>;
/// return value, thrown exception (type, message, data and state), argument state after the call (including
/// <c>ref</c>/<c>out</c>), the receiver's state, the type's public static state, standard output and error, and
/// created files and directories; the public and private static state of every project type after each input;
/// static state after type initialization; enum
/// values; and the surface (members, kind, visibility, base chain and interfaces). A member present in only
/// one build is a difference.
///
/// <para>Each build runs twice, every run in a fresh collectible load context, in the order original,
/// converted, original, converted, so each run starts from clean static state. Each observation has named
/// parts (result, args, this, statics, project, private-statics, stdout, stderr, files; see README.md),
/// compared one by one: a part counts only when both builds reproduce it on re-run, and a part neither build
/// reproduces is set aside alone, so it never hides a stable difference in another part. A part one build
/// reproduces on every input and the other does not is a difference (#1667). The member is NONDETERMINISTIC
/// only when no part of any input was reproducible. A setup (receiver, arguments) or observation failure identical on both builds, or a part
/// rendered incompletely on both, is not counted as compared.</para>
///
/// <para>A member input that times out or crashes is handled by restarting with the member limited to the
/// inputs before it, so every run executes the same inputs in the same order; the failing input is then
/// replayed from clean state (preceding members and inputs included), twice per build. A build that fails
/// where the other finishes quickly is a difference (HANG or CRASH); a failure both builds reproduce is
/// TIMEOUT (the prefix is still compared, and the inputs after the failing one run as a suffix segment);
/// a hang on one build and a crash on the other is a difference; disagreeing replays are NONDETERMINISTIC; a
/// replay that never reached the input is INCONCLUSIVE. AGREE means no disagreement on this finite input set; it is not a proof.</para>
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
        List<PlannedMember> expanded;
        while (true)
        {
            // A limited member gets a suffix segment for the inputs after its failing one (#1667).
            expanded = OraclePlanner.Expand(runnable, limits);
            try
            {
                var o = options with { Limits = limits };
                runs = [Run(original, "run1", expanded, o), Run(candidate, "run1", expanded, o), Run(original, "run2", expanded, o), Run(candidate, "run2", expanded, o)];
                break;
            }
            catch (TimeoutRestart restart)
            {
                limits[restart.Key] = restart.Completed;
            }
        }

        var judged = new List<(PlannedMember Member, MemberVerdict Verdict, bool Confirm)>();
        foreach (var member in expanded)
        {
            var verdict = Judge(member, runs.Select(r => r[member.Key]).ToArray(), limits.TryGetValue(member.Key, out var lim) ? lim : null, options, out var confirm);
            judged.Add((member, verdict, confirm));
        }
        if (judged.Any(j => j.Confirm))
        {
            // #1667: a part stable on one build and unstable on the other is a difference only when more runs bear it out
            // (two runs per build cannot tell a stable value from a low-entropy one that repeated by chance). The extra runs
            // repeat the whole plan, original then converted, so every member sees the same state as in the main runs.
            var extra = new List<Dictionary<string, MemberRun>>();
            try
            {
                var o = options with { Limits = limits, OnTimeout = null };
                for (var round = 1; round <= ConfirmationRounds; round++)
                {
                    extra.Add(Run(original, $"confirm{round}", expanded, o));
                    extra.Add(Run(candidate, $"confirm{round}", expanded, o));
                }
            }
            catch (TimeoutRestart)
            {
                extra.Clear();
            }
            for (var j = 0; j < judged.Count; j++)
            {
                var (member, verdict, confirm) = judged[j];
                if (!confirm)
                    continue;
                verdict = extra.Count == 0
                    ? verdict with
                    {
                        Status = MemberStatus.Inconclusive, Inputs = 0, Fingerprint = null, Witnesses = [],
                        Reason = "a confirmation run timed out; the provisional difference or one-sided instability could not be confirmed",
                    }
                    : Judge(member, runs.Concat(extra).Select(r => r[member.Key]).ToArray(), verdict.LimitedAt, options, out _);
                judged[j] = (member, verdict, false);
            }
        }

        foreach (var (member, judgedVerdict, _) in judged)
        {
            var verdict = judgedVerdict;
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

    /// <summary>The named parts of one observation: result, args, this, statics, project, private-statics, stdout, stderr, files (or setup).</summary>
    internal static Dictionary<string, string> Parts(string observation)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in observation.Split(PartSeparator))
        {
            var cut = part.IndexOf(NameSeparator);
            var name = cut < 0 ? "observation" : part[..cut];
            // A repeated name is kept under its own key, never overwriting the first (rendered text is escaped, so a
            // separator inside a part would itself be a defect, and must not be able to replace a part).
            for (var n = 2; parts.ContainsKey(name); n++)
                name = (cut < 0 ? "observation" : part[..cut]) + "#" + n;
            parts[name] = cut < 0 ? part : part[(cut + 1)..];
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
        string a1, string a2, string b1, string b2) =>
        CompareParts(a1, a2, b1, b2, out _);

    /// <summary>As above; <paramref name="sides"/> says, for each unstable part, which build failed to reproduce it.</summary>
    internal static (List<(string Part, string A, string B)> Differing, List<string> Unstable, bool Compared, List<string> Incomplete) CompareParts(
        string a1, string a2, string b1, string b2, out List<(string Part, bool OriginalStable, bool ConvertedStable)> sides)
    {
        var (pa1, pa2, pb1, pb2) = (Parts(a1), Parts(a2), Parts(b1), Parts(b2));
        var differing = new List<(string, string, string)>();
        var unstable = new List<string>();
        var incomplete = new List<string>();
        sides = [];
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
                sides.Add((name, x1 == x2, y1 == y2));
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

    /// <summary>Extra (original, converted) run pairs that confirm one-sided nondeterminism (#1667).</summary>
    internal const int ConfirmationRounds = 6;

    /// <summary>
    /// The verdict for one member from its runs: original 1, converted 1, original 2, converted 2, then any confirmation
    /// runs in the same alternation. <paramref name="needsConfirmation"/> is set when only the four main runs were given
    /// and a part looks stable on one build and unstable on the other: the caller then adds confirmation runs and judges
    /// again.
    /// </summary>
    private static MemberVerdict Judge(PlannedMember member, MemberRun[] r, int? limitedAt, OracleOptions options, out bool needsConfirmation)
    {
        needsConfirmation = false;
        var verdict = new MemberVerdict { Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Agree, LimitedAt = limitedAt, Origin = member.Origin };
        var n = r.Min(x => x.Completed);
        var (a1, b1, a2, b2) = (r[0].Observations, r[1].Observations, r[2].Observations, r[3].Observations);
        var differing = new List<(string Label, string A, string B)>();
        var unconfirmed = new SortedSet<string>(StringComparer.Ordinal);
        // Unconfirmed differences that only one build failed to reproduce: one-sided candidates found by the extra runs.
        var oneSidedFromConfirmation = new HashSet<(string Part, bool ConvertedUnstable)>();
        var unstableParts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        (string Label, string Part, string X, string Y)? firstUnstable = null;
        var executed = 0;
        var incompleteParts = new SortedSet<string>(StringComparer.Ordinal);
        // Per part: inputs on which each build failed to reproduce it (#1667, one-sided nondeterminism).
        var unstableOn = new SortedDictionary<string, (int Original, int Converted)>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            // The input's absolute index is part of its label, so two inputs that render alike (or a difference that moves to
            // another input) never share a witness or a fingerprint.
            var label = $"#{member.Offset + i} " + MemberOracleInputs.Render(member.Inputs[i]);
            var (diff, unstable, compared, incomplete) = CompareParts(a1[i], a2[i], b1[i], b2[i], out var sides);
            incompleteParts.UnionWith(incomplete);
            foreach (var (part, originalStable, convertedStable) in sides)
            {
                var entry = unstableOn.GetValueOrDefault(part);
                entry.Original += originalStable ? 0 : 1;
                entry.Converted += convertedStable ? 0 : 1;
                unstableOn[part] = entry;
            }
            foreach (var d in diff)
            {
                // With confirmation runs, a difference counts only when every run of each build reproduces the part: a
                // low-entropy value can repeat by chance in two runs per build (#1667).
                if (r.Length > 4 && (Reproduced(r, i, d.Part, original: true), Reproduced(r, i, d.Part, original: false)) is var (originalHolds, convertedHolds)
                    && !(originalHolds && convertedHolds))
                {
                    unconfirmed.Add(d.Part);
                    // A value one build kept and the other changed is still a candidate behavior change (one-sided).
                    if (originalHolds != convertedHolds)
                        oneSidedFromConfirmation.Add((d.Part, ConvertedUnstable: originalHolds));
                    unstableParts[d.Part] = unstableParts.GetValueOrDefault(d.Part) + 1;
                    continue;
                }
                differing.Add(($"{label} [{d.Part}]", d.A, d.B));
            }
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
        string? unstableNote = unstableParts.Count == 0 ? null
            : "nondeterministic parts set aside: " + string.Join(", ", unstableParts.Select(kv => $"{kv.Key} on {kv.Value} input(s)"));
        if (differing.Count > 0 && r.Length == 4)
            needsConfirmation = true;
        if (unconfirmed.Count > 0)
        {
            var note = $"a difference in {string.Join(", ", unconfirmed)} was not reproduced by {ConfirmationRounds} more runs per build";
            unstableNote = unstableNote is null ? note : unstableNote + "; " + note;
        }
        if (differing.Count > 0)
        {
            return verdict with
            {
                Status = MemberStatus.Different, Reason = "OBSERVATION" + (unstableNote is null ? "" : "; " + unstableNote), Inputs = executed,
                Witnesses = differing.Take(options.MaxWitnesses).Select(d => Witness(member.Member, d.Label, d.A, d.B, "original", "converted")).ToList(),
                Fingerprint = Hash(string.Join("\u0000", differing.Select(d => $"{d.Label}\u0001{d.A}\u0002{d.B}"))),
            };
        }
        // #1667: a part one build reproduces on every input while the other build does not reproduce it on some input. The
        // same code run in fresh contexts either is deterministic or is not; a conversion that turns a stable value into a
        // changing one (or the reverse) is a behavior change, not noise to set aside. It is confirmed on more runs first.
        var candidates = unstableOn.Where(kv => (kv.Value.Original == 0) != (kv.Value.Converted == 0))
            .Select(kv => (Part: kv.Key, ConvertedUnstable: kv.Value.Original == 0))
            .Union(oneSidedFromConfirmation).ToList();
        string? oneSidedNote = null;
        if (candidates.Count > 0 && r.Length == 4)
        {
            needsConfirmation = true;
        }
        else if (candidates.Count > 0)
        {
            if (ConfirmOneSided(member, r, n, candidates) is { } confirmed)
                return verdict with { Status = MemberStatus.Different, Inputs = executed, Reason = confirmed.Reason, Witnesses = confirmed.Witnesses, Fingerprint = confirmed.Fingerprint };
            oneSidedNote = $"one-sided instability in {string.Join(", ", candidates.Select(c => c.Part))} not confirmed by {ConfirmationRounds} more runs per build";
        }
        if (oneSidedNote is not null)
            unstableNote = unstableNote is null ? oneSidedNote : unstableNote + "; " + oneSidedNote;
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
            return verdict with
            {
                Status = MemberStatus.Timeout, Inputs = executed,
                Reason = $"input {limitedAt} of this segment (#{member.Offset + limitedAt}) timed out or crashed; the {executed} inputs before it agree"
                    + (member.Segment + 1 < OraclePlanner.MaxSegments && limitedAt + 1 < member.Inputs.Count ? "; the inputs after it run as the next segment" : "; no further segment")
                    + (unstableNote is null ? "" : $" ({unstableNote})"),
            };
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
        if (prefix.Status == MemberStatus.Inconclusive || prefix.LimitedAt is not { } k)
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
        string Describe(ProbeResult[] p) => string.Join("/", p.Select(x => x.Outcome
            + (x.Finished ? $"({StateRenderer.Invariant(x.Seconds)}s)" : x.ExitCode is { } code ? $"(exit {code})" : "")));
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
        bool Consistent(ProbeResult[] p) => p.All(x => x.Outcome == p[0].Outcome && x.ExitCode == p[0].ExitCode
            && (!x.Finished || Primary(x.Observation) == Primary(p[0].Observation)));
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
            var failure = $"{reason}:{arm}:exit={failing.ExitCode}";
            return (failure, prefix with
            {
                Status = MemberStatus.Different, Reason = reason, Fingerprint = Hash($"{failure}\u0001{prefix.Key}\u0001{k}"),
                Witnesses = [$"{prefix.Member} input #{k} (replayed from clean state, twice per build): {summary}; budget {budget}s"],
            });
        }
        // #1667: both builds fail, but differently (one hangs where the other crashes, reproduced twice each): the failure
        // itself changed, so it is a difference, not a shared timeout.
        if (!o.Finished && !c.Finished && (o.Outcome != c.Outcome || o.ExitCode != c.ExitCode))
        {
            return ($"FAILURE-MODE:{o.Outcome}{o.ExitCode}/{c.Outcome}{c.ExitCode}", prefix with
            {
                Status = MemberStatus.Different, Reason = "FAILURE-MODE",
                Fingerprint = Hash($"FAILURE-MODE\u0001{prefix.Key}\u0001{k}\u0001{o.Outcome}{o.ExitCode}\u0001{c.Outcome}{c.ExitCode}"),
                Witnesses = [$"{prefix.Member} input #{k} (replayed from clean state, twice per build): {summary}; budget {budget}s"],
            });
        }
        return (o.Finished && c.Finished ? "agrees" : $"both-fail:{o.Outcome}:exit={o.ExitCode}/{c.Outcome}:exit={c.ExitCode}", prefix with
        {
            Reason = !o.Finished && !c.Finished
                ? $"input {k} {o.Outcome} on both builds (replayed from clean state); the {prefix.Inputs} inputs before it agree"
                : $"input {k} failed in the run but not when replayed ({summary}); the {prefix.Inputs} inputs before it agree",
        });
    }

    /// <summary>
    /// #1667: confirms one-sided nondeterminism over every run (main and confirmation). A candidate part is confirmed when
    /// the stable build rendered it identically on every one of its runs, for every input, and on some input the other
    /// build produced at least two different values, none of them the stable build's. A low-entropy value that merely
    /// repeated (a coin flip) fails the first test on more runs, or produces the stable value on the other build. The
    /// fingerprint covers every affected input of every confirmed part (index, label, direction, stable value), never the
    /// changing values, so it is reproducible and a newly affected input changes it.
    /// </summary>
    /// <summary>Whether every run of one build (the even runs are the original's) rendered part <paramref name="part"/> of input <paramref name="i"/> alike.</summary>
    private static bool Reproduced(MemberRun[] r, int i, string part, bool original) =>
        r.Where((_, j) => (j % 2 == 0) == original).Select(x => Parts(x.Observations[i]).GetValueOrDefault(part, "<absent>")).Distinct(StringComparer.Ordinal).Count() == 1;

    private static (string Reason, List<string> Witnesses, string Fingerprint)? ConfirmOneSided(PlannedMember member, MemberRun[] r, int n,
        List<(string Part, bool ConvertedUnstable)> candidates)
    {
        var confirmed = new List<(string Part, bool ConvertedUnstable, List<(string Label, string Stable, List<string> Values)> Affected)>();
        foreach (var (part, convertedUnstable) in candidates)
        {
            var stableRuns = r.Where((_, j) => (j % 2 == 1) != convertedUnstable).ToArray();
            var unstableRuns = r.Where((_, j) => (j % 2 == 1) == convertedUnstable).ToArray();
            var affected = new List<(string, string, List<string>)>();
            var holds = true;
            for (var i = 0; i < n && holds; i++)
            {
                var stable = stableRuns.Select(x => Parts(x.Observations[i]).GetValueOrDefault(part, "<absent>")).Distinct(StringComparer.Ordinal).ToList();
                if (stable.Count != 1)
                {
                    holds = false;
                    continue;
                }
                var values = unstableRuns.Select(x => Parts(x.Observations[i]).GetValueOrDefault(part, "<absent>")).Distinct(StringComparer.Ordinal).ToList();
                if (values.Count >= 2 && !values.Contains(stable[0], StringComparer.Ordinal))
                    affected.Add(($"#{member.Offset + i} " + MemberOracleInputs.Render(member.Inputs[i]), stable[0], values));
            }
            if (holds && affected.Count > 0)
                confirmed.Add((part, convertedUnstable, affected));
        }
        if (confirmed.Count == 0)
            return null;
        string Arm(bool convertedUnstable, bool stable) => convertedUnstable == stable ? "original" : "converted";
        var first = confirmed[0];
        var reason = "ONE-SIDED-NONDETERMINISM: " + string.Join("; ", confirmed.Select(c =>
            $"the {c.Part} part is reproducible on every run of the {Arm(c.ConvertedUnstable, true)} build but changes on {c.Affected.Count} input(s) of the {Arm(c.ConvertedUnstable, false)} build"));
        var witnesses = confirmed.Select(c =>
            $"{member.Member} {c.Affected[0].Label} [{c.Part}]: {Arm(c.ConvertedUnstable, true)} (every run) {Excerpt(c.Affected[0].Stable)} | "
            + $"{Arm(c.ConvertedUnstable, false)} {string.Join(" / ", c.Affected[0].Values.Take(2).Select(Excerpt))}").ToList();
        var fingerprint = Hash("ONE-SIDED\u0001" + string.Join("\u0000", confirmed.SelectMany(c =>
            c.Affected.Select(a => $"{c.Part}\u0001{Arm(c.ConvertedUnstable, false)}\u0001{a.Label}\u0001{a.Stable}"))));
        return (reason, witnesses, fingerprint);
    }

    private static string Excerpt(string s) => s.Length <= 200 ? s : s[..200] + "…";

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
