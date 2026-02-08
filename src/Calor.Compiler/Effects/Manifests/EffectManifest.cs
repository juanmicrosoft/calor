namespace Calor.Compiler.Effects.Manifests;

/// <summary>
/// Represents an effect manifest file that declares effects for .NET types.
/// This is the deserialized form of a .calor-effects.json file.
/// </summary>
public sealed class EffectManifest
{
    /// <summary>
    /// Schema version for forward compatibility.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Optional description of this manifest's purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Type-specific effect mappings.
    /// </summary>
    public List<TypeMapping> Mappings { get; set; } = new();

    /// <summary>
    /// Default effects for namespaces when no specific mapping is found.
    /// Key is namespace pattern (supports wildcards), value is list of effect codes.
    /// </summary>
    public Dictionary<string, List<string>> NamespaceDefaults { get; set; } = new();
}

/// <summary>
/// Effect mapping for a specific .NET type.
/// </summary>
public sealed class TypeMapping
{
    /// <summary>
    /// Fully-qualified type name (e.g., "System.IO.File").
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// Default effects for all methods on this type if not explicitly specified.
    /// Null means unknown/not declared.
    /// </summary>
    public List<string>? DefaultEffects { get; set; }

    /// <summary>
    /// Method-specific effect overrides.
    /// Key is method name (or "*" for wildcard, or "MethodName(ParamType)" for specific overloads).
    /// Value is list of effect codes.
    /// </summary>
    public Dictionary<string, List<string>>? Methods { get; set; }

    /// <summary>
    /// Property getter effects.
    /// Key is property name, value is list of effect codes.
    /// </summary>
    public Dictionary<string, List<string>>? Getters { get; set; }

    /// <summary>
    /// Property setter effects.
    /// Key is property name, value is list of effect codes.
    /// </summary>
    public Dictionary<string, List<string>>? Setters { get; set; }

    /// <summary>
    /// Constructor effects.
    /// Key is parameter signature (e.g., "()" for parameterless, "(String)" for single string param).
    /// Value is list of effect codes.
    /// </summary>
    public Dictionary<string, List<string>>? Constructors { get; set; }
}

/// <summary>
/// Information about where a manifest was loaded from.
/// </summary>
public sealed class ManifestSource
{
    /// <summary>
    /// The file path where the manifest was loaded from.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// The priority level of this source (higher = wins on conflict).
    /// </summary>
    public ManifestPriority Priority { get; }

    public ManifestSource(string filePath, ManifestPriority priority)
    {
        FilePath = filePath;
        Priority = priority;
    }

    public override string ToString() => $"{Priority}: {FilePath}";
}

/// <summary>
/// Priority levels for manifest sources.
/// Higher values override lower values.
/// </summary>
public enum ManifestPriority
{
    /// <summary>
    /// Built-in embedded manifests (lowest priority).
    /// </summary>
    BuiltIn = 0,

    /// <summary>
    /// User-level manifests from ~/.calor/manifests/
    /// </summary>
    UserLevel = 100,

    /// <summary>
    /// Solution-level manifests from {solution}/.calor-effects/
    /// </summary>
    SolutionLevel = 200,

    /// <summary>
    /// Project-local manifest from .calor-effects.json
    /// </summary>
    ProjectLocal = 300
}
