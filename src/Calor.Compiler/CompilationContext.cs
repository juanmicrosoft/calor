using Calor.Compiler.Effects;

namespace Calor.Compiler;

/// <summary>
/// Holds shared, reusable state across multiple file compilations within a single build.
/// Constructed once by the MSBuild task, passed to each Program.Compile() call via CompilationOptions.
///
/// Separates "shared live objects" from "configuration" — CompilationOptions holds value-type config,
/// CompilationContext holds stateful services that are expensive to construct.
/// </summary>
public sealed class CompilationContext : IDisposable
{
    /// <summary>
    /// Pre-built effect resolver with IL analyzer attached (if IL analysis is enabled).
    /// Reused across all file compilations in a single build.
    /// Null when IL analysis is disabled or no assembly references are available.
    /// </summary>
    public EffectResolver? SharedEffectResolver { get; init; }

    public void Dispose()
    {
        // EffectResolver doesn't implement IDisposable directly,
        // but it may hold an ILEffectAnalyzer that does.
        // The ILEffectAnalyzer is disposed by whoever constructed the context.
    }
}

    /// <summary>
    /// Canonical-path helpers for project ownership and write confinement.
    /// Containment decisions compare real filesystem locations, not lexical paths.
    /// </summary>
    internal static class CanonicalPath
    {
        private const int MaxLinkDepth = 40;

        /// <summary>
        /// Resolves symlinks in every component of the path. Nonexistent trailing
        /// components are appended lexically.
        /// </summary>
        public static string Resolve(string path) => ResolveCore(Path.GetFullPath(path), 0);

        public static bool IsUnder(string canonicalPath, string canonicalRoot)
            => string.Equals(canonicalPath, canonicalRoot, StringComparison.Ordinal)
               || canonicalPath.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        private static string ResolveCore(string full, int depth)
        {
            if (depth > MaxLinkDepth)
                throw new IOException($"Too many levels of symbolic links resolving '{full}'");

            var root = Path.GetPathRoot(full)!;
            var parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var current = root.TrimEnd(Path.DirectorySeparatorChar);
            if (current.Length == 0)
                current = root;

            for (var i = 0; i < parts.Length; i++)
            {
                current = Path.Combine(current, parts[i]);

                FileSystemInfo info = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);
                if (!info.Exists)
                {
                    for (var j = i + 1; j < parts.Length; j++)
                        current = Path.Combine(current, parts[j]);
                    return current;
                }

                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    var remainder = string.Join(Path.DirectorySeparatorChar, parts[(i + 1)..]);
                    var combined = remainder.Length == 0
                        ? target.FullName
                        : Path.Combine(target.FullName, remainder);
                    return ResolveCore(Path.GetFullPath(combined), depth + 1);
                }
            }

            return current;
        }
    }
