using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Serialization;

namespace Calor.RoundTrip.Harness.Oracle;

/// <summary>#1587 member verdicts. Only <see cref="Different"/> can fail the gate.</summary>
public static class MemberStatus
{
    /// <summary>Every input observed the same on both builds, and each build agreed with its own re-run.</summary>
    public const string Agree = "AGREE";

    /// <summary>Some input observed differently on the two builds (or the public surface differs).</summary>
    public const string Different = "DIFFERENT";

    /// <summary>A build disagreed with its own re-run: the member is skipped, never failed.</summary>
    public const string Nondeterministic = "NONDETERMINISTIC";

    /// <summary>The member did not finish within the member budget on some run: skipped.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>Not exercised; <see cref="MemberVerdict.Reason"/> says why.</summary>
    public const string Skipped = "SKIPPED";

    /// <summary>The oracle could not decide (for example a probe never reached the input). The gate fails it.</summary>
    public const string Inconclusive = "INCONCLUSIVE";
}

public sealed record MemberVerdict
{
    public required string Type { get; init; }
    public required string Member { get; init; }

    /// <summary>construct, static, instance, statics (type-initializer state) or enum-values.</summary>
    public required string Kind { get; init; }
    public required string Status { get; init; }
    public string? Reason { get; init; }
    public int Inputs { get; init; }
    public List<string> Witnesses { get; init; } = [];

    /// <summary>For a difference: a hash of every differing stable input with both observations in full. A baseline
    /// entry matches only this exact fingerprint, so any additional or changed difference fails.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>The member was limited to its first <c>LimitedAt</c> inputs because input <c>LimitedAt</c> timed out.</summary>
    public int? LimitedAt { get; init; }

    [JsonIgnore]
    public string Key => Type + "::" + Member;
}

public sealed record OracleOptions
{
    /// <summary>State recursion depth (#1587 default 2).</summary>
    public int Depth { get; init; } = StateRenderer.DefaultDepth;

    /// <summary>Wall-clock budget for all inputs of one member on one run.</summary>
    public TimeSpan MemberTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Seeds the input generator; the same key gives the same inputs.</summary>
    public string SeedKey { get; init; } = "";

    public int MaxWitnesses { get; init; } = 3;

    /// <summary>Members limited to their first N inputs, because input N timed out on an earlier attempt.</summary>
    public IReadOnlyDictionary<string, int> Limits { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Child-process mode: called on the first member timeout with (member key, inputs finished). It must end the
    /// process, because the abandoned thread cannot be stopped and would disturb every later observation; the
    /// parent restarts the file with that member limited. When null (in-process use, for example tests), the
    /// oracle restarts the comparison itself in fresh contexts.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<string, int>? OnTimeout { get; init; }

    /// <summary>Types whose receiver construction timed out on an earlier attempt: their instance members are skipped.</summary>
    public IReadOnlySet<string> ReceiverBans { get; init; } = new HashSet<string>();

    /// <summary>Child mode: receiver construction for this type timed out during planning; must end the process.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<string>? OnPlanTimeout { get; init; }

    /// <summary>Called before each member of each run with (run label, member key): the crash checkpoint.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<string, string>? OnMemberStart { get; init; }

    /// <summary>The member whose inputs are checkpointed one by one (set when locating a crash).</summary>
    public string? TraceMember { get; init; }

    /// <summary>Called before each input of <see cref="TraceMember"/> with (run label, input index).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action<string, int>? OnInputStart { get; init; }

    /// <summary>Probe child: called right before the probed input runs (a crash after it is the subject's).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Action? OnProbeInput { get; init; }
}

/// <summary>How a probe ended.</summary>
public static class ProbeOutcome
{
    /// <summary>The input returned or threw within the budget; the observation is comparable.</summary>
    public const string Finished = "finished";

    /// <summary>The input did not finish within the member budget.</summary>
    public const string Hung = "hung";

    /// <summary>The process died while the input ran (for example a stack overflow).</summary>
    public const string Crashed = "crashed";

    /// <summary>The probe never reached the input (replay, loading or harness failure): not a behavioral observation.</summary>
    public const string Infrastructure = "infrastructure";
}

/// <summary>One replay of a member up to a single input, from clean state, to attribute a timeout or crash.</summary>
public sealed record ProbeResult(string Outcome, double Seconds, string? Observation)
{
    public bool Finished => Outcome == ProbeOutcome.Finished;
}

/// <summary>
/// One build of a subject: the emitted assembly image plus where its dependencies live. Every
/// <see cref="Load"/> makes a fresh collectible context, so each run starts from clean static state.
/// </summary>
public sealed class ArmImage(string name, byte[] image, IReadOnlyDictionary<string, string> dependencyPaths)
{
    public string Name { get; } = name;
    public byte[] Image { get; } = image;
    public IReadOnlyDictionary<string, string> DependencyPaths { get; } = dependencyPaths;

    public LoadedArm Load(string runLabel)
    {
        var context = new OracleLoadContext($"{Name}-{runLabel}", DependencyPaths);
        return new LoadedArm(context, context.LoadFromStream(new MemoryStream(Image)));
    }
}

public sealed class LoadedArm(AssemblyLoadContext context, Assembly assembly) : IDisposable
{
    public Assembly Assembly { get; } = assembly;

    public void Dispose() => context.Unload();
}

/// <summary>
/// Resolves the subject's own dependencies (NuGet packages, project references) from the build's
/// reference paths into this context. Anything else, including the shared framework, comes from the
/// default context.
/// </summary>
internal sealed class OracleLoadContext(string name, IReadOnlyDictionary<string, string> dependencyPaths)
    : AssemblyLoadContext(name, isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName) =>
        assemblyName.Name is { } simple && dependencyPaths.TryGetValue(simple, out var path) ? LoadFromAssemblyPath(path) : null;
}
