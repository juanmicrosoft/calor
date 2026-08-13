using Calor.Compiler.Effects.Manifests;

namespace Calor.Compiler.Effects;

/// <summary>
/// Resolves effects for .NET method calls using a layered approach.
/// Resolution order:
/// 1. Specific method signature in type mapping (from manifests)
/// 2. Method entry on the resolved declaring type
/// 3. Wildcard "*" in type mapping
/// 4. DefaultEffects on type
/// 5. NamespaceDefaults matching namespace pattern
/// 6. Unknown
/// </summary>
public sealed class EffectResolver
{
    private readonly ManifestLoader _manifestLoader;
    private readonly IL.ILEffectAnalyzer? _ilAnalyzer;
    private readonly Dictionary<string, ResolvedTypeInfo> _typeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EffectResolution> _methodCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _extensionProviders = new(StringComparer.Ordinal);
    private bool _initialized;

    public EffectResolver(ManifestLoader? manifestLoader = null, IL.ILEffectAnalyzer? ilAnalyzer = null)
    {
        _manifestLoader = manifestLoader ?? new ManifestLoader();
        _ilAnalyzer = ilAnalyzer;
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
        parameterTypes = parameterTypes.Select(NormalizeParameterType).ToArray();

        // Build cache key. The "m:" prefix keeps this entry point's cache
        // disjoint from ResolveSetter/ResolveGetter/ResolveConstructor —
        // without it, Resolve(T, "set_X") and ResolveSetter(T, "X") build the
        // IDENTICAL key, and whichever runs first poisons the other's result
        // (observed: the IL propagator probed Resolve(...) first and cached
        // Unknown, making manifest-covered setters look like assumptions).
        var signature = "m:" + BuildSignature(fullyQualifiedType, methodName, parameterTypes);
        if (_methodCache.TryGetValue(signature, out var cached))
            return cached;

        var resolution = ResolveInternal(fullyQualifiedType, methodName, parameterTypes, signature);
        _methodCache[signature] = resolution;
        return resolution;
    }

    /// <summary>
    /// Resolves an extension-method call from manifest-declared extension providers.
    /// The receiver type is included as the first signature parameter.
    /// </summary>
    public EffectResolution ResolveExtension(
        string receiverType,
        string methodName,
        params string[] parameterTypes)
    {
        EnsureInitialized();

        var allParameterTypes = new[] { receiverType }.Concat(parameterTypes).ToArray();
        foreach (var provider in _extensionProviders.OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!_typeCache.TryGetValue(provider, out var typeInfo))
                continue;

            var normalizedParameters = allParameterTypes
                .Select(NormalizeParameterType)
                .ToArray();
            if (normalizedParameters.All(IsKnownParameterType))
            {
                var signature = $"{methodName}({string.Join(",", normalizedParameters)})";
                if (typeInfo.Methods.TryGetValue(signature, out var signatureEffects))
                    return CreateResolution(signatureEffects, typeInfo.Source);
            }

            if (IsCompatibleExtensionReceiver(provider, receiverType)
                && typeInfo.Methods.TryGetValue(methodName, out var methodEffects))
                return CreateResolution(methodEffects, typeInfo.Source);
        }

        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private static bool IsCompatibleExtensionReceiver(string provider, string receiverType)
    {
        if (!provider.Equals("System.Linq.Enumerable", StringComparison.Ordinal))
            return false;

        var normalized = receiverType.Replace("global::", "", StringComparison.Ordinal);
        return normalized.EndsWith("[]", StringComparison.Ordinal)
            || normalized.StartsWith("System.Collections.", StringComparison.Ordinal)
            || normalized.StartsWith("IEnumerable<", StringComparison.Ordinal)
            || normalized.StartsWith("ICollection<", StringComparison.Ordinal)
            || normalized.StartsWith("IList<", StringComparison.Ordinal)
            || normalized.StartsWith("List<", StringComparison.Ordinal)
            || normalized.StartsWith("HashSet<", StringComparison.Ordinal)
            || normalized.StartsWith("Dictionary<", StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves effects for a property getter.
    /// </summary>
    public EffectResolution ResolveGetter(string fullyQualifiedType, string propertyName)
    {
        EnsureInitialized();

        var signature = $"g:{fullyQualifiedType}::get_{propertyName}()";
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

        var signature = $"s:{fullyQualifiedType}::set_{propertyName}()";
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
        parameterTypes = parameterTypes.Select(NormalizeParameterType).ToArray();

        var paramSig = $"({string.Join(",", parameterTypes)})";
        var signature = $"c:{fullyQualifiedType}::.ctor{paramSig}";

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
        // 1. Check type cache from manifests
        if (_typeCache.TryGetValue(type, out var typeInfo))
        {
            // 2a. Try specific method with parameters
            if (parameterTypes.All(IsKnownParameterType))
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

        // 4. IL analysis fallback (after all manifest layers, before Unknown)
        var ilResolution = TryILAnalysis(type, method, parameterTypes);
        if (ilResolution != null)
            return ilResolution;

        // 5. Unknown
        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution ResolveGetterInternal(string type, string propertyName, string signature)
    {
        // Check type cache from manifests
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

        // IL analysis fallback
        var ilResolution = TryILAnalysis(type, $"get_{propertyName}");
        if (ilResolution != null)
            return ilResolution;

        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution ResolveSetterInternal(string type, string propertyName, string signature)
    {
        // Check type cache from manifests
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

        // IL analysis fallback
        var ilResolution = TryILAnalysis(type, $"set_{propertyName}");
        if (ilResolution != null)
            return ilResolution;

        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution ResolveConstructorInternal(string type, string[] parameterTypes, string signature)
    {
        // Check type cache from manifests
        if (_typeCache.TryGetValue(type, out var typeInfo))
        {
            var paramSig = $"({string.Join(",", parameterTypes)})";
            if (typeInfo.Constructors.TryGetValue(paramSig, out var ctorEffects))
            {
                return CreateResolution(ctorEffects, typeInfo.Source);
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

        // IL analysis fallback
        var ilResolution = TryILAnalysis(type, ".ctor", parameterTypes);
        if (ilResolution != null)
            return ilResolution;

        return new EffectResolution(EffectResolutionStatus.Unknown, EffectSet.Unknown, "unknown");
    }

    private EffectResolution? TryILAnalysis(string type, string method, string[]? parameterTypes = null)
    {
        return _ilAnalyzer?.TryResolve(type, method, parameterTypes);
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
                if (mapping.ExtensionProvider)
                    _extensionProviders.Add(mapping.Type);

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
                        typeInfo.Methods[NormalizeMethodManifestKey(method)] = effects;
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
                        typeInfo.Constructors[NormalizeConstructorManifestKey(sig)] = effects;
                    }
                }

                // This will overwrite if already exists (higher priority wins)
                _typeCache[mapping.Type] = typeInfo;
            }
        }
    }

    private static string NormalizeMethodManifestKey(string method)
    {
        var open = method.IndexOf('(');
        if (open <= 0 || !method.EndsWith(')'))
            return method;

        var name = method[..open];
        var parameters = ParseParameterSignature(method[open..])
            .Select(NormalizeParameterType);
        return $"{name}({string.Join(",", parameters)})";
    }

    private static string NormalizeConstructorManifestKey(string signature)
    {
        if (!signature.StartsWith('(') || !signature.EndsWith(')'))
            return signature;

        var parameters = ParseParameterSignature(signature)
            .Select(NormalizeParameterType);
        return $"({string.Join(",", parameters)})";
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
        var parameters = string.Join(",", parameterTypes.Select(NormalizeParameterType));
        return $"{type}::{method}({parameters})";
    }

    /// <summary>
    /// Normalizes Calor and CLR type spellings to the short signature form used by
    /// effect manifests.
    /// </summary>
    public static string NormalizeParameterType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || typeName == "?")
            return "?";

        var type = typeName.Trim().Replace("global::", "", StringComparison.Ordinal);
        var nullable = type.EndsWith("?", StringComparison.Ordinal);
        if (nullable)
            type = type[..^1];

        if (type.EndsWith("[]", StringComparison.Ordinal))
            return $"{NormalizeParameterType(type[..^2])}[]";

        type = type switch
        {
            "str" or "string" or "STRING" or "System.String" => "String",
            "bool" or "BOOL" or "System.Boolean" => "Boolean",
            "i8" or "sbyte" or "System.SByte" => "SByte",
            "u8" or "byte" or "System.Byte" => "Byte",
            "i16" or "short" or "System.Int16" => "Int16",
            "u16" or "ushort" or "System.UInt16" => "UInt16",
            "i32" or "int" or "INT" or "System.Int32" => "Int32",
            "u32" or "uint" or "System.UInt32" => "UInt32",
            "i64" or "long" or "System.Int64" => "Int64",
            "u64" or "ulong" or "System.UInt64" => "UInt64",
            "f32" or "float" or "System.Single" => "Single",
            "f64" or "float" or "double" or "FLOAT" or "System.Double" => "Double",
            "dec" or "decimal" or "DECIMAL" or "System.Decimal" => "Decimal",
            "char" or "CHAR" or "System.Char" => "Char",
            _ => type
        };

        var genericStart = type.IndexOf('<');
        if (genericStart > 0)
        {
            var genericEnd = type.LastIndexOf('>');
            if (genericEnd > genericStart)
            {
                var genericArguments = ParseParameterSignature(
                        type[(genericStart + 1)..genericEnd])
                    .Select(NormalizeParameterType)
                    .ToArray();
                var genericType = type[..genericStart];
                if (genericType.StartsWith("System.Collections.Generic.", StringComparison.Ordinal))
                    genericType = genericType["System.Collections.Generic.".Length..];
                if (!genericType.Contains('`'))
                    genericType = $"{genericType}`{genericArguments.Length}";
                return $"{genericType}<{string.Join(",", genericArguments)}>";
            }
        }

        var lastDot = type.LastIndexOf('.');
        type = lastDot >= 0 ? type[(lastDot + 1)..] : type;
        return type;
    }

    /// <summary>
    /// Parses a serialized parameter signature such as
    /// <c>(System.String,System.Collections.Generic.List`1&lt;System.Int32&gt;)</c>.
    /// </summary>
    public static string[] ParseParameterSignature(string parameterSignature)
    {
        if (string.IsNullOrWhiteSpace(parameterSignature)
            || parameterSignature == "*"
            || parameterSignature == "()")
        {
            return [];
        }

        var content = parameterSignature.Trim();
        if (content.StartsWith('(') && content.EndsWith(')'))
            content = content[1..^1];

        var result = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i < content.Length; i++)
        {
            depth += content[i] switch
            {
                '<' or '{' or '[' => 1,
                '>' or '}' or ']' => -1,
                _ => 0
            };
            if (content[i] == ',' && depth == 0)
            {
                result.Add(content[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < content.Length)
            result.Add(content[start..].Trim());
        return result.ToArray();
    }

    private static bool IsKnownParameterType(string typeName)
        => !string.IsNullOrWhiteSpace(typeName) && typeName != "?";

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
