using System.Text.Json.Serialization;

namespace Calor.Compiler.Migration;

/// <summary>
/// #1144: the path that actually produced each recorded loss. Every conversion
/// surface (CLI convert, project migration, MCP convert/migrate/batch, the library)
/// reports these values, so a reader can tell which mechanism preserved a member
/// without inferring it from the flags that were passed.
/// </summary>
public static class ConversionPath
{
    /// <summary>The converter preserved a construct it does not support natively, whatever the rescue/passthrough options.</summary>
    public const string Interop = "interop";

    /// <summary>Automatic rescue: the #717 post-validation fallback, enabled by a default rather than by a passthrough request.</summary>
    public const string Rescue = "rescue";

    /// <summary>Preservation that happened only because the caller asked for passthrough (<c>PassthroughOnError</c>).</summary>
    public const string Passthrough = "passthrough";

    /// <summary>A lossy substitution (TODO fallback, stripped preprocessor branch, removed directive).</summary>
    public const string Lossy = "lossy";

    /// <summary>A construct dropped from the output.</summary>
    public const string Dropped = "dropped";

    internal static string ForKind(ConversionLossKind kind) => kind switch
    {
        ConversionLossKind.InteropPreserved or ConversionLossKind.EmitterFallback => Interop,
        ConversionLossKind.Dropped => Dropped,
        _ => Lossy
    };
}

/// <summary>#1144: the failure that fired a rescue or passthrough preservation.</summary>
public static class ConversionTrigger
{
    /// <summary>The member's emitted Calor did not parse.</summary>
    public const string ParseFailure = "parse-failure";

    /// <summary>The member's emitted Calor parsed but did not survive the Calor → C# round trip.</summary>
    public const string RoundTripFailure = "round-trip-failure";

    /// <summary>The visitor met a construct it cannot convert.</summary>
    public const string UnsupportedConstruct = "unsupported-construct";
}

/// <summary>#1144: the option or default that made a rescue or passthrough reachable.</summary>
public static class ConversionEnabledBy
{
    /// <summary>The lossless fidelity contract (the default on every surface) preserves C#.</summary>
    public const string LosslessFidelity = "lossless-fidelity";

    /// <summary><see cref="ConversionMode.Interop"/> preserves C#.</summary>
    public const string InteropMode = "interop-mode";

    /// <summary><see cref="ConversionOptions.RescueUnusableMembers"/> (the CLI sets it by default).</summary>
    public const string RescueUnusableMembers = "rescueUnusableMembers";

    /// <summary><see cref="ConversionOptions.PassthroughOnError"/> (<c>--passthrough</c>, MCP <c>passthroughOnError</c>).</summary>
    public const string PassthroughOnError = "passthroughOnError";
}

/// <summary>
/// #1144: per-file summary of the paths a conversion took, with the options that
/// applied. Additive to the existing loss counters; <see cref="SchemaVersion"/>
/// versions this object only.
/// </summary>
public sealed record ConversionPathSummary
{
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary><c>native</c>, <c>preserved</c> (interop/rescue/passthrough, no loss), <c>lossy</c>, or <c>refused</c> (conversion failed).</summary>
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("interop")]
    public int Interop { get; init; }

    [JsonPropertyName("rescue")]
    public int Rescue { get; init; }

    [JsonPropertyName("passthrough")]
    public int Passthrough { get; init; }

    [JsonPropertyName("lossy")]
    public int Lossy { get; init; }

    [JsonPropertyName("dropped")]
    public int Dropped { get; init; }

    /// <summary>Trigger → count over the rescue and passthrough entries.</summary>
    [JsonPropertyName("triggers")]
    public required IReadOnlyDictionary<string, int> Triggers { get; init; }

    /// <summary>Whether <see cref="ConversionOptions.RescueUnusableMembers"/> was on for this conversion.</summary>
    [JsonPropertyName("rescueUnusableMembers")]
    public bool RescueUnusableMembers { get; init; }

    /// <summary>Whether <see cref="ConversionOptions.PassthroughOnError"/> was on for this conversion.</summary>
    [JsonPropertyName("passthroughOnError")]
    public bool PassthroughOnError { get; init; }

    public static ConversionPathSummary From(
        IEnumerable<ConversionLoss> losses,
        bool success,
        bool rescueUnusableMembers,
        bool passthroughOnError)
    {
        var list = losses.ToList();
        int Count(string path) => list.Count(l => l.Path == path);
        var lossy = Count(ConversionPath.Lossy);
        var dropped = Count(ConversionPath.Dropped);
        return new ConversionPathSummary
        {
            Outcome = "native",
            Interop = Count(ConversionPath.Interop),
            Rescue = Count(ConversionPath.Rescue),
            Passthrough = Count(ConversionPath.Passthrough),
            Lossy = lossy,
            Dropped = dropped,
            Triggers = list.Where(l => l.Trigger != null)
                .GroupBy(l => l.Trigger!)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count()),
            RescueUnusableMembers = rescueUnusableMembers,
            PassthroughOnError = passthroughOnError
        }.WithSuccess(success);
    }

    /// <summary>The same counts with the outcome recomputed for whether the file was written.</summary>
    public ConversionPathSummary WithSuccess(bool success) => this with
    {
        Outcome = !success ? "refused"
            : Lossy + Dropped > 0 ? "lossy"
            : Interop + Rescue + Passthrough > 0 ? "preserved"
            : "native"
    };

    /// <summary>Summary of <paramref name="result"/>; <paramref name="success"/> overrides when a surface refuses after conversion.</summary>
    public static ConversionPathSummary From(ConversionResult result, bool? success = null)
        => From(result.Losses, success ?? result.Success,
            result.Context.RescueUnusableMembers, result.Context.PassthroughOnError);

    /// <summary>
    /// One human-readable line naming the preservation paths, or null when no member
    /// was preserved. <paramref name="passthroughName"/> is the surface's spelling of
    /// the passthrough request (<c>--passthrough</c>, <c>passthroughOnError</c>).
    /// </summary>
    public static string? Describe(IEnumerable<ConversionLoss> losses, string passthroughName)
    {
        var list = losses.ToList();
        string Triggers(string path) => string.Join(", ", list
            .Where(l => l.Path == path && l.Trigger != null)
            .GroupBy(l => l.Trigger!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {g.Count()}"));
        var parts = new List<string>();
        var rescue = list.Count(l => l.Path == ConversionPath.Rescue);
        if (rescue > 0)
            parts.Add($"{rescue} by automatic rescue ({Triggers(ConversionPath.Rescue)}; no passthrough request needed)");
        var passthrough = list.Count(l => l.Path == ConversionPath.Passthrough);
        if (passthrough > 0)
            parts.Add($"{passthrough} by requested passthrough ({passthroughName}; {Triggers(ConversionPath.Passthrough)})");
        var interop = list.Count(l => l.Path == ConversionPath.Interop);
        if (interop > 0)
            parts.Add($"{interop} by converter interop (unsupported construct)");
        return parts.Count == 0 ? null : "Preservation paths: " + string.Join("; ", parts);
    }
}
