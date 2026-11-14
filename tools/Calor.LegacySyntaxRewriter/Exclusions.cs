using System.Text.Json;

namespace Calor.Compiler.LegacySyntax;

/// <summary>Inputs the rewriter must never touch.</summary>
internal sealed class Exclusions
{
    private readonly HashSet<string> _registeredOriginals;

    private Exclusions(HashSet<string> registeredOriginals) => _registeredOriginals = registeredOriginals;

    public string RepoRoot { get; private init; } = "";

    public static Exclusions Load(string repoRoot)
    {
        var registry = System.IO.Path.Combine(repoRoot, "tests/TestData/ActiveSyntax/inputs.json");
        var originals = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(registry))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(registry));
            foreach (var entry in doc.RootElement.GetProperty("originalPaths").EnumerateArray())
                originals.Add(entry.GetString()!);
        }
        return new Exclusions(originals) { RepoRoot = repoRoot };
    }

    public string Relative(string path)
        => System.IO.Path.GetRelativePath(RepoRoot, System.IO.Path.GetFullPath(path)).Replace('\\', '/');

    /// <summary>
    /// A reason this path is off limits, or null. Registered ActiveSyntax originals are
    /// historical inputs whose current copies already live under tests/TestData/ActiveSyntax;
    /// they are only rewritten when <paramref name="includeRegistered"/> is set.
    /// </summary>
    public string? Reason(string path, bool includeRegistered)
    {
        var rel = Relative(path);
        if (rel.StartsWith("bench/phase0-agent-native/", StringComparison.Ordinal))
            return "excluded:historical-agent-output";
        if (rel.StartsWith("docs/plans/", StringComparison.Ordinal)
            || rel.StartsWith("docs/design/spikes/", StringComparison.Ordinal))
            return "excluded:historical-docs";
        if (rel.StartsWith("tests/TestData/ActiveSyntax/", StringComparison.Ordinal))
            return "excluded:active-syntax-mirror";
        if (rel.StartsWith("tests/", StringComparison.Ordinal) && rel.Contains("/Snapshots/", StringComparison.Ordinal)
            && rel.EndsWith(".approved.calr", StringComparison.Ordinal))
            return "excluded:approved-snapshot";
        if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal))
            return "excluded:build-output";
        if (!includeRegistered && _registeredOriginals.Contains(rel))
            return "excluded:registered-original";
        return null;
    }
}
