namespace Calor.Compiler.Effects.IL;

/// <summary>
/// Orchestrates cross-assembly IL analysis to resolve effects for external methods
/// not covered by manifests. Demand-driven: only loads assemblies that contain
/// types referenced by the Calor program.
///
/// Lifecycle: construct once per build, reuse across all file compilations, dispose at end.
/// </summary>
public sealed class ILEffectAnalyzer : IDisposable
{
    private readonly AssemblyIndex _assemblyIndex;
    private readonly ILCallGraphBuilder _callGraphBuilder;
    private readonly StateMachineResolver _stateMachineResolver;
    private readonly TransitiveEffectPropagator _propagator;
    private readonly ILAnalysisOptions _options;

    // Cache: resolved effects per method (populated after analysis)
    private readonly Dictionary<MethodKey, (ILResolutionStatus Status, EffectSet Effects)> _cache = [];

    // Name-only index: for lookups without parameter types
    private readonly Dictionary<(string TypeName, string MethodName), List<MethodKey>> _nameIndex = [];

    private bool _analyzed;
    private bool _disposed;

    public ILEffectAnalyzer(
        IReadOnlyList<string> assemblyPaths,
        EffectResolver manifestResolver,
        ILAnalysisOptions? options = null)
    {
        _options = options ?? new ILAnalysisOptions();
        _assemblyIndex = new AssemblyIndex(assemblyPaths, _options);
        _callGraphBuilder = new ILCallGraphBuilder(_assemblyIndex);
        _stateMachineResolver = new StateMachineResolver(_assemblyIndex);
        _propagator = new TransitiveEffectPropagator(
            _callGraphBuilder, _assemblyIndex, _stateMachineResolver, manifestResolver, _options);
    }

    /// <summary>
    /// Attempts to resolve effects for a method via IL analysis.
    /// Returns null if the method is not found, analysis is Incomplete,
    /// or the method is not in any loaded assembly.
    /// </summary>
    public EffectResolution? TryResolve(string fullyQualifiedType, string methodName,
        string[]? parameterTypes = null)
    {
        if (_disposed) return null;

        // Build parameter signature for lookup
        var paramSig = parameterTypes != null && parameterTypes.Length > 0
            ? $"({string.Join(",", parameterTypes)})"
            : "*"; // Name-only lookup

        var key = new MethodKey(fullyQualifiedType, methodName, paramSig);

        // Try exact match first
        if (paramSig != "*" && _cache.TryGetValue(key, out var exact))
        {
            return exact.Status switch
            {
                ILResolutionStatus.Resolved =>
                    new EffectResolution(EffectResolutionStatus.Resolved, exact.Effects,
                        $"il-analysis"),
                ILResolutionStatus.ResolvedPure =>
                    new EffectResolution(EffectResolutionStatus.PureExplicit, EffectSet.Empty,
                        $"il-analysis"),
                _ => null // Incomplete → fall through to Unknown
            };
        }

        // Name-only lookup: union effects across all overloads
        var nameKey = (fullyQualifiedType, methodName);
        if (_nameIndex.TryGetValue(nameKey, out var overloads))
        {
            var unionEffects = EffectSet.Empty;
            var anyResolved = false;
            var anyIncomplete = false;

            foreach (var overloadKey in overloads)
            {
                if (_cache.TryGetValue(overloadKey, out var overloadResult))
                {
                    switch (overloadResult.Status)
                    {
                        case ILResolutionStatus.Resolved:
                            unionEffects = unionEffects.Union(overloadResult.Effects);
                            anyResolved = true;
                            break;
                        case ILResolutionStatus.ResolvedPure:
                            anyResolved = true;
                            break;
                        case ILResolutionStatus.Incomplete:
                            anyIncomplete = true;
                            break;
                    }
                }
            }

            // If any overload is Incomplete, the union is unreliable — return null
            if (anyIncomplete)
                return null;

            if (anyResolved)
            {
                var status = unionEffects.IsEmpty
                    ? EffectResolutionStatus.PureExplicit
                    : EffectResolutionStatus.Resolved;
                return new EffectResolution(status, unionEffects, "il-analysis");
            }
        }

        return null;
    }

    /// <summary>
    /// Runs transitive analysis from the given external call sites.
    /// Call once per build before querying individual methods.
    /// </summary>
    public void AnalyzeFromCallSites(IEnumerable<(string Type, string Method)> externalCallSites)
    {
        if (_analyzed || _disposed) return;
        _analyzed = true;

        var entryPoints = externalCallSites
            .Select(cs => MethodKey.NameOnly(cs.Type, cs.Method))
            .Distinct()
            .ToList();

        if (entryPoints.Count == 0) return;

        var results = _propagator.Propagate(entryPoints);

        foreach (var (key, value) in results)
        {
            _cache[key] = value;

            // Build name-only index
            var nameKey = key.NameKey;
            if (!_nameIndex.TryGetValue(nameKey, out var list))
            {
                list = [];
                _nameIndex[nameKey] = list;
            }
            if (!list.Contains(key))
                list.Add(key);
        }
    }

    /// <summary>
    /// Number of methods in the analysis cache (for diagnostics/testing).
    /// </summary>
    public int CachedMethodCount => _cache.Count;

    /// <summary>
    /// Number of loaded assemblies (for diagnostics/testing).
    /// </summary>
    public int LoadedAssemblyCount => _assemblyIndex.LoadedAssemblies.Count;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _assemblyIndex.Dispose();
    }
}
