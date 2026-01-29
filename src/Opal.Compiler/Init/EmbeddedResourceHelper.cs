using System.Reflection;

namespace Opal.Compiler.Init;

/// <summary>
/// Utility for reading embedded resources from the opalc assembly.
/// </summary>
public static class EmbeddedResourceHelper
{
    private static readonly Assembly Assembly = typeof(EmbeddedResourceHelper).Assembly;

    /// <summary>
    /// Reads an embedded resource as a string.
    /// </summary>
    /// <param name="name">The logical name of the resource (e.g., "Opal.Compiler.Resources.Skills.opal.md")</param>
    /// <returns>The resource content as a string.</returns>
    /// <exception cref="InvalidOperationException">If the resource is not found.</exception>
    public static string ReadResource(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name);
        if (stream == null)
        {
            var available = string.Join(", ", Assembly.GetManifestResourceNames());
            throw new InvalidOperationException(
                $"Embedded resource '{name}' not found. Available resources: {available}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets the informational version of the opalc assembly.
    /// </summary>
    /// <returns>The version string (e.g., "0.1.2").</returns>
    public static string GetVersion()
    {
        var attr = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr != null)
        {
            // Remove any build metadata (e.g., "+abc123") from the version
            var version = attr.InformationalVersion;
            var plusIndex = version.IndexOf('+');
            return plusIndex >= 0 ? version[..plusIndex] : version;
        }

        // Fall back to assembly version
        var assemblyVersion = Assembly.GetName().Version;
        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// Reads a skill file from embedded resources.
    /// </summary>
    /// <param name="filename">The skill filename (e.g., "opal.md")</param>
    /// <returns>The skill content.</returns>
    public static string ReadSkill(string filename)
    {
        return ReadResource($"Opal.Compiler.Resources.Skills.{filename}");
    }

    /// <summary>
    /// Reads a template file from embedded resources.
    /// </summary>
    /// <param name="filename">The template filename (e.g., "CLAUDE.md.template")</param>
    /// <returns>The template content.</returns>
    public static string ReadTemplate(string filename)
    {
        return ReadResource($"Opal.Compiler.Resources.Templates.{filename}");
    }
}
