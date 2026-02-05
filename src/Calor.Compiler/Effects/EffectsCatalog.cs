using System.Text.Json;

namespace Calor.Compiler.Effects;

/// <summary>
/// Combined effects catalog that loads built-in effects and overlays project stubs.
/// </summary>
public sealed class EffectsCatalog
{
    private readonly Dictionary<string, EffectSet> _catalog;
    private readonly List<string> _loadErrors;

    /// <summary>
    /// File name for project-level effect stubs.
    /// </summary>
    public const string StubsFileName = "calor.effects.json";

    /// <summary>
    /// Any errors encountered while loading the catalog.
    /// </summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    private EffectsCatalog(Dictionary<string, EffectSet> catalog, List<string> errors)
    {
        _catalog = catalog;
        _loadErrors = errors;
    }

    /// <summary>
    /// Creates a catalog with built-in effects only.
    /// </summary>
    public static EffectsCatalog CreateDefault()
    {
        var catalog = new Dictionary<string, EffectSet>(BuiltInEffects.Catalog, StringComparer.Ordinal);
        return new EffectsCatalog(catalog, new List<string>());
    }

    /// <summary>
    /// Creates a catalog with built-in effects and project stubs.
    /// </summary>
    /// <param name="projectDirectory">Directory to search for calor.effects.json</param>
    public static EffectsCatalog CreateWithProjectStubs(string? projectDirectory)
    {
        var catalog = new Dictionary<string, EffectSet>(BuiltInEffects.Catalog, StringComparer.Ordinal);
        var errors = new List<string>();

        if (string.IsNullOrEmpty(projectDirectory))
        {
            return new EffectsCatalog(catalog, errors);
        }

        var stubsPath = Path.Combine(projectDirectory, StubsFileName);
        if (!File.Exists(stubsPath))
        {
            return new EffectsCatalog(catalog, errors);
        }

        try
        {
            var json = File.ReadAllText(stubsPath);
            var stubs = ParseStubsFile(json, errors);

            // Overlay project stubs (project wins on conflict)
            foreach (var (signature, effectSet) in stubs)
            {
                catalog[signature] = effectSet;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to load {StubsFileName}: {ex.Message}");
        }

        return new EffectsCatalog(catalog, errors);
    }

    /// <summary>
    /// Creates a catalog from a custom stubs dictionary (for testing).
    /// </summary>
    public static EffectsCatalog CreateWithCustomStubs(Dictionary<string, EffectSet> customStubs)
    {
        var catalog = new Dictionary<string, EffectSet>(BuiltInEffects.Catalog, StringComparer.Ordinal);
        foreach (var (signature, effectSet) in customStubs)
        {
            catalog[signature] = effectSet;
        }
        return new EffectsCatalog(catalog, new List<string>());
    }

    /// <summary>
    /// Looks up effects for a method signature.
    /// Returns null if the signature is not in the catalog.
    /// </summary>
    public EffectSet? TryGetEffects(string signature)
    {
        return _catalog.TryGetValue(signature, out var effects) ? effects : null;
    }

    /// <summary>
    /// Returns true if the signature is in the catalog.
    /// </summary>
    public bool IsKnown(string signature)
    {
        return _catalog.ContainsKey(signature);
    }

    /// <summary>
    /// Finds all matching signatures for a partial match.
    /// Used to detect ambiguous stubs.
    /// </summary>
    public List<string> FindMatchingSignatures(string methodName)
    {
        return _catalog.Keys
            .Where(sig => sig.Contains("::" + methodName + "(") || sig.EndsWith("::" + methodName + "()"))
            .ToList();
    }

    /// <summary>
    /// Returns all signatures in the catalog.
    /// </summary>
    public IEnumerable<string> AllSignatures => _catalog.Keys;

    /// <summary>
    /// Returns the number of entries in the catalog.
    /// </summary>
    public int Count => _catalog.Count;

    private static Dictionary<string, EffectSet> ParseStubsFile(string json, List<string> errors)
    {
        var result = new Dictionary<string, EffectSet>(StringComparer.Ordinal);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("stubs", out var stubsElement))
            {
                errors.Add("Missing 'stubs' property in calor.effects.json");
                return result;
            }

            foreach (var property in stubsElement.EnumerateObject())
            {
                var signature = property.Name;

                // Validate signature format
                if (!IsValidSignature(signature))
                {
                    errors.Add($"Invalid signature format: '{signature}'. Expected format: Namespace.Type::Method(ParamType1,ParamType2)");
                    continue;
                }

                var effectCodes = new List<string>();

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            effectCodes.Add(item.GetString()!);
                        }
                        else
                        {
                            errors.Add($"Invalid effect code in '{signature}': expected string");
                        }
                    }
                }
                else
                {
                    errors.Add($"Invalid value for '{signature}': expected array of effect codes");
                    continue;
                }

                try
                {
                    result[signature] = EffectSet.From(effectCodes.ToArray());
                }
                catch (Exception ex)
                {
                    errors.Add($"Invalid effect codes for '{signature}': {ex.Message}");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON in calor.effects.json: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Validates that a signature follows the expected format.
    /// </summary>
    private static bool IsValidSignature(string signature)
    {
        // Must contain :: for method separator
        var separatorIndex = signature.IndexOf("::", StringComparison.Ordinal);
        if (separatorIndex < 1) return false;

        // Must have type before ::
        var typePart = signature[..separatorIndex];
        if (string.IsNullOrWhiteSpace(typePart)) return false;

        // Must have method name after ::
        var methodPart = signature[(separatorIndex + 2)..];
        if (string.IsNullOrWhiteSpace(methodPart)) return false;

        // Method part should end with )
        if (!methodPart.EndsWith(")")) return false;

        // Method part should contain (
        if (!methodPart.Contains("(")) return false;

        return true;
    }

    /// <summary>
    /// Builds a fully-qualified signature from components.
    /// </summary>
    public static string BuildSignature(string namespaceName, string typeName, string methodName, params string[] parameterTypes)
    {
        var fullType = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
        var parameters = string.Join(",", parameterTypes);
        return $"{fullType}::{methodName}({parameters})";
    }

    /// <summary>
    /// Builds a property getter signature.
    /// </summary>
    public static string BuildGetterSignature(string namespaceName, string typeName, string propertyName)
    {
        return BuildSignature(namespaceName, typeName, $"get_{propertyName}");
    }

    /// <summary>
    /// Builds a property setter signature.
    /// </summary>
    public static string BuildSetterSignature(string namespaceName, string typeName, string propertyName, string valueType)
    {
        return BuildSignature(namespaceName, typeName, $"set_{propertyName}", valueType);
    }

    /// <summary>
    /// Builds a constructor signature.
    /// </summary>
    public static string BuildConstructorSignature(string namespaceName, string typeName, params string[] parameterTypes)
    {
        return BuildSignature(namespaceName, typeName, ".ctor", parameterTypes);
    }
}
