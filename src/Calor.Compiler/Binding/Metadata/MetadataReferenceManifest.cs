using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Binding.Metadata;

/// <summary>
/// F-2: metadata reference manifest. The curated whitelist of BCL assemblies
/// Calor programs may bind to, pinned by name and SHA-256 to make the .NET
/// surface area a frozen, reviewable artifact rather than "whatever TPA
/// happens to include at load time".
///
/// The manifest is a subset of <see cref="Calor.Compiler.CodeGen.GeneratedCSharpCompiler.References"/>
/// (see the v0.14 metadata-binding scoping doc §D3). Compiler-internal
/// assemblies (Z3, Microsoft.CodeAnalysis.*, Calor.Compiler) are deliberately
/// excluded so Calor code cannot bind to the compiler's own internals.
///
/// Version drift outside <see cref="SdkVersionRange"/> is a hard failure at
/// load time; SHA drift inside the range is meant to trigger a
/// bot-signed auto-regen review commit (that regen path lands with S1 tooling
/// under <c>scripts/generate-metadata-references-manifest.sh</c>).
/// </summary>
internal sealed record MetadataReferenceManifest(
    [property: JsonPropertyName("sdkVersionRange")] string SdkVersionRange,
    [property: JsonPropertyName("generatedFrom")] string? GeneratedFrom,
    [property: JsonPropertyName("generatedAt")] string? GeneratedAt,
    [property: JsonPropertyName("assemblies")] ImmutableArray<ManifestAssemblyEntry> Assemblies)
{
    private const string EmbeddedResourceName =
        "Calor.Compiler.Resources.metadata-references-manifest.json";

    /// <summary>
    /// Loads the manifest embedded in the compiler assembly. The global tool
    /// therefore uses the same reviewed manifest regardless of its installation
    /// directory or the caller's current working directory.
    /// </summary>
    public static MetadataReferenceManifest Load()
    {
        using var stream = typeof(MetadataReferenceManifest).Assembly
            .GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Compiler resource '{EmbeddedResourceName}' is missing. Rebuild or reinstall calor.");
        return Load(stream, $"embedded resource '{EmbeddedResourceName}'");
    }

    public static MetadataReferenceManifest LoadFrom(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream, $"'{path}'");
    }

    private static MetadataReferenceManifest Load(Stream stream, string source)
    {
        // Round-2 M2 mitigation: strict deserialization. Case-sensitivity
        // matches the JSON schema; trailing commas rejected. A typo in a
        // property name fails loudly rather than silently defaulting.
        var manifest = JsonSerializer.Deserialize<MetadataReferenceManifest>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = false,
            });
        if (manifest is null)
        {
            throw new InvalidOperationException(
                $"Metadata reference manifest from {source} deserialized to null.");
        }
        // Additional invariant checks — default(ImmutableArray<>) has
        // IsDefault=true and enumerating throws NullReferenceException.
        if (string.IsNullOrWhiteSpace(manifest.SdkVersionRange))
        {
            throw new InvalidOperationException(
                $"Metadata reference manifest from {source} is missing required property 'sdkVersionRange'.");
        }
        if (manifest.Assemblies.IsDefault || manifest.Assemblies.Length == 0)
        {
            throw new InvalidOperationException(
                $"Metadata reference manifest from {source} has no 'assemblies' entries.");
        }
        return manifest;
    }
}

/// <summary>
/// One row in the F-2 manifest. AssemblyName is the file name without <c>.dll</c>.
/// SHA-256 is lowercase-hex; drift produces a hard failure with a specific message.
/// </summary>
internal sealed record ManifestAssemblyEntry(
    [property: JsonPropertyName("assemblyName")] string AssemblyName,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("sha256")] string Sha256)
{
    /// <summary>Compute the SHA-256 of a file, lowercase hex — matches manifest format.</summary>
    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
