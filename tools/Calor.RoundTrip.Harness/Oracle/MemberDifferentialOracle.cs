using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>
/// #1587: the member-level differential oracle. Given two builds of the same subject (the original,
/// and one with a single file swapped for its round-tripped C#), it drives every public member of the
/// in-scope types on both and compares what it observes:
/// <list type="bullet">
/// <item>construction: the state right after each public constructor, to <see cref="OracleOptions.Depth"/>;</item>
/// <item>static and instance methods (including property setters and indexers): return value, thrown
/// exception type, argument state after the call (by-ref and collections), the receiver's state, the
/// declaring type's public static state, and console output;</item>
/// <item>the declaring type's static state after type initialization, and enum values;</item>
/// <item>the public surface itself: a member present in one build only is a difference.</item>
/// </list>
/// Each build is run twice, every run in a fresh collectible load context, in the order
/// original, candidate, original, candidate. A build that disagrees with its own re-run makes the member
/// NONDETERMINISTIC (skipped, not failed). A member that exceeds its budget is TIMEOUT (skipped).
/// AGREE means no disagreement on this finite input set; it is not a proof of equivalence.
/// </summary>
public static class MemberDifferentialOracle
{
    private sealed record PlannedMember(string Type, string Member, string Kind, MemberInfo? Info, IReadOnlyList<object?[]> Inputs, string? SkipReason)
    {
        /// <summary>For instance members: the constructor (plan key) and arguments that build each fresh receiver;
        /// null means the parameterless constructor (or a struct's default value).</summary>
        public string? ReceiverCtor { get; init; }
        public object?[] ReceiverArgs { get; init; } = [];
    }

    public static List<MemberVerdict> Compare(ArmImage original, ArmImage candidate, IReadOnlyCollection<string> typeScope, OracleOptions options)
    {
        var verdicts = new List<MemberVerdict>();
        List<PlannedMember> plan;
        HashSet<string> candidateSurface;
        using (var o = original.Load("plan"))
        using (var c = candidate.Load("plan"))
        {
            plan = Plan(o.Assembly, typeScope, options);
            var candidatePlan = Plan(c.Assembly, typeScope, options);
            candidateSurface = candidatePlan.Select(m => m.Type + "::" + m.Member).ToHashSet(StringComparer.Ordinal);
            var originalSurface = plan.Select(m => m.Type + "::" + m.Member).ToHashSet(StringComparer.Ordinal);
            foreach (var extra in candidatePlan.Where(m => !originalSurface.Contains(m.Type + "::" + m.Member)))
                verdicts.Add(new MemberVerdict
                {
                    Type = extra.Type, Member = extra.Member, Kind = extra.Kind, Status = MemberStatus.Different, Reason = "SURFACE",
                    Witnesses = ["public member present only in the converted build"],
                });
        }

        var runnable = new List<PlannedMember>();
        foreach (var member in plan)
        {
            if (!candidateSurface.Contains(member.Type + "::" + member.Member))
                verdicts.Add(new MemberVerdict
                {
                    Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Different, Reason = "SURFACE",
                    Witnesses = ["public member present only in the original build"],
                });
            else if (member.SkipReason is not null)
                verdicts.Add(new MemberVerdict { Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Skipped, Reason = member.SkipReason });
            else
                runnable.Add(member);
        }

        // Run 1 of each build drives every input. If either run 1 times out, run 2 of both builds drives only
        // the prefix both run-1s finished, so the re-run check and the comparison cover the same inputs.
        var full = runnable.ToDictionary(m => m.Type + "::" + m.Member, m => m.Inputs.Count, StringComparer.Ordinal);
        var o1 = Run(original, "run1", runnable, options, full);
        var c1 = Run(candidate, "run1", runnable, options, full);
        var prefix = runnable.ToDictionary(m => m.Type + "::" + m.Member,
            m => Math.Min(o1[m.Type + "::" + m.Member].Completed, c1[m.Type + "::" + m.Member].Completed), StringComparer.Ordinal);
        var o2 = Run(original, "run2", runnable, options, prefix);
        var c2 = Run(candidate, "run2", runnable, options, prefix);

        foreach (var member in runnable)
        {
            var key = member.Type + "::" + member.Member;
            var verdict = new MemberVerdict { Type = member.Type, Member = member.Member, Kind = member.Kind, Status = MemberStatus.Agree, Inputs = member.Inputs.Count };

            // A build that hangs where the other finishes all inputs quickly is a behavior difference, not a skip.
            if (Hang(o1[key], c1[key], member.Inputs.Count, options) is { } hang)
            {
                verdicts.Add(verdict with { Status = MemberStatus.Different, Reason = "HANG", Witnesses = [member.Member + " " + hang] });
                continue;
            }
            var n = new[] { prefix[key], o2[key].Completed, c2[key].Completed }.Min();
            var (a1, b1, a2, b2) = (o1[key].Take(n), c1[key].Take(n), o2[key].Take(n), c2[key].Take(n));
            var unstable = FirstDifference(a1, a2) ?? FirstDifference(b1, b2);
            if (unstable is { } u)
            {
                var originalUnstable = FirstDifference(a1, a2) is not null;
                verdicts.Add(verdict with
                {
                    Status = MemberStatus.Nondeterministic,
                    Reason = "a build disagreed with its own re-run",
                    Inputs = n,
                    Witnesses = [Witness(member, u, originalUnstable ? a1 : b1, originalUnstable ? a2 : b2, "run1", "run2")],
                });
                continue;
            }
            var witnesses = new List<string>();
            for (var i = 0; i < n && witnesses.Count < options.MaxWitnesses; i++)
                if (!string.Equals(a1[i], b1[i], StringComparison.Ordinal))
                    witnesses.Add(Witness(member, i, a1, b1, "original", "converted"));
            var timedOut = new[] { o1[key], c1[key], o2[key], c2[key] }.Any(r => r.TimedOut);
            if (witnesses.Count > 0)
                verdicts.Add(verdict with { Status = MemberStatus.Different, Reason = "OBSERVATION", Inputs = n, Witnesses = witnesses });
            else if (timedOut)
                verdicts.Add(verdict with
                {
                    Status = MemberStatus.Timeout, Inputs = n,
                    Reason = $"exceeded {options.MemberTimeout.TotalSeconds:0}s; the {n} of {member.Inputs.Count} inputs before it agree",
                });
            else
                verdicts.Add(verdict);
        }

        return verdicts.OrderBy(v => v.Type, StringComparer.Ordinal).ThenBy(v => v.Member, StringComparer.Ordinal).ToList();
    }

    /// <summary>One build's run 1 timed out while the other finished every input within a quarter of the budget.</summary>
    private static string? Hang(MemberRun original, MemberRun converted, int inputs, OracleOptions options)
    {
        var quick = options.MemberTimeout.TotalSeconds / 4;
        if (converted.TimedOut && !original.TimedOut && original.Completed == inputs && original.Seconds < quick)
            return $"input #{converted.Completed} {Describe(converted)}: original finished all {inputs} inputs in {original.Seconds:0.00}s | converted did not finish within {options.MemberTimeout.TotalSeconds:0}s";
        if (original.TimedOut && !converted.TimedOut && converted.Completed == inputs && converted.Seconds < quick)
            return $"input #{original.Completed} {Describe(original)}: original did not finish within {options.MemberTimeout.TotalSeconds:0}s | converted finished all {inputs} inputs in {converted.Seconds:0.00}s";
        return null;
    }

    private static string Describe(MemberRun run) => run.HangInput is null ? "" : MemberOracleInputs.Render(run.HangInput);

    private static int? FirstDifference(string[] a, string[] b)
    {
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                return i;
        return a.Length == b.Length ? null : Math.Min(a.Length, b.Length);
    }

    /// <summary>The input and the first differing region of the two observations.</summary>
    private static string Witness(PlannedMember member, int index, string[] left, string[] right, string leftName, string rightName)
    {
        var input = index < member.Inputs.Count ? MemberOracleInputs.Render(member.Inputs[index]) : "#" + index;
        var (l, r) = (index < left.Length ? left[index] : "<missing>", index < right.Length ? right[index] : "<missing>");
        // The excerpt spans every differing character (common prefix and suffix trimmed), with context, capped.
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

    // ---- Plan ----------------------------------------------------------------------------------

    private static List<PlannedMember> Plan(Assembly assembly, IReadOnlyCollection<string> typeScope, OracleOptions options)
    {
        var plan = new List<PlannedMember>();
        foreach (var name in typeScope.Order(StringComparer.Ordinal))
        {
            var type = assembly.GetType(name, throwOnError: false);
            if (type is null || !type.IsVisible)
                continue;
            if (type.IsGenericTypeDefinition)
            {
                if (CloseType(type) is not { } closed)
                {
                    plan.Add(new PlannedMember(StateRenderer.TypeName(type), "<type>", "type", null, [], "open generic type (no closing argument meets its constraints)"));
                    continue;
                }
                type = closed;
            }
            var typeName = StateRenderer.TypeName(type);
            PlannedMember Skip(string member, string kind, string reason) => new(typeName, member, kind, null, [], reason);
            if (type.IsInterface || typeof(Delegate).IsAssignableFrom(type))
                continue;
            if (type.IsEnum)
            {
                plan.Add(new PlannedMember(typeName, "<enum-values>", "enum-values", type, [[]], null));
                continue;
            }

            plan.Add(new PlannedMember(typeName, "<statics>", "statics", type, [[]], null));
            // A receiver comes from the parameterless constructor when there is one, else from the public constructor
            // with the fewest parameters the generator can supply, called with one fixed input tuple.
            var receiverCtor = type.IsValueType || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is { IsPublic: true } ? null
                : type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .Where(c => MemberOracleInputs.Unsupported(c.GetParameters()) is null)
                    .OrderBy(c => c.GetParameters().Length).ThenBy(Signature, StringComparer.Ordinal).FirstOrDefault();
            var constructible = type.IsValueType || (!type.IsAbstract && (type.GetConstructor(Type.EmptyTypes) is { IsPublic: true } || receiverCtor is not null));
            var receiverKey = receiverCtor is null ? null : "ctor" + Signature(receiverCtor);
            var receiverArgs = receiverCtor is null ? [] : MemberOracleInputs.ReceiverArguments(options.SeedKey, typeName + "::receiver", receiverCtor.GetParameters());
            if (!type.IsAbstract || type.IsValueType)
            {
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderBy(Signature, StringComparer.Ordinal))
                    plan.Add(Planned(typeName, "ctor" + Signature(ctor), "construct", ctor, ctor.GetParameters(), options));
                if (type.IsValueType && type.GetConstructor(Type.EmptyTypes) is null)
                    plan.Add(new PlannedMember(typeName, "ctor()", "construct", null, [[]], null));
            }

            const BindingFlags methods = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var declared in type.GetMethods(methods).OrderBy(m => m.Name + Signature(m), StringComparer.Ordinal))
            {
                if (declared.Name.Contains('<') || declared.IsAbstract)
                    continue;
                var method = declared.IsGenericMethodDefinition ? CloseMethod(declared) : declared;
                if (method is null)
                {
                    plan.Add(Skip(MethodKey(declared), declared.IsStatic ? "static" : "instance", "generic method (no closing argument meets its constraints)"));
                    continue;
                }
                var ps = method.GetParameters();
                // Parameterless getters are observed as state; event accessors take delegates.
                if (method.IsSpecialName && ((method.Name.StartsWith("get_", StringComparison.Ordinal) && ps.Length == 0)
                    || method.Name.StartsWith("add_", StringComparison.Ordinal) || method.Name.StartsWith("remove_", StringComparison.Ordinal)))
                    continue;
                var key = MethodKey(method);
                var kind = method.IsStatic ? "static" : "instance";
                if (!method.IsStatic && !constructible)
                    plan.Add(Skip(key, kind, type.IsAbstract ? "instance member of an abstract type" : "no public parameterless constructor for a receiver"));
                else
                    plan.Add(Planned(typeName, key, kind, method, ps, options) with { ReceiverCtor = method.IsStatic ? null : receiverKey, ReceiverArgs = method.IsStatic ? [] : receiverArgs });
            }
        }
        return plan;
    }

    private static PlannedMember Planned(string typeName, string key, string kind, MethodBase method, ParameterInfo[] parameters, OracleOptions options)
    {
        if (MemberOracleInputs.Unsupported(parameters) is { } reason)
            return new PlannedMember(typeName, key, kind, null, [], reason);
        if (method is MethodInfo { ReturnType: { IsByRef: true } or { IsByRefLike: true } or { IsPointer: true } })
            return new PlannedMember(typeName, key, kind, null, [], "by-ref or ref-struct return");
        return new PlannedMember(typeName, key, kind, method, MemberOracleInputs.Generate(options.SeedKey, typeName + "::" + key, parameters), null);
    }

    /// <summary>Type arguments tried, in order, to close a generic type or method; the first that meets every constraint wins.</summary>
    internal static readonly Type[] ClosingArguments = [typeof(object), typeof(string), typeof(int)];

    internal static Type? CloseType(Type open)
    {
        foreach (var argument in ClosingArguments)
        {
            try
            {
                return open.MakeGenericType(Enumerable.Repeat(argument, open.GetGenericArguments().Length).ToArray());
            }
            catch (ArgumentException)
            {
                // A constraint rejects this argument; try the next.
            }
        }
        return null;
    }

    internal static MethodInfo? CloseMethod(MethodInfo open)
    {
        foreach (var argument in ClosingArguments)
        {
            try
            {
                return open.MakeGenericMethod(Enumerable.Repeat(argument, open.GetGenericArguments().Length).ToArray());
            }
            catch (ArgumentException)
            {
                // A constraint rejects this argument; try the next.
            }
        }
        return null;
    }

    private static string MethodKey(MethodInfo m) =>
        (m.IsStatic ? "static " : "") + m.Name
        + (m.IsGenericMethod ? "<" + string.Join(",", m.GetGenericArguments().Select(StateRenderer.TypeName)) + ">" : "")
        + Signature(m) + "->" + StateRenderer.TypeName(m.ReturnType);

    private static string Signature(MethodBase m) => "(" + string.Join(",", m.GetParameters().Select(p =>
        (p.IsOut ? "out " : "") + StateRenderer.TypeName(p.ParameterType))) + ")";

    // ---- Run -----------------------------------------------------------------------------------

    /// <summary>One member on one run: the observations of the inputs it finished, in input order.</summary>
    private sealed record MemberRun(string[] Observations, int Completed, bool TimedOut, double Seconds, object?[]? HangInput)
    {
        public string[] Take(int n) => Observations[..Math.Min(n, Completed)];
    }

    /// <summary>Runs every planned member, in plan order, in one fresh load context of one build, driving the
    /// first <paramref name="limits"/>[member] inputs of each.</summary>
    private static Dictionary<string, MemberRun> Run(ArmImage arm, string label, List<PlannedMember> plan, OracleOptions options,
        IReadOnlyDictionary<string, int> limits)
    {
        var results = new Dictionary<string, MemberRun>(StringComparer.Ordinal);
        using var loaded = arm.Load(label);
        var types = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in loaded.Assembly.GetExportedTypes())
            if ((t.IsGenericTypeDefinition ? CloseType(t) : t) is { } usable)
                types.TryAdd(StateRenderer.TypeName(usable), usable);
        foreach (var member in plan)
        {
            var key = member.Type + "::" + member.Member;
            var limit = limits[key];
            var type = types.GetValueOrDefault(member.Type);
            var info = type is null ? null : Rebind(type, member);
            var observations = new string[limit];
            if (type is null || limit == 0)
            {
                results[key] = new MemberRun(observations, type is null ? 0 : limit, false, 0, null);
                continue;
            }
            var sandbox = Directory.CreateTempSubdirectory("calor-oracle-1587-");
            var (originalOut, originalErr, originalIn, originalDir) = (Console.Out, Console.Error, Console.In, Environment.CurrentDirectory);
            var abandoned = new StrongBox<bool>(false);
            var done = new StrongBox<int>(0);
            var thread = new Thread(() =>
            {
                // A timed-out worker cannot be stopped; it only finishes its current input, because each
                // input redirects the process-wide console.
                try
                {
                    for (var i = 0; i < limit && !Volatile.Read(ref abandoned.Value); i++)
                    {
                        observations[i] = Observe(type, member, info, member.Inputs[i], options.Depth, sandbox.FullName);
                        Volatile.Write(ref done.Value, i + 1);
                    }
                }
                catch (Exception ex) when (Volatile.Read(ref abandoned.Value))
                {
                    // An abandoned worker's sandbox is already gone; its observations are never read.
                    GC.KeepAlive(ex);
                }
            }, 16 * 1024 * 1024) { IsBackground = true, CurrentCulture = CultureInfo.InvariantCulture, CurrentUICulture = CultureInfo.InvariantCulture };
            var clock = System.Diagnostics.Stopwatch.StartNew();
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
            var completed = Volatile.Read(ref done.Value);
            results[key] = new MemberRun(observations, completed, timedOut, clock.Elapsed.TotalSeconds,
                timedOut && completed < member.Inputs.Count ? member.Inputs[completed] : null);
        }
        return results;
    }

    /// <summary>Finds the same member in this run's load context by its plan key.</summary>
    private static MemberInfo? Rebind(Type type, PlannedMember member)
    {
        switch (member.Kind)
        {
            case "statics":
            case "enum-values":
                return type;
            case "construct":
                return type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(c => "ctor" + Signature(c) == member.Member);
            default:
                return type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Select(m => m.IsGenericMethodDefinition ? CloseMethod(m) : m)
                    .FirstOrDefault(m => m is not null && MethodKey(m) == member.Member);
        }
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
                    object? instance;
                    try
                    {
                        instance = info is ConstructorInfo ctor ? ctor.Invoke(args) : Activator.CreateInstance(type);
                    }
                    catch (Exception ex)
                    {
                        sb.Append("throw(").Append(StateRenderer.Unwrap(ex).GetType().FullName).Append(')');
                        break;
                    }
                    sb.Append("state=").Append(StateRenderer.Render(instance, depth)).Append(" args=").Append(StateRenderer.Render(args, depth));
                    sb.Append(" statics=").Append(StateRenderer.RenderStatics(type, depth));
                    break;
                }
                default:
                {
                    var method = (MethodInfo)info!;
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
                                var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single(c => "ctor" + Signature(c) == member.ReceiverCtor);
                                receiver = ctor.Invoke(MemberOracleInputs.Materialize(member.ReceiverArgs, ctor.GetParameters()));
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.Append("receiver-throw(").Append(StateRenderer.Unwrap(ex).GetType().FullName).Append(')');
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
            // Rendering itself failed (for example a getter that recursed forever is caught as its own exception type).
            sb.Append(" observe-throw(").Append(StateRenderer.Unwrap(ex).GetType().FullName).Append(')');
        }
        sb.Append(" stdout=").Append(StateRenderer.Render(stdout.ToString(), 0));
        var files = Directory.EnumerateFiles(sandbox, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sandbox, f) + "=" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))[..16])
            .Order(StringComparer.Ordinal).ToList();
        if (files.Count > 0)
            sb.Append(" files=[").Append(string.Join(";", files)).Append(']');
        return sb.ToString();
    }
}
