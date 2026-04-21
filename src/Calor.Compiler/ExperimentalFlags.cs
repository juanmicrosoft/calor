namespace Calor.Compiler;

/// <summary>
/// Named on/off toggles for experimental compiler features, used by Phase 0+ of the
/// Calor-native type-system research plan (<c>docs/plans/calor-native-type-system-v2.md</c>).
///
/// Each experimental feature lands behind a flag. Flags are named in
/// <c>docs/experiments/registry.json</c> and referenced by hypothesis ID (e.g.,
/// <c>TIER1A-flow-option-tracking</c>).
///
/// On the CLI: <c>calor --experimental &lt;flag-name&gt; ...</c> (repeatable).
/// In MSBuild: <c>&lt;CalorExperimentalFlags&gt;flag1;flag2&lt;/CalorExperimentalFlags&gt;</c>.
///
/// Flag names are case-insensitive. Unknown flag names are accepted silently — the
/// compiler does not maintain a central enum of valid flags, because features are
/// added and removed frequently behind the scenes of the research program. A feature
/// that no longer exists ignores its flag; a feature gated on an unknown name stays
/// disabled. This matches the design of <c>--experimental-*</c> flags in other
/// compilers (TypeScript, Roslyn) where flag lifetimes are intentionally short.
/// </summary>
public sealed class ExperimentalFlags
{
    private readonly HashSet<string> _enabled;

    public ExperimentalFlags()
    {
        _enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public ExperimentalFlags(IEnumerable<string> flagNames) : this()
    {
        foreach (var name in flagNames)
        {
            Enable(name);
        }
    }

    /// <summary>
    /// Enable a flag. No-op if already enabled. Whitespace-trimmed; empty/null names ignored.
    /// </summary>
    public void Enable(string? flagName)
    {
        if (string.IsNullOrWhiteSpace(flagName))
            return;
        _enabled.Add(flagName.Trim());
    }

    /// <summary>
    /// Whether a flag is currently enabled. Case-insensitive.
    /// </summary>
    public bool IsEnabled(string flagName)
    {
        if (string.IsNullOrWhiteSpace(flagName))
            return false;
        return _enabled.Contains(flagName.Trim());
    }

    /// <summary>
    /// All enabled flag names, in no defined order.
    /// </summary>
    public IReadOnlyCollection<string> EnabledFlags => _enabled;

    /// <summary>
    /// Number of enabled flags.
    /// </summary>
    public int Count => _enabled.Count;

    /// <summary>
    /// Parse a semicolon- or comma-separated list of flag names (e.g., from an MSBuild property).
    /// Whitespace around separators is trimmed; empty segments are skipped.
    /// </summary>
    public static ExperimentalFlags Parse(string? delimited)
    {
        var flags = new ExperimentalFlags();
        if (string.IsNullOrWhiteSpace(delimited))
            return flags;

        foreach (var token in delimited.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            flags.Enable(token);
        }
        return flags;
    }

    /// <summary>
    /// A fixed, always-empty instance for code paths that need a default.
    /// </summary>
    public static ExperimentalFlags None { get; } = new();
}
