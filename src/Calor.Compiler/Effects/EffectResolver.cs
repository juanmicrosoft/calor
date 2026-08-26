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

/// <summary>
/// v0.15 E1 slice 2c (roadmap §4.2 E1 exit pin (c)) — the member kind an
/// <see cref="EffectResolverKey"/> names. This is the discriminator that used
/// to be a string prefix on the resolver's cache key (<c>"m:"</c>/<c>"g:"</c>/
/// <c>"s:"</c>/<c>"c:"</c>), and it is what keeps <c>Resolve(T, "set_X")</c>
/// and the setter of <c>X</c> from colliding.
/// </summary>
public enum EffectMemberKind
{
    /// <summary>An ordinary method call on the declaring type.</summary>
    Method,

    /// <summary>
    /// An extension-method call. <see cref="EffectResolverKey.DeclaringType"/>
    /// is the RECEIVER's type, not the provider's; the resolver searches the
    /// manifest-declared extension providers.
    /// </summary>
    Extension,

    /// <summary>A property getter. <see cref="EffectResolverKey.MemberName"/> is the property name.</summary>
    Getter,

    /// <summary>A property setter. <see cref="EffectResolverKey.MemberName"/> is the property name.</summary>
    Setter,

    /// <summary>A constructor. <see cref="EffectResolverKey.MemberName"/> is <c>".ctor"</c>.</summary>
    Constructor,
}

/// <summary>
/// v0.15 E1 slice 2c — the identity of an external member, as the effect
/// resolver keys on it. This replaces the
/// <c>Resolve(string type, string method, params string[] parameterTypes)</c>
/// family (roadmap §4.2 E1 exit pin (c): "no
/// <c>EffectResolver.Resolve(string, string, …)</c> overload remains").
///
/// <para><b>What is in the identity, and what is only provenance.</b> Equality
/// and hashing cover exactly <see cref="Kind"/>, <see cref="DeclaringType"/>,
/// <see cref="MemberName"/> and <see cref="ParameterTypes"/> — the four things
/// a manifest entry can name. <see cref="IsStatic"/>,
/// <see cref="ReceiverInterfaces"/> and <see cref="FromStringFallback"/> are
/// deliberately OUTSIDE equality: manifests record none of them, so letting
/// them split the cache would make two spellings of one member resolve twice
/// and, worse, let a bound-receiver key and a string-fallback key for the same
/// member disagree. They are provenance and side information, read where the
/// resolver needs them (extension-receiver compatibility) and reported by the
/// key ledger.</para>
///
/// <para><b><see cref="ParameterTypes"/> is nullable, and the two states are
/// not the same.</b> <c>null</c> means "no parameter list was named" — the
/// manifest's name-only entry (<c>"ReadAllText"</c>) and the wildcard
/// (<c>"*"</c>). An EMPTY list means "explicitly zero parameters"
/// (<c>"ReadLine()"</c>). The pre-slice string path drew exactly the same
/// distinction between its <c>"Name(…)"</c> and <c>"Name"</c> dictionary keys,
/// and the six-step resolution order depends on it: step 2a probes the
/// signature form, step 2b the name form.</para>
///
/// <para><b>Two factories, and only two.</b> <see cref="FromBoundReceiver"/>
/// builds a key from a bound receiver's
/// <see cref="Binding.BoundTypes.BoundType"/> — the symbol-identity path E1
/// exists to create. <see cref="FromStrings"/> is the SINGLE entry point for
/// every caller that has only text (the enforcement pass's surviving AST
/// fallbacks, <c>calor effects suggest</c>, the migration converter, the IL
/// propagator), and it stamps <see cref="FromStringFallback"/> so the split is
/// countable rather than asserted:
/// <c>bench/phase0-agent-native/effect-resolver-key-ledger.json</c> freezes it
/// per subject.</para>
/// </summary>
public sealed class EffectResolverKey : IEquatable<EffectResolverKey>
{
    /// <summary>
    /// The fully-qualified declaring type, normalized the way the manifests
    /// spell it. For a generic instantiation this is the generic DEFINITION
    /// plus arity (<c>Microsoft.Extensions.Logging.ILogger`1</c>), never the
    /// instantiated display form (<c>ILogger&lt;Foo&gt;</c>) — no committed
    /// manifest names a type with angle brackets.
    /// </summary>
    public string DeclaringType { get; }

    /// <summary>
    /// The member name: a method name, a property name for
    /// <see cref="EffectMemberKind.Getter"/>/<see cref="EffectMemberKind.Setter"/>,
    /// <c>".ctor"</c> for <see cref="EffectMemberKind.Constructor"/>, or the
    /// wildcard <c>"*"</c> for a manifest catch-all entry.
    /// </summary>
    public string MemberName { get; }

    /// <summary>
    /// Normalized parameter types, or null when no parameter list was named.
    /// See the class remarks: null and empty are different lookups.
    /// </summary>
    public IReadOnlyList<string>? ParameterTypes { get; }

    /// <summary>Which member family this key names.</summary>
    public EffectMemberKind Kind { get; }

    /// <summary>
    /// True when the call site is static (a type-reference receiver), false for
    /// an instance receiver, null when the caller could not say. Provenance
    /// only — outside equality, because no manifest entry records it.
    /// </summary>
    public bool? IsStatic { get; }

    /// <summary>
    /// Interfaces the BINDER knows the receiver implements, fully qualified.
    /// Empty when the binder has nothing to say — which is the common case
    /// today, because <c>TypeSymbol</c> carries no interface list (that is E2
    /// work). The resolver reads this to decide extension-method receiver
    /// compatibility, and falls back to its documented name-shape list when it
    /// is empty. Outside equality.
    /// </summary>
    public IReadOnlyList<string> ReceiverInterfaces { get; }

    /// <summary>
    /// True when this key was built from text rather than from a bound
    /// receiver — i.e. through <see cref="FromStrings"/>. Outside equality;
    /// counted by <c>EffectResolver.KeyOrigins</c> and frozen per subject by
    /// the key ledger.
    /// </summary>
    public bool FromStringFallback { get; }

    /// <summary>True for an extension-method key.</summary>
    public bool IsExtension => Kind == EffectMemberKind.Extension;

    /// <summary>
    /// Whether every named parameter type is usable for overload matching.
    /// The pre-slice path gated its signature probe on exactly this
    /// (<c>parameterTypes.All(IsKnownParameterType)</c>); step 2a still does.
    /// </summary>
    public bool HasKnownParameterTypes =>
        ParameterTypes is { } parameters
        && parameters.All(p => !string.IsNullOrWhiteSpace(p) && p != "?");

    private EffectResolverKey(
        EffectMemberKind kind,
        string declaringType,
        string memberName,
        IReadOnlyList<string>? parameterTypes,
        bool? isStatic,
        IReadOnlyList<string> receiverInterfaces,
        bool fromStringFallback)
    {
        Kind = kind;
        DeclaringType = declaringType;
        MemberName = memberName;
        ParameterTypes = parameterTypes;
        IsStatic = isStatic;
        ReceiverInterfaces = receiverInterfaces;
        FromStringFallback = fromStringFallback;
    }

    /// <summary>
    /// THE string-fallback factory. Every caller that holds only text goes
    /// through here, and every key it produces carries
    /// <see cref="FromStringFallback"/> = true.
    ///
    /// <para><paramref name="parameterTypes"/> follows the pre-slice
    /// <c>params string[]</c> semantics: omitting it yields an EMPTY list
    /// ("explicitly zero parameters"), which is what
    /// <c>Resolve(type, method)</c> meant. Call
    /// <see cref="WithoutParameterList"/> afterwards for a name-only key.</para>
    /// </summary>
    public static EffectResolverKey FromStrings(
        string declaringType,
        string memberName,
        IReadOnlyList<string>? parameterTypes = null,
        EffectMemberKind kind = EffectMemberKind.Method,
        bool? isStatic = null) =>
        new(
            kind,
            NormalizeDeclaringType(declaringType),
            memberName ?? string.Empty,
            NormalizeParameters(parameterTypes ?? Array.Empty<string>()),
            isStatic,
            Array.Empty<string>(),
            fromStringFallback: true);

    /// <summary>
    /// The symbol-identity factory: a key built from the BOUND receiver's type.
    /// <see cref="DeclaringType"/> comes from the bound type (generic definition
    /// + arity for a
    /// <see cref="Binding.BoundTypes.GenericInstantiationBoundType"/>), and
    /// <see cref="ReceiverInterfaces"/> from what the bound type structurally
    /// implies. <see cref="FromStringFallback"/> is false.
    /// </summary>
    public static EffectResolverKey FromBoundReceiver(
        Binding.BoundTypes.BoundType receiverType,
        string memberName,
        IReadOnlyList<string>? parameterTypes = null,
        EffectMemberKind kind = EffectMemberKind.Method,
        bool? isStatic = null)
    {
        ArgumentNullException.ThrowIfNull(receiverType);
        return new EffectResolverKey(
            kind,
            NormalizeDeclaringType(DeclaringTypeOf(receiverType)),
            memberName ?? string.Empty,
            NormalizeParameters(parameterTypes ?? Array.Empty<string>()),
            isStatic,
            InterfacesOf(receiverType),
            fromStringFallback: false);
    }

    /// <summary>
    /// The manifest-side factory: one key per parsed manifest entry, built once
    /// at load. <paramref name="parameterTypes"/> is null for a name-only or
    /// wildcard entry.
    /// </summary>
    internal static EffectResolverKey ForManifestEntry(
        EffectMemberKind kind,
        string declaringType,
        string memberName,
        IReadOnlyList<string>? parameterTypes) =>
        new(
            kind,
            NormalizeDeclaringType(declaringType),
            memberName ?? string.Empty,
            parameterTypes == null ? null : NormalizeParameters(parameterTypes),
            isStatic: null,
            Array.Empty<string>(),
            fromStringFallback: false);

    /// <summary>This key with its parameter list dropped — the step-2b name probe.</summary>
    public EffectResolverKey WithoutParameterList() =>
        ParameterTypes == null
            ? this
            : new EffectResolverKey(
                Kind, DeclaringType, MemberName, null, IsStatic, ReceiverInterfaces, FromStringFallback);

    /// <summary>This key with a different member name, keeping type and provenance.</summary>
    public EffectResolverKey WithMemberName(string memberName) =>
        new(Kind, DeclaringType, memberName, ParameterTypes, IsStatic, ReceiverInterfaces, FromStringFallback);

    /// <summary>This key re-pointed at a different declaring type, keeping provenance.</summary>
    public EffectResolverKey WithDeclaringType(string declaringType) =>
        new(
            Kind,
            NormalizeDeclaringType(declaringType),
            MemberName,
            ParameterTypes,
            IsStatic,
            ReceiverInterfaces,
            FromStringFallback);

    /// <summary>This key as a different member kind, keeping everything else.</summary>
    public EffectResolverKey WithKind(EffectMemberKind kind) =>
        new(kind, DeclaringType, MemberName, ParameterTypes, IsStatic, ReceiverInterfaces, FromStringFallback);

    /// <summary>This key with an explicit parameter list.</summary>
    public EffectResolverKey WithParameterTypes(IReadOnlyList<string> parameterTypes) =>
        new(
            Kind,
            DeclaringType,
            MemberName,
            NormalizeParameters(parameterTypes),
            IsStatic,
            ReceiverInterfaces,
            FromStringFallback);

    private static string NormalizeDeclaringType(string declaringType) =>
        string.IsNullOrWhiteSpace(declaringType)
            ? string.Empty
            : declaringType.Trim().Replace("global::", "", StringComparison.Ordinal);

    private static IReadOnlyList<string> NormalizeParameters(IReadOnlyList<string> parameterTypes) =>
        parameterTypes.Count == 0
            ? Array.Empty<string>()
            : parameterTypes.Select(EffectResolver.NormalizeParameterType).ToArray();

    /// <summary>
    /// The declaring-type name a bound receiver contributes.
    ///
    /// <para>A <see cref="Binding.BoundTypes.GenericInstantiationBoundType"/>
    /// answers with its generic DEFINITION plus arity, because that is how
    /// every committed manifest names a generic type
    /// (<c>System.Collections.Generic.List`1</c>,
    /// <c>FluentValidation.AbstractValidator`1</c>); the instantiated display
    /// form matches no manifest entry at all. Every other shape answers with
    /// its <c>DisplayString</c>, which is byte-for-byte the string the
    /// pre-slice AST path handed the resolver — so nothing but the generic case
    /// can move.</para>
    /// </summary>
    private static string DeclaringTypeOf(Binding.BoundTypes.BoundType type) =>
        type switch
        {
            Binding.BoundTypes.GenericInstantiationBoundType generic =>
                WithArity(generic.Definition.QualifiedName, generic.TypeArguments.Length),
            _ => type.DisplayString,
        };

    private static string WithArity(string qualifiedName, int arity) =>
        string.IsNullOrEmpty(qualifiedName) || qualifiedName.Contains('`') || arity == 0
            ? qualifiedName
            : $"{qualifiedName}`{arity}";

    /// <summary>
    /// Interfaces the receiver's bound type structurally implies. Deliberately
    /// small and deliberately honest: the binder's <c>TypeSymbol</c> carries no
    /// interface list today, so the only things derivable here are the ones the
    /// LANGUAGE guarantees — an array implements <c>IEnumerable`1</c> of its
    /// element type — plus the generic collection definitions the manifests
    /// already name. Everything else returns empty and the resolver falls back
    /// to its documented name-shape test. Widening this is E2 work (interface
    /// sets on bound types).
    /// </summary>
    private static IReadOnlyList<string> InterfacesOf(Binding.BoundTypes.BoundType type) =>
        type switch
        {
            Binding.BoundTypes.ArrayBoundType => EnumerableShape,
            Binding.BoundTypes.GenericInstantiationBoundType generic
                when IsEnumerableDefinition(generic.Definition.QualifiedName) => EnumerableShape,
            _ => Array.Empty<string>(),
        };

    private static readonly string[] EnumerableShape =
    [
        "System.Collections.Generic.IEnumerable`1",
        "System.Collections.IEnumerable",
    ];

    private static bool IsEnumerableDefinition(string qualifiedName)
    {
        var name = qualifiedName;
        var lastDot = name.LastIndexOf('.');
        if (lastDot >= 0)
            name = name[(lastDot + 1)..];
        var tick = name.IndexOf('`');
        if (tick > 0)
            name = name[..tick];
        return name is "IEnumerable" or "ICollection" or "IList" or "IReadOnlyList"
            or "IReadOnlyCollection" or "List" or "HashSet" or "Dictionary" or "Queue" or "Stack";
    }

    public bool Equals(EffectResolverKey? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Kind != other.Kind) return false;
        if (!string.Equals(DeclaringType, other.DeclaringType, StringComparison.Ordinal)) return false;
        if (!string.Equals(MemberName, other.MemberName, StringComparison.Ordinal)) return false;
        if (ParameterTypes is null) return other.ParameterTypes is null;
        if (other.ParameterTypes is null) return false;
        return ParameterTypes.SequenceEqual(other.ParameterTypes, StringComparer.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as EffectResolverKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add((int)Kind);
        hash.Add(DeclaringType, StringComparer.Ordinal);
        hash.Add(MemberName, StringComparer.Ordinal);
        if (ParameterTypes is null)
        {
            hash.Add(-1);
        }
        else
        {
            hash.Add(ParameterTypes.Count);
            foreach (var parameter in ParameterTypes)
                hash.Add(parameter, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }

    /// <summary>A human-readable rendering, used in resolver diagnostics and test failure text.</summary>
    public override string ToString()
    {
        var parameters = ParameterTypes is null ? "" : $"({string.Join(",", ParameterTypes)})";
        var prefix = Kind switch
        {
            EffectMemberKind.Method => "m",
            EffectMemberKind.Extension => "x",
            EffectMemberKind.Getter => "g",
            EffectMemberKind.Setter => "s",
            EffectMemberKind.Constructor => "c",
            _ => "?",
        };
        return $"{prefix}:{DeclaringType}::{MemberName}{parameters}";
    }
}
