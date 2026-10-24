using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Calor.RoundTrip.Harness.Oracle;

public static partial class MemberDifferentialOracle
{
    /// <summary>One member on one run: the observations of the inputs it finished, in input order.</summary>
    private sealed record MemberRun(string[] Observations, int Completed);

    private sealed record Execution(string[] Observations, int Completed, bool TimedOut, double TargetSeconds);

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
                results[member.Key] = new MemberRun(Enumerable.Repeat("<type missing>", limit).ToArray(), limit);
                continue;
            }
            var run = Execute(type, member, OraclePlanner.Rebind(type, member), limit, options, label, types);
            if (run.TimedOut)
            {
                options.OnTimeout?.Invoke(member.Key, run.Completed);
                throw new TimeoutRestart(member.Key, run.Completed);
            }
            results[member.Key] = new MemberRun(run.Observations, run.Completed);
        }
        return results;
    }

    private static int Limit(PlannedMember member, OracleOptions options) =>
        Math.Min(member.Inputs.Count, options.Limits.TryGetValue(member.Key, out var l) ? l : int.MaxValue);

    /// <summary>
    /// Drives inputs [0, count) of one member on a worker thread with the member's budget, in one private working
    /// directory with captured console (shared by all its inputs, as each input sees the files earlier ones wrote).
    /// <paramref name="target"/> is an input index the caller times separately (a probe), calling
    /// <see cref="OracleOptions.OnProbeInput"/> right before it.
    /// </summary>
    private static Execution Execute(Type type, PlannedMember member, MemberInfo? info, int count, OracleOptions options,
        string label, IReadOnlyDictionary<string, Type> types, int target = -1)
    {
        var observations = new string[count];
        var sandbox = Directory.CreateTempSubdirectory("calor-oracle-1587-");
        var (originalOut, originalErr, originalIn, originalDir) = (Console.Out, Console.Error, Console.In, Environment.CurrentDirectory);
        var abandoned = new StrongBox<bool>(false);
        var done = new StrongBox<int>(0);
        var targetClock = new Stopwatch();
        var traced = options.TraceMember == member.Key ? options.OnInputStart : null;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < count && !Volatile.Read(ref abandoned.Value); i++)
                {
                    traced?.Invoke(label, i);
                    if (i == target)
                    {
                        options.OnProbeInput?.Invoke();
                        targetClock.Start();
                    }
                    observations[i] = Observe(type, member, info, member.Inputs[i], options.Depth, sandbox.FullName, types);
                    if (i == target)
                        targetClock.Stop();
                    Volatile.Write(ref done.Value, i + 1);
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
            // The console goes to process-wide routers whose target is the current input's buffer, so a writer the subject
            // captured earlier (a static TextWriter initialized from Console.Out, say) still reaches the input that writes.
            Console.SetOut(ConsoleRouter.Out);
            Console.SetError(ConsoleRouter.Error);
            Console.SetIn(new StringReader(""));
            Environment.CurrentDirectory = sandbox.FullName;
            StateRenderer.SandboxRoot = sandbox.FullName;
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
            StateRenderer.SandboxRoot = null;
            (ConsoleRouter.Out.Target, ConsoleRouter.Error.Target) = (TextWriter.Null, TextWriter.Null);
            try { sandbox.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return new Execution(observations, Volatile.Read(ref done.Value), timedOut, targetClock.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// Replays one member up to input <paramref name="input"/> from clean state on one build: a fresh load context, every
    /// member before it in plan order with the same limits as the main runs, then the member's inputs 0 to
    /// <paramref name="input"/> in one execution (same sandbox, same budget), exactly as the main run drove them. The
    /// plan is the one the main runs used (receivers chosen in the original build). A replay that does not reach the
    /// input is <see cref="ProbeOutcome.Infrastructure"/>, never a behavioral observation. A hung input's thread is
    /// abandoned, so callers that must not leak threads run this in a process they then exit.
    /// </summary>
    public static ProbeResult Probe(ArmImage original, ArmImage candidate, IReadOnlyCollection<string> typeScope, string memberKey, int input,
        bool onCandidate, OracleOptions options)
    {
        var (_, plan) = SharedPlan(original, candidate, typeScope, options);
        var runnable = OraclePlanner.Expand(plan.Where(m => m.Kind != "hierarchy" && m.SkipReason is null), options.Limits);
        var targetMember = runnable.FirstOrDefault(m => m.Key == memberKey);
        if (targetMember is null || input >= targetMember.Inputs.Count)
            return new ProbeResult(ProbeOutcome.Infrastructure, 0, $"<{memberKey} input {input} is not in the plan>");
        using var loaded = (onCandidate ? candidate : original).Load("probe");
        var types = OraclePlanner.UsableTypes(loaded.Assembly);
        foreach (var member in runnable.TakeWhile(m => m.Key != memberKey))
        {
            if (types.GetValueOrDefault(member.Type) is not { } t)
                continue;
            if (Execute(t, member, OraclePlanner.Rebind(t, member), Limit(member, options), options, "probe", types).TimedOut)
                return new ProbeResult(ProbeOutcome.Infrastructure, 0, $"<replay of {member.Key} timed out>");
        }
        if (types.GetValueOrDefault(targetMember.Type) is not { } type)
            return new ProbeResult(ProbeOutcome.Infrastructure, 0, "<type missing>");
        var run = Execute(type, targetMember, OraclePlanner.Rebind(type, targetMember), input + 1, options, "probe", types, target: input);
        if (run.Completed == input + 1)
            return new ProbeResult(ProbeOutcome.Finished, run.TargetSeconds, run.Observations[input]);
        return run.Completed == input
            ? new ProbeResult(ProbeOutcome.Hung, options.MemberTimeout.TotalSeconds, null)
            : new ProbeResult(ProbeOutcome.Infrastructure, 0, $"<replay timed out at earlier input {run.Completed}>");
    }

    /// <summary>An observation under construction: one sink per named part, encoded in insertion order.</summary>
    private sealed class ObservationParts
    {
        private readonly List<(string Name, StateRenderer.Sink Sink)> _parts = [];

        public StateRenderer.Sink this[string name]
        {
            get
            {
                foreach (var (n, sink) in _parts)
                    if (n == name)
                        return sink;
                var created = new StateRenderer.Sink();
                _parts.Add((name, created));
                return created;
            }
        }

        public string Encode() => string.Join(PartSeparator, _parts.Select(p => p.Name + NameSeparator + p.Sink.Result()));
    }

    /// <summary>One invocation on fresh arguments and (for instance members) a fresh receiver, then every project type's
    /// public statics, so a change the call made anywhere in the project is part of this input's observation. Each kind of
    /// evidence is its own part, compared on its own.</summary>
    private static string Observe(Type type, PlannedMember member, MemberInfo? info, object?[] tuple, int depth, string sandbox,
        IReadOnlyDictionary<string, Type> types)
    {
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        var stderr = new StringWriter(CultureInfo.InvariantCulture);
        ConsoleRouter.Out.Target = stdout;
        ConsoleRouter.Error.Target = stderr;
        var parts = new ObservationParts();
        try
        {
            if (ObserveInto(parts, type, member, info, tuple, depth))
            {
                StateRenderer.AppendProjectStatics(parts["project"], types, depth);
                StateRenderer.AppendPrivateStatics(parts["private-statics"], types, depth);
            }
        }
        catch (Exception ex)
        {
            // The oracle failed to observe (not the member): a structured setup failure, never agreement on its own.
            var inner = StateRenderer.Unwrap(ex);
            parts = new ObservationParts();
            parts["setup"].Append($"{SetupFailed}(observe: {inner.GetType().FullName}: {inner.Message})");
        }
        // Standard output and standard error are separate parts: a message moved from one stream to the other is a difference.
        StateRenderer.Append(parts["stdout"], stdout.ToString(), 0);
        if (stderr.ToString() is { Length: > 0 } errors)
            StateRenderer.Append(parts["stderr"], errors, 0);
        // Files by content hash, and directories by name (an empty directory the member created is state too).
        var files = Directory.EnumerateFileSystemEntries(sandbox, "*", SearchOption.AllDirectories)
            .Select(f => Directory.Exists(f) ? Path.GetRelativePath(sandbox, f) + "/"
                : Path.GetRelativePath(sandbox, f) + "=" + Hash(Convert.ToBase64String(File.ReadAllBytes(f))))
            .Order(StringComparer.Ordinal).ToList();
        if (files.Count > 0)
            parts["files"].Append(string.Join(";", files));
        return parts.Encode();
    }

    /// <summary>Writes the member's parts; false when setup failed (the only part is then <c>setup</c>).</summary>
    private static bool ObserveInto(ObservationParts parts, Type type, PlannedMember member, MemberInfo? info, object?[] tuple, int depth)
    {
        switch (member.Kind)
        {
            case "enum-values":
                parts["result"].Append(string.Join(",", Enum.GetNames(type).Zip(Enum.GetValuesAsUnderlyingType(type).Cast<object>(),
                    (n, v) => n + "=" + Convert.ToString(v, CultureInfo.InvariantCulture))));
                return true;
            case "statics":
                StateRenderer.AppendStatics(parts["statics"], type, depth);
                return true;
            case "construct":
            {
                object?[] args;
                try
                {
                    args = info is ConstructorInfo c ? MemberOracleInputs.Materialize(tuple, c.GetParameters()) : [];
                }
                catch (Exception ex)
                {
                    parts["setup"].Append($"{SetupFailed}(args: {StateRenderer.Unwrap(ex).GetType().FullName})");
                    return false;
                }
                try
                {
                    var instance = info is ConstructorInfo ctor ? ctor.Invoke(args) : Activator.CreateInstance(type);
                    parts["result"].Append("state=");
                    StateRenderer.Append(parts["result"], instance, depth);
                }
                catch (Exception ex)
                {
                    StateRenderer.AppendThrown(parts["result"], ex, depth);
                }
                // Arguments and static state are observed whether the constructor returned or threw.
                StateRenderer.AppendArguments(parts["args"], args, depth);
                StateRenderer.AppendStatics(parts["statics"], type, depth);
                return true;
            }
        }
        if (info is not MethodInfo method)
        {
            parts["result"].Append("<member missing>");
            return true;
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
                parts["setup"].Append($"{SetupFailed}(receiver: {StateRenderer.Unwrap(ex).GetType().FullName})");
                return false;
            }
        }
        object?[] arguments;
        try
        {
            arguments = MemberOracleInputs.Materialize(tuple, method.GetParameters());
        }
        catch (Exception ex)
        {
            parts["setup"].Append($"{SetupFailed}(args: {StateRenderer.Unwrap(ex).GetType().FullName})");
            return false;
        }
        try
        {
            var returned = method.Invoke(receiver, arguments);
            parts["result"].Append("return=");
            if (method.ReturnType == typeof(void))
                parts["result"].Append("void");
            else
                StateRenderer.Append(parts["result"], returned, depth);
        }
        catch (Exception ex)
        {
            StateRenderer.AppendThrown(parts["result"], ex, depth);
        }
        StateRenderer.AppendArguments(parts["args"], arguments, depth);
        if (receiver is not null)
            StateRenderer.Append(parts["this"], receiver, depth);
        StateRenderer.AppendStatics(parts["statics"], type, depth);
        return true;
    }
}

/// <summary>
/// #1667: a process-wide console writer whose target is the current input's buffer. The oracle installs one for
/// standard output and one for standard error once per execution and retargets them per input, so a writer the
/// subject captured from <see cref="Console.Out"/> earlier (in a type initializer, say) still reaches the input
/// that writes through it, instead of a buffer nobody reads.
/// </summary>
internal sealed class ConsoleRouter : TextWriter
{
    public static readonly ConsoleRouter Out = new();
    public static readonly ConsoleRouter Error = new();

    private volatile TextWriter _target = Null;

    public TextWriter Target
    {
        get => _target;
        set => _target = value;
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override IFormatProvider FormatProvider => CultureInfo.InvariantCulture;

    public override void Write(char value) => _target.Write(value);

    public override void Write(string? value) => _target.Write(value);

    public override void Write(char[] buffer, int index, int count) => _target.Write(buffer, index, count);

    public override void Flush() => _target.Flush();
}
