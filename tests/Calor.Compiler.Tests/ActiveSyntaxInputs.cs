using System.Collections.Frozen;
using System.Text.Json;

namespace Calor.Compiler.Tests;

/// <summary>
/// Explicit current-compiler inputs for frozen subjects. Historical byte/hash checks
/// must keep reading the originals; these copies are not historical measurements.
/// </summary>
internal static class ActiveSyntaxInputs
{
    internal const string RelativeRoot = "tests/TestData/ActiveSyntax/";

    private static readonly Lazy<FrozenSet<string>> Registry = new(() =>
    {
        var root = CliTestHarness.FindRepoRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, RelativeRoot, "inputs.json")));
        var paths = document.RootElement.GetProperty("originalPaths").EnumerateArray()
            .Select(path => path.GetString() ?? throw new InvalidDataException("Null active input path."))
            .ToArray();
        var registered = paths.ToFrozenSet(StringComparer.Ordinal);
        if (registered.Count != paths.Length)
            throw new InvalidDataException("Duplicate original in the active syntax input registry.");
        return registered;
    });

    internal static IReadOnlySet<string> OriginalPaths => Registry.Value;

    internal static bool IsActiveCopy(string repoRelativePath)
        => repoRelativePath.StartsWith(RelativeRoot, StringComparison.Ordinal);

    internal static string ForCurrentCompiler(string repoRoot, string originalPath)
    {
        var original = Path.GetFullPath(originalPath, repoRoot);
        var relative = Path.GetRelativePath(repoRoot, original).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal))
            throw new ArgumentException("The original must be inside the repository.", nameof(originalPath));
        if (IsActiveCopy(relative))
            throw new ArgumentException("Pass the original subject, not its active copy.", nameof(originalPath));

        var active = Path.Combine(repoRoot, RelativeRoot, relative);
        if (OriginalPaths.Contains(relative))
        {
            if (!File.Exists(active))
                throw new FileNotFoundException($"Registered active input is missing for {relative}.", active);
            return active;
        }

        if (File.Exists(active))
            throw new InvalidDataException($"Unregistered active syntax input: {relative}.");
        return original;
    }
}
