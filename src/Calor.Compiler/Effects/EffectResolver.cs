using Calor.Compiler.Effects.Manifests;

namespace Calor.Compiler.Effects;

/// <summary>
/// Resolves effects for .NET method calls using a layered approach.
/// Resolution order:
/// 1. Specific method signature in type mapping
/// 2. Wildcard "*" in type mapping
/// 3. DefaultEffects on type
/// 4. NamespaceDefaults matching namespace pattern
/// 5. Built-in effects catalog
/// 6. Unknown
/// </summary>
public sealed class EffectResolver
{
    private readonly ManifestLoader _manifestLoader;
    private readonly EffectsCatalog _builtInCatalog;
    private readonly Dictionary<string, ResolvedTypeInfo> _typeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffectResolution> _methodCache = new(StringComparer.Ordinal);
    private bool _initialized;

    public EffectResolver(ManifestLoader? manifestLoader = null, EffectsCatalog? builtInCatalog = null)
    {
        _manifestLoader = manifestLoader ?? new ManifestLoader();
        _builtInCatalog = builtInCatalog ?? EffectsCatalog.CreateDefault();
    }

    /// <summary>
    /// Initialize the resolver by loading all manifests.
    /// </summary>
    public void Initialize(string? projectDirectory = null, string? solutionDirectory = null)
    {
        if (_initialized) return;

        _manifestLoader.LoadAll(projectDirectory, solutionDirectory);
        BuildTypeCache();
        _initialized = true;
    }

    /// <summary>
    /// Resolves effects for a method call.
    /// </summary>
    /// <param name="fullyQualifiedType">The fully-qualified type name (e.g., "System.IO.File")</param>
    /// <param name="methodName">The method name (e.g., "ReadAllText")</param>
    /// <param name="parameterTypes">Optional parameter types for overload resolution</param>
    public EffectResolution Resolve(string fullyQualifiedType, string methodName, params string[] parameterTypes)
    {
        EnsureInitialized();

        // Build cache key
        var signature = BuildSignature(fullyQualifiedType, methodName, parameterTypes);
        if (_methodCache.TryGetValue(signature, out var cached))
            return cached;

        var resolution = ResolveInternal(fullyQualifiedType, methodName, parameterTypes, signature);
        _methodCache[signature] = resolution;
        return resolution;
    }

    /// <summary>
    /// Resolves effects for a property getter.
    /// </summary>
    public EffectResolution ResolveGetter(string fullyQualifiedType, string propertyName)
    {
        EnsureInitialized();

        var signature = $"{fullyQualifiedType}::get_{propertyName}()";
        if (_methodCache.TryGetValue(signature, out var cached))
            return cached;

        var resolution = ResolveGetterInternal(fullyQualifiedType, propertyName, signature);
        _methodCache[signature] = resolution;
        return resolution;
    }

    /// <summary>
    /// Resolves effects for a property setter.
    /// </summary>
    public EffectResolution ResolveSetter(string fullyQualifiedType, string propertyName)
    {
        EnsureInitialized();

        var signature = $"{fullyQualifiedType}::set_{propertyName}()";
        if (_methodCache.TryGetValue(signature, out var cached))
            return cached;

        var resolution = ResolveSetterInternal(fullyQualifiedType, propertyName, signature);
        _methodCache[signature] = resolution;
        return resolution;
    }

    /// <summary>
    /// Resolves effects for a constructor.
    /// </summary>
    public EffectResolution ResolveConstructor(string fullyQualifiedType, params string[] parameterTypes)
    {
        EnsureInitialized();

        var paramSig = $"({string.Join(",", parameterTypes)})";
        var signature = $"{fullyQualifiedType}::.ctor{paramSig}";

        if (_methodCache.TryGetValue(signature, out var cached))
            return cached;

        var resolution = ResolveConstructorInternal(fullyQualifiedType, parameterTypes, signature);
        _methodCache[signature] = resolution;
        return resolution;
    }

    /// <summary>
    /// Gets any errors encountered during manifest loading.
    /// </summary>
    public IReadOnlyList<string> LoadErrors => _manifestLoader.LoadErrors;

    private EffectResolution ResolveInternal(string type, string method, string[] parameterTypes, string signature)
    {
        // 1. Check built-in catalog first (highest precision)
        var builtInEffects = _builtInCatalog.TryGetEffects(signature);
        if (builtInEffects != null)
        {
            return new EffectResolution(
                builtInEffects.IsEmpty ? EffectResolutionStatus.PureExplicit : EffectResolutionStatus.Resolved,
                builtInEffects,
                "built-in");
        }

        // 2. Check type cache from manifests
        if (_typeCache.TryGetValue(type, out var typeInfo))
        {
            // 2a. Try specific method with parameters
            if (parameterTypes.Length > 0)
            {
                var paramSig = $"{method}({string.Join(",", parameterTypes)})";
                if (typeInfo.Methods.TryGetValue(paramSig, out var specificEffects))
                {
                    return CreateResolution(specificEffects, typeInfo.Source);
                }
            }

            // 2b. Try method name without parameters
            if (typeInfo.Methods.TryGetValue(method, out var methodEffects))
            {
                return CreateResolution(methodEffects, typeInfo.Source);
            }

            // 2c. Try wildcard
            if (typeInfo.Methods.TryGetValue("*", out var wildcardEffects))
            {
                return CreateResolution(wildcardEffects, typeInfo.Source);
            }

            // 2d. Try default effects on type
            if (typeInfo.DefaultEffects != null)
            {
                return CreateResolution(typeInfo.DefaultEffects, typeInfo.Source);
            }
        }

        // 3. Check namespace defaults
        var nsResolution = ResolveFromNamespaceDefaults(type);
        if (nsResolution != null)
            return nsResolution;

        // 4. Unknown
        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution ResolveGetterInternal(string type, string propertyName, string signature)
    {
        // Check built-in catalog
        var builtInEffects = _builtInCatalog.TryGetEffects(signature);
        if (builtInEffects != null)
        {
            return new EffectResolution(
                builtInEffects.IsEmpty ? EffectResolutionStatus.PureExplicit : EffectResolutionStatus.Resolved,
                builtInEffects,
                "built-in");
        }

        // Check type cache
        if (_typeCache.TryGetValue(type, out var typeInfo))
        {
            if (typeInfo.Getters.TryGetValue(propertyName, out var getterEffects))
            {
                return CreateResolution(getterEffects, typeInfo.Source);
            }

            // Fall back to default effects
            if (typeInfo.DefaultEffects != null)
            {
                return CreateResolution(typeInfo.DefaultEffects, typeInfo.Source);
            }
        }

        // Check namespace defaults
        var nsResolution = ResolveFromNamespaceDefaults(type);
        if (nsResolution != null)
            return nsResolution;

        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution ResolveSetterInternal(string type, string propertyName, string signature)
    {
        // Check built-in catalog
        var builtInEffects = _builtInCatalog.TryGetEffects(signature);
        if (builtInEffects != null)
        {
            return new EffectResolution(
                builtInEffects.IsEmpty ? EffectResolutionStatus.PureExplicit : EffectResolutionStatus.Resolved,
                builtInEffects,
                "built-in");
        }

        // Check type cache
        if (_typeCache.TryGetValue(type, out var typeInfo))
        {
            if (typeInfo.Setters.TryGetValue(propertyName, out var setterEffects))
            {
                return CreateResolution(setterEffects, typeInfo.Source);
            }

            // Fall back to default effects
            if (typeInfo.DefaultEffects != null)
            {
                return CreateResolution(typeInfo.DefaultEffects, typeInfo.Source);
            }
        }

        // Check namespace defaults
        var nsResolution = ResolveFromNamespaceDefaults(type);
        if (nsResolution != null)
            return nsResolution;

        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution ResolveConstructorInternal(string type, string[] parameterTypes, string signature)
    {
        // Check built-in catalog
        var builtInEffects = _builtInCatalog.TryGetEffects(signature);
        if (builtInEffects != null)
        {
            return new EffectResolution(
                builtInEffects.IsEmpty ? EffectResolutionStatus.PureExplicit : EffectResolutionStatus.Resolved,
                builtInEffects,
                "built-in");
        }

        // Check type cache
        if (_typeCache.TryGetValue(type, out var typeInfo))
        {
            var paramSig = $"({string.Join(",", parameterTypes)})";
            if (typeInfo.Constructors.TryGetValue(paramSig, out var ctorEffects))
            {
                return CreateResolution(ctorEffects, typeInfo.Source);
            }

            // Try parameterless if no exact match
            if (typeInfo.Constructors.TryGetValue("()", out var defaultCtorEffects))
            {
                return CreateResolution(defaultCtorEffects, typeInfo.Source);
            }

            // Fall back to default effects
            if (typeInfo.DefaultEffects != null)
            {
                return CreateResolution(typeInfo.DefaultEffects, typeInfo.Source);
            }
        }

        // Check namespace defaults
        var nsResolution = ResolveFromNamespaceDefaults(type);
        if (nsResolution != null)
            return nsResolution;

        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution? ResolveFromNamespaceDefaults(string type)
    {
        // Extract namespace from type
        var lastDot = type.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        var ns = type[..lastDot];

        // Check all manifests for namespace defaults (higher priority manifests win)
        var orderedManifests = _manifestLoader.LoadedManifests
            .OrderByDescending(m => m.Source.Priority);

        foreach (var (manifest, source) in orderedManifests)
        {
            // Try exact namespace match
            if (manifest.NamespaceDefaults.TryGetValue(ns, out var effects))
            {
                return CreateResolution(effects, source.FilePath);
            }

            // Try wildcard patterns
            foreach (var (pattern, patternEffects) in manifest.NamespaceDefaults)
            {
                if (pattern.EndsWith(".*") && ns.StartsWith(pattern[..^2]))
                {
                    return CreateResolution(patternEffects, source.FilePath);
                }
            }
        }

        return null;
    }

    private void BuildTypeCache()
    {
        // Process manifests in priority order (lower to higher, so higher priority wins)
        var orderedManifests = _manifestLoader.LoadedManifests
            .OrderBy(m => m.Source.Priority);

        foreach (var (manifest, source) in orderedManifests)
        {
            foreach (var mapping in manifest.Mappings)
            {
                var typeInfo = new ResolvedTypeInfo(source.FilePath);

                // Copy default effects
                if (mapping.DefaultEffects != null)
                {
                    typeInfo.DefaultEffects = mapping.DefaultEffects;
                }

                // Copy methods
                if (mapping.Methods != null)
                {
                    foreach (var (method, effects) in mapping.Methods)
                    {
                        typeInfo.Methods[method] = effects;
                    }
                }

                // Copy getters
                if (mapping.Getters != null)
                {
                    foreach (var (prop, effects) in mapping.Getters)
                    {
                        typeInfo.Getters[prop] = effects;
                    }
                }

                // Copy setters
                if (mapping.Setters != null)
                {
                    foreach (var (prop, effects) in mapping.Setters)
                    {
                        typeInfo.Setters[prop] = effects;
                    }
                }

                // Copy constructors
                if (mapping.Constructors != null)
                {
                    foreach (var (sig, effects) in mapping.Constructors)
                    {
                        typeInfo.Constructors[sig] = effects;
                    }
                }

                // This will overwrite if already exists (higher priority wins)
                _typeCache[mapping.Type] = typeInfo;
            }
        }
    }

    private static EffectResolution CreateResolution(List<string> effectCodes, string source)
    {
        var effectSet = effectCodes.Count == 0
            ? EffectSet.Empty
            : EffectSet.From(effectCodes.ToArray());

        var status = effectSet.IsEmpty
            ? EffectResolutionStatus.PureExplicit
            : EffectResolutionStatus.Resolved;

        return new EffectResolution(status, effectSet, source);
    }

    private static string BuildSignature(string type, string method, string[] parameterTypes)
    {
        var parameters = string.Join(",", parameterTypes);
        return $"{type}::{method}({parameters})";
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            Initialize();
        }
    }

    /// <summary>
    /// Cached type information from manifests.
    /// </summary>
    private sealed class ResolvedTypeInfo
    {
        public string Source { get; }
        public List<string>? DefaultEffects { get; set; }
        public Dictionary<string, List<string>> Methods { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Getters { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Setters { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, List<string>> Constructors { get; } = new(StringComparer.Ordinal);

        public ResolvedTypeInfo(string source)
        {
            Source = source;
        }
    }
}

/// <summary>
/// Result of resolving effects for a method call.
/// </summary>
public sealed record EffectResolution(
    EffectResolutionStatus Status,
    EffectSet Effects,
    string Source);

/// <summary>
/// Status of effect resolution.
/// </summary>
public enum EffectResolutionStatus
{
    /// <summary>
    /// Effects were resolved from a manifest or built-in catalog.
    /// </summary>
    Resolved,

    /// <summary>
    /// Method was explicitly marked as pure (no effects).
    /// </summary>
    PureExplicit,

    /// <summary>
    /// Method's effects are unknown (not in any manifest).
    /// </summary>
    Unknown
}
