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
