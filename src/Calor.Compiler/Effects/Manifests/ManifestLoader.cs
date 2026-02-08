using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Calor.Compiler.Effects.Manifests;

/// <summary>
/// Loads effect manifests from multiple sources with proper layering.
/// Sources are loaded in priority order: built-in, user-level, solution-level, project-local.
/// Later sources override earlier ones.
/// </summary>
public sealed class ManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly List<string> _loadErrors = new();
    private readonly List<(EffectManifest Manifest, ManifestSource Source)> _loadedManifests = new();

    /// <summary>
    /// Errors encountered during loading.
    /// </summary>
    public IReadOnlyList<string> LoadErrors => _loadErrors;

    /// <summary>
    /// All manifests that were successfully loaded.
    /// </summary>
    public IReadOnlyList<(EffectManifest Manifest, ManifestSource Source)> LoadedManifests => _loadedManifests;

    /// <summary>
    /// Loads all manifests from standard locations.
    /// </summary>
    /// <param name="projectDirectory">Optional project directory for project-local manifests</param>
    /// <param name="solutionDirectory">Optional solution directory for solution-level manifests</param>
    public void LoadAll(string? projectDirectory = null, string? solutionDirectory = null)
    {
        // 1. Load built-in embedded manifests (lowest priority)
        LoadBuiltInManifests();

        // 2. Load user-level manifests from ~/.calor/manifests/
        LoadUserLevelManifests();

        // 3. Load solution-level manifests if provided
        if (!string.IsNullOrEmpty(solutionDirectory))
        {
            LoadSolutionLevelManifests(solutionDirectory);
        }

        // 4. Load project-local manifest (highest priority)
        if (!string.IsNullOrEmpty(projectDirectory))
        {
            LoadProjectLocalManifest(projectDirectory);
        }
    }

    /// <summary>
    /// Loads built-in manifests embedded in the assembly.
    /// </summary>
    private void LoadBuiltInManifests()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".calor-effects.json", StringComparison.OrdinalIgnoreCase));

        foreach (var resourceName in resourceNames)
        {
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    _loadErrors.Add($"Failed to load embedded resource: {resourceName}");
                    continue;
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                var manifest = JsonSerializer.Deserialize<EffectManifest>(json, JsonOptions);

                if (manifest != null)
                {
                    var source = new ManifestSource($"embedded:{resourceName}", ManifestPriority.BuiltIn);
                    _loadedManifests.Add((manifest, source));
                }
            }
            catch (Exception ex)
            {
                _loadErrors.Add($"Failed to parse embedded manifest {resourceName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Loads user-level manifests from ~/.calor/manifests/
    /// </summary>
    private void LoadUserLevelManifests()
    {
        var userDir = GetUserManifestsDirectory();
        if (string.IsNullOrEmpty(userDir) || !Directory.Exists(userDir))
            return;

        LoadManifestsFromDirectory(userDir, ManifestPriority.UserLevel);
    }

    /// <summary>
    /// Loads solution-level manifests from {solution}/.calor-effects/
    /// </summary>
    private void LoadSolutionLevelManifests(string solutionDirectory)
    {
        var manifestDir = Path.Combine(solutionDirectory, ".calor-effects");
        if (!Directory.Exists(manifestDir))
            return;

        LoadManifestsFromDirectory(manifestDir, ManifestPriority.SolutionLevel);
    }

    /// <summary>
    /// Loads project-local manifest from {project}/.calor-effects.json
    /// </summary>
    private void LoadProjectLocalManifest(string projectDirectory)
    {
        var manifestPath = Path.Combine(projectDirectory, ".calor-effects.json");
        if (!File.Exists(manifestPath))
            return;

        LoadManifestFromFile(manifestPath, ManifestPriority.ProjectLocal);
    }

    /// <summary>
    /// Loads all .calor-effects.json files from a directory.
    /// </summary>
    private void LoadManifestsFromDirectory(string directory, ManifestPriority priority)
    {
        try
        {
            var files = Directory.GetFiles(directory, "*.calor-effects.json", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                LoadManifestFromFile(file, priority);
            }
        }
        catch (Exception ex)
        {
            _loadErrors.Add($"Failed to enumerate manifests in {directory}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a single manifest file.
    /// </summary>
    private void LoadManifestFromFile(string filePath, ManifestPriority priority)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var manifest = JsonSerializer.Deserialize<EffectManifest>(json, JsonOptions);

            if (manifest != null)
            {
                var source = new ManifestSource(filePath, priority);
                _loadedManifests.Add((manifest, source));
            }
            else
            {
                _loadErrors.Add($"Failed to parse manifest {filePath}: result was null");
            }
        }
        catch (JsonException ex)
        {
            _loadErrors.Add($"Invalid JSON in manifest {filePath}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _loadErrors.Add($"Failed to load manifest {filePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a manifest from a JSON string (for testing).
    /// </summary>
    public EffectManifest? LoadFromJson(string json, string sourceName = "inline")
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<EffectManifest>(json, JsonOptions);
            if (manifest != null)
            {
                var source = new ManifestSource(sourceName, ManifestPriority.ProjectLocal);
                _loadedManifests.Add((manifest, source));
            }
            return manifest;
        }
        catch (Exception ex)
        {
            _loadErrors.Add($"Failed to parse manifest from JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validates all loaded manifests and returns any validation errors.
    /// </summary>
    public List<string> ValidateManifests()
    {
        var errors = new List<string>();

        foreach (var (manifest, source) in _loadedManifests)
        {
            ValidateManifest(manifest, source, errors);
        }

        return errors;
    }

    private void ValidateManifest(EffectManifest manifest, ManifestSource source, List<string> errors)
    {
        // Validate version
        if (string.IsNullOrEmpty(manifest.Version))
        {
            errors.Add($"{source.FilePath}: Missing 'version' field");
        }
        else if (manifest.Version != "1.0")
        {
            errors.Add($"{source.FilePath}: Unsupported version '{manifest.Version}' (expected '1.0')");
        }

        // Validate mappings
        foreach (var mapping in manifest.Mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Type))
            {
                errors.Add($"{source.FilePath}: Mapping has empty 'type' field");
                continue;
            }

            // Validate effect codes
            ValidateEffectCodes(mapping.DefaultEffects, $"{source.FilePath}: {mapping.Type}.defaultEffects", errors);
            ValidateMethodEffects(mapping.Methods, $"{source.FilePath}: {mapping.Type}", errors);
            ValidateMethodEffects(mapping.Getters, $"{source.FilePath}: {mapping.Type} getters", errors);
            ValidateMethodEffects(mapping.Setters, $"{source.FilePath}: {mapping.Type} setters", errors);
            ValidateMethodEffects(mapping.Constructors, $"{source.FilePath}: {mapping.Type} constructors", errors);
        }

        // Validate namespace defaults
        foreach (var (ns, effects) in manifest.NamespaceDefaults)
        {
            ValidateEffectCodes(effects, $"{source.FilePath}: namespaceDefaults[{ns}]", errors);
        }
    }

    private void ValidateEffectCodes(List<string>? codes, string context, List<string> errors)
    {
        if (codes == null) return;

        foreach (var code in codes)
        {
            if (!IsValidEffectCode(code))
            {
                errors.Add($"{context}: Unknown effect code '{code}'");
            }
        }
    }

    private void ValidateMethodEffects(Dictionary<string, List<string>>? methods, string context, List<string> errors)
    {
        if (methods == null) return;

        foreach (var (methodName, effects) in methods)
        {
            ValidateEffectCodes(effects, $"{context}.{methodName}", errors);
        }
    }

    private static bool IsValidEffectCode(string code)
    {
        // Known effect codes
        var knownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Console
            "cw", "cr",
            // Filesystem
            "fs:r", "fs:w", "fs:rw",
            // Network
            "net:r", "net:w", "net:rw",
            // Database
            "db:r", "db:w", "db:rw",
            // Environment
            "env:r", "env:w",
            // System
            "proc", "alloc", "unsafe",
            // Nondeterminism
            "time", "rand",
            // Mutation/Exception
            "mut", "throw"
        };

        return knownCodes.Contains(code);
    }

    /// <summary>
    /// Gets the user-level manifests directory (~/.calor/manifests/).
    /// </summary>
    private static string? GetUserManifestsDirectory()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
                return null;

            return Path.Combine(home, ".calor", "manifests");
        }
        catch
        {
            return null;
        }
    }
}
