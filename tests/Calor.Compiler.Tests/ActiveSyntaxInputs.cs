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

    internal sealed record ExecutableRegistration(string OriginalPath, string TranscriptPath);
    internal enum InputKind { CalorText, Structured, Transcript }

    private static readonly Lazy<FrozenDictionary<string, InputKind>> TypedRegistry = new(() =>
    {
        var root = FindRepoRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, RelativeRoot, "inputs.json")));
        var entries = new Dictionary<string, InputKind>(StringComparer.Ordinal);
        foreach (var (property, kind, extension) in new[]
        {
            ("calorTextPaths", InputKind.CalorText, ".txt"),
            ("structuredPaths", InputKind.Structured, ".json"),
            ("transcriptPaths", InputKind.Transcript, ".json")
        })
        {
            foreach (var entry in document.RootElement.GetProperty(property).EnumerateArray())
            {
                var path = entry.GetString() ?? throw new InvalidDataException("Null typed active input.");
                ValidateExecutablePath(root, path, extension);
                if (kind == InputKind.CalorText && !path.EndsWith(".calr.txt", StringComparison.Ordinal))
                    throw new InvalidDataException($"Not a Calor-text input: {path}.");
                if (!entries.TryAdd(path, kind) || OriginalPaths.Contains(path)
                    || ExecutableRegistrations.Values.Any(executable =>
                        executable.OriginalPath == path || executable.TranscriptPath == path))
                    throw new InvalidDataException($"Overlapping typed active input: {path}.");
            }
        }
        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    });

    private static readonly Lazy<FrozenDictionary<string, ExecutableRegistration>> Executables = new(() =>
    {
        var root = FindRepoRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, RelativeRoot, "inputs.json")));
        var entries = document.RootElement.GetProperty("executables").EnumerateArray()
            .Select(entry => new ExecutableRegistration(
                entry.GetProperty("originalPath").GetString()
                    ?? throw new InvalidDataException("Null executable original path."),
                entry.GetProperty("transcriptPath").GetString()
                    ?? throw new InvalidDataException("Null executable transcript path.")))
            .ToArray();
        foreach (var entry in entries)
        {
            ValidateExecutablePath(root, entry.OriginalPath, ".py");
            ValidateExecutablePath(root, entry.TranscriptPath, ".txt");
        }
        if (entries.Select(entry => entry.OriginalPath).Distinct(StringComparer.Ordinal).Count() != entries.Length
            || entries.Select(entry => entry.TranscriptPath).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new InvalidDataException("Duplicate executable or transcript in the active syntax registry.");
        return entries.ToFrozenDictionary(entry => entry.OriginalPath, StringComparer.Ordinal);
    });

    private static readonly Lazy<FrozenSet<string>> Registry = new(() =>
    {
        var root = FindRepoRoot();
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

    internal static IReadOnlyDictionary<string, ExecutableRegistration> ExecutableRegistrations
        => Executables.Value;

    internal static IReadOnlyDictionary<string, InputKind> TypedRegistrations => TypedRegistry.Value;
    internal static string RepositoryRoot => FindRepoRoot();

    internal static string ForCurrentTypedInput(string repoRoot, string originalPath, InputKind kind)
    {
        if (!TypedRegistrations.TryGetValue(originalPath, out var registered) || registered != kind)
            throw new ArgumentException("Pass an exactly registered original of the required input kind.", nameof(originalPath));
        return RequiredMirror(repoRoot, originalPath);
    }

    internal static string ForCurrentTranscript(string repoRoot, string originalPath)
        => TypedRegistrations.ContainsKey(originalPath)
            ? ForCurrentTypedInput(repoRoot, originalPath, InputKind.Transcript)
            : ForCurrentCompiler(repoRoot, originalPath);

    private static string FindRepoRoot()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Directory.Build.props")))
            root = Directory.GetParent(root)?.FullName
                ?? throw new DirectoryNotFoundException("Repository root not found.");
        return root;
    }

    internal static (string ScriptPath, string TranscriptPath) ForCurrentExecutable(
        string repoRoot, string originalPath)
    {
        if (!ExecutableRegistrations.TryGetValue(originalPath, out var registration))
            throw new ArgumentException("Pass an exactly registered executable original path.", nameof(originalPath));
        return (RequiredMirror(repoRoot, registration.OriginalPath),
            RequiredMirror(repoRoot, registration.TranscriptPath));
    }

    private static string RequiredMirror(string repoRoot, string relativePath)
    {
        var mirror = Path.Combine(repoRoot, RelativeRoot, relativePath);
        if (!File.Exists(mirror))
            throw new FileNotFoundException($"Registered active input is missing for {relativePath}.", mirror);
        return mirror;
    }

    private static void ValidateExecutablePath(string repoRoot, string path, string extension)
    {
        var canonical = Path.GetRelativePath(repoRoot, Path.GetFullPath(path, repoRoot)).Replace('\\', '/');
        if (Path.IsPathRooted(path) || path.Contains('\\')
            || !string.Equals(path, canonical, StringComparison.Ordinal)
            || path.Split('/').Any(segment => segment is "" or "." or "..")
            || IsActiveCopy(path)
            || !string.Equals(Path.GetExtension(path), extension, StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid active executable registration path: {path}.");
    }

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
        if (TypedRegistrations.ContainsKey(relative))
            throw new InvalidDataException($"Use the registered typed resolver for {relative}.");

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
