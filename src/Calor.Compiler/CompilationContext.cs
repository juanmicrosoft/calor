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
