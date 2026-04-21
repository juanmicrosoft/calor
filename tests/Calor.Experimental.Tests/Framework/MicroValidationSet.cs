using System.Text.Json;

namespace Calor.Experimental.Tests.Framework;

/// <summary>
/// A complete micro-validation set for one hypothesis: the manifest + program lists
/// in each of the three required coverage categories.
///
/// Load from disk via <see cref="Load(string)"/>; build in-memory via the constructor
/// for framework unit tests.
/// </summary>
public sealed class MicroValidationSet
{
    public MicroValidationManifest Manifest { get; }
    public IReadOnlyList<string> PositivePrograms { get; }
    public IReadOnlyList<string> NegativePrograms { get; }
    public IReadOnlyList<string> EdgePrograms { get; }

    /// <summary>
    /// Total count of <c>.calr</c> programs across all three categories.
    /// </summary>
    public int TotalPrograms =>
        PositivePrograms.Count + NegativePrograms.Count + EdgePrograms.Count;

    public MicroValidationSet(
        MicroValidationManifest manifest,
        IReadOnlyList<string> positive,
        IReadOnlyList<string> negative,
        IReadOnlyList<string> edge)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        PositivePrograms = positive ?? Array.Empty<string>();
        NegativePrograms = negative ?? Array.Empty<string>();
        EdgePrograms = edge ?? Array.Empty<string>();
    }

    /// <summary>
    /// Load a micro-validation set from a <c>TIERxY-name/</c> directory. The directory
    /// must contain a <c>manifest.json</c> and the <c>positive/</c>, <c>negative/</c>,
    /// <c>edge/</c> subdirectories. Missing subdirectories yield empty program lists —
    /// the coverage audit will flag that as a violation, but loading does not throw.
    /// </summary>
    public static MicroValidationSet Load(string tierDirectory)
    {
        if (string.IsNullOrWhiteSpace(tierDirectory))
            throw new ArgumentException("tierDirectory is required.", nameof(tierDirectory));

        if (!Directory.Exists(tierDirectory))
            throw new DirectoryNotFoundException($"Micro-validation directory not found: {tierDirectory}");

        var manifestPath = Path.Combine(tierDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                $"manifest.json missing in {tierDirectory}. Every TIERxY directory must have a manifest.");

        var manifest = JsonSerializer.Deserialize<MicroValidationManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
            ?? throw new InvalidDataException($"Failed to parse manifest at {manifestPath}");

        return new MicroValidationSet(
            manifest,
            EnumerateCalrFiles(Path.Combine(tierDirectory, "positive")),
            EnumerateCalrFiles(Path.Combine(tierDirectory, "negative")),
            EnumerateCalrFiles(Path.Combine(tierDirectory, "edge")));
    }

    private static IReadOnlyList<string> EnumerateCalrFiles(string dir)
    {
        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        return Directory.GetFiles(dir, "*.calr", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }
}
