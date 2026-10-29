using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Calor.Compiler.Tests;

internal static class TrustedPlatformReferences
{
    private static readonly Lazy<ImmutableArray<PortableExecutableReference>> References = new(() =>
    {
        var paths = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return paths
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    });

    internal static ImmutableArray<PortableExecutableReference> All => References.Value;
}
