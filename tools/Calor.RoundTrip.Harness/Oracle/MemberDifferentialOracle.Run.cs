using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Calor.RoundTrip.Harness.Oracle;

public static partial class MemberDifferentialOracle
{
    /// <summary>One member on one run: the observations of the inputs it finished, in input order, and every project
    /// type's public statics after its last input (null when it did not finish).</summary>
    private sealed record MemberRun(string[] Observations, int Completed, string? After);

    private sealed record Execution(string[] Observations, int Completed, bool TimedOut, string? After, double Seconds);

    /// <summary>Runs every planned member, in plan order, in one fresh load context of one build.</summary>
    private static Dictionary<string, MemberRun> Run(ArmImage arm, string label, List<PlannedMember> plan, OracleOptions options)
    {
        var results = new Dictionary<string, MemberRun>(StringComparer.Ordinal);
        using var loaded = arm.Load(label);
        var types = OraclePlanner.UsableTypes(loaded.Assembly);
        foreach (var member in plan)
        {
            options.OnMemberStart?.Invoke(label, member.Key);
            var limit = Limit(member, options);
            var type = types.GetValueOrDefault(member.Type);
            if (type is null)
            {
                results[member.Key] = new MemberRun(Enumerable.Repeat("<type missing>", limit).ToArray(), limit, null);
                continue;
            }
            var run = Execute(type, member, OraclePlanner.Rebind(type, member), 0, limit, options, label, types, snapshotAfter: true);
            if (run.TimedOut)
            {
                options.OnTimeout?.Invoke(member.Key, run.Completed);
                throw new TimeoutRestart(member.Key, run.Completed);
            }
            results[member.Key] = new MemberRun(run.Observations, run.Completed, run.After);
        }
        return results;
    }

    private static int Limit(PlannedMember member, OracleOptions options) =>
        Math.Min(member.Inputs.Count, options.Limits.TryGetValue(member.Key, out var l) ? l : int.MaxValue);

    /// <summary>
    /// Drives inputs [from, to) of one member on a worker thread with the member's budget, in a private working
    /// directory with captured console. With <paramref name="snapshotAfter"/>, also renders every project type's public
    /// statics after the last input, inside the same budget.
    /// </summary>
    private static Execution Execute(Type type, PlannedMember member, MemberInfo? info, int from, int to, OracleOptions options,
        string label, IReadOnlyDictionary<string, Type> types, bool snapshotAfter)
    {
        var observations = new string[to - from];
        var sandbox = Directory.CreateTempSubdirectory("calor-oracle-1587-");
        var (originalOut, originalErr, originalIn, originalDir) = (Console.Out, Console.Error, Console.In, Environment.CurrentDirectory);
        var abandoned = new StrongBox<bool>(false);
        var done = new StrongBox<int>(0);
        string? after = null;
        var traced = options.TraceMember == member.Key ? options.OnInputStart : null;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = from; i < to && !Volatile.Read(ref abandoned.Value); i++)
                {
                    traced?.Invoke(label, i);
                    observations[i - from] = Observe(type, member, info, member.Inputs[i], options.Depth, sandbox.FullName);
                    Volatile.Write(ref done.Value, i - from + 1);
                }
                if (snapshotAfter && !Volatile.Read(ref abandoned.Value))
                    after = ProjectStatics(types, options.Depth);
            }
            catch (Exception ex) when (Volatile.Read(ref abandoned.Value))
            {
                // An abandoned worker's sandbox is already gone; its observations are never read.
                GC.KeepAlive(ex);
            }
        }, 16 * 1024 * 1024) { IsBackground = true, CurrentCulture = CultureInfo.InvariantCulture, CurrentUICulture = CultureInfo.InvariantCulture };
        var clock = Stopwatch.StartNew();
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
        return new Execution(observations, Volatile.Read(ref done.Value), timedOut, timedOut ? null : after, clock.Elapsed.TotalSeconds);
    }

    private static string ProjectStatics(IReadOnlyDictionary<string, Type> types, int depth)
    {
        try
        {
            return StateRenderer.RenderProjectStatics(types, depth);
        }
        catch (Exception ex)
        {
            return SetupFailed + "(project statics: " + StateRenderer.Unwrap(ex).GetType().FullName + ")";
        }
    }

    /// <summary>
    /// Replays one member up to input <paramref name="input"/> from clean state on one build: a fresh load context, every
    /// member before it in plan order with the same limits (and the same after-member snapshots) as the main runs, the
    /// member's own inputs before <paramref name="input"/>, then that input under the member budget. The plan is the one
    /// the main runs used (receivers chosen in the original build). A replay that fails before reaching the input is
    /// <see cref="ProbeOutcome.Infrastructure"/>, never a behavioral observation. A hung input's thread is abandoned, so
    /// callers that must not leak threads run this in a process they then exit.
    /// </summary>
    public static ProbeResult Probe(ArmImage original, ArmImage candidate, IReadOnlyCollection<string> typeScope, string memberKey, int input,
        bool onCandidate, OracleOptions options)
    {
        var (_, plan) = SharedPlan(original, candidate, typeScope, options);
        var runnable = plan.Where(m => m.Kind != "hierarchy" && m.SkipReason is null).ToList();
        var target = runnable.FirstOrDefault(m => m.Key == memberKey);
        if (target is null || input >= target.Inputs.Count)
            return new ProbeResult(ProbeOutcome.Infrastructure, 0, $"<{memberKey} input {input} is not in the plan>");
        using var loaded = (onCandidate ? candidate : original).Load("probe");
        var types = OraclePlanner.UsableTypes(loaded.Assembly);
        foreach (var member in runnable.TakeWhile(m => m.Key != memberKey))
        {
            if (types.GetValueOrDefault(member.Type) is not { } t)
                continue;
            if (Execute(t, member, OraclePlanner.Rebind(t, member), 0, Limit(member, options), options, "probe", types, snapshotAfter: true).TimedOut)
                return new ProbeResult(ProbeOutcome.Infrastructure, 0, $"<replay of {member.Key} timed out>");
        }
        if (types.GetValueOrDefault(target.Type) is not { } type)
            return new ProbeResult(ProbeOutcome.Infrastructure, 0, "<type missing>");
        var info = OraclePlanner.Rebind(type, target);
        if (input > 0 && Execute(type, target, info, 0, input, options, "probe", types, snapshotAfter: false).TimedOut)
            return new ProbeResult(ProbeOutcome.Infrastructure, 0, "<replay of the earlier inputs timed out>");
        options.OnProbeInput?.Invoke();
        var run = Execute(type, target, info, input, input + 1, options, "probe", types, snapshotAfter: false);
        return run.Completed == 1 ? new ProbeResult(ProbeOutcome.Finished, run.Seconds, run.Observations[0]) : new ProbeResult(ProbeOutcome.Hung, run.Seconds, null);
    }

    /// <summary>One invocation on fresh arguments and (for instance members) a fresh receiver.</summary>
    private static string Observe(Type type, PlannedMember member, MemberInfo? info, object?[] tuple, int depth, string sandbox)
    {
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(stdout);
        Console.SetError(stdout);
        var sink = new StateRenderer.Sink();
        try
        {
            ObserveInto(sink, type, member, info, tuple, depth);
        }
        catch (Exception ex)
        {
            // The oracle failed to observe (not the member): a structured setup failure, never agreement on its own.
            var inner = StateRenderer.Unwrap(ex);
            var failed = new StateRenderer.Sink();
            failed.Append($"{SetupFailed}(observe: {inner.GetType().FullName}: {inner.Message})");
            sink = failed;
        }
        sink.Append(" stdout=");
        StateRenderer.Append(sink, stdout.ToString(), 0);
        var files = Directory.EnumerateFiles(sandbox, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sandbox, f) + "=" + Hash(Convert.ToBase64String(File.ReadAllBytes(f))))
            .Order(StringComparer.Ordinal).ToList();
        if (files.Count > 0)
            sink.Append(" files=[" + string.Join(";", files) + "]");
        return sink.Result();
    }

    private static void ObserveInto(StateRenderer.Sink sb, Type type, PlannedMember member, MemberInfo? info, object?[] tuple, int depth)
    {
        switch (member.Kind)
        {
            case "enum-values":
                sb.Append(string.Join(",", Enum.GetNames(type).Zip(Enum.GetValuesAsUnderlyingType(type).Cast<object>(),
                    (n, v) => n + "=" + Convert.ToString(v, CultureInfo.InvariantCulture))));
                return;
            case "statics":
                sb.Append("statics=" + StateRenderer.RenderStatics(type, depth));
                return;
            case "construct":
            {
                object?[] args;
                try
                {
                    args = info is ConstructorInfo c ? MemberOracleInputs.Materialize(tuple, c.GetParameters()) : [];
                }
                catch (Exception ex)
                {
                    sb.Append($"{SetupFailed}(args: {StateRenderer.Unwrap(ex).GetType().FullName})");
                    return;
                }
                try
                {
                    var instance = info is ConstructorInfo ctor ? ctor.Invoke(args) : Activator.CreateInstance(type);
                    sb.Append("state=");
                    StateRenderer.Append(sb, instance, depth);
                }
                catch (Exception ex)
                {
                    sb.Append("throw(" + StateRenderer.Unwrap(ex).GetType().FullName + ")");
                }
                // Arguments and static state are observed whether the constructor returned or threw.
                sb.Append(" args=");
                StateRenderer.Append(sb, args, depth);
                sb.Append(" statics=" + StateRenderer.RenderStatics(type, depth));
                return;
            }
        }
        if (info is not MethodInfo method)
        {
            sb.Append("<member missing>");
            return;
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
                sb.Append($"{SetupFailed}(receiver: {StateRenderer.Unwrap(ex).GetType().FullName})");
                return;
            }
        }
        object?[] arguments;
        try
        {
            arguments = MemberOracleInputs.Materialize(tuple, method.GetParameters());
        }
        catch (Exception ex)
        {
            sb.Append($"{SetupFailed}(args: {StateRenderer.Unwrap(ex).GetType().FullName})");
            return;
        }
        try
        {
            var returned = method.Invoke(receiver, arguments);
            sb.Append("return=");
            if (method.ReturnType == typeof(void))
                sb.Append("void");
            else
                StateRenderer.Append(sb, returned, depth);
        }
        catch (Exception ex)
        {
            sb.Append("throw(" + StateRenderer.Unwrap(ex).GetType().FullName + ")");
        }
        sb.Append(" args=");
        StateRenderer.Append(sb, arguments, depth);
        if (receiver is not null)
        {
            sb.Append(" this=");
            StateRenderer.Append(sb, receiver, depth);
        }
        sb.Append(" statics=" + StateRenderer.RenderStatics(type, depth));
    }
}
