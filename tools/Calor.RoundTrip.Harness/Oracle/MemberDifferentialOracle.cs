using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: the member-level differential oracle. Given two builds of the same subject (the original, and one
/// with a single file swapped for its round-tripped C#), it drives the members <see cref="OraclePlanner"/>
/// plans on both and compares what it observes: construction state to <see cref="OracleOptions.Depth"/>;
/// return value, thrown exception type, argument state after the call (including <c>ref</c>/<c>out</c>),
/// the receiver's state, the type's public static state, console output and created files; static state
/// after type initialization; enum values; and the surface (members, kind, visibility, base chain and
/// interfaces). A member present in only one build is a difference.
///
/// <para>Each build runs twice, every run in a fresh collectible load context, in the order original,
/// converted, original, converted, so each run starts from clean static state. An input is compared only
/// when both builds reproduce it on re-run; the member is NONDETERMINISTIC only when no stable input
/// differs. An input whose receiver could not be constructed on either build is not counted as run.</para>
///
/// <para>On a timeout, the comparison restarts with that member limited to the inputs before the one that
/// hung, so every run executes the same inputs in the same order. The hanging input is then probed from
/// clean state, twice per build: a build that hangs where the other finishes quickly is a HANG difference;
/// a hang both builds reproduce is TIMEOUT (the prefix is still compared); an inconsistent probe is
/// NONDETERMINISTIC. AGREE means no disagreement on this finite input set; it is not a proof.</para>
/// </summary>
public static class MemberDifferentialOracle
{
    private const string SetupFailed = "setup-failed";

    private sealed class TimeoutRestart(string key, int completed) : Exception($"{key} timed out after {completed} inputs")
    {
        public string Key { get; } = key;
        public int Completed { get; } = completed;
    }

    public static List<MemberVerdict> Compare(ArmImage original, ArmImage candidate, IReadOnlyCollection<string> typeScope, OracleOptions options)
    {
        var verdicts = new List<MemberVerdict>();
        List<PlannedMember> plan;
        using (var o = original.Load("plan"))
        using (var c = candidate.Load("plan"))
        {
            plan = OraclePlanner.Plan(o.Assembly, typeScope, options, chooseReceivers: true);
            var candidatePlan = OraclePlanner.Plan(c.Assembly, typeScope, options, chooseReceivers: false).ToDictionary(m => m.Key, StringComparer.Ordinal);
            var originalKeys = plan.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var extra in candidatePlan.Values.Where(m => !originalKeys.Contains(m.Key)))
                verdicts.Add(Surface(extra, "public member present only in the converted build"));
            foreach (var member in plan.Where(m => candidatePlan.ContainsKey(m.Key) && m.Kind == "hierarchy"
                && candidatePlan[m.Key].Hierarchy != m.Hierarchy))
                verdicts.Add(Surface(member, $"original {member.Hierarchy} | converted {candidatePlan[member.Key].Hierarchy}"));
            plan = plan.Where(m =>
            {
                if (!candidatePlan.ContainsKey(m.Key))
                {
                    verdicts.Add(Surface(m, "public member present only in the original build"));
                    return false;
                }
                return true;
            }).ToList();
        }

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
                runs = [Run(original, "run1", runnable, options, limits), Run(candidate, "run1", runnable, options, limits),
                        Run(original, "run2", runnable, options, limits), Run(candidate, "run2", runnable, options, limits)];
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
            if (verdict.LimitedAt is { } at && verdict.Status != MemberStatus.Different && options.OnTimeout is null)
            {
                // In-process: attribute the timeout now. (Child mode leaves it to the parent, which probes in fresh processes.)
                var probes = (Original: new[] { Probe(original, original, typeScope, member.Key, at, options), Probe(original, original, typeScope, member.Key, at, options) },
                    Converted: new[] { Probe(original, candidate, typeScope, member.Key, at, options), Probe(original, candidate, typeScope, member.Key, at, options) });
                verdict = ClassifyTimeout(verdict, probes.Original, probes.Converted, options);
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

    /// <summary>The verdict for one member from its four runs (original 1, converted 1, original 2, converted 2).</summary>
    private static MemberVerdict Judge(PlannedMember member, MemberRun[] r, int? limitedAt, OracleOptions options)
    {
        var verdict = new MemberVerdict { Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Agree, LimitedAt = limitedAt };
        var n = r.Min(x => x.Completed);
        var (a1, b1, a2, b2) = (r[0].Observations, r[1].Observations, r[2].Observations, r[3].Observations);
        var stable = Enumerable.Range(0, n).Where(i => a1[i] == a2[i] && b1[i] == b2[i]).ToList();
        // An input whose receiver failed identically on both builds never reached the member: it is not a run input.
        var executed = stable.Where(i => !(a1[i] == b1[i] && a1[i].StartsWith(SetupFailed, StringComparison.Ordinal))).ToList();
        var differing = executed.Where(i => a1[i] != b1[i]).ToList();
        if (differing.Count > 0)
        {
            return verdict with
            {
                Status = MemberStatus.Different, Reason = "OBSERVATION", Inputs = executed.Count,
                Witnesses = differing.Take(options.MaxWitnesses).Select(i => Witness(member, i, a1[i], b1[i], "original", "converted")).ToList(),
                Fingerprint = Hash(string.Join("\u0000", differing.Select(i => $"{i}\u0001{a1[i]}\u0002{b1[i]}"))),
            };
        }
        var unstable = Enumerable.Range(0, n).Except(stable).ToList();
        if (unstable.Count > 0)
        {
            var u = unstable[0];
            var originalUnstable = a1[u] != a2[u];
            return verdict with
            {
                Status = MemberStatus.Nondeterministic, Inputs = executed.Count,
                Reason = $"a build disagreed with its own re-run on {unstable.Count} of {n} inputs; the {executed.Count} stable inputs agree",
                Witnesses = [Witness(member, u, originalUnstable ? a1[u] : b1[u], originalUnstable ? a2[u] : b2[u], "run1", "run2")],
            };
        }
        if (limitedAt is not null)
            return verdict with { Status = MemberStatus.Timeout, Inputs = executed.Count, Reason = $"input {limitedAt} timed out; the {executed.Count} inputs before it agree" };
        if (executed.Count == 0)
            return verdict with { Status = MemberStatus.Skipped, Reason = "no input ran: the receiver could not be constructed on either build" };
        return verdict with
        {
            Inputs = executed.Count,
            Reason = executed.Count < n ? $"{n - executed.Count} of {n} inputs not run (receiver setup failed on both builds)" : null,
        };
    }

    /// <summary>
    /// Final verdict for a member limited at input k (its prefix verdict is not DIFFERENT), from two clean-state probes
    /// of input k per build. A build that hangs on both probes while the other finishes both within a quarter of the
    /// budget is a HANG difference. Probes that both finish are compared like any input.
    /// </summary>
    public static MemberVerdict ClassifyTimeout(MemberVerdict prefix, ProbeResult[] original, ProbeResult[] converted, OracleOptions options)
    {
        if (prefix.Status == MemberStatus.Different || prefix.LimitedAt is not { } k)
            return prefix;
        var quick = options.MemberTimeout.TotalSeconds / 4;
        bool Hung(ProbeResult[] p) => p.All(x => !x.Finished);
        bool Quick(ProbeResult[] p) => p.All(x => x.Finished && x.Seconds < quick);
        bool Consistent(ProbeResult[] p) => p.All(x => x.Finished == p[0].Finished && x.Observation == p[0].Observation);
        var budget = options.MemberTimeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        if (Hung(converted) && Quick(original))
            return prefix with
            {
                Status = MemberStatus.Different, Reason = "HANG", Fingerprint = Hash($"HANG\u0001{prefix.Key}\u0001{k}\u0001converted"),
                Witnesses = [$"{prefix.Member} input #{k}: original finished in {original.Max(x => x.Seconds):0.00}s (twice, from clean state) | converted did not finish within {budget}s (twice)"],
            };
        if (Hung(original) && Quick(converted))
            return prefix with
            {
                Status = MemberStatus.Different, Reason = "HANG", Fingerprint = Hash($"HANG\u0001{prefix.Key}\u0001{k}\u0001original"),
                Witnesses = [$"{prefix.Member} input #{k}: original did not finish within {budget}s (twice, from clean state) | converted finished in {converted.Max(x => x.Seconds):0.00}s (twice)"],
            };
        if (!Consistent(original) || !Consistent(converted))
            return prefix with { Status = MemberStatus.Nondeterministic, Reason = $"input {k} timed out, and its clean-state probes disagree with each other" };
        if (original[0].Finished && converted[0].Finished && original[0].Observation != converted[0].Observation)
            return prefix with
            {
                Status = MemberStatus.Different, Reason = "OBSERVATION",
                Witnesses = [$"{prefix.Member} input #{k} (clean state): original {original[0].Observation} | converted {converted[0].Observation}"],
                Fingerprint = Hash($"{k}\u0001{original[0].Observation}\u0002{converted[0].Observation}"),
            };
        return prefix with
        {
            Reason = Hung(original) && Hung(converted)
                ? $"input {k} hangs on both builds (reproduced from clean state); the {prefix.Inputs} inputs before it agree"
                : $"input {k} timed out after earlier inputs but not from clean state; the {prefix.Inputs} inputs before it agree",
        };
    }

    internal static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..32];

    /// <summary>The input and the region of the two observations that differs.</summary>
    private static string Witness(PlannedMember member, int index, string l, string r, string leftName, string rightName)
    {
        var input = index < member.Inputs.Count ? MemberOracleInputs.Render(member.Inputs[index]) : "#" + index;
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
        return $"{member.Member} {input}: {leftName} {Excerpt(l)} | {rightName} {Excerpt(r)}";
    }

    // ---- Run -------------------------------------------------------------------------------------

    /// <summary>One member on one run: the observations of the inputs it finished, in input order.</summary>
    private sealed record MemberRun(string[] Observations, int Completed);

    /// <summary>Runs every planned member, in plan order, in one fresh load context of one build.</summary>
    private static Dictionary<string, MemberRun> Run(ArmImage arm, string label, List<PlannedMember> plan, OracleOptions options,
        IReadOnlyDictionary<string, int> limits)
    {
        var results = new Dictionary<string, MemberRun>(StringComparer.Ordinal);
        using var loaded = arm.Load(label);
        var types = OraclePlanner.UsableTypes(loaded.Assembly);
        foreach (var member in plan)
        {
            var limit = Math.Min(member.Inputs.Count, limits.TryGetValue(member.Key, out var l) ? l : int.MaxValue);
            var type = types.GetValueOrDefault(member.Type);
            if (type is null)
            {
                results[member.Key] = new MemberRun(Enumerable.Repeat("<type missing>", limit).ToArray(), limit);
                continue;
            }
            var (observations, completed, timedOut) = Execute(type, member, OraclePlanner.Rebind(type, member), 0, limit, options);
            if (timedOut)
            {
                options.OnTimeout?.Invoke(member.Key, completed);
                throw new TimeoutRestart(member.Key, completed);
            }
            results[member.Key] = new MemberRun(observations, completed);
        }
        return results;
    }

    /// <summary>Drives inputs [from, to) of one member on a worker thread with the member's budget.</summary>
    private static (string[] Observations, int Completed, bool TimedOut) Execute(Type type, PlannedMember member, MemberInfo? info, int from, int to, OracleOptions options)
    {
        var observations = new string[to - from];
        var sandbox = Directory.CreateTempSubdirectory("calor-oracle-1587-");
        var (originalOut, originalErr, originalIn, originalDir) = (Console.Out, Console.Error, Console.In, Environment.CurrentDirectory);
        var abandoned = new StrongBox<bool>(false);
        var done = new StrongBox<int>(0);
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = from; i < to && !Volatile.Read(ref abandoned.Value); i++)
                {
                    observations[i - from] = Observe(type, member, info, member.Inputs[i], options.Depth, sandbox.FullName);
                    Volatile.Write(ref done.Value, i - from + 1);
                }
            }
            catch (Exception ex) when (Volatile.Read(ref abandoned.Value))
            {
                // An abandoned worker's sandbox is already gone; its observations are never read.
                GC.KeepAlive(ex);
            }
        }, 16 * 1024 * 1024) { IsBackground = true, CurrentCulture = CultureInfo.InvariantCulture, CurrentUICulture = CultureInfo.InvariantCulture };
        var timedOut = false;
        try
        {
            Console.SetIn(new StringReader(""));
            Environment.CurrentDirectory = sandbox.FullName;
            thread.Start();
            if (!thread.Join(options.MemberTimeout))
            {
                Volatile.Write(ref abandoned.Value, true);
                timedOut = true;
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            Console.SetIn(originalIn);
            Environment.CurrentDirectory = originalDir;
            try { sandbox.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return (observations, Volatile.Read(ref done.Value), timedOut);
    }

    /// <summary>
    /// One input of one member from clean state: a fresh load context of <paramref name="run"/>, members planned from
    /// <paramref name="planArm"/> (the original, so receivers match the main runs). Finished is false when it did not
    /// complete within the member budget; in that case the worker thread is abandoned, so a caller that must not
    /// leak threads runs this in a process it then exits.
    /// </summary>
    public static ProbeResult Probe(ArmImage planArm, ArmImage run, IReadOnlyCollection<string> typeScope, string memberKey, int input, OracleOptions options)
    {
        PlannedMember member;
        using (var p = planArm.Load("probe-plan"))
            member = OraclePlanner.Plan(p.Assembly, typeScope, options, chooseReceivers: true).Single(m => m.Key == memberKey);
        using var loaded = run.Load("probe");
        var type = OraclePlanner.UsableTypes(loaded.Assembly).GetValueOrDefault(member.Type);
        if (type is null)
            return new ProbeResult(true, 0, "<type missing>");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (observations, completed, _) = Execute(type, member, OraclePlanner.Rebind(type, member), input, input + 1, options);
        return completed == 1 ? new ProbeResult(true, clock.Elapsed.TotalSeconds, observations[0]) : new ProbeResult(false, clock.Elapsed.TotalSeconds, null);
    }

    /// <summary>One invocation on fresh arguments and (for instance members) a fresh receiver.</summary>
    private static string Observe(Type type, PlannedMember member, MemberInfo? info, object?[] tuple, int depth, string sandbox)
    {
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(stdout);
        Console.SetError(stdout);
        var sb = new StringBuilder();
        try
        {
            switch (member.Kind)
            {
                case "enum-values":
                    sb.Append(string.Join(",", Enum.GetNames(type).Zip(Enum.GetValuesAsUnderlyingType(type).Cast<object>(),
                        (n, v) => n + "=" + Convert.ToString(v, CultureInfo.InvariantCulture))));
                    break;
                case "statics":
                    sb.Append("statics=").Append(StateRenderer.RenderStatics(type, depth));
                    break;
                case "construct":
                {
                    var args = info is ConstructorInfo c ? MemberOracleInputs.Materialize(tuple, c.GetParameters()) : [];
                    try
                    {
                        var instance = info is ConstructorInfo ctor ? ctor.Invoke(args) : Activator.CreateInstance(type);
                        sb.Append("state=").Append(StateRenderer.Render(instance, depth));
                    }
                    catch (Exception ex)
                    {
                        sb.Append("throw(").Append(StateRenderer.Unwrap(ex).GetType().FullName).Append(')');
                    }
                    // Arguments and static state are observed whether the constructor returned or threw.
                    sb.Append(" args=").Append(StateRenderer.Render(args, depth)).Append(" statics=").Append(StateRenderer.RenderStatics(type, depth));
                    break;
                }
                default:
                {
                    if (info is not MethodInfo method)
                    {
                        sb.Append("<member missing>");
                        break;
                    }
                    object? receiver = null;
                    if (!method.IsStatic)
                    {
                        try
                        {
                            if (member.ReceiverCtor is null)
                            {
                                receiver = Activator.CreateInstance(type);
                            }
                            else
                            {
                                var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single(c => "ctor" + OraclePlanner.Signature(c) == member.ReceiverCtor);
                                receiver = ctor.Invoke(MemberOracleInputs.Materialize(member.ReceiverArgs, ctor.GetParameters()));
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.Append(SetupFailed).Append("(receiver: ").Append(StateRenderer.Unwrap(ex).GetType().FullName).Append(')');
                            break;
                        }
                    }
                    var args = MemberOracleInputs.Materialize(tuple, method.GetParameters());
                    try
                    {
                        var returned = method.Invoke(receiver, args);
                        sb.Append("return=").Append(method.ReturnType == typeof(void) ? "void" : StateRenderer.Render(returned, depth));
                    }
                    catch (Exception ex)
                    {
                        sb.Append("throw(").Append(StateRenderer.Unwrap(ex).GetType().FullName).Append(')');
                    }
                    sb.Append(" args=").Append(StateRenderer.Render(args, depth));
                    if (receiver is not null)
                        sb.Append(" this=").Append(StateRenderer.Render(receiver, depth));
                    sb.Append(" statics=").Append(StateRenderer.RenderStatics(type, depth));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // The oracle itself failed to observe (not the member): recorded, so both builds must fail the same way.
            sb.Append(" observe-throw(").Append(StateRenderer.Unwrap(ex).GetType().FullName).Append(": ").Append(StateRenderer.Unwrap(ex).Message).Append(')');
        }
        sb.Append(" stdout=").Append(StateRenderer.Render(stdout.ToString(), 0));
        var files = Directory.EnumerateFiles(sandbox, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sandbox, f) + "=" + Hash(Convert.ToBase64String(File.ReadAllBytes(f))))
            .Order(StringComparer.Ordinal).ToList();
        if (files.Count > 0)
            sb.Append(" files=[").Append(string.Join(";", files)).Append(']');
        return StateRenderer.Compact(sb);
    }
}
