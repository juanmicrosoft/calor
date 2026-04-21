namespace Calor.Compiler.Effects.IL;

/// <summary>
/// Configuration for compile-time IL analysis behavior.
/// </summary>
public sealed class ILAnalysisOptions
{
    /// <summary>
    /// Maximum call graph depth to traverse (default: 50).
    /// Prevents runaway analysis on deeply nested library code.
    /// </summary>
    public int MaxDepth { get; init; } = 50;

    /// <summary>
    /// Maximum number of concrete implementations to union for a virtual call (default: 5).
    /// If exceeded, the call is treated as Incomplete (falls back to manifest/Unknown).
    /// </summary>
    public int MaxVirtualImplementations { get; init; } = 5;

    /// <summary>
    /// Maximum number of methods to visit during analysis (default: 10,000).
    /// Safety valve for pathological assemblies. Emits Calor0416 when exceeded.
    /// </summary>
    public int MaxVisitedMethods { get; init; } = 10_000;

    /// <summary>
    /// Interface types to skip during IL analysis.
    /// These are known to produce 0% resolution via IL (validated by prototype).
    /// Tier B manifests handle them instead.
    /// </summary>
    public IReadOnlySet<string> SkipInterfaces { get; init; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "Microsoft.Extensions.Logging.ILogger",
        "Microsoft.Extensions.Logging.ILogger`1",
        "Microsoft.Extensions.Configuration.IConfiguration",
        "Microsoft.Extensions.Configuration.IConfigurationSection",
        "Microsoft.Extensions.Hosting.IHost",
        "Microsoft.Extensions.DependencyInjection.IServiceProvider",
        "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        "MediatR.IMediator",
        "MediatR.ISender"
    };

    /// <summary>
    /// Ubiquitous interfaces to skip during implementation resolution.
    /// These produce hundreds/thousands of implementations and are meaningless for effect analysis.
    /// </summary>
    public IReadOnlySet<string> UbiquitousInterfaces { get; init; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "System.IDisposable",
        "System.IAsyncDisposable",
        "System.IComparable",
        "System.IComparable`1",
        "System.IEquatable`1",
        "System.IFormattable",
        "System.IConvertible",
        "System.Collections.IEnumerable",
        "System.Collections.IEnumerator",
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.Generic.IEnumerator`1",
        "System.Collections.Generic.ICollection`1",
        "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IDictionary`2",
        "System.ICloneable"
    };

    /// <summary>
    /// Path to the .NET shared runtime directory for resolving BCL implementation assemblies.
    /// e.g., "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.0\"
    /// </summary>
    public string? RuntimeDirectory { get; init; }

    /// <summary>
    /// Path to the NuGet global packages folder.
    /// e.g., "C:\Users\user\.nuget\packages\"
    /// </summary>
    public string? NuGetPackageRoot { get; init; }

    /// <summary>
    /// Path to the project's .deps.json file for ref→impl assembly resolution.
    /// </summary>
    public string? DepsFilePath { get; init; }
}
