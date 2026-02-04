namespace Calor.Compiler.Init;

/// <summary>
/// Initializes .gitattributes files with GitHub linguist configuration for Calor files.
/// </summary>
public static class GitAttributesInitializer
{
    private const string CalorSection = """
        # Calor language support for GitHub linguist
        *.calr linguist-language=Calor
        *.calr linguist-detectable=true
        """;

    /// <summary>
    /// Creates or updates .gitattributes with Calor linguist configuration.
    /// </summary>
    /// <param name="targetDirectory">The directory where .gitattributes should be created/updated.</param>
    /// <returns>A tuple indicating whether the file was created or updated.</returns>
    public static async Task<(bool created, bool updated)> InitializeAsync(string targetDirectory)
    {
        var gitAttributesPath = Path.Combine(targetDirectory, ".gitattributes");

        if (!File.Exists(gitAttributesPath))
        {
            // Create new file
            await File.WriteAllTextAsync(gitAttributesPath, CalorSection + Environment.NewLine);
            return (created: true, updated: false);
        }

        // Check if already contains Calor config
        var content = await File.ReadAllTextAsync(gitAttributesPath);
        if (content.Contains("*.calr linguist-language=Calor"))
        {
            return (created: false, updated: false);
        }

        // Append to existing file
        var newContent = content.TrimEnd() + Environment.NewLine + Environment.NewLine + CalorSection + Environment.NewLine;
        await File.WriteAllTextAsync(gitAttributesPath, newContent);
        return (created: false, updated: true);
    }
}
